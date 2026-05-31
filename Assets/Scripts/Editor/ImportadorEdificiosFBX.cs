// Assets/Scripts/Editor/ImportadorEdificiosFBX.cs
// ═══════════════════════════════════════════════════════════════════════════
//  B6 — IMPORTADOR EDIFICIOS FBX
//  Menú: Altsasua/Importar Edificios FBX
//
//  Para cada edificio de buildings_fusion_final.json (hasta maxEdificios):
//    1. Busca FBX individual  → pathFBX/{id}.fbx
//    2. Busca arquetipo       → pathArch/{archetype}.fbx
//    3. Fallback primitiva    → Cube escalado a footprint × height_m
//    4. Configura LODGroup si el FBX contiene _LOD0/_LOD1/_LOD2/_LOD3
//    5. Material HDRP/Lit:
//       - Con textura real (photo_building_mapping.json) → BaseColor+Normal
//       - Sin textura → material por arquetipo desde Textures_AAA
//       - Color tint de facade_color_hex, enableInstancing = true
//    6. Guarda .mat en Assets/Materials/Buildings/
//
//  Usa GeoDataAlsasua.OX / OZ / Z_MIN — nunca literales.
// ═══════════════════════════════════════════════════════════════════════════

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public class ImportadorEdificiosFBX : EditorWindow
{
    // ── Rutas ────────────────────────────────────────────────────────────────
    string pathFBX     = "Assets/Models/Buildings_Generated/";
    string pathArch    = "Assets/Models/Archetypes_Maya/";
    string pathJson    = "Assets/AlsasuaData/buildings_fusion_final.json";
    string pathTex     = "Assets/AlsasuaData/FacadeTextures/Processed/";
    string pathMapping = "Assets/AlsasuaData/photo_building_mapping.json";

    // ── Opciones ─────────────────────────────────────────────────────────────
    bool usarLODs       = true;
    bool gpuInstancing  = true;
    int  maxEdificios   = 200;
    bool soloConFotos   = false;

    // ── Ventana ──────────────────────────────────────────────────────────────
    [MenuItem("Altsasua/Importar Edificios FBX")]
    public static void AbrirVentana()
        => GetWindow<ImportadorEdificiosFBX>("Importar Edificios FBX");

    void OnGUI()
    {
        GUILayout.Label("B6 — Importador Edificios FBX", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        GUILayout.Label("Rutas", EditorStyles.miniBoldLabel);
        pathFBX     = EditorGUILayout.TextField("Path FBX individuales",  pathFBX);
        pathArch    = EditorGUILayout.TextField("Path Arquetipos Maya",    pathArch);
        pathJson    = EditorGUILayout.TextField("JSON buildings_fusion",   pathJson);
        pathTex     = EditorGUILayout.TextField("Path Texturas Fachadas",  pathTex);
        pathMapping = EditorGUILayout.TextField("JSON photo_mapping",      pathMapping);

        EditorGUILayout.Space();
        GUILayout.Label("Opciones", EditorStyles.miniBoldLabel);
        usarLODs      = EditorGUILayout.Toggle("Usar LODs",              usarLODs);
        gpuInstancing = EditorGUILayout.Toggle("GPU Instancing",         gpuInstancing);
        maxEdificios  = EditorGUILayout.IntField("Max edificios",        maxEdificios);
        soloConFotos  = EditorGUILayout.Toggle("Solo con fotos reales",  soloConFotos);

        EditorGUILayout.Space();
        if (GUILayout.Button("Importar Edificios", GUILayout.Height(32)))
            ImportarEdificios();
    }

    // ════════════════════════════════════════════════════════════════════════
    //  MÉTODO PRINCIPAL
    // ════════════════════════════════════════════════════════════════════════
    void ImportarEdificios()
    {
        // ── Cargar JSON de edificios ─────────────────────────────────────────
        string jsonPath = Path.Combine(Application.dataPath, "..", pathJson)
                              .Replace("Assets/../", "");
        // Ruta relativa a Application.dataPath
        string absJson = Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", pathJson));

        if (!File.Exists(absJson))
        {
            Debug.LogError($"[B6] No se encontró el JSON: {absJson}");
            return;
        }

        string jsonRaw = File.ReadAllText(absJson);
        // JsonUtility necesita un wrapper de array
        string wrappedJson = "{\"items\":" + jsonRaw + "}";
        BuildingList buildingList = JsonUtility.FromJson<BuildingList>(wrappedJson);

        if (buildingList == null || buildingList.items == null)
        {
            Debug.LogError("[B6] Error al parsear buildings_fusion_final.json");
            return;
        }

        // ── Cargar photo_building_mapping.json ───────────────────────────────
        Dictionary<string, PhotoMappingEntry> photoMap =
            new Dictionary<string, PhotoMappingEntry>();

        string absMappingPath = Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", pathMapping));

        if (File.Exists(absMappingPath))
        {
            string mappingRaw = File.ReadAllText(absMappingPath);
            string wrappedMapping = "{\"mappings\":" + mappingRaw + "}";
            PhotoMapping pm = JsonUtility.FromJson<PhotoMapping>(wrappedMapping);
            if (pm?.mappings != null)
                foreach (var entry in pm.mappings)
                    if (!string.IsNullOrEmpty(entry.edificio_id))
                        photoMap[entry.edificio_id] = entry;
        }
        else
        {
            Debug.LogWarning($"[B6] No se encontró photo_building_mapping.json en {absMappingPath}. Continuando sin texturas reales.");
        }

        // ── Carpeta de materiales ────────────────────────────────────────────
        const string matDir = "Assets/Materials/Buildings";
        if (!AssetDatabase.IsValidFolder(matDir))
        {
            AssetDatabase.CreateFolder("Assets/Materials", "Buildings");
        }

        // ── Objeto padre ─────────────────────────────────────────────────────
        GameObject padre = GameObject.Find("Edificios_AAA");
        if (padre == null)
            padre = new GameObject("Edificios_AAA");

        int countTotal    = 0;
        int countTextura  = 0;
        int countArquetipo = 0;
        int countPrimitiva = 0;

        int limite = Mathf.Min(maxEdificios, buildingList.items.Length);

        for (int i = 0; i < limite; i++)
        {
            BuildingData b = buildingList.items[i];

            if (soloConFotos && !b.has_mapillary_photos) continue;

            // ── Coordenadas Unity ────────────────────────────────────────────
            float posX = (b.center_unity != null && b.center_unity.Length >= 2)
                         ? b.center_unity[0] : GeoDataAlsasua.OX;
            float posZ = (b.center_unity != null && b.center_unity.Length >= 2)
                         ? b.center_unity[1] : GeoDataAlsasua.OZ;
            float posY = GeoDataAlsasua.AlturaTerreno(posX, posZ);

            // ── Buscar FBX o crear primitiva ─────────────────────────────────
            GameObject go = null;
            bool usoPrimitiva = false;

            // 1. FBX individual
            string fbxPath = pathFBX + b.id + ".fbx";
            GameObject fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);

            if (fbxAsset != null)
            {
                go = (GameObject)PrefabUtility.InstantiatePrefab(fbxAsset, padre.transform);
            }
            else
            {
                // 2. Arquetipo
                string archPath = pathArch + b.archetype + ".fbx";
                GameObject archAsset = AssetDatabase.LoadAssetAtPath<GameObject>(archPath);

                if (archAsset != null)
                {
                    go = (GameObject)PrefabUtility.InstantiatePrefab(archAsset, padre.transform);
                    countArquetipo++;
                }
                else
                {
                    // 3. Primitiva fallback
                    go = GenerarPrimitivaEdificio(b);
                    go.transform.SetParent(padre.transform, false);
                    usoPrimitiva = true;
                    countPrimitiva++;
                }
            }

            if (go == null) continue;

            go.name = b.id;
            go.transform.position = new Vector3(posX, posY, posZ);

            // ── LOD Group ────────────────────────────────────────────────────
            if (usarLODs && !usoPrimitiva)
                ConfigurarLODGroup(go);

            // ── Colliders para física y NavMesh ──────────────────────────────
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.name.Contains("LOD0") || (!mf.name.Contains("LOD")))
                {
                    var col = mf.gameObject.GetComponent<MeshCollider>()
                              ?? mf.gameObject.AddComponent<MeshCollider>();
                    col.sharedMesh = mf.sharedMesh;
                    col.convex = false;
                }
            }
            // Marcar como estático para bake NavMesh
            GameObjectUtility.SetStaticEditorFlags(go,
                StaticEditorFlags.NavigationStatic |
                StaticEditorFlags.ContributeGI |
                StaticEditorFlags.OccluderStatic);

            // ── Material ─────────────────────────────────────────────────────
            bool tieneTextura = photoMap.ContainsKey(b.id);
            Material mat = tieneTextura
                ? CrearMaterialConTextura(b, photoMap[b.id], matDir)
                : CrearMaterialArquetipo(b, matDir);

            if (mat != null)
            {
                if (gpuInstancing)
                    mat.enableInstancing = true;

                AplicarMaterialRecursivo(go, mat);

                if (tieneTextura) countTextura++;
            }

            // ── Registrar en SistemaZonas si está disponible en escena ───────
            var sistemaZonas = Object.FindObjectOfType<SistemaZonas>();
            if (sistemaZonas != null)
            {
                // TODO: construir EdificioData completo desde BuildingData cuando
                // se unifique el modelo de datos entre ImportadorEdificiosFBX y GeneradorMundoOSM.
                Debug.Log($"[B6] Edificio {b.id} importado — registrar manualmente en SistemaZonas si es necesario");
            }

            countTotal++;

            EditorUtility.DisplayProgressBar(
                "Importando Edificios",
                $"Procesando {b.id} ({i + 1}/{limite})",
                (float)(i + 1) / limite);
        }

        EditorUtility.ClearProgressBar();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[B6] Importados {countTotal} edificios. " +
                  $"Con textura real: {countTextura}. " +
                  $"Con arquetipo: {countArquetipo}. " +
                  $"Primitiva: {countPrimitiva}.");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  LOD GROUP
    // ════════════════════════════════════════════════════════════════════════
    static void ConfigurarLODGroup(GameObject go)
    {
        // Buscar renderers por sufijo de nombre
        var lod0 = BuscarRenderersPorSufijo(go, "_LOD0");
        var lod1 = BuscarRenderersPorSufijo(go, "_LOD1");
        var lod2 = BuscarRenderersPorSufijo(go, "_LOD2");
        var lod3 = BuscarRenderersPorSufijo(go, "_LOD3");

        // Solo configurar si hay al menos LOD0 y LOD1
        if (lod0.Count == 0 || lod1.Count == 0) return;

        LODGroup lodGroup = go.GetComponent<LODGroup>();
        if (lodGroup == null) lodGroup = go.AddComponent<LODGroup>();

        var lods = new System.Collections.Generic.List<LOD>();

        lods.Add(new LOD(0.25f, lod0.ToArray()));
        lods.Add(new LOD(0.08f, lod1.ToArray()));

        if (lod2.Count > 0) lods.Add(new LOD(0.02f,  lod2.ToArray()));
        if (lod3.Count > 0) lods.Add(new LOD(0.005f, lod3.ToArray()));

        lodGroup.SetLODs(lods.ToArray());
        lodGroup.RecalculateBounds();
    }

    static List<Renderer> BuscarRenderersPorSufijo(GameObject root, string sufijo)
    {
        var result = new List<Renderer>();
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            if (r.gameObject.name.EndsWith(sufijo, StringComparison.OrdinalIgnoreCase))
                result.Add(r);
        return result;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  MATERIALES
    // ════════════════════════════════════════════════════════════════════════
    Material CrearMaterialConTextura(BuildingData b, PhotoMappingEntry entry, string matDir)
    {
        string matPath = $"{matDir}/{b.id}.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (existing != null) return existing;

        // Buscar texturas procesadas
        string fotoBase = Path.GetFileNameWithoutExtension(entry.foto_procesada ?? b.id);
        string texDiffuse = pathTex + fotoBase + "_diffuse.png";
        string texNormal  = pathTex + fotoBase + "_normal.png";

        Texture2D albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(texDiffuse);
        Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(texNormal);

        Shader hdrpLit = Shader.Find("HDRP/Lit");
        if (hdrpLit == null) hdrpLit = Shader.Find("HDRenderPipeline/Lit");

        Material mat = new Material(hdrpLit != null ? hdrpLit : Shader.Find("Standard"));
        mat.name = b.id;

        if (albedo != null) mat.SetTexture("_BaseColorMap",   albedo);
        if (normal != null) mat.SetTexture("_NormalMap",      normal);
        mat.SetFloat("_Smoothness", 0.12f);
        mat.SetFloat("_Metallic",   0f);

        AplicarColorTint(mat, b.facade_color_hex);

        AssetDatabase.CreateAsset(mat, matPath);
        return mat;
    }

    Material CrearMaterialArquetipo(BuildingData b, string matDir)
    {
        string matPath = $"{matDir}/{b.id}.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (existing != null) return existing;

        string texPath = ResolverTexturaArquetipo(b.archetype, b.year_built);
        Texture2D albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);

        Shader hdrpLit = Shader.Find("HDRP/Lit");
        if (hdrpLit == null) hdrpLit = Shader.Find("HDRenderPipeline/Lit");

        Material mat = new Material(hdrpLit != null ? hdrpLit : Shader.Find("Standard"));
        mat.name = b.id;

        if (albedo != null) mat.SetTexture("_BaseColorMap", albedo);
        mat.SetFloat("_Smoothness", 0.1f);
        mat.SetFloat("_Metallic",   0f);

        AplicarColorTint(mat, b.facade_color_hex);

        AssetDatabase.CreateAsset(mat, matPath);
        return mat;
    }

    static string ResolverTexturaArquetipo(string archetype, int yearBuilt)
    {
        // Asignar textura base según arquetipo/año
        if (!string.IsNullOrEmpty(archetype))
        {
            if (archetype.Contains("caserio") || archetype.Contains("baserri"))
                return "Assets/_ExtractedAssets/Textures/wall_cobblestone_cracks.png";

            if (archetype.Contains("pre1940") || yearBuilt > 0 && yearBuilt < 1940)
                return "Assets/_ExtractedAssets/Textures/wall_bricks_old.png";

            if (archetype.Contains("industrial"))
                return "Assets/_ExtractedAssets/Textures/brick7.png";
        }

        // Default
        return "Assets/_ExtractedAssets/Textures/wall_bricks_old.png";
    }

    static void AplicarColorTint(Material mat, string hexColor)
    {
        if (string.IsNullOrEmpty(hexColor)) return;
        if (ColorUtility.TryParseHtmlString(hexColor, out Color c))
        {
            // HDRP/Lit usa _BaseColor; Standard usa _Color
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", c);
            else
                mat.color = c;
        }
    }

    static void AplicarMaterialRecursivo(GameObject go, Material mat)
    {
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            // Asignar el mismo material a todos los slots
            var mats = new Material[r.sharedMaterials.Length];
            for (int j = 0; j < mats.Length; j++) mats[j] = mat;
            r.sharedMaterials = mats;
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  PRIMITIVA FALLBACK
    // ════════════════════════════════════════════════════════════════════════
    static GameObject GenerarPrimitivaEdificio(BuildingData b)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "PRIM_" + b.id;

        // Dimensiones del bounding box del footprint
        float width  = 10f;
        float depth  = 10f;
        float height = Mathf.Max(3f, b.height_m);

        if (b.footprint_unity != null && b.footprint_unity.Length >= 2)
        {
            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var pt in b.footprint_unity)
            {
                if (pt.Length < 2) continue;
                if (pt[0] < minX) minX = pt[0];
                if (pt[0] > maxX) maxX = pt[0];
                if (pt[1] < minZ) minZ = pt[1];
                if (pt[1] > maxZ) maxZ = pt[1];
            }
            width  = Mathf.Max(1f, maxX - minX);
            depth  = Mathf.Max(1f, maxZ - minZ);
        }

        go.transform.localScale = new Vector3(width, height, depth);
        // El pivot del Cube está en el centro; elevar medio height para que base esté en Y=0
        go.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);

        // Material provisional con color de fachada
        Shader hdrpLit = Shader.Find("HDRP/Lit");
        if (hdrpLit == null) hdrpLit = Shader.Find("HDRenderPipeline/Lit");

        Material mat = new Material(hdrpLit != null ? hdrpLit : Shader.Find("Standard"));
        mat.name = "PRIM_" + b.id;
        AplicarColorTint(mat, b.facade_color_hex);
        mat.enableInstancing = true;

        go.GetComponent<Renderer>().sharedMaterial = mat;

        return go;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  CLASES DE DATOS (inner)
    // ════════════════════════════════════════════════════════════════════════

    [Serializable]
    class BuildingData
    {
        public string   id;
        public string   archetype;
        public string   roof_type;
        public string   facade_color_hex;
        public string   height_source;
        public float    height_m;
        public float    surface_m2;
        public int      floors;
        public int      year_built;
        public float[][] footprint_unity;
        public float[]  center_unity;
        public bool     has_mapillary_photos;
    }

    [Serializable]
    class BuildingList { public BuildingData[] items; }

    [Serializable]
    class PhotoMapping { public PhotoMappingEntry[] mappings; }

    [Serializable]
    class PhotoMappingEntry
    {
        public string foto_procesada;
        public string edificio_id;
        public string zona;
        public string confidence;
    }
}
#endif
