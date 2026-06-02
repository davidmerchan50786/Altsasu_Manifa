using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Carga las texturas PBR generadas desde fotos reales de Alsasua y las aplica
/// a los renderers de edificios en runtime.
///
/// Lee Assets/AlsasuaData/FacadeTextures/aaa_textures_report.json y por cada
/// edificio con textura disponible sustituye el material de fachada por uno
/// HDRP/Lit con Albedo+Normal+AO reales (4K).
///
/// Orden de ejecución: después de SistemaEdificiosAAA (-50) y FusionadorEdificiosUltra (-92).
/// </summary>
[DefaultExecutionOrder(-45)]
public class AplicadorTexturasReales : MonoBehaviour
{
    [Header("Configuración")]
    [SerializeField] Shader _shaderHDRP = null;           // HDRP/Lit — asignar en Inspector
    [SerializeField] float  _normalStrength   = 0.6f;
    [SerializeField] float  _roughness        = 0.65f;
    [SerializeField] bool   _applyOnlyHighConf = false;   // Si true, solo fotos high/medium

    [Header("Rutas (relativas al proyecto)")]
    [SerializeField] string _reportPath  = "Assets/AlsasuaData/FacadeTextures/aaa_textures_report.json";
    [SerializeField] string _aaaDir      = "Assets/AlsasuaData/FacadeTextures/AAA";

    // Shader property IDs
    static readonly int ID_BaseColor      = Shader.PropertyToID("_BaseColorMap");
    static readonly int ID_NormalMap      = Shader.PropertyToID("_NormalMap");
    static readonly int ID_NormalScale    = Shader.PropertyToID("_NormalScale");
    static readonly int ID_AOMap         = Shader.PropertyToID("_MaskMap");
    static readonly int ID_Smoothness     = Shader.PropertyToID("_Smoothness");
    static readonly int ID_Metallic       = Shader.PropertyToID("_Metallic");

    // Cache de materiales creados para poder destruirlos al salir de escena
    readonly List<Material> _createdMaterials = new List<Material>();

    struct TextureEntry
    {
        public string edificioId;
        public string albedoPath;
        public string normalPath;
        public string aoPath;
        public bool   isHighConf;
    }

    void Start()
    {
        StartCoroutine(CargarYAplicar());
    }

    IEnumerator CargarYAplicar()
    {
        string fullReport = Path.GetFullPath(_reportPath);
        if (!File.Exists(fullReport))
        {
            AlsasuaLogger.Warn("TexReales", $"Reporte no encontrado: {fullReport}");
            AlsasuaLogger.Warn("TexReales", "Ejecuta Tools/blender_apply_facade_photos.py para generarlo.");
            yield break;
        }

        // Leer JSON en hilo secundario
        string json = null;
        bool done = false;
        Task.Run(() => { json = File.ReadAllText(fullReport); done = true; });
        yield return new WaitUntil(() => done);

        List<TextureEntry> entries = ParseReport(json);
        if (entries == null || entries.Count == 0)
        {
            AlsasuaLogger.Warn("TexReales", "Sin texturas en el reporte.");
            yield break;
        }

        AlsasuaLogger.Info("TexReales", $"Aplicando texturas reales a {entries.Count} edificios...");

        int aplicados = 0;
        foreach (var entry in entries)
        {
            if (_applyOnlyHighConf && !entry.isHighConf) continue;
            yield return StartCoroutine(AplicarEdificio(entry));
            aplicados++;
            // Ceder frame cada 5 edificios para no bloquear
            if (aplicados % 5 == 0) yield return null;
        }

        AlsasuaLogger.Info("TexReales", $"Texturas reales aplicadas: {aplicados}/{entries.Count}", "OK");
    }

    IEnumerator AplicarEdificio(TextureEntry entry)
    {
        // Buscar el GameObject del edificio por ID en la escena
        GameObject go = FindEdificio(entry.edificioId);
        if (go == null) yield break;

        Renderer[] renderers = go.GetComponentsInChildren<Renderer>(includeInactive: false);
        if (renderers.Length == 0) yield break;

        // Cargar texturas de forma asíncrona
        Texture2D albedo = null, normal = null, ao = null;

        if (!string.IsNullOrEmpty(entry.albedoPath) && File.Exists(entry.albedoPath))
        {
            yield return StartCoroutine(LoadTexture(entry.albedoPath, true,  t => albedo = t));
        }
        if (!string.IsNullOrEmpty(entry.normalPath) && File.Exists(entry.normalPath))
        {
            yield return StartCoroutine(LoadTexture(entry.normalPath, false, t => normal = t));
        }
        if (!string.IsNullOrEmpty(entry.aoPath) && File.Exists(entry.aoPath))
        {
            yield return StartCoroutine(LoadTexture(entry.aoPath, false, t => ao = t));
        }

        if (albedo == null) yield break;

        // Crear material HDRP Lit
        Shader shader = _shaderHDRP != null ? _shaderHDRP : Shader.Find("HDRP/Lit");
        if (shader == null) shader = Shader.Find("Standard");

        Material mat = new Material(shader);
        mat.name = $"Mat_Photo_{entry.edificioId}";
        mat.enableInstancing = true;
        mat.SetFloat(ID_Metallic,    0f);
        mat.SetFloat(ID_Smoothness,  1f - _roughness);
        mat.SetTexture(ID_BaseColor, albedo);
        if (normal != null)
        {
            mat.SetTexture(ID_NormalMap,  normal);
            mat.SetFloat(ID_NormalScale, _normalStrength);
        }
        if (ao != null)
        {
            mat.SetTexture(ID_AOMap, ao);
        }

        _createdMaterials.Add(mat);

        // Aplicar solo al LOD0 / renderer de fachada principal
        Renderer target = FindFacadeRenderer(renderers);
        if (target != null)
        {
            Material[] mats = target.sharedMaterials;
            // Reemplazar el primer slot (fachada) manteniendo el resto (tejado, ventanas)
            if (mats.Length > 0) mats[0] = mat;
            target.sharedMaterials = mats;
        }
    }

    // ── Utilidades ────────────────────────────────────────────────────────────

    static GameObject FindEdificio(string id)
    {
        // Buscar por tag "Building" y nombre que contenga el ID
        GameObject[] all = GameObject.FindGameObjectsWithTag("Building");
        foreach (var go in all)
            if (go.name.Contains(id)) return go;

        // Fallback: buscar en toda la escena
        var allObjs = FindObjectsOfType<GameObject>();
        foreach (var go in allObjs)
            if (go.name.Contains(id)) return go;

        return null;
    }

    static Renderer FindFacadeRenderer(Renderer[] renderers)
    {
        // Preferir LOD0 con mayor poly count
        Renderer best = null;
        int bestPoly = 0;
        foreach (var r in renderers)
        {
            string n = r.gameObject.name.ToLower();
            if (n.Contains("roof") || n.Contains("tejado") || n.Contains("window") || n.Contains("ventana"))
                continue;
            var mf = r.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                int poly = mf.sharedMesh.triangles.Length;
                if (poly > bestPoly) { bestPoly = poly; best = r; }
            }
        }
        return best ?? (renderers.Length > 0 ? renderers[0] : null);
    }

    IEnumerator LoadTexture(string path, bool sRGB, Action<Texture2D> callback)
    {
        byte[] bytes = null;
        bool done = false;
        Task.Run(() => { bytes = File.ReadAllBytes(path); done = true; });
        yield return new WaitUntil(() => done);

        if (bytes == null || bytes.Length == 0) yield break;

        var tex = new Texture2D(2, 2,
            sRGB ? TextureFormat.RGBA32 : TextureFormat.RGBA32,
            mipChain: true);
        tex.filterMode = FilterMode.Trilinear;
        tex.anisoLevel = 4;
        if (tex.LoadImage(bytes))
        {
            tex.Apply(updateMipmaps: true, makeNoLongerReadable: true);
            callback(tex);
        }
    }

    List<TextureEntry> ParseReport(string json)
    {
        var result = new List<TextureEntry>();
        try
        {
            var root = JsonUtility.FromJson<ReportRoot>(json);
            if (root?.edificios == null) return result;
            foreach (var e in root.edificios)
            {
                result.Add(new TextureEntry
                {
                    edificioId  = e.edificio_id,
                    albedoPath  = AbsPath(e.albedo),
                    normalPath  = AbsPath(e.normal),
                    aoPath      = AbsPath(e.ao),
                    isHighConf  = e.n_fotos_disponibles >= 3,
                });
            }
        }
        catch (Exception ex)
        {
            AlsasuaLogger.Warn("TexReales", $"Error parseando reporte: {ex.Message}");
        }
        return result;
    }

    static string AbsPath(string p)
    {
        if (string.IsNullOrEmpty(p)) return null;
        if (Path.IsPathRooted(p)) return p;
        return Path.GetFullPath(p);
    }

    void OnDestroy()
    {
        foreach (var mat in _createdMaterials)
            if (mat != null) Destroy(mat);
        _createdMaterials.Clear();
    }

    // ── Serialización JSON ────────────────────────────────────────────────────

    [Serializable] class ReportRoot   { public int total; public List<ReportEntry> edificios; }
    [Serializable] class ReportEntry  {
        public string edificio_id;
        public string albedo;
        public string normal;
        public string ao;
        public int    n_fotos_disponibles;
    }
}
