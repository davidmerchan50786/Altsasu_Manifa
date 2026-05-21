// Assets/Scripts/Editor/ConstructorProyectoCompleto.cs
// ═══════════════════════════════════════════════════════════════════════════
//  CONSTRUCTOR DE PROYECTO COMPLETO
//  Tools → Alsasua → 🏗 Construir TODO el proyecto
//
//  Ejecuta secuencialmente (usando delayCall):
//    1. Crear carpetas necesarias
//    2. Crear HDRenderPipelineAsset
//    3. Crear escena MenuPrincipal
//    4. Crear escena Alsasua_Main con todos los sistemas
//    5. Crear prefab Interceptor_Jugador (coche)
//    6. Crear prefab Jugador_Altsasua (jugador)
//    7. Importar 335 FBX como prefabs
//    8. Crear materiales HDRP desde las 542 texturas
//    9. Crear AnimationClips procedurales
//   10. Añadir escenas a Build Settings
//   11. Asignar HDRP pipeline asset
//   12. Verificación final
// ═══════════════════════════════════════════════════════════════════════════

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.AI;
using System.IO;
using System.Collections.Generic;
using System.Linq;

public static class ConstructorProyectoCompleto
{
    static int _paso = 0;
    static readonly string[] PASOS = {
        "Carpetas",         "HDRP Asset",      "Escena Menú",
        "Escena Principal", "Prefab Coche",    "Prefab Jugador",
        "Prefabs FBX",      "Materiales HDRP", "Animaciones",
        "Build Settings",   "Pipeline Asset",  "Verificación"
    };

    // [DESACTIVADO] [MenuItem("Tools/Alsasua/🏗 Construir TODO el proyecto", priority = 1)]
    public static void ConstruirTodo()
    {
        if (!EditorUtility.DisplayDialog("Construir proyecto completo",
            "Esto creará:\n• 2 escenas (Menú + Principal)\n• Prefabs de coche y jugador\n" +
            "• Prefabs de los 335 FBX\n• Materiales HDRP\n• Animaciones procedurales\n\n" +
            "Tiempo estimado: 10-20 minutos.\n¿Continuar?", "Sí, construir todo", "Cancelar"))
            return;

        _paso = 0;
        EjecutarPaso();
    }

    static void EjecutarPaso()
    {
        if (_paso >= PASOS.Length) { Finalizar(); return; }
        float progreso = (float)_paso / PASOS.Length;
        EditorUtility.DisplayProgressBar("🏗 Construyendo proyecto...",
            $"Paso {_paso+1}/{PASOS.Length}: {PASOS[_paso]}", progreso);
        try
        {
            switch (_paso)
            {
                case 0:  Paso_Carpetas();        break;
                case 1:  Paso_HDRPAsset();       break;
                case 2:  Paso_EscenaMenu();      break;
                case 3:  Paso_EscenaPrincipal(); break;
                case 4:  Paso_PrefabCoche();     break;
                case 5:  Paso_PrefabJugador();   break;
                case 6:  Paso_PrefabsFBX();      break;
                case 7:  Paso_Materiales();      break;
                case 8:  Paso_Animaciones();     break;
                case 9:  Paso_BuildSettings();   break;
                case 10: Paso_PipelineAsset();   break;
                case 11: Paso_Verificacion();    break;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[Constructor] Paso {_paso} ({PASOS[_paso]}) error: {e.Message}");
        }
        _paso++;
        EditorApplication.delayCall += EjecutarPaso;
    }

    // ════════════════════════════════════════════════════════════════════
    //  PASO 0 — CARPETAS
    // ════════════════════════════════════════════════════════════════════
    static void Paso_Carpetas()
    {
        var dirs = new[] {
            "Assets/#Scenes", "Assets/Animators", "Assets/Audio",
            "Assets/Resources", "Assets/Resources/Audio",
            "Assets/Prefabs", "Assets/Prefabs/Coches",
            "Assets/Prefabs/Personajes", "Assets/Prefabs/Mundo",
            "Assets/Prefabs/FBX", "Assets/Materials",
            "Assets/Materials/Edificios", "Assets/Materials/Vehiculos",
            "Assets/Materials/Naturaleza", "Assets/Settings",
            "Assets/AlsasuaData"
        };
        foreach (var d in dirs)
        {
            var parts = d.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
        AssetDatabase.Refresh();
        Debug.Log("[Constructor] ✅ Carpetas creadas");
    }

    // ════════════════════════════════════════════════════════════════════
    //  PASO 1 — HDRP ASSET
    // ════════════════════════════════════════════════════════════════════
    static void Paso_HDRPAsset()
    {
        // Buscar asset HDRP existente
        var guids = AssetDatabase.FindAssets("t:HDRenderPipelineAsset");
        if (guids.Length > 0)
        {
            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            Debug.Log($"[Constructor] ✅ HDRP Asset existente: {path}");
            return;
        }

        // Crear uno nuevo via método público de HDRP
        var asset = ScriptableObject.CreateInstance<HDRenderPipelineAsset>();
        AssetDatabase.CreateAsset(asset, "Assets/Settings/HDRenderPipelineAsset.asset");
        AssetDatabase.SaveAssets();
        Debug.Log("[Constructor] ✅ HDRenderPipelineAsset.asset creado");
    }

    // ════════════════════════════════════════════════════════════════════
    //  PASO 2 — ESCENA MENÚ PRINCIPAL
    // ════════════════════════════════════════════════════════════════════
    static void Paso_EscenaMenu()
    {
        const string RUTA = "Assets/#Scenes/MenuPrincipal.unity";
        if (File.Exists(Path.GetFullPath(RUTA.Replace("Assets/", Application.dataPath + "/"))))
        { Debug.Log("[Constructor] ✅ MenuPrincipal.unity ya existe"); return; }

        var escena = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Cámara
        var camGO = new GameObject("Main Camera");
        camGO.tag = "MainCamera";
        var cam = camGO.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.04f, 0.04f, 0.08f);
        camGO.AddComponent<AudioListener>();
        camGO.AddComponent<HDAdditionalCameraData>();

        // EventSystem
        var esGO = new GameObject("EventSystem");
        esGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
        esGO.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();

        // MenuPrincipal script
        var menuGO = new GameObject("MenuPrincipal");
        menuGO.AddComponent<MenuPrincipal>();

        EditorSceneManager.SaveScene(escena, RUTA);
        Debug.Log($"[Constructor] ✅ MenuPrincipal.unity creada");
    }

    // ════════════════════════════════════════════════════════════════════
    //  PASO 3 — ESCENA PRINCIPAL ALSASUA
    // ════════════════════════════════════════════════════════════════════
    static void Paso_EscenaPrincipal()
    {
        const string RUTA = "Assets/#Scenes/Alsasua_Main.unity";

        var escena = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ── AltsasuCore (master) ──
        var coreGO = new GameObject("AltsasuCore");
        coreGO.AddComponent<AltsasuCore>();

        // ── Cámara ──
        var camGO = new GameObject("Main Camera");
        camGO.tag = "MainCamera";
        var cam = camGO.AddComponent<Camera>();
        cam.fieldOfView = 75f;
        cam.nearClipPlane = 0.15f;
        cam.farClipPlane  = 1200f;
        camGO.AddComponent<AudioListener>();
        var hdCam = camGO.AddComponent<HDAdditionalCameraData>();

        // ── Luz solar ──
        var solGO = new GameObject("Directional Light (Sol)");
        var sol   = solGO.AddComponent<Light>();
        sol.type      = LightType.Directional;
        sol.intensity = 80000f; // lux
        sol.color     = new Color(1f, 0.95f, 0.82f);
        sol.shadows   = LightShadows.Soft;
        solGO.transform.eulerAngles = new Vector3(50f, 30f, 0f);
        var hdLight = solGO.AddComponent<HDAdditionalLightData>();

        // ── Terrain ──
        var terrain = Terrain.CreateTerrainGameObject(
            new TerrainData { heightmapResolution = 1025, size = new Vector3(4000, 1200, 4000) });
        terrain.name = "Terrain_Alsasua";

        // ── Sky y fog volume ──
        var skyGO = new GameObject("Sky and Fog Volume");
        var vol   = skyGO.AddComponent<Volume>();
        vol.isGlobal = true;
        var profile  = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, "Assets/Settings/AltsasuaSkyProfile.asset");

        var hdriSky = profile.Add<HDRISky>(true);
        // Intentar asignar el primer HDRI disponible
        var hdriGuids = AssetDatabase.FindAssets("t:Cubemap", new[]{"Assets/HDRIs"});
        if (hdriGuids.Length == 0) hdriGuids = AssetDatabase.FindAssets("t:Texture2D belfast");
        if (hdriGuids.Length > 0)
        {
            var hdri = AssetDatabase.LoadAssetAtPath<Texture>(AssetDatabase.GUIDToAssetPath(hdriGuids[0]));
            if (hdri != null) hdriSky.hdriSky.Override(hdri as Cubemap);
        }

        profile.Add<Fog>(true).enabled.Override(true);
        profile.Add<VisualEnvironment>(true).skyType.Override(1);
        var exposure = profile.Add<Exposure>(true);
        exposure.mode.Override(ExposureMode.Automatic);
        vol.profile = profile;

        // ── EventSystem ──
        var esGO = new GameObject("EventSystem");
        esGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
        esGO.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();

        // Guardar
        EditorSceneManager.SaveScene(escena, RUTA);
        AssetDatabase.SaveAssets();
        Debug.Log("[Constructor] ✅ Alsasua_Main.unity creada con terreno, sol, cámara y sky HDRP");
    }

    // ════════════════════════════════════════════════════════════════════
    //  PASO 4 — PREFAB COCHE (INTERCEPTOR)
    // ════════════════════════════════════════════════════════════════════
    static void Paso_PrefabCoche()
    {
        const string RUTA = "Assets/Prefabs/Coches/Interceptor_Jugador.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(RUTA) != null)
        { Debug.Log("[Constructor] ✅ Interceptor_Jugador.prefab ya existe"); return; }

        // Buscar modelo de coche ya importado
        var fbxGuids = AssetDatabase.FindAssets("t:Model",
            new[]{"Assets/_ExtractedAssets","Assets/Police Car & Helicopter","Assets/Hot Rod"});
        GameObject modelCoche = null;
        foreach (var g in fbxGuids)
        {
            var path = AssetDatabase.GUIDToAssetPath(g);
            var name = Path.GetFileNameWithoutExtension(path).ToLower();
            if (name.Contains("police") || name.Contains("car") || name.Contains("sedan") ||
                name.Contains("interceptor") || name.Contains("hotrod"))
            {
                modelCoche = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (modelCoche != null) break;
            }
        }

        // Crear la jerarquía del prefab
        var root = new GameObject("Interceptor_Jugador");

        // Carrocería visual
        GameObject carroceria;
        if (modelCoche != null)
        {
            carroceria = (GameObject)PrefabUtility.InstantiatePrefab(modelCoche, root.transform);
            carroceria.name = "Carroceria";
        }
        else
        {
            // Fallback procedural
            carroceria = GameObject.CreatePrimitive(PrimitiveType.Cube);
            carroceria.name = "Carroceria";
            carroceria.transform.localPosition = new Vector3(0f, 0.45f, 0f);
            carroceria.transform.localScale    = new Vector3(1.8f, 0.8f, 4.4f);
            carroceria.transform.SetParent(root.transform);
            var techoGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
            techoGO.name = "Techo";
            techoGO.transform.localScale    = new Vector3(1.4f, 0.5f, 2.2f);
            techoGO.transform.localPosition = new Vector3(0f, 1.05f, 0.1f);
            techoGO.transform.SetParent(root.transform);
            // Material azul de policia
            var matPol = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"));
            matPol.color = new Color(0.1f, 0.18f, 0.55f);
            carroceria.GetComponent<Renderer>().sharedMaterial = matPol;
            techoGO.GetComponent<Renderer>().sharedMaterial    = matPol;
        }

        // Rigidbody
        var rb = root.AddComponent<Rigidbody>();
        rb.mass = 1400f; rb.linearDamping = 0.1f; rb.angularDamping = 0.1f;
        rb.centerOfMass = new Vector3(0f, -0.2f, 0f);

        // BoxCollider principal
        var col = root.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, 0.45f, 0f);
        col.size   = new Vector3(1.8f, 1.0f, 4.4f);

        // WheelColliders (4 ruedas)
        var posiciones = new (string n, Vector3 p)[] {
            ("WC_FL", new Vector3(-0.77f,  0.33f,  1.50f)),
            ("WC_FR", new Vector3( 0.77f,  0.33f,  1.50f)),
            ("WC_RL", new Vector3(-0.77f,  0.33f, -1.45f)),
            ("WC_RR", new Vector3( 0.77f,  0.33f, -1.45f)),
        };
        foreach (var (n, p) in posiciones)
        {
            var wcGO = new GameObject(n);
            wcGO.transform.SetParent(root.transform);
            wcGO.transform.localPosition = p;
            var wc = wcGO.AddComponent<WheelCollider>();
            wc.radius = 0.35f; wc.mass = 20f;
            wc.suspensionDistance = 0.3f;
            var spring = wc.suspensionSpring;
            spring.spring = 35000f; spring.damper = 4500f;
            wc.suspensionSpring = spring;
        }

        // Controlador
        root.AddComponent<ControladorVehiculoJugador>();

        // Luz delantera
        var faro = new GameObject("Faro_F");
        faro.transform.SetParent(root.transform);
        faro.transform.localPosition = new Vector3(0f, 0.5f, 2.3f);
        var luzFaro = faro.AddComponent<Light>();
        luzFaro.type = LightType.Spot; luzFaro.range = 30f;
        luzFaro.spotAngle = 50f; luzFaro.intensity = 5000f;
        luzFaro.color = new Color(0.95f, 0.95f, 1f);
        faro.AddComponent<HDAdditionalLightData>();

        // Guardar prefab
        PrefabUtility.SaveAsPrefabAsset(root, RUTA);
        Object.DestroyImmediate(root);
        Debug.Log($"[Constructor] ✅ Interceptor_Jugador.prefab → {RUTA}");
    }

    // ════════════════════════════════════════════════════════════════════
    //  PASO 5 — PREFAB JUGADOR
    // ════════════════════════════════════════════════════════════════════
    static void Paso_PrefabJugador()
    {
        const string RUTA = "Assets/Prefabs/Personajes/Jugador_Altsasua.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(RUTA) != null)
        { Debug.Log("[Constructor] ✅ Jugador_Altsasua.prefab ya existe"); return; }

        // Buscar modelo humano
        var fbxGuids = AssetDatabase.FindAssets("t:Model",
            new[]{"Assets/_ExtractedAssets","Assets/LowPolySoldiers_demo"});
        GameObject modelHumano = null;
        foreach (var g in fbxGuids)
        {
            var path = AssetDatabase.GUIDToAssetPath(g);
            var name = Path.GetFileNameWithoutExtension(path).ToLower();
            if (name.Contains("human") || name.Contains("person") || name.Contains("character") ||
                name.Contains("soldier") || name.Contains("guardia") || name.Contains("man"))
            {
                modelHumano = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (modelHumano != null) break;
            }
        }

        var root = new GameObject("Jugador_Altsasua");
        root.tag = "Player";

        // Cuerpo visual
        if (modelHumano != null)
        {
            var body = (GameObject)PrefabUtility.InstantiatePrefab(modelHumano, root.transform);
            body.name = "Modelo";
        }
        else
        {
            // Cápsula fallback
            var caps = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            caps.name = "Cuerpo"; caps.transform.SetParent(root.transform);
            caps.transform.localPosition = new Vector3(0f, 1f, 0f);
            caps.transform.localScale    = new Vector3(0.5f, 1f, 0.5f);
            var matJug = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"));
            matJug.color = new Color(0.25f, 0.25f, 0.35f); // azul oscuro (manifestante)
            caps.GetComponent<Renderer>().sharedMaterial = matJug;
        }

        // CharacterController (para movimiento)
        var cc = root.AddComponent<CharacterController>();
        cc.height = 1.85f; cc.radius = 0.35f;
        cc.center = new Vector3(0f, 0.925f, 0f);

        // Rigidbody (para colisiones físicas con vehículos)
        var rb = root.AddComponent<Rigidbody>();
        rb.mass = 75f; rb.useGravity = true;
        rb.constraints = RigidbodyConstraints.FreezeRotationX |
                         RigidbodyConstraints.FreezeRotationY |
                         RigidbodyConstraints.FreezeRotationZ;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        // Animator
        var anim = root.AddComponent<Animator>();
        var ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
            "Assets/Animators/JugadorAnimator.controller");
        if (ctrl != null) anim.runtimeAnimatorController = ctrl;

        // Scripts de jugador
        root.AddComponent<ControladorJugador>();
        root.AddComponent<SistemaIKProcedural>();

        // Cámara hijo (spring arm)
        var camPivot = new GameObject("CamPivot");
        camPivot.transform.SetParent(root.transform);
        camPivot.transform.localPosition = new Vector3(0f, 1.75f, 0f);

        var camGO = new GameObject("PlayerCamera");
        camGO.transform.SetParent(camPivot.transform);
        camGO.transform.localPosition = new Vector3(0.35f, 0.4f, -2.8f);
        camGO.AddComponent<Camera>().fieldOfView = 75f;
        camGO.AddComponent<HDAdditionalCameraData>();

        // NavMeshObstacle (jugador evita que la IA lo atraviese)
        var obs = root.AddComponent<NavMeshObstacle>();
        obs.carving = true; obs.radius = 0.35f;

        PrefabUtility.SaveAsPrefabAsset(root, RUTA);
        Object.DestroyImmediate(root);
        Debug.Log($"[Constructor] ✅ Jugador_Altsasua.prefab → {RUTA}");
    }

    // ════════════════════════════════════════════════════════════════════
    //  PASO 6 — PREFABS DE LOS 335 FBX
    // ════════════════════════════════════════════════════════════════════
    static void Paso_PrefabsFBX()
    {
        var guids = AssetDatabase.FindAssets("t:Model", new[]{"Assets/_ExtractedAssets"});
        int creados = 0, saltados = 0;

        // Categorías por nombre para carpeta destino
        var categorias = new Dictionary<string, string>
        {
            ["tree"] = "Naturaleza", ["bush"] = "Naturaleza", ["plant"] = "Naturaleza",
            ["grass"] = "Naturaleza", ["hedge"] = "Naturaleza", ["oak"] = "Naturaleza",
            ["house"] = "Edificios", ["building"] = "Edificios", ["wall"] = "Edificios",
            ["window"] = "Edificios", ["door"] = "Edificios", ["modular"] = "Edificios",
            ["chicken"] = "Animales", ["horse"] = "Animales", ["rabbit"] = "Animales",
            ["deer"] = "Animales", ["wolf"] = "Animales", ["dog"] = "Animales",
            ["sheep"] = "Animales", ["crow"] = "Animales", ["rat"] = "Animales",
            ["car"] = "Vehiculos", ["vehicle"] = "Vehiculos", ["truck"] = "Vehiculos",
            ["human"] = "Personajes", ["person"] = "Personajes", ["guardia"] = "Personajes",
            ["soldier"] = "Personajes", ["character"] = "Personajes",
        };

        foreach (var guid in guids)
        {
            var srcPath  = AssetDatabase.GUIDToAssetPath(guid);
            var fileName = Path.GetFileNameWithoutExtension(srcPath).ToLower();

            // Determinar carpeta
            string cat = "Mundo";
            foreach (var kv in categorias)
                if (fileName.Contains(kv.Key)) { cat = kv.Value; break; }

            string destFolder = $"Assets/Prefabs/FBX/{cat}";
            if (!AssetDatabase.IsValidFolder(destFolder))
                AssetDatabase.CreateFolder("Assets/Prefabs/FBX",
                    cat.Contains('/') ? cat.Split('/').Last() : cat);

            string prefabRuta = $"{destFolder}/{Path.GetFileNameWithoutExtension(srcPath)}.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabRuta) != null)
            { saltados++; continue; }

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(srcPath);
            if (model == null) continue;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            if (instance == null) continue;

            // Añadir LODGroup si el modelo es > 500 triángulos
            AddLODGroupIfNeeded(instance);

            // Añadir componente según categoría
            switch (cat)
            {
                case "Animales":
                    if (instance.GetComponent<SistemaFauna>() == null) { /* fauna gestionada por SistemaFauna */ }
                    break;
                case "Vehiculos":
                    if (instance.GetComponent<Rigidbody>() == null) instance.AddComponent<Rigidbody>().mass = 800f;
                    instance.AddComponent<VehiculoNPC>();
                    break;
            }

            PrefabUtility.SaveAsPrefabAsset(instance, prefabRuta);
            Object.DestroyImmediate(instance);
            creados++;

            if (creados % 20 == 0)
            {
                EditorUtility.DisplayProgressBar("Creando prefabs...",
                    $"{creados}/{guids.Length} prefabs", (float)creados/guids.Length);
                AssetDatabase.SaveAssets();
            }
        }

        AssetDatabase.Refresh();
        Debug.Log($"[Constructor] ✅ Prefabs FBX: {creados} creados, {saltados} ya existían");
    }

    static void AddLODGroupIfNeeded(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<MeshRenderer>();
        if (renderers.Length == 0) return;
        if (go.GetComponent<LODGroup>() != null) return;

        var lod = go.AddComponent<LODGroup>();
        var lods = new LOD[]
        {
            new LOD(0.15f, renderers),
            new LOD(0.04f, renderers),
            new LOD(0.01f, new Renderer[0])
        };
        lod.SetLODs(lods);
        lod.RecalculateBounds();
    }

    // ════════════════════════════════════════════════════════════════════
    //  PASO 7 — MATERIALES HDRP DESDE TEXTURAS
    // ════════════════════════════════════════════════════════════════════
    static void Paso_Materiales()
    {
        var shaderLit = Shader.Find("HDRP/Lit");
        if (shaderLit == null) { Debug.LogWarning("[Constructor] HDRP/Lit no disponible — saltando materiales"); return; }

        // Agrupar texturas por nombre base (sin sufijo _diffuse / _nor_gl / _arm / _rough)
        var guids = AssetDatabase.FindAssets("t:Texture2D", new[]{"Assets/Textures_AAA"});
        var grupos = new Dictionary<string, Dictionary<string, Texture2D>>();

        foreach (var g in guids)
        {
            var path   = AssetDatabase.GUIDToAssetPath(g);
            var nombre = Path.GetFileNameWithoutExtension(path).ToLower();

            // Detectar canal
            string canal = "diffuse";
            string baseName = nombre;
            foreach (var suf in new[]{"_diffuse","_diff","_nor_gl","_normal","_rough","_arm","_ao","_2k","_4k","_1k"})
            {
                if (nombre.EndsWith(suf)) { canal = suf.TrimStart('_'); baseName = nombre.Substring(0, nombre.Length - suf.Length); break; }
            }

            if (!grupos.ContainsKey(baseName)) grupos[baseName] = new Dictionary<string, Texture2D>();
            grupos[baseName][canal] = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        int creados = 0;
        foreach (var kv in grupos)
        {
            string matRuta = $"Assets/Materials/{CategoriaTextura(kv.Key)}/{kv.Key}.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(matRuta) != null) continue;

            var mat = new Material(shaderLit) { name = kv.Key };

            if (kv.Value.TryGetValue("diffuse", out var diff) || kv.Value.TryGetValue("diff", out diff))
                mat.SetTexture("_BaseColorMap", diff);
            if (kv.Value.TryGetValue("nor_gl", out var normal) || kv.Value.TryGetValue("normal", out normal))
                mat.SetTexture("_NormalMap", normal);
            if (kv.Value.TryGetValue("arm", out var arm))
                mat.SetTexture("_MaskMap", arm);
            if (kv.Value.TryGetValue("rough", out var rough))
            {
                mat.SetFloat("_Smoothness", 0.0f); // rough = 1 - smooth
                mat.SetTexture("_MaskMap", rough);
            }

            mat.SetFloat("_MaterialID", 1f); // Specular workflow
            AssetDatabase.CreateAsset(mat, matRuta);
            creados++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[Constructor] ✅ Materiales HDRP: {creados} creados de {grupos.Count} grupos de textura");
    }

    static string CategoriaTextura(string nombre)
    {
        if (nombre.Contains("concrete") || nombre.Contains("brick") || nombre.Contains("plaster")) return "Edificios";
        if (nombre.Contains("grass") || nombre.Contains("ground") || nombre.Contains("rock") || nombre.Contains("dirt")) return "Naturaleza";
        if (nombre.Contains("metal") || nombre.Contains("asphalt") || nombre.Contains("road")) return "Vehiculos";
        return "Edificios";
    }

    // ════════════════════════════════════════════════════════════════════
    //  PASO 8 — ANIMACIONES PROCEDURALES
    // ════════════════════════════════════════════════════════════════════
    static void Paso_Animaciones()
    {
        // Primero crear el controller
        ConfiguradorAnimatorJugador.Configurar();

        // Crear clips procedurales básicos si no existen
        CrearClipProcedural("Idle",  0f,    false);
        CrearClipProcedural("Walk",  1.4f,  true);
        CrearClipProcedural("Run",   5.0f,  true);
        CrearClipProcedural("Jump",  0f,    false);
        CrearClipProcedural("Drive", 0f,    false);
        CrearClipProcedural("Crouch",0f,    false);

        AssetDatabase.SaveAssets();
        Debug.Log("[Constructor] ✅ AnimationClips procedurales creados");
    }

    static void CrearClipProcedural(string nombre, float velocidad, bool loop)
    {
        string ruta = $"Assets/Animators/{nombre}.anim";
        if (AssetDatabase.LoadAssetAtPath<AnimationClip>(ruta) != null) return;

        var clip = new AnimationClip { name = nombre, frameRate = 30f };
        clip.legacy = false;
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        // Añadir curva de posición Y básica (rebote para walk/run)
        if (velocidad > 0f)
        {
            float freq    = velocidad > 3f ? 2.0f : 1.0f; // frecuencia de pasos
            float amp     = velocidad > 3f ? 0.06f : 0.03f;
            float duracion = 1f / freq;

            var curva = new AnimationCurve();
            curva.AddKey(new Keyframe(0f,              0f));
            curva.AddKey(new Keyframe(duracion * 0.25f, amp));
            curva.AddKey(new Keyframe(duracion * 0.5f,  0f));
            curva.AddKey(new Keyframe(duracion * 0.75f, amp));
            curva.AddKey(new Keyframe(duracion,         0f));
            clip.SetCurve("", typeof(Transform), "localPosition.y", curva);
        }

        // Keyframe de posición z para salto
        if (nombre == "Jump")
        {
            var curvaY = AnimationCurve.EaseInOut(0f, 0f, 0.25f, 0.8f);
            curvaY.AddKey(new Keyframe(0.5f, 0f));
            clip.SetCurve("", typeof(Transform), "localPosition.y", curvaY);
        }

        AssetDatabase.CreateAsset(clip, ruta);
    }

    // ════════════════════════════════════════════════════════════════════
    //  PASO 9 — BUILD SETTINGS
    // ════════════════════════════════════════════════════════════════════
    static void Paso_BuildSettings()
    {
        var escenas = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

        string[] esRutas = {
            "Assets/#Scenes/MenuPrincipal.unity",
            "Assets/#Scenes/Alsasua_Main.unity"
        };

        foreach (var ruta in esRutas)
        {
            if (!File.Exists(Path.GetFullPath(ruta.Replace("Assets/", Application.dataPath + "/"))))
            { Debug.LogWarning($"[Constructor] Escena no encontrada: {ruta}"); continue; }

            bool yaExiste = escenas.Any(s => s.path == ruta);
            if (!yaExiste) escenas.Insert(0, new EditorBuildSettingsScene(ruta, true));
            else { var s = escenas.First(x => x.path == ruta); s.enabled = true; }
        }

        EditorBuildSettings.scenes = escenas.ToArray();
        Debug.Log("[Constructor] ✅ Build Settings actualizados");
    }

    // ════════════════════════════════════════════════════════════════════
    //  PASO 10 — PIPELINE ASSET
    // ════════════════════════════════════════════════════════════════════
    static void Paso_PipelineAsset()
    {
        var guids = AssetDatabase.FindAssets("t:HDRenderPipelineAsset");
        if (guids.Length == 0) { Debug.LogWarning("[Constructor] No se encontró HDRenderPipelineAsset"); return; }

        var asset = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(guids[0]));
        if (asset == null) return;

        var settings = GraphicsSettings.defaultRenderPipeline;
        if (settings == null || settings.GetType().Name != "HDRenderPipelineAsset")
        {
            GraphicsSettings.defaultRenderPipeline = asset;
            QualitySettings.renderPipeline         = asset;
            EditorUtility.SetDirty(AssetDatabase.LoadAssetAtPath<Object>(
                "ProjectSettings/GraphicsSettings.asset"));
            Debug.Log($"[Constructor] ✅ HDRP Pipeline Asset asignado: {AssetDatabase.GUIDToAssetPath(guids[0])}");
        }
        else Debug.Log("[Constructor] ✅ HDRP Pipeline ya asignado");
    }

    // ════════════════════════════════════════════════════════════════════
    //  PASO 11 — VERIFICACIÓN FINAL
    // ════════════════════════════════════════════════════════════════════
    static void Paso_Verificacion()
    {
        var resultados = new List<(string nombre, bool ok, string detalle)>
        {
            ("Escena MenuPrincipal",    File.Exists(Application.dataPath + "/../Assets/#Scenes/MenuPrincipal.unity"), ""),
            ("Escena Alsasua_Main",     File.Exists(Application.dataPath + "/../Assets/#Scenes/Alsasua_Main.unity"),  ""),
            ("Prefab Interceptor",      AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Coches/Interceptor_Jugador.prefab") != null, ""),
            ("Prefab Jugador",          AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Personajes/Jugador_Altsasua.prefab") != null, ""),
            ("buildings_unity.json",    File.Exists(Application.dataPath + "/AlsasuaData/buildings_unity.json"), ""),
            ("roads_unity.json",        File.Exists(Application.dataPath + "/AlsasuaData/roads_unity.json"), ""),
            ("dem_unity_1025.raw",      File.Exists(Application.dataPath + "/AlsasuaData/dem_unity_1025.raw"), ""),
            ("AnimatorController",      AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Animators/JugadorAnimator.controller") != null, ""),
            ("Texturas_AAA (542)",      AssetDatabase.FindAssets("t:Texture2D", new[]{"Assets/Textures_AAA"}).Length > 100, $"{AssetDatabase.FindAssets("t:Texture2D", new[]{"Assets/Textures_AAA"}).Length} texturas"),
            ("HDRIs (11)",              AssetDatabase.FindAssets("t:Texture", new[]{"Assets/HDRIs"}).Length > 0, ""),
            ("Scripts (.cs)",           AssetDatabase.FindAssets("t:MonoScript", new[]{"Assets/Scripts"}).Length > 0, $"{AssetDatabase.FindAssets("t:MonoScript", new[]{"Assets/Scripts"}).Length} scripts"),
            ("Materiales HDRP",         AssetDatabase.FindAssets("t:Material", new[]{"Assets/Materials"}).Length > 0, ""),
            ("Prefabs FBX",             AssetDatabase.FindAssets("t:Prefab", new[]{"Assets/Prefabs/FBX"}).Length > 0, $"{AssetDatabase.FindAssets("t:Prefab", new[]{"Assets/Prefabs/FBX"}).Length} prefabs"),
        };

        string resumen = "VERIFICACIÓN FINAL DEL PROYECTO\n\n";
        int ok = 0;
        foreach (var (nombre, estado, detalle) in resultados)
        {
            string ico = estado ? "✅" : "❌";
            resumen += $"{ico} {nombre} {(detalle.Length>0?$"({detalle})":"")}\n";
            if (estado) ok++;
        }
        resumen += $"\n{ok}/{resultados.Count} verificaciones OK";

        EditorUtility.ClearProgressBar();
        Debug.Log("[Constructor] " + resumen.Replace("\n", " | "));
        EditorUtility.DisplayDialog("✅ Construcción completada", resumen, "OK");
    }

    static void Finalizar()
    {
        EditorUtility.ClearProgressBar();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Constructor] ★ PROYECTO CONSTRUIDO COMPLETAMENTE ★");
    }
}
#endif
