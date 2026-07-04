Shader "Hidden/Vista/Graph/LandslideHelper"
{
	CGINCLUDE
	#include "../Includes/ShaderIncludes.hlsl"
	#include SAMPLING_HLSL

	struct appdata
	{
		float4 vertex: POSITION;
		float2 uv: TEXCOORD0;
	};

	struct v2f
	{
		float2 uv: TEXCOORD0;
		float4 vertex: SV_POSITION;
		float3 localPos: TEXCOORD1;
	};

	float4 _Bounds;

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

		Pass //init
		{
			CGPROGRAM

			#pragma vertex vert
			#pragma fragment frag
			
			sampler2D _HeightMap;
			sampler2D _MaskMap;
			float _Intensity;

			float2 frag(v2f i): SV_Target
			{
				float worldHeight = tex2D(_HeightMap, i.localPos).r * _Bounds.y;
				float mask = tex2D(_MaskMap, i.localPos).r;
				float soil = _Intensity * mask;
				float2 color = float2(worldHeight - soil, soil); //height, soil

				return color;
			}
			ENDCG

		}
		Pass //output height
		{
			CGPROGRAM

			#pragma vertex vert
			#pragma fragment frag
			
			sampler2D _HeightMap;
			float4 _HeightMap_TexelSize;
			sampler2D _MaskMap;
			float4 _MaskMap_TexelSize;
			sampler2D _WorldData;
			float4 _WorldData_TexelSize;
			float _Intensity;

			float frag(v2f i): SV_Target
			{
				float currentHeight = tex2DBicubic(_HeightMap, _HeightMap_TexelSize.zw, i.localPos).r;
				float mask = tex2DBicubic(_MaskMap, _MaskMap_TexelSize.zw, i.localPos).r;
				float erodedSoil = _Intensity * mask;

				float depositSoil = tex2DBicubic(_WorldData, _WorldData_TexelSize.zw, i.localPos).g;
				float h = currentHeight - erodedSoil / _Bounds.y + depositSoil / _Bounds.y;
				return h;
			}
			ENDCG

		}
		Pass //output soil
		{
			CGPROGRAM

			#pragma vertex vert
			#pragma fragment frag
			
			sampler2D _WorldData;
			float4 _WorldData_TexelSize;

			float frag(v2f i): SV_Target
			{
				float2 data = tex2DBicubic(_WorldData, _WorldData_TexelSize.zw, i.localPos).rg;
				float s = data.g / _Bounds.y;
				return s;
			}
			ENDCG

		}
	}
}