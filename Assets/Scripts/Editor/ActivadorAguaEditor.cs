// Assets/Scripts/Editor/ActivadorAguaEditor.cs
// ═══════════════════════════════════════════════════════════════════════════
//  ACTIVADOR AGUA HDRP — Fase 7 del plan AAA
//
//  SistemaAguaRio.cs ya tiene toda la lógica de HDRP WaterSurface pero
//  requiere dos pasos manuales que esta herramienta automatiza:
//    1. Añadir ALSASUA_WATER a Scripting Define Symbols
//    2. Crear una WaterSurface en la escena centrada sobre el Río Arakil
//       (el corredor E-O que el río traza por el valle de Alsasua, cota ~525m)
//
//  WORKFLOW:
//    a) HDRP Asset → Lighting → Water = ON  (manual — no modificable por código)
//    b) Tools/Alsasua/Agua/💧 Activar Agua HDRP (este menú)
//    c) Asignar el WaterSurface creado al campo "aguaRio" de SistemaAguaRio
//    d) Tools/Alsasua/Agua/🌊 Ajustar WaterSurface a cauce exacto (optional)
//
//  GEOMETRÍA DEL ARAKIL EN ALSASUA:
//    El río cruza el valle de E a O a ~525m de altitud. En coords Unity:
//    Centro aproximado: (OX+0, cota_arakil, OZ+120) = (1918, 14, 8690)
//    Ancho medio: 25m (río Burunda/Arakil en este tramo)
//    Longitud cubierta: 3200m E-O
// ═══════════════════════════════════════════════════════════════════════════

using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

public static class ActivadorAguaEditor
{
    // Posición de la WaterSurface del Arakil en Unity
    // El río corre ~120m al norte del centro (Herriko Plaza)
    const float AGUA_X = GeoDataAlsasua.OX;
    const float AGUA_Z = GeoDataAlsasua.OZ + 120f;
    const float AGUA_ANCHO   = 25f;    // m (ancho del río)
    const float AGUA_LARGO   = 3200f;  // m (cobertura E-O)
    const float AGUA_COTA    = 14f;    // altura Unity ≈ cota_real 525m - datum 511.33m

    const string DEFINE_AGUA = "ALSASUA_WATER";

    // ── Menú principal ──────────────────────────────────────────────────────
    [MenuItem("Tools/Alsasua/Agua/💧 Activar Agua HDRP (define + WaterSurface)", priority = 60)]
    static void ActivarAgua()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid()) { Debug.LogError("[Agua] No hay escena activa."); return; }

        // 1. Añadir define ALSASUA_WATER
        bool defineAniadido = AniadirDefine(DEFINE_AGUA);

        // 2. Crear WaterSurface en escena si no existe ya
        bool wsCreado = CrearWaterSurface();

        // 3. Asegurar que SistemaAguaRio existe en la escena
        bool aguaRioExiste = Object.FindFirstObjectByType<SistemaAguaRio>() != null;
        if (!aguaRioExiste)
        {
            var go = new GameObject("SistemaAguaRio");
            go.AddComponent<SistemaAguaRio>();
            Debug.Log("[Agua] SistemaAguaRio creado en escena.");
        }

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();

        string msg = $"✅ Agua HDRP activada:\n" +
            $"· {DEFINE_AGUA}: {(defineAniadido ? "AÑADIDO" : "ya existía")}\n" +
            $"· WaterSurface: {(wsCreado ? "CREADA" : "ya existía")}\n\n" +
            "SIGUIENTE (manual):\n" +
            "1. HDRP Asset → Lighting → Water = ON\n" +
            "2. Asigna el 'WaterSurface_Arakil' al campo 'aguaRio' de SistemaAguaRio\n" +
            "3. Guarda la escena y dale a Play";

        Debug.Log($"[Agua] {msg}");
        EditorUtility.DisplayDialog("Activar Agua HDRP", msg, "Entendido");
    }

    [MenuItem("Tools/Alsasua/Agua/🌊 Ajustar WaterSurface al cauce exacto", priority = 61)]
    static void AjustarCauce()
    {
        var ws = Object.FindFirstObjectByType<WaterSurface>();
        if (ws == null)
        {
            EditorUtility.DisplayDialog("Ajustar cauce",
                "No hay WaterSurface en escena. Ejecuta 💧 Activar Agua primero.", "Vale");
            return;
        }

        // Ajustar posición, rotación y escala para seguir el Arakil E-O
        ws.transform.position = new Vector3(AGUA_X, AGUA_COTA, AGUA_Z);
        ws.transform.rotation = Quaternion.identity;

        // WaterSurface se escala con transform.localScale
        ws.transform.localScale = new Vector3(AGUA_LARGO, 1f, AGUA_ANCHO);

        EditorUtility.SetDirty(ws.gameObject);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[Agua] WaterSurface ajustada: pos=({AGUA_X:F0},{AGUA_COTA:F1},{AGUA_Z:F0}) " +
            $"escala={AGUA_LARGO}×{AGUA_ANCHO}m");
    }

    [MenuItem("Tools/Alsasua/Agua/❌ Desactivar Agua HDRP", priority = 62)]
    static void DesactivarAgua()
    {
        if (!EditorUtility.DisplayDialog("Desactivar Agua",
            $"Elimina el define {DEFINE_AGUA} y borra la WaterSurface del Arakil.", "Desactivar", "Cancelar"))
            return;

        EliminarDefine(DEFINE_AGUA);
        var ws = GameObject.Find("WaterSurface_Arakil");
        if (ws != null) Object.DestroyImmediate(ws);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[Agua] {DEFINE_AGUA} eliminado y WaterSurface borrada.");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────
    static bool CrearWaterSurface()
    {
        if (GameObject.Find("WaterSurface_Arakil") != null) return false;

        var go = new GameObject("WaterSurface_Arakil");
        go.transform.position = new Vector3(AGUA_X, AGUA_COTA, AGUA_Z);
        go.transform.localScale = new Vector3(AGUA_LARGO, 1f, AGUA_ANCHO);

        var ws = go.AddComponent<WaterSurface>();
        ws.surfaceType  = WaterSurfaceType.River;
        ws.geometryType = WaterGeometryType.Quad;
        // NOTA: las propiedades de simulación (windSpeed, foam, caustics) varían por versión
        // de HDRP. Configúralas en el Inspector del WaterSurface_Arakil después de crearlo.
        // En HDRP 16+ (Unity 6): busca "Ripples", "Foam", "Caustics" en el componente.

        // Auto-asignar al SistemaAguaRio si existe en escena (campo aguaRio solo con define)
        var aguaRio = Object.FindFirstObjectByType<SistemaAguaRio>();
        if (aguaRio != null)
        {
            var campo = typeof(SistemaAguaRio).GetField("aguaRio",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            campo?.SetValue(aguaRio, ws);
            EditorUtility.SetDirty(aguaRio);
            Debug.Log("[Agua] WaterSurface asignada automáticamente a SistemaAguaRio.aguaRio.");
        }

        Debug.Log($"[Agua] WaterSurface_Arakil creada en ({AGUA_X:F0},{AGUA_COTA:F1},{AGUA_Z:F0}), " +
            $"{AGUA_LARGO}m×{AGUA_ANCHO}m. Ajusta simulación en el Inspector.");
        return true;
    }

    static bool AniadirDefine(string define)
    {
        var group = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
        PlayerSettings.GetScriptingDefineSymbols(
            UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(group),
            out string[] defines);

        foreach (var d in defines)
            if (d == define) return false;

        var lista = new System.Collections.Generic.List<string>(defines) { define };
        PlayerSettings.SetScriptingDefineSymbols(
            UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(group),
            lista.ToArray());

        Debug.Log($"[Agua] Scripting define '{define}' añadido → recompilando...");
        return true;
    }

    static void EliminarDefine(string define)
    {
        var group = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
        PlayerSettings.GetScriptingDefineSymbols(
            UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(group),
            out string[] defines);

        var lista = new System.Collections.Generic.List<string>(defines);
        lista.Remove(define);
        PlayerSettings.SetScriptingDefineSymbols(
            UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(group),
            lista.ToArray());
    }
}
