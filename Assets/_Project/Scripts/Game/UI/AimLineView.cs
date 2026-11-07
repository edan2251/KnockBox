using Knockbox.Presentation;
using Knockbox.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Knockbox.Game
{
    /// <summary>
    /// CR-004: 내 선수의 몸 중심에서 마우스 커서 쪽으로 점선 조준선을 그린다. 입력과 같은 64방향 양자화를 쓴다.
    /// 실험 경기에서만, 마우스가 있을 때만 보인다.
    /// </summary>
    public sealed class AimLineView : MonoBehaviour
    {
        const int Dots = 6;
        const float Start = 0.75f;
        const float Spacing = 0.32f;
        const float DotSize = 0.11f;
        const int SortingOrder = 40;

        [SerializeField] SpriteRenderer[] dots = new SpriteRenderer[Dots];
        PaletteAsset palette;

        public void Setup(PaletteAsset palette, ShapeLibrary shapes)
        {
            this.palette = palette;
            for (int i = 0; i < Dots; i++)
            {
                var color = palette.p1Body;
                color.a = Mathf.Lerp(0.9f, 0.35f, i / (Dots - 1f));
                if(dots[i] == null) dots[i] = ShapeLibrary.Create(transform, $"AimDot{i}", shapes.Circle, SortingLayers.UI, SortingOrder, color);
                dots[i].enabled=true;
                dots[i].transform.localScale = new Vector3(DotSize, DotSize, 1f);
            }
        }

        /// <summary>보간된 내 선수 위치(연출과 같은 비율)에서 커서 방향으로 점을 놓고 슬롯 색상을 적용한다.</summary>
        public void Render(MatchRunner runner, float alpha)
        {
            var cam = Camera.main;
            var mouse = Mouse.current;
            bool show = runner.BrawlExperiment && cam != null && mouse != null && runner.World != null;
            gameObject.SetActive(show);
            if (!show)
                return;

            int slot = runner.LocalPlayerSlot;
            var a = runner.PreviousState.GetFighter(slot).Position;
            var b = runner.World.State.GetFighter(slot).Position;
            var center = Vector2.Lerp(new Vector2(a.X, a.Y), new Vector2(b.X, b.Y), alpha);
            byte aim = AimInput.FromScreen(cam, mouse.position.ReadValue(), center);
            var dir = Vector2.right;
            if (AimDirections.TryGet(aim, out var d))
                dir = new Vector2(d.X, d.Y);
            for (int i = 0; i < Dots; i++)
            {
                dots[i].transform.localPosition = center + dir * (Start + Spacing * i);
                dots[i].color = PaletteAsset.WithAlpha(palette.FighterBody(slot), Mathf.Lerp(0.9f, 0.35f, i / (Dots - 1f)));
            }
        }
    }
}
