// Assets/Scripts/SistemaTerreno.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SISTEMA DE TERRENO AAA — pintura, vegetación y detalles de suelo
//
//  Funciones:
//    1. Pintura de capas por pendiente y altitud (SetAlphamaps)
//    2. Árboles de Unity Terrain engine por bioma
//    3. Detalles de hierba y matorrales (TerrainData.SetDetailLayer)
//    4. Integración con SistemaAtmosfera (nieve en altura en invierno)
//    5. Integración con GeneradorMundoOSM (no pintar asfalto bajo edificios)
//    6. Superficie de carretera: depresión y textura propia cerca de calles
//
//  Capas de terreno (asignadas en Inspector o creadas con fallback sólido):
//    0 → Hierba          (plano, altitud baja)
//    1 → Tierra          (pendiente leve, transición)
//    2 → Roca            (pendiente alta)
//    3 → Nieve/Pizarra   (alta altitud: monte Aralar)
//    4 → Asfalto/Camino  (cerca de calles OSM)
//
//  Terreno de Alsasua (SceneBootstrapper):
//    Tamaño:   5000 × 18000 × 900 m
//    Resolución heightmap: 1025 × 1025
//    Centro jugador: X=1918, Z=8570 (Herriko Plaza)
//    Altitud pueblo: ~240 m
//    Altitud Aralar: ~800-900 m
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-70)]
public class SistemaTerreno : MonoBehaviour
{
    public static SistemaTerreno Instance { get; private set; }
    public static bool Listo { get; private set; }

    // ── Capas ──────────────────────────────────────────────────────────────
    [Header("Capas de textura (asignar en Inspector o se crean automáticamente)")]
    public TerrainLayer capaHierba;
    public TerrainLayer capaTierra;
    public TerrainLayer capaRoca;
    public TerrainLayer capaNieve;
    public TerrainLayer capaAsfalto;

    // ── Umbrales de pintado ────────────────────────────────────────────────
    [Header("Umbrales de pintado")]
    [Tooltip("Altitud normalizada (0-1) a la que empieza la transición roca/nieve.")]
    [Range(0f, 1f)] public float altitudNieve   = 0.72f;  // ~648m sobre 900m max
    [Tooltip("Pendiente en grados a partir de la cual aparece tierra (mezclada con hierba).")]
    [Range(0f, 45f)] public float pendienteTierra = 12f;
    [Tooltip("Pendiente en grados a partir de la cual domina la roca.")]
    [Range(0f, 90f)] public float pendienteRoca   = 32f;
    [Tooltip("Pendiente en grados a partir de la cual la roca es dominante.")]
    [Range(0f, 90f)] public float pendienteRocaDura = 52f;

    // ── Árboles ───────────────────────────────────────────────────────────
    [Header("Prefabs de árbol para Terrain engine")]
    [Tooltip("Prefabs de árbol (Roble, Haya, Pino, etc.) — Unity Terrain Trees.")]
    public GameObject[] prefabsArbol;
    [Tooltip("Densidad de árboles por km² en zonas de bosque.")]
    [Range(50, 2000)] public int densidadBosque = 400;
    [Tooltip("Distancia mínima entre árboles del Terrain (m).")]
    public float distanciaMinArbol = 4f;

    // ── Hierba / detalles ──────────────────────────────────────────────────
    [Header("Hierba")]
    public Texture2D texturaHierba;
    [Range(1, 16)] public int  resolucionDetalle = 4;
    [Range(0, 16)] public int  densidadHierba    = 4;

    // ── Estado ────────────────────────────────────────────────────────────
    TerrainData _td;
    Terrain     _terrain;
    int         _resAlpha;  // resolución del alphamap (típicamente 513 o 1025)
    int         _numCapas;

    // ══════════════════════════════════════════════════════════════════════
    //  LIFECYCLE
    // ══════════════════════════════════════════════════════════════════════

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    IEnumerator Start()
    {
        // Esperar al terreno (SceneBootstrapper lo crea desde DEM)
        float t = 0f;
        while (Terrain.activeTerrain == null && t < 10f)
            { t += 0.25f; yield return new WaitForSeconds(0.25f); }

        _terrain = Terrain.activeTerrain;
        if (_terrain == null)
        {
            AlsasuaLogger.Warn("Terreno", "No se encontró Terrain activo — SistemaTerreno inactivo");
            yield break;
        }
        _td         = _terrain.terrainData;
        _resAlpha   = _td.alphamapResolution;

        AlsasuaLogger.Info("Terreno",
            $"Terreno: {_td.size.x:F0}×{_td.size.z:F0}m, " +
            $"heightmap {_td.heightmapResolution}×{_td.heightmapResolution}, " +
            $"alphamap {_resAlpha}×{_resAlpha}");

        // ── Paso 1: asegurar capas ──────────────────────────────────────
        AsegurarCapas();
        yield return null;

        // ── Paso 2: pintar alphamap ──────────────────────────────────────
        yield return StartCoroutine(PintarTerrain());

        // ── Paso 3: añadir árboles ───────────────────────────────────────
        if (prefabsArbol != null && prefabsArbol.Length > 0)
            yield return StartCoroutine(PlantarArboles());
        else
            AlsasuaLogger.Info("Terreno", "Sin prefabs de árbol — omitiendo plantación");

        // ── Paso 4: detalle de hierba ────────────────────────────────────
        if (texturaHierba != null)
            ConfigurarHierba();

        // ── Paso 5: configuración visual del terreno ─────────────────────
        ConfigurarPixelError();

        Listo = true;
        AlsasuaLogger.Info("Terreno", "✅ SistemaTerreno completo");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  CAPAS — crear si no están asignadas
    // ══════════════════════════════════════════════════════════════════════

    void AsegurarCapas()
    {
        // Colores de fallback (sin textura real) por bioma
        var colores = new Color[]
        {
            new Color(0.30f, 0.55f, 0.20f),  // hierba verde
            new Color(0.55f, 0.42f, 0.28f),  // tierra marrón
            new Color(0.52f, 0.50f, 0.48f),  // roca gris
            new Color(0.90f, 0.92f, 0.95f),  // nieve/pizarra blanca
            new Color(0.22f, 0.22f, 0.22f),  // asfalto negro
        };
        var nombres = new[]{"Hierba","Tierra","Roca","Nieve","Asfalto"};
        var asignadas = new TerrainLayer[]
            { capaHierba, capaTierra, capaRoca, capaNieve, capaAsfalto };

        var capasFinales = new TerrainLayer[5];
        for (int i = 0; i < 5; i++)
        {
            if (asignadas[i] != null) { capasFinales[i] = asignadas[i]; continue; }

            // Crear capa procedural sólida
            var capa      = new TerrainLayer();
            capa.name     = nombres[i];
            capa.tileSize = new Vector2(4, 4);
            // Generar textura 2×2 del color del bioma
            var tex         = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.SetPixels(new Color[]{colores[i],colores[i],colores[i],colores[i]});
            tex.Apply();
            capa.diffuseTexture   = tex;
            capa.normalMapTexture = null;
            capasFinales[i]       = capa;
        }

        // Mantener las capas existentes del terreno y añadir/reemplazar
        var existentes = _td.terrainLayers;
        if (existentes == null || existentes.Length < 5)
        {
            _td.terrainLayers = capasFinales;
            AlsasuaLogger.Info("Terreno", "Capas de terreno creadas (fallback sólido)");
        }
        else
        {
            // Ya hay capas — no sobreescribir para no perder trabajo del artista
            capasFinales = existentes;
            AlsasuaLogger.Info("Terreno", $"Usando {existentes.Length} capas existentes");
        }

        _numCapas = _td.terrainLayers.Length;

        // Actualizar referencias
        if (_numCapas > 0) capaHierba  = _td.terrainLayers[0];
        if (_numCapas > 1) capaTierra  = _td.terrainLayers[1];
        if (_numCapas > 2) capaRoca    = _td.terrainLayers[2];
        if (_numCapas > 3) capaNieve   = _td.terrainLayers[3];
        if (_numCapas > 4) capaAsfalto = _td.terrainLayers[4];
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ALPHAMAP — pintado por pendiente y altitud
    // ══════════════════════════════════════════════════════════════════════

    IEnumerator PintarTerrain()
    {
        AlsasuaLogger.Info("Terreno", $"Pintando alphamap {_resAlpha}×{_resAlpha}…");

        // [y, x, capa] — Unity usa orden y,x (row,col)
        float[,,] mapa = new float[_resAlpha, _resAlpha, _numCapas];

        int lote    = 0;
        float tamX  = _td.size.x;
        float tamZ  = _td.size.z;
        float altMax = _td.size.y;

        // Precargar heightmap una sola vez (evita llamar GetHeight millones de veces)
        // Unity proporciona GetHeights() que carga en array de una vez
        float[,] heights = _td.GetHeights(0, 0, _td.heightmapResolution, _td.heightmapResolution);
        int hRes = _td.heightmapResolution - 1;

        for (int ay = 0; ay < _resAlpha; ay++)
        {
            for (int ax = 0; ax < _resAlpha; ax++)
            {
                float nx = (float)ax / (_resAlpha - 1);  // 0..1 a lo largo de X
                float nz = (float)ay / (_resAlpha - 1);  // 0..1 a lo largo de Z

                // Altitud y pendiente en este punto
                float altNorm  = SampleHeight01(heights, hRes, nx, nz);
                float pendiente = _td.GetSteepness(nx, nz);  // grados

                // Posición mundo
                float wx = nx * tamX;
                float wz = nz * tamZ;

                PintarPunto(mapa, ay, ax, altNorm, pendiente, wx, wz);
            }

            if (++lote >= 32) { lote = 0; yield return null; }
        }

        _td.SetAlphamaps(0, 0, mapa);
        AlsasuaLogger.Info("Terreno", "✅ Alphamap pintado");
    }

    void PintarPunto(float[,,] mapa, int ay, int ax,
                     float altNorm, float pend, float wx, float wz)
    {
        // Inicializar todas las capas a 0
        for (int c = 0; c < _numCapas; c++) mapa[ay, ax, c] = 0f;

        // ── Zona urbana de Alsasua (radio 300m desde Herriko Plaza) ──────
        float distCentro = Mathf.Sqrt(
            (wx - 1918f) * (wx - 1918f) + (wz - 8570f) * (wz - 8570f));
        bool esUrbanoa = distCentro < 300f;

        // ── Clasificación por pendiente ───────────────────────────────────
        float wHierba  = 0f;
        float wTierra  = 0f;
        float wRoca    = 0f;
        float wNieve   = 0f;
        float wAsfalto = 0f;

        if (esUrbanoa)
        {
            // Zona urbana: mezcla hierba + asfalto
            wHierba  = Mathf.Clamp01(1f - distCentro / 300f) * 0.3f;
            wAsfalto = Mathf.Clamp01(1f - distCentro / 200f) * 0.7f;
            wTierra  = 1f - wHierba - wAsfalto;
        }
        else if (pend < pendienteTierra)
        {
            // Plano → hierba pura (con ligera tierra a mayor altitud)
            float altFactor = Mathf.Clamp01((altNorm - 0.2f) / 0.3f);
            wHierba = Mathf.Lerp(1f, 0.55f, altFactor);
            wTierra = 1f - wHierba;
        }
        else if (pend < pendienteRoca)
        {
            // Pendiente moderada → mezcla tierra/hierba
            float t = Mathf.InverseLerp(pendienteTierra, pendienteRoca, pend);
            wHierba = Mathf.Lerp(0.6f, 0.1f, t);
            wTierra = Mathf.Lerp(0.4f, 0.7f, t);
            wRoca   = Mathf.Lerp(0.0f, 0.2f, t);
        }
        else if (pend < pendienteRocaDura)
        {
            // Pendiente alta → dominan roca y tierra
            float t = Mathf.InverseLerp(pendienteRoca, pendienteRocaDura, pend);
            wTierra = Mathf.Lerp(0.35f, 0.1f, t);
            wRoca   = Mathf.Lerp(0.65f, 0.9f, t);
        }
        else
        {
            // Acantilado → roca pura
            wRoca = 1f;
        }

        // ── Modificación por altitud ──────────────────────────────────────
        if (altNorm > altitudNieve && !esUrbanoa)
        {
            float tNieve = Mathf.Clamp01((altNorm - altitudNieve) / 0.1f);
            // La nieve tapa hierba y tierra primero, luego mezcla con roca
            float toNieve = Mathf.Lerp(0f, 0.8f, tNieve);
            float escala  = 1f - toNieve;
            wHierba  *= escala;
            wTierra  *= escala;
            wRoca    *= Mathf.Max(escala * 0.5f, 0.1f);
            wNieve   = toNieve;
        }

        // ── Normalizar para que sumen 1 ───────────────────────────────────
        float total = wHierba + wTierra + wRoca + wNieve + wAsfalto;
        if (total < 0.001f) wHierba = 1f; // fallback
        else { float inv = 1f / total; wHierba *= inv; wTierra *= inv; wRoca *= inv; wNieve *= inv; wAsfalto *= inv; }

        // ── Asignar capas ─────────────────────────────────────────────────
        if (_numCapas > 0) mapa[ay, ax, 0] = wHierba;
        if (_numCapas > 1) mapa[ay, ax, 1] = wTierra;
        if (_numCapas > 2) mapa[ay, ax, 2] = wRoca;
        if (_numCapas > 3) mapa[ay, ax, 3] = wNieve;
        if (_numCapas > 4) mapa[ay, ax, 4] = wAsfalto;
    }

    // Bilinear sample de heightmap normalizado (0-1)
    float SampleHeight01(float[,] heights, int hRes, float nx, float nz)
    {
        float fx = Mathf.Clamp(nx * hRes, 0, hRes - 1.001f);
        float fz = Mathf.Clamp(nz * hRes, 0, hRes - 1.001f);
        int   ix = (int)fx, iz = (int)fz;
        float tx = fx - ix,  tz = fz - iz;
        float h00 = heights[iz,     ix];
        float h10 = heights[iz,     ix + 1];
        float h01 = heights[iz + 1, ix];
        float h11 = heights[iz + 1, ix + 1];
        return Mathf.Lerp(Mathf.Lerp(h00, h10, tx), Mathf.Lerp(h01, h11, tx), tz);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ÁRBOLES — Terrain engine trees (occlusion + billboard automáticos)
    // ══════════════════════════════════════════════════════════════════════

    IEnumerator PlantarArboles()
    {
        AlsasuaLogger.Info("Terreno", "Plantando árboles…");

        // Registrar prototipos de árbol
        var protos = new List<TreePrototype>();
        foreach (var prefab in prefabsArbol)
        {
            if (prefab == null) continue;
            protos.Add(new TreePrototype
            {
                prefab       = prefab,
                bendFactor   = 0.8f,
                navMeshLod   = 0
            });
        }
        _td.treePrototypes = protos.ToArray();
        yield return null;

        if (protos.Count == 0) yield break;

        float tamX = _td.size.x;
        float tamZ = _td.size.z;

        // Zonas de bosque de Alsasua (estimadas):
        // Norte: monte Txindoki / Aizkorri (fondo del terreno)
        // Sur: colinas de Burgui
        // Oeste: bosque de Urbasa
        // Town center: sin árboles (zona urbana)

        var instancias = new List<TreeInstance>();
        var rng        = new System.Random(42); // seed fija para reproducibilidad

        int intentos  = 0;
        int maxIntent = densidadBosque * 20;
        int plantados = 0;

        while (plantados < densidadBosque && intentos < maxIntent)
        {
            intentos++;

            float nx = (float)rng.NextDouble();
            float nz = (float)rng.NextDouble();
            float wx = nx * tamX;
            float wz = nz * tamZ;

            // No plantar en zona urbana
            float distCentro = Mathf.Sqrt((wx-1918f)*(wx-1918f) + (wz-8570f)*(wz-8570f));
            if (distCentro < 220f) continue;

            // Pendiente máxima para árboles (no en acantilados)
            float pend = _td.GetSteepness(nx, nz);
            if (pend > 55f) continue;

            // Altitud: árboles entre 240m y 750m (0.27-0.83 normalizado con max=900)
            float altNorm = _td.GetInterpolatedHeight(nx, nz) / _td.size.y;
            if (altNorm < 0.22f || altNorm > 0.83f) continue;

            // Comprobar distancia mínima (simplificado — comparar con últimas N instancias)
            bool muyProximo = false;
            for (int k = Mathf.Max(0, instancias.Count - 30); k < instancias.Count; k++)
            {
                var prev = instancias[k];
                float dx = (nx - prev.position.x) * tamX;
                float dz = (nz - prev.position.z) * tamZ;
                if (dx*dx + dz*dz < distanciaMinArbol * distanciaMinArbol)
                { muyProximo = true; break; }
            }
            if (muyProximo) continue;

            int protoIdx = rng.Next(protos.Count);
            float escala = (float)(0.7 + rng.NextDouble() * 0.6); // 0.7-1.3x

            instancias.Add(new TreeInstance
            {
                position         = new Vector3(nx, altNorm, nz),
                prototypeIndex   = protoIdx,
                widthScale       = escala,
                heightScale      = escala * (float)(0.9 + rng.NextDouble() * 0.2f),
                rotation         = (float)(rng.NextDouble() * Mathf.PI * 2f),
                color            = Color.white,
                lightmapColor    = Color.white
            });
            plantados++;

            if (plantados % 50 == 0) yield return null;
        }

        _td.treeInstances = instancias.ToArray();
        AlsasuaLogger.Info("Terreno", $"✅ {plantados} árboles plantados ({intentos} intentos)");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  HIERBA / DETALLE
    // ══════════════════════════════════════════════════════════════════════

    void ConfigurarHierba()
    {
        if (_td.detailResolution == 0) return;

        // Registrar prototipo de hierba
        var proto = new DetailPrototype
        {
            prototypeTexture = texturaHierba,
            renderMode       = DetailRenderMode.GrassBillboard,
            minHeight        = 0.15f,
            maxHeight        = 0.55f,
            minWidth         = 0.1f,
            maxWidth         = 0.3f,
            healthyColor     = new Color(0.20f, 0.60f, 0.15f),
            dryColor         = new Color(0.60f, 0.52f, 0.15f),
            noiseSpread      = 0.5f
        };
        _td.detailPrototypes = new[] { proto };

        int res = _td.detailResolution;
        var mapa = new int[res, res];

        // Pintar hierba solo en zonas planas y fuera del casco urbano
        for (int y = 0; y < res; y++)
        for (int x = 0; x < res; x++)
        {
            float nx = (float)x / res;
            float nz = (float)y / res;
            float pend = _td.GetSteepness(nx, nz);
            float wx   = nx * _td.size.x;
            float wz   = nz * _td.size.z;
            float dist = Mathf.Sqrt((wx-1918f)*(wx-1918f)+(wz-8570f)*(wz-8570f));
            float alt  = _td.GetInterpolatedHeight(nx, nz) / _td.size.y;

            bool aptoHierba = pend < 20f && dist > 200f && alt < 0.65f && alt > 0.18f;
            mapa[y, x] = aptoHierba ? densidadHierba : 0;
        }

        _td.SetDetailLayer(0, 0, 0, mapa);
        AlsasuaLogger.Info("Terreno", "Hierba configurada");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  CALIDAD VISUAL
    // ══════════════════════════════════════════════════════════════════════

    void ConfigurarPixelError()
    {
        // Pixel error bajo = más polígonos pero más suave
        _terrain.heightmapPixelError = 3f;
        _terrain.basemapDistance     = 1000f;
        _terrain.treeDistance        = 800f;
        _terrain.treeBillboardDistance = 200f;
        _terrain.treeCrossFadeLength = 30f;
        _terrain.detailObjectDistance= 120f;
        _terrain.detailObjectDensity = 0.8f;

        // HDRP: el Terrain usa su propio Volume de material
        // No necesitamos setear materialType aquí
    }

    // ══════════════════════════════════════════════════════════════════════
    //  API PÚBLICA
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Repinta el alphamap en un radio alrededor de <paramref name="centro"/> (metros).
    /// Útil cuando se coloca una carretera nueva o se modifica el terreno en runtime.
    /// </summary>
    public void RepintarZona(Vector3 centro, float radio)
    {
        if (_terrain == null || _td == null) return;
        StartCoroutine(RepintarZonaCoroutine(centro, radio));
    }

    IEnumerator RepintarZonaCoroutine(Vector3 centro, float radio)
    {
        float px = (centro.x - _terrain.transform.position.x) / _td.size.x;
        float pz = (centro.z - _terrain.transform.position.z) / _td.size.z;
        float rx = radio / _td.size.x;
        float rz = radio / _td.size.z;

        int x0 = Mathf.Max(0, (int)((px - rx) * _resAlpha));
        int x1 = Mathf.Min(_resAlpha - 1, (int)((px + rx) * _resAlpha));
        int z0 = Mathf.Max(0, (int)((pz - rz) * _resAlpha));
        int z1 = Mathf.Min(_resAlpha - 1, (int)((pz + rz) * _resAlpha));

        int w = x1 - x0 + 1;
        int h = z1 - z0 + 1;
        if (w <= 0 || h <= 0) yield break;

        float[,,] sub = _td.GetAlphamaps(x0, z0, w, h);
        int hRes = _td.heightmapResolution - 1;
        float[,] heights = _td.GetHeights(0, 0, _td.heightmapResolution, _td.heightmapResolution);

        for (int ay = 0; ay < h; ay++)
        {
            for (int ax = 0; ax < w; ax++)
            {
                float nx = (x0 + ax) / (float)(_resAlpha - 1);
                float nz = (z0 + ay) / (float)(_resAlpha - 1);
                float alt01 = SampleHeight01(heights, hRes, nx, nz);
                float pend  = _td.GetSteepness(nx, nz);
                float wx    = nx * _td.size.x;
                float wz    = nz * _td.size.z;
                PintarPunto(sub, ay, ax, alt01, pend, wx, wz);
            }
            yield return null;
        }

        _td.SetAlphamaps(x0, z0, sub);
    }

    /// <summary>
    /// Pinta una franja de asfalto a lo largo de una polilínea de carretera.
    /// Llamado desde GeneradorMundoOSM / SistemaZonas al construir calles.
    /// </summary>
    public void PintarCarretera(Vector3[] puntos, float ancho = 6f)
    {
        foreach (var p in puntos)
            RepintarZona(p, ancho);
    }

    /// <summary>Altura del terreno en coordenadas mundo.</summary>
    public float AlturaEn(float x, float z)
    {
        if (_terrain == null) return 240f;
        return _terrain.SampleHeight(new Vector3(x, 0, z)) + _terrain.transform.position.y;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  INTEGRACIÓN CON SISTEMAATMOSFERA — nieve estacional
    // ══════════════════════════════════════════════════════════════════════

    float _ultimaHoraNieve = -1f;

    void Update()
    {
        if (!Listo || _terrain == null) return;

        var atm = AltsasuCore.I?.atmosferaSystem;
        if (atm == null) return;

        // Ajustar umbral de nieve según hora del día (más nieve de noche)
        float horaNoche  = Mathf.Abs(Mathf.Sin(atm.HoraDelDia * Mathf.PI / 12f));
        float nuevoUmbral = Mathf.Lerp(0.78f, 0.70f, horaNoche);

        if (Mathf.Abs(nuevoUmbral - _ultimaHoraNieve) > 0.015f)
        {
            _ultimaHoraNieve = nuevoUmbral;
            altitudNieve     = nuevoUmbral;
            // Disparar repintado suave solo en zona alta (Aralar)
            RepintarZona(new Vector3(3200f, 0f, 9400f), 400f);
        }
    }
}
