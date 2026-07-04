Shader "Hidden/Vista/Graph/GradientMap"
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

			sampler2D _MaskTex;
			sampler2D _GradientTex;
			float _Loop;

			v2f vert(appdata v)
			{
				v2f o;
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.uv = v.uv;
				o.localPos = v.vertex;
				return o;
			}

			float4 frag(v2f input): SV_Target
			{
				float mask = tex2D(_MaskTex, input.localPos).r;
				float f = mask * _Loop;
				float4 color = tex2D(_GradientTex, f.xx);
				return color;
			}
			ENDCG

		}
	}
}
