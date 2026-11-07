// 3D 시험 외형(CR-004)용 무광 셰이더(URP 2D 렌더러). 텍스처 × 색에 화면 왼쪽 위 빛의 부드러운 음영을 조금 더한다.
// 2D 렌더러에서 3D 메시가 자기 자신을 가리도록 깊이를 쓴다(앞뒤 순서는 깊이로, 다른 그림과의 순서는 정렬 레이어로).
Shader "Knockbox/ModelUnlit"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _Shade ("Shade", Range(0, 1)) = 0.3
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        ZWrite On
        ZTest LEqual
        Cull Back

        Pass
        {
            Tags { "LightMode" = "Universal2D" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _Shade;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 normalWS : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                half3 light = normalize(half3(-0.35, 0.8, -0.5));
                half ndl = saturate(dot(normalize(i.normalWS), light) * 0.5 + 0.5);
                c.rgb *= lerp(1.0 - _Shade, 1.0 + _Shade * 0.2, ndl);
                return half4(c.rgb, 1);
            }
            ENDHLSL
        }
    }
}
