using System.Collections.Generic;
using Knockbox.Presentation;
using Knockbox.Simulation;
using UnityEngine;
using UnityEngine.UI;

namespace Knockbox.Game.UI
{
    /// <summary>실제 FighterView의 스프라이트·배치·색을 UI Image로 옮긴다. 별도 얼굴이나 캡처 텍스처를 만들지 않는다.</summary>
    public sealed class FighterPortraitSource
    {
        struct Part
        {
            public string Name;
            public Sprite Sprite;
            public Vector2 Position, Size;
            public float Angle, BorderScale;
            public Color Color;
            public bool Sliced;
            public int Order;
        }
        readonly List<Part>[] parts = { new List<Part>(), new List<Part>() };

        public FighterPortraitSource(Transform owner, MatchRunner runner, MatchPresenter presenter)
        {
            var config = runner.World.Config;
            var arena = runner.World.Arena;
            var state = SimState.CreateRound(config, arena, 1u, MatchPhase.Fighting);
            var sample = new SimWorld(config, arena, state);
            for (int i = 0; i < 120; i++) sample.Step(default, default);
            var sourceRoot = new GameObject("UI_actual_FighterView_sources");
            sourceRoot.transform.SetParent(owner, false);
            for (int player = 0; player < 2; player++)
            {
                var actor = new GameObject("P" + (player + 1));
                actor.transform.SetParent(sourceRoot.transform, false);
                var view = actor.AddComponent<FighterView>();
                view.Setup(player, presenter.Palette, presenter.Shapes);
                var fighter = sample.State.GetFighter(player);
                view.Render(fighter, fighter, 1f, 0f, arena, config);
                foreach (var sr in actor.GetComponentsInChildren<SpriteRenderer>())
                {
                    if (!sr.enabled || sr.sprite == null || sr.name == "Glow" || sr.color.a <= 0) continue;
                    Vector2 size = sr.drawMode == SpriteDrawMode.Sliced ? sr.size : sr.sprite.rect.size / sr.sprite.pixelsPerUnit;
                    var scale = sr.transform.lossyScale;
                    parts[player].Add(new Part { Name = sr.name, Sprite = sr.sprite,
                        Position = (Vector2)sr.transform.position - new Vector2(fighter.Position.X,fighter.Position.Y),
                        Size = new Vector2(size.x * Mathf.Abs(scale.x),size.y * Mathf.Abs(scale.y)),
                        Angle = sr.transform.eulerAngles.z, Color = sr.color,
                        Sliced = sr.drawMode == SpriteDrawMode.Sliced, BorderScale = Mathf.Abs(scale.x),
                        Order = SortingLayer.GetLayerValueFromID(sr.sortingLayerID) * 100 + sr.sortingOrder });
                }
                parts[player].Sort((a,b) => a.Order.CompareTo(b.Order));
            }
            sourceRoot.SetActive(false);
        }

        public RectTransform Create(Transform parent, int player, Rect bounds, bool head, float angle = 0)
        {
            var root = FrontendWidgets.Rect(parent, "Actual P" + (player + 1), bounds.x, bounds.y, bounds.width, bounds.height);
            float ppu = head ? bounds.width / 1.1f : Mathf.Min(bounds.width, bounds.height) / 1.65f;
            float ox = bounds.width * .5f, oy = bounds.height * (head ? .6f : .47f);
            if (!head)
            {
                float lowest = 0;
                foreach (var p in parts[player])
                    if (p.Name == "Leg0End" || p.Name == "Leg1End") lowest = Mathf.Min(lowest, p.Position.y - p.Size.y * .5f);
                oy = bounds.height * .92f + lowest * ppu;
            }
            foreach (var p in parts[player])
            {
                if (head && p.Name != "Body" && p.Name != "Outline" && !p.Name.StartsWith("Eye")) continue;
                var image = FrontendWidgets.Image(root,p.Name,new Rect(0,0,p.Size.x*ppu,p.Size.y*ppu),p.Color,p.Sprite);
                var r = image.rectTransform;
                r.pivot = new Vector2(.5f,.5f);
                r.anchoredPosition = new Vector2(ox+p.Position.x*ppu,-oy+p.Position.y*ppu);
                r.localRotation = Quaternion.Euler(0,0,p.Angle);
                if (p.Sliced) { image.type=Image.Type.Sliced; image.pixelsPerUnitMultiplier=100f/(ppu*p.BorderScale); }
            }
            root.pivot = new Vector2(.5f,.15f);
            root.anchoredPosition += new Vector2(bounds.width*.5f,-bounds.height*.85f);
            root.localRotation = Quaternion.Euler(0,0,angle);
            return root;
        }
    }
}
