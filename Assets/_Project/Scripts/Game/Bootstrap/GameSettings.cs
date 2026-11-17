using System.Collections.Generic;
using UnityEngine;

namespace Knockbox.Game
{
    /// <summary>
    /// 기본 옵션(2026-11-16 게임 흐름 CP6): 효과음 음량, 화면 모드·해상도, 화면 흔들림·강한 섬광 줄이기. PlayerPrefs에 저장하고 실행 시 되살린다.
    /// 판정·입력·기록과 무관한 화면·소리 설정이다. 기존 설정 저장소가 없어 새로 둔다(음악·키 재설정·세부 그래픽은 후속 범위).
    /// </summary>
    public sealed class GameSettings
    {
        const string Prefix = "Knockbox.Settings.";
        public float Volume = 1f;
        public bool Fullscreen = true;
        /// <summary>0이면 모니터 기본 해상도.</summary>
        public int Width, Height;
        public bool ReduceShake, ReduceFlash;

        public static GameSettings Load() => new GameSettings
        {
            Volume = Mathf.Clamp01(PlayerPrefs.GetFloat(Prefix + "Volume", 1f)),
            Fullscreen = PlayerPrefs.GetInt(Prefix + "Fullscreen", 1) != 0,
            Width = PlayerPrefs.GetInt(Prefix + "Width", 0),
            Height = PlayerPrefs.GetInt(Prefix + "Height", 0),
            ReduceShake = PlayerPrefs.GetInt(Prefix + "ReduceShake", 0) != 0,
            ReduceFlash = PlayerPrefs.GetInt(Prefix + "ReduceFlash", 0) != 0,
        };

        public void Save()
        {
            PlayerPrefs.SetFloat(Prefix + "Volume", Volume);
            PlayerPrefs.SetInt(Prefix + "Fullscreen", Fullscreen ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "Width", Width);
            PlayerPrefs.SetInt(Prefix + "Height", Height);
            PlayerPrefs.SetInt(Prefix + "ReduceShake", ReduceShake ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "ReduceFlash", ReduceFlash ? 1 : 0);
            PlayerPrefs.Save();
        }

        public void ResetToDefaults()
        {
            Volume = 1f; Fullscreen = true; Width = Height = 0; ReduceShake = ReduceFlash = false;
        }

        /// <summary>
        /// 실제 적용. 소리는 AudioListener 전체 음량(지금 소리는 효과음뿐), 흔들림은 F7과 같은 카메라 줄이기, 섬광은 KO 화면 섬광·색수차 펄스.
        /// 에디터에서는 창 크기를 바꾸지 않는다.
        /// </summary>
        public void Apply(MatchPresenter presenter)
        {
            AudioListener.volume = Volume;
            if (presenter != null)
            {
                if (presenter.CameraRig != null) presenter.CameraRig.ReduceMotion = ReduceShake;
                presenter.ReduceFlash = ReduceFlash;
            }
            if (Application.isEditor) return;
            var mode = Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            var native = Screen.currentResolution;
            int w = Width > 0 ? Width : native.width, h = Height > 0 ? Height : native.height;
            Screen.SetResolution(w, h, mode);
        }

        public void CycleVolume() => Volume = Volume <= .001f ? 1f : Mathf.Max(0f, Mathf.Round((Volume - .2f) * 10f) / 10f);

        public void NextResolution()
        {
            var sizes = Resolutions();
            if (sizes.Count == 0) return;
            int current = sizes.FindIndex(s => s.x == Width && s.y == Height);
            var next = sizes[(current + 1) % sizes.Count];
            Width = next.x; Height = next.y;
        }

        static List<Vector2Int> Resolutions()
        {
            var list = new List<Vector2Int>();
            foreach (var r in Screen.resolutions)
            {
                var size = new Vector2Int(r.width, r.height);
                if (size.x >= 1024 && !list.Contains(size)) list.Add(size);
            }
            return list;
        }

        /// <summary>옵션 화면의 버튼 글자(순서: 음량, 화면 모드, 해상도, 흔들림, 섬광, 기본값, 뒤로).</summary>
        public string[] Labels() => new[]
        {
            "효과음 음량   " + Mathf.RoundToInt(Volume * 100f) + "%",
            "화면 모드   " + (Fullscreen ? "전체 화면" : "창 모드"),
            "해상도   " + (Width > 0 ? Width + " × " + Height : "모니터 기본"),
            "화면 흔들림 줄이기   " + (ReduceShake ? "켬" : "끔"),
            "강한 섬광 줄이기   " + (ReduceFlash ? "켬" : "끔"),
            "기본값으로 되돌리기",
            "뒤로",
        };

        public const string ControlsGuide =
            "조작   이동 A / D · 점프 Space · 조준 마우스\n" +
            "펀치 왼쪽 클릭 · 몸통 박치기 왼쪽 길게 눌렀다 놓기\n" +
            "잡기 오른쪽 클릭 · 잡은 상대 던지기 왼쪽 클릭\n" +
            "준비·다시하기 R 또는 O · 나가기 X · 메뉴 Esc";
    }
}
