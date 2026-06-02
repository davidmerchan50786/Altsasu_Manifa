// Assets/Shaders/InteriorMapping.shader
// ═══════════════════════════════════════════════════════════════════════════
//  INTERIOR MAPPING (parallax box) — Altsasu Manifa
//
//  Simula una habitación 3D con PROFUNDIDAD real detrás del cristal usando
//  un único quad y un Cubemap. Técnica AAA (Spider-Man PS4, Forza, etc.):
//  se traza un rayo desde la superficie hacia el interior de una caja unitaria
//  y se muestrea el cubemap por la dirección al punto de impacto → parallax
//  perfecto al mover la cámara, coste ~igual que un unlit normal.
//
//  Propiedades:
//   _InteriorCube  cubemap de la habitación (6 caras: paredes/suelo/techo/fondo)
//   _Rooms         nº de habitaciones repetidas en U,V (tiling de ventanas)
//   _RoomDepth     profundidad de la caja (1 = cúbica, >1 más honda)
//   _EmissionNight 0=día (interior apagado), 1=noche (interior iluminado)
//   _GlassTint     tinte/reflejo del cristal sobre el interior
//   _Seed          desplaza la selección de habitación por edificio (variedad)
//
//  Compatible con HDRP/URP/Built-in: es un Unlit forward simple (el interior
//  ya lleva su propia iluminación "pintada" en el cubemap + emisión nocturna).
//  NOTA: verifica en Unity que el material usa este shader; si HDRP se queja,
//  cámbialo a "Shader Graphs/InteriorMapping" equivalente (ver doc del script).
// ═══════════════════════════════════════════════════════════════════════════
Shader "Altsasu/InteriorMapping"
{
    Properties
    {
        [NoScaleOffset] _InteriorCube ("Interior Cubemap", Cube) = "" {}
        _Rooms          ("Rooms (U,V)", Vector) = (3, 3, 0, 0)
        _RoomDepth      ("Room Depth", Range(0.3, 3)) = 1.0
        _EmissionNight  ("Emission Night (0-1)", Range(0,1)) = 0
        _EmissionBoost  ("Emission Boost", Range(1, 8)) = 2.5
        _GlassTint      ("Glass Tint", Color) = (0.55, 0.62, 0.70, 1)
        _GlassReflect   ("Glass Reflection", Range(0,1)) = 0.18
        _Seed           ("Seed Offset", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="HDRenderPipeline" }
        LOD 200

        Pass
        {
            Name "FORWARD"
            Tags { "LightMode" = "Forward" }
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            samplerCUBE _InteriorCube;
            float4 _Rooms;
            float  _RoomDepth;
            float  _EmissionNight;
            float  _EmissionBoost;
            float4 _GlassTint;
            float  _GlassReflect;
            float  _Seed;

            struct appdata
            {
                float4 vertex  : POSITION;
                float3 normal  : NORMAL;
                float4 tangent : TANGENT;
                float2 uv      : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos        : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 tViewDir   : TEXCOORD1; // dirección de vista en espacio tangente
                float3 worldNormal: TEXCOORD2;
                float3 worldView  : TEXCOORD3;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;

                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 worldViewDir = worldPos - _WorldSpaceCameraPos;

                // Base tangente (TBN) para llevar la vista a espacio de la superficie
                float3 wN = UnityObjectToWorldNormal(v.normal);
                float3 wT = normalize(mul((float3x3)unity_ObjectToWorld, v.tangent.xyz));
                float3 wB = cross(wN, wT) * v.tangent.w;

                o.tViewDir.x = dot(worldViewDir, wT);
                o.tViewDir.y = dot(worldViewDir, wB);
                o.tViewDir.z = dot(worldViewDir, wN);

                o.worldNormal = wN;
                o.worldView   = normalize(worldViewDir);
                return o;
            }

            // Intersección rayo-caja unitaria [-1,1]; devuelve la dirección al fondo
            float3 InteriorRay(float2 roomUV, float3 viewDir)
            {
                // Punto de entrada en la cara frontal de la caja (z = -1)
                // roomUV en [0,1] → [-1,1]
                float2 p = roomUV * 2.0 - 1.0;
                float3 pos = float3(p, -1.0);

                // Profundidad de la caja
                float3 dir = viewDir;
                dir.z *= _RoomDepth;
                dir = normalize(dir);

                // Intersección con las caras "lejanas" según el signo de la dirección
                float3 inv = 1.0 / -dir;                 // miramos hacia adentro
                float3 k   = abs(inv) - pos * inv;
                float  kMin = min(min(k.x, k.y), k.z);

                float3 hit = pos + (-dir) * kMin;
                return hit;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Repetir habitaciones según _Rooms con offset por seed
                float2 room = i.uv * _Rooms.xy + float2(_Seed, _Seed * 0.37);
                float2 roomUV = frac(room);

                float3 vd = normalize(i.tViewDir);
                // La vista entra hacia el interior (z negativo en espacio tangente)
                vd.z = -abs(vd.z) - 0.001;

                float3 dir = InteriorRay(roomUV, vd);

                // Muestrear cubemap por la dirección al punto interior
                fixed3 interior = texCUBE(_InteriorCube, dir).rgb;

                // Iluminación nocturna del interior (emisión)
                fixed3 lit = interior * lerp(0.18, _EmissionBoost, _EmissionNight);

                // Tinte y reflejo de cristal (Fresnel suave)
                float fres = pow(1.0 - saturate(dot(i.worldNormal, -i.worldView)), 3.0);
                fixed3 glass = _GlassTint.rgb * (_GlassReflect + fres * 0.5);

                fixed3 col = lit + glass;
                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
    FallBack "Unlit/Texture"
}
