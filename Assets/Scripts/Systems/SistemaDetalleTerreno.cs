// Assets/Scripts/SistemaDetalleTerreno.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SISTEMA DE DETALLE DE TERRENO — GPU instanced ground cover
//
//  Distribuye micro-objetos alrededor del jugador usando DrawMeshInstanced:
//    · Piedrecitas  — bioma hierba y roca, radio 25m
//    · Champiñones  — bioma bosque, zonas sombrías, radio 18m
//    · Ramillas     — bioma bosque y hierba, radio 20m
//    · Parches musgo— bioma musgo (canal 6), laderas norte, radio 22m
//
//  Sin GameObjects — cero overhead en jerarquía.
//  Se regenera cuando el jugador se mueve >8m desde la última generación.
//  Burst Jobs opcionales para el sampling del terreno.
//
//  Coloca este componente en el mismo GO que SistemaTerreno o SceneBootstrapper.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-50)]
public class SistemaDetalleTerreno : MonoBehaviour
{
    public static SistemaDetalleTerreno Instance { get; private set; }

    [Header("Radios de distribución")]
    public float radioPiedrecitas  = 25f;
    public float radioChampinones  = 18f;
    public float radioRamillas     = 20f;
    public float radioMusgo        = 22f;
    [Tooltip("El jugador debe moverse esta distancia para regenerar.")]
    public float distanciaRegen    = 8f;
    [Tooltip("Densidad general (0-1). Reduce para mejor rendimiento.")]
    [Range(0.1f, 1f)] public float densidad = 0.6f;

    // ── Materiales ─────────────────────────────────────────────────────────
    Material _matPiedra;
    Material _matHongo;
    Material _matPalo;
    Material _matMusgo;

    // ── Meshes procedurales ────────────────────────────────────────────────
    Mesh _meshPiedra;
    Mesh _meshHongo;
    Mesh _meshPalo;
    Mesh _meshMusgoPlano;

    // ── Buffers de instancias (DrawMeshInstanced: max 1023) ───────────────
    const int MAX_INSTANCIAS = 1023;
    Matrix4x4[] _mPiedras    = new Matrix4x4[MAX_INSTANCIAS];
    Matrix4x4[] _mHongos     = new Matrix4x4[MAX_INSTANCIAS];
    Matrix4x4[] _mPalos      = new Matrix4x4[MAX_INSTANCIAS];
    Matrix4x4[] _mMusgo      = new Matrix4x4[MAX_INSTANCIAS];
    int _nPiedras, _nHongos, _nPalos, _nMusgo;

    // ── Estado ────────────────────────────────────────────────────────────
    Vector3    _ultimaRegen = new Vector3(99999f, 0, 99999f);
    bool       _listo;
    Terrain    _terrain;

    // BUG FIX #3: alphamap cacheado en Start — evita GetAlphamaps() cada 8m.
    // GetAlphamaps = lectura GPU→CPU (~2-8ms). Con cache = O(1) lookup.
    // Se invalida llamando InvalidarCacheAlpha() si SistemaTerreno repinta.
    float[,,]  _alphaCache;
    int        _alphaCacheW, _alphaCacheH, _alphaCacheCapas;
    Vector3    _terrainPos;
    float      _terrainW, _terrainL;

    // ── Shader property IDs ───────────────────────────────────────────────
    static readonly int ID_BaseColor  = Shader.PropertyToID("_BaseColor");
    static readonly int ID_Smoothness = Shader.PropertyToID("_Smoothness");

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void Start() => StartCoroutine(InicializarTras(6f));

    IEnumerator InicializarTras(float delay)
    {
        yield return new WaitForSeconds(delay);
        _terrain = Terrain.activeTerrain;
        if (_terrain == null) { AlsasuaLogger.Warn("DetalleTerreno", "Sin Terrain activo"); yield break; }

        CrearMateriales();
        CrearMeshes();
        // BUG FIX #3: pre-cargar alphamap completo una sola vez
        var td = _terrain.terrainData;
        _alphaCacheW    = td.alphamapWidth;
        _alphaCacheH    = td.alphamapHeight;
        _alphaCacheCapas= td.alphamapLayers;
        _terrainPos     = _terrain.transform.position;
        _terrainW       = td.size.x;
        _terrainL       = td.size.z;
        _alphaCache     = td.GetAlphamaps(0, 0, _alphaCacheW, _alphaCacheH);
        _listo = true;
        AlsasuaLogger.Info("DetalleTerreno",
            $"Sistema de detalle listo. AlphaCache: {_alphaCacheW}×{_alphaCacheH}×{_alphaCacheCapas}");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  UPDATE — regenerar si el jugador se movió
    // ════════════════════════════════════════════════════════════════════════

    void Update()
    {
        if (!_listo) return;

        var jugador = AltsasuCore.Jugador;
        Vector3 pos = jugador != null ? jugador.position : Vector3.zero;

        if (Vector3.Distance(pos, _ultimaRegen) > distanciaRegen)
        {
            _ultimaRegen = pos;
            RegenerarInstancias(pos);
        }

        // Dibujar cada tipo
        if (_nPiedras > 0) Graphics.DrawMeshInstanced(_meshPiedra,   0, _matPiedra, _mPiedras, _nPiedras);
        if (_nHongos  > 0) Graphics.DrawMeshInstanced(_meshHongo,    0, _matHongo,  _mHongos,  _nHongos);
        if (_nPalos   > 0) Graphics.DrawMeshInstanced(_meshPalo,     0, _matPalo,   _mPalos,   _nPalos);
        if (_nMusgo   > 0) Graphics.DrawMeshInstanced(_meshMusgoPlano, 0, _matMusgo, _mMusgo,  _nMusgo);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  GENERACIÓN DE INSTANCIAS
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Invalida la cache de alphamap. Llamar cuando SistemaTerreno repinta.</summary>
    public void InvalidarCacheAlpha()
    {
        if (_terrain == null) return;
        var td = _terrain.terrainData;
        _alphaCache = td.GetAlphamaps(0, 0, td.alphamapWidth, td.alphamapHeight);
        _alphaCacheCapas = td.alphamapLayers;
        _ultimaRegen = new Vector3(99999f, 0, 99999f); // forzar regeneración
    }

    void RegenerarInstancias(Vector3 centro)
    {
        _nPiedras = _nHongos = _nPalos = _nMusgo = 0;
        if (_alphaCache == null) return; // cache aún no lista

        // BUG FIX #3: usar cache en lugar de GetAlphamaps() costoso
        int   aw      = _alphaCacheW;
        int   ah      = _alphaCacheH;
        float terW    = _terrainW;
        float terL    = _terrainL;
        Vector3 terP  = _terrainPos;
        float[,,] alpha = _alphaCache;  // O(1) — sin lectura GPU
        int numCapas  = _alphaCacheCapas;
        // (xStart, zStart, w, h ya no hacen falta — trabajamos sobre el cache completo)

        // Semilla determinista por posición de regeneración
        var rng = new System.Random(
            Mathf.RoundToInt(centro.x * 13.7f) ^ Mathf.RoundToInt(centro.z * 31.3f));

        // Ancho de región a poblar: el doble del radio de detalle mayor
        float regionW = Mathf.Max(Mathf.Max(radioPiedrecitas, radioChampinones),
                                  Mathf.Max(radioRamillas, radioMusgo)) * 2f;

        // Muestreo regular con jitter
        float paso = 1.2f / Mathf.Max(0.1f, densidad);

        for (float wz = -regionW * 0.5f; wz < regionW * 0.5f; wz += paso)
        for (float wx = -regionW * 0.5f; wx < regionW * 0.5f; wx += paso)
        {
            // Jitter para no ser una grilla perfecta
            float jx = (float)(rng.NextDouble() - 0.5) * paso * 0.8f;
            float jz = (float)(rng.NextDouble() - 0.5) * paso * 0.8f;
            float px = centro.x + wx + jx;
            float pz = centro.z + wz + jz;

            float dist2D = Mathf.Sqrt(wx * wx + wz * wz);
            float py = _terrain.SampleHeight(new Vector3(px, 0, pz)) + terP.y;

            // Coordenadas en alphamap local
            float fnx = Mathf.Clamp01((px - terP.x) / terW);
            float fnz = Mathf.Clamp01((pz - terP.z) / terL);
            int   aax = Mathf.Clamp((int)(fnx * aw), 0, aw - 1);
            int   aaz = Mathf.Clamp((int)(fnz * ah), 0, ah - 1);

            float wHierba  = numCapas > 0 ? alpha[aaz, aax, 0] : 0f;
            float wBosque  = numCapas > 7 ? alpha[aaz, aax, 7] : 0f;
            float wMusgoBio= numCapas > 6 ? alpha[aaz, aax, 6] : 0f;
            float wRoca    = numCapas > 2 ? alpha[aaz, aax, 2] : 0f;

            double dice = rng.NextDouble();

            // ── Piedrecitas: hierba + roca, radio 25m ────────────────────
            if (dist2D < radioPiedrecitas && _nPiedras < MAX_INSTANCIAS
                && (wHierba > 0.2f || wRoca > 0.15f) && dice < 0.18f * densidad)
            {
                float s = (float)(rng.NextDouble() * 0.08f + 0.04f); // 4-12 cm
                float ry = (float)(rng.NextDouble() * 360f);
                _mPiedras[_nPiedras++] = Matrix4x4.TRS(
                    new Vector3(px, py - s * 0.3f, pz),
                    Quaternion.Euler((float)(rng.NextDouble() * 20f), ry, (float)(rng.NextDouble() * 20f)),
                    new Vector3(s, s * 0.6f, s));
            }

            // ── Champiñones: bosque sombrío, radio 18m ───────────────────
            if (dist2D < radioChampinones && _nHongos < MAX_INSTANCIAS
                && wBosque > 0.35f && dice < 0.04f * densidad)
            {
                float s = (float)(rng.NextDouble() * 0.10f + 0.06f);
                _mHongos[_nHongos++] = Matrix4x4.TRS(
                    new Vector3(px, py, pz),
                    Quaternion.Euler(0f, (float)(rng.NextDouble() * 360f), 0f),
                    Vector3.one * s);
            }

            // ── Ramillas: bosque + hierba, radio 20m ─────────────────────
            if (dist2D < radioRamillas && _nPalos < MAX_INSTANCIAS
                && (wBosque > 0.2f || wHierba > 0.3f) && dice < 0.08f * densidad)
            {
                float lx = (float)(rng.NextDouble() * 0.18f + 0.06f);
                float ry = (float)(rng.NextDouble() * 360f);
                _mPalos[_nPalos++] = Matrix4x4.TRS(
                    new Vector3(px, py + 0.01f, pz),
                    Quaternion.Euler(0f, ry, 90f), // tumbado en el suelo
                    new Vector3(lx, 0.015f, 0.015f));
            }

            // ── Parches de musgo: bioma musgo canal 6, radio 22m ─────────
            if (dist2D < radioMusgo && _nMusgo < MAX_INSTANCIAS
                && wMusgoBio > 0.25f && dice < 0.12f * densidad)
            {
                float s = (float)(rng.NextDouble() * 0.25f + 0.10f);
                float ry = (float)(rng.NextDouble() * 360f);
                _mMusgo[_nMusgo++] = Matrix4x4.TRS(
                    new Vector3(px, py + 0.005f, pz),
                    Quaternion.Euler(0f, ry, 0f),
                    new Vector3(s, 0.02f, s)); // plano, aplastado contra suelo
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  MESHES PROCEDURALES
    // ════════════════════════════════════════════════════════════════════════

    void CrearMeshes()
    {
        _meshPiedra    = CrearMeshIcosaedro();
        _meshHongo     = CrearMeshHongo();
        _meshPalo      = CrearMeshCilindro(4);
        _meshMusgoPlano = CrearMeshPlano();
    }

    static Mesh CrearMeshIcosaedro()
    {
        const float t = 1.618f;
        var verts = new Vector3[] {
            new Vector3(-1,t,0).normalized, new Vector3(1,t,0).normalized,  new Vector3(-1,-t,0).normalized, new Vector3(1,-t,0).normalized,
            new Vector3(0,-1,t).normalized, new Vector3(0,1,t).normalized,  new Vector3(0,-1,-t).normalized, new Vector3(0,1,-t).normalized,
            new Vector3(t,0,-1).normalized, new Vector3(t,0,1).normalized,  new Vector3(-t,0,-1).normalized, new Vector3(-t,0,1).normalized,
        };
        var tris = new int[] {
            0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11,
            1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
            3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9,
            4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1
        };
        var m = new Mesh { name = "Piedrecita" };
        m.vertices = verts; m.triangles = tris; m.RecalculateNormals(); return m;
    }

    static Mesh CrearMeshHongo()
    {
        // Tallo cilíndrico + sombrero cónico plano
        var verts = new List<Vector3>();
        var tris  = new List<int>();
        const int seg = 6;

        // Tallo
        for (int i = 0; i <= seg; i++)
        {
            float a = i / (float)seg * Mathf.PI * 2f;
            float c = Mathf.Cos(a) * 0.15f, s = Mathf.Sin(a) * 0.15f;
            verts.Add(new Vector3(c, 0f,    s));
            verts.Add(new Vector3(c, 0.7f,  s));
        }
        for (int i = 0; i < seg; i++)
        {
            int b = i * 2;
            tris.AddRange(new[] { b, b+1, b+2, b+1, b+3, b+2 });
        }

        // Sombrero (disco + punta)
        int baseIdx = verts.Count;
        verts.Add(new Vector3(0, 1.1f, 0)); // punta
        for (int i = 0; i <= seg; i++)
        {
            float a = i / (float)seg * Mathf.PI * 2f;
            verts.Add(new Vector3(Mathf.Cos(a) * 0.6f, 0.7f, Mathf.Sin(a) * 0.6f));
        }
        for (int i = 0; i < seg; i++)
            tris.AddRange(new[] { baseIdx, baseIdx + 1 + i + 1, baseIdx + 1 + i });

        var m = new Mesh { name = "Hongo" };
        m.vertices = verts.ToArray(); m.triangles = tris.ToArray();
        m.RecalculateNormals(); return m;
    }

    static Mesh CrearMeshCilindro(int seg)
    {
        var verts = new List<Vector3>();
        var tris  = new List<int>();
        for (int i = 0; i <= seg; i++)
        {
            float a = i / (float)seg * Mathf.PI * 2f;
            float c = Mathf.Cos(a) * 0.5f, s = Mathf.Sin(a) * 0.5f;
            verts.Add(new Vector3(c, -0.5f, s));
            verts.Add(new Vector3(c,  0.5f, s));
        }
        for (int i = 0; i < seg; i++)
        {
            int b = i * 2;
            tris.AddRange(new[] { b, b+1, b+2, b+1, b+3, b+2 });
        }
        var m = new Mesh { name = "Palo" };
        m.vertices = verts.ToArray(); m.triangles = tris.ToArray();
        m.RecalculateNormals(); return m;
    }

    static Mesh CrearMeshPlano()
    {
        var m = new Mesh { name = "MusgoPlano" };
        m.vertices  = new[] { new Vector3(-0.5f,0,-0.5f), new Vector3(0.5f,0,-0.5f),
                              new Vector3(-0.5f,0, 0.5f), new Vector3(0.5f,0, 0.5f) };
        m.triangles = new[] { 0,2,1, 2,3,1 };
        m.uv        = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
        m.RecalculateNormals(); return m;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  MATERIALES
    // ════════════════════════════════════════════════════════════════════════

    void CrearMateriales()
    {
        Shader sh = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");

        _matPiedra = CrearMat(sh, "Mat_Piedrecita", new Color(0.55f, 0.52f, 0.48f), 0.12f);
        _matHongo  = CrearMat(sh, "Mat_Hongo",  new Color(0.75f, 0.55f, 0.30f), 0.20f);
        _matPalo   = CrearMat(sh, "Mat_Palo",   new Color(0.38f, 0.28f, 0.18f), 0.08f);
        _matMusgo  = CrearMat(sh, "Mat_Musgo",  new Color(0.32f, 0.50f, 0.22f), 0.10f);
    }

    static Material CrearMat(Shader sh, string nombre, Color color, float smoothness)
    {
        var m = new Material(sh) { name = nombre };
        if (m.HasProperty(ID_BaseColor))  m.SetColor(ID_BaseColor, color);
        else m.color = color;
        if (m.HasProperty(ID_Smoothness)) m.SetFloat(ID_Smoothness, smoothness);
        m.enableInstancing = true;
        return m;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
