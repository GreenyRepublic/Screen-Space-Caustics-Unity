#ifndef WORLD_BUFFER_TOOLS
#define WORLD_BUFFER_TOOLS

#define WORLD_UNITS 4096.0f

float4 EncodeWorldSpace(float4 position)
{
	return float4(position.xyz / WORLD_UNITS, position.w);
}

float4 DecodeWorldSpace(float4 data)
{
	return float4(data.xyz * WORLD_UNITS, data.w);
}


#endif