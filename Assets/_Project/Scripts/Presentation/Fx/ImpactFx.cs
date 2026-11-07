using UnityEngine;

namespace Knockbox.Presentation
{
    /// <summary>
    /// 타격 연출(05-presentation.md 4절)의 입자들: 삼각 파편, 불꽃 줄기, 확산 고리, 클래시 별, 벽꽝 금 자국, 화면 번쩍임.
    /// 모두 미리 만든 스프라이트를 돌려 쓰므로 실행 중 할당이 없다. 시간은 연출 시간(dt)으로 진행한다.
    /// 지형 파편은 연출 물리 대신 단순 포물선 파편으로 그린다(연출 물리는 CP8의 KO 파편에서).
    /// </summary>
    public sealed class ImpactFx : MonoBehaviour
    {
        const int ShardCapacity = 96;
        const int RingCapacity = 12;
        const int GlowCapacity = 10;
        const int RayCapacity = 48;
        const int CrackCapacity = 6;
        const int CrackLines = 7;
        const float ShardGravity = 22f;
        const float RingLife = 0.2f;
        const float CrackLife = 1.5f;
        const float CrackFade = 0.6f;
        const float CrackThickness = 0.05f;
        const float FlashFrame = 1f / 60f;

        struct Shard
        {
            public Vector2 Position;
            public Vector2 Velocity;
            public float Angle;
            public float Spin;
            public float Age;
            public float Life;
            public float Size;
            public float Gravity;
            public bool Streak;
            public Color Color;
        }

        struct Ring
        {
            public Vector2 Position;
            public float From;
            public float To;
            public float Age;
            public float Life;
            public Color Color;
            public float Alpha;
        }

        struct Ray
        {
            public Vector2 Position;
            public float Angle;
            public float Length;
            public float Age;
            public float Life;
        }

        struct CrackMark
        {
            public float Age;
        }

        readonly SpriteRenderer[] shardRenderers = new SpriteRenderer[ShardCapacity];
        readonly SpriteRenderer[] streakRenderers = new SpriteRenderer[ShardCapacity];
        readonly Shard[] shards = new Shard[ShardCapacity];
        readonly SpriteRenderer[] ringRenderers = new SpriteRenderer[RingCapacity];
        readonly Ring[] rings = new Ring[RingCapacity];
        readonly SpriteRenderer[] glowRenderers = new SpriteRenderer[GlowCapacity];
        readonly Ring[] glows = new Ring[GlowCapacity];
        int nextGlow;
        readonly SpriteRenderer[] rayRenderers = new SpriteRenderer[RayCapacity];
        readonly Ray[] rays = new Ray[RayCapacity];
        readonly SpriteRenderer[] crackRenderers = new SpriteRenderer[CrackCapacity * CrackLines];
        readonly CrackMark[] cracks = new CrackMark[CrackCapacity];
        SpriteRenderer flash;
        PaletteAsset palette;
        FxRandom random = new FxRandom(0x1A7Cu);
        int nextShard, nextRing, nextRay, nextCrack;
        float flashAge = 1f;
        bool flashInvert;
        bool flashShown;

        public int ActiveCount { get; private set; }

        public void Setup(PaletteAsset paletteAsset, ShapeLibrary shapes)
        {
            palette = paletteAsset;
            for (int i = 0; i < ShardCapacity; i++)
            {
                shardRenderers[i] = ShapeLibrary.Create(transform, "Shard", shapes.Triangle, SortingLayers.Fx, 5, palette.flash);
                shardRenderers[i].enabled = false;
                streakRenderers[i] = ShapeLibrary.Create(transform, "Spark", shapes.Square, SortingLayers.Fx, 12, palette.energy);
                streakRenderers[i].enabled = false;
                shards[i].Age = shards[i].Life = 1f;
            }
            for (int i = 0; i < RingCapacity; i++)
            {
                ringRenderers[i] = ShapeLibrary.Create(transform, "Ring", shapes.Ring, SortingLayers.Fx, 10, palette.flash);
                ringRenderers[i].enabled = false;
                rings[i].Age = rings[i].Life = 1f;
            }
            for (int i = 0; i < GlowCapacity; i++)
            {
                glowRenderers[i] = ShapeLibrary.Create(transform, "Glow", shapes.SoftCircle, SortingLayers.Fx, 1, palette.flash);
                glowRenderers[i].enabled = false;
                glows[i].Age = glows[i].Life = 1f;
            }
            for (int i = 0; i < RayCapacity; i++)
            {
                rayRenderers[i] = ShapeLibrary.Create(transform, "Ray", shapes.Square, SortingLayers.Fx, 14, palette.flash);
                rayRenderers[i].enabled = false;
                rays[i].Age = rays[i].Life = 1f;
            }
            for (int i = 0; i < crackRenderers.Length; i++)
            {
                // 지형 몸체가 잉크색이라 금은 밝은 그림자색으로 지형 위에 그린다.
                crackRenderers[i] = ShapeLibrary.Create(transform, "Crack", shapes.Square, SortingLayers.Terrain, 20, palette.bgBase);
                crackRenderers[i].enabled = false;
            }
            for (int i = 0; i < CrackCapacity; i++)
                cracks[i].Age = CrackLife;
            flash = ShapeLibrary.Create(transform, "ScreenFlash", shapes.Square, SortingLayers.Fx, 100, palette.flash);
            ShapeLibrary.SetSize(flash, 120f, 80f);
            flash.enabled = false;
        }

        /// <summary>남은 입자를 모두 지운다(라운드·시나리오 시작).</summary>
        public void Clear()
        {
            for (int i = 0; i < ShardCapacity; i++)
            {
                shards[i].Age = shards[i].Life;
                shardRenderers[i].enabled = false;
                streakRenderers[i].enabled = false;
            }
            for (int i = 0; i < RingCapacity; i++)
            {
                rings[i].Age = rings[i].Life;
                ringRenderers[i].enabled = false;
            }
            for (int i = 0; i < GlowCapacity; i++)
            {
                glows[i].Age = glows[i].Life;
                glowRenderers[i].enabled = false;
            }
            for (int i = 0; i < RayCapacity; i++)
            {
                rays[i].Age = rays[i].Life;
                rayRenderers[i].enabled = false;
            }
            for (int i = 0; i < CrackCapacity; i++)
                cracks[i].Age = CrackLife;
            for (int i = 0; i < crackRenderers.Length; i++)
                crackRenderers[i].enabled = false;
            flash.enabled = false;
            ActiveCount = 0;
        }

        /// <summary>삼각 파편 count개를 direction 둘레 spreadDegrees 안으로 흩뿌린다.</summary>
        public void Shards(Vector2 position, Vector2 direction, float spreadDegrees, int count, Color color, float speed, float size, float life = 0.45f)
        {
            float baseAngle = direction.sqrMagnitude > 1e-6f ? Mathf.Atan2(direction.y, direction.x) : Mathf.PI * 0.5f;
            for (int i = 0; i < count; i++)
            {
                float a = baseAngle + random.Range(-0.5f, 0.5f) * spreadDegrees * Mathf.Deg2Rad;
                ref var s = ref shards[nextShard];
                s.Position = position;
                s.Velocity = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * speed * random.Range(0.55f, 1.15f);
                s.Angle = random.Range(0f, 360f);
                s.Spin = random.Range(-900f, 900f);
                s.Age = 0f;
                s.Life = life * random.Range(0.75f, 1.2f);
                s.Size = size * random.Range(0.6f, 1.2f);
                s.Gravity = ShardGravity;
                s.Streak = false;
                s.Color = color;
                Activate(nextShard);
                nextShard = (nextShard + 1) % ShardCapacity;
            }
        }

        /// <summary>불꽃 줄기(속도 방향으로 늘어난 가는 막대)를 사방으로.</summary>
        public void Sparks(Vector2 position, int count, float speed)
        {
            for (int i = 0; i < count; i++)
            {
                float a = (i + random.Range(0f, 0.8f)) / count * Mathf.PI * 2f;
                ref var s = ref shards[nextShard];
                s.Position = position;
                s.Velocity = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * speed * random.Range(0.6f, 1.2f);
                s.Angle = 0f;
                s.Spin = 0f;
                s.Age = 0f;
                s.Life = random.Range(0.14f, 0.24f);
                s.Size = random.Range(0.05f, 0.08f);
                s.Gravity = 6f;
                s.Streak = true;
                s.Color = palette.energy;
                Activate(nextShard);
                nextShard = (nextShard + 1) % ShardCapacity;
            }
        }

        void Activate(int index)
        {
            bool streak = shards[index].Streak;
            shardRenderers[index].enabled = !streak;
            streakRenderers[index].enabled = streak;
        }

        /// <summary>확산 고리(지름 from → to).</summary>
        public void RingBurst(Vector2 position, Color color, float from, float to, float life = RingLife)
        {
            ref var r = ref rings[nextRing];
            r.Position = position;
            r.From = from;
            r.To = to;
            r.Age = 0f;
            r.Life = life;
            r.Color = color;
            ringRenderers[nextRing].enabled = true;
            nextRing = (nextRing + 1) % RingCapacity;
        }

        /// <summary>
        /// 밝은 연출 둘레의 부드러운 빛 번짐(08 5절의 블룸 대신). 지름 size의 70%에서 size로 커지며 alpha에서 사라진다.
        /// 지형은 번지지 않고 이 연출만 번진다.
        /// </summary>
        public void Glow(Vector2 position, Color color, float size, float alpha, float life)
        {
            ref var g = ref glows[nextGlow];
            g.Position = position;
            g.From = size * 0.7f;
            g.To = size;
            g.Age = 0f;
            g.Life = life;
            g.Color = color;
            g.Alpha = alpha;
            glowRenderers[nextGlow].enabled = true;
            nextGlow = (nextGlow + 1) % GlowCapacity;
        }

        /// <summary>클래시의 별 모양 폭발: 접점에서 뻗는 빛살 count개.</summary>
        public void Star(Vector2 position, int count, float length)
        {
            float offset = random.Range(0f, 180f);
            for (int i = 0; i < count; i++)
            {
                ref var r = ref rays[nextRay];
                r.Position = position;
                r.Angle = offset + i * 360f / count;
                r.Length = length * (i % 2 == 0 ? 1f : 0.55f);
                r.Age = 0f;
                r.Life = 0.16f;
                rayRenderers[nextRay].enabled = true;
                nextRay = (nextRay + 1) % RayCapacity;
            }
        }

        /// <summary>벽꽝 금 자국: 접점에서 지형 안쪽으로 갈라지는 선들. 1.5초 동안 사라진다.</summary>
        public void Crack(Vector2 point, Vector2 normal)
        {
            int c = nextCrack;
            nextCrack = (nextCrack + 1) % CrackCapacity;
            cracks[c].Age = 0f;
            // 지형 안쪽(법선 반대)과 면을 따라가는 방향 사이로 퍼진다.
            float inward = Mathf.Atan2(-normal.y, -normal.x) * Mathf.Rad2Deg;
            for (int i = 0; i < CrackLines; i++)
            {
                var sr = crackRenderers[c * CrackLines + i];
                float angle = inward + Mathf.Lerp(-80f, 80f, (i + random.Range(0.1f, 0.9f)) / CrackLines);
                float length = random.Range(0.25f, 0.75f) * (i % 2 == 0 ? 1f : 0.6f);
                var dir = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
                sr.transform.localPosition = point + dir * (length * 0.5f);
                sr.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
                ShapeLibrary.SetSize(sr, length, CrackThickness * random.Range(0.7f, 1.3f));
                sr.enabled = true;
            }
        }

        /// <summary>화면 전체 번쩍임. invert면 먼저 잉크색으로 한 프레임 눌렀다가 흰색 한 프레임(색 반전 느낌, 패링).</summary>
        public void ScreenFlash(bool invert)
        {
            flashAge = 0f;
            flashInvert = invert;
            flashShown = false;
            flash.enabled = true;
        }

        public void Tick(float dt, Vector2 cameraCenter)
        {
            int active = 0;
            for (int i = 0; i < ShardCapacity; i++)
            {
                ref var s = ref shards[i];
                if (s.Age >= s.Life)
                    continue;
                s.Age += dt;
                var sr = s.Streak ? streakRenderers[i] : shardRenderers[i];
                if (s.Age >= s.Life)
                {
                    sr.enabled = false;
                    continue;
                }
                s.Velocity.y -= s.Gravity * dt;
                s.Velocity *= Mathf.Exp(-2.5f * dt);
                s.Position += s.Velocity * dt;
                s.Angle += s.Spin * dt;
                float t = s.Age / s.Life;
                sr.transform.localPosition = s.Position;
                if (s.Streak)
                {
                    float length = Mathf.Clamp(s.Velocity.magnitude * 0.035f, 0.08f, 0.5f) * (1f - t);
                    sr.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(s.Velocity.y, s.Velocity.x) * Mathf.Rad2Deg);
                    ShapeLibrary.SetSize(sr, Mathf.Max(length, 0.01f), s.Size);
                }
                else
                {
                    sr.transform.localRotation = Quaternion.Euler(0f, 0f, s.Angle);
                    float size = s.Size * (1f - t * 0.5f);
                    ShapeLibrary.SetSize(sr, size, size);
                }
                var color = s.Color;
                color.a = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
                sr.color = color;
                active++;
            }

            for (int i = 0; i < GlowCapacity; i++)
            {
                ref var g = ref glows[i];
                if (g.Age >= g.Life)
                    continue;
                g.Age += dt;
                if (g.Age >= g.Life)
                {
                    glowRenderers[i].enabled = false;
                    continue;
                }
                float t = g.Age / g.Life;
                float size = Mathf.Lerp(g.From, g.To, 1f - (1f - t) * (1f - t));
                glowRenderers[i].transform.localPosition = g.Position;
                ShapeLibrary.SetSize(glowRenderers[i], size, size);
                glowRenderers[i].color = PaletteAsset.WithAlpha(g.Color, g.Alpha * (1f - t) * (1f - t));
                active++;
            }

            for (int i = 0; i < RingCapacity; i++)
            {
                ref var r = ref rings[i];
                if (r.Age >= r.Life)
                    continue;
                r.Age += dt;
                if (r.Age >= r.Life)
                {
                    ringRenderers[i].enabled = false;
                    continue;
                }
                float t = r.Age / r.Life;
                float eased = 1f - (1f - t) * (1f - t);
                float size = Mathf.Lerp(r.From, r.To, eased);
                ringRenderers[i].transform.localPosition = r.Position;
                ShapeLibrary.SetSize(ringRenderers[i], size, size);
                var color = r.Color;
                color.a = 1f - t;
                ringRenderers[i].color = color;
                active++;
            }

            for (int i = 0; i < RayCapacity; i++)
            {
                ref var r = ref rays[i];
                if (r.Age >= r.Life)
                    continue;
                r.Age += dt;
                if (r.Age >= r.Life)
                {
                    rayRenderers[i].enabled = false;
                    continue;
                }
                float t = r.Age / r.Life;
                float length = r.Length * (t < 0.3f ? t / 0.3f : 1f - (t - 0.3f) / 0.7f * 0.6f);
                var dir = new Vector2(Mathf.Cos(r.Angle * Mathf.Deg2Rad), Mathf.Sin(r.Angle * Mathf.Deg2Rad));
                rayRenderers[i].transform.localPosition = r.Position + dir * (length * 0.5f + 0.05f);
                rayRenderers[i].transform.localRotation = Quaternion.Euler(0f, 0f, r.Angle);
                ShapeLibrary.SetSize(rayRenderers[i], Mathf.Max(length, 0.01f), 0.09f * (1f - t * 0.5f));
                active++;
            }

            for (int c = 0; c < CrackCapacity; c++)
            {
                if (cracks[c].Age >= CrackLife)
                    continue;
                cracks[c].Age += dt;
                bool alive = cracks[c].Age < CrackLife;
                float alpha = Mathf.Clamp01((CrackLife - cracks[c].Age) / CrackFade);
                for (int i = 0; i < CrackLines; i++)
                {
                    var sr = crackRenderers[c * CrackLines + i];
                    sr.enabled = alive;
                    var color = palette.bgBase;
                    color.a = alpha;
                    sr.color = color;
                }
                if (alive)
                    active++;
            }

            // 시간이 흐르지 않은 프레임(일시정지, 캡처 직전)에는 번쩍임을 진행하지 않는다.
            if (flash.enabled && (dt > 0f || !flashShown))
            {
                flashShown = true;
                // 60Hz 기준 두 프레임: 색 반전 느낌이면 잉크 → 흰색, 아니면 흰색 한 프레임.
                float frames = flashInvert ? 2f : 1f;
                if (flashAge >= frames * FlashFrame - 1e-4f)
                {
                    flash.enabled = false;
                }
                else
                {
                    bool dark = flashInvert && flashAge < FlashFrame - 1e-4f;
                    var color = dark ? palette.ink : palette.flash;
                    color.a = dark ? 0.6f : 0.55f;
                    flash.color = color;
                    flash.transform.localPosition = cameraCenter;
                }
                flashAge += dt;
            }
            ActiveCount = active;
        }
    }
}
