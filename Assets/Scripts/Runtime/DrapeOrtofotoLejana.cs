// Assets/Scripts/Runtime/DrapeOrtofotoLejana.cs
// ═══════════════════════════════════════════════════════════════════════════
//  DRAPE DE ORTOFOTO — la foto aérea real "pegada" al relieve (estilo GTA/MSFS)
//
//  El suelo lejano se ve como la ortofoto real, no como splatmap de biomas. Son
//  mallas que se ajustan al terreno (cuadrícula que muestrea TerrenoGlobal),
//  texturizadas con la foto aérea PNOA → pocas draw calls, unlit, sin sombras.
//
//  PIRÁMIDE LOD DE IMAGEN AÉREA (de lejos a cerca, cada una ENCIMA de la previa):
//    1) FONDO  — todo el mundo jugable (±7200 m = mosaico V2), ~3.5 m/px.
//       Tools/DescargarOrtofotoFondo.py → ortofoto_fondo.jpg (+ _meta.json).
//    2) VALLE  — casco/valle (~2.75 km), ~1.34 m/px.
//       Tools/GenerarOrtofotoDrape.py → ortofoto_drape.png.
//    3) NEAR   — teselas 25 cm/px ≤400 m: las pinta AplicadorOrtofoto con un
//       offsetY MAYOR (0.30) para quedar por encima de estas capas.
//  Más allá de ±7200 m → Cesium de fondo lejano (CesiumFondoLejano).
//
//  Auto-arranca solo (RuntimeInitializeOnLoad): no hay que tocar la escena.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

[DefaultExecutionOrder(-54)]
[DisallowMultipleComponent]
public class DrapeOrtofotoLejana : MonoBehaviour
{
    public static DrapeOrtofotoLejana Instance { get; private set; }

    // Cada capa: textura + bbox (coords Unity) + altura sobre el terreno + densidad.
    class Capa
    {
        public string nombre, png, metaJson;
        public float x0, z0, x1, z1;     // bbox Unity (si metaJson != null se lee de ahí)
        public float offsetY;
        public int   celdas, renderQueue;
        public Texture2D tex;
        public Material  mat;
    }

    [System.Serializable] class BBoxMeta { public float ux_min, uz_min, ux_max, uz_max; }

    GameObject _root;

    // DESACTIVADO 2026-06-18: el drape flotante es la herramienta equivocada para el
    // suelo que se pisa — z-fighting con el relieve real (cuadrícula gruesa) → el terreno
    // asoma a parches ("verde raro"). Un drape solo sirve de fondo MUY lejano, no para
    // alfombrar el valle jugable. El look "ortofoto sobre el terreno" debe hacerse
    // texturizando el PROPIO terreno (basemap/TerrainLayer del mosaico), que conforma
    // perfecto y no hace z-fight. Se deja el código + los datos (regenerables) para
    // cuando se aborde de esa forma. NO auto-arranca.
    static void Boot() { }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    IEnumerator Start()
    {
        // Esperar suelo listo (mosaico anillo 0) — alturas vía TerrenoGlobal (tile-aware).
        float t = 0f;
        while (t < 60f)
        {
            var svc = ServiceLocator.Get<ITerrainService>();
            if (svc != null && svc.EstaListo) break;
            if (svc == null && Terrain.activeTerrain != null) break;
            t += 0.5f; yield return new WaitForSeconds(0.5f);
        }

        _root = new GameObject("OrtofotoDrape_Mallas");
        _root.transform.SetParent(transform, false);

        // Pirámide: fondo (todo el mundo) primero, valle (nítido) por encima.
        var capas = new[]
        {
            new Capa { nombre = "Fondo", png = "AlsasuaData/ortofoto_fondo.jpg",
                       metaJson = "AlsasuaData/ortofoto_fondo_meta.json",
                       offsetY = 0.05f, celdas = 400, renderQueue = (int)RenderQueue.Geometry - 2 },
            new Capa { nombre = "Valle", png = "AlsasuaData/ortofoto_drape.png",
                       x0 = 596.3f, z0 = 7378.9f, x1 = 3346.7f, z1 = 10050.6f,
                       offsetY = 0.12f, celdas = 160, renderQueue = (int)RenderQueue.Geometry - 1 },
        };

        int ok = 0;
        foreach (var c in capas)
            yield return StartCoroutine(ConstruirCapa(c, r => { if (r) ok++; }));

        if (ok == 0) { AlsasuaLogger.Warn("DrapeOrto", "Ninguna capa de ortofoto cargada (faltan PNG)."); enabled = false; }
        else AlsasuaLogger.Info("DrapeOrto", $"✅ Drape de ortofoto listo: {ok}/{capas.Length} capas (fondo 14.4 km + valle), {ok} draw calls.");
    }

    IEnumerator ConstruirCapa(Capa c, System.Action<bool> done)
    {
        // bbox desde sidecar JSON si lo hay.
        if (!string.IsNullOrEmpty(c.metaJson))
        {
            string mp = Path.Combine(Application.dataPath, c.metaJson);
            if (File.Exists(mp))
            {
                try
                {
                    var bb = JsonUtility.FromJson<BBoxMeta>(File.ReadAllText(mp));
                    c.x0 = bb.ux_min; c.z0 = bb.uz_min; c.x1 = bb.ux_max; c.z1 = bb.uz_max;
                }
                catch (System.Exception e) { AlsasuaLogger.Warn("DrapeOrto", $"meta {c.nombre}: {e.Message}"); }
            }
        }

        // Cargar textura (lectura en hilo de fondo + LoadImage en el principal).
        string ruta = Path.Combine(Application.dataPath, c.png);
        if (!File.Exists(ruta))
        {
            AlsasuaLogger.Warn("DrapeOrto", $"Capa '{c.nombre}': no existe {c.png} → omitida.");
            done(false); yield break;
        }
        byte[] bytes = null;
        var read = Task.Run(() => { try { bytes = File.ReadAllBytes(ruta); } catch { bytes = null; } });
        while (!read.IsCompleted) yield return null;
        if (bytes == null) { done(false); yield break; }

        c.tex = new Texture2D(2, 2, TextureFormat.RGB24, mipChain: true)
        { name = $"OrtoDrape_{c.nombre}", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
        if (!c.tex.LoadImage(bytes)) { Destroy(c.tex); done(false); yield break; }
        c.tex.Apply(true, true);
        c.tex.anisoLevel = 8;
        yield return null;

        var sh = Shader.Find("HDRP/Unlit") ?? Shader.Find("Unlit/Texture") ?? Shader.Find("Sprites/Default");
        c.mat = new Material(sh) { name = $"Mat_OrtoDrape_{c.nombre}" };
        if (c.mat.HasProperty("_UnlitColorMap")) c.mat.SetTexture("_UnlitColorMap", c.tex);
        if (c.mat.HasProperty("_BaseColorMap"))  c.mat.SetTexture("_BaseColorMap", c.tex);
        if (c.mat.HasProperty("_MainTex"))        c.mat.SetTexture("_MainTex", c.tex);
        c.mat.renderQueue = c.renderQueue;

        yield return StartCoroutine(ConstruirMalla(c));
        done(true);
    }

    IEnumerator ConstruirMalla(Capa c)
    {
        int n = Mathf.Clamp(c.celdas, 8, 1000);
        int vpl = n + 1;
        float dx = (c.x1 - c.x0) / n, dz = (c.z1 - c.z0) / n;
        float w = c.x1 - c.x0, h = c.z1 - c.z0;

        var verts = new Vector3[vpl * vpl];
        var uvs   = new Vector2[vpl * vpl];
        for (int j = 0; j <= n; j++)
        {
            float z = c.z0 + j * dz;
            for (int i = 0; i <= n; i++)
            {
                float x = c.x0 + i * dx;
                int k = j * vpl + i;
                verts[k] = new Vector3(x, TerrenoGlobal.AlturaMundo(x, z) + c.offsetY, z);
                uvs[k]   = new Vector2((x - c.x0) / w, (z - c.z0) / h);
            }
            if ((j & 7) == 0) yield return null;   // amortizar el muestreo de alturas
        }

        var tris = new int[n * n * 6];
        int o = 0;
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                int bl = j * vpl + i, br = bl + 1, tl = bl + vpl, tr = tl + 1;
                tris[o++] = bl; tris[o++] = tl; tris[o++] = br;
                tris[o++] = br; tris[o++] = tl; tris[o++] = tr;
            }

        var mesh = new Mesh { name = $"OrtoDrapeMesh_{c.nombre}" };
        if (verts.Length > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var go = new GameObject($"OrtoDrape_{c.nombre}");
        go.transform.SetParent(_root.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial    = c.mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows    = false;
        go.isStatic = true;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
