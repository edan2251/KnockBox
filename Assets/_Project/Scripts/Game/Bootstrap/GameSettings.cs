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
        /// <summary>닉네임(2026-11-21 메인 화면): 옵션이 아니라 프로필 값이라 기본값으로 되돌리기에서 지우지 않는다.</summary>
        const string NicknameKey = "Knockbox.Profile.Nickname";
        public const string DefaultNickname = "wombat";
        public const int NicknameMaxLength = 10;
        string nicknameKey = NicknameKeyForBuild(Application.buildGUID);
        public float Volume = 1f;
        /// <summary>정리된 닉네임. 비어 있으면 자리 이름(P1/P2)을 쓴다.</summary>
        public string Nickname = DefaultNickname;
        public bool Fullscreen = true;
        /// <summary>0이면 모니터 기본 해상도.</summary>
        public int Width, Height;
        public bool ReduceShake, ReduceFlash;

        public static GameSettings Load() => LoadForBuild(Application.buildGUID);

        // 에디터의 buildGUID는 빈 문자열이다. 새 빌드는 다른 키에서 기본 이름으로 시작하고, 같은 빌드의 수정은 유지한다.
        static string NicknameKeyForBuild(string buildGuid) => string.IsNullOrEmpty(buildGuid) ? NicknameKey : NicknameKey + "." + buildGuid;

        static GameSettings LoadForBuild(string buildGuid) => new GameSettings
        {
            nicknameKey = NicknameKeyForBuild(buildGuid),
            Volume = Mathf.Clamp01(PlayerPrefs.GetFloat(Prefix + "Volume", 1f)),
            Fullscreen = PlayerPrefs.GetInt(Prefix + "Fullscreen", 1) != 0,
            Width = PlayerPrefs.GetInt(Prefix + "Width", 0),
            Height = PlayerPrefs.GetInt(Prefix + "Height", 0),
            ReduceShake = PlayerPrefs.GetInt(Prefix + "ReduceShake", 0) != 0,
            ReduceFlash = PlayerPrefs.GetInt(Prefix + "ReduceFlash", 0) != 0,
            Nickname = CleanNickname(PlayerPrefs.GetString(NicknameKeyForBuild(buildGuid), DefaultNickname)),
        };

        /// <summary>
        /// 화면에 그대로 써도 되는 닉네임으로 정리한다(입력·저장값·상대가 보낸 값 공통): 글자·숫자·공백과 몇 가지 기호만 남기고
        /// (서식 태그·줄바꿈·이모지처럼 글꼴에 없는 문자는 뺀다) 연속 공백을 하나로, 앞뒤 공백을 지운 뒤 최대 길이로 자른다.
        /// </summary>
        public static string CleanNickname(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            var sb = new System.Text.StringBuilder(NicknameMaxLength);
            bool space = false;
            foreach (char c in raw)
            {
                if (sb.Length >= NicknameMaxLength) break;
                if (char.IsWhiteSpace(c)) { space = sb.Length > 0; continue; }
                bool keep = !char.IsSurrogate(c) && (char.IsLetterOrDigit(c) || "-_.!?~♥".IndexOf(c) >= 0);
                if (!keep) continue;
                if (space && sb.Length < NicknameMaxLength - 1) sb.Append(' ');
                space = false;
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>화면에 쓰는 이름: 닉네임이 비어 있으면 자리 이름(P1/P2).</summary>
        public static string DisplayName(string nickname, int slot) => string.IsNullOrEmpty(nickname) ? SlotName(slot) : nickname;

        static readonly string[] SlotNames = { "P1", "P2" };
        /// <summary>자리 이름(P1/P2). 매 프레임 쓰므로 미리 만든 글자를 돌려준다.</summary>
        public static string SlotName(int slot) => SlotNames[slot == 1 ? 1 : 0];

        /// <summary>닉네임만 바꿔 저장한다(옵션 값은 건드리지 않는다).</summary>
        public void SaveNickname(string value)
        {
            Nickname = CleanNickname(value);
            PlayerPrefs.SetString(nicknameKey, Nickname);
            PlayerPrefs.Save();
        }

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
