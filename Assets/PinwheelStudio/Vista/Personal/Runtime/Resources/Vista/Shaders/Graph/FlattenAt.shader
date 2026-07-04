Shader "Hidden/Vista/Graph/FlattenAt"
{
	SubShader
	{
		Tags { "RenderType" = "Opaque" }
		LOD 100

		Pass
		{
			CGPROGRAM

			#pragma vertex vert
			#pragma fragment frag

			#include "../Includes/ShaderIncludes.hlsl"
			#include MATH_HLSL

			struct v2f
			{
				float2 uv: TEXCOORD0;
				float4 vertex: SV_POSITION;
				float4 localPos: TEXCOORD1;
				float targetHeight: TEXCOORD2;
			};

			sampler2D _MainTex;
			StructuredBuffer<float2> _Vertices;
			StructuredBuffer<float2> _Texcoords;

			int _HasMaskMap;
			sampler2D _Mask;

			StructuredBuffer<float> _TargetHeights;

			v2f vert(uint id: SV_VERTEXID)
			{
				v2f o;
				float4 vertex = float4(_Vertices[id].xy, 0, 1);
				o.vertex = UnityObjectToClipPos(vertex);
				o.uv = _Texcoords[id];
				o.localPos = vertex;
				o.targetHeight = _TargetHeights[id / 6];
				return o;
			}

			float2 frag(v2f input): SV_Target
			{
				float currentHeight = tex2D(_MainTex, input.localPos).r;
				float targetHeight = input.targetHeight;
				float mask = 1;

				if (_HasMaskMap)
					mask *= tex2D(_Mask, input.uv).r;
								
				float value = lerp(currentHeight, targetHeight, mask);
				return value;
			}
			ENDCG
		}		
	}
}
