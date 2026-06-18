// Assets/Scripts/Runtime/DrapeOrtofotoLejana.cs
// ═══════════════════════════════════════════════════════════════════════════
//  DRAPE DE ORTOFOTO LEJANA — la foto aérea real "pegada" al relieve a distancia
//
//  Truco AAA (GTA/MSFS): el suelo lejano se ve como la ortofoto real del valle,
//  no como splatmap de biomas. Es UNA sola malla que se ajusta al terreno
//  (cuadrícula que muestrea TerrenoGlobal.AlturaMundo) texturizada con el bake
//  combinado de las 72 teselas PNOA → 1 draw call, unlit, sin sombras, baratísimo.
//
//  · NEAR (≤ ~400 m): AplicadorOrtofoto pinta teselas 25 cm/px ENCIMA de este
//    drape (offsetY mayor) → cerca se ve nítido, lejos este drape (~1.3 m/px).
//  · Bake: Tools/GenerarOrtofotoDrape.py → Assets/AlsasuaData/ortofoto_drape.png
//    (norte arriba; v=1 = norte = Z máx). Se lee en runtime (File + LoadImage).
//  · Cobertura: solo el bbox del valle con datos PNOA (~2.75×2.67 km centrado en
//    la plaza). Más allá (sierras) → terreno/biomas + Cesium de fondo lejano.
//
//  Auto-arranca solo (RuntimeInitializeOnLoad): no hay que tocar la escena.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

[DefaultExecutionOrder(-54)]   // tras AplicadorOrtofoto (-55), tras terreno
[DisallowMultipleComponent]
public class DrapeOrtofotoLejana : MonoBehaviour
{
    public static DrapeOrtofotoLejana Instance { get; private set; }

    [Header("Geometría")]
    [Tooltip("Celdas por lado de la cuadrícula (se ajusta al relieve). 160 → ~17 m/celda.")]
    public int celdas = 160;
    [Tooltip("Altura sobre el terreno (m). Menor que el de AplicadorOrtofoto para que las teselas nítidas ganen cerca.")]
    public float offsetY = 0.10f;

    // bbox del valle con datos PNOA — = orto_tiles_meta.json (= GenerarOrtofotoDrape.py)
    const float X0 = 596.3f, X1 = 3346.7f, Z0 = 7378.9f, Z1 = 10050.6f;
    const string PNG = "AlsasuaData/ortofoto_drape.png";

    GameObject _go;
    Texture2D  _tex;
    Material   _mat;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance != null || FindFirstObjectByType<DrapeOrtofotoLejana>() != null) return;
        var go = new GameObject("DrapeOrtofotoLejana");
        go.AddComponent<DrapeOrtofotoLejana>();
    }

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

        // Cargar el bake (hilo de fondo + LoadImage en el principal).
        string ruta = Path.Combine(Application.dataPath, PNG);
        if (!File.Exists(ruta))
        {
            AlsasuaLogger.Warn("DrapeOrto",
                $"No existe {PNG} → ejecuta Tools/GenerarOrtofotoDrape.py. Drape desactivado.");
            enabled = false; yield break;
        }
        byte[] bytes = null;
        var read = Task.Run(() => { try { bytes = File.ReadAllBytes(ruta); } catch { bytes = null; } });
        while (!read.IsCompleted) yield return null;
        if (bytes == null) { enabled = false; yield break; }

        _tex = new Texture2D(2, 2, TextureFormat.RGB24, mipChain: true)
        { name = "OrtofotoDrape", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
        if (!_tex.LoadImage(bytes)) { Destroy(_tex); enabled = false; yield break; }
        _tex.Apply(true, true);   // genera mips + libera copia CPU (no la muestreamos)
        _tex.anisoLevel = 8;      // se ve en ángulo rasante a distancia

        yield return null;

        var sh = Shader.Find("HDRP/Unlit") ?? Shader.Find("Unlit/Texture") ?? Shader.Find("Sprites/Default");
        _mat = new Material(sh) { name = "Mat_OrtofotoDrape" };
        if (_mat.HasProperty("_UnlitColorMap")) _mat.SetTexture("_UnlitColorMap", _tex);
        if (_mat.HasProperty("_BaseColorMap"))  _mat.SetTexture("_BaseColorMap", _tex);
        if (_mat.HasProperty("_MainTex"))        _mat.SetTexture("_MainTex", _tex);
        _mat.renderQueue = (int)RenderQueue.Geometry;   // base; las teselas near van por encima

        yield return StartCoroutine(ConstruirMalla());

        AlsasuaLogger.Info("DrapeOrto",
            $"✅ Drape de ortofoto lejana listo ({_tex.width}×{_tex.height}, {celdas}×{celdas} celdas, 1 draw call).");
    }

    IEnumerator ConstruirMalla()
    {
        int n = Mathf.Clamp(celdas, 8, 254);   // (n+1)² < 65535 → índices 16 bit
        int vpl = n + 1;
        float dx = (X1 - X0) / n, dz = (Z1 - Z0) / n;

        var verts = new Vector3[vpl * vpl];
        var uvs   = new Vector2[vpl * vpl];
        for (int j = 0; j <= n; j++)
        {
            float z = Z0 + j * dz;
            for (int i = 0; i <= n; i++)
            {
                float x = X0 + i * dx;
                int k = j * vpl + i;
                verts[k] = new Vector3(x, TerrenoGlobal.AlturaMundo(x, z) + offsetY, z);
                uvs[k]   = new Vector2((x - X0) / (X1 - X0), (z - Z0) / (Z1 - Z0));
            }
            if ((j & 15) == 0) yield return null;   // amortizar el muestreo de alturas
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

        var mesh = new Mesh { name = "OrtofotoDrapeMesh" };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        _go = new GameObject("OrtofotoDrape_Mesh");
        _go.transform.SetParent(transform, false);
        _go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = _go.AddComponent<MeshRenderer>();
        mr.sharedMaterial    = _mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows    = false;
        _go.isStatic = true;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (_mat != null) Destroy(_mat);
        if (_tex != null) Destroy(_tex);
    }
}
