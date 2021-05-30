
Shader "Hidden/ScreenSpaceCaustics"
{
    // It's GreenyRepublic's caustic shader test!
    Properties
    {
    }
    SubShader
    {
    Tags { "RenderType" = "Opaque" "Queue" = "Transparent" }
    Cull Off 
    ZWrite Off 
    ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityStandardBRDF.cginc"
            #include "UnityCG.cginc"
            #include "UnityStandardUtils.cginc"
            #include "./cginc/Halton.cginc"
            #include "cginc/WorldSpaceBufferTools.cginc"

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


            // Little modulo helper since HLSL doesn't give us one
            float mod(float x, float y)
            {
                int intPart; 
                modf(x / y, intPart);
                return (x - y) / intPart;
            }

            float4 vectorMod(float4 a, float4 b)
            {
                return float4(
                    mod(a.r, b.r),
                    mod(a.g, b.g),
                    mod(a.b, b.b),
                    mod(a.a, b.a)
                    );
            }

            //As presented in Hachisuka's 2012 paper on GPU-accelerated photon mapping
            //Originally taken from an anonymous forum post on GPGPU.
            float GPURnd(inout float4 state)
            {
                const float4 q = float4(1225.0, 1585.0, 2457.0, 2098.0);
                const float4 r = float4(1112.0, 367.0, 92.0, 265.0);
                const float4 a = float4(3423.0, 2646.0, 1707.0, 1999.0);
                const float4 m = float4(4194287.0, 4194277.0, 4194191.0, 4194167.0);

                float4 beta = floor(state / q);
                float4  p = a * (state - beta * q) - beta * r;
                beta = (sign(-p) + float4 (1.0f, 1.0f, 1.0f, 1.0f)) * float4 (0.5f, 0.5f, 0.5f, 0.5f) * m;
                state = (p + beta);

                return frac(dot(state / m, float4(1.0, -1.0, 1.0, -1.0)));
            }

            float rand_1_05(in float2 uv)
            {
                float2 noise = (frac(sin(dot(uv, float2(12.9898, 78.233) * 2.0)) * 43758.5453));
                return abs(noise.x + noise.y) * 0.5;
            }

            float2 rand_2_10(in float2 uv) {
                float noiseX = (frac(sin(dot(uv, float2(12.9898, 78.233) * 2.0)) * 43758.5453));
                float noiseY = sqrt(1 - noiseX * noiseX);
                return float2(noiseX, noiseY);
            }

            float2 rand_2_0004(in float2 uv)
            {
                float noiseX = (frac(sin(dot(uv, float2(12.9898, 78.233))) * 43758.5453));
                float noiseY = (frac(sin(dot(uv, float2(12.9898, 78.233) * 2.0)) * 43758.5453));
                return float2(noiseX, noiseY) * 0.004;
            }

            float roughnessToPhongGloss(float roughness)
            {
                return pow(2.0 - roughness, 4.0f);
            }

            float4 convertNormals(float4 normals)
            {
                return normals - float4(0.5, 0.5, 0.5, 0.0);
            }

            float4 _RandomSeed;
            bool _LightDirectional; // is light directional? light is point light if false
            float _CausticStrength;
            uint _SampleCount;
            uint _SampleDistance;
            float4 _LightPosition;
            float4 _LightColour;

            sampler2D _CameraGBufferTexture0; // Diffuse color (RGB), occlusion (A)
            sampler2D _CameraGBufferTexture1; // Specular color (RGB), roughness(A).
            sampler2D _CameraGBufferTexture2; // World space normal (RGB), unused (A).
            sampler2D _CameraGBufferTexture3; // Emission + lighting + lightmaps + reflection probes buffer
            sampler2D _CameraGBufferTexture4; // Depth + stencil
            sampler2D _WorldPositionDepthTexture; // position (xyz), depth (w) (YESYES)
            sampler2D _CameraDepthTexture;

            fixed4 frag(v2f i) : SV_Target
            {
                float4 randState = float4(
                    i.uv.y * _ScreenParams.y,
                    i.uv.x * _ScreenParams.x,
                    i.uv.x * _ScreenParams.x,
                    i.uv.y * _ScreenParams.y);

                float2 nrs = float2(i.uv.x * _ScreenParams.x * _RandomSeed.x, i.uv.y * _ScreenParams.y * _RandomSeed.y) ;

                float4 receiverPosition = DecodeWorldSpace(tex2D(_WorldPositionDepthTexture, i.uv));
                float4 receiverDiffuse = tex2D(_CameraGBufferTexture0, i.uv);
                float4 receiverSpecularSmoothness = tex2D(_CameraGBufferTexture1, i.uv);
                float4 receiverNormal = tex2D(_CameraGBufferTexture2, i.uv);

                float4 receivedRadiance = float4(0, 0, 0, 0);

                if (receiverDiffuse.a == 0.0f || length(receiverDiffuse.rgb) < 0.1f)
                {
                    return receivedRadiance;
                }

                float3 cameraRay = receiverPosition.xyz - _WorldSpaceCameraPos.xyz;
                float3 normCameraRay = normalize(cameraRay.xyz);
                float contributingSamples = 0.0f;


                UnityIndirect gi;
                gi.diffuse = float3(0,0,0);
                gi.specular = float3(0,0,0);

                [unroll(64)]
                for (int smp = 0; smp < _SampleCount; ++smp)
                {
                    float sampleAngle = rand_2_10(i.uv.xy * (smp + 1)) * UNITY_TWO_PI;
                    float sampleDistance = rand_2_10(i.uv.xy * (smp+3)) * _SampleDistance;

                    float4 sampleCoord = float4(i.uv.xy, 0,0) + 
                        float4((sampleDistance * cos(sampleAngle)) / _ScreenParams.x
                        , (sampleDistance * sin(sampleAngle)) / _ScreenParams.y
                        , 0 , 0);


                    float4 senderPosition = DecodeWorldSpace(tex2Dlod(_WorldPositionDepthTexture, sampleCoord));
                    float4 senderDiffuse = tex2Dlod(_CameraGBufferTexture0, sampleCoord);
                    float4 senderSpecular = tex2Dlod(_CameraGBufferTexture1, sampleCoord);
                    float4 senderNormal = tex2Dlod(_CameraGBufferTexture2, sampleCoord);

                    if (senderDiffuse.a == 0.0f)
                    {
                        continue;
                    }

                    if (SpecularStrength(senderSpecular.xyz) < 0.5f)
                    {
                        continue;
                    }

                    float3 receiverToSender = senderPosition.xyz - receiverPosition.xyz;
                    float distanceFalloffSquared = max(dot(receiverToSender, receiverToSender), 1.0f);
                    receiverToSender = normalize(receiverToSender);
                    float3 correctedReceiverNormal = normalize(receiverNormal.xyz - float3(0.5,0.5,0.5));
                    float3 correctedSenderNormal = normalize(senderNormal.xyz - float3(0.5, 0.5, 0.5));

                    if (dot(receiverToSender, correctedSenderNormal) >= 0.0f)
                    {
                        continue;
                    }

                    float3 reflectedRay = normalize(reflect(-receiverToSender, correctedSenderNormal));
                    half4 skyData = UNITY_SAMPLE_TEXCUBE(unity_SpecCube0, -reflectedRay);
                    half3 skyColour = DecodeHDR(skyData, unity_SpecCube0_HDR);
                    
                    UnityLight light;
                    light.color = skyColour.xyz;
                    light.dir = -reflectedRay;

                    float4 incomingRadiance = BRDF1_Unity_PBS(
                        senderDiffuse.xyz,
                        senderSpecular.xyz,
                        1.0f - SpecularStrength(senderSpecular.rgb),
                        1.0f - senderSpecular.w,
                        correctedSenderNormal.xyz,
                        normalize(-receiverToSender),
                        light,
                        gi
                    );
                    float lambertianTerm = dot(normalize(receiverToSender), normalize(correctedReceiverNormal.xyz));
                    receivedRadiance += incomingRadiance * lambertianTerm * (1.0 / distanceFalloffSquared);
                    contributingSamples += 1.0f;
                }
                receivedRadiance.xyz /= contributingSamples;
                receivedRadiance.a = 1;
                return receivedRadiance * _CausticStrength;
            }
            ENDCG
        }
    }
}
