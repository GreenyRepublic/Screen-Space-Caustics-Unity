// Unity built-in shader source. Copyright (c) 2016 Unity Technologies. MIT license (see license.txt)

Shader "WorldPositionDepthTexture" {
Properties {
}

SubShader {
     Tags { "RenderType" = "Opaque" "Queue" = "Transparent" }
     Blend One Zero
     ZTest LEqual 
     Cull Back
     LOD 200
    Pass {
CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#include "UnityCG.cginc"
#include "cginc/WorldSpaceBufferTools.cginc"
struct v2f {
    float4 pos : SV_POSITION;
    float4 nz : TEXCOORD0;
    float4 worldPos : TEXCOORD1;
};
v2f vert( appdata_base v ) {
    v2f o;
    o.worldPos = mul(unity_ObjectToWorld, v.vertex);
    o.pos = UnityObjectToClipPos(v.vertex);
    o.nz.xyz = COMPUTE_VIEW_NORMAL;
    o.nz.w = COMPUTE_DEPTH_01;
    return o;
}
fixed4 frag(v2f i) : SV_Target{
    float4 output = float4(i.worldPos.xyz, i.nz.w);
    return EncodeWorldSpace(output);
}
ENDCG
    }
}
FallBack "Diffuse"
}
