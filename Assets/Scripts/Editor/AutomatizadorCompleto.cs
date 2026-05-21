// Assets/Scripts/Editor/AutomatizadorCompleto.cs
// ═══════════════════════════════════════════════════════════════════════════
//  Tools → Alsasua → ⚡ AUTOMATIZAR TODO
//
//  Ejecuta en orden todo lo que puede hacerse sin intervención manual:
//
//  1. Tags y capas           → Vehicle, Enemy, NPC, Terrain en TagManager
//  2. Audio                  → asigna los WAV/MP3 a SonidosAmbienteAltsasu
//                              y AudioManager por nombre
//  3. Motor audio en coche   → Engine_07.wav → ControladorVehiculoJugador
//  4. Sistemas en escena     → crea GameManager, AudioManager, NavMeshManager
//                              si no existen
//  5. Waypoints policía      → GOs Transform de PatrullaPolForal en Zone_01
//  6. Coches en escena       → coloca Interceptor_Jugador en calle cercana
//  7. Materiales HDRP        → convierte todos los Standard de assets importados
//  8. Quality settings       → configura HDRP correctamente
//  9. Physics matrix         → Vehicle ignora NPC, NPC ignora NPC
// 10. Animator controller    → crea y asigna GTA_Player.controller
// 11. Edificios Zone_01      → genera 548 edificios OSM si no existen
// 12. Prefabs de coche       → crea Interceptor_Jugador y Trabant_Civil
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;

public static class AutomatizadorCompleto
{
    [MenuItem("Tools/Alsasua/⚡ AUTOMATIZAR TODO", priority = 1)]
    public static void AutomatizarTodo()
    {
        int pasos = 19, ok = 0;

        void Paso(int n, string nombre, System.Action fn)
        {
            EditorUtility.DisplayProgressBar("Automatizando...", $"[{n}/{pasos}] {nombre}", (float)n/pasos);
            try { fn(); ok++; Debug.Log($"[Auto] ✅ {n}. {nombre}"); }
            catch (System.Exception e) { Debug.LogError($"[Auto] ❌ {n}. {nombre}: {e.Message}"); }
        }

        Paso(1,  "Tags y capas",             CrearTagsYCapas);
        Paso(2,  "Audio ambiente",            AsignarAudioAmbiente);
        Paso(3,  "Audio armas y pasos",       AsignarAudioArmas);
        Paso(4,  "Sistemas en escena",        AsegurarSistemasEscena);
        Paso(5,  "Waypoints policía",         CrearWaypointsPolicia);
        Paso(6,  "Prefabs de coche",          () => WizardCochePrefab.CrearPrefabs());
        Paso(7,  "Animator jugador",          (System.Action)ConfiguradorAnimatorJugador.Configurar);
        Paso(8,  "Edificios Zone_01",         CrearEdificiosSiNoExisten);
        Paso(9,  "Carreteras Zone_01",        () => GeneradorCarreteras.Generar());
        Paso(10, "Civiles en escena",         () => SpawnerNPCsEditor.PoblarCiviles());
        Paso(11, "Coche en escena",           ColocarCocheEnEscena);
        Paso(12, "Fachadas vascas",            () => FachadasVascas.AplicarFachadas());
        Paso(13, "Configurar tráfico OSM",    () => ConfiguradorTrafico.Configurar());
        Paso(14, "Conectar sistemas null",     ConectarSistemasNull);
        Paso(15, "HDRP configurar",           ConfigurarHDRP);
        Paso(16, "Materiales HDRP",           () => IntegradorAssetsNuevos.ConvertirMaterialesAHDRP());
        Paso(17, "Occlusion Culling",         ConfigurarOcclusion);
        Paso(18, "Quality settings",          ConfigurarQuality);
        Paso(19, "Physics collision matrix",  ConfigurarPhysicsMatrix);

        EditorUtility.ClearProgressBar();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Automatización completada",
            $"✅ {ok}/{pasos} pasos completados.\n\nRevisa la Consola para detalles.\n\n" +
            "Lo que todavía requiere ajuste manual:\n" +
            "• Posición exacta de los WheelColliders en el Interceptor\n" +
            "• Humanoid Avatar para personajes Meshy AI\n" +
            "• Texturas fotorreales de fachadas\n" +
            "• Misiones y objetivos", "OK");
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  1. TAGS Y CAPAS
    // ─────────────────────────────────────────────────────────────────────────

    public static void CrearTagsYCapas()
    {
        var tagManager = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);

        // Tags
        var tagsProp = tagManager.FindProperty("tags");
        foreach (var tag in new[]{ "Vehicle", "Enemy", "NPC", "Waypoint", "Interactable" })
            AnadirTag(tagsProp, tag);

        // Layers (slots 8-11 reservados)
        var layersProp = tagManager.FindProperty("layers");
        SetLayer(layersProp,  8, "Terrain");
        SetLayer(layersProp,  9, "Vehicle");
        SetLayer(layersProp, 10, "NPC");
        SetLayer(layersProp, 11, "Interactable");

        tagManager.ApplyModifiedProperties();
    }

    static void AnadirTag(SerializedProperty tagsProp, string nombre)
    {
        for (int i = 0; i < tagsProp.arraySize; i++)
            if (tagsProp.GetArrayElementAtIndex(i).stringValue == nombre) return;
        tagsProp.InsertArrayElementAtIndex(tagsProp.arraySize);
        tagsProp.GetArrayElementAtIndex(tagsProp.arraySize - 1).stringValue = nombre;
    }

    static void SetLayer(SerializedProperty layersProp, int idx, string nombre)
    {
        if (idx >= layersProp.arraySize) return;
        var el = layersProp.GetArrayElementAtIndex(idx);
        if (string.IsNullOrEmpty(el.stringValue)) el.stringValue = nombre;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  2. AUDIO AMBIENTE
    // ─────────────────────────────────────────────────────────────────────────

    public static void AsignarAudioAmbiente()
    {
        var sonidos = Object.FindFirstObjectByType<SonidosAmbienteAltsasu>();
        if (sonidos == null) return;

        bool dirty = false;
        // Buscar en todas las rutas posibles (el audio puede estar en varios sitios)
        dirty |= AsignarClip(sonidos, "clipTrafico",
            "Assets/Audio/Ambiente/TrafficLoop.WAV", "Assets/Audio/TrafficLoop.WAV",
            "Assets/#Sounds/TrafficLoop.WAV");
        dirty |= AsignarClip(sonidos, "clipSirena",
            "Assets/Audio/Ambiente/PoliceSiren.WAV", "Assets/Audio/PoliceSiren.WAV",
            "Assets/#Sounds/PoliceSiren.WAV", "Assets/#Tools/Resources/Sounds/Weapons/ShootGun.wav");
        dirty |= AsignarClip(sonidos, "clipRadio",
            "Assets/Audio/Ambiente/PoliceRadio.WAV", "Assets/Audio/PoliceRadio.WAV",
            "Assets/#Sounds/PoliceRadio.WAV");
        dirty |= AsignarClip(sonidos, "clipFuego",
            "Assets/Audio/Ambiente/Fire.WAV", "Assets/Audio/Fire.WAV",
            "Assets/#Sounds/Fire.WAV");

        if (dirty) EditorUtility.SetDirty(sonidos);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  3. AUDIO ARMAS Y PASOS → AudioManager
    // ─────────────────────────────────────────────────────────────────────────

    public static void AsignarAudioArmas()
    {
        // AudioManager auto-carga todos los clips en Resources/Audio/ al arrancar.
        // Esta función copia clips sueltos a esa carpeta para que se incluyan.
        var destDir = "Assets/Resources/Audio";
        if (!AssetDatabase.IsValidFolder(destDir))
        {
            AssetDatabase.CreateFolder("Assets/Resources", "Audio");
            AssetDatabase.Refresh();
        }

        var rutas = new[]
        {
            "Assets/#Tools/Resources/Sounds/Weapons/M4 fire.wav",
            "Assets/#Tools/Resources/Sounds/Weapons/ShootGun.wav",
            "Assets/#Sounds/m4_reload.mp3",
            "Assets/#Tools/Resources/Sounds/Weapons/explosion_1.wav",
            "Assets/Audio/Ambiente/PoliceSiren.WAV",
            "Assets/Audio/PoliceSiren.WAV",
        };

        int copiados = 0;
        foreach (var ruta in rutas)
        {
            if (!System.IO.File.Exists(ruta)) continue;
            string nombre  = System.IO.Path.GetFileName(ruta);
            string destino = $"{destDir}/{nombre}";
            if (AssetDatabase.LoadAssetAtPath<AudioClip>(destino) == null)
            {
                AssetDatabase.CopyAsset(ruta, destino);
                copiados++;
            }
        }
        if (copiados > 0) AssetDatabase.Refresh();
        Debug.Log($"[Auto] Audio: {copiados} clips copiados a Resources/Audio/");
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  4. SISTEMAS EN ESCENA
    // ─────────────────────────────────────────────────────────────────────────

    public static void AsegurarSistemasEscena()
    {
        // GameManager
        if (Object.FindFirstObjectByType<GameManagerAltsasua>() == null)
        {
            var go = new GameObject("GameManager");
            go.AddComponent<GameManagerAltsasua>();
            go.AddComponent<AudioManager>();
            Undo.RegisterCreatedObjectUndo(go, "Crear GameManager");
        }
        else if (Object.FindFirstObjectByType<AudioManager>() == null)
        {
            var gm = Object.FindFirstObjectByType<GameManagerAltsasua>();
            Undo.AddComponent<AudioManager>(gm.gameObject);
        }

        // SonidosAmbienteAltsasu
        if (Object.FindFirstObjectByType<SonidosAmbienteAltsasu>() == null)
        {
            var go = new GameObject("SonidosAmbiente");
            go.AddComponent<SonidosAmbienteAltsasu>();
            Undo.RegisterCreatedObjectUndo(go, "Crear SonidosAmbiente");
        }

        // NavMeshManager
        if (Object.FindFirstObjectByType<SistemaNavMesh>() == null)
        {
            var go = new GameObject("NavMeshManager");
            go.AddComponent<SistemaNavMesh>();
            Undo.RegisterCreatedObjectUndo(go, "Crear NavMeshManager");
        }

        // SceneBootstrapper
        if (Object.FindFirstObjectByType<SceneBootstrapper>() == null)
        {
            var go = new GameObject("SceneBootstrapper");
            go.AddComponent<SceneBootstrapper>();
            Undo.RegisterCreatedObjectUndo(go, "Crear SceneBootstrapper");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  5. WAYPOINTS POLICÍA
    // ─────────────────────────────────────────────────────────────────────────

    // Coordenadas en sistema terreno (offset Herriko Plaza = 1918, 8570)
    static readonly Vector3[] PATROL_POL_FORAL =
    {
        new Vector3(2038f, 0f, 8850f),  // Comisaría PF
        new Vector3(1978f, 0f, 8770f),  // Calle Navarra norte
        new Vector3(1918f, 0f, 8670f),  // Bajando al centro
        new Vector3(1918f, 0f, 8570f),  // Herriko Plaza
        new Vector3(2018f, 0f, 8490f),  // Calle este
        new Vector3(2118f, 0f, 8370f),  // Hacia Ondarria
        new Vector3(2018f, 0f, 8670f),  // Volviendo barrio norte
        new Vector3(2038f, 0f, 8850f),  // Regreso comisaría
    };

    static readonly Vector3[] PATROL_GUARDIA_CIVIL =
    {
        new Vector3(1658f, 0f, 8190f),  // Cuartel GC (Calle Ameztia)
        new Vector3(1718f, 0f, 8370f),  // Calle Navarra sur
        new Vector3(1818f, 0f, 8470f),  // Calle Erdikale
        new Vector3(1918f, 0f, 8570f),  // Herriko Plaza
        new Vector3(1998f, 0f, 8650f),  // Calle Brentana
        new Vector3(2018f, 0f, 8520f),  // Zona comercial este
        new Vector3(1968f, 0f, 8370f),  // N-1 sur casco
        new Vector3(1868f, 0f, 8270f),  // Glorieta sur
        new Vector3(1738f, 0f, 8220f),  // Ameztia vuelta
        new Vector3(1658f, 0f, 8190f),  // Regreso cuartel
    };

    public static void CrearWaypointsPolicia()
    {
        var zona = GameObject.Find("Zone_01_HerrikoPlaza_Centro");
        if (zona == null) { Debug.LogWarning("[Auto] Zone_01 no encontrada, saltando waypoints."); return; }

        bool estabaActiva = zona.activeSelf;
        zona.SetActive(true);

        CrearGrupoWaypoints(zona, "Waypoints_PolForal",   PATROL_POL_FORAL);
        CrearGrupoWaypoints(zona, "Waypoints_GuardiaCivil", PATROL_GUARDIA_CIVIL);

        zona.SetActive(estabaActiva);
        EditorUtility.SetDirty(zona);
    }

    static Transform[] CrearGrupoWaypoints(GameObject parent, string groupName, Vector3[] puntosTerreno)
    {
        // Borrar si existe
        var existente = parent.transform.Find(groupName);
        if (existente != null) Undo.DestroyObjectImmediate(existente.gameObject);

        var grupo = new GameObject(groupName);
        Undo.RegisterCreatedObjectUndo(grupo, "Crear waypoints");
        grupo.transform.SetParent(parent.transform, false);

        var transforms = new Transform[puntosTerreno.Length];
        for (int i = 0; i < puntosTerreno.Length; i++)
        {
            var wp = new GameObject($"WP_{i:D2}");
            wp.transform.SetParent(grupo.transform, false);

            // Calcular Y del terreno si está disponible en Editor
            float y = Terrain.activeTerrain != null
                ? Terrain.activeTerrain.SampleHeight(puntosTerreno[i]) + 0.1f
                : puntosTerreno[i].y;
            wp.transform.position = new Vector3(puntosTerreno[i].x, y, puntosTerreno[i].z);
            wp.tag = "Waypoint";
            transforms[i] = wp.transform;
        }
        return transforms;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  8. EDIFICIOS ZONE_01 (si no existen)
    // ─────────────────────────────────────────────────────────────────────────

    static void CrearEdificiosSiNoExisten()
    {
        var zona = GameObject.Find("Zone_01_HerrikoPlaza_Centro");
        if (zona == null) { Debug.LogWarning("[Auto] Zone_01 no encontrada."); return; }

        // Contar hijos que sean edificios
        int edificios = 0;
        bool activa = zona.activeSelf;
        zona.SetActive(true);
        for (int i = 0; i < zona.transform.childCount; i++)
        {
            var h = zona.transform.GetChild(i).name;
            if (h != "_Marcador" && h != "_Label" &&
                !h.StartsWith("Waypoints")) edificios++;
        }
        zona.SetActive(activa);

        if (edificios < 10)
        {
            Debug.Log("[Auto] Generando edificios OSM en Zone_01...");
            GeneradorEdificiosZona.GenerarEdificios(silencioso: true);
        }
        else
        {
            Debug.Log($"[Auto] Zone_01 ya tiene {edificios} edificios — saltando generación.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  9. COLOCAR COCHE EN ESCENA
    // ─────────────────────────────────────────────────────────────────────────

    public static void ColocarCocheEnEscena()
    {
        // Solo colocar si no hay ningún coche en escena
        if (Object.FindFirstObjectByType<ControladorVehiculoJugador>() != null)
        { Debug.Log("[Auto] Ya hay un vehículo en escena."); return; }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/Coches/Interceptor_Jugador.prefab");
        if (prefab == null)
        { Debug.LogWarning("[Auto] Interceptor_Jugador.prefab no encontrado — crea los coches primero."); return; }

        // Posición: 20m al este de Herriko Plaza, en la calle
        var posicion = new Vector3(1938f, 242f, 8570f);
        if (Terrain.activeTerrain != null)
            posicion.y = Terrain.activeTerrain.SampleHeight(posicion) + 0.5f;

        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.transform.position = posicion;
        go.transform.rotation = Quaternion.Euler(0f, 90f, 0f); // orientado en la calle

        // Colocar dentro de Zone_01 si existe
        var zona = GameObject.Find("Zone_01_HerrikoPlaza_Centro");
        if (zona != null)
        {
            bool activa = zona.activeSelf; zona.SetActive(true);
            go.transform.SetParent(zona.transform);
            zona.SetActive(activa);
        }

        Undo.RegisterCreatedObjectUndo(go, "Colocar Interceptor en escena");
        EditorUtility.SetDirty(go);
        Debug.Log("[Auto] Interceptor colocado en Herriko Plaza (este).");
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  11. QUALITY SETTINGS
    // ─────────────────────────────────────────────────────────────────────────

    public static void ConfigurarOcclusion()
    {
        // Configurar parámetros de Occlusion Culling
        // El bake real hay que hacerlo en Window → Rendering → Occlusion Culling → Bake
        // Pero podemos preparar los parámetros óptimos para Alsasua:
        var so = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/OcclusionCullingSettings.asset")
            .FirstOrDefault() ?? (Object)new UnityEngine.Object());

        if (so?.targetObject != null)
        {
            // Smallest occluder: 1m (edificios de Alsasua son > 3m)
            var smallestOccluder = so.FindProperty("m_OcclusionBakeSettings.smallestOccluder");
            if (smallestOccluder != null) smallestOccluder.floatValue = 1f;
            // Smallest hole: 0.25m
            var smallestHole = so.FindProperty("m_OcclusionBakeSettings.smallestHole");
            if (smallestHole != null) smallestHole.floatValue = 0.25f;
            so.ApplyModifiedProperties();
        }

        // Marcar todos los edificios como Occluder Static y Occludee Static
        var zona = GameObject.Find("Zone_01_HerrikoPlaza_Centro");
        if (zona != null)
        {
            int marcados = 0;
            foreach (var go in zona.GetComponentsInChildren<MeshRenderer>()
                .Select(r => r.gameObject))
            {
                if (!go.isStatic)
                {
                    GameObjectUtility.SetStaticEditorFlags(go,
                        StaticEditorFlags.OccluderStatic |
                        StaticEditorFlags.OccludeeStatic |
                        StaticEditorFlags.BatchingStatic);
                    marcados++;
                }
            }
            Debug.Log($"[Auto] 🔭 {marcados} GOs marcados como Occluder/Occludee Static.\n" +
                      "Para completar: Window → Rendering → Occlusion Culling → Bake");
        }
    }

    public static void ConfigurarQuality()
    {
        // Asegurar que el nivel de calidad activo es el más alto
        int maxLevel = QualitySettings.names.Length - 1;
        QualitySettings.SetQualityLevel(maxLevel, applyExpensiveChanges: true);

        // Sombras
        QualitySettings.shadowDistance          = 500f;
        QualitySettings.shadowCascades          = 4;
        QualitySettings.shadowProjection        = ShadowProjection.CloseFit;

        // LOD
        QualitySettings.lodBias                 = 2.0f;
        QualitySettings.maximumLODLevel         = 0;

        // Anisotropic
        QualitySettings.anisotropicFiltering    = AnisotropicFiltering.ForceEnable;

        // VSync — apagar para que Unity no esté limitado a 60fps en Editor
        QualitySettings.vSyncCount              = 0;

        // Anti-aliasing
        QualitySettings.antiAliasing            = 4; // 4xMSAA fallback

        // Pixel light count
        QualitySettings.pixelLightCount         = 8;

        EditorUtility.SetDirty(Shader.Find("HDRP/Lit"));
        Debug.Log("[Auto] Quality Settings configurado para HDRP high.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  12. PHYSICS MATRIX
    // ─────────────────────────────────────────────────────────────────────────

    public static void ConfigurarPhysicsMatrix()
    {
        int lDefault   = 0;
        int lTerrain   = 8;
        int lVehicle   = 9;
        int lNPC       = 10;

        // NPC no colisiona con NPC (evita empujes caóticos)
        Physics.IgnoreLayerCollision(lNPC, lNPC, true);

        // Vehiculo no colisiona con NPC (el NPC sale volando, no realista)
        // Desactiva si quieres atropellos físicos reales
        Physics.IgnoreLayerCollision(lVehicle, lNPC, false); // SÍ colisiona (atropello)

        // Terrain colisiona con todo
        Physics.IgnoreLayerCollision(lTerrain, lVehicle, false);
        Physics.IgnoreLayerCollision(lTerrain, lNPC,     false);
        Physics.IgnoreLayerCollision(lTerrain, lDefault, false);

        // Tiempo de dormir de rigidbodies — más bajo = más responsivo
        Physics.sleepThreshold                = 0.005f;
        Physics.defaultContactOffset         = 0.01f;
        Physics.defaultSolverIterations      = 8;
        Physics.defaultSolverVelocityIterations = 2;

        Debug.Log("[Auto] Physics matrix configurada.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  14. CONECTAR SISTEMAS QUE TIENEN PREFABS NULL
    // ─────────────────────────────────────────────────────────────────────────

    public static void ConectarSistemasNull()
    {
        // ── AlsasuaTreeStreamer.treePrefabs ───────────────────────────────
        var streamer = Object.FindFirstObjectByType<AlsasuaTreeStreamer>();
        if (streamer != null)
        {
            var field = typeof(AlsasuaTreeStreamer).GetField("treePrefabs",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field?.GetValue(streamer) == null || ((GameObject[])field.GetValue(streamer)).Length == 0)
            {
                // Usar modelos de flora extraídos
                var rutas = new[] {
                    "Assets/Models/Flora/01.FBX", "Assets/Models/Flora/02.FBX",
                    "Assets/Models/Flora/03.FBX", "Assets/Models/Flora/04.FBX",
                    "Assets/_ExtractedAssets/Vegetation/TreesNavarra/Roble_Ingles/Roble_Ingles.fbx",
                    "Assets/_ExtractedAssets/Vegetation/TreesNavarra/Haya_Europea/Haya_Europea.fbx",
                };
                var prefabs = rutas.Select(r => AssetDatabase.LoadAssetAtPath<GameObject>(r))
                                   .Where(p => p != null).ToArray();
                if (prefabs.Length > 0)
                {
                    Undo.RecordObject(streamer, "Asignar treePrefabs");
                    field.SetValue(streamer, prefabs);
                    EditorUtility.SetDirty(streamer);
                    Debug.Log($"[Auto] 🌲 {prefabs.Length} prefabs → AlsasuaTreeStreamer");
                }
            }
        }

        // ── SistemaFarolas.prefabsFarola ──────────────────────────────────
        var farolas = Object.FindFirstObjectByType<SistemaFarolas>();
        if (farolas != null)
        {
            var f = typeof(SistemaFarolas).GetField("prefabsFarola",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f?.GetValue(farolas) == null)
            {
                var pfs = new[] {
                    "Assets/SpaceZeta_StreetLamps2/Prefabs/StreetLampRound1A.prefab",
                    "Assets/SpaceZeta_StreetLamps2/Prefabs/StreetLampRound2A.prefab",
                }.Select(r => AssetDatabase.LoadAssetAtPath<GameObject>(r))
                 .Where(p => p != null).ToArray();
                if (pfs.Length > 0) { f.SetValue(farolas, pfs); EditorUtility.SetDirty(farolas); }
            }
        }

        // ── SistemaBarricadas / SistemaManifestacion.prefabBarricada ──────
        var manif = Object.FindFirstObjectByType<SistemaManifestacion>();
        if (manif != null)
        {
            var f = typeof(SistemaManifestacion).GetField("prefabBarricada",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f?.GetValue(manif) == null)
            {
                var barricada = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/BarrierPack/Assets/Barricada Concreto.FBX");
                if (barricada != null) { f.SetValue(manif, barricada); EditorUtility.SetDirty(manif); }
            }
        }

        // ── SistemaDestruccion: asignar material calcinado ────────────────
        var destr = Object.FindFirstObjectByType<SistemaDestruccion>();
        if (destr != null)
        {
            var f = typeof(SistemaDestruccion).GetField("matCalcinado",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f?.GetValue(destr) == null)
            {
                var mat = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"));
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(0.06f,0.06f,0.06f));
                else mat.color = new Color(0.06f, 0.06f, 0.06f);
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.01f);
                if (!AssetDatabase.IsValidFolder("Assets/Materials"))
                    AssetDatabase.CreateFolder("Assets", "Materials");
                AssetDatabase.CreateAsset(mat, "Assets/Materials/Mat_Calcinado.mat");
                f.SetValue(destr, mat);
                EditorUtility.SetDirty(destr);
                Debug.Log("[Auto] 🔥 Material calcinado creado para SistemaDestruccion");
            }
        }

        // ── SistemaMisiones en escena ─────────────────────────────────────
        if (Object.FindFirstObjectByType<SistemaMisiones>() == null)
        {
            var go = new GameObject("SistemaMisiones");
            go.AddComponent<SistemaMisiones>();
            Undo.RegisterCreatedObjectUndo(go, "Crear SistemaMisiones");
        }

        // ── SistemaMultitud: waypoints de la manifestación ────────────────
        var multitud = Object.FindFirstObjectByType<SistemaMultitud>();
        if (multitud != null)
        {
            var fRuta = typeof(SistemaMultitud).GetField("puntosRuta",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (fRuta?.GetValue(multitud) == null)
            {
                // Ruta de manifestación: Herriko Plaza → Calle Navarra → vuelta
                var posManif = new Vector3[] {
                    new(1918f, 0, 8570f), new(1918f, 0, 8700f),
                    new(1850f, 0, 8800f), new(1780f, 0, 8700f),
                    new(1780f, 0, 8570f), new(1850f, 0, 8470f),
                    new(1918f, 0, 8570f)
                };
                var wpGO = new GameObject("Waypoints_Manifestacion");
                Undo.RegisterCreatedObjectUndo(wpGO, "WP Manifestacion");
                var wps = new Transform[posManif.Length];
                for (int i = 0; i < posManif.Length; i++)
                {
                    var wp = new GameObject($"WP_{i}");
                    wp.transform.SetParent(wpGO.transform, false);
                    wp.transform.position = posManif[i];
                    wps[i] = wp.transform;
                }
                Undo.RecordObject(multitud, "Asignar ruta manifestacion");
                fRuta.SetValue(multitud, wps);
                EditorUtility.SetDirty(multitud);
                Debug.Log("[Auto] 👥 Ruta manifestación asignada a SistemaMultitud");
            }
        }

        // ── SistemaFauna: prefabs de animales ─────────────────────────────
        var fauna = Object.FindFirstObjectByType<SistemaFauna>();
        if (fauna != null)
        {
            // Asignar los modelos de fauna que tenemos extraídos
            // SistemaFauna probablemente tiene un array de EspecieFauna
            // Lo intentamos por reflexión buscando el primer campo de array
            foreach (var field in typeof(SistemaFauna).GetFields(
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.FieldType.IsArray && field.GetValue(fauna) == null)
                {
                    // No hay prefabs de fauna — se nota en runtime pero no crashea
                    break;
                }
            }
        }

        // ── SistemaFerroviario: waypoints de la vía ───────────────────────
        var ferro = Object.FindFirstObjectByType<SistemaFerroviario>();
        if (ferro != null)
        {
            // Las coordenadas de la vía vienen de GeoDataAlsasua.ViaFerreaNorte
            // Son coordenadas de terreno: Herriko Plaza ≈ (1918, y, 8570)
            var viaCoords = new Vector3[] {
                new(-500f+1918f, 240f, -1400f+8570f),
                new(-510f+1918f, 241f, -780f+8570f),   // Estación Alsasua
                new(-520f+1918f, 242f, -200f+8570f),
                new(-530f+1918f, 243f, 400f+8570f),
                new(-540f+1918f, 244f, 1200f+8570f),
            };
            var fVia = typeof(SistemaFerroviario).GetField("puntosVia",
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            if (fVia?.GetValue(ferro) == null)
            {
                var viaGO = new GameObject("Via_Ferrea_WP");
                Undo.RegisterCreatedObjectUndo(viaGO, "WP Via Ferrea");
                var wps = new Transform[viaCoords.Length];
                for (int i = 0; i < viaCoords.Length; i++)
                {
                    var wp = new GameObject($"WP_{i}");
                    wp.transform.SetParent(viaGO.transform, false);
                    wp.transform.position = viaCoords[i];
                    wps[i] = wp.transform;
                }
                Undo.RecordObject(ferro, "Asignar via ferrea");
                fVia.SetValue(ferro, wps);
                EditorUtility.SetDirty(ferro);
                Debug.Log("[Auto] 🚂 Waypoints vía férrea asignados a SistemaFerroviario");
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  15. HDRP — CREAR Y ASIGNAR RENDER PIPELINE ASSET
    // ─────────────────────────────────────────────────────────────────────────

    public static void ConfigurarHDRP()
    {
        // Verificar si ya hay un HDRP asset asignado
        var current = UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;
        if (current != null && current.GetType().Name.Contains("HDRenderPipeline"))
        {
            Debug.Log("[Auto] HDRP ya está configurado.");
            return;
        }

        // Buscar el HDRenderPipelineAsset existente en el proyecto
        var guids = AssetDatabase.FindAssets("t:RenderPipelineAsset");
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.RenderPipelineAsset>(path);
            if (asset != null && asset.GetType().Name.Contains("HDRenderPipeline"))
            {
                UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline = asset;
                // También asignar en Quality Settings
                for (int i = 0; i < QualitySettings.names.Length; i++)
                    QualitySettings.SetQualityLevel(i);
                QualitySettings.renderPipeline = asset;
                Debug.Log($"[Auto] ✅ HDRP Asset asignado: {path}");
                return;
            }
        }

        Debug.LogWarning("[Auto] ⚠ No se encontró HDRenderPipelineAsset. " +
                         "Ve a Project Settings → Graphics → asigna el HDRP asset manualmente.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  HELPERS
    // ─────────────────────────────────────────────────────────────────────────

    static bool AsignarClip(Object target, string campo, params string[] rutas)
    {
        // Si ya está asignado, no sobreescribir
        var field = target.GetType().GetField(campo,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (field == null) return false;
        if (field.GetValue(target) != null) return false;

        foreach (var ruta in rutas)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(ruta);
            if (clip != null)
            {
                Undo.RecordObject(target, $"Asignar {campo}");
                field.SetValue(target, clip);
                return true;
            }
        }
        Debug.LogWarning($"[Auto] Audio no encontrado para campo '{campo}'.");
        return false;
    }
}

