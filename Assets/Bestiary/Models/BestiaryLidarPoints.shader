Shader "LABAGI/Bestiary LiDAR Points"
{
    Properties
    {
        _BaseColor ("Point Color", Color) = (1,1,1,1)
        _PointSize ("Point Size (texture pixels)", Range(0.5,8)) = 2
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _PointSize;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionCS.xy += (input.uv - 0.5) * (2 * _PointSize / _ScreenParams.xy) * output.positionCS.w;
                output.uv = input.uv;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float distanceFromCenter = length(input.uv - 0.5);
                clip(0.5 - distanceFromCenter);
                return half4(_BaseColor.rgb, _BaseColor.a * saturate((0.5 - distanceFromCenter) * 12));
            }
            ENDHLSL
        }
    }
}
