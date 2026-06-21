// Assets/Scripts/Editor/AplicadorOrtoDecalEditor.cs
// ═══════════════════════════════════════════════════════════════════════════
//  ORTOFOTO COMO HDRP DECAL — la foto aérea PNOA proyectada sobre el terreno
//
//  Solución al "verde raro" y a "quiero que se vea igual que Cesium":
//    Un DecalProjector HDRP cuelga sobre el mundo y proyecta la ortofoto
//    hacia abajo sobre los 48 tiles del mosaico V2. El decal usa el pase
//    GBuffer de HDRP → conforma PERFECTAMENTE al relieve, cero Z-fighting,
//    1-2 draw calls para 14.4 km. No hace falta tocar los TerrainData.
//
//  PIRÁMIDE LOD (de lejos a cerca):
//    1) FONDO 14.4km  — ortofoto_fondo.jpg (3.52m/px)  — este script
//    2) VALLE 2.75km  — ortofoto_drape.png (1.34m/px)  — este script
//    3) NEAR ≤400m   — 72 teselas 25cm/px              — AplicadorOrtofoto (runtime)
//
//  Prerrequisito: los dos archivos JPG/PNG ya existen (Tools/Descargar*.py ya
//  corrió) y los decales HDRP están habilitados en el HDRP Asset.
//
//  Menú: Tools → Alsasua → Mundo → 📸 Ortofoto como Decal [APLICAR / QUITAR]
// ═══════════════════════════════════════════════════════════════════════════

#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

public static class AplicadorOrtoDecalEditor
{
    // ── Rutas de assets ──────────────────────────────────────────────────────
    const string TEX_FONDO  = "Assets/AlsasuaData/ortofoto_fondo.jpg";
    const string TEX_VALLE  = "Assets/AlsasuaData/ortofoto_drape.png";
    const string META_FONDO = "Assets/AlsasuaData/ortofoto_fondo_meta.json";
    const string MAT_FONDO  = "Assets/Materials/Ortofoto/OrtoFondo_Decal.mat";
    const string MAT_VALLE  = "Assets/Materials/Ortofoto/OrtoValle_Decal.mat";
    const string ROOT_NAME  = "DecalProjectors_Orto";

    // Valle bbox (Unity coords) — ortofoto_drape.png, cobertura orto_tiles_meta.json
    const float VALLE_X0 = 596.3f,  VALLE_Z0 = 7378.9f;
    const float VALLE_X1 = 3346.7f, VALLE_Z1 = 10050.6f;

    // Y del proyector: muy por encima del pico más alto (Maiza 1182m real ≈ 670m Unity)
    // El box cubre [PROJ_Y - PROJ_DEPTH, PROJ_Y] = [-1000, 4000] ← todo el terreno dentro
    const float PROJ_Y     = 4000f;
    const float PROJ_DEPTH = 5000f;

    [System.Serializable]
    class BBoxMeta { public float ux_min, uz_min, ux_max, uz_max; }

    // ══════════════════════════════════════════════════════════════════════════
    //  APLICAR
    // ══════════════════════════════════════════════════════════════════════════
    [MenuItem("Tools/Alsasua/Terreno/📸 Ortofoto como Decal [APLICAR]", priority = 30)]
    public static void Aplicar()
    {
        // 1) Importar texturas (max size correcto, sRGB, mips, aniso)
        ImportarTextura(TEX_FONDO, 4096);
        ImportarTextura(TEX_VALLE, 2048);

        // 2) Leer bbox del fondo desde sidecar JSON
        float fx0 = -4827f, fz0 = 1370f, fx1 = 8664f, fz1 = 15770f;
        string metaFull = Path.GetFullPath(META_FONDO);
        if (File.Exists(metaFull))
        {
            try
            {
                var m = JsonUtility.FromJson<BBoxMeta>(File.ReadAllText(metaFull));
                fx0 = m.ux_min; fz0 = m.uz_min; fx1 = m.ux_max; fz1 = m.uz_max;
                Debug.Log($"[OrtoDecal] Meta fondo: ux=[{fx0:F0},{fx1:F0}] uz=[{fz0:F0},{fz1:F0}]");
            }
            catch (System.Exception e) { Debug.LogWarning($"[OrtoDecal] meta parse: {e.Message}"); }
        }
        else Debug.LogWarning($"[OrtoDecal] {META_FONDO} no encontrado — usando bbox por defecto.");

        // 3) Crear directorio de materiales y los materiales decal
        EnsureDir("Assets/Materials/Ortofoto");

        var matFondo = CrearOCargarMaterial(MAT_FONDO, TEX_FONDO);
        var matValle = CrearOCargarMaterial(MAT_VALLE, TEX_VALLE);
        if (matFondo == null)
        {
            Debug.LogError("[OrtoDecal] No se pudo crear el material fondo. ¿Está HDRP instalado?");
            EditorUtility.DisplayDialog("Error", "No se encontró el shader HDRP/Decal.\n\nVerifica que HDRP está correctamente instalado.", "OK");
            return;
        }

        // 4) Recrear los GameObjects en la escena activa
        var raiz = CrearRaiz();

        // Fondo: todo el mundo jugable (14.4 km × 14.4 km)
        float fAncho   = fx1 - fx0;   // extensión en World X
        float fProfund = fz1 - fz0;   // extensión en World Z
        CrearProyector("Orto_Fondo_Decal", raiz, matFondo,
            (fx0 + fx1) * 0.5f, (fz0 + fz1) * 0.5f,
            fAncho, fProfund, drawDist: 25000f);

        // Valle: casco + sierra próxima (2.75 km, 1.34m/px)
        if (matValle != null)
        {
            float vAncho   = VALLE_X1 - VALLE_X0;
            float vProfund = VALLE_Z1 - VALLE_Z0;
            CrearProyector("Orto_Valle_Decal", raiz, matValle,
                (VALLE_X0 + VALLE_X1) * 0.5f, (VALLE_Z0 + VALLE_Z1) * 0.5f,
                vAncho, vProfund, drawDist: 8000f);
        }
        else Debug.LogWarning("[OrtoDecal] ortofoto_drape.png no encontrada — solo fondo 14.4km.");

        // 5) Aumentar drawDistance global de decales en todos los HDRP assets
        //    (por defecto es 1000m → cortaría el fondo a 1km; necesitamos 20km+)
        ActualizarHDRPDecalDrawDistance(20000f);

        // 6) Marcar escena como modificada
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();

        string msg = $"Decales aplicados:\n" +
                     $"• Fondo 14.4km (3.5 m/px) — ortofoto_fondo.jpg\n" +
                     (matValle != null ? $"• Valle 2.75km (1.3 m/px) — ortofoto_drape.png\n" : "") +
                     $"\nEl terreno mostrará la foto aérea real PNOA.\n" +
                     $"Guarda la escena (Ctrl+S) para persistir.";
        Debug.Log($"[OrtoDecal] ✅ {msg}");
        EditorUtility.DisplayDialog("📸 Ortofoto como Decal", msg, "OK");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  QUITAR
    // ══════════════════════════════════════════════════════════════════════════
    [MenuItem("Tools/Alsasua/Terreno/📸 Ortofoto como Decal [QUITAR]", priority = 31)]
    static void Quitar()
    {
        var go = GameObject.Find(ROOT_NAME);
        if (go == null)
        {
            EditorUtility.DisplayDialog("Ortofoto Decal", "No hay proyectores de ortofoto en la escena.", "OK");
            return;
        }
        Object.DestroyImmediate(go);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[OrtoDecal] Decales de ortofoto eliminados. El terreno vuelve a mostrar el splatmap.");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  HELPERS
    // ══════════════════════════════════════════════════════════════════════════

    static void ImportarTextura(string assetPath, int maxSize)
    {
        if (!File.Exists(Path.GetFullPath(assetPath)))
        {
            Debug.LogWarning($"[OrtoDecal] {assetPath} no existe — omitida.");
            return;
        }
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

        var imp = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (imp == null) return;

        bool cambio = false;
        if (imp.maxTextureSize    != maxSize)                                 { imp.maxTextureSize    = maxSize;                                  cambio = true; }
        if (!imp.sRGBTexture)                                                 { imp.sRGBTexture        = true;                                    cambio = true; }
        if (!imp.mipmapEnabled)                                               { imp.mipmapEnabled      = true;                                    cambio = true; }
        if (imp.anisoLevel        < 8)                                        { imp.anisoLevel         = 8;                                       cambio = true; }
        if (imp.filterMode        != FilterMode.Trilinear)                    { imp.filterMode         = FilterMode.Trilinear;                    cambio = true; }
        if (imp.wrapMode          != TextureWrapMode.Clamp)                   { imp.wrapMode           = TextureWrapMode.Clamp;                   cambio = true; }
        if (imp.textureCompression != TextureImporterCompression.CompressedHQ) { imp.textureCompression = TextureImporterCompression.CompressedHQ; cambio = true; }

        if (cambio) imp.SaveAndReimport();
        Debug.Log($"[OrtoDecal] Textura importada: {assetPath} ({maxSize}px)");
    }

    static Material CrearOCargarMaterial(string matPath, string texPath)
    {
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        if (tex == null)
        {
            Debug.LogWarning($"[OrtoDecal] Textura no cargada: {texPath} — material omitido.");
            return null;
        }

        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            var sh = Shader.Find("HDRP/Decal");
            if (sh == null) return null;
            mat = new Material(sh) { name = Path.GetFileNameWithoutExtension(matPath) };
            AssetDatabase.CreateAsset(mat, matPath);
        }

        // Afectar solo color (albedo); sin normal ni máscara
        if (mat.HasProperty("_BaseColorMap"))     mat.SetTexture("_BaseColorMap",     tex);
        if (mat.HasProperty("_BaseColor"))        mat.SetColor("_BaseColor",           Color.white);
        if (mat.HasProperty("_BaseColorOpacity")) mat.SetFloat("_BaseColorOpacity",    1f);
        if (mat.HasProperty("_NormalOpacity"))    mat.SetFloat("_NormalOpacity",       0f);
        if (mat.HasProperty("_MaskOpacity"))      mat.SetFloat("_MaskOpacity",         0f);
        if (mat.HasProperty("_DecalBlend"))       mat.SetFloat("_DecalBlend",          1f);

        // Propiedades [Toggle] del shader HDRP/Decal que activan los keywords de variante
        if (mat.HasProperty("_AffectColor"))      mat.SetFloat("_AffectColor",      1f);
        if (mat.HasProperty("_AffectNormal"))     mat.SetFloat("_AffectNormal",     0f);
        if (mat.HasProperty("_AffectMetal"))      mat.SetFloat("_AffectMetal",      0f);
        if (mat.HasProperty("_AffectSmoothness")) mat.SetFloat("_AffectSmoothness", 0f);
        if (mat.HasProperty("_AffectEmissive"))   mat.SetFloat("_AffectEmissive",   0f);
        // Keywords explícitos como respaldo (HDRP 17.x / Unity 6)
        mat.EnableKeyword("_MATERIAL_AFFECTS_ALBEDO");
        mat.DisableKeyword("_MATERIAL_AFFECTS_NORMAL");
        mat.DisableKeyword("_MATERIAL_AFFECTS_MASKMAP");
        mat.DisableKeyword("_MATERIAL_AFFECTS_EMISSIVE");

        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        return mat;
    }

    static GameObject CrearRaiz()
    {
        // Eliminar instancias previas para idempotencia
        var viejo = GameObject.Find(ROOT_NAME);
        if (viejo != null) Object.DestroyImmediate(viejo);
        return new GameObject(ROOT_NAME);
    }

    // Crea un DecalProjector que proyecta HACIA ABAJO (Euler(90,0,0)):
    //   Local X = World X (Este)
    //   Local Y = World +Z (Norte) — size.y = extensión en Z mundial
    //   Local Z → World -Y (abajo) — size.z = profundidad de proyección
    //
    // UV mapping: sin flip porque Unity invierte el JPG al cargar
    //   (image top = Norte → V=1 en Unity; local Y=+size.y/2 = Norte → UV.y=1) ✓
    static void CrearProyector(string nombre, GameObject padre, Material mat,
        float cx, float cz, float anchoX, float profundoZ, float drawDist)
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(padre.transform, false);
        go.transform.SetPositionAndRotation(
            new Vector3(cx, PROJ_Y, cz),
            Quaternion.Euler(90f, 0f, 0f));

        var dp = go.AddComponent<DecalProjector>();
        dp.material     = mat;
        dp.size         = new Vector3(anchoX, profundoZ, PROJ_DEPTH);
        // pivot.z = -0.5 pone la cara FRONTAL del box EN transform.position (Y=4000).
        // El box proyecta size.z=5000m hacia abajo → cubre el terreno en World Y [4000, -1000].
        // Con pivot=(0,0,0) el terreno (Y=0-900) quedaría FUERA del box centrado en Y=4000.
        dp.pivot        = new Vector3(0f, 0f, -0.5f);
        dp.drawDistance = drawDist;
        dp.fadeScale    = 0.95f;
        dp.fadeFactor   = 1f;
        dp.uvScale      = Vector2.one;
        dp.uvBias       = Vector2.zero;

        // Todas las capas de render (mismo cast que SistemaDecalesHDRP)
        dp.decalLayerMask = (UnityEngine.Rendering.HighDefinition.RenderingLayerMask)0xFF;

        Debug.Log($"[OrtoDecal] {nombre}: cx={cx:F0} cz={cz:F0}  {anchoX:F0}×{profundoZ:F0}m  drawDist={drawDist:F0}");
    }

    static void ActualizarHDRPDecalDrawDistance(float dist)
    {
        // Paths conocidos de los HDRP assets en este proyecto
        string[] paths = {
            "Assets/Settings/HDRP Balanced.asset",
            "Assets/Settings/HDRP Performant.asset",
            "Assets/Settings/HDRP High Fidelity.asset",
        };

        int actualizados = 0;
        foreach (string p in paths)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(p);
            if (asset == null) continue;

            var so = new SerializedObject(asset);
            // Ruta del campo dentro del HDRP asset YAML
            var prop = so.FindProperty("m_RenderPipelineSettings.decalSettings.drawDistance");
            if (prop == null) prop = so.FindProperty("decalSettings.drawDistance");
            if (prop == null) { Debug.LogWarning($"[OrtoDecal] No se encontró decalSettings.drawDistance en {p}"); continue; }

            if (prop.floatValue < dist)
            {
                prop.floatValue = dist;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
                actualizados++;
                Debug.Log($"[OrtoDecal] {p}: decalSettings.drawDistance → {dist}m");
            }
        }

        if (actualizados > 0)
        {
            AssetDatabase.SaveAssets();
            Debug.Log($"[OrtoDecal] ✅ drawDistance de decales actualizado a {dist}m en {actualizados} HDRP assets.");
        }
        else
            Debug.Log($"[OrtoDecal] drawDistance de decales ya es ≥ {dist}m (o no encontrado).");
    }

    static void EnsureDir(string assetPath)
    {
        string full = Path.GetFullPath(assetPath);
        if (!Directory.Exists(full))
        {
            Directory.CreateDirectory(full);
            AssetDatabase.Refresh();
        }
    }
}
#endif
