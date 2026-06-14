// Assets/Scripts/Editor/ConstructorAPV.cs
// ═══════════════════════════════════════════════════════════════════════════
//  CONSTRUCTOR — Adaptive Probe Volumes (APV) por anillos del mosaico
//
//  Coloca 3 Probe Volumes anidados y deja que APV use la regla DOCUMENTADA de
//  "donde se solapan, gana la subdivisión MÁS FINA":
//    · Global (14.4 km)  → highest bajo  = bricks GRUESOS (sierras, Anillo 2)
//    · Valle  (7.2 km)   → highest medio = bricks MEDIOS  (Anillo 1)
//    · Ciudad (2.4 km)   → sin override  = bricks FINOS   (Anillo 0, LIDAR)
//  Como la ciudad queda cubierta por los tres, la regla "gana el más fino" la
//  deja en alta densidad; las sierras, cubiertas solo por el global, quedan
//  baratas. Así un mundo de 14 km no genera millones de bricks.
//
//  El BAKE usa AdaptiveProbeVolumes.BakeAsync() (NO bloqueante): el editor sigue
//  respondiendo y se sondea AdaptiveProbeVolumes.isRunning desde el update loop.
//
//  Densidad base (minDistanceBetweenProbes / simplificationLevels) se fija en el
//  Baking Set activo si es resoluble; si no, se avisa para ponerlo a mano.
//
//  Capa: Alsasua.Editor. Usa GeoDataAlsasua (Runtime) para anclar a Herriko Plaza.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class ConstructorAPV
{
    // Niveles de subdivisión (índice alto = más fino). Se clampan al global del set.
    const int kSimplificationLevels = 4;   // 0..4 → 5 tamaños de brick
    const int kNivelMedioValle      = 2;   // cap del valle
    const int kNivelGruesoSierra    = 1;   // cap del global/sierras
    const float kMinDistProbes      = 1.0f; // espaciado más fino (m) en la ciudad

    [MenuItem("Tools/Alsasua/Render/APV · Auto-colocar volúmenes (3 anillos)")]
    public static void ColocarVolumenes()
    {
        var raiz = GameObject.Find("APV_Volumes") ?? new GameObject("APV_Volumes");
        Undo.RegisterCreatedObjectUndo(raiz, "APV_Volumes");

        Vector3 plaza = GeoDataAlsasua.HerrikoPlaza;
        float yValle  = GeoDataAlsasua.COTA_PLAZA - GeoDataAlsasua.Z_MIN; // ≈ 20.6 (suelo del valle)

        // Global (sierras incluidas): cubre 14.4 km y toda la altura (valle→cumbres ~670 u).
        CrearVolumen(raiz.transform, "APV_Global_Grueso",
            centro: new Vector3(plaza.x, yValle + 330f, plaza.z),
            size:   new Vector3(14600f, 900f, 14600f),
            aplicarOverride: true, highest: kNivelGruesoSierra);

        // Valle (Anillo 1): 7.2 km, densidad media.
        CrearVolumen(raiz.transform, "APV_Valle_Medio",
            centro: new Vector3(plaza.x, yValle + 90f, plaza.z),
            size:   new Vector3(7300f, 320f, 7300f),
            aplicarOverride: true, highest: kNivelMedioValle);

        // Ciudad (Anillo 0): 2.4 km, SIN override → densidad fina (gana el más fino al solapar).
        CrearVolumen(raiz.transform, "APV_Ciudad_Fino",
            centro: new Vector3(plaza.x, yValle + 45f, plaza.z),
            size:   new Vector3(2500f, 130f, 2500f),
            aplicarOverride: false, highest: 0);

        ConfigurarDensidadBakingSet();
        Debug.Log("[APV] 3 volúmenes colocados (Ciudad fino / Valle medio / Global grueso). " +
                  "Revisa el Baking Set y lanza 'APV · Bake async'.");
    }

    static void CrearVolumen(Transform padre, string nombre, Vector3 centro, Vector3 size,
                             bool aplicarOverride, int highest)
    {
        var go = GameObject.Find(nombre);
        if (go == null)
        {
            go = new GameObject(nombre);
            go.transform.SetParent(padre, false);
            Undo.RegisterCreatedObjectUndo(go, nombre);
        }
        go.transform.position = centro;

        var pv = go.GetComponent<ProbeVolume>() ?? Undo.AddComponent<ProbeVolume>(go);
        pv.size = size;
        pv.overridesSubdivLevels    = aplicarOverride;
        pv.lowestSubdivLevelOverride  = 0;
        pv.highestSubdivLevelOverride = highest;
        pv.fillEmptySpaces = false;  // bricks siguen a la geometría (edificios LIDAR), no rellenan el vacío
        EditorUtility.SetDirty(go);
    }

    static void ConfigurarDensidadBakingSet()
    {
        var set = ProbeReferenceVolume.instance != null
            ? ProbeReferenceVolume.instance.currentBakingSet : null;
        if (set == null)
        {
            Debug.LogWarning("[APV] No hay Baking Set activo resoluble por código. " +
                "Abre Window ▸ Rendering ▸ Lighting ▸ Adaptive Probe Volumes, crea/asigna el set, y pon " +
                $"Min Distance Between Probes = {kMinDistProbes} y Simplification Levels = {kSimplificationLevels}.");
            return;
        }
        set.minDistanceBetweenProbes = kMinDistProbes;
        set.simplificationLevels     = kSimplificationLevels;
        EditorUtility.SetDirty(set);
        Debug.Log($"[APV] Baking Set '{set.name}': minDist={kMinDistProbes} m, niveles={kSimplificationLevels}.");
    }

    // ── BAKE ASÍNCRONO (no congela el editor) ───────────────────────────────
    [MenuItem("Tools/Alsasua/Render/APV · Bake async (sin congelar)")]
    public static void BakeAsync()
    {
        if (Lightmapping.isRunning || AdaptiveProbeVolumes.isRunning)
        {
            Debug.LogWarning("[APV] Ya hay un bake en curso.");
            return;
        }
        if (!AdaptiveProbeVolumes.BakeAsync())
        {
            Debug.LogError("[APV] BakeAsync no pudo arrancar (¿APV activado en el HDRP Asset? " +
                           "¿hay Probe Volumes y un Baking Set con la escena?).");
            return;
        }
        _vistoRunning = false;
        _t0 = (float)EditorApplication.timeSinceStartup;
        EditorApplication.update -= SondearBake;
        EditorApplication.update += SondearBake;
        Debug.Log("[APV] Bake asíncrono lanzado — el editor sigue respondiendo.");
    }

    static bool  _vistoRunning;
    static float _t0;

    static void SondearBake()
    {
        if (AdaptiveProbeVolumes.isRunning) { _vistoRunning = true; return; }
        // Aún no ha arrancado del todo: espera a ver el flag en true (un par de frames).
        if (!_vistoRunning)
        {
            if (EditorApplication.timeSinceStartup - _t0 > 5.0) // arranque fallido
                EditorApplication.update -= SondearBake;
            return;
        }
        EditorApplication.update -= SondearBake;
        double seg = EditorApplication.timeSinceStartup - _t0;
        Debug.Log($"[APV] Bake completado en {seg:F1}s. Recuerda guardar la escena/lighting data.");
    }
}
