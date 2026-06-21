// Assets/Scripts/Editor/AplicadorTexturasAAA.cs
// ═══════════════════════════════════════════════════════════════════════════
//  Tools → Alsasua → 🎨 Aplicar Texturas AAA
//
//  Lee las texturas descargadas de Poly Haven en Assets/Textures_AAA/
//  y las aplica automáticamente a:
//    · Edificios Zone_01 — por tipo (ladrillo, piedra, hormigón, iglesia)
//    · Carreteras — asfalto real PBR
//    · Adoquines — cobblestone real PBR
//    · Tejados — teja árabe CC0
//    · Balcones — madera oscura real
//    · Terreno Terrain — capas de textura PBR
//
//  También configura el terrain con las texturas descargadas.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;
using System.Collections.Generic;

public static class AplicadorTexturasAAA
{
    private const string TEX_DIR  = "Assets/Textures_AAA";
    private const string HDRI_DIR = "Assets/HDRIs";

    [MenuItem("Tools/Alsasua/Render/🎨 Texturas AAA PBR", priority = 10)]
    public static void Aplicar()
    {
        // Verificar que hay texturas descargadas
        if (!AssetDatabase.IsValidFolder(TEX_DIR))
        {
            EditorUtility.DisplayDialog("Sin texturas",
                $"No se encontró {TEX_DIR}.\n\n" +
                "Ejecuta primero: .\\DescargarAssetsAAA.ps1\n" +
                "desde PowerShell en la raíz del proyecto.", "OK");
            return;
        }

        // Refrescar para que Unity vea los nuevos archivos
        AssetDatabase.Refresh();

        int cambios = 0;
        var shader = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");

        EditorUtility.DisplayProgressBar("Aplicando texturas AAA", "Cargando...", 0f);

        cambios += AplicarAEdificios(shader);
        cambios += AplicarACarreteras(shader);
        cambios += AplicarATejados(shader);
        cambios += AplicarATerreno();
        cambios += ConfigurarSkyHDRI();

        EditorUtility.ClearProgressBar();
        EditorUtility.SetDirty(Camera.main);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();

        Debug.Log($"[TexturasAAA] ✅ {cambios} materiales actualizados con PBR real.");
        EditorUtility.DisplayDialog("Texturas AAA aplicadas",
            $"✅ {cambios} materiales actualizados con texturas PBR de Poly Haven.\n\n" +
            "Los edificios ahora tienen piedra/ladrillo real fotorrealista.", "OK");
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  EDIFICIOS — asignar por tipo de edificio
    // ─────────────────────────────────────────────────────────────────────────

    static int AplicarAEdificios(Shader shader)
    {
        var zona = GameObject.Find("Zone_01_HerrikoPlaza_Centro");
        if (zona == null) return 0;

        // Grupos de texturas por tipo de edificio
        // Cada grupo tiene varias opciones para variedad visual
        var texturasPorTipo = new Dictionary<string, string[]>
        {
            // Casco histórico — piedra y ladrillo antiguo
            ["casco"] = new[] {
                "church_bricks_02", "church_bricks_03", "castle_brick_01",
                "brick_wall_001",   "brick_wall_002",   "brick_wall_007",
                "beige_wall_001",   "beige_wall_002",   "clay_plaster"
            },
            // Apartamentos — revoco moderno
            ["aptos"] = new[] {
                "beige_wall_001", "beige_wall_002", "brushed_concrete",
                "brushed_concrete_2", "blue_plaster_weathered"
            },
            // Industrial — hormigón y chapa
            ["indus"] = new[] {
                "brushed_concrete", "brushed_concrete_03", "box_profile_metal_sheet"
            },
            // Iglesias — piedra de sillería
            ["iglesia"] = new[] {
                "church_bricks_02", "church_bricks_03", "castle_brick_01"
            }
        };

        var rnd = new System.Random(1337);
        int cambios = 0;
        bool activa = zona.activeSelf; zona.SetActive(true);

        var hijos = new Transform[zona.transform.childCount];
        for (int hi = 0; hi < zona.transform.childCount; hi++)
            hijos[hi] = zona.transform.GetChild(hi);

        int procesados = 0;
        foreach (Transform hijo in hijos)
        {
            if (hijo == null) continue;
            string hn = hijo.name.ToLower();
            if (hn.StartsWith("carreteras") || hn.StartsWith("waypoints") ||
                hn.StartsWith("civiles")    || hn.StartsWith("batch_")) continue;

            procesados++;
            EditorUtility.DisplayProgressBar("Aplicando texturas a edificios",
                hijo.name, (float)procesados / hijos.Length);

            // Determinar tipo
            string tipo = "casco";
            if (hn.Contains("eliza") || hn.Contains("iglesia") || hn.Contains("chapel"))
                tipo = "iglesia";
            else if (hn.Contains("apart") || hn.Contains("block"))
                tipo = "aptos";
            else if (hn.Contains("industrial") || hn.Contains("garage") || hn.Contains("farm"))
                tipo = "indus";

            var opciones = texturasPorTipo[tipo];
            string texId = opciones[rnd.Next(opciones.Length)];

            // Aplicar a las paredes LOD0
            var renderer = (hijo.Find("LOD0/Paredes") ?? hijo.Find("Paredes"))
                           ?.GetComponent<MeshRenderer>();
            if (renderer == null) continue;

            var mat = CrearMatPBR(shader, texId, "Fachadas");
            if (mat != null)
            {
                renderer.sharedMaterial = mat;
                EditorUtility.SetDirty(renderer);
                cambios++;
            }
        }

        zona.SetActive(activa);
        return cambios;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  CARRETERAS
    // ─────────────────────────────────────────────────────────────────────────

    static int AplicarACarreteras(Shader shader)
    {
        int cambios = 0;
        var zona = GameObject.Find("Zone_01_HerrikoPlaza_Centro");
        if (zona == null) return 0;

        bool activa = zona.activeSelf; zona.SetActive(true);

        var carre = zona.transform.Find("Carreteras");
        if (carre == null) { zona.SetActive(activa); return 0; }

        // Asfalto para calles principales
        var batchAsfalto = carre.Find("Batch_asfalto")?.GetComponent<MeshRenderer>();
        if (batchAsfalto != null)
        {
            var mat = CrearMatPBR(shader, "asphalt_02", "Suelo", tiling: 8f);
            if (mat != null) { batchAsfalto.sharedMaterial = mat; EditorUtility.SetDirty(batchAsfalto); cambios++; }
        }

        // Bordillo
        var batchBordillo = carre.Find("Batch_bordillo")?.GetComponent<MeshRenderer>();
        if (batchBordillo != null)
        {
            var mat = CrearMatPBR(shader, "brushed_concrete", "Fachadas", tiling: 4f);
            if (mat != null) { batchBordillo.sharedMaterial = mat; EditorUtility.SetDirty(batchBordillo); cambios++; }
        }

        // Caminos de tierra
        var batchCamino = carre.Find("Batch_camino")?.GetComponent<MeshRenderer>();
        if (batchCamino != null)
        {
            var mat = CrearMatPBR(shader, "brown_mud", "Suelo", tiling: 5f);
            if (mat != null) { batchCamino.sharedMaterial = mat; EditorUtility.SetDirty(batchCamino); cambios++; }
        }

        zona.SetActive(activa);
        return cambios;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  TEJADOS
    // ─────────────────────────────────────────────────────────────────────────

    static int AplicarATejados(Shader shader)
    {
        int cambios = 0;
        var zona = GameObject.Find("Zone_01_HerrikoPlaza_Centro");
        if (zona == null) return 0;

        bool activa = zona.activeSelf; zona.SetActive(true);

        var hijos = new Transform[zona.transform.childCount];
        for (int hi = 0; hi < zona.transform.childCount; hi++)
            hijos[hi] = zona.transform.GetChild(hi);

        var rnd = new System.Random(42);
        string[] tejadosOpciones = { "clay_roof_tiles", "clay_roof_tiles_02", "clay_roof_tiles_03" };

        foreach (Transform hijo in hijos)
        {
            if (hijo == null) continue;
            var tejado = hijo.Find("LOD0/Tejado") ?? hijo.Find("Tejado");
            if (tejado == null) continue;
            var mr = tejado.GetComponent<MeshRenderer>();
            if (mr == null) continue;

            bool esIglesia = hijo.name.ToLower().Contains("eliza") ||
                             hijo.name.ToLower().Contains("iglesia");
            string texId = esIglesia ? "castle_wall_slates"
                                     : tejadosOpciones[rnd.Next(tejadosOpciones.Length)];
            var mat = CrearMatPBR(shader, texId, "Tejados", tiling: 3f);
            if (mat != null) { mr.sharedMaterial = mat; EditorUtility.SetDirty(mr); cambios++; }
        }

        zona.SetActive(activa);
        return cambios;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  TERRENO — aplicar texturas PBR a las capas del Terrain
    // ─────────────────────────────────────────────────────────────────────────

    static int AplicarATerreno()
    {
        var terrain = Terrain.activeTerrain;
        if (terrain == null || terrain.terrainData == null) return 0;

        // Crear TerrainLayers con las texturas descargadas
        var layerDefs = new[]
        {
            new { id = "aerial_asphalt_01", name = "Asfalto",  tileX = 8f,  tileZ = 8f  },
            new { id = "brown_mud_03",      name = "Barro",    tileX = 4f,  tileZ = 4f  },
            new { id = "aerial_grass_rock", name = "Hierba",   tileX = 6f,  tileZ = 6f  },
            new { id = "aerial_rocks_01",   name = "Roca",     tileX = 10f, tileZ = 10f },
        };

        var layers = new List<TerrainLayer>();
        bool alguno = false;

        foreach (var ld in layerDefs)
        {
            var albedo = BuscarTextura($"{ld.id}_Albedo", "Suelo");
            if (albedo == null) continue;

            var layer = new TerrainLayer
            {
                diffuseTexture  = albedo,
                tileSize        = new Vector2(ld.tileX, ld.tileZ),
                name            = ld.name
            };

            var normal = BuscarTextura($"{ld.id}_Normal", "Suelo");
            if (normal != null) layer.normalMapTexture = normal;

            string layerPath = $"Assets/Textures_AAA/TerrainLayers/{ld.name}.terrainlayer";
            if (!AssetDatabase.IsValidFolder("Assets/Textures_AAA/TerrainLayers"))
                AssetDatabase.CreateFolder("Assets/Textures_AAA", "TerrainLayers");

            AssetDatabase.CreateAsset(layer, layerPath);
            layers.Add(layer);
            alguno = true;
        }

        if (alguno)
        {
            Undo.RecordObject(terrain.terrainData, "Aplicar TerrainLayers AAA");
            terrain.terrainData.terrainLayers = layers.ToArray();
            EditorUtility.SetDirty(terrain.terrainData);
            Debug.Log($"[TexturasAAA] 🌍 {layers.Count} capas de terreno PBR aplicadas.");
        }

        return layers.Count;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  HDRI SKY — asignar el mejor HDRI al volume del HDRP
    // ─────────────────────────────────────────────────────────────────────────

    static int ConfigurarSkyHDRI()
    {
        // Buscar el HDRI más adecuado para Alsasua (clima atlántico, nublado)
        string[] preferidos = { "approaching_storm", "autumn_field", "kloppenheim_06_puresky" };
        Texture hdriTextura = null;

        foreach (var id in preferidos)
        {
            var ruta = $"{HDRI_DIR}/{id}.hdr";
            hdriTextura = AssetDatabase.LoadAssetAtPath<Texture>(ruta);
            if (hdriTextura != null) break;
        }

        if (hdriTextura == null)
        {
            // Buscar cualquier HDR en la carpeta
            var guids = AssetDatabase.FindAssets("t:Texture", new[]{ HDRI_DIR });
            if (guids.Length > 0)
                hdriTextura = AssetDatabase.LoadAssetAtPath<Texture>(
                    AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        if (hdriTextura == null) return 0;

        // Asignar al HDRP Volume
        var vol = Object.FindFirstObjectByType<UnityEngine.Rendering.Volume>();
        if (vol?.profile == null) return 0;

        // Intentar asignar HDRISky si existe en el perfil
        if (vol.profile.TryGet<UnityEngine.Rendering.HighDefinition.HDRISky>(out var sky))
        {
            sky.hdriSky.value = (Cubemap)hdriTextura;
            EditorUtility.SetDirty(vol.profile);
            Debug.Log($"[TexturasAAA] 🌄 HDRI '{hdriTextura.name}' asignado al cielo.");
            return 1;
        }

        // Si no tiene HDRISky, añadirlo
        var hdriSky = vol.profile.Add<UnityEngine.Rendering.HighDefinition.HDRISky>(true);
        hdriSky.hdriSky.value = (Cubemap)hdriTextura;
        EditorUtility.SetDirty(vol.profile);
        Debug.Log($"[TexturasAAA] 🌄 HDRISky creado con '{hdriTextura.name}'.");
        return 1;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  HELPERS
    // ─────────────────────────────────────────────────────────────────────────

    static Material CrearMatPBR(Shader shader, string texId, string subfolder,
                                 float tiling = 4f)
    {
        var albedo = BuscarTextura($"{texId}_Albedo", subfolder);
        if (albedo == null) return null;  // sin textura, no crear material vacío

        var mat = new Material(shader) { name = $"Mat_{texId}_PBR" };

        // Albedo / BaseColor
        if (mat.HasProperty("_BaseColorMap")) mat.SetTexture("_BaseColorMap", albedo);
        else mat.mainTexture = albedo;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);

        // Tiling
        if (mat.HasProperty("_BaseColorMap"))
            mat.SetTextureScale("_BaseColorMap", new Vector2(tiling, tiling));
        else mat.mainTextureScale = new Vector2(tiling, tiling);

        // Normal map
        var normal = BuscarTextura($"{texId}_Normal", subfolder);
        if (normal != null && mat.HasProperty("_NormalMap"))
        {
            mat.SetTexture("_NormalMap", normal);
            mat.SetFloat("_NormalScale", 1.0f);
            if (mat.HasProperty("_NormalMap"))
                mat.SetTextureScale("_NormalMap", new Vector2(tiling, tiling));
        }

        // ARM map (AO+Roughness+Metallic) = MaskMap en HDRP
        var arm = BuscarTextura($"{texId}_ARM", subfolder);
        if (arm != null && mat.HasProperty("_MaskMap"))
        {
            mat.SetTexture("_MaskMap", arm);
            mat.SetTextureScale("_MaskMap", new Vector2(tiling, tiling));
        }

        // Roughness
        if (mat.HasProperty("_Smoothness"))
            mat.SetFloat("_Smoothness", arm != null ? 0.5f : 0.15f);
        if (mat.HasProperty("_Metallic"))
            mat.SetFloat("_Metallic", 0f);

        return mat;
    }

    static Texture2D BuscarTextura(string nombre, string subfolder)
    {
        // Buscar en subcarpeta específica y en la raíz
        string[] rutas = {
            $"{TEX_DIR}/{subfolder}/{nombre}.png",
            $"{TEX_DIR}/{nombre}.png",
        };
        foreach (var ruta in rutas)
        {
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(ruta);
            if (t != null) return t;
        }
        return null;
    }

    // ── Conversión de materiales Standard → HDRP/Lit ─────────────────────────
    // Movido desde IntegradorAssetsNuevos (eliminado por redundancia).

    [MenuItem("Tools/Alsasua/Render/🎨 Convertir Materiales HDRP", priority = 11)]
    public static int ConvertirMaterialesAHDRP()
    {
        EditorUtility.DisplayProgressBar("Convirtiendo materiales", "Buscando materiales Standard...", 0f);

        string[] carpetas = {
            "Assets/Police Car & Helicopter",
            "Assets/Hot Rod",
            "Assets/BarrierPack",
            "Assets/SpaceZeta_StreetLamps2",
            "Assets/LowPolySoldiers_demo",
        };

        var shaderHDRP = Shader.Find("HDRP/Lit");
        if (shaderHDRP == null)
        {
            Debug.LogError("[AplicadorTexturasAAA] Shader 'HDRP/Lit' no encontrado. ¿Está HDRP instalado?");
            EditorUtility.ClearProgressBar();
            return 0;
        }

        var guids = AssetDatabase.FindAssets("t:Material", carpetas);
        int convertidos = 0;

        for (int i = 0; i < guids.Length; i++)
        {
            EditorUtility.DisplayProgressBar("Convirtiendo materiales",
                $"Material {i + 1}/{guids.Length}...", (float)i / guids.Length);

            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) continue;

            string shaderName = mat.shader?.name ?? "";
            bool esStandard = shaderName.Contains("Standard")
                           || shaderName.Contains("Legacy")
                           || shaderName == "0000000000000000f000000000000000";
            if (!esStandard && mat.shader != shaderHDRP) continue;

            Color  colorAntes  = mat.HasProperty("_Color")              ? mat.GetColor("_Color")                : Color.white;
            Texture texAlbedo  = mat.HasProperty("_MainTex")            ? mat.GetTexture("_MainTex")            : null;
            Texture texNormal  = mat.HasProperty("_BumpMap")            ? mat.GetTexture("_BumpMap")            : null;
            Texture texMetal   = mat.HasProperty("_MetallicGlossMap")   ? mat.GetTexture("_MetallicGlossMap")   : null;
            float metallic     = mat.HasProperty("_Metallic")           ? mat.GetFloat("_Metallic")             : 0f;
            float smoothness   = mat.HasProperty("_Glossiness")         ? mat.GetFloat("_Glossiness")           : 0.5f;

            Undo.RecordObject(mat, $"Convert {mat.name} to HDRP");
            mat.shader = shaderHDRP;
            if (texAlbedo != null) mat.SetTexture("_BaseColorMap", texAlbedo);
            if (texNormal != null) mat.SetTexture("_NormalMap",    texNormal);
            if (texMetal  != null) mat.SetTexture("_MaskMap",      texMetal);
            mat.SetColor("_BaseColor",   colorAntes);
            mat.SetFloat("_Metallic",    metallic);
            mat.SetFloat("_Smoothness",  smoothness);
            EditorUtility.SetDirty(mat);
            convertidos++;
        }

        EditorUtility.ClearProgressBar();
        AssetDatabase.SaveAssets();
        Debug.Log($"[AplicadorTexturasAAA] 🎨 {convertidos}/{guids.Length} materiales convertidos a HDRP/Lit.");
        return convertidos;
    }
}
