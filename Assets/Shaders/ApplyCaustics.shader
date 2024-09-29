Shader "Hidden/ApplyCaustics"
{
    // Applies caustics - samples from the stored texture, multiples by the diffuse gbuffer and adds to the input camera buffer

    Properties
    {
    }
    SubShader
    {
        // No culling or depth
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Blend OneMinusDstColor One // Soft additive

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            sampler2D _CameraGBufferTexture0; // Diffuse color (RGB), occlusion (A)
            sampler2D _CausticsBuffer;

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 causticValue = tex2D(_CausticsBuffer, i.uv);
                fixed4 diffuseValue = tex2D(_CameraGBufferTexture0, i.uv);

                return causticValue;
            }
            ENDCG
        }
    }
}
