Shader "Custom/LiDAR_dots"
{
    Properties
    {
        _NearColor   ("Near Color", Color) = (0.2, 1, 0.6, 1)
        _FarColor    ("Far Color", Color) = (0.1, 0.4, 1, 1)
        _GridSize    ("Dot Spacing (world units)", Float) = 0.15
        _DotRadius   ("Dot Radius (world units)", Float) = 0.025
        _Jitter      ("Random Jitter", Range(0, 1)) = 0.8
        _FadeStart   ("Dropout Start (0-1 of range)", Range(0, 1)) = 0.4
        _SweepSpeed  ("Sweep Speed", Float) = 0.3
        _SweepWidth  ("Sweep Width", Float) = 0.8
        _SweepBoost  ("Sweep Brightness", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "Unlit"
            Tags { "LightMode"="UniversalForward" }
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // Set globally by LidarOrigin.cs
            float4 _LidarOrigin; // xyz = player position
            float  _LidarRange;

            CBUFFER_START(UnityPerMaterial)
                half4 _NearColor;
                half4 _FarColor;
                float _GridSize;
                float _DotRadius;
                float _Jitter;
                float _FadeStart;
                float _SweepSpeed;
                float _SweepWidth;
                float _SweepBoost;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings  { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 wp   = IN.positionWS;
                float  dist = distance(wp, _LidarOrigin.xyz);

                // Outside the scan range: nothing is drawn
                clip(_LidarRange - dist);

                // Triplanar-style projection: pick the 2 axes facing the surface
                float3 n  = abs(normalize(IN.normalWS));
                float2 uv = (n.x > n.y && n.x > n.z) ? wp.yz :
                            (n.y > n.z)              ? wp.xz : wp.xy;

                // Dot grid with random jitter
                float2 g     = uv / _GridSize;
                float2 cell  = floor(g);
                float2 local = frac(g) - 0.5;
                float2 off   = (float2(hash21(cell), hash21(cell + 17.0)) - 0.5) * _Jitter * 0.7;
                float  d     = length(local - off) * _GridSize;

                // Far away, randomly drop dots so the cloud thins out
                float t    = saturate(dist / _LidarRange);
                float keep = 1.0 - smoothstep(_FadeStart, 1.0, t);

                // Not a dot: paint black (still writes depth, so it hides what's behind)
                bool isDot = (d < _DotRadius) && (keep > hash21(cell + 91.0));
                if (!isDot) return half4(0, 0, 0, 1);

                // Colour by distance + a pulse wave moving outward from the player
                half3 col = lerp(_NearColor.rgb, _FarColor.rgb, t);
                float wave = frac(_Time.y * _SweepSpeed) * _LidarRange;
                col += col * exp(-abs(dist - wave) / _SweepWidth) * _SweepBoost;

                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}