Shader "Hidden/VK/ImposterCapture"
{
    // Assigned to a temporary copy of each source renderer during baking.
    // The baker copies the source material's _BaseMap/_BaseColor/_BumpMap onto
    // this material, then renders two passes into two atlas targets.
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _Cutoff ("Cutoff", Range(0,1)) = 0.5
        _CaptureNear ("Capture Near", Float) = 0
        _CaptureRange ("Capture Range", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        // ---- Pass 0: baked albedo (RGB) + coverage (A) ----
        Pass
        {
            Name "Albedo"
            Cull Back ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST; float4 _BaseColor; float _Cutoff; float _CaptureNear; float _CaptureRange;
            CBUFFER_END

            struct A { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };

            V vert(A IN){ V o; o.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                          o.uv = TRANSFORM_TEX(IN.uv, _BaseMap); return o; }

            half4 frag(V IN):SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
                clip(c.a - _Cutoff);
                return half4(c.rgb, 1.0);   // alpha = coverage (covered = 1)
            }
            ENDHLSL
        }

        // ---- Pass 1: object-space normal (RGB) + linear depth (A) ----
        Pass
        {
            Name "NormalDepth"
            Cull Back ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST; float4 _BaseColor; float _Cutoff; float _CaptureNear; float _CaptureRange;
            CBUFFER_END

            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; };
            struct V { float4 positionCS:SV_POSITION; float3 normalOS:TEXCOORD0; float2 uv:TEXCOORD1; float depth:TEXCOORD2; };

            V vert(A IN)
            {
                V o;
                o.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                o.normalOS = IN.normalOS;
                o.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                // linear depth along the ortho capture axis, normalized to [0,1]
                float3 posVS = TransformWorldToView(TransformObjectToWorld(IN.positionOS.xyz));
                o.depth = saturate((-posVS.z - _CaptureNear) / max(_CaptureRange, 1e-4));
                return o;
            }

            half4 frag(V IN):SV_Target
            {
                half a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).a * _BaseColor.a;
                clip(a - _Cutoff);
                float3 n = normalize(IN.normalOS) * 0.5 + 0.5;  // object-space normal encoded
                return half4(n, IN.depth);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
