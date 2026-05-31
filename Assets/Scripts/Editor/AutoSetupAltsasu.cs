// Assets/Scripts/Editor/AutoSetupAltsasu.cs
// ═══════════════════════════════════════════════════════════════════════════
//  AUTO SETUP — Se ejecuta automáticamente al abrir Unity.
//  Crea los GameObjects de la escena, asigna materiales y genera las calles.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class AutoSetupAltsasu
{
    const string KEY_DONE      = "AltsasuAutoSetup_v2_Done";
    const string ESCENA_TARGET = "OutdoorsScene";
    const string MAT_ASFALTO   = "Assets/Materials/Roads/M_Asfalto_Carretera.mat";
    const string MAT_ARCEN     = "Assets/Materials/Roads/M_Arcen_Hormigon.mat";
    const string MAT_LINEAS    = "Assets/Materials/Roads/M_Lineas_Carretera.mat";

    static AutoSetupAltsasu()
    {
        // Solo ejecutar una vez por proyecto (hasta que el usuario lo resetee)
        if (SessionState.GetBool(KEY_DONE, false)) return;
        EditorApplication.delayCall += EjecutarSetup;
    }

    [MenuItem("Altsasu GTA/Forzar Setup Completo", false, 50)]
    static void ForzarSetup()
    {
        SessionState.SetBool(KEY_DONE, false);
        EjecutarSetup();
    }

    static void EjecutarSetup()
    {
        SessionState.SetBool(KEY_DONE, true);

        // Asegurarse de estar en OutdoorsScene
        var escenaActiva = SceneManager.GetActiveScene();
        if (!escenaActiva.name.Contains(ESCENA_TARGET))
        {
            Debug.Log($"[AutoSetup] Escena activa: '{escenaActiva.name}'. Para setup completo abre {ESCENA_TARGET}.unity");
            // Intentar cargar la escena correcta
            string[] guids = AssetDatabase.FindAssets($"t:Scene {ESCENA_TARGET}");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                if (EditorUtility.DisplayDialog("Altsasu GTA Auto-Setup",
                    $"Se abrirá '{ESCENA_TARGET}' para configurar la escena automáticamente.",
                    "Abrir y configurar", "Cancelar"))
                {
                    EditorSceneManager.OpenScene(path);
                    EditorApplication.delayCall += () => ConfigurarEscena();
                    return;
                }
            }
        }

        ConfigurarEscena();
    }

    static void ConfigurarEscena()
    {
        var mat_asfalto = AssetDatabase.LoadAssetAtPath<Material>(MAT_ASFALTO);
        var mat_arcen   = AssetDatabase.LoadAssetAtPath<Material>(MAT_ARCEN);
        var mat_lineas  = AssetDatabase.LoadAssetAtPath<Material>(MAT_LINEAS);

        bool materialesOk = mat_asfalto && mat_arcen && mat_lineas;
        if (!materialesOk)
            Debug.LogWarning("[AutoSetup] Materiales de asfalto no encontrados. Ejecuta Altsasu GTA → Crear Materiales.");

        // ── 1. GeneradorCallesAltsasu ─────────────────────────────────────
        var genCalles = SetupComponente<GeneradorCallesAltsasu>("GeneradorCalles");
        if (genCalles != null && materialesOk)
        {
            genCalles.materialAsfalto   = mat_asfalto;
            genCalles.materialArcen     = mat_arcen;
            genCalles.materialLineas    = mat_lineas;
            genCalles.materialPeatonal  = mat_arcen;
            EditorUtility.SetDirty(genCalles.gameObject);
            // Generar las calles inmediatamente
            genCalles.GenerarTodas();
            Debug.Log("[AutoSetup] ✓ Calles generadas con texturas de asfalto PBR.");
        }

        // ── 2. GameManagerAltsasua ────────────────────────────────────────
        var gm = SetupComponente<GameManagerAltsasua>("GameManager");
        if (gm != null)
        {
            // Asignar prefabs desde Assets
            gm.prefabCochePolicia = CargarPrefab("Interceptor");
            gm.prefabHelicoptero  = CargarPrefab("Helicopter");
            gm.prefabEnemigo      = CargarPrefab("Z Walker");

            // Árboles Tree10
            gm.prefabsArboles = new GameObject[]
            {
                CargarPrefab("Tree10_1"), CargarPrefab("Tree10_2"),
                CargarPrefab("Tree10_3"), CargarPrefab("Tree10_4"),
                CargarPrefab("Tree10_5"),
            };

            // Terreno
            var terreno = GameObject.Find("GroundPointsMesh");
            if (terreno) gm.terrenoCloudCompare = terreno;

            EditorUtility.SetDirty(gm.gameObject);
            Debug.Log("[AutoSetup] ✓ GameManager configurado.");
        }

        // ── 3. OptimizadorTerreno ─────────────────────────────────────────
        var terrenoGO = GameObject.Find("GroundPointsMesh");
        if (terrenoGO != null)
        {
            var opt = terrenoGO.GetComponent<OptimizadorTerreno>()
                   ?? Undo.AddComponent<OptimizadorTerreno>(terrenoGO);
            var texCC = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/EscenarioSuelo/Alsasua_Color.png");
            if (texCC) opt.texturaCCOlor = texCC;
            EditorUtility.SetDirty(terrenoGO);
            Debug.Log("[AutoSetup] ✓ OptimizadorTerreno configurado en GroundPointsMesh.");
        }

        // ── Guardar escena ────────────────────────────────────────────────
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        Debug.Log("[AutoSetup] ✅ Setup completo. Pulsa Play para jugar.");
        EditorUtility.DisplayDialog("Altsasu GTA — Setup Completo",
            "✅ Todo configurado automáticamente:\n\n" +
            "• Calles generadas con asfalto PBR\n" +
            "• GameManager con policía, helicóptero y enemigos\n" +
            "• Terreno CloudCompare optimizado\n\n" +
            "Pulsa ▶ Play para jugar.", "¡Vamos!");
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    static T SetupComponente<T>(string goName) where T : Component
    {
        var existing = Object.FindFirstObjectByType<T>();
        if (existing != null) return existing;

        var go = new GameObject(goName);
        Undo.RegisterCreatedObjectUndo(go, goName);
        return go.AddComponent<T>();
    }

    static GameObject CargarPrefab(string nombre)
    {
        string[] guids = AssetDatabase.FindAssets($"t:Prefab {nombre}");
        if (guids.Length > 0)
            return AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guids[0]));
        return null;
    }
}
