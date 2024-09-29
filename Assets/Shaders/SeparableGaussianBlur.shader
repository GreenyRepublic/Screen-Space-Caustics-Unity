Shader "Hidden/SeparableGaussianBlur"
{
    // Implements a separable gaussian blur with geometry awareness
    // I.e. won't blur across large changes in normal, position, etc.
    // Source: https://venturebeat.com/2017/07/13/an-investigation-of-fast-real-time-gpu-based-image-blur-algorithms/

    Properties
    {
        [HideInInspector]_MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        // No culling or depth
        Cull Off ZWrite Off ZTest Always

        Pass
        {
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

            sampler2D _MainTex;
            sampler2D _CameraGBufferTexture0; // Diffuse color (RGB), occlusion (A)
            sampler2D _CameraGBufferTexture1; // Specular color (RGB), roughness(A).
            sampler2D _CameraGBufferTexture2; // World space normal (RGB), unused (A).
            sampler2D _CameraGBufferTexture3; // Emission + lighting + lightmaps + reflection probes buffer
            sampler2D _CameraGBufferTexture4; // Depth + stencil
            sampler2D _WorldPositionDepthTexture; // position (xyz), depth (w) (YESYES)
            sampler2D _CameraDepthTexture;

            float _GaussKernelValues[32];
            float4 _PassDirection;
            float _KernelSize;

            fixed4 frag (v2f fragData) : SV_Target
            {
                float4 weightedRadiance = float4(0, 0, 0, 1);
                float4 passStep = float4(_PassDirection.x / _ScreenParams.x, _PassDirection.y / _ScreenParams.y, 0, 1);
                float totalWeight = 0.0f;
                for (int i = -_KernelSize; i < _KernelSize; ++i)
                {
                    float4 samplePoint = float4(fragData.uv.xy, 0, 0) + (passStep * i);
                    float4 samp = tex2D(_MainTex, samplePoint.xy);
                    float weight = _GaussKernelValues[min(abs(i), _KernelSize-1)];
                    weightedRadiance += samp * weight;
                    totalWeight += weight;
                }
                if (totalWeight > 0.0f)
                {
                    return weightedRadiance / totalWeight;
                }
                else
                {
                    return tex2D(_MainTex, fragData.uv) / ((2 * _KernelSize) + 1);
                }
            }
            ENDCG
        }
    }
}
