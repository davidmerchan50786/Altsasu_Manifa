Shader "Hidden/Vista/Graph/CrackHelper"
{
	CGINCLUDE
	#pragma vertex vert
	#pragma fragment frag

	#include "../Includes/ShaderIncludes.hlsl"
	#include GEOMETRY_HLSL
	#include PATTERN_GENERATOR_HLSL

	struct appdata
	{
		float4 vertex: POSITION;
		float2 uv: TEXCOORD0;
	};

	struct v2f
	{
		float2 uv: TEXCOORD0;
		float4 vertex: SV_POSITION;
		float4 localPos: TEXCOORD1;
	};
	
	sampler2D _HeightMap;
	float4 _HeightMap_TexelSize;
	float3 _TerrainSize;

	v2f vert(appdata v)
	{
		v2f o;
		o.vertex = UnityObjectToClipPos(v.vertex);
		o.uv = v.uv;
		o.localPos = v.vertex;
		return o;
	}
	ENDCG

	SubShader
	{
		Tags { "RenderType" = "Opaque" }
		LOD 100

		Pass
		{
			Name "Height Mask"

			CGPROGRAM

			float _MinAngle;
			float _MaxAngle;
			float _NoiseScale;
			float2 _NoiseOffset;

			float frag(v2f input): SV_Target
			{
				float value = tex2D(_HeightMap, input.localPos).r;
				float blendSlopeValue = 1;
				float3 normal = normalFromHeightMap(_HeightMap, _HeightMap_TexelSize, _TerrainSize, input.localPos);
				float cosine = abs(normal.y);
				float slopeAngle = acos(cosine);
				float slopeTransitionFactor = (slopeAngle - _MinAngle) / (_MaxAngle - _MinAngle);
				float slopeTransition = 1 - slopeTransitionFactor;
				blendSlopeValue = (slopeAngle >= _MinAngle) * (slopeAngle <= _MaxAngle) * slopeTransition;

				float noise = perlinNoise((input.localPos.xy + _NoiseOffset) * _NoiseScale);
				noise = noise * 0.5 + 0.5;
				noise *= 0.25;

				float mask = 1 - noise * blendSlopeValue;

				return mask;
			}
			ENDCG

		}

		Pass
		{
			Name "Output Height"

			CGPROGRAM

			sampler2D _TrailMap;
			float _TrailDepth;

			float frag(v2f input): SV_Target
			{
				float height = tex2D(_HeightMap, input.localPos).r;
				float trail = tex2D(_TrailMap, input.localPos).r;

				height -= trail * _TrailDepth;
				return height;
			}
			ENDCG

		}
	}
}
