// Assets/Scripts/Editor/CreadorEscenaPrincipal.cs
// Tools → Alsasua → 🎬 Crear Escena Principal Alsasua
//
// Construye Assets/#Scenes/Alsasua_Main.unity con jerarquía completa v4:
//   SceneBootstrapper  → terreno DEM, jugador, cámara, NavMesh (en Play)
//   GameManager        → AudioManager, GameManagerAltsasua, SistemaOpciones
//   AltsasuCore        → coordinador central con todos los sistemas cableados
//   Mundo/             → Terreno, NavMesh, OSM, Zonas, Edificios, Árboles
//   Simulacion/        → Atmósfera, Clima, Tráfico, Vegetación, Fauna, Multitud
//   Gameplay/          → Disparo, Armas, Destrucción, Misiones, Polish, UI
//   Ambiente/          → Sol, Audio ambiente, Cesium

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;
using System.Reflection;

public static class CreadorEscenaPrincipal
{
    const string RUTA_ESCENA = "Assets/#Scenes/Alsasua_Main.unity";

    [MenuItem("Tools/Alsasua/Escena/🎬 Crear Escena Principal", priority = 10)]
    public static void Crear()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var escena = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ── 1. SceneBootstrapper (ejecución -200, lo primero) ─────────────
        var bootstrapGO = Nuevo("SceneBootstrapper");
        var sb = bootstrapGO.AddComponent<SceneBootstrapper>();
        Set(sb, "centroX",                  1918f);
        Set(sb, "centroZ",                  8570f);
        Set(sb, "generarTerrenoDesdeDEM",   true);
        Set(sb, "usarPlanoCuadradoFallback", true);
        Set(sb, "crearJugadorSiNoHay",      true);

        // ── 2. GameManager (core sin dependencias) ────────────────────────
        var gmGO = Nuevo("GameManager");
        gmGO.AddComponent<GameManagerAltsasua>();
        gmGO.AddComponent<AudioManager>();
        // SistemaOpciones es static — AltsasuCore llama SistemaOpciones.AplicarTodo() directamente
        gmGO.AddComponent<SistemaApoyoPopular>();

        // ── 3. AltsasuCore (coordinador -100) ─────────────────────────────
        var coreGO = Nuevo("AltsasuCore");
        var core   = coreGO.AddComponent<AltsasuCore>();

        // ── 4. Mundo ──────────────────────────────────────────────────────
        var mundoGO = Nuevo("Mundo");

        var navGO      = HijoNuevo("NavMesh",       mundoGO);
        var navMesh    = navGO.AddComponent<SistemaNavMesh>();

        var osmGO      = HijoNuevo("WorldOSM",      mundoGO);
        var genMundo   = osmGO.AddComponent<GeneradorMundoOSM>();

        var zonasGO    = HijoNuevo("Zonas",         mundoGO);
        var zonas      = zonasGO.AddComponent<SistemaZonas>();
        zonasGO.AddComponent<GestorZonasAlsasua>();
        zonasGO.AddComponent<SistemaChunks>(); // requerido por GestorZonasAlsasua.Awake()

        var edificGO   = HijoNuevo("Edificios",     mundoGO);
        var edificios  = edificGO.AddComponent<SistemaEdificiosAAA>();
        edificGO.AddComponent<GeneradorGeometriaPrecisa>(); // geometría exacta OSM

        var terrenoGO  = HijoNuevo("Terreno",       mundoGO);
        var terreno    = terrenoGO.AddComponent<SistemaTerreno>();
        terrenoGO.AddComponent<SistemaSueloAAA>();
        terrenoGO.AddComponent<OptimizadorTerreno>();
        terrenoGO.AddComponent<AplicadorOrtofoto>(); // ortofoto IGN PNOA 25cm/px
        var gestorMat  = terrenoGO.AddComponent<GestorMaterialesAlsasua>();
        AsignarMaterialesAlGestor(gestorMat);         // carga los 280 mats PBR

        var treesGO    = HijoNuevo("TreeStreamer",  mundoGO);
        var trees      = treesGO.AddComponent<AlsasuaTreeStreamer>();

        // ── 5. Simulación ─────────────────────────────────────────────────
        var simGO = Nuevo("Simulacion");

        var atmosGO    = HijoNuevo("Atmosfera",     simGO);
        var atmosfera  = atmosGO.AddComponent<SistemaAtmosfera>();
        var clima      = atmosGO.AddComponent<SistemaClima>();
        var paranoia   = atmosGO.AddComponent<SistemaParanoia>();

        var trafGO     = HijoNuevo("Trafico",       simGO);
        var trafico    = trafGO.AddComponent<SistemaTrafico>();

        var vegGO      = HijoNuevo("Vegetacion",    simGO);
        var vegetacion = vegGO.AddComponent<SistemaVegetacion>();

        var faunaGO    = HijoNuevo("Fauna",         simGO);
        var fauna      = faunaGO.AddComponent<SistemaFauna>();

        var multGO     = HijoNuevo("Multitud",      simGO);
        var multitud   = multGO.AddComponent<SistemaMultitud>();
        multGO.AddComponent<SistemaManifestacion>();

        // ── 6. Gameplay ───────────────────────────────────────────────────
        var gameplayGO = Nuevo("Gameplay");

        var disparoGO  = HijoNuevo("Disparo",       gameplayGO);
        var disparo    = disparoGO.AddComponent<SistemaDisparo>();
        var armas      = disparoGO.AddComponent<SistemaArmasExtendido>();
        var bombas     = disparoGO.AddComponent<SistemaBombas>();

        var destrucGO  = HijoNuevo("Destruccion",   gameplayGO);
        var destruccion= destrucGO.AddComponent<SistemaDestruccion>();
        var barricadas = destrucGO.AddComponent<SistemaBarricadas>();
        var impactos   = destrucGO.AddComponent<SistemaImpactos>();
        // SistemaExplosion y SistemaRagdoll son clases static — no se añaden como componente

        var grafGO     = HijoNuevo("Grafitis",      gameplayGO);
        var grafitis   = grafGO.AddComponent<SistemaGrafitis>();

        var misGO      = HijoNuevo("Misiones",      gameplayGO);
        var misiones   = misGO.AddComponent<SistemaMisiones>();
        var misionesSec= misGO.AddComponent<GestorMisionesSecundarias>();
        var logros     = misGO.AddComponent<SistemaLogros>();
        misGO.AddComponent<SistemaTutorial>();

        var polishGO   = HijoNuevo("Polish",        gameplayGO);
        var polish     = polishGO.AddComponent<SistemaPolish>();
        var reverb     = polishGO.AddComponent<SistemaReverbZonas>();

        var savedGO    = HijoNuevo("Guardado",      gameplayGO);
        var guardado   = savedGO.AddComponent<SistemaGuardado>();

        var optGO      = HijoNuevo("Optimizacion",  gameplayGO);
        var optimiz    = optGO.AddComponent<SistemaOptimizacion>();
        var diagnos    = optGO.AddComponent<SistemaDiagnostico>();
        optGO.AddComponent<SistemaSeguridad>(); // monitor de salud runtime

        // ── 7. UI ─────────────────────────────────────────────────────────
        var uiGO    = Nuevo("UI");
        var hudGO   = HijoNuevo("HUD",      uiGO);
        var hud     = hudGO.AddComponent<HUDCanvas>();
        var pausaGO = HijoNuevo("Pausa",    uiGO);
        var pausa   = pausaGO.AddComponent<MenuPausa>();

        // ── 8. Ambiente ───────────────────────────────────────────────────
        var ambGO = Nuevo("Ambiente");

        var solGO = HijoNuevo("Sol_Directional", ambGO);
        var luz   = solGO.AddComponent<Light>();
        luz.type      = LightType.Directional;
        luz.intensity = 4f;
        luz.color     = new Color(1f, 0.95f, 0.85f);
        luz.shadows   = LightShadows.Soft;
        solGO.transform.rotation = Quaternion.Euler(52f, -30f, 0f);

        var audioAmbGO = HijoNuevo("SonidosAmbiente", ambGO);
        audioAmbGO.AddComponent<SonidosAmbienteAltsasu>();
        audioAmbGO.AddComponent<SistemaMusica>(); // requerido por SistemaExplosion.Instance

        var cesiumGO = HijoNuevo("CesiumCapas", ambGO);
        var cesiumCapas = cesiumGO.AddComponent<CesiumCapasAlsasua>();
        Set(cesiumCapas, "treeStreamer", trees); // AlsasuaTreeStreamer del GO Mundo/TreeStreamer

        // ── 9. Cablear AltsasuCore ────────────────────────────────────────
        Set(core, "audioManagerSystem",   gmGO.GetComponent<AudioManager>());
        Set(core, "wantedSystem",         gmGO.GetComponent<GameManagerAltsasua>());
        Set(core, "apoyoSystem",          gmGO.GetComponent<SistemaApoyoPopular>());
        Set(core, "atmosferaSystem",      atmosfera);
        Set(core, "navMeshSystem",        navMesh);
        Set(core, "generadorMundoSystem", genMundo);
        Set(core, "zonasSystem",          zonas);
        Set(core, "traficoSystem",        trafico);
        Set(core, "vegetacionSystem",     vegetacion);
        Set(core, "faunaSystem",          fauna);
        Set(core, "multitudSystem",       multitud);
        Set(core, "climaSystem",          clima);
        Set(core, "paranoiaSystem",       paranoia);
        Set(core, "disparoSystem",        disparo);
        Set(core, "armasSystem",          armas);
        Set(core, "bombasSystem",         bombas);
        Set(core, "barricadasSystem",     barricadas);
        Set(core, "destruccionSystem",    destruccion);
        Set(core, "grafitisSystem",       grafitis);
        Set(core, "manifestacionSystem",  multGO.GetComponent<SistemaManifestacion>());
        Set(core, "polishSystem",         polish);
        Set(core, "impactosSystem",       impactos);
        Set(core, "guardadoSystem",       guardado);
        Set(core, "logrosSystem",         logros);
        Set(core, "reverbSystem",         reverb);
        Set(core, "tutorialSystem",       misGO.GetComponent<SistemaTutorial>());
        Set(core, "misionesSystem",       misiones);
        Set(core, "misionesSecSystem",    misionesSec);
        Set(core, "hudCanvasSystem",      hud);
        Set(core, "menuPausaSystem",      pausa);
        Set(core, "optimizacionSystem",   optimiz);
        Set(core, "diagnosticoSystem",    diagnos);
        Set(core, "edificiosAAASystem",   edificios);
        Set(core, "terrenoSystem",        terreno);
        Set(core, "centroX",              1918f);
        Set(core, "centroZ",              8570f);

        // ── 10. SistemaClima necesita el sol ──────────────────────────────
        Set(clima, "solDireccional", luz);

        // ── Guardar ───────────────────────────────────────────────────────
        Directory.CreateDirectory(Path.GetDirectoryName(
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", RUTA_ESCENA))));
        EditorSceneManager.SaveScene(escena, RUTA_ESCENA);
        AnadirBuildSettings(RUTA_ESCENA);
        AssetDatabase.Refresh();

        Debug.Log("[Escena] ✅ Alsasua_Main.unity creada con jerarquía v4 completa.");
        EditorUtility.DisplayDialog("✅ Escena creada",
            "Alsasua_Main.unity lista con:\n\n" +
            "• SceneBootstrapper — terreno DEM + jugador al Play\n" +
            "• AltsasuCore v4 — 30 sistemas cableados\n" +
            "• Mundo: NavMesh, OSM, Zonas, Edificios, Árboles\n" +
            "• Simulación: Clima, Tráfico, Fauna, Multitud\n" +
            "• Gameplay: Disparo, Destrucción, Misiones, Polish\n" +
            "• UI: HUD, Menú pausa\n" +
            "• Ambiente: Sol, Audio, Cesium\n\n" +
            "Dale Play — SceneBootstrapper construye el terreno\n" +
            "y spawn el jugador automáticamente.", "OK");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    static GameObject Nuevo(string nombre)
        => new GameObject(nombre);

    static GameObject HijoNuevo(string nombre, GameObject padre)
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(padre.transform, false);
        return go;
    }

    static void Set(object target, string campo, object valor)
    {
        if (target == null || valor == null) return;
        var f = target.GetType().GetField(campo,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        f?.SetValue(target, valor);
    }

    static void AnadirBuildSettings(string ruta)
    {
        var scenes = EditorBuildSettings.scenes;
        foreach (var s in scenes)
            if (s.path == ruta) return;
        var lista = new System.Collections.Generic.List<EditorBuildSettingsScene>(scenes);
        lista.Add(new EditorBuildSettingsScene(ruta, true));
        EditorBuildSettings.scenes = lista.ToArray();
    }

    // ── Asigna los 280 materiales PBR al GestorMaterialesAlsasua ──────────────

    static Material Cargar(string path)
        => AssetDatabase.LoadAssetAtPath<Material>(path);

    static void AsignarMaterialesAlGestor(GestorMaterialesAlsasua g)
    {
        const string E  = "Assets/Materials/Edificios/";
        const string R  = "Assets/Materials/Roads/";

        // Paredes
        g.matParedRevoco      = Cargar(E + "rough_plaster_03.mat")
                             ?? Cargar(E + "clay_plaster.mat");
        g.matParedRevocoBeis  = Cargar(E + "beige_wall_001.mat")
                             ?? Cargar(E + "beige_wall_002.mat");
        g.matParedArcilaPlast = Cargar(E + "clay_plaster.mat");
        g.matParedPiedra      = Cargar(E + "sandstone_brick_wall_01.mat")
                             ?? Cargar(E + "sandstone_blocks_04.mat");
        g.matParedSilleria    = Cargar(E + "stone_brick_wall_001.mat");
        g.matParedLadrillo    = Cargar(E + "brick_wall_001.mat")
                             ?? Cargar(E + "worn_brick_wall.mat");
        g.matParedIglesia     = Cargar(E + "church_bricks_02.mat")
                             ?? Cargar(E + "church_bricks_03.mat");
        g.matParedHormigon    = Cargar(E + "brushed_concrete.mat");
        g.matParedHormigonMod = Cargar(E + "brushed_concrete_2.mat")
                             ?? Cargar(E + "brushed_concrete_03.mat");

        // Tejados
        g.matTejadoPizarra    = Cargar(E + "castle_wall_slates.mat");
        g.matTejadoHormigon   = Cargar(E + "anti_slip_concrete.mat");
        g.matTejadoHormigonTex= Cargar(E + "brushed_concrete_03.mat");
        g.matTejadoTeja       = Cargar(E + "clay_roof_tiles_02.mat")
                             ?? Cargar(E + "clay_roof_tiles.mat");
        g.matTejadoCeramica   = Cargar(E + "ceramic_roof_01.mat");
        g.matTejadoFibro      = Cargar(E + "asbestos_sheet_02.mat")
                             ?? Cargar(E + "asbestos_sheet.mat");

        // Aceras
        g.matAceraAdoquin     = Cargar(E + "cobblestone_floor_001.mat")
                             ?? Cargar(E + "cobblestone_02.mat");
        g.matAceraAdoquinGrande= Cargar(E + "cobblestone_large_01.mat")
                             ?? Cargar(E + "cobblestone_floor_02.mat");
        g.matAceraHormigon    = Cargar(E + "anti_slip_concrete.mat");
        g.matAceraBaldosa     = Cargar(E + "brick_pavement_02.mat")
                             ?? Cargar(E + "brick_pavement_03.mat");
        g.matAceraEmpedrado   = Cargar(E + "cobblestone_pavement.mat")
                             ?? Cargar(E + "cobblestone_01.mat");

        // Calzadas
        g.matCalzadaAsfalto   = Cargar(R + "M_Asfalto_Carretera.mat");
        g.matCalzadaAdoquin   = Cargar(E + "cobblestone_01.mat");
        g.matCalzadaTierra    = Cargar(E + "brown_mud_dry.mat")
                             ?? Cargar(E + "brown_mud.mat");

        // Bordillo
        g.matBordillo         = Cargar(E + "brushed_concrete_03.mat")
                             ?? Cargar(E + "anti_slip_concrete.mat");

        int asignados = 0;
        foreach (var f in typeof(GestorMaterialesAlsasua).GetFields(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
            if (f.FieldType == typeof(Material) && f.GetValue(g) != null) asignados++;

        Debug.Log($"[GestorMat] {asignados} materiales PBR asignados al gestor");
    }
}
#endif
