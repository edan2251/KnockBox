using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Knockbox.Game.Online
{
    /// <summary>Separate schema: authoritative/predicted ticks and SDK RTT are not the former lockstep input-age metric.</summary>
    public sealed class FusionPredictionDiagnostics : IDisposable
    {
        /// <summary>
        /// 프레임 계측(에디터·개발 빌드 기본, 일반 빌드는 실행 인수 -netframes). 프레임마다 미리 잡은 메모리 배열에만 쓰고
        /// 세션이 끝날 때 한 번 저장한다(매 프레임 파일 쓰기가 측정 대상인 끊김을 만들지 않게). 약 15분(60fps 기준) 뒤에는 더 쌓지 않는다.
        /// </summary>
        public static bool FrameTraceEnabled = Debug.isDebugBuild || AutoPlayDebug.HasArg("-netframes");
        const int MaxFrames = 60 * 60 * 15, MaxInputs = 60 * 60 * 15, PendingCapacity = 512, InitialTrace = 4096;

        StreamWriter writer;
        readonly double started;
        readonly int slot, delay;
        readonly string stamp;
        double nextWrite, frameSum, frameMax;
        int frames;
        public string FilePath { get; private set; }

        // 프레임 계측. 틱은 모두 SimState.Tick 기준이다(Fusion 틱과 다르다).
        struct FrameSample
        {
            public double At;
            public float DtMs, RttMs, Alpha, CorrectionMax;
            public int Predicted, Server, Confirmed, From, To, Forward, Resim, Corrected, MissingLocal, MissingRemote;
            public bool RenderedThisFrame;
        }
        struct InputSample { public int Sequence, Tick; public double Sampled, Applied, Rendered, Snapshot, Confirmed; }
        FrameSample[] frameTrace;
        InputSample[] inputTrace;
        readonly InputSample[] pending = new InputSample[PendingCapacity];
        int frameCount, inputCount, pendingStart, pendingCount, droppedInputs;
        public string FrameTracePath { get; private set; }
        public string InputTracePath { get; private set; }
        public FrameTraceSummary LastSummary { get; private set; }

        public FusionPredictionDiagnostics(int slot, int delay)
        {
            this.slot = slot; this.delay = delay;
            started = Time.realtimeSinceStartupAsDouble;
            nextWrite = started + 1;
            stamp = $"{DateTime.UtcNow:yyyyMMdd_HHmmss_fffZ}_{Guid.NewGuid():N}";
            if (FrameTraceEnabled)
            {
                // 처음엔 작게 잡고 필요할 때 두 배로 늘린다(한 번에 6.5MB를 잡느라 경기 시작 프레임이 11~20ms 늘던 것을 줄임).
                frameTrace = new FrameSample[InitialTrace];
                inputTrace = new InputSample[InitialTrace];
            }
            // 배포용 빌드(2026-11-21)는 게임 폴더에 진단 CSV를 쓰지 않는다(개발 빌드·에디터·검사 실행만).
            if (!Application.isEditor && !Debug.isDebugBuild && !FrameTraceEnabled) return;
            try
            {
                string folder = Path.Combine(ReplayFiles.Folder, "Network");
                Directory.CreateDirectory(folder);
                FilePath = Path.Combine(folder, $"online_prediction_P{slot + 1}_{stamp}.csv");
                writer = new StreamWriter(FilePath);
                writer.WriteLine("schema,elapsed_s,slot,delay_ticks,network_hz,send_hz,path,control_rtt_ms,sdk_rtt_ms,predicted_tick,confirmed_tick,forward_total,resim_total,corrected_ticks_total,max_same_tick_correction,missing_local_total,host_missing_remote_total,local_samples_total,local_age_mean_ms,local_age_max_ms,verified_batches,frame_mean_ms,frame_max_ms,snapshot_bytes,build_guid");
                writer.Flush();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { Disable(e); }
        }

        public void RecordFrame(FusionPredictionSession s, double controlRtt, FusionTransport.ConnectionTiming timing)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (frameTrace != null) TraceFrame(s, now, timing);
            if (writer == null) return;
            frames++; frameSum += Time.unscaledDeltaTime; frameMax = Math.Max(frameMax, Time.unscaledDeltaTime);
            if (now < nextWrite) return;
            try
            {
                writer.WriteLine(string.Join(",", "prediction-v1", F(now - started), slot + 1, delay,
                    timing.TickRate, timing.SendRate, timing.Path, F(controlRtt * 1000), F(timing.RttSeconds * 1000),
                    s.Match.World.State.Tick, s.Journal.World.State.Tick, s.ForwardTicks, s.ResimulationTicks,
                    s.CorrectedTicks, F(s.LargestCorrection), s.MissingLocalInputs, s.MissingRemoteInputs,
                    s.LocalInputSamples, F(s.LocalInputSamples > 0 ? s.LocalInputAgeTotal * 1000 / s.LocalInputSamples : -1),
                    F(s.LocalInputAgeMax * 1000), s.Journal.BatchesVerified, F(frameSum * 1000 / frames),
                    F(frameMax * 1000), FusionWorldSnapshot.ByteCount, Application.buildGUID));
                writer.Flush();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { Disable(e); }
            frames = 0; frameSum = frameMax = 0; nextWrite = now + 1;
        }

        /// <summary>순방향 예측이 처음 쓴 내 입력(수집 시각·결과 틱). 같은 로컬 시계로 표시·수신·확정 시점을 이어 찾는다.</summary>
        public void InputApplied(int sequence, double sampledAt, double appliedAt, int tick)
        {
            if (inputTrace == null) return;
            if (pendingCount == PendingCapacity) { droppedInputs++; pendingStart = (pendingStart + 1) % PendingCapacity; pendingCount--; }
            pending[(pendingStart + pendingCount) % PendingCapacity] = new InputSample
            { Sequence = sequence, Tick = tick, Sampled = sampledAt, Applied = appliedAt, Rendered = -1, Snapshot = -1, Confirmed = -1 };
            pendingCount++;
        }

        void TraceFrame(FusionPredictionSession s, double now, FusionTransport.ConnectionTiming timing)
        {
            // 예측 월드가 첫 틱을 계산하기 전(시작 대기 1초)은 표시 시간이 원래 멈춰 있으므로 기록하지 않는다.
            if (s.ForwardTicks == 0) return;
            var m = s.Match;
            int from = m.PreviousState.Tick, to = m.World.State.Tick;
            float alpha = m.RenderAlpha;
            if (frameCount == frameTrace.Length && frameCount < MaxFrames)
                System.Array.Resize(ref frameTrace, System.Math.Min(MaxFrames, frameCount * 2));
            if (frameCount < frameTrace.Length)
            {
                frameTrace[frameCount++] = new FrameSample
                {
                    At = now, DtMs = Time.unscaledDeltaTime * 1000, RttMs = (float)(timing.RttSeconds * 1000), Alpha = alpha,
                    CorrectionMax = s.TakeFrameCorrection(), Predicted = s.PredictedTick, Server = s.LatestServerTick,
                    Confirmed = s.Journal.World.State.Tick, From = from, To = to, Forward = s.ForwardTicks,
                    Resim = s.ResimulationTicks, Corrected = s.CorrectedTicks, MissingLocal = s.MissingLocalInputs,
                    MissingRemote = s.MissingRemoteInputs, RenderedThisFrame = s.RenderObservedFrame == Time.frameCount,
                };
            }
            // 입력 효과가 그려지기 시작한 프레임: 표시 시간(from + alpha·(to-from))이 그 틱의 직전 틱을 넘은 첫 프레임.
            double renderTime = from + alpha * (to - from);
            int confirmed = s.Journal.World.State.Tick;
            for (int i = 0; i < pendingCount; i++)
            {
                ref var p = ref pending[(pendingStart + i) % PendingCapacity];
                if (p.Rendered < 0 && renderTime > p.Tick - 1 + 1e-4) p.Rendered = now;
                if (p.Snapshot < 0 && s.LatestServerTick >= p.Tick) p.Snapshot = now;
                if (p.Confirmed < 0 && confirmed >= p.Tick) p.Confirmed = now;
            }
            while (pendingCount > 0)
            {
                ref var head = ref pending[pendingStart];
                if (head.Rendered < 0 || head.Snapshot < 0 || head.Confirmed < 0) break;
                if (inputCount == inputTrace.Length && inputCount < MaxInputs)
                    System.Array.Resize(ref inputTrace, System.Math.Min(MaxInputs, inputCount * 2));
                if (inputCount < inputTrace.Length) inputTrace[inputCount++] = head;
                pendingStart = (pendingStart + 1) % PendingCapacity; pendingCount--;
            }
        }

        void SaveTrace()
        {
            if (frameTrace == null || frameCount == 0) return;
            LastSummary = FrameTraceSummary.FromRenderTimes(Collect(frameCount, i => frameTrace[i].From + frameTrace[i].Alpha * (frameTrace[i].To - frameTrace[i].From)),
                Collect(frameCount, i => (double)frameTrace[i].DtMs));
            try
            {
                string folder = Path.Combine(ReplayFiles.Folder, "Network");
                FrameTracePath = Path.Combine(folder, $"online_frames_P{slot + 1}_{stamp}.csv");
                var sb = new StringBuilder(frameCount * 96);
                sb.Append("schema,t_s,dt_ms,sdk_rtt_ms,predicted_tick,server_tick,confirmed_tick,render_from,render_to,alpha,render_time,rendered_this_frame,forward_total,resim_total,corrected_total,frame_correction_max,missing_local_total,host_missing_remote_total,delay_ticks\n");
                for (int i = 0; i < frameCount; i++)
                {
                    ref var f = ref frameTrace[i];
                    sb.Append("frames-v1,").Append(F(f.At - started)).Append(',').Append(F(f.DtMs)).Append(',').Append(F(f.RttMs)).Append(',')
                      .Append(f.Predicted).Append(',').Append(f.Server).Append(',').Append(f.Confirmed).Append(',')
                      .Append(f.From).Append(',').Append(f.To).Append(',').Append(F(f.Alpha)).Append(',')
                      .Append(F(f.From + f.Alpha * (f.To - f.From))).Append(',').Append(f.RenderedThisFrame ? 1 : 0).Append(',')
                      .Append(f.Forward).Append(',').Append(f.Resim).Append(',').Append(f.Corrected).Append(',').Append(F(f.CorrectionMax)).Append(',')
                      .Append(f.MissingLocal).Append(',').Append(f.MissingRemote).Append(',').Append(delay).Append('\n');
                }
                File.WriteAllText(FrameTracePath, sb.ToString());
                InputTracePath = Path.Combine(folder, $"online_inputs_P{slot + 1}_{stamp}.csv");
                sb.Clear().Append("schema,sequence,tick,sampled_s,collect_to_sim_ms,collect_to_render_ms,collect_to_snapshot_ms,collect_to_confirmed_ms,dropped_total\n");
                for (int i = 0; i < inputCount; i++)
                {
                    ref var p = ref inputTrace[i];
                    sb.Append("inputs-v1,").Append(p.Sequence).Append(',').Append(p.Tick).Append(',').Append(F(p.Sampled - started)).Append(',')
                      .Append(F((p.Applied - p.Sampled) * 1000)).Append(',').Append(F((p.Rendered - p.Sampled) * 1000)).Append(',')
                      .Append(F((p.Snapshot - p.Sampled) * 1000)).Append(',').Append(F((p.Confirmed - p.Sampled) * 1000)).Append(',')
                      .Append(droppedInputs).Append('\n');
                }
                File.WriteAllText(InputTracePath, sb.ToString());
                Debug.Log($"[Knockbox][prediction] 프레임 계측 P{slot + 1}: 표시 {LastSummary}; 입력 수집 후(평균/p95 ms) {InputLatencySummary()}");
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            { Debug.LogWarning($"[Knockbox][prediction] 프레임 계측 저장 실패: {e.GetType().Name}"); }
            frameTrace = null; inputTrace = null;
        }

        string InputLatencySummary()
        {
            if (inputCount == 0) return "표본 없음";
            string Stat(string name, Func<InputSample, double> at)
            {
                var v = new double[inputCount];
                for (int i = 0; i < inputCount; i++) v[i] = (at(inputTrace[i]) - inputTrace[i].Sampled) * 1000;
                Array.Sort(v);
                double sum = 0; foreach (double x in v) sum += x;
                return string.Format(CultureInfo.InvariantCulture, "{0} {1:0.0}/{2:0.0}", name, sum / v.Length, v[(int)Math.Ceiling(v.Length * 0.95) - 1]);
            }
            return $"n={inputCount}, " + Stat("예측", p => p.Applied) + ", " + Stat("표시", p => p.Rendered) + ", "
                + Stat("Host 스냅샷", p => p.Snapshot) + ", " + Stat("확정", p => p.Confirmed);
        }

        static double[] Collect(int count, Func<int, double> value)
        {
            var result = new double[count];
            for (int i = 0; i < count; i++) result[i] = value(i);
            return result;
        }

        static string F(double value) => value.ToString("0.000", CultureInfo.InvariantCulture);
        void Disable(Exception e)
        {
            try { writer?.Dispose(); } catch (IOException) { }
            writer = null;
            Debug.LogWarning($"[Knockbox][prediction] 진단 저장 중단: {e.GetType().Name}");
        }
        public void Dispose()
        {
            try { writer?.Dispose(); } catch (IOException) { }
            writer = null;
            SaveTrace();
        }
    }

    /// <summary>
    /// 프레임마다 그린 표시 시간(틱 단위)의 연속성 요약. 표시 시간은 틱 번호 기준이라 경기 안의 히트스톱(틱은 계속 진행)과
    /// 섞이지 않는다. 정지 = 프레임 시간이 흘렀는데 표시 시간이 그대로, 도약 = 한 프레임에 1.5틱 넘게 전진, 역행 = 뒤로 감.
    /// </summary>
    public readonly struct FrameTraceSummary
    {
        public readonly int Frames, Stalls, Jumps, Backwards;
        public readonly double MaxStepTicks, StepErrorRms;
        public double StallRatio => Frames > 1 ? (double)Stalls / (Frames - 1) : 0;

        FrameTraceSummary(int frames, int stalls, int jumps, int backwards, double maxStep, double rms)
        { Frames = frames; Stalls = stalls; Jumps = jumps; Backwards = backwards; MaxStepTicks = maxStep; StepErrorRms = rms; }

        /// <param name="renderTimes">프레임별 표시 시간(틱)</param>
        /// <param name="frameMs">같은 프레임의 프레임 시간(ms). 기대 전진량 = 프레임 시간 × 60/1000틱</param>
        public static FrameTraceSummary FromRenderTimes(IReadOnlyList<double> renderTimes, IReadOnlyList<double> frameMs, double tickRate = 60)
        {
            int stalls = 0, jumps = 0, backwards = 0, n = 0;
            double maxStep = 0, sq = 0;
            for (int i = 1; i < renderTimes.Count; i++)
            {
                double step = renderTimes[i] - renderTimes[i - 1];
                double expected = frameMs[i] * tickRate / 1000.0;
                if (step < -1e-6) backwards++;
                else if (step < 1e-6 && expected > 0.05) stalls++;
                if (step > 1.5) jumps++;
                maxStep = Math.Max(maxStep, step);
                sq += (step - expected) * (step - expected); n++;
            }
            return new FrameTraceSummary(renderTimes.Count, stalls, jumps, backwards, maxStep, n > 0 ? Math.Sqrt(sq / n) : 0);
        }

        public override string ToString() => string.Format(CultureInfo.InvariantCulture,
            "frames={0} stalls={1} ({2:P1}) jumps>1.5={3} backwards={4} maxStep={5:0.00} stepErrRms={6:0.000}",
            Frames, Stalls, StallRatio, Jumps, Backwards, MaxStepTicks, StepErrorRms);
    }
}
