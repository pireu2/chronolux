Shader "Hidden/UVSpaceBaker"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

            float4x4 _O2W, _O2WIT;
            struct Attributes { float3 posOS : POSITION; float3 nrmOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 posCS : SV_POSITION; float3 posWS : TEXCOORD0; float3 nrmWS : TEXCOORD1; };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                float2 ndc = IN.uv * 2.0 - 1.0;
                ndc.y *= -1.0; // Flip for RT
                OUT.posCS = float4(ndc, 0.5, 1.0);
                OUT.posWS = mul(_O2W, float4(IN.posOS, 1.0)).xyz;
                OUT.nrmWS = mul((float3x3)_O2WIT, IN.nrmOS);
                return OUT;
            }

            struct FragOut { float4 pos : SV_Target0; float4 nrm : SV_Target1; };
            FragOut Frag(Varyings IN)
            {
                FragOut OUT;
                OUT.pos = float4(IN.posWS, 1.0);
                OUT.nrm = float4(normalize(IN.nrmWS), 1.0);
                return OUT;
            }
            ENDHLSL
        }
    }
}
