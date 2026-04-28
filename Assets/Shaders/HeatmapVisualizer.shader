Shader "ChronoLux/HeatmapVisualizer"
{
    Properties
    {
        [NoScaleOffset] _DoseMap("Accumulated Dose Map (float)", 2D) = "black" {}
        _MinDose("Min Exposure Range (Lux*Hours)", Float) = 0.0
        _MaxDose("Max Exposure Range (Lux*Hours)", Float) = 5000000.0
        _UseLogScale("Use Logarithmic Scaling (0 or 1)", Float) = 0
        _ShadowVisibility("Shadow Visibility", Float) = 0.2
        _CriticalLimit("Critical Damage Limit", Float) = 10000000.0
        _CriticalColor("Critical Limit Color", Color) = (1, 1, 1, 1)
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
            float _MinDose;
            float _MaxDose;
            float _UseLogScale;
            float _ShadowVisibility;
            float _CriticalLimit;
            float4 _CriticalColor;

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
                // Pure Black for zero dose (with slight ambient for detail)
                if (t <= 0.0) return float3(0.01, 0.01, 0.02) * _ShadowVisibility;

                float3 colorLow = float3(0.1, 0.0, 0.3);  // Purple
                float3 colorMid = float3(0.8, 0.1, 0.2);  // Red
                float3 colorHigh = float3(1.0, 0.8, 0.1); // Yellow
                
                t = saturate(t);
                if (t < 0.5) return lerp(colorLow, colorMid, t * 2.0);
                else return lerp(colorMid, colorHigh, saturate((t - 0.5) * 2.0));
            }

            float4 frag(Varyings input) : SV_Target
            {
                float dose = _DoseMap.Sample(sampler_DoseMap, input.uv).r;
                
                if (dose > _CriticalLimit) return _CriticalColor;

                float t = 0.0;
                // Use a float check instead of keyword for PropertyBlock compatibility
                if (_UseLogScale > 0.5)
                {
                    float logMin = log10(max(1.0, _MinDose));
                    float logMax = log10(max(10.0, _MaxDose));
                    float logDose = log10(max(1.0, dose));
                    t = (logDose - logMin) / max(0.001, logMax - logMin);
                }
                else
                {
                    t = (dose - _MinDose) / max(1.0, _MaxDose - _MinDose);
                }
                
                float3 col = GetHeatmapColor(t);
                
                // Add ambient detail to shadows so it's not a flat block
                if (dose < 1.0) col += float3(0.05, 0.05, 0.05) * _ShadowVisibility;

                return float4(col, 1.0);
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
