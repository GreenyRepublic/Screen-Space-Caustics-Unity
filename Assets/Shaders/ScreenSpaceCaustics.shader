
Shader "SSCaustics/ScreenSpaceCaustics"
{
    Properties
    {
        _SamplesPerPixel ("Samples per Pixel", Integer) = 8
        _SampleDistance ("Sample Distance", Integer) = 128
        _CausticStrength ("Brightness Multiplier", Float) = 1.0
    }
    SubShader
    {
    Tags { "RenderType" = "Opaque" "Queue" = "Transparent" }
    Cull Off 
    ZWrite Off 
    ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityGBuffer.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            
            #define SPEC_THRESHOLD 0.9f

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
                VertexPositionInputs vertexInput = GetVertexPositionInputs(v.vertex.xyz);
                o.vertex = vertexInput.positionCS;
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

            //  As presented in Hachisuka's 2012 paper on GPU-accelerated photon mapping
            //  Originally taken from an anonymous forum post on GPGPU.com (weird right?)
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
            int _SamplesPerPixel;
            int _SampleDistance;
            float4 _LightPosition;
            float4 _LightColour;

            sampler2D _GBuffer0; // Diffuse color (RGB), Material Flags (A)
            sampler2D _GBuffer1; // Metallic (RGB), Occlusion(A).
            sampler2D _GBuffer2; // World space normal (RGB), Smoothness (A).
            sampler2D _GBuffer3; // Emission + lighting + lightmaps + reflection probes buffer

            float4 frag(v2f i) : SV_Target
            {
                float4 randState = float4(
                    i.uv.y * _ScreenParams.y,
                    i.uv.x * _ScreenParams.x,
                    i.uv.x * _ScreenParams.x,
                    i.uv.y * _ScreenParams.y);


                float2 nrs = float2(i.uv.x * _ScreenParams.x * _RandomSeed.x, i.uv.y * _ScreenParams.y * _RandomSeed.y) ;

                
                float receiverDepth = SampleSceneDepth(i.uv.xy);
                float4 receiverDiffuse = tex2D(_GBuffer0, i.uv.xy);
                float4 receiverSpecular = tex2D(_GBuffer1, i.uv.xy);
                float4 receiverNormal = tex2D(_GBuffer2, i.uv.xy);
                float3 receiverPosition = ComputeWorldSpacePosition(i.uv.xy, receiverDepth, unity_MatrixInvVP);
                
                float4 receivedRadiance = float4(0, 0, 0, 0);

                float3 cameraRay = receiverPosition.xyz - GetCameraPositionWS().xyz;
                float3 normCameraRay = SafeNormalize(cameraRay.xyz);
                float contributingSamples = 0.0f;
                
                BRDFData receiverBRDF = BRDFDataFromGbuffer(receiverDiffuse, receiverSpecular, receiverNormal);
                   
                //  Scale our sampling distance by the camera distance
                //
                _SampleDistance = _SampleDistance * (1/length(cameraRay));

                [unroll(64)]
                for (int smp = 0; smp < _SamplesPerPixel; ++smp)
                {
                    //  Generate a random vector in the unit sphere, invert it if it's in the wrong half-space
                    //float angleRandomFactor = halton(2, smp + 1) + (0.3 * rand_2_10(i.uv.xy * (smp + 1)));

                    float rand1 = rand_2_10(i.uv.xy * (smp + 1));
                    float rand2 = rand_2_10(i.uv.yx * (smp + 2));
                    float rand3 = rand_2_10(i.uv.xy * (smp + 5));

                    float3 sampleVecWS = normalize(float3(rand1, rand2, rand3));
                    if (dot(sampleVecWS,receiverNormal) < 0.0f)
                    {
                        sampleVecWS *= -1;
                    }
                    float2 sampleVecCS = normalize(TransformWorldToHClip(sampleVecWS).xy);
                    
                    float distanceRandomFactor = rand_2_10(i.uv.yx * (smp+7));
                    float sampleDistance = sqrt(distanceRandomFactor) * _SampleDistance;
                    float2 sampleOffset = sampleVecCS * (sampleDistance/_ScreenParams.x);
                    
                    float4 sampleCoord = float4(i.uv.xy + sampleOffset, 0, 1);

                    float3 senderPosition = ComputeWorldSpacePosition(sampleCoord, receiverDepth, unity_MatrixInvVP);
                    float4 senderDiffuse = tex2Dlod(_GBuffer0, sampleCoord);
                    float4 senderSpecular = tex2Dlod(_GBuffer1, sampleCoord);
                    float4 senderNormal = tex2Dlod(_GBuffer2, sampleCoord);
                    
                    float senderSmoothness = senderNormal.a;
                    
                    if (senderSmoothness < SPEC_THRESHOLD)
                    {
                        continue;
                    }
                    
                    float3 receiverToSender = senderPosition.xyz - receiverPosition.xyz;
                    float distanceSquared = max(length(receiverToSender) * length(receiverToSender), 1.0f);
                    receiverToSender = normalize(receiverToSender);

                    float3 correctedReceiverNormal = SafeNormalize(receiverNormal.xyz);
                    float3 correctedSenderNormal = SafeNormalize(senderNormal.xyz);

                    if (dot(receiverToSender, correctedSenderNormal) >= 0.0f)
                    {
                        continue;
                    }
                    
                    float3 reflectedRay = SafeNormalize(reflect(-receiverToSender, correctedSenderNormal));
                    
                    BRDFData senderBRDF = BRDFDataFromGbuffer(senderDiffuse, senderSpecular, senderNormal);
                    //half3 IBLIrradiance = CalculateIrradianceFromReflectionProbes(reflectedRay, senderPosition, senderBRDF.perceptualRoughness);

                    half3 IBLIrradiance = (half3)0;
                    float3 incomingRadiance = LightingPhysicallyBased(
                        senderBRDF, 
                        IBLIrradiance, 
                        normalize(reflectedRay), 
                        1.0f/distanceSquared, 
                        correctedSenderNormal, 
                        normalize(-receiverToSender),
                        false);

                    float3 outgoingRadiance = LightingPhysicallyBased(
                        receiverBRDF, 
                        incomingRadiance, 
                        normalize(receiverToSender), 
                        1.0f, 
                        correctedReceiverNormal, 
                        -normCameraRay,
                        false);

                    receivedRadiance.xyz += outgoingRadiance;
                    contributingSamples += 1.0f;
                    
                }
                receivedRadiance.xyz /= contributingSamples;
                receivedRadiance.a = 1;
                return receivedRadiance * _CausticStrength;
            }
            ENDHLSL
        }
    }
}
