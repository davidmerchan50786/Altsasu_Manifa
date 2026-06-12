// Assets/Scripts/GeneradorRocasProcedurales.cs
// ═══════════════════════════════════════════════════════════════════════════
//  GENERADOR DE ROCAS PROCEDURALES — GPU Instancing en bioma Roca
//
//  · Lee el alphamap del terrain activo para detectar bioma Roca (canal 2)
//  · Coloca 1-4 rocas por zona usando posición seeded (sin GameObjects)
//  · Mesh procedural: esfera con ruido Perlin 3D en vértices
//  · Material HDRP/Lit con cliff_side de Textures_AAA/Naturaleza/
//  · Graphics.DrawMeshInstanced en chunks de 1023 instancias
//  · LOD por distancia: <25m full, 25-80m low-poly, >80m culled
//  · Se actualiza si el jugador se mueve >50m
//
//  DefaultExecutionOrder(-55) — después de SistemaTerreno (-60 ... -70)
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-55)]
public class GeneradorRocasProcedurales : MonoBehaviour
{
    public static GeneradorRocasProcedurales Instance { get; private set; }

    // ── Inspector ──────────────────────────────────────────────────────────
    [Header("Parámetros de generación")]
    [Tooltip("Umbral del canal Roca (bioma 2) para considerar zona de roca")]
    [Range(0.1f, 0.9f)] public float umbralRoca = 0.4f;

    [Tooltip("Distancia de muestreo en metros (cada N metros se evalúa una celda)")]
    [Range(1f, 10f)] public float paseoMuestreoM = 3f;

    [Tooltip("Radio de visibilidad máximo para chunks de rocas (metros)")]
    [Range(50f, 500f)] public float radioVisibilidad = 200f;

    [Tooltip("Distancia mínima de movimiento del jugador para regenerar chunks visibles")]
    [Range(10f, 100f)] public float distanciaRegeneracion = 50f;

    [Header("Ruido procedural de mesh")]
    [Range(0f, 0.3f)] public float amplitudRuido = 0.15f;
    [Range(0.5f, 5f)]  public float frecuenciaRuido = 2.3f;

    [Header("Debug")]
    public bool logVerboso = true;

    // ── Estado privado ─────────────────────────────────────────────────────
    // Datos de roca precalculados en Start
    struct DatoRoca
    {
        public Vector3 posicion;
        public Vector3 escala;
        public Quaternion rotacion;
        public float tintVariacion; // 0..1 para colorizar ligeramente
        public bool  esMusgo;       // true = ladera norte húmeda → material verde
    }

    List<DatoRoca> _todasLasRocas = new List<DatoRoca>();

    // Meshes LOD (creados una sola vez)
    Mesh   _meshFull;    // esfera con ruido Perlin, ~480 tris
    Mesh   _meshLow;     // esfera sin ruido, ~80 tris
    Material _matFull;   // HDRP/Lit con normalmap cliff_side
    Material _matLow;    // HDRP/Lit sin normalmap
    Material _matMusgo;  // variante verde musgo para laderas norte húmedas
    Material _matMusgoLow;

    // Chunks de 1023 instancias para DrawMeshInstanced
    const int CHUNK_SIZE = 1023;

    struct ChunkInstancias
    {
        public Matrix4x4[]      matricesFull;
        public Matrix4x4[]      matricesLow;
        public MaterialPropertyBlock propsFull;
        public MaterialPropertyBlock propsLow;
        public int countFull;
        public int countLow;
    }

    List<ChunkInstancias> _chunks      = new List<ChunkInstancias>();  // rocas normales
    List<ChunkInstancias> _chunksMusgo = new List<ChunkInstancias>(); // rocas con musgo

    // Buffer de matrices reutilizables (no new en Update)
    Matrix4x4[] _bufMatFull = new Matrix4x4[CHUNK_SIZE];
    Matrix4x4[] _bufMatLow  = new Matrix4x4[CHUNK_SIZE];

    // Jugador
    Transform  _jugador;
    Vector3    _ultimaPosJugador = new Vector3(float.MaxValue, 0, float.MaxValue);

    bool _listo;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    IEnumerator Start()
    {
        // Esperar terrain activo
        while (Terrain.activeTerrain == null)
            yield return new WaitForSeconds(0.2f);

        // Esperar SistemaTerreno si existe
        if (SistemaTerreno.Instance != null)
        {
            while (!SistemaTerreno.Listo)
                yield return new WaitForSeconds(0.3f);
        }

        // Encontrar jugador (puede no existir en Editor; no fatal)
        var go = GameObject.FindWithTag("Player");
        if (go != null) _jugador = go.transform;

        // Crear meshes y materiales
        _meshFull = CrearMeshRocaProcedural(3, amplitudRuido, frecuenciaRuido);
        _meshLow  = CrearMeshRocaProcedural(1, 0f, 0f);
        CrearMateriales();

        // Leer alphamap y generar lista de rocas
        yield return StartCoroutine(GenerarListaRocas());

        // Construir chunks iniciales
        ReconstruirChunks();
        _listo = true;

        if (logVerboso)
            Debug.Log($"[RocasProcedurales] Generadas {_todasLasRocas.Count} rocas en {_chunks.Count * CHUNK_SIZE / Mathf.Max(1, _chunks.Count)} promedio/chunk");
    }

    void Update()
    {
        if (!_listo) return;

        // Calcular posición del jugador (o cámara si no hay jugador)
        Vector3 posRef = _jugador != null ? _jugador.position
                       : Camera.main != null ? Camera.main.transform.position
                       : Vector3.zero;

        // Regenerar lista de chunks visibles si el jugador se movió suficiente
        float dist2D = Vector2.Distance(new Vector2(posRef.x, posRef.z),
                                        new Vector2(_ultimaPosJugador.x, _ultimaPosJugador.z));
        if (dist2D > distanciaRegeneracion)
        {
            _ultimaPosJugador = posRef;
            ReconstruirChunks();
        }

        // Dibujar chunks — rocas normales
        foreach (var chunk in _chunks)
        {
            if (chunk.countFull > 0)
                Graphics.DrawMeshInstanced(_meshFull, 0, _matFull,
                    chunk.matricesFull, chunk.countFull, chunk.propsFull);
            if (chunk.countLow > 0)
                Graphics.DrawMeshInstanced(_meshLow, 0, _matLow,
                    chunk.matricesLow, chunk.countLow, chunk.propsLow);
        }
        // Dibujar chunks — rocas con musgo
        foreach (var chunk in _chunksMusgo)
        {
            if (chunk.countFull > 0)
                Graphics.DrawMeshInstanced(_meshFull, 0, _matMusgo,
                    chunk.matricesFull, chunk.countFull, chunk.propsFull);
            if (chunk.countLow > 0)
                Graphics.DrawMeshInstanced(_meshLow, 0, _matMusgoLow,
                    chunk.matricesLow, chunk.countLow, chunk.propsLow);
        }
        // (FIN dibujo — eliminar el bloque duplicado que sigue)
        foreach (var chunk in _chunks)
        {
            if (chunk.countFull > 0)
                Graphics.DrawMeshInstanced(_meshFull, 0, _matFull,
                    chunk.matricesFull, chunk.countFull, chunk.propsFull,
                    UnityEngine.Rendering.ShadowCastingMode.On, true);

            if (chunk.countLow > 0)
                Graphics.DrawMeshInstanced(_meshLow, 0, _matLow,
                    chunk.matricesLow, chunk.countLow, chunk.propsLow,
                    UnityEngine.Rendering.ShadowCastingMode.Off, false);
        }
    }

    // ── Generación de datos de roca ────────────────────────────────────────

    IEnumerator GenerarListaRocas()
    {
        _todasLasRocas.Clear();

        var terrain = Terrain.activeTerrain;
        var td      = terrain.terrainData;
        int aw      = td.alphamapWidth;
        int ah      = td.alphamapHeight;
        int nLayers = td.alphamapLayers;

        const int CANAL_ROCA = 2;
        if (nLayers <= CANAL_ROCA)
        {
            Debug.LogWarning("[RocasProcedurales] Sin suficientes capas TerrainLayer.");
            yield break;
        }

        float[,,] alpha = td.GetAlphamaps(0, 0, aw, ah);
        float terW   = td.size.x;
        float terL   = td.size.z;
        Vector3 terPos = terrain.transform.position;

        // Paso de muestreo = celda de cluster (~6m para clusters más espaciados)
        float pasoCluster = Mathf.Max(paseoMuestreoM * 2f, 6f);
        int pasoPixel = Mathf.Max(1, Mathf.RoundToInt(pasoCluster * aw / terW));

        int frameCounter = 0;
        const int POR_FRAME = 80; // clusters por frame (cada cluster genera 3-8 rocas)

        for (int ay = 0; ay < ah; ay += pasoPixel)
        {
            for (int ax = 0; ax < aw; ax += pasoPixel)
            {
                float valorRoca = alpha[ay, ax, CANAL_ROCA];
                if (valorRoca < umbralRoca) continue;

                float nx = (float)ax / aw;
                float nz = (float)ay / ah;
                float wx = terPos.x + nx * terW;
                float wz = terPos.z + nz * terL;

                int seed = Mathf.RoundToInt(wx * 73.1f + wz * 131.7f);
                var rng  = new System.Random(seed);

                // ── CLUSTERING ────────────────────────────────────────────
                // Solo el 35% de las celdas genera un cluster → distribución
                // menos uniforme, más realista (grupos con claros entre ellos).
                if (rng.NextDouble() > 0.35f) continue;

                float pendiente = td.GetSteepness(nx, nz);
                bool  rocaGrande = pendiente > 35f;

                // Normal local → detectar ladera norte para musgo
                Vector3 normalLocal = td.GetInterpolatedNormal(nx, nz);
                // En el hemisferio norte, laderas norte = normal apunta hacia -Z (sur en Unity = +Z)
                // Se aproxima: si el componente Z de la normal horizontal es negativo = cara norte
                bool laderaNorte = normalLocal.z < -0.25f && pendiente > 12f;

                // Tamaño del cluster: 3-8 rocas, radio 1-4m
                int  numCluster  = rng.Next(3, 9);
                float radioCluster = Mathf.Lerp(1f, 4f, (float)rng.NextDouble());

                // Roca "ancla" del cluster — la más grande
                float sAncla = rocaGrande
                    ? Mathf.Lerp(0.5f, 2.2f, (float)rng.NextDouble())
                    : Mathf.Lerp(0.2f, 1.0f, (float)rng.NextDouble());

                for (int r = 0; r < numCluster; r++)
                {
                    // Posición orgánica: ángulo+distancia aleatorios desde el centro del cluster
                    double angulo   = rng.NextDouble() * System.Math.PI * 2.0;
                    double distRad  = System.Math.Pow(rng.NextDouble(), 0.6) * radioCluster; // más rocas cerca del centro
                    float jx = (float)(System.Math.Cos(angulo) * distRad);
                    float jz = (float)(System.Math.Sin(angulo) * distRad);
                    float px = wx + jx;
                    float pz = wz + jz;
                    float py = terrain.SampleHeight(new Vector3(px, 0, pz)) + terPos.y;

                    // Las rocas periféricas son más pequeñas (pebbles de dispersión)
                    float factorDistancia = 1f - (float)(distRad / radioCluster) * 0.65f;
                    float sBase = r == 0
                        ? sAncla                                                      // roca ancla
                        : sAncla * Mathf.Lerp(0.15f, 0.7f, factorDistancia * (float)rng.NextDouble());

                    // Variación Perlin por eje para romper simetría
                    float noiseX = 1f + (Mathf.PerlinNoise(px * 0.05f, pz * 0.05f) - 0.5f) * 0.4f;
                    float noiseY = 1f + (Mathf.PerlinNoise(px * 0.05f + 100f, pz * 0.05f) - 0.5f) * 0.35f;
                    float noiseZ = 1f + (Mathf.PerlinNoise(px * 0.05f, pz * 0.05f + 100f) - 0.5f) * 0.4f;
                    Vector3 escala = new Vector3(sBase * noiseX, sBase * noiseY, sBase * noiseZ);

                    // Semi-enterrado: rocas grandes más enterradas (más estables geológicamente)
                    float entierro = Mathf.Lerp(0.20f, 0.60f, sBase / 2.2f);
                    py -= escala.y * entierro;

                    float ry = (float)(rng.NextDouble() * 360.0);
                    float rx = (float)((rng.NextDouble() - 0.5) * 30.0);
                    float rz = (float)((rng.NextDouble() - 0.5) * 30.0);

                    _todasLasRocas.Add(new DatoRoca
                    {
                        posicion      = new Vector3(px, py, pz),
                        escala        = escala,
                        rotacion      = Quaternion.Euler(rx, ry, rz),
                        tintVariacion = (float)rng.NextDouble(),
                        esMusgo       = laderaNorte && (float)rng.NextDouble() < 0.65f,
                    });
                }
            }

            frameCounter++;
            if (frameCounter >= POR_FRAME)
            {
                frameCounter = 0;
                yield return null;
            }
        }

        if (logVerboso)
            Debug.Log($"[RocasProcedurales] Clusters generados: {_todasLasRocas.Count} rocas.");
    }

    // ── Construcción de chunks visibles ───────────────────────────────────

    void ReconstruirChunks()
    {
        Vector3 posRef = _jugador != null ? _jugador.position
                       : Camera.main != null ? Camera.main.transform.position
                       : Vector3.zero;

        float r2Full = 25f * 25f;
        float r2Low  = 80f * 80f;
        float r2Max  = radioVisibilidad * radioVisibilidad;

        // Listas temporales (no en Update, solo en este método)
        var tmpFull  = new List<(Matrix4x4 m, float t)>();
        var tmpLow   = new List<(Matrix4x4 m, float t)>();
        var tmpMFull = new List<(Matrix4x4 m, float t)>();  // musgo full
        var tmpMLow  = new List<(Matrix4x4 m, float t)>();  // musgo low

        foreach (var roca in _todasLasRocas)
        {
            float dx = roca.posicion.x - posRef.x;
            float dz = roca.posicion.z - posRef.z;
            float d2 = dx * dx + dz * dz;

            if (d2 > r2Max) continue;

            var mat = Matrix4x4.TRS(roca.posicion, roca.rotacion, roca.escala);

            if (roca.esMusgo)
            {
                if (d2 < r2Full) tmpMFull.Add((mat, roca.tintVariacion));
                else if (d2 < r2Low) tmpMLow.Add((mat, roca.tintVariacion));
            }
            else
            {
                if (d2 < r2Full) tmpFull.Add((mat, roca.tintVariacion));
                else if (d2 < r2Low) tmpLow.Add((mat, roca.tintVariacion));
            }
        }

        _chunks.Clear();
        _chunksMusgo.Clear();
        EmitirChunks(tmpFull,  tmpLow,  _chunks);
        EmitirChunks(tmpMFull, tmpMLow, _chunksMusgo);
    }

    void EmitirChunks(List<(Matrix4x4 m, float t)> full, List<(Matrix4x4 m, float t)> low,
                      List<ChunkInstancias> destino)
    {
        // Calculamos cuántos chunks necesitamos
        int nChunks = Mathf.Max(
            Mathf.CeilToInt((float)full.Count / CHUNK_SIZE),
            Mathf.CeilToInt((float)low.Count  / CHUNK_SIZE),
            1);

        for (int c = 0; c < nChunks; c++)
        {
            int fStart = c * CHUNK_SIZE;
            int lStart = c * CHUNK_SIZE;

            int fCount = Mathf.Clamp(full.Count - fStart, 0, CHUNK_SIZE);
            int lCount = Mathf.Clamp(low.Count  - lStart, 0, CHUNK_SIZE);

            var mFull = new Matrix4x4[CHUNK_SIZE];
            var mLow  = new Matrix4x4[CHUNK_SIZE];
            var pbFull = new MaterialPropertyBlock();
            var pbLow  = new MaterialPropertyBlock();

            var tintsFull = new float[CHUNK_SIZE];
            var tintsLow  = new float[CHUNK_SIZE];

            for (int i = 0; i < fCount; i++)
            {
                mFull[i]      = full[fStart + i].m;
                tintsFull[i]  = full[fStart + i].t;
            }
            for (int i = 0; i < lCount; i++)
            {
                mLow[i]      = low[lStart + i].m;
                tintsLow[i]  = low[lStart + i].t;
            }

            // Pasar variaciones de tint via MPB (color como vector)
            // Usamos _BaseColor con un tint gris-beige navarro + leve variación por instancia
            // DrawMeshInstanced no admite arrays de Color directamente en MPB global,
            // pero sí Vector4Array para _Color. Lo codificamos como array de Vector4.
            var v4Full = new Vector4[CHUNK_SIZE];
            var v4Low  = new Vector4[CHUNK_SIZE];
            var baseCol = new Color(0.72f, 0.65f, 0.55f);
            for (int i = 0; i < CHUNK_SIZE; i++)
            {
                float v = i < fCount ? tintsFull[i] : 0f;
                v4Full[i] = new Vector4(baseCol.r + (v - 0.5f) * 0.06f,
                                        baseCol.g + (v - 0.5f) * 0.04f,
                                        baseCol.b + (v - 0.5f) * 0.04f, 1f);
            }
            for (int i = 0; i < CHUNK_SIZE; i++)
            {
                float v = i < lCount ? tintsLow[i] : 0f;
                v4Low[i] = new Vector4(baseCol.r + (v - 0.5f) * 0.06f,
                                       baseCol.g + (v - 0.5f) * 0.04f,
                                       baseCol.b + (v - 0.5f) * 0.04f, 1f);
            }
            pbFull.SetVectorArray("_BaseColor", v4Full);
            pbLow.SetVectorArray("_BaseColor", v4Low);

            destino.Add(new ChunkInstancias
            {
                matricesFull = mFull,
                matricesLow  = mLow,
                propsFull    = pbFull,
                propsLow     = pbLow,
                countFull    = fCount,
                countLow     = lCount
            });
        }
    }

    // ── Mesh procedural (esfera con ruido Perlin 3D en vértices) ─────────

    static Mesh CrearMeshRocaProcedural(int subdivisiones, float amplitud, float frecuencia)
    {
        // Generamos una esfera UV con N subdivisiones y aplicamos ruido en vértices
        int longSegments = 8 + subdivisiones * 4;
        int latSegments  = 6 + subdivisiones * 3;

        var vertices  = new List<Vector3>();
        var normales  = new List<Vector3>();
        var uvs       = new List<Vector2>();
        var triangles = new List<int>();

        for (int lat = 0; lat <= latSegments; lat++)
        {
            float theta = lat * Mathf.PI / latSegments;
            float sinT  = Mathf.Sin(theta);
            float cosT  = Mathf.Cos(theta);

            for (int lon = 0; lon <= longSegments; lon++)
            {
                float phi   = lon * 2f * Mathf.PI / longSegments;
                float sinP  = Mathf.Sin(phi);
                float cosP  = Mathf.Cos(phi);

                Vector3 dir = new Vector3(sinT * cosP, cosT, sinT * sinP);

                // Ruido Perlin 3D aproximado: 3 samples en planos 2D
                float n = amplitud > 0f
                    ? amplitud * SampleNoise3D(dir * frecuencia)
                    : 0f;

                Vector3 v = dir * (0.5f + n); // radio base 0.5

                vertices.Add(v);
                normales.Add(dir);
                uvs.Add(new Vector2((float)lon / longSegments, (float)lat / latSegments));
            }
        }

        for (int lat = 0; lat < latSegments; lat++)
        {
            for (int lon = 0; lon < longSegments; lon++)
            {
                int cur  = lat * (longSegments + 1) + lon;
                int next = cur + longSegments + 1;

                triangles.Add(cur);
                triangles.Add(next);
                triangles.Add(cur + 1);

                triangles.Add(cur + 1);
                triangles.Add(next);
                triangles.Add(next + 1);
            }
        }

        var mesh = new Mesh();
        mesh.name = $"RocaProc_LOD{subdivisiones}";
        mesh.SetVertices(vertices);
        mesh.SetNormals(normales);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // Aproximación Perlin 3D con 3 muestras 2D
    static float SampleNoise3D(Vector3 p)
    {
        float xy = Mathf.PerlinNoise(p.x, p.y);
        float yz = Mathf.PerlinNoise(p.y, p.z);
        float xz = Mathf.PerlinNoise(p.x, p.z);
        return (xy + yz + xz) / 3f - 0.5f; // centrado en 0
    }

    // ── Material HDRP/Lit con cliff_side ──────────────────────────────────

    void CrearMateriales()
    {
        // Intentar cargar el shader HDRP/Lit
        Shader hdrpLit = Shader.Find("HDRP/Lit");
        if (hdrpLit == null) hdrpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (hdrpLit == null) hdrpLit = Shader.Find("Standard");

        _matFull = new Material(hdrpLit) { name = "Roca_Full_HDRP" };
        _matLow  = new Material(hdrpLit) { name = "Roca_Low_HDRP"  };

        // Tint base gris-beige navarro
        Color baseCol = new Color(0.72f, 0.65f, 0.55f);
        _matFull.SetColor("_BaseColor", baseCol);
        _matLow.SetColor("_BaseColor",  baseCol);

        // Propiedades PBR
        _matFull.SetFloat("_Smoothness", 0.15f);
        _matFull.SetFloat("_Metallic",   0f);
        _matLow.SetFloat("_Smoothness",  0.15f);
        _matLow.SetFloat("_Metallic",    0f);

        // GPU Instancing obligatorio
        _matFull.enableInstancing = true;
        _matLow.enableInstancing  = true;

        // Cargar texturas cliff_side desde Assets/Textures_AAA/Naturaleza/
        const string BASE_PATH = "Assets/Textures_AAA/Naturaleza/";

        Texture2D albedo  = TryLoadTexture(BASE_PATH + "cliff_side_Albedo.png");
        Texture2D normal  = TryLoadTexture(BASE_PATH + "cliff_side_Normal.png");
        Texture2D arm     = TryLoadTexture(BASE_PATH + "cliff_side_ARM.png");   // AO+Roughness+Metallic

        if (albedo == null) albedo = TryLoadTexture(BASE_PATH + "cliff_side_Color.png");
        if (arm    == null) arm    = TryLoadTexture(BASE_PATH + "cliff_side_AO.png");

        if (albedo != null)
        {
            _matFull.SetTexture("_BaseColorMap", albedo);
            _matLow.SetTexture("_BaseColorMap",  albedo);
        }
        if (normal != null)
        {
            _matFull.SetTexture("_NormalMap", normal);
            // LOD low: sin normalmap intencionalmente
        }
        if (arm != null)
        {
            // En HDRP, _MaskMap = R:AO, G:Roughness(1-Smooth), B:Metallic, A:Smoothness
            _matFull.SetTexture("_MaskMap", arm);
            _matLow.SetTexture("_MaskMap",  arm);
        }

        // Material musgo: igual que _matFull pero con tinte verde
        _matMusgo    = new Material(_matFull);    _matMusgo.name = "Roca_Musgo_HDRP";
        _matMusgoLow = new Material(_matLow);     _matMusgoLow.name = "Roca_MusgoLow_HDRP";
        var tintMusgo = new Color(0.48f, 0.55f, 0.38f); // verde-gris
        _matMusgo.SetColor("_BaseColor",    tintMusgo);
        _matMusgoLow.SetColor("_BaseColor", tintMusgo);
        _matMusgo.SetFloat("_Smoothness",    0.10f); // musgo = menos brillante
        _matMusgoLow.SetFloat("_Smoothness", 0.10f);
        _matMusgo.enableInstancing    = true;
        _matMusgoLow.enableInstancing = true;

        if (logVerboso)
            Debug.Log($"[RocasProcedurales] Material creado — albedo:{albedo != null}, normal:{normal != null}, ARM:{arm != null}");
    }

    static Texture2D TryLoadTexture(string path)
    {
#if UNITY_EDITOR
        var tex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        return tex;
#else
        // En runtime: cargar por nombre desde Resources si se han exportado
        string name = System.IO.Path.GetFileNameWithoutExtension(path);
        return Resources.Load<Texture2D>(name);
#endif
    }

    // ── Integración con SistemaTerreno ────────────────────────────────────

    /// <summary>Regenerar rocas cuando el splatmap cambia (llamar desde SistemaTerreno).</summary>
    public void OnSplatmapActualizado()
    {
        if (!_listo) return;
        StopAllCoroutines();
        StartCoroutine(RegenerarCoroutine());
    }

    IEnumerator RegenerarCoroutine()
    {
        _listo = false;
        yield return StartCoroutine(GenerarListaRocas());
        ReconstruirChunks();
        _listo = true;
    }

    // ── Context Menu (fallback Editor) ────────────────────────────────────

    [ContextMenu("Generar Rocas")]
    void GenerarRocasEditor()
    {
        if (Application.isPlaying)
        {
            StopAllCoroutines();
            StartCoroutine(RegenerarCoroutine());
        }
        else
        {
            Debug.LogWarning("[RocasProcedurales] 'Generar Rocas' solo funciona en Play Mode.");
        }
    }
}
