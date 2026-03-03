// Renders the mesh in UV space so the hardware rasteriser interpolates
// world-space position and normal per pixel, written to two MRT outputs.
Shader "Hidden/UVSpaceBaker"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }

        Pass
        {
            Cull Off        // bake both front and back faces
            ZWrite Off      // no depth needed
            ZTest Always    // never discard a fragment

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag
            #pragma target   4.5

            float4x4 _O2W;    // object → world (positions)
            float4x4 _O2WIT;  // inverse-transpose of _O2W (normals)

            struct Attributes
            {
                float3 posOS : POSITION;
                float3 nrmOS : NORMAL;
                float2 uv    : TEXCOORD0;
            };

            struct Varyings
            {
                float4 posCS : SV_POSITION;
                float3 posWS : TEXCOORD0;
                float3 nrmWS : TEXCOORD1;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;

                // UV [0,1] → NDC [-1,+1]. Flip Y because DX render targets
                // have row 0 at the top while UV y=0 is at the bottom.
                float2 ndc = IN.uv * 2.0 - 1.0;
                ndc.y      = -ndc.y;
                OUT.posCS  = float4(ndc, 0.0, 1.0);

                OUT.posWS = mul(_O2W, float4(IN.posOS, 1.0)).xyz;
                OUT.nrmWS = normalize(mul((float3x3)_O2WIT, IN.nrmOS));

                return OUT;
            }

            struct FragOut
            {
                float4 position : SV_Target0; // PositionMap: RGB=worldXYZ, A=1
                float4 normal   : SV_Target1; // NormalMap:   RGB=worldNRM, A=1
            };

            FragOut Frag(Varyings IN)
            {
                FragOut OUT;
                OUT.position = float4(IN.posWS, 1.0);
                OUT.normal   = float4(normalize(IN.nrmWS), 1.0);
                return OUT;
            }
            ENDHLSL
        }
    }
}
