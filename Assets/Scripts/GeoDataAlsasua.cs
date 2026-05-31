// Assets/Scripts/GeoDataAlsasua.cs
// ═══════════════════════════════════════════════════════════════════════════
//  FUENTE ÚNICA DE VERDAD GEOGRÁFICA — ALSASUA / ALTSASU
//
//  Todos los waypoints, zonas, rutas y puntos de referencia del proyecto
//  vienen de esta clase. Si algo geográfico cambia, solo hay que tocarlo aquí.
//
//  SISTEMA DE COORDENADAS LOCAL UNITY:
//    · Origen (0, 0, 0) = Herriko Plaza / Plaza de los Fueros
//    · +Z = Norte     (latitud creciente)
//    · +X = Este      (longitud creciente)
//    · +Y = Arriba    (altitud)
//    · Escala: 1 unidad Unity = 1 metro real
//
//  CONVERSIÓN GPS → UNITY:
//    Centro: 42.9016° N, -2.1668° W, alt 536 m
//    m_por_grado_lat = 111 300 m/°
//    m_por_grado_lon = 111 300 × cos(42.9016° × π/180) ≈ 81 490 m/°
//
//    X = (lon - (-2.1668)) × 81490
//    Z = (lat - 42.9016)   × 111300
//
//  Fuentes de datos:
//    · OpenStreetMap (openstreetmap.org)
//    · Guardia Civil directorio oficial (web.guardiacivil.es)
//    · Renfe / Adif estación Alsasua
//    · Polígonos Industriales Navarra (anuarioguia.com)
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

public static class GeoDataAlsasua
{
    // ═══════════════════════════════════════════════════════════════════════
    //  GEORREFERENCIA — PUNTO CERO
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Latitud del origen (Herriko Plaza).</summary>
    public const double LAT_CENTRO  =  42.9016;
    /// <summary>Longitud del origen (Herriko Plaza).</summary>
    public const double LON_CENTRO  =  -2.1668;
    /// <summary>Altitud real de Alsasua sobre el nivel del mar (m).</summary>
    public const double ALT_CENTRO  = 536.0;

    // Factores de conversión grados → metros (calculados para lat 42.9°)
    public const double M_POR_GRADO_LAT = 111_300.0;
    public const double M_POR_GRADO_LON =  81_490.0;   // = 111300 × cos(42.9°×π/180)

    // ═══════════════════════════════════════════════════════════════════════
    //  PUNTOS DE REFERENCIA URBANOS
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Centro de la Herriko Plaza / Plaza de los Fueros.</summary>
    public static readonly Vector3 HerrikoPlaza       = new Vector3(   0f,  0f,    0f);

    /// <summary>Ayuntamiento de Alsasua (Udaletxea) — junto a la plaza.</summary>
    public static readonly Vector3 Ayuntamiento        = new Vector3(  30f,  0f,   40f);

    /// <summary>Estación de tren Alsasua (Adif / Renfe) — al SO del casco urbano.</summary>
    public static readonly Vector3 EstacionTren        = new Vector3(-510f,  0f, -780f);

    /// <summary>Cuartel Guardia Civil — Calle Ameztia 24.</summary>
    public static readonly Vector3 CuartelGuardiaCivil = new Vector3(-260f,  0f, -380f);

    /// <summary>Comisaría Policía Foral — zona norte del casco.</summary>
    public static readonly Vector3 ComisariaPolForal   = new Vector3( 120f,  0f,  280f);

    /// <summary>Polígono Industrial Isasia — al SO (carretera de Urdiain).</summary>
    public static readonly Vector3 PoligonoIsasia      = new Vector3(-1100f, 0f,-1445f);

    /// <summary>Polígono Industrial Ondarria/Isustu — al SE (N-1 sur).</summary>
    public static readonly Vector3 PoligonoOndarria    = new Vector3( 490f,  0f, -965f);

    /// <summary>Polígono Industrial Ibarrea — al O.</summary>
    public static readonly Vector3 PoligonoIbarrea     = new Vector3(-830f,  0f,  200f);

    /// <summary>Iglesia San Miguel Arcángel (casco viejo).</summary>
    public static readonly Vector3 IglesiaSanMiguel    = new Vector3( -80f,  0f,  -60f);

    // ═══════════════════════════════════════════════════════════════════════
    //  MONTES Y CIMAS MÁS PRÓXIMOS (Burunda / Altsasua)
    //  Distancias aproximadas al centro; Y = cota relativa al fondo del valle
    // ═══════════════════════════════════════════════════════════════════════

    // Monte Artia (o Aratz menor, colina al O del casco) ~600 m
    public static readonly Vector3 MonteArtia          = new Vector3(-550f, 200f,  250f);

    // Peña Blanca / Askiz (loma al NE, ~1 km) — primera sierra visible desde el pueblo
    public static readonly Vector3 PenaBlanca          = new Vector3( 700f, 250f,  650f);

    // Comienzo del corredor del Bidasoa-Ebro (N, ~1.5 km)
    public static readonly Vector3 ColladoBurunda      = new Vector3(  50f, 120f, 1400f);

    // Sierra de Aralar (NE lejano, ~13 km) — Txindoki 1 346 m
    public static readonly Vector3 Aralar_Txindoki     = new Vector3(2500f, 810f,12000f);

    // Aizkorri / Aitxuri (SE lejano, ~25 km) — 1 551 m
    public static readonly Vector3 Aizkorri           = new Vector3(5000f,1015f,-22000f);

    // Sierra de Urbasa (S lejano, ~16 km) — Puerto Urbasa 927 m
    public static readonly Vector3 PuertoUrbasa        = new Vector3(-1800f,391f,-14600f);

    // ═══════════════════════════════════════════════════════════════════════
    //  ZONAS DE BOSQUE — para SistemaVegetacion
    //  Ajustadas a los relieves reales del Valle de Burunda:
    //    Norte: laderas del colladoBurunda y Askiz (pinos y hayas)
    //    Sur:   ladera hacia N-1, bosque mixto de robles y pinos
    //    Oeste: ladera del Monte Artia (robledal)
    //    Este:  ladera hacia la vía del tren, pinar de repoblación
    //    NE:    piedemonte Aralar, hayedo
    // ═══════════════════════════════════════════════════════════════════════

    public struct ZonaBosque
    {
        public Vector3 Centro;
        public float   Radio;
        public float   FraccionPinos; // 0=todo robles, 1=todo pinos
        public string  Nombre;
    }

    public static readonly ZonaBosque[] ZonasBosque = new ZonaBosque[]
    {
        // NORTE — colada del Bidasoa, pinar de repoblación
        new ZonaBosque { Centro = new Vector3(   0f, 0f,  800f), Radio = 380f, FraccionPinos = 0.75f, Nombre = "Pinar Norte" },
        // SUR  — laderas hacia N-1 y polígono Ondarria, mezcla pino-roble
        new ZonaBosque { Centro = new Vector3( 200f, 0f, -650f), Radio = 300f, FraccionPinos = 0.50f, Nombre = "Bosque Sur N-1" },
        // OESTE — ladera Monte Artia, robledal atlántico
        new ZonaBosque { Centro = new Vector3(-650f, 0f,  200f), Radio = 420f, FraccionPinos = 0.25f, Nombre = "Robledal Monte Artia" },
        // ESTE — ladera hacia Askiz/Peña Blanca, pinar
        new ZonaBosque { Centro = new Vector3( 750f, 0f,  350f), Radio = 350f, FraccionPinos = 0.80f, Nombre = "Pinar Askiz" },
        // NE — piedemonte Aralar, hayedo denso
        new ZonaBosque { Centro = new Vector3( 900f, 0f,  900f), Radio = 450f, FraccionPinos = 0.10f, Nombre = "Hayedo Aralar" },
        // SO — zona industrial Ibarrea, vegetación de ribera
        new ZonaBosque { Centro = new Vector3(-700f, 0f, -100f), Radio = 200f, FraccionPinos = 0.30f, Nombre = "Ribera Oyantzun" },
    };

    // ═══════════════════════════════════════════════════════════════════════
    //  RUTAS DE TRÁFICO — SistemaTrafico
    //  Todos los waypoints en espacio local Unity (metros desde Herriko Plaza)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>N-1 sentido NORTE (pasando por el casco urbano).</summary>
    public static readonly Vector3[] N1_Norte = new Vector3[]
    {
        new Vector3(  80f, 0f, -900f),  // entrada sur, zona industrial Ondarria
        new Vector3(  60f, 0f, -600f),  // llegando al casco por el sur
        new Vector3(  40f, 0f, -300f),  // glorieta sur del pueblo
        new Vector3(  20f, 0f,    0f),  // Herriko Plaza / centro
        new Vector3(   0f, 0f,  300f),  // barrio norte, hacia Arakil
        new Vector3( -20f, 0f,  600f),  // salida norte por N-1
        new Vector3( -40f, 0f,  900f),  // hacia Salvatierra / Agurain
    };

    /// <summary>N-1 sentido SUR (contracarril).</summary>
    public static readonly Vector3[] N1_Sur = new Vector3[]
    {
        new Vector3( -30f, 0f,  900f),
        new Vector3( -10f, 0f,  600f),
        new Vector3(  10f, 0f,  300f),
        new Vector3(  30f, 0f,    0f),
        new Vector3(  50f, 0f, -300f),
        new Vector3(  70f, 0f, -600f),
        new Vector3(  90f, 0f, -900f),
    };

    /// <summary>NA-120 / GI-627 sentido ESTE (hacia Arrasate / Mondragón).</summary>
    public static readonly Vector3[] NA120_Este = new Vector3[]
    {
        new Vector3(-500f, 0f,  100f),   // acceso desde polígono Ibarrea
        new Vector3(-250f, 0f,   80f),   // zona industrial oeste
        new Vector3(   0f, 0f,   50f),   // cruce con N-1
        new Vector3( 300f, 0f,   30f),   // barrio este, línea de tren
        new Vector3( 600f, 0f,   10f),   // salida hacia Aretxabaleta / Arrasate
    };

    /// <summary>NA-120 / GI-627 sentido OESTE (hacia Olazti / Ciordia).</summary>
    public static readonly Vector3[] NA120_Oeste = new Vector3[]
    {
        new Vector3( 600f, 0f,  -10f),
        new Vector3( 300f, 0f,   -5f),
        new Vector3(   0f, 0f,   -5f),
        new Vector3(-250f, 0f,   -5f),
        new Vector3(-500f, 0f,  -10f),
    };

    /// <summary>Calle interior del casco urbano (loop circundante).</summary>
    public static readonly Vector3[] CalleInteriorCasco = new Vector3[]
    {
        new Vector3( 150f, 0f,  200f),   // norte casco
        new Vector3( 200f, 0f,    0f),   // este
        new Vector3( 150f, 0f, -200f),   // sureste
        new Vector3(   0f, 0f, -300f),   // sur (glorieta)
        new Vector3(-180f, 0f, -150f),   // suroeste
        new Vector3(-200f, 0f,   50f),   // oeste
        new Vector3(-100f, 0f,  250f),   // noroeste
        new Vector3( 150f, 0f,  200f),   // cierre
    };

    // ═══════════════════════════════════════════════════════════════════════
    //  RUTAS DE PATRULLA — Guardia Civil y Policía Foral
    //  Siguen las calles reales de Alsasua:
    //    Calle Navarra, Calle Ameztia, Calle Erdikale, Plaza de los Fueros
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Patrulla de la Guardia Civil.
    /// Cuartel en Calle Ameztia → N-1 → plaza → calles del casco → vuelta.
    /// </summary>
    public static readonly Vector3[] PatrullaGuardiaCivil = new Vector3[]
    {
        new Vector3(-260f, 0f, -380f),   // Salida cuartel GC (Calle Ameztia)
        new Vector3(-200f, 0f, -200f),   // Calle Navarra, tramo sur
        new Vector3(-100f, 0f, -100f),   // Calle Erdikale
        new Vector3(   0f, 0f,    0f),   // Herriko Plaza
        new Vector3(  80f, 0f,   80f),   // Calle Brentana (NE plaza)
        new Vector3( 100f, 0f,  -50f),   // Zona comercial este
        new Vector3(  50f, 0f, -200f),   // N-1 tramo sur casco
        new Vector3( -50f, 0f, -300f),   // Glorieta sur
        new Vector3(-180f, 0f, -350f),   // De vuelta por Calle Ameztia
        new Vector3(-260f, 0f, -380f),   // Regreso al cuartel
    };

    /// <summary>
    /// Patrulla de la Policía Foral.
    /// Comisaría norte → Herriko Plaza → zona industrial → vuelta.
    /// </summary>
    public static readonly Vector3[] PatrullaPolForal = new Vector3[]
    {
        new Vector3( 120f, 0f,  280f),   // Salida comisaría PF
        new Vector3(  60f, 0f,  200f),   // Calle Navarra norte
        new Vector3(   0f, 0f,  100f),   // Bajando al centro
        new Vector3(   0f, 0f,    0f),   // Herriko Plaza
        new Vector3( 100f, 0f,  -80f),   // Calle este
        new Vector3( 200f, 0f, -200f),   // Acceso a Polígono Ondarria
        new Vector3( 100f, 0f,  100f),   // Volviendo por barrio norte
        new Vector3( 120f, 0f,  280f),   // Regreso comisaría PF
    };

    // ═══════════════════════════════════════════════════════════════════════
    //  VÍA FÉRREA (Madrid–Irún)
    //  Renfe Cercanías C1 / Larga Distancia — pasa al SO del pueblo
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Waypoints de la vía del tren sentido norte (hacia Vitoria).</summary>
    public static readonly Vector3[] ViaFerreaNorte = new Vector3[]
    {
        new Vector3(-500f, 0f, -1400f),  // sur (procedente de Pamplona)
        new Vector3(-510f, 0f,  -780f),  // Estación Alsasua
        new Vector3(-520f, 0f,  -200f),  // tramo urbano oeste
        new Vector3(-530f, 0f,   400f),  // salida norte hacia Vitoria
        new Vector3(-540f, 0f,  1200f),  // norte lejano
    };

    // ═══════════════════════════════════════════════════════════════════════
    //  PUNTOS DE SPAWN DEL JUGADOR
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Spawn principal: Herriko Plaza (centro del pueblo).</summary>
    public static readonly Vector3 SpawnPrincipal    = new Vector3(   0f, 10f,    0f);

    /// <summary>Spawn alternativo 1: junto a la estación de tren.</summary>
    public static readonly Vector3 SpawnEstacion     = new Vector3(-450f, 10f, -740f);

    /// <summary>Spawn alternativo 2: polígono industrial Ondarria.</summary>
    public static readonly Vector3 SpawnIndustrial   = new Vector3( 450f, 10f, -900f);

    /// <summary>Spawn alternativo 3: ladera norte (vistas al valle de Burunda).</summary>
    public static readonly Vector3 SpawnMirador      = new Vector3(-100f, 80f,  900f);

    // ═══════════════════════════════════════════════════════════════════════
    //  UTILIDAD: Conversión GPS ↔ Unity
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Convierte coordenadas GPS reales a posición Unity local respecto al centro de Alsasua.
    /// </summary>
    public static Vector3 GpsAUnity(double latitud, double longitud, float alturaRelativa = 0f)
    {
        float x = (float)((longitud - LON_CENTRO) * M_POR_GRADO_LON);
        float z = (float)((latitud  - LAT_CENTRO) * M_POR_GRADO_LAT);
        return new Vector3(x, alturaRelativa, z);
    }

    /// <summary>
    /// Convierte posición Unity local a coordenadas GPS aproximadas.
    /// </summary>
    public static (double lat, double lon) UnityAGps(Vector3 pos)
    {
        double lat = LAT_CENTRO + pos.z / M_POR_GRADO_LAT;
        double lon = LON_CENTRO + pos.x / M_POR_GRADO_LON;
        return (lat, lon);
    }
}
