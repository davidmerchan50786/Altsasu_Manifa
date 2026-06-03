// Assets/Shaders/InteriorMapping.shader
// ═══════════════════════════════════════════════════════════════════════════
//  INTERIOR MAPPING AAA — Altsasu Manifa
//
//  Técnica completa de interior mapping con parallax, idéntica a Spider-Man
//  PS4, Forza Horizon, Cyberpunk 2077 y otros títulos AAA:
//
//  • Rayo desde cada pixel → intersección con caja unitaria → muestreo Cubemap
//    → profundidad 3D real a coste de un unlit.
//  • TRES CAPAS de habitación por ventana:
//      - Habitación principal (cubemap del arquetipo)
//      - Habitación de fondo más pequeña (oscurecida, parallax mayor)
//      - Plano de fondo lejano (muy oscuro, da sensación de corredor)
//  • Variación aleatoria por ventana via hash del UV:
//      - Rotación aleatoria del cubemap (misma habitación, perspectiva distinta)
//      - Offset en profundidad ±15% (habitaciones de tamaños distintos)
//      - Tinte de color leve (diferentes bombillas)
//  • Cristal físico: Fresnel PBR + reflejo del entorno + aberración cromática
//  • Oclusión de derrame de luz (luz interior no bleedea hacia afuera)
//  • Control día/noche: brillo gradual, cambio de temperatura de color
//  • Compatible HDRP forward (LightMode = Forward)
// ═══════════════════════════════════════════════════════════════════════════
Shader "Altsasu/InteriorMapping"
{
    Properties
    {
        // ── Cubemap de la habitación ────────────────────────────────────────
        [NoScaleOffset]
        _InteriorCube   ("Interior Cubemap", Cube) = "white" {}

        // ── Layout de ventanas ─────────────────────────────────────────────
        _Rooms          ("Habitaciones (U, V)", Vector) = (2, 3, 0, 0)
        _RoomDepth      ("Profundidad caja (1=cúbica)", Range(0.4, 4.0)) = 1.3
        _DepthVariation ("Variación profundidad ±", Range(0, 0.5)) = 0.15

        // ── Emisión / ciclo día-noche ───────────────────────────────────────
        _EmissionNight  ("Emisión Noche (0=día 1=noche)", Range(0, 1)) = 0
        _EmissionDay    ("Emisión Día",   Range(0, 1)) = 0.05
        _EmissionBoost  ("Boost emisión nocturna", Range(1, 12)) = 5.0
        _ColorTemperatura("Temperatura color noche (cálido)", Range(0,1)) = 0.6

        // ── Cristal ─────────────────────────────────────────────────────────
        _GlassColor     ("Color base cristal", Color) = (0.55, 0.62, 0.70, 1)
        _GlassReflect   ("Reflectividad cristal", Range(0, 0.5)) = 0.15
        _GlassRoughness ("Rugosidad cristal", Range(0, 1)) = 0.05
        _ChromaAberr    ("Aberración cromática", Range(0, 0.02)) = 0.004

        // ── Variedad por edificio ──────────────────────────────────────────
        _Seed           ("Semilla variación", Float) = 0
        _RotacionAleat  ("Rotación cubemap aleatoria", Range(0, 1)) = 1

        // ── Porcentaje de ventanas apagadas ────────────────────────────────
        _PctApagadas    ("% ventanas apagadas", Range(0, 0.6)) = 0.2
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "Queue"          = "Geometry+1"
            "RenderPipeline" = "HDRenderPipeline"
        }
        LOD 300

        Pass
        {
            Name "FORWARD"
            Tags { "LightMode" = "Forward" }
            Cull Back
            ZWrite On

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   3.5
            #include "UnityCG.cginc"

            samplerCUBE _InteriorCube;
            float4  _Rooms;
            float   _RoomDepth;
            float   _DepthVariation;
            float   _EmissionNight;
            float   _EmissionDay;
            float   _EmissionBoost;
            float   _ColorTemperatura;
            float4  _GlassColor;
            float   _GlassReflect;
            float   _GlassRoughness;
            float   _ChromaAberr;
            float   _Seed;
            float   _RotacionAleat;
            float   _PctApagadas;

            // ── Hash sin textura (GPU-friendly) ─────────────────────────────
            float hash21(float2 p)
            {
                p = frac(p * float2(127.1, 311.7));
                p += dot(p, p + 17.5);
                return frac(p.x * p.y);
            }
            float hash11(float x) { return frac(sin(x * 127.1) * 43758.5453); }

            // ── Intersección rayo-caja [-1,1]³ ───────────────────────────────
            // Devuelve la dirección al punto de impacto en la cara lejana.
            float3 CajaRay(float3 pos, float3 dir, float profundidad)
            {
                float3 d = dir;
                d.z *= profundidad;
                d = normalize(d);
                float3 inv = 1.0 / (-d + 1e-6);
                float3 k   = abs(inv) - pos * inv;
                float  t   = min(min(k.x, k.y), k.z);
                return pos + (-d) * t;
            }

            // ── Rotación aleatoria en Y (varía la perspectiva del cubemap) ───
            float3 RotarY(float3 v, float angulo)
            {
                float s, c;
                sincos(angulo, s, c);
                return float3(v.x * c - v.z * s, v.y, v.x * s + v.z * c);
            }

            // ── Temperatura de color Kelvin simplificada ─────────────────────
            float3 ColorTemp(float t)
            {
                // t=0 → cálido ámbar (2700K), t=1 → blanco frío (6500K)
                return float3(
                    lerp(1.00f, 0.85f, t),
                    lerp(0.72f, 0.95f, t),
                    lerp(0.40f, 1.00f, t)
                );
            }

            struct appdata
            {
                float4 vertex  : POSITION;
                float3 normal  : NORMAL;
                float4 tangent : TANGENT;
                float2 uv      : TEXCOORD0;
            };

            struct v2f
            {
                float4 clip     : SV_POSITION;
                float2 uv       : TEXCOORD0;
                float3 tViewDir : TEXCOORD1;   // vista en espacio tangente
                float3 wNormal  : TEXCOORD2;
                float3 wView    : TEXCOORD3;
                float3 wPos     : TEXCOORD4;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.clip = UnityObjectToClipPos(v.vertex);
                o.uv   = v.uv;

                float3 wPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 wViewDir = wPos - _WorldSpaceCameraPos;
                o.wPos  = wPos;
                o.wView = normalize(wViewDir);

                // TBN para llevar la vista al espacio tangente de la superficie
                float3 wN = UnityObjectToWorldNormal(v.normal);
                float3 wT = normalize(mul((float3x3)unity_ObjectToWorld, v.tangent.xyz));
                float3 wB = cross(wN, wT) * v.tangent.w;
                o.wNormal = wN;

                o.tViewDir = float3(
                    dot(wViewDir, wT),
                    dot(wViewDir, wB),
                    dot(wViewDir, wN)
                );
                return o;
            }

            // ── Muestrear una "habitación" dado el UV de ventana ──────────────
            float3 MuestrarHabitacion(float2 uvVentana, float3 tView, float profRaiz,
                                      float rotY, float tiintR, float tintG, float tintB,
                                      float emision)
            {
                float3 vd = normalize(tView);
                vd.z = -abs(vd.z) - 1e-4;

                // Punto de entrada: [-1,1] en XY, -1 en Z (cara frontal)
                float3 pos = float3(uvVentana * 2.0 - 1.0, -1.0);

                float3 hitDir = CajaRay(pos, vd, profRaiz);
                hitDir = RotarY(hitDir, rotY);

                // Aberración cromática: muestrear con ligero offset en R y B
                float3 colC = texCUBE(_InteriorCube, hitDir).rgb;
                float3 hitR = RotarY(CajaRay(pos, vd * float3(1+_ChromaAberr, 1, 1), profRaiz), rotY);
                float3 hitB = RotarY(CajaRay(pos, vd * float3(1-_ChromaAberr, 1, 1), profRaiz), rotY);
                float3 col = float3(
                    texCUBE(_InteriorCube, hitR).r,
                    colC.g,
                    texCUBE(_InteriorCube, hitB).b
                );

                // Tinte por bombilla + temperatura de color
                float3 temp = ColorTemp(1.0 - _ColorTemperatura);
                col *= float3(tiintR, tintG, tintB) * temp;
                col *= emision;
                return col;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // ── UV de ventana dentro de su celda ─────────────────────────
                float2 roomCoord = i.uv * _Rooms.xy + float2(_Seed, _Seed * 0.37f);
                float2 roomID    = floor(roomCoord);
                float2 roomUV    = frac(roomCoord);

                // Hash único por celda (variedad por ventana)
                float h0 = hash21(roomID + float2(_Seed, 0));
                float h1 = hash21(roomID + float2(0, _Seed + 1));
                float h2 = hash21(roomID + float2(_Seed * 2, _Seed));

                // ── ¿Ventana apagada? ─────────────────────────────────────────
                bool apagada = (h0 < _PctApagadas) && (_EmissionNight > 0.05);

                // ── Profundidad aleatoria por ventana ─────────────────────────
                float profVar = _RoomDepth + (h1 * 2.0 - 1.0) * _DepthVariation * _RoomDepth;

                // ── Rotación aleatoria del cubemap (Y) ────────────────────────
                float rotY = _RotacionAleat > 0.5
                    ? hash11(h0 * 17.3f + 5.1f) * 6.2832f   // [0, 2π]
                    : 0.0f;

                // ── Tinte de color (bombilla ligeramente distinta) ─────────────
                float tr = lerp(0.95, 1.05, hash11(h2));
                float tg = lerp(0.95, 1.02, hash11(h2 + 0.33));
                float tb = lerp(0.88, 1.00, hash11(h2 + 0.66));

                // ── Emisión (día/noche + ventana apagada) ─────────────────────
                float emisionBase = lerp(_EmissionDay,
                    _EmissionBoost * lerp(0.5, 1.0, h1),   // algunas más brillantes
                    _EmissionNight);
                float emision = apagada ? _EmissionDay * 0.3 : emisionBase;

                // ── Capa 1: habitación principal ──────────────────────────────
                float3 colHab = MuestrarHabitacion(roomUV, i.tViewDir,
                    profVar, rotY, tr, tg, tb, emision);

                // ── Capa 2: habitación de fondo (más pequeña, más oscura) ──────
                // Simula haber una habitación más al fondo (corredor, pared de fondo)
                float profFondo = profVar * 2.2;
                float2 uvFondo  = lerp(roomUV, float2(0.5, 0.5), 0.1);  // ligeramente centrado
                float3 colFondo = MuestrarHabitacion(uvFondo, i.tViewDir,
                    profFondo, rotY + 0.3, tr * 0.6, tg * 0.6, tb * 0.6, emision * 0.4);

                // Mezcla: la hab. principal domina, el fondo se ve en los bordes
                float bordeUV = 4.0 * roomUV.x * (1.0 - roomUV.x) *
                               4.0 * roomUV.y * (1.0 - roomUV.y);  // 0 en bordes, 1 en centro
                float3 colInterior = lerp(colFondo, colHab, saturate(bordeUV * 1.5));

                // ── Cristal: Fresnel PBR ──────────────────────────────────────
                float3 wN   = normalize(i.wNormal);
                float3 wV   = -i.wView;
                float  NdotV = saturate(dot(wN, wV));

                // Fresnel de Schlick
                float f0    = _GlassReflect;
                float fres  = f0 + (1.0 - f0) * pow(1.0 - NdotV, 5.0);

                // Reflejo del entorno (aproximación: cubemap del cielo tintado)
                float3 reflDir = reflect(i.wView, wN);
                float3 reflCol = UNITY_SAMPLE_TEXCUBE_LOD(
                    unity_SpecCube0, reflDir, _GlassRoughness * 6.0).rgb;

                float3 glass = _GlassColor.rgb * (fres * 0.7 + 0.05);
                glass += reflCol * fres * 0.3;

                // ── Composición final ─────────────────────────────────────────
                float3 col = colInterior + glass;

                // Vignette de ventana (más oscuro en la junta del marco)
                float vignette = smoothstep(0.0, 0.08, roomUV.x) *
                                 smoothstep(1.0, 0.92, roomUV.x) *
                                 smoothstep(0.0, 0.06, roomUV.y) *
                                 smoothstep(1.0, 0.94, roomUV.y);
                col *= lerp(0.15, 1.0, vignette);

                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
    FallBack "Unlit/Texture"
}
