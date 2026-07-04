Shader "Hidden/Vista/Graph/GeometryMask"
{
	SubShader
	{
		Tags { "RenderType" = "Opaque" }
		LOD 100
		ColorMask R

		Pass
		{
			CGPROGRAM

			#pragma vertex vert
			#pragma fragment frag
			//#pragma shader_feature_local BLEND_MAX
			//#pragma shader_feature_local HEIGHT_MASK
			//#pragma shader_feature_local SLOPE_MASK
			//#pragma shader_feature_local DIRECTION_MASK

			#include "../Includes/ShaderIncludes.hlsl"
			#include MATH_HLSL
			#include GEOMETRY_HLSL

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

			sampler2D _MainTex;
			float4 _MainTex_TexelSize;
			float3 _TerrainSize;

			int _UseHeightMask;
			float _MinHeight;
			float _MaxHeight;
			sampler2D _HeightTransition;
			
			int _UseSlopeMask;
			float _MinAngle;
			float _MaxAngle;
			sampler2D _SlopeTransition;

			int _UseDirectionMask;
			float _DirectionAngle;
			float _DirectionTolerance;
			sampler2D _DirectionFalloff;

			int _BlendMax;

			v2f vert(appdata v)
			{
				v2f o;
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.uv = v.uv;
				o.localPos = v.vertex;
				return o;
			}

			float frag(v2f input): SV_Target
			{
				float h = tex2D(_MainTex, input.localPos).r;
				float mask;
				if (_BlendMax)
					mask = 0;
				else
					mask = 1;

				if (_UseHeightMask)
				{
					float fHeightMin = _MinHeight / _TerrainSize.y;
					float fHeightMax = _MaxHeight / _TerrainSize.y;
					float fHeight = saturate(inverseLerp(h, fHeightMin, fHeightMax));
					float heightTransition = tex2D(_HeightTransition, float2(fHeight, 0.5)).r;
					float heightMask = (h >= fHeightMin) * (h <= fHeightMax) * heightTransition;
					if (_BlendMax)
						mask = max(mask, heightMask);
					else
						mask = mask * heightMask;
				}

				float3 normal = float3(0, 1, 0);
				if (_UseSlopeMask || _UseDirectionMask)
				{
					normal = normalFromHeightMap(_MainTex, _MainTex_TexelSize, _TerrainSize, input.localPos);
				}

				if (_UseSlopeMask)
				{
					float cosine = abs(normal.y);
					float slopeAngle = acos(cosine);
					float slopeTransitionFactor = (slopeAngle - _MinAngle) / (_MaxAngle - _MinAngle);
					float slopeTransition = tex2D(_SlopeTransition, float2(slopeTransitionFactor, 0.5f)).r;
					float slopeMask = (slopeAngle >= _MinAngle) * (slopeAngle <= _MaxAngle) * slopeTransition;
					if (_BlendMax)
						mask = max(mask, slopeMask);
					else
						mask = mask * slopeMask;
				}

				if (_UseDirectionMask)
				{
					float2 dir = normalize(normal.xz);
					float rad = atan2(dir.y, dir.x);
					float deg = (rad >= 0) * (rad * 57.2958) + (rad < 0) * (359 + rad * 57.2958);
					float minAngle = (_DirectionAngle - _DirectionTolerance * 0.5);
					float maxAngle = (_DirectionAngle + _DirectionTolerance * 0.5);

					float deg0 = (deg + 180) % 360;
					float minAngle0 = (minAngle + 180) % 360;
					float maxAngle0 = (maxAngle + 180) % 360;
					float v0 = deg0 > minAngle0 && deg0 <= maxAngle0;
					float f0 = (1 - abs(inverseLerp(deg0, minAngle0, maxAngle0) * 2 - 1)) * v0;

					float deg1 = (deg + 360) % 360;
					float minAngle1 = (minAngle + 360) % 360;
					float maxAngle1 = (maxAngle + 360) % 360;
					float v1 = deg1 > minAngle1 && deg1 <= maxAngle1;
					float f1 = (1 - abs(inverseLerp(deg1, minAngle1, maxAngle1) * 2 - 1)) * v1;

					float f = max(f0, f1);
					float directionMask = tex2D(_DirectionFalloff, f.xx).r * ((dir.x + dir.y) != 0);
					if (_BlendMax)
						mask = max(mask, directionMask);
					else
						mask = mask * directionMask;
				}

				return mask;
			}
			ENDCG

		}
	}
}
