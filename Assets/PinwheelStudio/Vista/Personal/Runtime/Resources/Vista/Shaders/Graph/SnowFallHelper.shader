Shader "Hidden/Vista/Graph/SnowFallHelper"
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

		Pass //copy height
		{
			CGPROGRAM

			#pragma vertex vert
			#pragma fragment frag
			
			sampler2D _HeightMap;
			sampler2D _SnowMask;

			float2 frag(v2f i): SV_Target
			{
				float h = tex2D(_HeightMap, i.localPos).r * _Bounds.y;
				float s = tex2D(_SnowMask, i.localPos).r;
				float2 color = float2(h, 0); //height, snow

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
			sampler2D _WorldData;
			float4 _WorldData_TexelSize;

			float frag(v2f i): SV_Target
			{
				float currentHeight = tex2DBicubic(_HeightMap, _HeightMap_TexelSize.zw, i.localPos).r;
				float s = tex2DBicubic(_WorldData, _WorldData_TexelSize.zw, i.localPos).g;
				float h = currentHeight + s / _Bounds.y;
				return h;
			}
			ENDCG

		}Pass //output snow
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