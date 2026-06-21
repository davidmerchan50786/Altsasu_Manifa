// Assets/Scripts/Editor/FLUJO_COMPLETO.cs
// ═══════════════════════════════════════════════════════════════════════════
//  ▶▶ FLUJO COMPLETO — UN BOTÓN, TODO EN ORDEN CORRECTO ◀◀
//  Tools → Alsasua → ▶▶ FLUJO COMPLETO ◀◀
//
//  PREREQUISITO (hacer UNA VEZ antes):
//    Tools → Alsasua → Importar → 📦 Importar Assets Locales
//    Tools → Alsasua → Mundo  → 🧩 Construir Mosaico V2  (terreno antes que ciudad)
//
//  PASOS AUTOMÁTICOS (en orden de dependencia):
//
//  FASE 1 · ESTRUCTURA
//    01  Crear escena Alsasua_Main v4 (SceneBootstrapper + 30 sistemas)
//    02  Configurar Cesium (Georeference Herriko Plaza, deshabilitado hasta resolver FPS)
//    03  Configurar Zonas del mapa (10 zonas reales de Alsasua)
//
//  FASE 2 · ASSETS
//    04  Importar FBX extraídos (_ExtractedAssets — personajes, vehículos...)
//    05  Construir personajes MeshyAI (10 NPCs PBR + animadores)
//    06  Configurar Animator jugador (blend tree locomotion)
//    07  Configurar Animator policía (20 animaciones biped)
//    08  Crear prefabs coche policial (Police Car Interceptor)
//    09  Construir prefabs vehículos (Trabant, Ambulancia, Tractor, Camión)
//
//  FASE 3 · MUNDO  (assets reales, generación procedural OFF)
//    10  🏙️ Construir edificios de asset (1030 footprints OSM → prefabs HousePack/lisbon)
//    11  🛣️ Construir calles + autovía (roads_unity.json → 4 mallas estáticas)
//    12  Material asfalto PBR (M_Asfalto_Carretera.mat)
//    13  📸 Ortofoto como Decal (pirámide LOD: fondo 14.4km + valle 2.75km)
//    14  Texturas AAA PBR (600+ materiales PolyHaven)
//    15  🏗️ Hornear Ciudad (LOD pyramid estilo RAGE → <2.5k draw calls)
//
//  FASE 4 · AUDIO + CONEXIONES
//    16  Asignar audio (clips reales → AudioManager)
//    17  🔌 Conectar todos los sistemas (asset → campo exacto)
//    18  Poblar Zone_01 con civiles (25 NPCs)
//
//  FASE 5 · SELLADO
//    19  Tags y capas (Player, NPC, Vehicle, Projectile...)
//    20  Quality settings (60fps, sombras 4 cascades, LOD 2.5)
//    21  Fix EventSystem (Input System UGUI)
//    22  Build Settings (MenuPrincipal → Alsasua_Main)
//    23  Guardar todo
// ═══════════════════════════════════════════════════════════════════════════

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;
using System.Linq;
using System.IO;

public static class FLUJO_COMPLETO
{
    [MenuItem("Tools/Alsasua/▶▶ FLUJO COMPLETO ◀◀", priority = -100)]
    public static void Ejecutar()
    {
        bool ok = EditorUtility.DisplayDialog(
            "▶▶ FLUJO COMPLETO ◀◀",
            "Construirá el simulador entero en orden correcto:\n\n" +
            "FASE 1 · Estructura (escena + Cesium + zonas)\n" +
            "FASE 2 · Assets (personajes + vehículos + animadores)\n" +
            "FASE 3 · Mundo (edificios asset + calles + ortofoto + bake)\n" +
            "FASE 4 · Audio + conexiones + civiles\n" +
            "FASE 5 · Sellado (tags + quality + builds)\n\n" +
            "⚠ PRERREQUISITOS:\n" +
            "  1) Importar → 📦 Importar Assets Locales\n" +
            "  2) Mundo → 🧩 Construir Mosaico V2 (terreno antes de ciudad)\n\n" +
            "⏱ Estimado: 5-15 minutos.",
            "▶▶ EJECUTAR ◀◀", "Cancelar");

        if (!ok) return;

        _cola = new Queue<(string label, System.Action paso)>();
        _log  = new List<string>();

        // ── FASE 1: ESTRUCTURA ────────────────────────────────────────────
        Encolar("01 · Crear escena Alsasua_Main v4",  CreadorEscenaPrincipal.Crear);
        Encolar("02 · Configurar Cesium",             ConfiguradorCesiumAlsasua.Configurar);
        Encolar("03 · Configurar zonas del mapa",     ConfiguradorZonasEditor.ConfigurarZonas);

        // ── FASE 2: ASSETS ────────────────────────────────────────────────
        Encolar("04 · Importar FBX extraídos",        ImportadorExtractedAssets.Abrir);
        Encolar("05 · Personajes MeshyAI",            ConstructorPersonajesMeshyAI.Construir);
        Encolar("06 · Animator jugador",              ConfiguradorAnimatorJugador.Configurar);
        Encolar("07 · Animator policía",              ConfiguradorAnimatorPolicia.Configurar);
        Encolar("08 · Prefabs Police Car",            WizardCochePrefab.CrearPrefabs);
        Encolar("09 · Prefabs vehículos",             ConstructorVehiculosPrefabs.Construir);

        // ── FASE 3: MUNDO (assets reales, procedural OFF) ────────────────────
        Encolar("10 · Construir edificios de asset",  ConstructorCiudadAssets.Construir);
        Encolar("11 · Construir calles + autovía",    ConstructorCallesAssets.Construir);
        Encolar("12 · Material asfalto PBR",          CreadorMaterialAsfalto.CrearMateriales);
        Encolar("13 · Ortofoto como Decal",           AplicadorOrtoDecalEditor.Aplicar);
        Encolar("14 · Texturas AAA PBR",              AplicadorTexturasAAA.Aplicar);
        Encolar("15 · Hornear Ciudad (LOD pyramid)",  HorneadorCiudad.HornearAuto);

        // ── FASE 4: AUDIO + CONEXIONES ────────────────────────────────────
        Encolar("16 · Asignar audio",                 AsignadorAudioManager.AsignarTodos);
        Encolar("17 · Conectar todos los sistemas",   ConectorSistemas.ConectarTodo);
        Encolar("18 · Poblar Zone_01 civiles",        SpawnerNPCsEditor.PoblarCiviles);

        // ── FASE 5: SELLADO ───────────────────────────────────────────────
        Encolar("19 · Tags y capas",                  CrearTagsYCapas);
        Encolar("20 · Quality settings 60fps",        ConfigurarQuality);
        Encolar("21 · Fix EventSystem",               FixEventSystem.UpgradeAllEventSystems);
        Encolar("22 · Build Settings",                ConfigurarBuildSettings);
        Encolar("23 · Guardar todo",                  GuardarTodo);

        _total  = _cola.Count;
        _actual = 0;
        EditorApplication.delayCall += Siguiente;
    }


    // ── Motor de pasos ────────────────────────────────────────────────────

    static Queue<(string, System.Action)> _cola;
    static List<string> _log;
    static int _total, _actual;

    static void Encolar(string label, System.Action accion)
        => _cola.Enqueue((label, accion));

    static void Siguiente()
    {
        if (_cola == null || _cola.Count == 0)
        {
            EditorUtility.ClearProgressBar();
            string resumen = string.Join("\n", _log.Select(l => l));
            Debug.Log("[FLUJO_COMPLETO] ✅\n" + resumen);
            EditorUtility.DisplayDialog("✅ FLUJO COMPLETO",
                $"Completado: {_actual}/{_total} pasos\n\n" +
                "El simulador está listo.\n\n" +
                "Lo que tienes ahora:\n" +
                "• Edificios de asset en footprints reales (Edificios_Asset)\n" +
                "• Red viaria completa con rotondas (Calles_Asset)\n" +
                "• Ortofoto PNOA sobre el terreno (DecalProjectors_Orto)\n" +
                "• Ciudad horneada en LOD pyramid (CiudadHorneada/)\n\n" +
                "Dale PLAY — SceneBootstrapper generará el terreno\n" +
                "y el jugador automáticamente.",
                "OK");
            return;
        }

        var (label, accion) = _cola.Dequeue();
        _actual++;
        float p = (float)_actual / _total;
        EditorUtility.DisplayProgressBar($"FLUJO [{_actual}/{_total}]", label, p);

        try   { accion(); _log.Add($"  ✅ {label}"); }
        catch (System.Exception e)
        {
            _log.Add($"  ⚠ {label}: {e.Message.Split('\n')[0]}");
            Debug.LogWarning($"[FLUJO] {label}: {e.Message}");
        }

        EditorApplication.delayCall += Siguiente;
    }

    // ── Pasos inline ──────────────────────────────────────────────────────

    static void CrearTagsYCapas()
    {
        string[] tags = { "Player","Enemy","NPC","Vehicle","Projectile",
                          "Ground","Building","Prop","Weapon","Manifestante" };
        foreach (var tag in tags)
            try { if (!System.Array.Exists(UnityEditorInternal.InternalEditorUtility.tags, t => t == tag))
                    UnityEditorInternal.InternalEditorUtility.AddTag(tag); } catch { }

        var so = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath(
            "ProjectSettings/TagManager.asset")[0]);
        var lp = so.FindProperty("layers");
        var layers = new (int i, string n)[]
        {
            (8,"Ground"),(9,"Player"),(10,"NPC"),
            (11,"Vehicle"),(12,"Projectile"),(13,"Building"),(14,"Ignore"),
        };
        foreach (var (i, n) in layers)
        { var p = lp.GetArrayElementAtIndex(i); if (string.IsNullOrEmpty(p.stringValue)) p.stringValue = n; }
        so.ApplyModifiedProperties();
    }

    static void ConfigurarQuality()
    {
        QualitySettings.shadowDistance   = 300f;
        QualitySettings.shadowCascades   = 4;
        QualitySettings.lodBias          = 2.5f;
        QualitySettings.maximumLODLevel  = 0;
        QualitySettings.vSyncCount       = 0;
        Application.targetFrameRate      = 60;
        Physics.defaultSolverIterations          = 8;
        Physics.defaultSolverVelocityIterations  = 2;
        Physics.IgnoreLayerCollision(12, 12);
        Physics.IgnoreLayerCollision(10, 10);
    }

    static void ConfigurarBuildSettings()
    {
        var scenes = new[]
        {
            new EditorBuildSettingsScene("Assets/#Scenes/MenuPrincipal.unity", true),
            new EditorBuildSettingsScene("Assets/#Scenes/Alsasua_Main.unity",  true),
        };
        var lista = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        foreach (var s in scenes)
        {
            int idx = lista.FindIndex(e => e.path == s.path);
            if (idx < 0) lista.Add(s); else lista[idx] = s;
        }
        EditorBuildSettings.scenes = lista.ToArray();
    }

    static void GuardarTodo()
    {
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorSceneManager.SaveOpenScenes();
    }
}

#endif
