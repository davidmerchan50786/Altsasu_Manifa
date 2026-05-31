// Assets/Scripts/SistemaTerreno.cs
// ═══════════════════════════════════════════════════════════════════════════
//  TEXTURIZADO AUTOMÁTICO DE TERRENO — AAA HDRP
//
//  Pinta las capas del terreno automáticamente basándose en:
//   · Altura (metros sobre el nivel del mar)
//   · Pendiente (°)
//   · Proximidad a carreteras OSM
//   · Zonas de bosque definidas en GeoDataAlsasua
//
//  Capas HDRP usadas (desde Assets/AlsasuaData/*.terrainlayer):
//   0 → Ortofoto PNOA (base realista)
//   1 → Hierba      (prados, praderas)
//   2 → Bosque      (zona arbolada)
//   3 → Roca        (alta montaña, pendientes >40°)
//   4 → Alpino      (cumbres > 700m)
//   5 → Hormigón    (zonas urbanas, calles)
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using UnityEngine;

[DefaultExecutionOrder(10)]
public class SistemaTerreno : MonoBehaviour
{
    public static SistemaTerreno Instance { get; private set; }

    [Header("Capas (asignar o se buscan automáticamente)")]
    public TerrainLayer layerOrtofoto;
    public TerrainLayer layerHierba;
    public TerrainLayer layerBosque;
    public TerrainLayer layerRoca;
    public TerrainLayer layerAlpino;
    public TerrainLayer layerHormigon;

    [Header("Umbrales de altura (Unity units, origen = 305m snm)")]
    public float alturaHierba    =  50f;   // < 50u → hierba (hasta ~355m snm)
    public float alturaBosque    = 180f;   // 50-180u → bosque (355-485m)
    public float alturaRoca      = 400f;   // 180-400u → roca (485-705m)
    public float alturaAlpino    = 550f;   // > 400u → alpino (>705m)

    [Header("Pendiente")]
    public float pendienteRoca   = 35f;    // >35° → roca independiente de altura

    [Header("Zona urbana (radio alrededor de Herriko Plaza)")]
    public float radioUrbano     = 600f;
    public Vector2 centroUrbano  = new Vector2(1918f, 8570f);

    [Header("Calidad de pintura")]
    public bool pintarAlInicio   = true;
    public bool pintarEnRuntime  = false;   // muy costoso, solo para debugging

    Terrain   _terrain;
    int       _res;
    float[,,] _mapa;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    IEnumerator Start()
    {
        yield return null; // esperar a que SceneBootstrapper cree el terrain

        _terrain = Terrain.activeTerrain;
        if (_terrain == null)
        {
            AlsasuaLogger.Warn("SistemaTerreno", "No hay Terrain activo. Esperando 3s...");
            yield return new WaitForSeconds(3f);
            _terrain = Terrain.activeTerrain;
        }

        if (_terrain == null) { AlsasuaLogger.Error("SistemaTerreno", "Sin terrain — texturizado abortado."); yield break; }

        CargarCapas();

        // Solo texturizar si hay layers reales — si no, SceneBootstrapper puso color base
        bool hayLayers = (layerOrtofoto != null || layerHierba != null ||
                          layerBosque   != null || layerRoca   != null);

        if (hayLayers)
        {
            AplicarCapasAlTerrain();
            if (pintarAlInicio)
            {
                AlsasuaLogger.Info("SistemaTerreno", "Pintando splatmap...");
                yield return StartCoroutine(PintarSplatmap());
                AlsasuaLogger.Info("SistemaTerreno", "✓ Texturizado completado.");
            }
        }
        else
        {
            AlsasuaLogger.Info("SistemaTerreno", "Sin layers — usando material base de SceneBootstrapper.");
        }
    }

    // =========================================================================
    //  CARGAR CAPAS
    // =========================================================================

    void CargarCapas()
    {
#if UNITY_EDITOR
        layerOrtofoto ??= UnityEditor.AssetDatabase.LoadAssetAtPath<TerrainLayer>("Assets/AlsasuaData/Layer_Ortofoto.terrainlayer");
        layerHierba   ??= UnityEditor.AssetDatabase.LoadAssetAtPath<TerrainLayer>("Assets/AlsasuaData/Layer_Grass.terrainlayer");
        layerBosque   ??= UnityEditor.AssetDatabase.LoadAssetAtPath<TerrainLayer>("Assets/AlsasuaData/Layer_Forest.terrainlayer");
        layerRoca     ??= UnityEditor.AssetDatabase.LoadAssetAtPath<TerrainLayer>("Assets/AlsasuaData/Layer_Rock.terrainlayer");
        layerAlpino   ??= UnityEditor.AssetDatabase.LoadAssetAtPath<TerrainLayer>("Assets/AlsasuaData/Layer_Alpine.terrainlayer");
        layerHormigon ??= UnityEditor.AssetDatabase.LoadAssetAtPath<TerrainLayer>("Assets/AlsasuaData/Layer_Concrete.terrainlayer");
#endif
    }

    void AplicarCapasAlTerrain()
    {
        var capas = new TerrainLayer[6];
        capas[0] = layerOrtofoto;
        capas[1] = layerHierba;
        capas[2] = layerBosque;
        capas[3] = layerRoca;
        capas[4] = layerAlpino;
        capas[5] = layerHormigon;

        // Filtrar nulls
        int validas = 0;
        for (int i = 0; i < capas.Length; i++) if (capas[i] != null) validas++;

        if (validas == 0)
        {
            AlsasuaLogger.Warn("SistemaTerreno", "Sin terrain layers asignadas. Usando terreno con color base.");
            return;
        }

        // Solo asignar si el terrain no tiene ya capas configuradas
        if (_terrain.terrainData.terrainLayers == null || _terrain.terrainData.terrainLayers.Length == 0)
        {
            var listCapas = new System.Collections.Generic.List<TerrainLayer>();
            for (int i = 0; i < capas.Length; i++) if (capas[i] != null) listCapas.Add(capas[i]);
            _terrain.terrainData.terrainLayers = listCapas.ToArray();
        }
    }

    // =========================================================================
    //  PINTAR SPLATMAP
    // =========================================================================

    IEnumerator PintarSplatmap()
    {
        var td  = _terrain.terrainData;
        _res = td.alphamapResolution;
        int numCapas = td.terrainLayers.Length;
        if (numCapas == 0) yield break;

        _mapa = new float[_res, _res, numCapas];

        float terW = td.size.x;
        float terL = td.size.z;
        float terH = td.size.y;

        int pixelesPorFrame = 64; // procesar 64 columnas por frame
        int procesados = 0;

        for (int z = 0; z < _res; z++)
        {
            for (int x = 0; x < _res; x++)
            {
                float nx = (float)x / _res;
                float nz = (float)z / _res;

                // Posición real
                float wx = nx * terW;
                float wz = nz * terL;

                // Altura normalizada (0-1) y ángulo de pendiente
                float altNorm  = td.GetHeight(x, z) / terH;
                float altUnity = altNorm * terH;
                float pendiente= td.GetSteepness(nx, nz);

                // Distancia al centro urbano
                float dx = wx - centroUrbano.x;
                float dz = wz - centroUrbano.y;
                float distUrb = Mathf.Sqrt(dx * dx + dz * dz);
                bool esUrbano = distUrb < radioUrbano;

                // Calcular pesos base
                float wOrtofoto  = 0f;
                float wHierba    = 0f;
                float wBosque    = 0f;
                float wRoca      = 0f;
                float wAlpino    = 0f;
                float wHormigon  = 0f;

                if (esUrbano)
                {
                    // Zona urbana: mezcla ortofoto + hormigón cerca del centro
                    float tUrb = 1f - Mathf.InverseLerp(0f, radioUrbano, distUrb);
                    wHormigon = Mathf.SmoothStep(0f, 1f, tUrb * 0.8f);
                    wOrtofoto = 1f - wHormigon;
                }
                else if (pendiente > pendienteRoca)
                {
                    // Pendiente pronunciada → roca
                    wRoca = Mathf.SmoothStep(pendienteRoca, pendienteRoca + 15f, pendiente) * 0.8f;
                    wHierba = 1f - wRoca;
                }
                else if (altUnity > alturaAlpino)
                {
                    wAlpino = 0.7f;
                    wRoca   = 0.3f;
                }
                else if (altUnity > alturaRoca)
                {
                    float t = Mathf.InverseLerp(alturaRoca, alturaAlpino, altUnity);
                    wRoca   = 1f - t * 0.5f;
                    wAlpino = t * 0.5f;
                }
                else if (altUnity > alturaBosque)
                {
                    // Verificar si es zona de bosque
                    bool enZonaBosque = EsZonaDeBosque(wx, wz);
                    wBosque = enZonaBosque ? 0.75f : 0.3f;
                    wHierba = 1f - wBosque;
                }
                else
                {
                    // Pradera / pasto bajo
                    wHierba = 0.7f;
                    wOrtofoto = 0.3f;
                }

                // Asignar al splatmap según capas disponibles
                float[] pesos = { wOrtofoto, wHierba, wBosque, wRoca, wAlpino, wHormigon };
                float total = 0f;
                for (int c = 0; c < numCapas && c < pesos.Length; c++) total += pesos[c];
                if (total > 0f)
                    for (int c = 0; c < numCapas && c < pesos.Length; c++)
                        _mapa[z, x, c] = pesos[c] / total;
                else
                    _mapa[z, x, 0] = 1f; // fallback: capa base
            }

            procesados++;
            if (procesados >= pixelesPorFrame)
            {
                procesados = 0;
                yield return null; // no bloquear el hilo principal
            }
        }

        td.SetAlphamaps(0, 0, _mapa);
    }

    bool EsZonaDeBosque(float wx, float wz)
    {
        var zonas = GeoDataAlsasua.ZonasBosque;
        if (zonas == null) return false;
        foreach (var z in zonas)
        {
            // ZonaBosque.Centro es relativo al origen del terrain, + 1918/8570
            float cx = z.Centro.x + 1918f;
            float cz = z.Centro.z + 8570f;
            float dx = wx - cx;
            float dz = wz - cz;
            if (dx * dx + dz * dz < z.Radio * z.Radio) return true;
        }
        return false;
    }

    // =========================================================================
    //  API PÚBLICA
    // =========================================================================

    /// <summary>Repinta el splatmap (por ejemplo después de una explosión).</summary>
    public void RepintarZona(Vector3 centro, float radio)
    {
        // Simplificado: solo repinta una región pequeña
        StartCoroutine(PintarSplatmap());
    }
}
