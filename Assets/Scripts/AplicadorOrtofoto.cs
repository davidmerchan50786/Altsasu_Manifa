// Assets/Scripts/AplicadorOrtofoto.cs
// ═══════════════════════════════════════════════════════════════════════════
//  APLICADOR DE ORTOFOTO IGN PNOA
//  Proyecta las 72 teselas de ortofoto aérea (25cm/pixel) como quads planos
//  sobre el terreno, pintando el suelo con la imagen real de Alsasua.
//
//  Datos: IGN PNOA 2024 CC BY 4.0 — 1320×1320px por tesela ≈ 330m×330m
//  Cobertura: 42.888–42.912°N, 2.185–2.152°W — 23.8 km² del casco urbano
//
//  Uso:
//    · Añadir este componente al GO "Ambiente" o "Terreno"
//    · Se carga automáticamente al iniciar, después del terreno
//    · Tools → Alsasua → Ortofoto → 🗺 Aplicar (bake en Editor)
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using System.IO;

[DefaultExecutionOrder(-55)]   // después de GeometríaPrecisa (-60)
public class AplicadorOrtofoto : MonoBehaviour
{
    public static AplicadorOrtofoto Instance { get; private set; }

    [Header("Configuración")]
    [Tooltip("Y de los quads sobre el terreno (m)")]
    public float offsetY = 0.05f;

    [Tooltip("Activar streaming: cargar solo teselas cercanas a la cámara (SIEMPRE activado para controlar memoria)")]
    public bool streaming = true;

    [Range(100f, 600f)]
    [Tooltip("Radio máximo. Cada tile ~7MB GPU. A 400m se cargan ~4 tiles=28MB")]
    public float radioStreaming = 400f;

    [Tooltip("Shader para los quads de ortofoto (deja vacío = auto-detecta)")]
    public Shader shaderOrtofoto;

    // ── Datos internos ─────────────────────────────────────────────────────

    [System.Serializable]
    public class TiloMeta
    {
        public string file;
        public float  ux_min, uz_min, ux_max, uz_max;
        public float  pixel_m;
        public int    width_px, height_px;
    }

    public class TiloRuntime
    {
        public TiloMeta   meta;
        public GameObject go;
        public Texture2D  tex;
        public bool       cargada;
    }

    List<TiloRuntime>       _tilos      = new();
    Dictionary<int,Material> _matPool   = new(); // Bug-fix #1: shared material pool per tile
    Transform         _parent;
    Shader            _shader;
    Camera            _cam;
#pragma warning disable CS0414
    bool              _iniciado;
#pragma warning restore CS0414

    const string META_PATH = "Assets/AlsasuaData/orto_tiles_meta.json";
    const string TILES_DIR = "Assets/AlsasuaData/tiles/orto/";

    // ══════════════════════════════════════════════════════════════════════
    //  BOOT
    // ══════════════════════════════════════════════════════════════════════

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    IEnumerator Start()
    {
        while (Terrain.activeTerrain == null) yield return new WaitForSeconds(0.5f);

        _shader = shaderOrtofoto
                ?? Shader.Find("HDRP/Unlit")
                ?? Shader.Find("Unlit/Texture")
                ?? Shader.Find("Standard");

        _parent = new GameObject("Ortofoto_Tiles").transform;
        _parent.SetParent(transform, false);

        if (!CargarMeta()) yield break;

        // Bug-fix #1: pre-create one material per tile into the pool (Awake-equivalent)
        for (int i = 0; i < _tilos.Count; i++)
        {
            var mat = CrearMatNuevo();
            _matPool[i] = mat;
            var mr = _tilos[i].go.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = mat;
        }

        _cam      = Camera.main;
        _iniciado = true;

        if (streaming)
        {
            StartCoroutine(BucleStreaming());
        }
        else
        {
            yield return StartCoroutine(CargarTodasLasTeselas());
        }

        AlsasuaLogger.Info("Ortofoto",
            $"✅ {_tilos.Count} teselas ortofoto IGN PNOA listas");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  CARGA DE METADATOS
    // ══════════════════════════════════════════════════════════════════════

    bool CargarMeta()
    {
        string fullPath = Path.Combine(
            Application.dataPath.Replace("Assets", ""), META_PATH);

        if (!File.Exists(fullPath))
        {
            AlsasuaLogger.Warn("Ortofoto",
                $"orto_tiles_meta.json no encontrado en {META_PATH}");
            return false;
        }

        try
        {
            string json = File.ReadAllText(fullPath);
            var metas   = JsonHelper.ParseArray<TiloMeta>(json);

            foreach (var m in metas)
            {
                float cx = (m.ux_min + m.ux_max) * 0.5f;
                float cz = (m.uz_min + m.uz_max) * 0.5f;
                float w  = m.ux_max - m.ux_min;
                float h  = m.uz_max - m.uz_min;

                var worldCenter = new Vector3(cx, 0, cz);

                // Bug-fix #4: quad conforms to terrain surface at all 4 corners
                var go  = new GameObject($"Tile_{m.file}");
                go.transform.SetParent(_parent, false);

                var mf  = go.AddComponent<MeshFilter>();
                mf.sharedMesh = CrearQuadConforme(w, h, worldCenter, offsetY);

                var mr  = go.AddComponent<MeshRenderer>();
                // sharedMaterial will be set from _matPool after Start initialises it
                mr.shadowCastingMode  = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows     = false;

                float y = AlturaTerreno(worldCenter) + offsetY;
                go.transform.position = new Vector3(cx, y, cz);
                go.isStatic = true;

                _tilos.Add(new TiloRuntime { meta = m, go = go, cargada = false });
            }

            AlsasuaLogger.Info("Ortofoto",
                $"Meta cargada: {_tilos.Count} teselas de ortofoto");
            return true;
        }
        catch (System.Exception e)
        {
            AlsasuaLogger.Warn("Ortofoto", $"Error cargando meta: {e.Message}");
            return false;
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  STREAMING: carga/descarga según distancia a cámara
    // ══════════════════════════════════════════════════════════════════════

    IEnumerator BucleStreaming()
    {
        while (true)
        {
            yield return new WaitForSeconds(1f); // revisar cada segundo

            if (_cam == null) _cam = Camera.main;
            if (_cam == null) continue;

            Vector3 camPos = _cam.transform.position;
            float   r2     = radioStreaming * radioStreaming;

            int cargadasEsteFrame = 0;
            foreach (var t in _tilos)
            {
                float cx    = (t.meta.ux_min + t.meta.ux_max) * 0.5f;
                float cz    = (t.meta.uz_min + t.meta.uz_max) * 0.5f;
                float dist2 = (camPos.x - cx) * (camPos.x - cx)
                            + (camPos.z - cz) * (camPos.z - cz);

                if (dist2 <= r2 && !t.cargada && cargadasEsteFrame < 2)
                {
                    yield return StartCoroutine(CargarTesela(t));
                    cargadasEsteFrame++;   // max 2 tiles por tick (cada 1s) = max 14MB/s
                }
                else if (dist2 > r2 * 1.5f && t.cargada)
                    DescargarTesela(t);
            }
        }
    }

    IEnumerator CargarTodasLasTeselas()
    {
        int i = 0;
        foreach (var t in _tilos)
        {
            yield return StartCoroutine(CargarTesela(t));
            if (i++ % 8 == 0) yield return null;
        }
    }

    IEnumerator CargarTesela(TiloRuntime t)
    {
        if (t.cargada) yield break;

        string fullPath = Path.Combine(
            Application.dataPath.Replace("Assets", ""), TILES_DIR, t.meta.file);

        if (!File.Exists(fullPath)) yield break;

        // Bug-fix #2: read file on background thread so main thread is not blocked
        byte[] bytes = null;
        var readTask = Task.Run(() =>
        {
            try { bytes = File.ReadAllBytes(fullPath); }
            catch { bytes = null; }
        });
        while (!readTask.IsCompleted) yield return null; // 1 tile per frame max
        if (bytes == null) yield break;

        yield return null; // frame break before GPU upload

        // Bug-fix #3: create with mipChain=true and call Apply(generateMips, noLongerReadable=false)
        var tex = new Texture2D(t.meta.width_px, t.meta.height_px,
                                TextureFormat.RGB24, mipChain: true);
        tex.name = t.meta.file;
        tex.filterMode   = FilterMode.Trilinear; // trilinear benefits from mipmaps
        tex.wrapMode     = TextureWrapMode.Clamp;

        if (!tex.LoadImage(bytes)) { Destroy(tex); yield break; }
        tex.Apply(true, false); // Bug-fix #3: generate full mip chain

        t.tex = tex;
        t.cargada = true;

        // Bug-fix #1: reuse pooled material — just set texture, no new Material()
        int idx = _tilos.IndexOf(t);
        var mr = t.go.GetComponent<MeshRenderer>();
        if (mr != null && _matPool.TryGetValue(idx, out var mat))
        {
            AplicarTexturaMat(mat, tex);
            mr.sharedMaterial = mat;
            t.go.SetActive(true);
        }
    }

    void DescargarTesela(TiloRuntime t)
    {
        if (!t.cargada) return;
        t.cargada = false;
        t.go.SetActive(false);

        if (t.tex != null)
        {
            Destroy(t.tex);
            t.tex = null;
        }

        // Bug-fix #1: clear texture from pooled material instead of creating new one
        int idx = _tilos.IndexOf(t);
        if (_matPool.TryGetValue(idx, out var mat))
            AplicarTexturaMat(mat, null);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  HELPERS DE GEOMETRÍA Y MATERIAL
    // ══════════════════════════════════════════════════════════════════════

    static Mesh CrearQuad(float w, float h)
    {
        float hw = w * 0.5f, hh = h * 0.5f;
        var m = new Mesh { name = "OrtoQuad" };
        m.SetVertices(new[]
        {
            new Vector3(-hw, 0, -hh),
            new Vector3( hw, 0, -hh),
            new Vector3( hw, 0,  hh),
            new Vector3(-hw, 0,  hh),
        });
        m.SetTriangles(new[] { 0,2,1, 0,3,2 }, 0);
        m.SetUVs(0, new[]
        {
            new Vector2(0,0), new Vector2(1,0),
            new Vector2(1,1), new Vector2(0,1),
        });
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    // Bug-fix #4: build a quad whose 4 corner vertices follow the terrain surface,
    // so tiles conform to excavated riverbeds instead of floating at fixed Y.
    // worldCenter is the tile's world-space center (XZ); the mesh is in local space
    // (centered at origin) but each vertex Y is offset relative to the center height.
    static Mesh CrearQuadConforme(float w, float h, Vector3 worldCenter, float offsetY)
    {
        float hw = w * 0.5f, hh = h * 0.5f;
        float cy = AlturaTerreno(worldCenter);

        // Sample terrain height at each corner in world space
        float y00 = AlturaTerreno(new Vector3(worldCenter.x - hw, 0, worldCenter.z - hh)) + offsetY - cy;
        float y10 = AlturaTerreno(new Vector3(worldCenter.x + hw, 0, worldCenter.z - hh)) + offsetY - cy;
        float y11 = AlturaTerreno(new Vector3(worldCenter.x + hw, 0, worldCenter.z + hh)) + offsetY - cy;
        float y01 = AlturaTerreno(new Vector3(worldCenter.x - hw, 0, worldCenter.z + hh)) + offsetY - cy;

        var m = new Mesh { name = "OrtoQuadConforme" };
        m.SetVertices(new[]
        {
            new Vector3(-hw, y00, -hh),
            new Vector3( hw, y10, -hh),
            new Vector3( hw, y11,  hh),
            new Vector3(-hw, y01,  hh),
        });
        m.SetTriangles(new[] { 0,2,1, 0,3,2 }, 0);
        m.SetUVs(0, new[]
        {
            new Vector2(0,0), new Vector2(1,0),
            new Vector2(1,1), new Vector2(0,1),
        });
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    // Bug-fix #1: factory called once per tile into _matPool
    Material CrearMatNuevo()
    {
        var mat = new Material(_shader);
        mat.enableInstancing = true; // GPU instancing
        if (mat.HasProperty("_SurfaceType"))
            mat.SetFloat("_SurfaceType", 0); // opaque HDRP
        return mat;
    }

    // Set or clear the texture on an existing pooled material
    void AplicarTexturaMat(Material mat, Texture2D tex)
    {
        if (mat.HasProperty("_UnlitColorMap"))
            mat.SetTexture("_UnlitColorMap", tex);
        else if (mat.HasProperty("_MainTex"))
            mat.SetTexture("_MainTex", tex);
    }

    static float AlturaTerreno(Vector3 pos) => GeoDataAlsasua.AlturaTerreno(pos);

    // ── API pública ────────────────────────────────────────────────────────

    /// Devuelve el tile de ortofoto que contiene las coordenadas Unity (x,z)
    public TiloRuntime TileEnPosicion(float ux, float uz)
        => _tilos.Find(t => ux >= t.meta.ux_min && ux <= t.meta.ux_max
                         && uz >= t.meta.uz_min && uz <= t.meta.uz_max);

    /// Sample del color de la ortofoto en posición Unity (x,z)
    public bool SampleColor(float ux, float uz, out Color color)
    {
        color = Color.black;
        var tile = TileEnPosicion(ux, uz);
        if (tile == null || !tile.cargada || tile.tex == null) return false;

        float u = Mathf.InverseLerp(tile.meta.ux_min, tile.meta.ux_max, ux);
        float v = Mathf.InverseLerp(tile.meta.uz_min, tile.meta.uz_max, uz);
        color   = tile.tex.GetPixelBilinear(u, v);
        return true;
    }
}
