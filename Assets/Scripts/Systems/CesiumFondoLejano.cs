// Assets/Scripts/Systems/CesiumFondoLejano.cs
// ═══════════════════════════════════════════════════════════════════════════
//  MODO HÍBRIDO CESIUM = SOLO FONDO LEJANO
//
//  Problema que corrige (junio 2026):
//    · El CesiumGeoreference se creaba en (0,0,0) con el GPS de Herriko Plaza,
//      pero el jugador hace spawn en (1918, y, 8570) → el jugador caía sobre
//      la ladera de Urbasa de los tiles de Google, 8,8 km lejos de la plaza
//      de Cesium → "vetas" verdes de fotogrametría en LOD mínimo.
//    · Los physics meshes de los tiles low-LOD (triángulos >500 u) competían
//      con el Terrain LIDAR local → suelo no sólido.
//    · CesiumSunSky + Sun_AutoPlay → doble sol, imagen quemada.
//
//  Solución (este componente, auto-arranca en Play):
//    1. Ancla el CesiumGeoreference en (OX, alturaTerrenoPlaza, OZ) con el
//       GPS real de la plaza → los tiles coinciden con el mundo local.
//    2. Calibra la altura vertical con SampleHeightMostDetailed (los tiles
//       de Google usan altura elipsoidal WGS84, ~+50 m sobre el nivel del
//       mar en Navarra; sin esto los tiles flotan o se hunden ~50 m).
//    3. Tilesets → createPhysicsMeshes = false (el collider jugable es el
//       Terrain LIDAR) y maximumScreenSpaceError bajo (anillo acotado = barato).
//    4. ExcluidorTilesCercanos: recorte en ANILLO CUADRADO centrado en la plaza:
//       · agujero interior (medioLadoInterior=7150) → ahí manda el MOSAICO V2
//         (14.4×14.4 km; 50 m de solape bajo su borde para evitar rendijas)
//       · recorte exterior (medioLadoExterior=9600) → mundo total ~369 km²;
//         fuera de él no se carga NINGÚN tile de Cesium.
//    5. Desactiva CesiumSunSky y quita CesiumCameraController/GlobeAnchor de
//       las cámaras (peleaban con la cámara en tercera persona).
//
//  Capa: WORLD (Systems). No referencia capas superiores.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using UnityEngine;

#if CESIUM_FOR_UNITY
using CesiumForUnity;
using Unity.Mathematics;
#endif

public class CesiumFondoLejano : MonoBehaviour
{
    // GPS real de Herriko Plaza (mismas constantes que ConfiguradorCesiumAlsasua)
    const double LAT_PLAZA = 42.89873;
    const double LON_PLAZA = -2.16770;
    const double ALT_PLAZA = GeoDataAlsasua.COTA_PLAZA; // m s.n.m. (orientativa; se calibra)

    [Tooltip("Medio lado del agujero interior SIN tiles (m), centrado en la plaza.\n" +
             "El mundo jugable lo cubre el mosaico V2 (±7200 m) → 7150 deja\n" +
             "50 m de solape bajo el borde del terreno para evitar rendijas.")]
    public float medioLadoInterior = 7150f;

    [Tooltip("Medio lado del recorte exterior (m). 9600 → cuadrado de ~369 km²\n" +
             "centrado en Herriko Plaza. Fuera de él no se carga ningún tile.")]
    public float medioLadoExterior = 9600f;

    [Tooltip("Calidad de los tiles del anillo. Menor = más detalle.\n" +
             "32: con el mosaico V2 el anillo Cesium empieza a ≥7 km del jugador.")]
    public float screenSpaceErrorFondo = 32f;

    [Tooltip("Crear Cesium OSM Buildings (ID 96188) para dar volumen a los\n" +
             "edificios de los pueblos del anillo: Google NO tiene fotogrametría\n" +
             "en la Sakana (solo ortofoto drapeada, edificios planos).")]
    public bool crearOsmBuildings = true;

    // ── Auto-arranque en Play ──────────────────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
#if CESIUM_FOR_UNITY
        if (FindFirstObjectByType<CesiumFondoLejano>() != null) return;
        var go = new GameObject("CesiumFondoLejano");
        go.AddComponent<CesiumFondoLejano>();
#endif
    }

#if CESIUM_FOR_UNITY
    IEnumerator Start()
    {
        // ── 1. Esperar al CesiumGeoreference (lo crea el configurador/escena) ─
        CesiumGeoreference georef = null;
        float t = 0f;
        while (georef == null && t < 10f)
        {
            georef = FindFirstObjectByType<CesiumGeoreference>();
            if (georef == null) { t += 0.5f; yield return new WaitForSeconds(0.5f); }
        }
        if (georef == null)
        {
            AlsasuaLogger.Info("CesiumFondo", "Sin CesiumGeoreference en escena — nada que alinear.");
            yield break;
        }

        // GPS de la plaza como origen del georeference
        georef.latitude  = LAT_PLAZA;
        georef.longitude = LON_PLAZA;
        georef.height    = ALT_PLAZA;

        // ── 2. Esperar al suelo jugable vía ITerrainService (sin polling ciego) ─
        ITerrainService svcTerreno = null;
        t = 0f;
        while (t < 30f)
        {
            svcTerreno ??= ServiceLocator.Get<ITerrainService>();
            if (svcTerreno != null &&
                svcTerreno.Estado != EstadoTerreno.Inicializando &&
                svcTerreno.Estado != EstadoTerreno.Generando) break;
            // Escena sin servicio (tests aislados): el activeTerrain clásico vale
            if (svcTerreno == null && Terrain.activeTerrain != null) break;
            t += 0.25f;
            yield return new WaitForSeconds(0.25f);
        }

        float alturaPlazaLocal = (float)ALT_PLAZA - GeoDataAlsasua.Z_MIN; // fallback ≈ 11.7
        var terreno = svcTerreno != null ? svcTerreno.Terreno : Terrain.activeTerrain;
        if (terreno != null)
        {
            alturaPlazaLocal = terreno.SampleHeight(GeoDataAlsasua.HerrikoPlaza)
                             + terreno.transform.position.y;
        }
        else
        {
            // GUARDA: sin Terrain local no hay referencia de altura → los tiles
            // quedarían mal anclados y el jugador acaba DENTRO del globo viendo
            // la cara trasera de la fotogrametría ("rayas amarillas en el cielo").
            // Mejor apagar Cesium que mostrar eso.
            foreach (var ts in georef.GetComponentsInChildren<Cesium3DTileset>(true))
                ts.gameObject.SetActive(false);
            AlsasuaLogger.Warn("CesiumFondo",
                $"Sin Terrain jugable (servicio: {svcTerreno?.Estado.ToString() ?? "ausente"}, " +
                $"fuente: {svcTerreno?.Fuente.ToString() ?? "—"}) → tilesets Cesium " +
                "desactivados: sin referencia de altura envolverían al jugador.");
            yield break;
        }

        // ── 3. Anclar el georeference en la plaza del mundo local ─────────────
        // Re-asegurar el GPS: otro sistema pudo tocarlo durante la espera.
        georef.latitude  = LAT_PLAZA;
        georef.longitude = LON_PLAZA;
        // La cámara puede haberse re-parentado bajo el georef ANTES de mover el
        // GO — la desacoplamos para que no se desplace con él.
        DesparentarCamarasDelGeoref(georef);
        georef.transform.position = new Vector3(
            GeoDataAlsasua.OX, alturaPlazaLocal, GeoDataAlsasua.OZ);

        AlsasuaLogger.Info("CesiumFondo",
            $"Georeference anclado en ({GeoDataAlsasua.OX}, {alturaPlazaLocal:F1}, " +
            $"{GeoDataAlsasua.OZ}) = Herriko Plaza GPS ({LAT_PLAZA}, {LON_PLAZA}).");

        // ── 3b. OSM Buildings: volumen para los pueblos del anillo ────────────
        if (crearOsmBuildings) CrearOsmBuildingsSiFalta(georef);

        // ── 4. Configurar tilesets: sin física, LOD de fondo, anillo cuadrado ─
        Cesium3DTileset tilesetPrincipal = null;
        foreach (var ts in georef.GetComponentsInChildren<Cesium3DTileset>(true))
        {
            ts.createPhysicsMeshes      = false;   // el suelo es el Terrain LIDAR
            ts.maximumScreenSpaceError  = screenSpaceErrorFondo; // anillo acotado → calidad alta
            var ex = ts.GetComponent<ExcluidorTilesCercanos>()
                  ?? ts.gameObject.AddComponent<ExcluidorTilesCercanos>();
            ex.medioLadoInterior = medioLadoInterior;
            ex.medioLadoExterior = medioLadoExterior;
            if (tilesetPrincipal == null) tilesetPrincipal = ts;
            AlsasuaLogger.Info("CesiumFondo",
                $"Tileset '{ts.name}': física OFF, SSE={ts.maximumScreenSpaceError}, " +
                $"anillo cuadrado [{medioLadoInterior:F0}–{medioLadoExterior:F0}] m " +
                $"(mundo {(medioLadoExterior * 2f / 1000f):F2} km de lado ≈ " +
                $"{(medioLadoExterior * medioLadoExterior * 4f / 1e6f):F0} km²).");
        }

        // ── 5. Quitar el doble sol y los controladores que pelean con la TP ───
        DesactivarSunSky();
        QuitarControladoresCesiumDeCamaras();

        // ── 6. Calibración vertical fina (elipsoide WGS84 vs nivel del mar) ───
        if (tilesetPrincipal != null)
            yield return CalibrarAltura(georef, tilesetPrincipal, alturaPlazaLocal);

        // ── 7. Segunda pasada: capturar tilesets creados tarde por otros ──────
        //      sistemas (p. ej. CesiumCapasAlsasua en su Start) para que ninguno
        //      quede sin física OFF, SSE y recorte de anillo cuadrado.
        yield return new WaitForSeconds(2f);
        if (crearOsmBuildings) CrearOsmBuildingsSiFalta(georef);
        foreach (var ts in georef.GetComponentsInChildren<Cesium3DTileset>(true))
        {
            ts.createPhysicsMeshes     = false;
            ts.maximumScreenSpaceError = screenSpaceErrorFondo;
            var ex = ts.GetComponent<ExcluidorTilesCercanos>()
                  ?? ts.gameObject.AddComponent<ExcluidorTilesCercanos>();
            ex.medioLadoInterior = medioLadoInterior;
            ex.medioLadoExterior = medioLadoExterior;
        }
    }

    // Los tiles de Google usan altura elipsoidal. Muestreamos la altura real
    // del tile en la plaza y ajustamos georef.height para que la superficie
    // del tile coincida con el Terrain local en ese punto.
    IEnumerator CalibrarAltura(CesiumGeoreference georef,
                               Cesium3DTileset tileset,
                               float alturaPlazaLocal)
    {
        var tarea = tileset.SampleHeightMostDetailed(
            new double3(LON_PLAZA, LAT_PLAZA, 0.0));

        float t = 0f;
        while (!tarea.IsCompleted && t < 30f)
        {
            t += 0.5f;
            yield return new WaitForSeconds(0.5f);
        }

        if (!tarea.IsCompletedSuccessfully || tarea.Result == null ||
            tarea.Result.sampleSuccess == null ||
            tarea.Result.sampleSuccess.Length == 0 || !tarea.Result.sampleSuccess[0])
        {
            AlsasuaLogger.Warn("CesiumFondo",
                "No se pudo muestrear la altura del tile en la plaza — " +
                "se mantiene la altura nominal (puede haber ±50 m de desfase " +
                "en el fondo lejano, no afecta al suelo jugable).");
            yield break;
        }

        double alturaElipsoidalTile = tarea.Result.longitudeLatitudeHeightPositions[0].z;

        // georef.height = altura elipsoidal que mapea al Y del GO del georef.
        // Al ponerla = altura del tile, la superficie del tile en la plaza
        // queda exactamente en Y = alturaPlazaLocal (donde está el GO).
        georef.height = alturaElipsoidalTile;

        AlsasuaLogger.Info("CesiumFondo",
            $"Calibración vertical: tile Google a {alturaElipsoidalTile:F1} m " +
            $"elipsoidales → enrasado con el Terrain local (Y={alturaPlazaLocal:F1}).");
    }

    const long ID_OSM_BUILDINGS = 96188;

    // Google Photorealistic no tiene fotogrametría en la Sakana (solo ciudades
    // grandes) → los edificios del anillo saldrían planos. OSM Buildings les
    // da volumen (footprints de Catastro vía OSM). El bucle de configuración
    // posterior le aplica SSE, física OFF y el excluder de anillo cuadrado.
    void CrearOsmBuildingsSiFalta(CesiumGeoreference georef)
    {
        foreach (var ts in georef.GetComponentsInChildren<Cesium3DTileset>(true))
            if (ts.ionAssetID == ID_OSM_BUILDINGS) return; // ya existe

        var go = new GameObject("Cesium_OSMBuildings");
        go.transform.SetParent(georef.transform, false);
        var tileset = go.AddComponent<Cesium3DTileset>();
        tileset.ionAssetID = ID_OSM_BUILDINGS;
        AlsasuaLogger.Info("CesiumFondo",
            "Cesium OSM Buildings (96188) creado — volumen para los edificios " +
            "del anillo (Google ahí solo da ortofoto plana).");
    }

    void DesparentarCamarasDelGeoref(CesiumGeoreference georef)
    {
        foreach (var cam in FindObjectsByType<Camera>(FindObjectsInactive.Include,
                                                      FindObjectsSortMode.None))
        {
            if (cam.transform.IsChildOf(georef.transform))
                cam.transform.SetParent(null, worldPositionStays: true);
        }
    }

    void DesactivarSunSky()
    {
        // CesiumSunSky no existe como tipo en com.cesium.unity 1.23 (viene de los
        // samples) — detección por nombre de tipo para no romper la compilación.
        foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include,
                                                            FindObjectsSortMode.None))
        {
            if (mb == null || mb.GetType().Name != "CesiumSunSky") continue;
            mb.gameObject.SetActive(false);
            AlsasuaLogger.Info("CesiumFondo",
                "CesiumSunSky desactivado — la iluminación la lleva SistemaVolumenHDRP.");
        }
    }

    void QuitarControladoresCesiumDeCamaras()
    {
        foreach (var cam in FindObjectsByType<Camera>(FindObjectsInactive.Include,
                                                      FindObjectsSortMode.None))
        {
            var ctrl = cam.GetComponent<CesiumCameraController>();
            if (ctrl != null) Destroy(ctrl);
            var anchor = cam.GetComponent<CesiumGlobeAnchor>();
            if (anchor != null) Destroy(anchor);
        }
    }
#endif
}

#if CESIUM_FOR_UNITY
/// <summary>
/// Recorta los tiles de Cesium a un ANILLO CUADRADO centrado en el origen del
/// tileset (= Herriko Plaza tras la alineación). Se excluye un tile si:
///   · su AABB horizontal cabe COMPLETO dentro del cuadrado interior
///     (ahí manda el Terrain LIDAR local), o
///   · su AABB queda COMPLETO fuera del cuadrado exterior (límite del mundo,
///     ~60 km² con medioLado 3873 m).
/// Excluir un tile excluye todo su subárbol, pero un tile padre que cruza un
/// borde se conserva y sus hijos se evalúan individualmente → el recorte
/// converge al cuadrado con precisión de tile hoja.
/// </summary>
public class ExcluidorTilesCercanos : CesiumTileExcluder
{
    [Tooltip("Medio lado del agujero interior sin tiles (m). 0 = sin agujero.")]
    public float medioLadoInterior = 7150f;

    [Tooltip("Medio lado del límite exterior del mundo (m). 9600 ≈ 369 km².")]
    public float medioLadoExterior = 9600f;

    public override bool ShouldExclude(Cesium3DTile tile)
    {
        // bounds en coordenadas locales del GO del excluder (= GO del tileset,
        // hijo del georef anclado en la plaza) → la plaza es (0,0,0).
        Bounds b = tile.bounds;

        // 1) Completamente FUERA del cuadrado exterior → fuera del mundo.
        if (b.center.x - b.extents.x >  medioLadoExterior) return true;
        if (b.center.x + b.extents.x < -medioLadoExterior) return true;
        if (b.center.z - b.extents.z >  medioLadoExterior) return true;
        if (b.center.z + b.extents.z < -medioLadoExterior) return true;

        // 2) Completamente DENTRO del cuadrado interior → lo cubre el LIDAR.
        float maxX = Mathf.Abs(b.center.x) + b.extents.x;
        float maxZ = Mathf.Abs(b.center.z) + b.extents.z;
        return maxX < medioLadoInterior && maxZ < medioLadoInterior;
    }
}
#endif
