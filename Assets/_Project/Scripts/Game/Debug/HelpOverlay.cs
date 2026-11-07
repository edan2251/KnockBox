using Knockbox.Presentation;
using UnityEngine;

namespace Knockbox.Game
{
    /// <summary>
    /// `~` 단축키 도움말(06-match-and-tools.md 4·6절). 빌드에서는 처음 실행할 때 열려 있다.
    /// 화면 가운데의 잉크색 판과 밝은 영문 글자(D-016). 월드 공간이라 캡처에도 찍힌다.
    /// </summary>
    public sealed class HelpOverlay : MonoBehaviour
    {
        public const string Text =
            "KNOCKBOX  -  prototype controls\n" +
            "\n" +
            "P1  WASD move   SPACE jump   F attack   LSHIFT dash   R switch form   E guard   Q grab\n" +
            "P2  ARROWS move   NUM0 jump   NUM1 attack   NUM2 dash   NUM5 switch form   NUM3 guard   NUM4 grab\n" +
            "PAD  stick/dpad   A jump   X attack   B/RT dash   RB switch form   LB/LT guard   Y grab\n" +
            "\n" +
            "F1 start form (scissors / rock / paper)    F2 arena: rotation / lock one\n" +
            "F3 hitbox overlay    F4 guard+grab on/off    INS energy on/off    F5 reset round\n" +
            "F6 P2 input (keyboard / pad / dummy / bot)    [ ] dummy behavior\n" +
            "F7 reduce shake    F8 sync test    F11 tuning preset (default / fast)    HOME form display\n" +
            "F9 start/stop input recording    F10 replay last recording\n" +
            "O/P KO P1/P2 (offline test)   CTRL+P pause   CTRL+O step   F12 debug text   ~ help";

        public const string BrawlText =
            "KNOCKBOX  -  brawl controls\n" +
            "\n" +
            "A / D move   SPACE jump   W at wall climb   MOUSE aim\n" +
            "LEFT: release before 8 ticks (0.13s) = punch / use weapon; SHIFT unused\n" +
            "LEFT: hold 20+ ticks (0.33s) to launch; release at 8-19 ticks cancels.\n" +
            "RIGHT CLICK picks up weapon / grabs opponent; held weapon = throw at cursor\n" +
            "Full hold auto-launches. Aim at launch. RIGHT cancels into grab / throw.\n" +
            "PAN melee   PISTOL 6 tap shots; EMPTY stays held; no reload\n" +
            "Aim upward to throw farther; weak wall bounce; settle for pickup.\n" +
            "Wall splat drops your weapon. Throwing / picking up keeps remaining ammo.\n" +
            "Holding opponent: LEFT CLICK throw. Result: O ready / X menu (online: both ready)\n" +
            "\n" +
            "F3 hitbox overlay   F4 surface mode   F5 reset round   F6 P2 input\n" +
            "[ ] dummy behavior   F7 reduce shake   F8 sync test\n" +
            "F9 start/stop input recording   F10 replay last recording\n" +
            "O/P KO P1/P2 (offline test)   CTRL+P pause   CTRL+O step   F12 debug text   ~ help";

        TextMesh text;
        SpriteRenderer panel;

        public void Setup(PaletteAsset palette, ShapeLibrary shapes, bool brawlExperiment = false)
        {
            // 경기 화면(밝은 배경) 위에 확실히 떠 보이도록 불투명 잉크색 판에 밝은 글자.
            // 반투명이면 Linear 색 공간 혼합 때문에 뒤의 발판 선이 글자 사이로 비친다.
            panel = ShapeLibrary.CreateSliced(transform, "Panel", shapes.RoundedBody, SortingLayers.UI, 200, palette.ink);
            ShapeLibrary.SetSize(panel, 25f, brawlExperiment ? 11.8f : 9.6f);
            var go = new GameObject("Text");
            go.transform.SetParent(transform, false);
            text = go.AddComponent<TextMesh>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = text.font.material;
            mr.sortingLayerName = SortingLayers.UI;
            mr.sortingOrder = 201;
            text.fontSize = 56;
            text.characterSize = 0.09f;
            text.lineSpacing = 1.15f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Left;
            text.color = PaletteAsset.ForText(palette.flash);
            text.text = brawlExperiment ? BrawlText : Text;
            transform.localPosition = new Vector3(0f, 0.5f, 0f);
        }

        public bool Visible
        {
            get => gameObject.activeSelf;
            set => gameObject.SetActive(value);
        }
    }
}
