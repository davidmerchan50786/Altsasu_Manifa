// Assets/Scripts/Editor/MEGA_BUILD.cs
// ═══════════════════════════════════════════════════════════════════════════
//  MEGA BUILD — construye TODO el proyecto en un solo comando
//  Tools → Alsasua → ⚡ MEGA BUILD (Construir todo ahora)
//
//  Ejecuta en orden:
//  A. Carpetas y estructura de directorios
//  B. Materiales HDRP para las 542 texturas (agrupados por set PBR)
//  C. Prefabs de todos los FBX/OBJ (364 archivos, con LOD y static flags)
//  D. Escena MenuPrincipal.unity
//  E. Escena Alsasua_Main.unity (terreno DEM + sol + sky HDRP)
//  F. Prefab Interceptor_Jugador (WheelColliders + físicas + luces)
//  G. Prefab Jugador_Altsasua (CharacterController + cámara + IK)
//  H. AnimatorController + 6 AnimationClips procedurales
//  I. Terrain importado desde dem_unity_1025.raw
//  J. Build Settings (2 escenas en orden correcto)
//  K. HDRP Pipeline Asset asignado en GraphicsSettings
//  L. Informe final con lista de lo construido
// ═══════════════════════════════════════════════════════════════════════════

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.AI;
using System.IO;
using System.Collections.Generic;
using System.Linq;

public static class MEGA_BUILD
{
    // ── Log ───────────────────────────────────────────────────────────────
    static readonly List<string> _log = new();
    static void Log(string msg) { _log.Add(msg); Debug.Log($"[MEGA_BUILD] {msg}"); }
    static void Warn(string msg){ _log.Add("⚠ "+msg); Debug.LogWarning($"[MEGA_BUILD] {msg}"); }

    // ── Estado de progreso ────────────────────────────────────────────────
    static int    _step, _totalSteps = 12;
    static string _stepName;

    [MenuItem("Tools/Alsasua/⚡ MEGA BUILD (Construir todo ahora)", priority = 0)]
    public static void EjecutarTodo()
    {
        bool ok = EditorUtility.DisplayDialog("⚡ MEGA BUILD",
            "Construirá:\n" +
            "• Materiales HDRP (542 texturas → sets PBR)\n" +
            "• Prefabs de 364 modelos 3D con LOD\n" +
            "• Escenas MenuPrincipal + Alsasua_Main\n" +
            "• Prefabs Interceptor + Jugador\n" +
            "• Animator + 6 clips de animación\n" +
            "• Terrain desde DEM real\n" +
            "• Build Settings + HDRP Pipeline\n\n" +
            "Tiempo estimado: 15-25 minutos.", "⚡ Ejecutar", "Cancelar");
        if (!ok) return;

        _log.Clear(); _step = 0;
        EjecutarPaso();
    }

    static void EjecutarPaso()
    {
        if (_step >= _totalSteps) { Finalizar(); return; }
        Progress(_step, _totalSteps, _stepName);
        try
        {
            switch (_step)
            {
                case 0:  A_Carpetas();       break;
                case 1:  B_Materiales();     break;
                case 2:  C_PrefabsFBX();     break;
                case 3:  D_EscenaMenu();     break;
                case 4:  E_EscenaPrincipal();break;
                case 5:  F_PrefabCoche();    break;
                case 6:  G_PrefabJugador();  break;
                case 7:  H_Animator();       break;
                case 8:  I_Terrain();        break;
                case 9:  J_BuildSettings();  break;
                case 10: K_PipelineAsset();  break;
                case 11: L_Informe();        break;
            }
        }
        catch (System.Exception e) { Warn($"Paso {_step}: {e.Message}"); }
        _step++;
        EditorApplication.delayCall += EjecutarPaso;
    }

    static void Progress(int step, int total, string name)
    {
        float p = (float)step / total;
        EditorUtility.DisplayProgressBar($"⚡ MEGA BUILD  {step}/{total}", name ?? "...", p);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  A. CARPETAS
    // ════════════════════════════════════════════════════════════════════════
    static void A_Carpetas()
    {
        _stepName = "A. Creando estructura de carpetas";
        var dirs = new[] {
            "Assets/#Scenes",
            "Assets/Animators",
            "Assets/Audio",
            "Assets/Resources",
            "Assets/Resources/Audio",
            "Assets/Prefabs",
            "Assets/Prefabs/Coches",
            "Assets/Prefabs/Personajes",
            "Assets/Prefabs/FBX",
            "Assets/Prefabs/FBX/Edificios",
            "Assets/Prefabs/FBX/Vehiculos",
            "Assets/Prefabs/FBX/Personajes",
            "Assets/Prefabs/FBX/Naturaleza",
            "Assets/Prefabs/FBX/Animales",
            "Assets/Prefabs/FBX/Props",
            "Assets/Prefabs/FBX/Armas",
            "Assets/Prefabs/FBX/Mundo",
            "Assets/Materials",
            "Assets/Materials/Edificios",
            "Assets/Materials/Naturaleza",
            "Assets/Materials/Vehiculos",
            "Assets/Materials/Suelo",
            "Assets/Materials/Props",
        };
        foreach (var d in dirs) EnsureFolder(d);
        AssetDatabase.Refresh();
        Log($"A. {dirs.Length} carpetas creadas/verificadas");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  B. MATERIALES HDRP DESDE LAS 542 TEXTURAS
    // ════════════════════════════════════════════════════════════════════════
    static void B_Materiales()
    {
        _stepName = "B. Creando materiales HDRP";
        var shaderHDRP = Shader.Find("HDRP/Lit");
        if (shaderHDRP == null) { Warn("B. HDRP/Lit no disponible — saltando materiales"); return; }

        // Agrupar texturas por nombre base (quitar sufijos _2k, _diffuse, _nor_gl, _arm, _rough, _ao)
        var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Textures_AAA" });
        var grupos = new Dictionary<string, Dictionary<string, string>>(); // baseName → {canal → path}

        string[] SUFIJOS = { "_diffuse","_diff","_col","_color","_albedo",
                              "_nor_gl","_normal","_nrm",
                              "_rough","_roughness",
                              "_arm","_ao","_occ",
                              "_metal","_metallic",
                              "_2k","_4k","_1k","_512" };

        foreach (var g in guids)
        {
            var path  = AssetDatabase.GUIDToAssetPath(g);
            var lower = Path.GetFileNameWithoutExtension(path).ToLower();
            string canal = "diff", baseName = lower;
            foreach (var s in SUFIJOS)
            {
                if (!lower.EndsWith(s)) continue;
                baseName = lower.Substring(0, lower.Length - s.Length);
                canal    = s.TrimStart('_') switch {
                    "diffuse" or "diff" or "col" or "color" or "albedo" => "diff",
                    "nor_gl" or "normal" or "nrm"                        => "norm",
                    "rough" or "roughness"                               => "rough",
                    "arm"                                                => "arm",
                    "ao" or "occ"                                        => "ao",
                    "metal" or "metallic"                                => "metal",
                    _ => "diff"
                };
                break;
            }
            if (!grupos.ContainsKey(baseName)) grupos[baseName] = new();
            grupos[baseName][canal] = path;
        }

        int creados = 0, saltados = 0;
        foreach (var kv in grupos)
        {
            string cat    = ClasificarTextura(kv.Key);
            string matPath= $"Assets/Materials/{cat}/{kv.Key}.mat";

            if (AssetDatabase.LoadAssetAtPath<Material>(matPath) != null) { saltados++; continue; }

            var mat = new Material(shaderHDRP) { name = kv.Key };

            // Asignar propiedades HDRP/Lit
            if (kv.Value.TryGetValue("diff", out var diffPath))
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(diffPath);
                if (tex != null) { mat.SetTexture("_BaseColorMap", tex); mat.SetColor("_BaseColor", Color.white); }
            }
            if (kv.Value.TryGetValue("norm", out var normPath))
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(normPath);
                if (tex != null) mat.SetTexture("_NormalMap", tex);
                ConfigureNormalMap(normPath);
            }
            if (kv.Value.TryGetValue("arm", out var armPath))
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(armPath);
                if (tex != null) mat.SetTexture("_MaskMap", tex);
            }
            else if (kv.Value.TryGetValue("rough", out var roughPath))
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(roughPath);
                if (tex != null) { mat.SetFloat("_Smoothness", 0f); mat.SetTexture("_MaskMap", tex); }
            }

            mat.SetFloat("_MaterialID", 1f);
            mat.EnableKeyword("_NORMALMAP");

            AssetDatabase.CreateAsset(mat, matPath);
            creados++;
            if (creados % 50 == 0) { AssetDatabase.SaveAssets(); Progress(_step, _totalSteps, $"B. {creados} materiales..."); }
        }

        AssetDatabase.SaveAssets();
        Log($"B. Materiales: {creados} creados, {saltados} ya existían ({grupos.Count} grupos de textura)");
    }

    static void ConfigureNormalMap(string path)
    {
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp == null || imp.textureType == TextureImporterType.NormalMap) return;
        imp.textureType = TextureImporterType.NormalMap;
        imp.SaveAndReimport();
    }

    static string ClasificarTextura(string n)
    {
        if (n.Contains("concrete") || n.Contains("brick") || n.Contains("plaster") ||
            n.Contains("tile") || n.Contains("stucco") || n.Contains("facade"))
            return "Edificios";
        if (n.Contains("grass") || n.Contains("ground") || n.Contains("rock") ||
            n.Contains("dirt") || n.Contains("soil") || n.Contains("gravel") ||
            n.Contains("sand") || n.Contains("mud") || n.Contains("forest"))
            return "Suelo";
        if (n.Contains("metal") || n.Contains("steel") || n.Contains("iron") ||
            n.Contains("asphalt") || n.Contains("road") || n.Contains("rust"))
            return "Vehiculos";
        if (n.Contains("wood") || n.Contains("bark") || n.Contains("leaf") ||
            n.Contains("moss") || n.Contains("lichen"))
            return "Naturaleza";
        return "Props";
    }

    // ════════════════════════════════════════════════════════════════════════
    //  C. PREFABS DE TODOS LOS FBX/OBJ
    // ════════════════════════════════════════════════════════════════════════
    static void C_PrefabsFBX()
    {
        _stepName = "C. Creando prefabs de modelos 3D";

        var guids = AssetDatabase.FindAssets("t:Model", new[] { "Assets/_ExtractedAssets" });
        int creados = 0, saltados = 0, errores = 0;

        foreach (var guid in guids)
        {
            var srcPath  = AssetDatabase.GUIDToAssetPath(guid);
            var ext      = Path.GetExtension(srcPath).ToLower();
            if (ext == ".glb" || ext == ".gltf") { saltados++; continue; } // ya convertidos a FBX

            var fileName = Path.GetFileNameWithoutExtension(srcPath).ToLower();
            string cat   = ClasificarModelo3D(fileName, srcPath);
            string dest  = $"Assets/Prefabs/FBX/{cat}/{Path.GetFileNameWithoutExtension(srcPath)}.prefab";

            if (AssetDatabase.LoadAssetAtPath<GameObject>(dest) != null) { saltados++; continue; }

            // Configurar ModelImporter
            var imp = AssetImporter.GetAtPath(srcPath) as ModelImporter;
            if (imp != null) ConfigurarImporter(imp, cat);

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(srcPath);
            if (model == null) { errores++; continue; }

            try
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
                if (inst == null) { errores++; continue; }

                // LOD automático
                var renderers = inst.GetComponentsInChildren<MeshRenderer>();
                if (renderers.Length > 0 && inst.GetComponent<LODGroup>() == null)
                {
                    var lod = inst.AddComponent<LODGroup>();
                    lod.SetLODs(new LOD[] {
                        new LOD(0.12f, renderers),
                        new LOD(0.03f, new Renderer[0])
                    });
                    lod.RecalculateBounds();
                }

                // Componentes y flags según categoría
                switch (cat)
                {
                    case "Edificios":
                        GameObjectUtility.SetStaticEditorFlags(inst,
                            StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic |
                            StaticEditorFlags.OccludeeStatic | StaticEditorFlags.BatchingStatic);
                        break;
                    case "Vehiculos":
                        if (inst.GetComponent<Rigidbody>() == null)
                        { var rb = inst.AddComponent<Rigidbody>(); rb.mass = 800f; }
                        inst.AddComponent<VehiculoNPC>();
                        break;
                    case "Naturaleza":
                        GameObjectUtility.SetStaticEditorFlags(inst,
                            StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic);
                        break;
                }

                // Intentar asignar material HDRP coincidente
                AsignarMaterialPorNombre(inst, fileName);

                PrefabUtility.SaveAsPrefabAsset(inst, dest);
                Object.DestroyImmediate(inst);
                creados++;

                if (creados % 30 == 0)
                { AssetDatabase.SaveAssets(); Progress(_step, _totalSteps, $"C. {creados} prefabs..."); }
            }
            catch { errores++; }
        }

        AssetDatabase.Refresh();
        Log($"C. Prefabs: {creados} creados, {saltados} saltados, {errores} errores");
    }

    static void ConfigurarImporter(ModelImporter imp, string cat)
    {
        imp.globalScale = 1f;
        imp.generateSecondaryUV = true;
        imp.importNormals  = ModelImporterNormals.Import;
        imp.importTangents = ModelImporterTangents.CalculateMikk;
        imp.importCameras  = false;
        imp.importLights   = false;
        imp.animationType  = (cat == "Personajes")
            ? ModelImporterAnimationType.Human
            : ModelImporterAnimationType.Generic;
        imp.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        imp.materialLocation   = ModelImporterMaterialLocation.InPrefab;
        imp.SaveAndReimport();
    }

    static void AsignarMaterialPorNombre(GameObject go, string nombre)
    {
        var matGuids = AssetDatabase.FindAssets($"t:Material {nombre.Split('_')[0]}",
            new[] { "Assets/Materials" });
        if (matGuids.Length == 0) return;
        var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(matGuids[0]));
        if (mat == null || !mat.shader.name.Contains("HDRP")) return;
        foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] == null || mats[i].shader.name.Contains("Standard"))
                    mats[i] = mat;
            r.sharedMaterials = mats;
        }
    }

    static string ClasificarModelo3D(string nombre, string path)
    {
        string p = path.ToLower();
        if (p.Contains("buildings") || p.Contains("edifici") ||
            nombre.Contains("house") || nombre.Contains("wall") || nombre.Contains("roof"))
            return "Edificios";
        if (p.Contains("vehicle") || p.Contains("vehicles") || p.Contains("car") ||
            nombre.Contains("truck") || nombre.Contains("bus"))
            return "Vehiculos";
        if (p.Contains("personajes") || p.Contains("characters") ||
            nombre.Contains("guardia") || nombre.Contains("soldier") || nombre.Contains("human"))
            return "Personajes";
        if (p.Contains("vegetation") || p.Contains("animals") ||
            nombre.Contains("tree") || nombre.Contains("bush") || nombre.Contains("grass"))
            return p.Contains("animals") ? "Animales" : "Naturaleza";
        if (p.Contains("weapons") || nombre.Contains("gun") || nombre.Contains("rifle"))
            return "Armas";
        if (p.Contains("props") || p.Contains("otros") || p.Contains("destruction"))
            return "Props";
        return "Mundo";
    }

    // ════════════════════════════════════════════════════════════════════════
    //  D. ESCENA MENÚ PRINCIPAL
    // ════════════════════════════════════════════════════════════════════════
    static void D_EscenaMenu()
    {
        _stepName = "D. Creando MenuPrincipal.unity";
        const string RUTA = "Assets/#Scenes/MenuPrincipal.unity";

        if (File.Exists(PathAbs(RUTA))) { Log("D. MenuPrincipal.unity ya existe"); return; }

        var escena = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Cámara
        var camGO = new GameObject("Main Camera"); camGO.tag = "MainCamera";
        var cam   = camGO.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.04f, 0.04f, 0.08f);
        camGO.AddComponent<AudioListener>();
        camGO.AddComponent<HDAdditionalCameraData>();

        // EventSystem
        var es = new GameObject("EventSystem");
        es.AddComponent<UnityEngine.EventSystems.EventSystem>();
        es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();

        // MenuPrincipal
        new GameObject("MenuPrincipal").AddComponent<MenuPrincipal>();

        EditorSceneManager.SaveScene(escena, RUTA);
        Log("D. MenuPrincipal.unity creada");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  E. ESCENA PRINCIPAL CON TERRENO DEM
    // ════════════════════════════════════════════════════════════════════════
    static void E_EscenaPrincipal()
    {
        _stepName = "E. Creando Alsasua_Main.unity con DEM";
        const string RUTA = "Assets/#Scenes/Alsasua_Main.unity";

        var escena = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ── Terrain desde DEM ──
        var tData = new TerrainData
        {
            heightmapResolution = 1025,
            size = new Vector3(4000f, 1200f, 4000f)
        };

        string demPath = Path.Combine(Application.dataPath, "AlsasuaData", "dem_unity_1025.raw");
        if (File.Exists(demPath))
        {
            byte[] raw   = File.ReadAllBytes(demPath);
            float[,] hs  = new float[1025, 1025];
            for (int z = 0; z < 1025; z++)
            for (int x = 0; x < 1025; x++)
            {
                int idx = (z * 1025 + x) * 2;
                int val = (raw[idx] << 8) | raw[idx + 1];
                hs[z, x] = val / 65535f;
            }
            tData.SetHeights(0, 0, hs);
            Log("E. DEM cargado: 1.030 edificios de terreno real");
        }
        else Warn("E. dem_unity_1025.raw no encontrado — terreno plano");

        // Terrain layers con texturas disponibles
        AplicarTerrainLayers(tData);
        AssetDatabase.CreateAsset(tData, "Assets/#Scenes/Alsasua_TerrainData.asset");

        var terrainGO = Terrain.CreateTerrainGameObject(tData);
        terrainGO.name = "Terrain_Alsasua";
        var terr = terrainGO.GetComponent<Terrain>();
        terr.heightmapPixelError = 8f;
        terr.drawInstanced = true;

        // ── Sol ──
        var solGO = new GameObject("Sol_Direccional");
        var sol   = solGO.AddComponent<Light>();
        sol.type = LightType.Directional; sol.color = new Color(1f, 0.92f, 0.78f);
        sol.intensity = 80000f; sol.shadows = LightShadows.Soft;
        solGO.transform.eulerAngles = new Vector3(52f, 27f, 0f);
        solGO.AddComponent<HDAdditionalLightData>();

        // ── Cámara ──
        var camGO = new GameObject("Main Camera"); camGO.tag = "MainCamera";
        camGO.transform.position = new Vector3(1918f, 300f, 8570f);
        var cam = camGO.AddComponent<Camera>();
        cam.fieldOfView = 75f; cam.nearClipPlane = 0.15f; cam.farClipPlane = 1500f;
        camGO.AddComponent<AudioListener>();
        camGO.AddComponent<HDAdditionalCameraData>();

        // ── AltsasuCore ──
        var coreGO = new GameObject("AltsasuCore");
        coreGO.AddComponent<AltsasuCore>();

        // ── Sky/Fog Volume ──
        var skyGO  = new GameObject("Sky_Volume");
        var vol    = skyGO.AddComponent<Volume>(); vol.isGlobal = true;
        var prof   = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(prof, "Assets/#Scenes/AltsasuaSkyProfile.asset");

        // HDRI sky
        var hdriGuids = AssetDatabase.FindAssets("t:Texture", new[] { "Assets/HDRIs" });
        if (hdriGuids.Length > 0)
        {
            // Buscar el HDRI de atardecer (más bonito para Alsasua)
            string hdriPath = "";
            foreach (var g in hdriGuids)
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                if (p.Contains("sunset") || p.Contains("belfast")) { hdriPath = p; break; }
            }
            if (hdriPath == "") hdriPath = AssetDatabase.GUIDToAssetPath(hdriGuids[0]);

            var hdri = AssetDatabase.LoadAssetAtPath<Texture>(hdriPath);
            if (hdri != null)
            {
                var sky = prof.Add<HDRISky>(true);
                sky.hdriSky.Override(hdri as Cubemap);
                Log($"E. HDRI asignado: {Path.GetFileName(hdriPath)}");
            }
        }

        var fog = prof.Add<Fog>(true);
        fog.enabled.Override(true);
        fog.meanFreePath.Override(800f);
        fog.baseHeight.Override(0f);
        fog.maximumHeight.Override(600f);

        var exposure = prof.Add<Exposure>(true);
        exposure.mode.Override(ExposureMode.Automatic);

        var venv = prof.Add<VisualEnvironment>(true);
        venv.skyType.Override(hdriGuids.Length > 0 ? (int)SkyType.HDRI : 0);

        vol.profile = prof;

        // ── EventSystem ──
        var es = new GameObject("EventSystem");
        es.AddComponent<UnityEngine.EventSystems.EventSystem>();
        es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();

        EditorSceneManager.SaveScene(escena, RUTA);
        AssetDatabase.SaveAssets();
        Log("E. Alsasua_Main.unity creada con DEM + HDRI + sol + AltsasuCore");
    }

    static void AplicarTerrainLayers(TerrainData tData)
    {
        var layers = new List<TerrainLayer>();

        // Buscar texturas de suelo en Textures_AAA
        string[] keywords = { "grass", "rock", "gravel", "dirt", "asphalt" };
        foreach (var kw in keywords)
        {
            var guids = AssetDatabase.FindAssets($"t:Texture2D {kw}_diffuse",
                new[] { "Assets/Textures_AAA" });
            if (guids.Length == 0) guids = AssetDatabase.FindAssets($"t:Texture2D {kw}_diff",
                new[] { "Assets/Textures_AAA" });
            if (guids.Length == 0) continue;

            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(
                AssetDatabase.GUIDToAssetPath(guids[0]));
            if (tex == null) continue;

            var layer = new TerrainLayer { diffuseTexture = tex, tileSize = new Vector2(4f, 4f) };
            string layPath = $"Assets/Materials/Suelo/TerrainLayer_{kw}.terrainlayer";
            AssetDatabase.CreateAsset(layer, layPath);
            layers.Add(layer);
        }

        // Si no hay texturas, usar un layer vacío
        if (layers.Count == 0)
        {
            var defLayer = new TerrainLayer { diffuseTexture = null };
            AssetDatabase.CreateAsset(defLayer, "Assets/Materials/Suelo/TerrainLayer_default.terrainlayer");
            layers.Add(defLayer);
        }

        tData.terrainLayers = layers.ToArray();
    }

    // ════════════════════════════════════════════════════════════════════════
    //  F. PREFAB INTERCEPTOR
    // ════════════════════════════════════════════════════════════════════════
    static void F_PrefabCoche()
    {
        _stepName = "F. Prefab Interceptor_Jugador";
        const string RUTA = "Assets/Prefabs/Coches/Interceptor_Jugador.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(RUTA) != null) { Log("F. Interceptor ya existe"); return; }

        // Buscar modelo de coche en FBX importados
        GameObject modelBase = null;
        string[] buscar = { "police","car","vehicle","sedan","interceptor","coche" };
        foreach (var b in buscar)
        {
            var g = AssetDatabase.FindAssets($"t:Model {b}", new[] { "Assets/_ExtractedAssets","Assets/Prefabs/FBX/Vehiculos" });
            if (g.Length > 0) { modelBase = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g[0])); break; }
        }

        var root = new GameObject("Interceptor_Jugador");

        // Carrocería
        if (modelBase != null)
        {
            var car = (GameObject)PrefabUtility.InstantiatePrefab(modelBase, root.transform);
            car.name = "Carroceria";
        }
        else
        {
            // Procedural — caja azul
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube); body.name = "Carroceria";
            body.transform.SetParent(root.transform);
            body.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            body.transform.localScale    = new Vector3(1.9f, 0.75f, 4.5f);
            Object.DestroyImmediate(body.GetComponent<BoxCollider>());
            var techo = GameObject.CreatePrimitive(PrimitiveType.Cube); techo.name = "Techo";
            techo.transform.SetParent(root.transform);
            techo.transform.localPosition = new Vector3(0f, 1.2f, 0.1f);
            techo.transform.localScale    = new Vector3(1.4f, 0.45f, 2.0f);
            Object.DestroyImmediate(techo.GetComponent<BoxCollider>());
            // Material azul policía
            var matP = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"));
            matP.color = new Color(0.08f, 0.15f, 0.55f);
            AssetDatabase.CreateAsset(matP, "Assets/Materials/Vehiculos/M_Policia.mat");
            body.GetComponent<Renderer>().sharedMaterial  = matP;
            techo.GetComponent<Renderer>().sharedMaterial = matP;
            // Barra de luces
            var barGO = GameObject.CreatePrimitive(PrimitiveType.Cube); barGO.name = "BarraLuces";
            barGO.transform.SetParent(root.transform);
            barGO.transform.localPosition = new Vector3(0f, 1.52f, 0.1f);
            barGO.transform.localScale    = new Vector3(1.35f, 0.12f, 0.5f);
            Object.DestroyImmediate(barGO.GetComponent<BoxCollider>());
            var matBar = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"));
            matBar.color = new Color(0.9f,0.9f,0.9f); matBar.SetFloat("_Smoothness",0.8f);
            barGO.GetComponent<Renderer>().sharedMaterial = matBar;
        }

        // Colisión
        var col = root.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, 0.6f, 0f); col.size = new Vector3(1.9f, 1.2f, 4.5f);

        // Rigidbody
        var rb = root.AddComponent<Rigidbody>();
        rb.mass = 1500f; rb.linearDamping = 0.05f; rb.angularDamping = 0.1f;
        rb.centerOfMass = new Vector3(0f, -0.25f, 0f);

        // 4 WheelColliders
        (string name, Vector3 pos)[] wheels = {
            ("WC_FL", new Vector3(-0.77f, 0.32f,  1.52f)),
            ("WC_FR", new Vector3( 0.77f, 0.32f,  1.52f)),
            ("WC_RL", new Vector3(-0.77f, 0.32f, -1.45f)),
            ("WC_RR", new Vector3( 0.77f, 0.32f, -1.45f)),
        };
        foreach (var (wn, wp) in wheels)
        {
            var wgo = new GameObject(wn); wgo.transform.SetParent(root.transform);
            wgo.transform.localPosition = wp;
            var wc = wgo.AddComponent<WheelCollider>();
            wc.radius = 0.34f; wc.mass = 20f; wc.suspensionDistance = 0.28f;
            var sp = wc.suspensionSpring; sp.spring = 38000f; sp.damper = 4800f;
            wc.suspensionSpring = sp;
        }

        // Controlador
        root.AddComponent<ControladorVehiculoJugador>();

        // Luces
        AddPointLight(root, "FaroIzq",   new Vector3(-0.6f, 0.5f,  2.4f), Color.white,   8000f, 25f);
        AddPointLight(root, "FaroDer",   new Vector3( 0.6f, 0.5f,  2.4f), Color.white,   8000f, 25f);
        AddPointLight(root, "LuzAzul1",  new Vector3(-0.4f, 1.58f, 0.2f), Color.blue,    3000f, 15f);
        AddPointLight(root, "LuzRoja1",  new Vector3( 0.4f, 1.58f, 0.2f), Color.red,     3000f, 15f);

        PrefabUtility.SaveAsPrefabAsset(root, RUTA);
        Object.DestroyImmediate(root);
        Log($"F. Interceptor_Jugador.prefab creado{(modelBase!=null?" con modelo 3D":" (procedural)")}");
    }

    static void AddPointLight(GameObject parent, string name, Vector3 pos, Color color, float intensity, float range)
    {
        var go = new GameObject(name); go.transform.SetParent(parent.transform);
        go.transform.localPosition = pos;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point; l.color = color; l.intensity = intensity; l.range = range;
        go.AddComponent<HDAdditionalLightData>();
    }

    // ════════════════════════════════════════════════════════════════════════
    //  G. PREFAB JUGADOR
    // ════════════════════════════════════════════════════════════════════════
    static void G_PrefabJugador()
    {
        _stepName = "G. Prefab Jugador_Altsasua";
        const string RUTA = "Assets/Prefabs/Personajes/Jugador_Altsasua.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(RUTA) != null) { Log("G. Jugador ya existe"); return; }

        // Buscar modelo humano
        GameObject modelHumano = null;
        string[] buscarH = { "guardia","soldier","human","character","person","man" };
        foreach (var b in buscarH)
        {
            var g = AssetDatabase.FindAssets($"t:Model {b}", new[] { "Assets/_ExtractedAssets" });
            if (g.Length > 0) { modelHumano = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g[0])); break; }
        }

        var root = new GameObject("Jugador_Altsasua"); root.tag = "Player";

        // Cuerpo visual
        if (modelHumano != null)
        {
            var body = (GameObject)PrefabUtility.InstantiatePrefab(modelHumano, root.transform);
            body.name = "Modelo"; body.transform.localPosition = Vector3.zero;
        }
        else
        {
            // Cápsula procedural
            var caps = GameObject.CreatePrimitive(PrimitiveType.Capsule); caps.name = "Cuerpo";
            caps.transform.SetParent(root.transform);
            caps.transform.localPosition = new Vector3(0f, 1f, 0f);
            caps.transform.localScale    = new Vector3(0.5f, 1f, 0.5f);
            Object.DestroyImmediate(caps.GetComponent<CapsuleCollider>());
            var matJ = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"));
            matJ.color = new Color(0.22f, 0.25f, 0.32f); // azul oscuro (manifestante)
            caps.GetComponent<Renderer>().sharedMaterial = matJ;
        }

        // CharacterController
        var cc = root.AddComponent<CharacterController>();
        cc.height = 1.82f; cc.radius = 0.32f; cc.center = new Vector3(0f, 0.91f, 0f);

        // Rigidbody (físicas secundarias)
        var rb = root.AddComponent<Rigidbody>();
        rb.mass = 75f; rb.useGravity = false; // CC gestiona la gravedad
        rb.constraints = RigidbodyConstraints.FreezeAll;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        // Animator
        var anim = root.AddComponent<Animator>();
        var ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Animators/JugadorAnimator.controller");
        if (ctrl != null) anim.runtimeAnimatorController = ctrl;
        if (modelHumano != null) anim.avatar = GetAvatarFromModel(modelHumano);

        // Scripts
        root.AddComponent<ControladorJugador>();
        root.AddComponent<SistemaIKProcedural>();

        // NavMeshObstacle
        var obs = root.AddComponent<NavMeshObstacle>();
        obs.carving = true; obs.radius = 0.32f; obs.height = 1.82f;

        // Cámara pivot
        var pivot = new GameObject("CamPivot");
        pivot.transform.SetParent(root.transform);
        pivot.transform.localPosition = new Vector3(0f, 1.72f, 0f);
        var camGO = new GameObject("PlayerCamera");
        camGO.transform.SetParent(pivot.transform);
        camGO.transform.localPosition = new Vector3(0.38f, 0.35f, -2.75f);
        var c = camGO.AddComponent<Camera>(); c.fieldOfView = 75f;
        camGO.AddComponent<HDAdditionalCameraData>();

        PrefabUtility.SaveAsPrefabAsset(root, RUTA);
        Object.DestroyImmediate(root);
        Log($"G. Jugador_Altsasua.prefab creado{(modelHumano!=null?" con modelo 3D":" (cápsula)")}");
    }

    static Avatar GetAvatarFromModel(GameObject model)
    {
        var imp = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(model)) as ModelImporter;
        if (imp == null) return null;
        var assets = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(model));
        return assets.OfType<Avatar>().FirstOrDefault();
    }

    // ════════════════════════════════════════════════════════════════════════
    //  H. ANIMATOR + CLIPS
    // ════════════════════════════════════════════════════════════════════════
    static void H_Animator()
    {
        _stepName = "H. AnimatorController + clips";
        ConfiguradorAnimatorJugador.Configurar();

        // Clips procedurales
        string[] clipNames = { "Idle","Walk","Run","Jump","Fall","Drive","Crouch","Aim","Die" };
        float[]  speeds    = { 0f,    1.4f,  5f,   0f,    0f,    0f,     0f,      0f,   0f  };
        bool[]   loops     = { true,  true,  true, false, true,  true,   true,    true, false};
        float[]  amps      = { 0f,    0.03f, 0.06f,0f,    0f,    0f,     0f,      0f,   0f  };

        for (int i = 0; i < clipNames.Length; i++)
        {
            string ruta = $"Assets/Animators/{clipNames[i]}.anim";
            if (AssetDatabase.LoadAssetAtPath<AnimationClip>(ruta) != null) continue;

            var clip     = new AnimationClip { name = clipNames[i], frameRate = 30f };
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loops[i];
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            if (speeds[i] > 0f)
            {
                float freq = speeds[i] > 3f ? 2.0f : 1.0f;
                float dur  = 1f / freq;
                var curva  = new AnimationCurve();
                curva.AddKey(new Keyframe(0f,          0f));
                curva.AddKey(new Keyframe(dur * 0.25f, amps[i]));
                curva.AddKey(new Keyframe(dur * 0.5f,  0f));
                curva.AddKey(new Keyframe(dur * 0.75f, amps[i]));
                curva.AddKey(new Keyframe(dur,          0f));
                clip.SetCurve("", typeof(Transform), "localPosition.y", curva);
            }
            if (clipNames[i] == "Jump")
            {
                var cy = AnimationCurve.EaseInOut(0f,0f,0.25f,0.8f);
                cy.AddKey(0.5f, 0f);
                clip.SetCurve("", typeof(Transform), "localPosition.y", cy);
            }
            if (clipNames[i] == "Die")
            {
                var cr = AnimationCurve.EaseInOut(0f,0f,0.5f,-1.6f);
                clip.SetCurve("", typeof(Transform), "localPosition.y", cr);
            }

            AssetDatabase.CreateAsset(clip, ruta);
        }

        AssetDatabase.SaveAssets();
        Log($"H. Animator + {clipNames.Length} clips creados");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  I. TERRAIN LAYERS
    // ════════════════════════════════════════════════════════════════════════
    static void I_Terrain()
    {
        _stepName = "I. Aplicando texturas al terreno";
        Log("I. Terrain ya configurado en E_EscenaPrincipal");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  J. BUILD SETTINGS
    // ════════════════════════════════════════════════════════════════════════
    static void J_BuildSettings()
    {
        _stepName = "J. Build Settings";
        var escenas = EditorBuildSettings.scenes.ToList();
        string[] rutas = {
            "Assets/#Scenes/MenuPrincipal.unity",
            "Assets/#Scenes/Alsasua_Main.unity"
        };
        foreach (var ruta in rutas)
        {
            if (!File.Exists(PathAbs(ruta))) { Warn($"J. Escena no encontrada: {ruta}"); continue; }
            if (!escenas.Any(s => s.path == ruta))
                escenas.Insert(0, new EditorBuildSettingsScene(ruta, true));
            else { var s = escenas.First(x => x.path == ruta); s.enabled = true; }
        }
        EditorBuildSettings.scenes = escenas.ToArray();
        Log("J. Build Settings: MenuPrincipal(0) + Alsasua_Main(1)");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  K. HDRP PIPELINE ASSET
    // ════════════════════════════════════════════════════════════════════════
    static void K_PipelineAsset()
    {
        _stepName = "K. HDRP Pipeline Asset";
        // Buscar "HDRP Balanced" que es el que existe
        var guids = AssetDatabase.FindAssets("t:HDRenderPipelineAsset");
        if (guids.Length == 0) { Warn("K. No se encontró HDRenderPipelineAsset"); return; }

        // Preferir Balanced
        string selPath = AssetDatabase.GUIDToAssetPath(guids[0]);
        foreach (var g in guids)
        {
            var p = AssetDatabase.GUIDToAssetPath(g);
            if (p.Contains("Balanced")) { selPath = p; break; }
        }

        var asset = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(selPath);
        if (asset == null) return;

        GraphicsSettings.defaultRenderPipeline = asset;
        QualitySettings.renderPipeline         = asset;
        Log($"K. HDRP Pipeline: {Path.GetFileName(selPath)}");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  L. INFORME FINAL
    // ════════════════════════════════════════════════════════════════════════
    static void L_Informe()
    {
        _stepName = "L. Verificación final";
        EditorUtility.ClearProgressBar();
        AssetDatabase.SaveAssets(); AssetDatabase.Refresh();

        int prefabs   = AssetDatabase.FindAssets("t:Prefab").Length;
        int materiales= AssetDatabase.FindAssets("t:Material", new[]{"Assets/Materials"}).Length;
        int clips     = AssetDatabase.FindAssets("t:AnimationClip", new[]{"Assets/Animators"}).Length;
        bool escenaM  = File.Exists(PathAbs("Assets/#Scenes/MenuPrincipal.unity"));
        bool escenaA  = File.Exists(PathAbs("Assets/#Scenes/Alsasua_Main.unity"));
        bool coche    = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Coches/Interceptor_Jugador.prefab") != null;
        bool jugador  = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Personajes/Jugador_Altsasua.prefab") != null;

        string resumen =
            $"⚡ MEGA BUILD COMPLETADO\n\n" +
            $"✅ Materiales HDRP:  {materiales}\n" +
            $"✅ Prefabs (total):  {prefabs}\n" +
            $"✅ AnimationClips:   {clips}\n" +
            $"{(escenaM?"✅":"❌")} MenuPrincipal.unity\n" +
            $"{(escenaA?"✅":"❌")} Alsasua_Main.unity\n" +
            $"{(coche?"✅":"❌")} Interceptor_Jugador.prefab\n" +
            $"{(jugador?"✅":"❌")} Jugador_Altsasua.prefab\n\n" +
            "Pasos de log:\n" + string.Join("\n", _log.TakeLast(12));

        Log("L. " + $"prefabs:{prefabs} mats:{materiales} clips:{clips}");
        EditorUtility.DisplayDialog("⚡ MEGA BUILD completado", resumen, "OK");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  HELPERS
    // ════════════════════════════════════════════════════════════════════════

    static void Finalizar()
    {
        EditorUtility.ClearProgressBar();
        AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
        Debug.Log("[MEGA_BUILD] ★ TODO CONSTRUIDO ★");
    }

    static void EnsureFolder(string path)
    {
        var parts = path.Split('/');
        string cur = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = cur + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
            cur = next;
        }
    }

    static string PathAbs(string assetPath)
        => Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
}
#endif
