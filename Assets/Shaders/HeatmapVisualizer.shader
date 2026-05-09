Shader "ChronoLux/HeatmapVisualizer"
{
    Properties
    {
        [NoScaleOffset] _DoseMap("Accumulated Dose Map (float)", 2D) = "black" {}
        _MinDose("Min Range", Float) = 0.0
        _MaxDose("Max Range", Float) = 100000.0
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
                if (t <= 0.0) return float3(0, 0, 0);

                float3 c0 = float3(0.0, 0.0, 0.0);   // Black
                float3 c1 = float3(0.2, 0.0, 0.5);   // Deep Purple
                float3 c2 = float3(0.8, 0.0, 0.5);   // Vibrant Magenta
                float3 c3 = float3(1.0, 0.5, 0.0);   // Orange
                float3 c4 = float3(1.0, 0.9, 0.2);   // Yellow
                
                t = saturate(t);
                if (t < 0.25) return lerp(c0, c1, t * 4.0);
                if (t < 0.50) return lerp(c1, c2, (t - 0.25) * 4.0);
                if (t < 0.75) return lerp(c2, c3, (t - 0.50) * 4.0);
                return lerp(c3, c4, (t - 0.75) * 4.0);
            }

            float4 frag(Varyings input) : SV_Target
            {
                float dose = _DoseMap.Sample(sampler_DoseMap, input.uv).r;
                float range = _MaxDose - _MinDose;
                // Epsilon-protected normalization to ensure sub-unit ranges scale correctly
                float t = saturate((dose - _MinDose) / max(1e-6, range));
                return float4(GetHeatmapColor(t), 1.0);
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
