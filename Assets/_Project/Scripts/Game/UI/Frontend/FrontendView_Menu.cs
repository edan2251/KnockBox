using System;
using UnityEngine;
using UnityEngine.UI;

namespace Knockbox.Game.UI
{
    /// <summary>
    /// 메인 메뉴의 두 쪽(2026-11-16 게임 흐름 CP3): 첫 화면은 게임 시작·옵션·게임 종료, 게임 시작을 누르면 방 만들기·코드로 참가·연습·뒤로.
    /// 버튼은 Frontend 프리팹의 Menu 아래 실제 오브젝트이며(Start_game, Options, Quit, Menu_back), 배치·문구는 프리팹에서 고친다.
    /// </summary>
    public sealed partial class FrontendView
    {
        public event Action Options, Quit;
        [SerializeField] Button startGame, optionsButton, quitButton, menuBack;
        /// <summary>참이면 게임 시작 하위 메뉴(방 만들기·코드로 참가·연습·뒤로)를 보여준다.</summary>
        public bool StartMenuOpen { get; private set; }

        /// <summary>메인 메뉴 쪽을 바꾸고 항목 등장 연출을 처음부터 다시 한다.</summary>
        public void SetMenuPage(bool start)
        {
            StartMenuOpen = start;
            ApplyMenuPage();
            if (Screen == FrontendScreen.Main) enteredAt = Time.unscaledTime;
        }

        void ApplyMenuPage()
        {
            foreach (var b in new[] { create, joinMenu, practice, menuBack })
                if (b != null) b.gameObject.SetActive(StartMenuOpen);
            foreach (var b in new[] { startGame, optionsButton, quitButton })
                if (b != null) b.gameObject.SetActive(!StartMenuOpen);
        }

        void BindMenuPages()
        {
            Bind(startGame, () => SetMenuPage(true));
            Bind(menuBack, () => SetMenuPage(false));
            Bind(optionsButton, () => Options?.Invoke());
            Bind(quitButton, () => Quit?.Invoke());
        }

        /// <summary>절차식 화면(Sandbox·검사용)에도 같은 버튼을 만든다. Main은 프리팹의 저장된 버튼을 쓴다.</summary>
        void BuildMenuPages(RectTransform mainContent)
        {
            startGame = FrontendWidgets.Button(mainContent, "Start_game", new Rect(120, 240, 720, 94), "게임 시작", Theme, Presentation.PaletteAsset.Transparent, Theme.white, () => SetMenuPage(true), 40);
            optionsButton = FrontendWidgets.Button(mainContent, "Options", new Rect(120, 354, 720, 94), "옵션", Theme, Presentation.PaletteAsset.Transparent, Theme.white, () => Options?.Invoke(), 40);
            quitButton = FrontendWidgets.Button(mainContent, "Quit", new Rect(120, 468, 720, 94), "게임 종료", Theme, Presentation.PaletteAsset.Transparent, Theme.white, () => Quit?.Invoke(), 40);
            menuBack = FrontendWidgets.Button(mainContent, "Menu_back", new Rect(120, 582, 720, 94), "뒤로", Theme, Presentation.PaletteAsset.Transparent, Theme.back, () => SetMenuPage(false), 34);
        }
    }
}
