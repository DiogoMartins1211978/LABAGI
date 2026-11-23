Shader "LABAGI/Bestiary Model Preview"
{
    Properties
    {
        _BaseColor ("Model Color", Color) = (1,1,1,1)
        [HideInInspector] _DepthOnly ("Depth Only", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _DepthOnly;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half shade = 0.22h + 0.78h * saturate(dot(normalize(input.normalWS), normalize(float3(-0.4,0.7,-0.6))));
                return half4(_BaseColor.rgb * shade, 1 - _DepthOnly);
            }
            ENDHLSL
        }
    }
}
