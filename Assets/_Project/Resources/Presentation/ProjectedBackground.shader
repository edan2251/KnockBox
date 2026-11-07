// 배경과 모든 그림자가 같은 월드 좌표 색을 쓴다. 겹친 그림자는 누적해서 더 어두워지지 않는다.
Shader "Knockbox/ProjectedBackground"
{
    Properties
    {
        _BackgroundTex ("Unshadowed background", 2D) = "white" {}
        _CoverSize ("Background size", Vector) = (40, 23, 0, 0)
        _BaseLight ("Original light", Vector) = (0, 25, 0, 0)
        _LightPosition ("Moving light", Vector) = (0, 25, 0, 0)
        _HalfHeight ("Arena half height", Float) = 10
        _FalloffExponent ("Falloff", Float) = 0.7
        _ShadowStrength ("Shadow darkness", Range(0,1)) = 0
        _LightBoost ("Lit background GO pulse", Range(0,0.2)) = 0
        _Opacity ("Transition opacity", Range(0,1)) = 1
        _Visible ("Visible", Float) = 1
        _BackgroundColor ("Empty background", Color) = (0,0,0,1)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend One Zero
        Cull Off
        ZWrite Off
        Pass
        {
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            TEXTURE2D(_BackgroundTex); SAMPLER(sampler_BackgroundTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _CoverSize, _BaseLight, _LightPosition;
                float _HalfHeight, _FalloffExponent, _ShadowStrength, _Opacity, _Visible, _LightBoost;
                half4 _BackgroundColor;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 world : TEXCOORD0; };
            Varyings vert(Attributes v)
            {
                Varyings o; o.positionCS=TransformObjectToHClip(v.positionOS);
                o.world=TransformObjectToWorld(v.positionOS).xy; return o;
            }
            float Falloff(float2 p, float2 light)
            {
                float top=max(abs(light.y-_HalfHeight),.1);
                return pow(top/max(top,distance(p,light)),_FalloffExponent);
            }
            half4 frag(Varyings i) : SV_Target
            {
                clip(_Visible-.001);
                float2 uv=i.world/_CoverSize.xy+.5;
                clip(min(min(uv.x,uv.y),min(1-uv.x,1-uv.y)));
                half3 c=SAMPLE_TEXTURE2D(_BackgroundTex,sampler_BackgroundTex,uv).rgb;
                float k=Falloff(i.world,_LightPosition.xy)/max(.001,Falloff(i.world,_BaseLight.xy));
                k*=1-_ShadowStrength;
                // 원색/그림자 밝기는 유지하고, 빛 받는 배경 레이어에만 짧은 점등 과노출을 준다.
                k*=1+_LightBoost;
                #ifndef UNITY_COLORSPACE_GAMMA
                    c=SRGBToLinear(LinearToSRGB(c)*k);
                #else
                    c*=k;
                #endif
                return half4(lerp(_BackgroundColor.rgb,c,_Opacity),1);
            }
            ENDHLSL
        }
    }
}
