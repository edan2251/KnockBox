// CR-004 단계 S2: 외형 후보 R3. 부위를 거리장(SDF)으로 합쳐 한 번에 그린다. 물체 좌표 = 인형 좌표(원점 = 판정 상자 중심).
// 부위 최대 16개: _A[i] = 캡슐이면 (a.x, a.y, b.x, b.y), 둥근 사각형이면 (중심 x, y, 반폭, 반높이).
// _R[i] = (반지름 a 또는 모서리 반지름, 반지름 b 또는 회전(라디안), 겹 번호, 모양 0 캡슐 / 1 둥근 사각형).
// 겹: 0 몸통, 1 앞 팔, 2 뒤 팔, 3 다리, 4 머리. 몸통·머리·다리는 서로 조금씩 녹여 붙이고, 팔은 몸과 녹이지 않는다.
// 외곽선은 전체 실루엣에만 그리고, 앞 팔과 몸의 구분과 뒤쪽 부위는 명암(빛 방향 음영, 가장자리 어둡게, 앞 팔이 드리우는 그림자)으로 한다.
Shader "Knockbox/SdfDoll"
{
    Properties
    {
        _Fill ("Fill", Color) = (1, 0.83, 0.4, 1)
        _Ink ("Ink", Color) = (0.01, 0.01, 0.02, 1)
        _Outline ("Outline", Float) = 0.045
        _Count ("Count", Float) = 0
        _Light ("Light (local)", Vector) = (-0.3, 0.95, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            Tags { "LightMode" = "Universal2D" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            half4 _Fill;
            half4 _Ink;
            float _Outline;
            float _Count;
            float4 _Light;
            CBUFFER_END
            float4 _A[16];
            float4 _R[16];

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 local : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.local = v.positionOS.xy;
                return o;
            }

            float Capsule(float2 p, float2 a, float2 b, float ra, float rb)
            {
                float2 pa = p - a;
                float2 ba = b - a;
                float h = saturate(dot(pa, ba) / max(dot(ba, ba), 1e-6));
                return length(pa - ba * h) - lerp(ra, rb, h);
            }

            float RoundBox(float2 p, float2 center, float2 halfSize, float radius, float angle)
            {
                float c = cos(angle), s = sin(angle);
                float2 q = p - center;
                q = float2(c * q.x + s * q.y, -s * q.x + c * q.y);
                float2 d = abs(q) - (halfSize - radius);
                return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0) - radius;
            }

            float SmoothMin(float a, float b, float k)
            {
                float h = saturate(0.5 + 0.5 * (b - a) / k);
                return lerp(b, a, h) - k * h * (1.0 - h);
            }

            float Part(float2 p, int n)
            {
                return _R[n].w > 0.5
                    ? RoundBox(p, _A[n].xy, _A[n].zw, _R[n].x, _R[n].y)
                    : Capsule(p, _A[n].xy, _A[n].zw, _R[n].x, _R[n].y);
            }

            // 겹마다 거리(팔·다리 마디끼리는 매끄럽게 합친다).
            void Groups(float2 p, out float torso, out float front, out float back, out float legs, out float head)
            {
                torso = 1e3; front = 1e3; back = 1e3; legs = 1e3; head = 1e3;
                int count = (int)_Count;
                [loop] for (int n = 0; n < 16; n++)
                {
                    if (n >= count) break;
                    float d = Part(p, n);
                    int g = (int)(_R[n].z + 0.5);
                    if (g == 0) torso = SmoothMin(torso, d, 0.04);
                    else if (g == 1) front = SmoothMin(front, d, 0.05);
                    else if (g == 2) back = SmoothMin(back, d, 0.05);
                    else if (g == 3) legs = SmoothMin(legs, d, 0.05);
                    else head = SmoothMin(head, d, 0.04);
                }
            }

            float Body(float torso, float legs, float head)
            {
                return SmoothMin(SmoothMin(torso, head, 0.06), legs, 0.05);
            }

            // 겹 하나의 거리(명암 법선용).
            float Layer(float2 p, int which)
            {
                float t, f, b, l, h;
                Groups(p, t, f, b, l, h);
                return which == 1 ? f : which == 2 ? b : Body(t, l, h);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.local;
                float torso, front, back, legs, head;
                Groups(p, torso, front, back, legs, head);
                float body = Body(torso, legs, head);
                float silhouette = min(min(body, front), back);
                float w = max(fwidth(silhouette), 1e-4);
                float alpha = 1.0 - smoothstep(-w, w, silhouette);
                if (alpha <= 0.0)
                    discard;

                // 보이는 겹: 앞 팔 > 몸 > 뒤 팔.
                int which = front < 0.0 ? 1 : body < 0.0 ? 0 : 2;
                float d = which == 1 ? front : which == 0 ? body : back;
                const float e = 0.01;
                float2 grad = float2(Layer(p + float2(e, 0), which) - Layer(p - float2(e, 0), which),
                                     Layer(p + float2(0, e), which) - Layer(p - float2(0, e), which));
                float2 n = normalize(grad + 1e-6);
                // 가운데는 빛을 정면으로, 가장자리로 갈수록 법선 방향으로 기운 둥근 덩어리처럼.
                float edge = saturate(-d / 0.12);
                float3 normal3 = normalize(float3(n * (1.0 - edge), edge + 0.35));
                float3 light = normalize(float3(_Light.xy, 0.9));
                float lit = saturate(dot(normal3, light));
                half3 color = _Fill.rgb * lerp(0.62, 1.05, lit);
                if (which == 2)
                    color *= 0.68;
                // 앞 팔이 몸 위에 드리우는 부드러운 그림자, 몸이 뒤 팔 위에 드리우는 그림자.
                if (which == 0)
                    color *= lerp(0.72, 1.0, saturate(front / 0.08));
                if (which == 2)
                    color *= lerp(0.75, 1.0, saturate(body / 0.06));
                // 실루엣 외곽선(전체 덩어리 바깥에만).
                float ink = 1.0 - smoothstep(-w, w, silhouette + _Outline);
                half3 final = lerp(_Ink.rgb, color, ink);
                return half4(final, alpha * _Fill.a);
            }
            ENDHLSL
        }
    }
}
