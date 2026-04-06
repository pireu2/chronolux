Shader "ChronoLux/HeatmapVisualizer"
{
    Properties
    {
        [NoScaleOffset] _DoseMap("Accumulated Dose Map (float)", 2D) = "black" {}
        _MaxDose("Max Exposure Limit (Lux*Hours)", Float) = 100000.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="HDRenderPipeline" "Queue"="Geometry" }
        
        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            Cull Off ZWrite On ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #define SHADERPASS SHADERPASS_FORWARD
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            
            Texture2D<float> _DoseMap;
            SamplerState sampler_DoseMap;
            float _MaxDose;

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 posWS = mul(GetObjectToWorldMatrix(), float4(input.positionOS, 1.0)).xyz;
                output.positionCS = mul(GetWorldToHClipMatrix(), float4(posWS, 1.0));
                output.uv = input.uv;
                return output;
            }

            float3 GetHeatmapColor(float t)
            {
                float3 colorLow = float3(0.1, 0.0, 0.3);
                float3 colorMid = float3(0.8, 0.1, 0.2);
                float3 colorHigh = float3(1.0, 0.8, 0.1);
                if (t < 0.5) return lerp(colorLow, colorMid, t * 2.0);
                else return lerp(colorMid, colorHigh, saturate((t - 0.5) * 2.0));
            }

            float4 frag(Varyings input) : SV_Target
            {
                float dose = _DoseMap.Sample(sampler_DoseMap, input.uv).r;
                float normalizedDose = saturate(dose / max(1.0, _MaxDose));
                return float4(GetHeatmapColor(normalizedDose), 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthForwardOnly"
            Tags { "LightMode" = "DepthForwardOnly" }
            Cull Off ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #define SHADERPASS SHADERPASS_DEPTH_ONLY
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            struct Attributes { float3 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 posWS = mul(GetObjectToWorldMatrix(), float4(input.positionOS, 1.0)).xyz;
                output.positionCS = mul(GetWorldToHClipMatrix(), float4(posWS, 1.0));
                return output;
            }
            float4 frag(Varyings input) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    Fallback "HDRP/Unlit"
}
