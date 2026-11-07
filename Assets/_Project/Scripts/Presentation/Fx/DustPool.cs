using UnityEngine;

namespace Knockbox.Presentation
{
    /// <summary>
    /// 먼지 원(착지·점프·대시·벽 점프). 미리 만든 스프라이트를 돌려 쓰므로 실행 중 할당이 없다.
    /// 시간은 연출 시간(dt)으로 진행한다.
    /// </summary>
    public sealed class DustPool : MonoBehaviour
    {
        const int Capacity = 48;
        const float Life = 0.35f;

        struct Puff
        {
            public Vector2 Position;
            public Vector2 Velocity;
            public float Age;
            public float Size;
        }

        readonly SpriteRenderer[] renderers = new SpriteRenderer[Capacity];
        readonly Puff[] puffs = new Puff[Capacity];
        Color color;
        int next;
        FxRandom random = new FxRandom(0xD057u);

        public int ActiveCount { get; private set; }

        public void Setup(PaletteAsset palette, ShapeLibrary shapes)
        {
            color = palette.uiSub;
            for (int i = 0; i < Capacity; i++)
            {
                renderers[i] = ShapeLibrary.Create(transform, "Dust", shapes.Circle, SortingLayers.Fx, 0, color);
                renderers[i].enabled = false;
                puffs[i].Age = Life;
            }
        }

        public void Clear()
        {
            for (int i = 0; i < Capacity; i++)
            {
                puffs[i].Age = Life;
                renderers[i].enabled = false;
            }
            ActiveCount = 0;
        }

        /// <summary>count개(2~4)를 바닥을 따라 옆으로 흩뿌린다. direction은 주로 퍼질 방향(0이면 양쪽).</summary>
        public void Burst(Vector2 position, int count, float spread, float direction)
        {
            for (int i = 0; i < count; i++)
            {
                float side = direction != 0f ? Mathf.Sign(direction) : (i % 2 == 0 ? -1f : 1f);
                ref var p = ref puffs[next];
                p.Position = position + new Vector2(side * random.Range(0.05f, 0.25f), random.Range(0f, 0.08f));
                p.Velocity = new Vector2(side * spread * random.Range(0.6f, 1.2f), random.Range(0.3f, 1.1f));
                p.Size = random.Range(0.18f, 0.32f);
                p.Age = 0f;
                renderers[next].enabled = true;
                next = (next + 1) % Capacity;
            }
        }

        public void Tick(float dt)
        {
            int active = 0;
            for (int i = 0; i < Capacity; i++)
            {
                ref var p = ref puffs[i];
                if (p.Age >= Life)
                    continue;
                p.Age += dt;
                if (p.Age >= Life)
                {
                    renderers[i].enabled = false;
                    continue;
                }
                p.Velocity *= Mathf.Exp(-5f * dt);
                p.Position += p.Velocity * dt;
                float t = p.Age / Life;
                float size = p.Size * (1f - t * 0.6f);
                renderers[i].transform.localPosition = new Vector3(p.Position.x, p.Position.y, 0f);
                renderers[i].transform.localScale = new Vector3(size, size, 1f) / renderers[i].sprite.bounds.size.x;
                var c = color;
                c.a = 1f - t;
                renderers[i].color = c;
                active++;
            }
            ActiveCount = active;
        }
    }
}
