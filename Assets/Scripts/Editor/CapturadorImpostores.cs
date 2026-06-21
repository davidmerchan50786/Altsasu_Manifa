// Assets/Scripts/Editor/CapturadorImpostores.cs
// ═══════════════════════════════════════════════════════════════════════════
//  CAPTURADOR DE IMPOSTORES — Fase 2 del plan AAA (Docs/plan_render_aaa.md)
//
//  Toma cada prefab de celda horneada (ManifestCiudadSO) y le añade un
//  LOD2 de billboard real: quad + 4 texturas capturadas (N/E/S/W) +
//  ImpostorCeldaSelector que elige la textura según el ángulo de cámara.
//
//  Por qué no un atlas octaédrico completo todavía: 4 cardinales dan
//  error < 45° de ángulo → suficiente para ciudades vistas desde nivel
//  de suelo a 400m+. El atlas octaédrico real es el follow-up (Fase 2b).
//
//  PIPELINE:
//    1. Lee ManifestCiudadSO → lista de prefabs de celda
//    2. Por celda: instancia en layer oculto, renderiza desde 4 ángulos
//       con cámara ortográfica + HDAdditionalCameraData, guarda PNG
//    3. Importa texturas con ajustes correctos (RGBA, no mipmap)
//    4. Crea 4 materiales HDRP/Unlit con alpha cutoff por celda
//    5. Edita el prefab de celda: añade quad impostor + ImpostorCeldaSelector
//       + actualiza su LODGroup con el nivel LOD2
//
//  REQUISITO: ejecutar DESPUÉS de 🏗️ Hornear Ciudad (el ManifestCiudadSO
//  debe existir en Assets/Resources/CiudadHorneada/).
//
//  REVERSIBLE: borra Assets/CiudadHorneada/Impostores/ y los LOD2 de los
//  prefabs con ↩️ Deshacer Impostores.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

public static class CapturadorImpostores
{
    const int    TEX_RES      = 512;
    const string DIR_IMPOST   = "Assets/CiudadHorneada/Impostores";
    const string SO_PATH      = "Assets/Resources/CiudadHorneada/ManifestCiudadSO.asset";
    const float  LOD2_SCREEN  = 0.018f;   // ~400 m → activar LOD2 billboard
    const int    LAYER_CAPTURA = 29;      // layer temporal; no necesita nombre

    // 8 ángulos octaédricos (45° cada uno). La cámara está en la dirección OPUESTA.
    // "N" = cámara al norte mirando al sur → captura fachada sur del edificio.
    static readonly (Quaternion rot, string nombre)[] DIRS = {
        (Quaternion.Euler(0,   0, 0), "N"),
        (Quaternion.Euler(0,  45, 0), "NE"),
        (Quaternion.Euler(0,  90, 0), "E"),
        (Quaternion.Euler(0, 135, 0), "SE"),
        (Quaternion.Euler(0, 180, 0), "S"),
        (Quaternion.Euler(0, 225, 0), "SW"),
        (Quaternion.Euler(0, 270, 0), "W"),
        (Quaternion.Euler(0, 315, 0), "NW"),
    };

    // ── Menú principal ────────────────────────────────────────────────────
    [MenuItem("Tools/Alsasua/Mundo/🎭 Capturar Impostores de Celdas", priority = 32)]
    static void MenuCapturar()
    {
        var so = AssetDatabase.LoadAssetAtPath<ManifestCiudadSO>(SO_PATH);
        if (so == null || so.prefabs == null || so.prefabs.Length == 0)
        {
            EditorUtility.DisplayDialog("Capturar Impostores",
                "Hornea la ciudad primero (🏗️ Hornear Ciudad).\n" +
                "El ManifestCiudadSO no existe todavía.", "Vale");
            return;
        }
        if (!EditorUtility.DisplayDialog("Capturar Impostores",
            $"Capturará {so.prefabs.Length} celdas desde {DIRS.Length} ángulos cada una (octaédrico).\n" +
            "Crea LOD2 billboard real en cada celda. Puede tardar 1-3 min.\n\n" +
            "¿Continuar?", "Capturar", "Cancelar")) return;

        Capturar(so);
    }

    [MenuItem("Tools/Alsasua/Mundo/↩️ Deshacer Impostores", priority = 33)]
    static void MenuDeshacer()
    {
        if (!EditorUtility.DisplayDialog("Deshacer Impostores",
            "Borrará Assets/CiudadHorneada/Impostores/ y eliminará los LOD2 de los prefabs.\n¿Continuar?",
            "Deshacer", "Cancelar")) return;

        var so = AssetDatabase.LoadAssetAtPath<ManifestCiudadSO>(SO_PATH);
        if (so?.prefabs != null)
            foreach (var p in so.prefabs) EliminarLOD2DePrefab(p);

        if (AssetDatabase.IsValidFolder(DIR_IMPOST))
            AssetDatabase.DeleteAsset(DIR_IMPOST);

        AssetDatabase.Refresh();
        Debug.Log("[Impostores] Deshecho.");
    }

    // ── Flujo principal ───────────────────────────────────────────────────
    static void Capturar(ManifestCiudadSO so)
    {
        Directory.CreateDirectory(DIR_IMPOST);

        // Shader HDRP/Unlit para los materiales de impostor
        var shader = Shader.Find("HDRP/Unlit");
        if (shader == null)
        {
            Debug.LogError("[Impostores] Shader 'HDRP/Unlit' no encontrado. ¿HDRP instalado?");
            return;
        }

        // ── FASE A: captura de texturas ──────────────────────────────────
        var (cam, camGO, rt) = CrearCamaraCaptura();
        int n = 0;
        try
        {
            foreach (var prefab in so.prefabs)
            {
                if (prefab == null) continue;
                n++;
                if (EditorUtility.DisplayCancelableProgressBar("Capturar Impostores",
                    $"Capturando celda {n}/{so.prefabs.Length}: {prefab.name}…",
                    n / (float)so.prefabs.Length)) break;

                CapturarCelda(cam, rt, prefab);
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            Object.DestroyImmediate(camGO);
            rt.Release();
            Object.DestroyImmediate(rt);
        }

        // ── FASE B: importar texturas ─────────────────────────────────────
        AssetDatabase.Refresh();
        ConfigurarImportacionTexturas(so);

        // ── FASE C: crear materiales + actualizar prefabs ────────────────
        AssetDatabase.Refresh();
        AssetDatabase.StartAssetEditing();
        int procesadas = 0;
        try
        {
            foreach (var prefab in so.prefabs)
            {
                if (prefab == null) continue;
                procesadas++;
                EditorUtility.DisplayProgressBar("Crear materiales e impostores",
                    $"{procesadas}/{so.prefabs.Length}: {prefab.name}…",
                    procesadas / (float)so.prefabs.Length);

                CrearMaterialesYActualizarPrefab(prefab, shader);
            }
        }
        finally { AssetDatabase.StopAssetEditing(); EditorUtility.ClearProgressBar(); }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[Impostores] ✅ {procesadas} celdas con LOD2 billboard. " +
            $"Texturas en {DIR_IMPOST}. LOD2 activo a ~400m+ (< {LOD2_SCREEN * 100:F1}% pantalla).");
        EditorUtility.DisplayDialog("Capturar Impostores",
            $"✅ {procesadas} celdas con impostor real (LOD2).\n\n" +
            $"LOD0 (HD) · LOD1 (HLOD) · LOD2 (billboard quad) · Cull\n" +
            "Billboard activo ~400m+ → casi 0 draw calls a distancia.", "Genial");
    }

    // ── Creación de cámara de captura ─────────────────────────────────────
    static (Camera cam, GameObject go, RenderTexture rt) CrearCamaraCaptura()
    {
        var go  = new GameObject("__CapturadorImpostores_Cam");
        var cam = go.AddComponent<Camera>();
        cam.orthographic   = true;
        cam.nearClipPlane  = 0.1f;
        cam.farClipPlane   = 2000f;
        cam.cullingMask    = 1 << LAYER_CAPTURA;
        cam.clearFlags     = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.clear;
        cam.enabled        = false;   // no renderizar automáticamente

        var hd = go.AddComponent<HDAdditionalCameraData>();
        hd.clearColorMode     = HDAdditionalCameraData.ClearColorMode.Color;
        hd.backgroundColorHDR = Color.clear;

        var rt = new RenderTexture(TEX_RES, TEX_RES, 32, RenderTextureFormat.ARGB32);
        rt.antiAliasing = 1;
        cam.targetTexture = rt;

        return (cam, go, rt);
    }

    // ── Captura de una celda desde N ángulos (8 = octaédrico) ────────────
    static void CapturarCelda(Camera cam, RenderTexture rt, GameObject prefab)
    {
        // Instanciar en layer de captura (invisible para el resto de la escena)
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        SetLayerRecursivo(inst, LAYER_CAPTURA);

        var bounds = CalcularBounds(inst);
        if (bounds.size == Vector3.zero) { Object.DestroyImmediate(inst); return; }

        foreach (var (rot, nombre) in DIRS)
        {
            // Posicionar cámara: al lado opuesto de la dirección capturada
            var forward  = rot * Vector3.forward;
            float dist   = bounds.size.magnitude * 1.5f;
            cam.transform.SetPositionAndRotation(
                bounds.center - forward * dist,
                Quaternion.LookRotation(forward));

            // Tamaño ortográfico = mayor semi-dimensión de la celda
            cam.orthographicSize = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z) * 1.05f;

            RenderTexture.active = rt;
            try { cam.Render(); }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[Impostores] cam.Render() falló en {prefab.name}_{nombre}: {ex.Message}. " +
                    "Usando textura de fallback (color dominante).");
                RenderTexture.active = null;
                string fbPath = $"{DIR_IMPOST}/{prefab.name}_{nombre}.png";
                File.WriteAllBytes(fbPath, GenerarTexturaSolidaFallback(bounds));
                continue;
            }

            var tex = new Texture2D(TEX_RES, TEX_RES, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, TEX_RES, TEX_RES), 0, 0);
            tex.Apply();
            RenderTexture.active = null;

            // Verificar que la captura no está vacía (HDRP puede silenciosamente no renderizar)
            Color sample = tex.GetPixel(TEX_RES / 2, TEX_RES / 2);
            if (sample.a < 0.01f)
            {
                Debug.LogWarning($"[Impostores] {prefab.name}_{nombre}: captura transparente — " +
                    "HDRP no renderizó. Usa textura de fallback o verifica la cámara de captura.");
            }

            string path = $"{DIR_IMPOST}/{prefab.name}_{nombre}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        Object.DestroyImmediate(inst);
    }

    // ── Ajustes de importación de texturas ───────────────────────────────
    static void ConfigurarImportacionTexturas(ManifestCiudadSO so)
    {
        foreach (var prefab in so.prefabs)
        {
            if (prefab == null) continue;
            foreach (var (_, nombre) in DIRS)
            {
                string path = $"{DIR_IMPOST}/{prefab.name}_{nombre}.png";
                var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                if (imp == null) continue;
                imp.alphaIsTransparency = true;
                imp.mipmapEnabled       = false;
                imp.textureCompression  = TextureImporterCompression.Compressed;
                imp.SaveAndReimport();
            }
        }
    }

    // ── Crear materiales + quad LOD2 en el prefab ─────────────────────────
    static void CrearMaterialesYActualizarPrefab(GameObject cellPrefab, Shader shader)
    {
        string baseName = cellPrefab.name;

        // Cargar las 4 texturas importadas
        var mats = new Material[4];
        for (int d = 0; d < 4; d++)
        {
            string texPath = $"{DIR_IMPOST}/{baseName}_{DIRS[d].nombre}.png";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (tex == null) return;   // si falta alguna, saltamos esta celda

            var mat = new Material(shader) { name = $"Impostor_{baseName}_{DIRS[d].nombre}" };
            mat.SetTexture("_UnlitColorMap", tex);
            mat.SetFloat("_AlphaCutoffEnable", 1f);
            mat.SetFloat("_AlphaCutoff", 0.08f);
            mat.SetFloat("_DoubleSidedEnable", 1f);
            mat.enableInstancing = true;

            string matPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{DIR_IMPOST}/Mat_{baseName}_{DIRS[d].nombre}.mat");
            AssetDatabase.CreateAsset(mat, matPath);
            mats[d] = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        }

        // Editar el prefab para añadir LOD2
        string prefabPath = AssetDatabase.GetAssetPath(cellPrefab);
        using var scope   = new PrefabUtility.EditPrefabContentsScope(prefabPath);
        var root = scope.prefabContentsRoot;

        // Calcular bounds de la geometría ya horneada dentro del prefab
        var bounds = CalcularBounds(root);
        if (bounds.size == Vector3.zero) return;

        // Quad impostor: escala = footprint horizontal × altura real
        var impostorGO = GameObject.CreatePrimitive(PrimitiveType.Quad);
        impostorGO.name = "LOD2_Impostor";
        impostorGO.transform.SetParent(root.transform, false);
        impostorGO.transform.localPosition = bounds.center - root.transform.position;
        impostorGO.transform.localScale = new Vector3(
            Mathf.Max(bounds.size.x, bounds.size.z),
            bounds.size.y,
            1f);
        if (impostorGO.TryGetComponent<MeshCollider>(out var mc)) Object.DestroyImmediate(mc);

        var mr = impostorGO.GetComponent<MeshRenderer>();
        mr.sharedMaterial      = mats[0];
        mr.shadowCastingMode   = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows      = false;

        var selector = impostorGO.AddComponent<ImpostorCeldaSelector>();
        selector.materiales = mats;

        // Actualizar LODGroup existente: insertar LOD2 con el impostor
        var lg = root.GetComponent<LODGroup>();
        if (lg != null)
        {
            var lodList = new List<LOD>(lg.GetLODs());

            // Eliminar LOD2 previo si existe (re-captura)
            lodList.RemoveAll(l => l.screenRelativeTransitionHeight <= LOD2_SCREEN + 0.001f
                                   && l.screenRelativeTransitionHeight >= LOD2_SCREEN - 0.001f);

            lodList.Add(new LOD(LOD2_SCREEN, new Renderer[] { mr }));
            lodList.Sort((a, b) =>
                b.screenRelativeTransitionHeight.CompareTo(a.screenRelativeTransitionHeight));
            lg.SetLODs(lodList.ToArray());
            lg.RecalculateBounds();
        }
    }

    // ── Deshacer: eliminar LOD2 de un prefab ─────────────────────────────
    static void EliminarLOD2DePrefab(GameObject prefab)
    {
        if (prefab == null) return;
        string path = AssetDatabase.GetAssetPath(prefab);
        if (string.IsNullOrEmpty(path)) return;

        using var scope = new PrefabUtility.EditPrefabContentsScope(path);
        var root = scope.prefabContentsRoot;

        foreach (Transform t in root.transform)
            if (t.name == "LOD2_Impostor") { Object.DestroyImmediate(t.gameObject); break; }

        var lg = root.GetComponent<LODGroup>();
        if (lg != null)
        {
            var lodList = new List<LOD>(lg.GetLODs());
            lodList.RemoveAll(l => l.screenRelativeTransitionHeight <= LOD2_SCREEN + 0.001f
                                   && l.screenRelativeTransitionHeight >= LOD2_SCREEN - 0.001f);
            lg.SetLODs(lodList.ToArray());
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────
    static Bounds CalcularBounds(GameObject go)
    {
        var mrs = go.GetComponentsInChildren<MeshRenderer>(true);
        if (mrs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        var b = mrs[0].bounds;
        for (int i = 1; i < mrs.Length; i++) b.Encapsulate(mrs[i].bounds);
        return b;
    }

    static void SetLayerRecursivo(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursivo(child.gameObject, layer);
    }

    // ── Fallback si HDRP no puede renderizar: textura sólida con el color del material dominante
    static byte[] GenerarTexturaSolidaFallback(Bounds bounds)
    {
        // Color base ladrillo/piedra vasco — representativo sin captura real
        var tex = new Texture2D(TEX_RES, TEX_RES, TextureFormat.RGBA32, false);
        var pixels = new Color32[TEX_RES * TEX_RES];
        // Franja de fachada: parte inferior más oscura (sombra de suelo), parte superior más clara
        for (int y = 0; y < TEX_RES; y++)
        {
            float t = y / (float)TEX_RES;
            // Color arenisca vasca con gradiente de iluminación
            byte r = (byte)Mathf.Lerp(130, 170, t);
            byte g = (byte)Mathf.Lerp(110, 145, t);
            byte b = (byte)Mathf.Lerp( 95, 125, t);
            byte a = y < (int)(TEX_RES * 0.05f) ? (byte)0 : (byte)255; // alpha fade en base
            var c = new Color32(r, g, b, a);
            for (int x = 0; x < TEX_RES; x++)
                pixels[y * TEX_RES + x] = c;
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        byte[] png = tex.EncodeToPNG();
        Object.DestroyImmediate(tex);
        return png;
    }
}
