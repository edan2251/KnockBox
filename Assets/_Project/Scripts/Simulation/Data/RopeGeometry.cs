namespace Knockbox.Simulation
{
    /// <summary>고정 8분할 처짐 곡선. 연결부 판정과 표시가 같은 좌표를 사용한다. 줄 자체의 강체는 만들지 않는다.</summary>
    public static class RopeGeometry
    {
        public const int Segments = 8;

        public static float Sag(Vec2 start, Vec2 end, float length)
        {
            if (length <= SimMath.Sqrt((end - start).LengthSquared) + .001f) return 0f;
            float low = 0f, high = length * 2f;
            for (int i = 0; i < 10; i++)
            {
                float sag = (low + high) * .5f, measured = 0f;
                var previous = start;
                for (int j = 1; j <= Segments; j++)
                {
                    var point = Curve(start, end, sag, (float)j / Segments);
                    measured += SimMath.Sqrt((point - previous).LengthSquared);
                    previous = point;
                }
                if (measured < length) low = sag; else high = sag;
            }
            return (low + high) * .5f;
        }

        public static Vec2 Curve(Vec2 start, Vec2 end, float sag, float t) =>
            start + (end - start) * t + new Vec2(0f, -4f * t * (1f - t) * sag);

        /// <summary>보이는 선분 위의 위치. 곡선 수식의 점과 8분할 선분의 차이를 없앤다.</summary>
        public static Vec2 Point(Vec2 start, Vec2 end, float length, float t)
        {
            float sag = Sag(start, end, length);
            float scaled = SimMath.Clamp(t, 0f, 1f) * Segments;
            int segment = SimMath.Min((int)scaled, Segments - 1);
            var a = Curve(start, end, sag, (float)segment / Segments);
            var b = Curve(start, end, sag, (float)(segment + 1) / Segments);
            return a + (b - a) * (scaled - segment);
        }

        public static Vec2 Connector(in PropJoint joint, Vec2 position, Vec2 rotation)
        {
            var local = joint.LocalAnchor;
            var end = position + new Vec2(rotation.X * local.X - rotation.Y * local.Y,
                rotation.Y * local.X + rotation.X * local.Y);
            return Point(joint.WorldAnchor, end, joint.Length, joint.ConnectorPosition);
        }
    }
}
