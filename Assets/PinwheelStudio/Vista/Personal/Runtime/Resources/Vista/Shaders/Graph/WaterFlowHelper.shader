Shader "Hidden/Vista/Graph/WaterFlowHelper"
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

			float2 frag(v2f i): SV_Target
			{
				float h = tex2D(_HeightMap, i.localPos).r * _Bounds.y;
				float2 color = float2(h, 0);

				return color;
			}
			ENDCG

		}
		Pass //output
		{
			CGPROGRAM

			#pragma vertex vert
			#pragma fragment frag
			
			sampler2D _WorldData;
			float4 _WorldData_TexelSize;

			float frag(v2f i): SV_Target
			{
				float waterLevel = tex2DBicubic(_WorldData, _WorldData_TexelSize.zw, i.localPos).g;
				return waterLevel / _Bounds.y;
			}
			ENDCG

		}
	}
}