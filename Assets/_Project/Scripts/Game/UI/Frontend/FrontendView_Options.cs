using System;
using UnityEngine;
using UnityEngine.UI;

namespace Knockbox.Game.UI
{
    /// <summary>
    /// 옵션 화면(2026-11-16 게임 흐름 CP6, 임시 배치): 메인의 큰 캐릭터 옆에 버튼 목록을 띄운다. 버튼을 누를 때마다 값이 바뀌고(음량은 20%씩),
    /// 바로 적용·저장한다. 순서: 음량, 화면 모드, 해상도, 화면 흔들림 줄이기, 강한 섬광 줄이기, 기본값, 뒤로. 아래에 조작 안내를 둔다.
    /// 버튼은 Frontend 프리팹 Main_and_lobby/Options_panel 아래 실제 오브젝트다.
    /// </summary>
    public sealed partial class FrontendView
    {
        public event Action<int> OptionPressed;
        [SerializeField] RectTransform optionsPanel;
        [SerializeField] Button[] optionButtons = new Button[0];
        [SerializeField] Text controlsGuide;
        public const int OptionBack = 6;

        public void SetOptionLabels(string[] labels, string guide)
        {
            for (int i = 0; i < optionButtons.Length && i < labels.Length; i++)
                if (optionButtons[i] != null) optionButtons[i].GetComponentInChildren<Text>(true).text = labels[i];
            if (controlsGuide != null) controlsGuide.text = guide;
        }

        void BindOptions()
        {
            for (int i = 0; i < optionButtons.Length; i++)
            {
                int index = i;
                if (optionButtons[i] != null) Bind(optionButtons[i], () => OptionPressed?.Invoke(index));
            }
        }

        void ShowOptions(bool on)
        {
            if (optionsPanel != null) optionsPanel.gameObject.SetActive(on);
        }

        /// <summary>절차식 화면(Sandbox·검사용)에도 같은 버튼을 만든다. Main은 프리팹의 저장된 오브젝트를 쓴다.</summary>
        void BuildOptions(RectTransform parent)
        {
            optionsPanel = FrontendWidgets.Rect(parent, "Options_panel", 900, 110, 860, 900);
            optionButtons = new Button[7];
            for (int i = 0; i < optionButtons.Length; i++)
            {
                int index = i;
                optionButtons[i] = FrontendWidgets.Button(optionsPanel, "Option_" + i, new Rect(70, 20 + i * 80, 720, 70), "", Theme,
                    Presentation.PaletteAsset.Transparent, Theme.white, () => OptionPressed?.Invoke(index), 30);
            }
            controlsGuide = FrontendWidgets.Text(optionsPanel, "Controls_guide", new Rect(0, 600, 860, 200), "", Theme.textFont, 24, Theme.status);
            optionsPanel.gameObject.SetActive(false);
        }
    }
}
