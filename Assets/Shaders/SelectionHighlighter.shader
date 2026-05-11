Shader "ChronoLux/SelectionHighlighter"
{
    Properties
    {
        _GlowColor("Glow Color", Color) = (0, 0.5, 1, 1)
        _Intensity("Intensity", Float) = 2.0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="HDRenderPipeline" }
        
        Pass
        {
            Blend One One // Additive
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            struct Attributes { float3 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 viewDirWS : TEXCOORD1; };

            float4 _GlowColor;
            float _Intensity;

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 posWS = mul(GetObjectToWorldMatrix(), float4(input.positionOS, 1.0)).xyz;
                // Slightly expand the mesh to make it look like a "Glow"
                posWS += mul((float3x3)GetObjectToWorldMatrix(), input.normalOS) * 0.01;
                
                output.positionCS = mul(GetWorldToHClipMatrix(), float4(posWS, 1.0));
                output.normalWS = mul((float3x3)GetObjectToWorldMatrix(), input.normalOS);
                output.viewDirWS = GetWorldSpaceNormalizeViewDir(posWS);
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                // Fresnel rim effect for that "scientific scanner" look
                float rim = 1.0 - saturate(dot(normalize(input.normalWS), normalize(input.viewDirWS)));
                rim = pow(rim, 3.0);
                
                return float4(_GlowColor.rgb * rim * _Intensity, 1.0);
            }
            ENDHLSL
        }
    }
}
