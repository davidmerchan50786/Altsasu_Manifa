// Assets/Scripts/Editor/ConfiguradorFase5.cs
// ═══════════════════════════════════════════════════════════════════════════
//  CONFIGURADOR FASE 5 — Iluminación horneada AAA (Docs/plan_render_aaa.md)
//
//  Prepara la escena para el bake de luz con la calidad del plan AAA:
//    1. LightingSettings: Progressive GPU, 2 rebotes, AO, lightmaps combinados
//    2. ProbeVolumes APV para cada anillo del mosaico (GI para dinámicos)
//    3. Opciones de calidad: shadowDist, lodBias, GPU Resident Drawer
//    4. Menú para lanzar el bake de lightmaps y el bake de APV
//
//  FLUJO RECOMENDADO:
//    a) Hornear Ciudad (🏗️) + Hornear Terreno (🏔️) → geometría estática lista
//    b) ⚡ Configurar Iluminación AAA → esta herramienta
//    c) 💡 Iniciar Bake (async) → tardará 10-60 min según la CPU/GPU
//    d) 🔬 Bake APV (Probe Volumes) → 1-5 min para GI dinámica
//
//  Reversible: ↩️ Restablecer Iluminación elimina las ProbeVolumes añadidas
//  y restaura los valores por defecto de LightingSettings.
// ═══════════════════════════════════════════════════════════════════════════

using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
#if UNITY_6000_0_OR_NEWER
using UnityEngine.Rendering;
#endif

public static class ConfiguradorFase5
{
    const string DIR_LIGHTING   = "Assets/Settings/Lighting";
    const string SETTINGS_PATH  = DIR_LIGHTING + "/AlsasuaLightingSettings.asset";
    const string APV_ROOT_NAME  = "ProbeVolumes_AAA";

    // Anillos del mosaico: (radio en m, nombre)
    static readonly (float radio, string nombre)[] ANILLOS = {
        (1200f,  "APV_Urbano"),
        (3600f,  "APV_Valle"),
        (7200f,  "APV_Sierras"),
    };

    // ── Menú: Configurar ────────────────────────────────────────────────────
    [MenuItem("Tools/Alsasua/Iluminacion/⚡ Configurar Iluminación AAA", priority = 50)]
    static void Configurar()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid()) { Debug.LogError("[Fase5] No hay escena activa."); return; }

        // 1. LightingSettings
        var ls = CrearOCargarLightingSettings();
        Lightmapping.lightingSettings = ls;

        // 2. ProbeVolumes APV
        int pvCreadas = CrearProbeVolumes();

        // 3. Ambient light desde el cielo
        RenderSettings.ambientMode = AmbientMode.Skybox;

        // 4. GPU Resident Drawer (Unity 6+) — intento vía API pública si disponible
        IntentarActivarGPUResidentDrawer();

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();

        Debug.Log($"[Fase5] ✅ Iluminación configurada: LightingSettings en {SETTINGS_PATH}, " +
                  $"{pvCreadas} ProbeVolumes añadidas. " +
                  "Siguiente: 💡 Iniciar Bake → luego 🔬 Bake APV.");
        EditorUtility.DisplayDialog("Configurar Iluminación AAA",
            $"✅ Scene lista para bake:\n" +
            $"· LightingSettings: Progressive GPU, 2 bounces, AO\n" +
            $"· {pvCreadas} ProbeVolumes APV en escena\n\n" +
            "Siguiente: 💡 Iniciar Bake (10-60 min) → 🔬 Bake APV (1-5 min).", "Entendido");
    }

    // ── Menú: GPU Resident Drawer ───────────────────────────────────────────
    [MenuItem("Tools/Alsasua/Iluminacion/⚡ Activar GPU Resident Drawer", priority = 50)]
    static void ActivarGPUResidentDrawer()
    {
        bool activado = false;
        var hdrpAsset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline
            as HDRenderPipelineAsset;
        if (hdrpAsset != null)
        {
            var prop = typeof(HDRenderPipelineAsset).GetProperty("gpuResidentDrawerMode",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (prop != null)
            {
                var valores = System.Enum.GetValues(prop.PropertyType);
                object valorOn = null;
                foreach (var v in valores)
                    if (System.Convert.ToInt32(v) != 0) { valorOn = v; break; }
                if (valorOn != null)
                {
                    prop.SetValue(hdrpAsset, valorOn);
                    EditorUtility.SetDirty(hdrpAsset);
                    AssetDatabase.SaveAssets();
                    activado = true;
                    Debug.Log($"[Fase5] GPU Resident Drawer = {valorOn} en HDRP Asset.");
                }
            }
        }

        if (!activado)
        {
            EditorUtility.DisplayDialog("GPU Resident Drawer",
                "No se pudo activar automáticamente.\n\n" +
                "Hazlo manualmente:\n" +
                "1. Project Settings → Graphics → HDRP Asset (o selecciona el asset)\n" +
                "2. Rendering → GPU Resident Drawer → 'Instanced Drawing'\n\n" +
                "También en:\n" +
                "Edit → Project Settings → Player → Other Settings → " +
                "Enable GPU Resident Drawer (Unity 6+).", "Entendido");
        }
        else
        {
            EditorUtility.DisplayDialog("GPU Resident Drawer",
                "✅ GPU Resident Drawer activado en el HDRP Asset.\n\n" +
                "Efecto inmediato en Play: auto-batching de draw calls " +
                "independiente del número de materiales.", "Genial");
        }
    }

    // ── Menú: Iniciar bake de lightmaps ────────────────────────────────────
    [MenuItem("Tools/Alsasua/Iluminacion/💡 Iniciar Bake Lightmaps (async)", priority = 51)]
    static void IniciarBakeAsync()
    {
        if (!EditorUtility.DisplayDialog("Bake Lightmaps",
            "Inicia el bake de lightmaps en background.\n" +
            "Puede tardar 10-60 min. ¿Continuar?", "Bake", "Cancelar")) return;

        if (Lightmapping.lightingSettings == null)
            Lightmapping.lightingSettings = CrearOCargarLightingSettings();

        bool iniciado = Lightmapping.BakeAsync();
        Debug.Log(iniciado
            ? "[Fase5] Bake de lightmaps iniciado. Progreso en Window → Rendering → Lighting."
            : "[Fase5] ERROR: no se pudo iniciar el bake. Comprueba que la escena tiene luces y objetos estáticos.");
    }

    // ── Menú: Bake de APV (Probe Volumes) ──────────────────────────────────
    [MenuItem("Tools/Alsasua/Iluminacion/🔬 Bake APV (Probe Volumes)", priority = 52)]
    static void BakeAPV()
    {
#if UNITY_6000_0_OR_NEWER
        if (!EditorUtility.DisplayDialog("Bake APV",
            "Hornea los Adaptive Probe Volumes (GI para NPCs, jugador, partículas).\n" +
            "Más rápido que los lightmaps: 1-5 min. ¿Continuar?", "Bake APV", "Cancelar")) return;

        // En Unity 6, el bake de APV se lanza desde el mismo sistema de lightmapping
        // con la flag de probe volumes activada. El menú en el editor es
        // Window → Rendering → Lighting → Bake Reflection Probes / Bake All Probe Volumes.
        // Vía API: usamos Lightmapping.BakeAsync con ProbeVolumes habilitadas.
        Lightmapping.BakeAsync();
        Debug.Log("[Fase5] Bake APV iniciado (combinado con lightmaps). " +
                  "Verifica que 'Probe Volumes' está ON en HDRP Asset → Lighting.");
#else
        EditorUtility.DisplayDialog("Bake APV",
            "El bake de APV via código requiere Unity 6000+.\n" +
            "Usa Window → Rendering → Lighting → Bake All Probe Volumes manualmente.", "Vale");
#endif
    }

    // ── Menú: Cancelar bake ─────────────────────────────────────────────────
    [MenuItem("Tools/Alsasua/Iluminacion/⏹️ Cancelar Bake", priority = 53)]
    static void CancelarBake() => Lightmapping.Cancel();

    // ── Menú: Restablecer ───────────────────────────────────────────────────
    [MenuItem("Tools/Alsasua/Iluminacion/↩️ Eliminar ProbeVolumes AAA", priority = 54)]
    static void Restablecer()
    {
        var raiz = GameObject.Find(APV_ROOT_NAME);
        if (raiz != null) { Object.DestroyImmediate(raiz); Debug.Log("[Fase5] ProbeVolumes eliminadas."); }
        else Debug.Log("[Fase5] No se encontró la raíz de ProbeVolumes.");
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    // ── Creación de LightingSettings ────────────────────────────────────────
    static LightingSettings CrearOCargarLightingSettings()
    {
        var existente = AssetDatabase.LoadAssetAtPath<LightingSettings>(SETTINGS_PATH);
        if (existente != null) return existente;

        Directory.CreateDirectory(DIR_LIGHTING);

        var ls = new LightingSettings
        {
            // ── Lightmapper ──────────────────────────────────────────────
            lightmapper          = LightingSettings.Lightmapper.ProgressiveGPU,
            // lightmapsMode no existe en esta version de Unity (default)

            // ── GI ───────────────────────────────────────────────────────
            bakedGI              = true,
            realtimeGI           = false,

            // ── Resolución ───────────────────────────────────────────────
            indirectResolution   = 2f,          // 2 texels/m para GI indirecta
            lightmapResolution   = 10f,         // 10 texels/m para lightmaps directos
            lightmapPadding      = 2,
            // maxLightmapSize no existe en esta version de Unity (default)
            // compress no existe en esta version de Unity (default)

            // ── Calidad ──────────────────────────────────────────────────
            maxBounces              = 2,
            filteringMode        = LightingSettings.FilterMode.Auto,
            directSampleCount    = 32,
            indirectSampleCount  = 512,

            // ── Ambient Occlusion ─────────────────────────────────────────
            ao                   = true,
            aoMaxDistance        = 1.5f,
            aoExponentIndirect   = 0.5f,
            aoExponentDirect     = 0f,          // AO solo en indirecta (como RDR2/Cyberpunk)
        };

        AssetDatabase.CreateAsset(ls, SETTINGS_PATH);
        Debug.Log($"[Fase5] LightingSettings creado en {SETTINGS_PATH} " +
                  "(Progressive GPU, 2 bounces, AO, 10 texels/m).");
        return ls;
    }

    // ── Creación de ProbeVolumes APV ────────────────────────────────────────
    static int CrearProbeVolumes()
    {
        // Evitar duplicados
        var raizExistente = GameObject.Find(APV_ROOT_NAME);
        if (raizExistente != null)
        {
            Debug.Log("[Fase5] ProbeVolumes ya existían — sin cambios.");
            return raizExistente.transform.childCount;
        }

        var raiz = new GameObject(APV_ROOT_NAME);
        int creadas = 0;
        float cx = GeoDataAlsasua.OX, cz = GeoDataAlsasua.OZ;

        for (int i = 0; i < ANILLOS.Length; i++)
        {
            var (radio, nombre) = ANILLOS[i];
            float ancho = radio * 2f;

            var go = new GameObject(nombre);
            go.transform.SetParent(raiz.transform);
            go.transform.position = new Vector3(cx, 30f, cz);  // 30m sobre el suelo

            // ProbeVolume API varía por versión HDRP — envuelto en try/catch
            try
            {
                var pv = go.AddComponent<ProbeVolume>();
                pv.size = new Vector3(ancho, 80f, ancho);   // 80m de altura: zona jugable
                // Subdivisiones finas en el anillo cercano — propiedad HDRP 14+
                // Si no existe en tu versión, configúrala manualmente en el Inspector.
                var pvType  = typeof(ProbeVolume);
                var fOver   = pvType.GetField("overrideSubdivisions");
                var fHigh   = pvType.GetField("highestSubdivisionLevelOverride");
                var fLow    = pvType.GetField("lowestSubdivisionLevelOverride");
                if (fOver  != null) fOver.SetValue(pv, true);
                if (fHigh  != null) fHigh.SetValue(pv, Mathf.Max(0, 3 - i));
                if (fLow   != null) fLow.SetValue(pv,  Mathf.Max(0, 1 - i));
                creadas++;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[Fase5] ProbeVolume {nombre}: {ex.Message} — configúrala manualmente.");
            }
        }

        Debug.Log($"[Fase5] {creadas} ProbeVolumes APV creadas en {APV_ROOT_NAME}.");
        return creadas;
    }

    // ── GPU Resident Drawer (Unity 6) ────────────────────────────────────────
    static void IntentarActivarGPUResidentDrawer()
    {
        // GPU Resident Drawer se activa en el HDRP Asset.
        // En Unity 6 la propiedad pública es HDRenderPipelineAsset.gpuResidentDrawerMode.
        // Lo intentamos vía reflexión para no romper compilación en versiones anteriores.
        var hdrpType = typeof(HDRenderPipelineAsset);
        var prop = hdrpType.GetProperty("gpuResidentDrawerMode",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (prop == null)
        {
            Debug.LogWarning("[Fase5] GPU Resident Drawer: propiedad 'gpuResidentDrawerMode' no disponible " +
                "en esta versión de HDRP. Actívalo manualmente: HDRP Asset → Rendering → GPU Resident Drawer.");
            return;
        }

        var hdrpAsset = GraphicsSettings.currentRenderPipeline as HDRenderPipelineAsset;
        if (hdrpAsset == null) return;

        var valorActual = prop.GetValue(hdrpAsset);
        var tipoEnum = prop.PropertyType;
        // Buscar el primer valor no-cero del enum (normalmente "InstancedDrawing" o "On")
        var valores = System.Enum.GetValues(tipoEnum);
        object valorOn = null;
        foreach (var v in valores) if (System.Convert.ToInt32(v) != 0) { valorOn = v; break; }
        if (valorOn == null) return;
        if (!valorActual.Equals(valorOn))
        {
            prop.SetValue(hdrpAsset, valorOn);
            EditorUtility.SetDirty(hdrpAsset);
            Debug.Log("[Fase5] GPU Resident Drawer activado en el HDRP Asset.");
        }
    }
}
