using Knockbox.Simulation;
using UnityEngine;

namespace Knockbox.Presentation
{
    /// <summary>
    /// 바위 투사체 그림(CP19): 잉크 외곽선을 두른 소유자 색 원과, 진행 반대쪽으로 늘어나는 옅은 꼬리.
    /// 칸마다 미리 만든 스프라이트를 켜고 끈다(할당 없음). 직전·현재 상태를 보간해 그린다.
    /// </summary>
    public sealed class ProjectileView : MonoBehaviour
    {
        const float OutlineScale = 1.35f;
        const float TrailSeconds = 0.05f;

        readonly SpriteRenderer[] cores = new SpriteRenderer[ProjectileState.Capacity];
        readonly SpriteRenderer[] outlines = new SpriteRenderer[ProjectileState.Capacity];
        readonly SpriteRenderer[] trails = new SpriteRenderer[ProjectileState.Capacity];
        readonly SpriteRenderer[] windups = new SpriteRenderer[2];
        readonly SpriteRenderer[] windupRings = new SpriteRenderer[2];
        PaletteAsset palette;

        public void Setup(PaletteAsset paletteAsset, ShapeLibrary shapes)
        {
            palette = paletteAsset;
            for (int i = 0; i < ProjectileState.Capacity; i++)
            {
                trails[i] = ShapeLibrary.Create(transform, $"Trail{i}", shapes.Square, SortingLayers.Fx, 0, palette.energySoft);
                outlines[i] = ShapeLibrary.Create(transform, $"Outline{i}", shapes.Circle, SortingLayers.Fx, 1, palette.ink);
                cores[i] = ShapeLibrary.Create(transform, $"Core{i}", shapes.Circle, SortingLayers.Fx, 2, palette.p1Body);
                Hide(i);
            }
            for (int i = 0; i < 2; i++)
            {
                windupRings[i] = ShapeLibrary.Create(transform, $"WindupRing{i}", shapes.Ring, SortingLayers.Fx, 3, palette.energy);
                windups[i] = ShapeLibrary.Create(transform, $"Windup{i}", shapes.Circle, SortingLayers.Fx, 4, palette.p1Body);
                windups[i].enabled = windupRings[i].enabled = false;
            }
        }

        /// <summary>
        /// 바위 선딜(CP21 폼별 선딜 구분): 몸 앞에 소유자 색 돌이 자라고 민트 고리가 조여든다. 가위(납작해지며 떨림)·보(무기를 뒤로)와 다르다.
        /// </summary>
        public void RenderWindup(in FighterState f, int i, Vector2 body, SimConfig config)
        {
            ref readonly var rock = ref config.Combat.Rock;
            bool on = f.Action == ActionState.Shoot && f.ActionTick <= rock.StartupTicks && rock.StartupTicks > 0;
            windups[i].enabled = windupRings[i].enabled = on;
            if (!on)
                return;
            float k = Mathf.Clamp01((float)f.ActionTick / rock.StartupTicks);
            var at = body + new Vector2(f.Facing * rock.SpawnOffset, 0.05f);
            float d = rock.Radius * 2f * Mathf.Lerp(0.3f, 1f, k);
            windups[i].color = palette.FighterBody(i);
            windups[i].transform.localPosition = at;
            ShapeLibrary.SetSize(windups[i], d, d);
            float ring = Mathf.Lerp(1.2f, 0.5f, k);
            windupRings[i].transform.localPosition = at;
            ShapeLibrary.SetSize(windupRings[i], ring, ring);
        }

        void Hide(int i)
        {
            cores[i].enabled = false;
            outlines[i].enabled = false;
            trails[i].enabled = false;
        }

        public void Render(in ProjectileState previous, in ProjectileState current, float alpha, float radius)
        {
            for (int i = 0; i < ProjectileState.Capacity; i++)
            {
                ref readonly var p = ref ProjectileState.At(current, i);
                if (!p.Active)
                {
                    Hide(i);
                    continue;
                }
                ref readonly var before = ref ProjectileState.At(previous, i);
                // 막 생긴 칸(직전 틱에 없었거나 다른 발사)은 보간하지 않는다.
                bool same = before.Active && before.SpawnTick == p.SpawnTick;
                var a = same ? new Vector2(before.Position.X, before.Position.Y) : new Vector2(p.Position.X, p.Position.Y);
                var pos = Vector2.Lerp(a, new Vector2(p.Position.X, p.Position.Y), alpha);
                var vel = new Vector2(p.Velocity.X, p.Velocity.Y);

                cores[i].enabled = outlines[i].enabled = trails[i].enabled = true;
                cores[i].color = palette.FighterBody(p.Owner);
                float d = radius * 2f;
                cores[i].transform.localPosition = pos;
                ShapeLibrary.SetSize(cores[i], d, d);
                outlines[i].transform.localPosition = pos;
                ShapeLibrary.SetSize(outlines[i], d * OutlineScale, d * OutlineScale);

                float length = vel.magnitude * TrailSeconds;
                var dir = length > 0.0001f ? vel.normalized : Vector2.right;
                trails[i].transform.localPosition = pos - dir * (length * 0.5f);
                trails[i].transform.localRotation = Quaternion.FromToRotation(Vector3.right, dir);
                ShapeLibrary.SetSize(trails[i], length, d * 0.6f);
            }
        }
    }
}
