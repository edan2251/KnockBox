using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Knockbox.Game;
using Knockbox.Game.Online;
using Knockbox.Game.UI;
using Knockbox.Presentation;
using Knockbox.Simulation;
using Unity.Collections;
using UnityEditor;
using UnityEditor.Media;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using Object = UnityEngine.Object;

namespace Knockbox.EditorTools
{
    /// <summary>실제 Main Game View와 게임 효과음을 촬영하는 검수 도구. 새 설치/씬 저장 없이 Play 안에서만 실행한다.</summary>
    public static class FrontendReviewRecorder
    {
        static string Folder;
        const int SampleRate = 48000;
        public static string Status { get; private set; } = "idle";
        public static string Output { get; private set; }
        public static double Seconds { get; private set; }
        public static int Frames { get; private set; }
        public static float AudioPeak { get; private set; }
        static bool stopRequested;
        public static void RequestStop() => stopRequested = true;
        sealed class Voice
        {
            public float[] Data;
            public int Channels, Frequency;
            public double Start;
            public float Volume, Pitch;
        }

        [MenuItem("Knockbox/Record/Frontend Review (Main Play)")]
        public static void Start()
        {
            var runner = Object.FindFirstObjectByType<MatchRunner>();
            if (!EditorApplication.isPlaying || runner == null || runner.Lockstep != null || runner.Recording != null || runner.Playback != null)
                throw new InvalidOperationException("Main의 로컬 Play에서만 촬영할 수 있습니다.");
            if (Status == "recording") throw new InvalidOperationException("촬영 중입니다.");
            stopRequested = false;
            // 매 촬영의 영상/스틸/기록을 분리해 이전 검수 자료를 덮어쓰지 않는다.
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            Folder = Path.GetFullPath(Path.Combine("Recordings", "UI-Review-" + stamp));
            Directory.CreateDirectory(Folder);
            Output = Path.Combine(Folder, "KNOCKBOX_UI_FullFlow_" + stamp + ".mp4");
            runner.StartCoroutine(Record(runner));
        }

        static IEnumerator Record(MatchRunner runner)
        {
            var view = runner.GetComponent<FrontendController>().View;
            var lobby = runner.GetComponent<OnlineLobby>();
            var presenter = runner.GetComponent<MatchPresenter>();
            var cache = new Dictionary<AudioClip, float[]>();
            var voices = new List<Voice>();
            var chapters = new List<string>();
            var stills = new HashSet<string>();
            double started = Time.realtimeSinceStartupAsDouble;
            int oldRate = Application.targetFrameRate, oldVsync = QualitySettings.vSyncCount;
            var oldCapture = Time.captureDeltaTime;
            bool oldBackground = Application.runInBackground;
            float oldVolume = AudioListener.volume;
            MediaEncoder encoder = null;
            long samples = 0;
            bool complete = false;
            Action<AudioClip,float,float> played = (clip,volume,pitch) =>
            {
                if (!cache.TryGetValue(clip,out var data))
                {
                    data = new float[clip.samples * clip.channels];clip.GetData(data,0);cache.Add(clip,data);
                }
                voices.Add(new Voice { Data=data,Channels=clip.channels,Frequency=clip.frequency,
                    Start=Time.realtimeSinceStartupAsDouble-started,Volume=volume,Pitch=pitch });
            };
            Status="recording";Frames=0;AudioPeak=0;
            presenter.SfxPlayer.Played += played;
            Application.runInBackground=true;Application.targetFrameRate=30;QualitySettings.vSyncCount=0;
            Time.captureDeltaTime=0; // UI의 unscaled 시간과 시뮬레이션 모두 실제 시간으로 촬영한다.
            AudioListener.volume=0; // 파일에는 SfxPlayer의 실제 재생 사건을 같은 음높이/볼륨으로 섞는다.
            int stage=0,match=1,lastRound=-1,lastPick=-1,lastHover=-1;
            double stageAt=0,fightingAt=0,pickAt=0,lastProgress=-1;
            string lastChapter="";
            Button At(string path)=>view.Stage.Find(Knockbox.Presentation.SceneObjectName.Path(path)).GetComponent<Button>();
            void Bots()
            {
                runner.Player1Input=runner.RouteFrontendInput(new BrawlBot(()=>runner.World,0,17),0);
                runner.Player2Input=runner.RouteFrontendInput(new BrawlBot(()=>runner.World,1,29),1,true);
            }
            try
            {
                while (!stopRequested && (Seconds<420 || Frames==0))
                {
                    Seconds=Time.realtimeSinceStartupAsDouble-started;
                    var m=runner.World.State.Match;
                    if(stage==0 && Seconds>=2){At("Main_and_lobby/Menu/Join_room").onClick.Invoke();stage=1;stageAt=Seconds;}
                    else if(stage==1 && Seconds-stageAt>=2){At("Main_and_lobby/Room/Back").onClick.Invoke();stage=2;stageAt=Seconds;}
                    else if(stage==2 && Seconds-stageAt>=1){At("Main_and_lobby/Menu/Practice").onClick.Invoke();Bots();stage=3;}
                    else if(stage==3)
                    {
                        if(m.Phase==MatchPhase.Fighting)
                        {
                            if(lastRound!=m.Round){lastRound=m.Round;fightingAt=Seconds;}
                            double fightSeconds=match==1&&m.Round==1?8:1.4;
                            if(Seconds-fightingAt>=fightSeconds)
                            {
                                // 첫 경기는 P2 한 세트 뒤 P1 승리, 두 번째는 P2 승리. 실제 즉사 기능으로 정상 경기 흐름을 탄다.
                                int defeated=match==2 || m.SetScore0+m.SetScore1==0?0:1;
                                runner.TryDebugKnockout(defeated);
                            }
                        }
                        else if(m.Phase==MatchPhase.SetPick && m.Picker==0 && m.PickedAt<0)
                        {
                            if(lastPick!=m.Round){lastPick=m.Round;pickAt=Seconds;lastHover=-1;}
                            int choice=Math.Min(2,(int)((Seconds-pickAt)/.75));
                            if(choice!=lastHover && view.CanChoose){view.CardButton(choice).GetComponent<FrontendHover>().Hover?.Invoke(null);lastHover=choice;}
                            if(Seconds-pickAt>=2.6 && view.CanChoose)view.CardButton(2).onClick.Invoke();
                        }
                        else if(m.Phase==MatchPhase.MatchEnd && m.PhaseTick>=390)
                        {
                            if(match==1)
                            {
                                At("Shared_VS_and_cards/Ready_for_rematch").onClick.Invoke();
                                stage=4; // 결과 화면에서 6.5초를 실제로 기다린 다음 O를 누른다.
                            }
                            else {At("Shared_VS_and_cards/Return_to_menu").onClick.Invoke();stage=5;stageAt=Seconds;}
                        }
                    }
                    else if(stage==4 && m.Phase==MatchPhase.Countdown)
                    {match=2;lastRound=lastPick=-1;Bots();stage=3;}
                    else if(stage==5 && Seconds-stageAt>=2){complete=true;break;}

                    yield return new WaitForEndOfFrame();
                    double elapsed=Time.realtimeSinceStartupAsDouble-started;
                    var texture=ScreenCapture.CaptureScreenshotAsTexture(); // Overlay Canvas까지 포함.
                    try
                    {
                        if(encoder==null)
                            encoder=new MediaEncoder(Output,new VideoTrackAttributes {frameRate=new MediaRational(30),width=(uint)texture.width,height=(uint)texture.height,includeAlpha=false,bitRateMode=VideoBitrateMode.High},
                                new AudioTrackAttributes {sampleRate=new MediaRational(SampleRate),channelCount=2,language="ko"});
                        if(!encoder.AddFrame(texture,new MediaTime((long)(elapsed*1000000),1000000,1)))throw new InvalidOperationException("Video frame rejected");
                        long end=(long)(elapsed*SampleRate);
                        int count=(int)(end-samples);
                        if(count>0)
                        {
                            var audio=new NativeArray<float>(count*2,Allocator.Temp);
                            try
                            {
                                foreach(var voice in voices)for(int i=0;i<count;i++)
                                {
                                    double at=((samples+i)/(double)SampleRate-voice.Start)*voice.Frequency*voice.Pitch;
                                    if(at<0)continue;int index=(int)at;if(index+1>=voice.Data.Length/voice.Channels)continue;
                                    float blend=(float)(at-index);
                                    for(int c=0;c<2;c++){int channel=Math.Min(c,voice.Channels-1);audio[i*2+c]+=Mathf.Lerp(voice.Data[index*voice.Channels+channel],voice.Data[(index+1)*voice.Channels+channel],blend)*voice.Volume;}
                                }
                                for(int i=0;i<audio.Length;i++){audio[i]=SfxMaster.Limit(audio[i]);AudioPeak=Math.Max(AudioPeak,Math.Abs(audio[i]));}
                                if(!encoder.AddSamples(audio))throw new InvalidOperationException("Audio samples rejected");
                            }
                            finally{audio.Dispose();}
                            samples=end;
                            voices.RemoveAll(v=>elapsed-v.Start>(v.Data.Length/(double)v.Channels/v.Frequency/v.Pitch)+.1);
                        }
                        Frames++;
                        var state=runner.World.State.Match;
                        string chapter=$"match {match} / {view.Screen} / round {state.Round} / {state.Phase} / score {state.SetScore0}:{state.SetScore1}";
                        if(chapter!=lastChapter){chapters.Add(elapsed.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+"  "+chapter);lastChapter=chapter;}
                        string still=null;
                        if(view.Screen==FrontendScreen.Main&&elapsed>1)still="main";
                        if(view.Screen==FrontendScreen.Versus&&state.PhaseTick>=45&&state.PhaseTick<60)still="vs";
                        if(view.Screen==FrontendScreen.Versus&&state.PhaseTick>=8&&state.PhaseTick<17)still="curtain-vs";
                        if(view.Screen==FrontendScreen.Cards&&state.PickedAt<0&&state.PhaseTick>=30&&state.PhaseTick<42)still="whole-picker-entry-p"+(state.Picker+1);
                        if(view.Screen==FrontendScreen.Cards&&state.PickedAt<0&&state.PhaseTick>=90)still="cards-p"+(state.Picker+1);
                        if(state.Phase==MatchPhase.Countdown&&view.Screen==FrontendScreen.Hidden&&state.PhaseTick>=view.IntroRevealTick+CardCollectionRules.RebuildTicks+20)still="countdown";
                        if(state.Phase==MatchPhase.KO&&state.PhaseTick>=40&&state.PhaseTick<55)still="ko-celebration";
                        if(state.Phase==MatchPhase.KO&&state.PhaseTick>=119&&state.PhaseTick<130)still="score-flight";
                        if(state.Phase==MatchPhase.KO&&state.PhaseTick>=134&&state.PhaseTick<140)still="fragments-exit-p"+(state.RoundWinner==0?2:1);
                        if(view.Screen==FrontendScreen.Result&&state.PhaseTick>=70&&state.PhaseTick<100)still="winner-"+match;
                        if(view.Screen==FrontendScreen.Result&&state.PhaseTick>=280&&state.PhaseTick<320)still="rematch-"+match;
                        if(view.Screen==FrontendScreen.Result&&stage==4&&state.PhaseTick>=405)still="rematch-clicked";
                        if(still!=null&&stills.Add(still))File.WriteAllBytes(Path.Combine(Folder,still+".png"),texture.EncodeToPNG());
                    }
                    finally{Object.Destroy(texture);}
                    if(elapsed-lastProgress>=1)
                    {
                        lastProgress=elapsed;
                        File.WriteAllText(Path.Combine(Folder,"status.txt"),$"recording {elapsed:F1}s / {Frames} frames / {lastChapter}");
                    }
                }
            }
            finally
            {
                encoder?.Dispose();presenter.SfxPlayer.Played-=played;
                Time.captureDeltaTime=oldCapture;Application.targetFrameRate=oldRate;QualitySettings.vSyncCount=oldVsync;
                Application.runInBackground=oldBackground;AudioListener.volume=oldVolume;runner.Running=false;
                Status=complete?"complete":"incomplete";
                File.WriteAllLines(Path.Combine(Folder,"chapters.txt"),chapters);
                File.WriteAllText(Path.Combine(Folder,"status.txt"),$"{Status}\n{Output}\nseconds={Seconds:F3}\nframes={Frames}\naudioPeak={AudioPeak:F4}\nLocal scripted UI review using offline KO cheats; not a 2PC match.");
            }
        }

        public static void VerifyVideo()
        {
            var runner=Object.FindFirstObjectByType<MatchRunner>();
            if(runner==null || !File.Exists(Output))throw new InvalidOperationException("No recorded video");
            runner.StartCoroutine(Verify());
        }
        static IEnumerator Verify()
        {
            var go=new GameObject("Temporary_video_verification");
            var player=go.AddComponent<VideoPlayer>();
            var target=new RenderTexture(960,540,0);target.Create();
            var texture=new Texture2D(960,540,TextureFormat.RGB24,false);
            player.playOnAwake=false;player.url=Output;player.renderMode=VideoRenderMode.RenderTexture;player.targetTexture=target;
            player.audioOutputMode=VideoAudioOutputMode.None;player.Prepare();
            try
            {
                double deadline=Time.realtimeSinceStartupAsDouble+15;
                while(!player.isPrepared&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
                if(!player.isPrepared)throw new InvalidOperationException("Video prepare timed out");
                File.WriteAllText(Path.Combine(Folder,"video-metadata.txt"),$"width={player.width}\nheight={player.height}\nseconds={player.length:F3}\nframes={player.frameCount}\nfps={player.frameRate}\naudioTracks={player.audioTrackCount}\n");
                double[] positions={1,player.length*.5,player.length-7};
                for(int i=0;i<positions.Length;i++)
                {
                    player.time=positions[i];player.Play();yield return new WaitForSecondsRealtime(.15f);player.Pause();
                    var before=RenderTexture.active;RenderTexture.active=target;
                    texture.ReadPixels(new Rect(0,0,960,540),0,0);texture.Apply();RenderTexture.active=before;
                    File.WriteAllBytes(Path.Combine(Folder,"decoded-"+i+".png"),texture.EncodeToPNG());
                }
            }
            finally{Object.Destroy(go);target.Release();Object.Destroy(target);Object.Destroy(texture);}
        }
    }
}
