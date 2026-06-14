Shader "VK/Imposter"
{
    Properties
    {
        [MainTexture] _AlbedoAtlas ("Albedo + Coverage", 2D) = "white" {}
        _NormalDepthAtlas ("Normal + Depth", 2D) = "bump" {}

        _Frames ("Frames Per Axis", Float) = 12
        _ImposterSize ("Imposter Size (2*radius)", Float) = 1
        _Pivot ("Pivot (local)", Vector) = (0,0,0,0)

        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        _Parallax ("Parallax Strength", Range(0,1)) = 0.25

        [Toggle(_IMPOSTER_HEMI)]      _Hemi ("Hemisphere Only", Float) = 1
        [Toggle(_IMPOSTER_PARALLAX)]  _Par  ("Depth Parallax", Float) = 1
        [Toggle(_IMPOSTER_LIT)]       _Lit  ("Receive Main Light", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            Cull Off            // billboard can be seen from either winding
            ZWrite On
            AlphaToMask On      // MSAA coverage = soft silhouette without sorting

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            #pragma shader_feature_local _IMPOSTER_HEMI
            #pragma shader_feature_local _IMPOSTER_PARALLAX
            #pragma shader_feature_local _IMPOSTER_LIT

            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Imposter.hlsl"

            TEXTURE2D(_AlbedoAtlas);       SAMPLER(sampler_AlbedoAtlas);
            TEXTURE2D(_NormalDepthAtlas);  SAMPLER(sampler_NormalDepthAtlas);

            CBUFFER_START(UnityPerMaterial)
                float  _Frames;
                float  _ImposterSize;
                float4 _Pivot;
                float  _Cutoff;
                float  _Parallax;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;   // quad in [-0.5,0.5]^2 on XY plane
                float2 uv         : TEXCOORD0;   // [0,1]
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                // three frame uvs packed: f0.xy, f1.xy, f2.xy
                float4 uv01   : TEXCOORD0;       // (f0.xy, f1.xy)
                float4 uv2w   : TEXCOORD1;       // (f2.xy, w0, w1)
                float3 weights: TEXCOORD2;       // (w0, w1, w2) — redundant w0/w1 kept for clarity
                // per-frame tangent-space view dirs for parallax: packed as xy each
                float4 par01  : TEXCOORD3;       // (vTS0.xy, vTS1.xy)
                float2 par2   : TEXCOORD4;       // (vTS2.xy)
                float3 normalWS : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Map an integer grid cell -> octa uv -> direction.
            float3 CellDir(float2 cell)
            {
                float2 uv = cell / (_Frames - 1.0);
                return VK_OctaDecode(uv);
            }

            // Project a billboard-plane offset P (object space, relative to pivot)
            // onto frame f's basis to get that frame's local uv, then atlas uv.
            // Also returns the current view dir expressed in the frame's tangent space.
            float2 FrameUV(float2 cell, float3 P, float3 viewDirOS, float3 upRef,
                           out float2 vTS_xy)
            {
                float3 dir = CellDir(cell);
                float3 r, u;
                VK_FrameBasis(dir, upRef, r, u);

                float2 local = float2(dot(P, r), dot(P, u)) / _ImposterSize + 0.5;
                vTS_xy = float2(dot(viewDirOS, r), dot(viewDirOS, u));

                cell = clamp(cell, 0.0, _Frames - 1.0);
                return (cell + saturate(local)) / _Frames;
            }

            Varyings vert (Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float3 pivotOS = _Pivot.xyz;
                float3 camOS   = mul(GetWorldToObjectMatrix(), float4(_WorldSpaceCameraPos, 1)).xyz;
                float3 viewDirOS = normalize(camOS - pivotOS);

                // Stable up reference (avoid degenerate cross near the pole).
                float3 upRef = abs(viewDirOS.y) > 0.99 ? float3(0,0,1) : float3(0,1,0);

                // Camera-facing billboard basis in object space.
                float3 bRight, bUp;
                VK_FrameBasis(viewDirOS, upRef, bRight, bUp);

                float2 q = (IN.uv - 0.5) * _ImposterSize;
                float3 P = bRight * q.x + bUp * q.y;          // offset on billboard plane
                float3 posOS = pivotOS + P;
                OUT.positionCS = TransformObjectToHClip(posOS);

                // ---- frame selection (triangular barycentric) ----
                float2 grid = VK_OctaEncode(viewDirOS) * (_Frames - 1.0);
                float2 cell = floor(grid);
                float2 f    = frac(grid);

                float2 c0, c1, c2;
                float3 w;
                if (f.x + f.y > 1.0)
                {
                    // upper triangle
                    c0 = cell + float2(1,1);
                    c1 = cell + float2(1,0);
                    c2 = cell + float2(0,1);
                    w  = float3(f.x + f.y - 1.0, 1.0 - f.y, 1.0 - f.x);
                }
                else
                {
                    // lower triangle
                    c0 = cell + float2(0,0);
                    c1 = cell + float2(1,0);
                    c2 = cell + float2(0,1);
                    w  = float3(1.0 - f.x - f.y, f.x, f.y);
                }

                float2 p0, p1, p2;
                float2 uv0 = FrameUV(c0, P, viewDirOS, upRef, p0);
                float2 uv1 = FrameUV(c1, P, viewDirOS, upRef, p1);
                float2 uv2 = FrameUV(c2, P, viewDirOS, upRef, p2);

                OUT.uv01 = float4(uv0, uv1);
                OUT.uv2w = float4(uv2, w.x, w.y);
                OUT.weights = w;
                OUT.par01 = float4(p0, p1);
                OUT.par2  = p2;

                OUT.normalWS = TransformObjectToWorldNormal(viewDirOS);
                return OUT;
            }

            // Sample one frame, optionally parallax-corrected by its baked depth.
            float4 SampleFrame(float2 uv, float2 parTS, out float depth)
            {
                depth = 0.5;
            #ifdef _IMPOSTER_PARALLAX
                // single-step parallax: read depth, shift toward/away along view.
                float4 nd = SAMPLE_TEXTURE2D(_NormalDepthAtlas, sampler_NormalDepthAtlas, uv);
                depth = nd.a;
                float2 offset = parTS * (depth - 0.5) * _Parallax / _Frames;
                uv += offset;
            #endif
                return SAMPLE_TEXTURE2D(_AlbedoAtlas, sampler_AlbedoAtlas, uv);
            }

            half4 frag (Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                float3 w = IN.weights;

                float d0, d1, d2;
                float4 a0 = SampleFrame(IN.uv01.xy, IN.par01.xy, d0);
                float4 a1 = SampleFrame(IN.uv01.zw, IN.par01.zw, d1);
                float4 a2 = SampleFrame(IN.uv2w.xy, IN.par2,    d2);

                // Weighted blend of color and coverage.
                float coverage = a0.a * w.x + a1.a * w.y + a2.a * w.z;
                clip(coverage - _Cutoff);

                float3 color = (a0.rgb * a0.a * w.x +
                                a1.rgb * a1.a * w.y +
                                a2.rgb * a2.a * w.z) / max(coverage, 1e-4);

            #ifdef _IMPOSTER_LIT
                // Cheap response to the main directional light using object-space
                // baked normal from the dominant (highest weight) frame.
                float2 nuv = w.x >= w.y && w.x >= w.z ? IN.uv01.xy
                           : (w.y >= w.z ? IN.uv01.zw : IN.uv2w.xy);
                float3 nOS = SAMPLE_TEXTURE2D(_NormalDepthAtlas, sampler_NormalDepthAtlas, nuv).rgb * 2 - 1;
                float3 nWS = normalize(TransformObjectToWorldNormal(nOS));
                Light mainLight = GetMainLight();
                float ndotl = saturate(dot(nWS, mainLight.direction)) * 0.5 + 0.5; // wrap
                color *= mainLight.color * ndotl + unity_AmbientSky.rgb;
            #endif

                return half4(color, coverage);
            }
            ENDHLSL
        }

        // Depth/shadow caster so imposters cast and receive shadows correctly.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual Cull Off
            AlphaToMask On

            HLSLPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #pragma target 3.5
            #pragma shader_feature_local _IMPOSTER_HEMI
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Imposter.hlsl"

            TEXTURE2D(_AlbedoAtlas); SAMPLER(sampler_AlbedoAtlas);
            CBUFFER_START(UnityPerMaterial)
                float  _Frames; float _ImposterSize; float4 _Pivot; float _Cutoff; float _Parallax;
            CBUFFER_END

            struct A { float3 positionOS:POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };

            V vertShadow (A IN)
            {
                V OUT = (V)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                float3 pivotOS = _Pivot.xyz;
                float3 camOS = mul(GetWorldToObjectMatrix(), float4(_WorldSpaceCameraPos,1)).xyz;
                float3 viewDirOS = normalize(camOS - pivotOS);
                float3 upRef = abs(viewDirOS.y) > 0.99 ? float3(0,0,1) : float3(0,1,0);
                float3 r,u; VK_FrameBasis(viewDirOS, upRef, r, u);
                float2 q = (IN.uv - 0.5) * _ImposterSize;
                float3 posOS = pivotOS + r*q.x + u*q.y;
                OUT.positionCS = TransformObjectToHClip(posOS);

                float2 grid = VK_OctaEncode(viewDirOS) * (_Frames - 1.0);
                float2 cell = clamp(floor(grid + 0.5), 0.0, _Frames - 1.0);
                OUT.uv = (cell + IN.uv) / _Frames;   // nearest frame is fine for shadows
                return OUT;
            }

            half4 fragShadow (V IN) : SV_Target
            {
                float a = SAMPLE_TEXTURE2D(_AlbedoAtlas, sampler_AlbedoAtlas, IN.uv).a;
                clip(a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
