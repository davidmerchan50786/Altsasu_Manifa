// Assets/Scripts/SistemaRocasHD.cs
// ═══════════════════════════════════════════════════════════════════════════
//  ROCAS HD — HQ Rock prefabs en el anillo cercano al jugador
//
//  GeneradorRocasProcedurales usa GPU Instancing con meshes de esfera+Perlin
//  para las rocas lejanas. Este sistema spawnea los prefabs HD reales de
//  "HQ Rocks" (24 variantes) en el anillo <radioHD metros del jugador,
//  reemplazando visualmente las instancias procedurales en primer plano.
//
//  Flujo:
//    1. En Start, obtiene las posiciones de rocas del bioma Roca del terreno
//       (altitud > altitudMinRoca) y selecciona las más cercanas al jugador.
//    2. Spawnea hasta maxRocasHD prefabs (pool fijo).
//    3. En Update (throttled), recicla las que se alejan > radioHD*1.3
//       y activa nuevas cercanas.
//
//  Quality tier:
//    0-1: maxRocasHD = 20 (radio 60m)
//    2:   maxRocasHD = 8  (radio 40m)
//    3:   desactivado (usa solo procedural)
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[DefaultExecutionOrder(190)]
public class SistemaRocasHD : MonoBehaviour
{
    public static SistemaRocasHD Instance { get; private set; }

    [SerializeField] float radioHD         = 55f;
    [SerializeField] float altitudMinRoca  = 30f;   // Unity Y mínima (~541 m real)
    [Range(4, 30)]
    [SerializeField] int   maxRocasHD      = 16;

    readonly List<GameObject> _pool        = new();
    readonly List<Vector3>    _candidatos  = new();   // posiciones precalculadas
    float _timerReciclar;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    IEnumerator Start()
    {
        yield return new WaitForSeconds(15f);   // esperar terreno

        if (SistemaOptimizacion.TierCalidad >= 3) yield break;

        var assets = SistemaAssets.Instance;
        if (assets == null || assets.ContarRocas() == 0) yield break;

        PrecalcularCandidatos();
        InicializarPool(assets);
        AlsasuaLogger.Info("RocasHD", $"Pool {_pool.Count} rocas HD · {_candidatos.Count} candidatos");
    }

    // ── Precalcular posiciones de roca en el terreno ──────────────────────

    void PrecalcularCandidatos()
    {
        var terrain = Terrain.activeTerrain;
        if (terrain == null) return;

        var td     = terrain.terrainData;
        int res    = td.alphamapResolution;
        int canal  = 2;   // canal bioma Roca (igual que GeneradorRocasProcedurales)
        var mapa   = td.GetAlphamaps(0, 0, res, res);

        // Muestrear cada 4 px
        for (int z = 0; z < res; z += 4)
        for (int x = 0; x < res; x += 4)
        {
            if (mapa[z, x, canal] < 0.35f) continue;

            float nx = (float)x / res;
            float nz = (float)z / res;
            Vector3 pos = terrain.transform.position + new Vector3(
                nx * td.size.x, 0f, nz * td.size.z);
            pos.y = terrain.SampleHeight(pos) + 0.05f;

            if (pos.y < altitudMinRoca) continue;

            // Jitter ±2m
            pos += new Vector3(Random.Range(-2f, 2f), 0f, Random.Range(-2f, 2f));
            pos.y = terrain.SampleHeight(pos) + 0.05f;
            _candidatos.Add(pos);
        }
    }

    void InicializarPool(SistemaAssets assets)
    {
        int max = SistemaOptimizacion.TierCalidad == 0 ? maxRocasHD
                : SistemaOptimizacion.TierCalidad == 1 ? maxRocasHD
                : Mathf.Max(4, maxRocasHD / 2);

        for (int i = 0; i < max; i++)
        {
            var prefab = assets.RocaAleatoria();
            if (prefab == null) break;

            var go = Instantiate(prefab, Vector3.down * 500f, Quaternion.identity, transform);
            go.name = $"RocaHD_{i}";
            go.SetActive(false);
            _pool.Add(go);
        }
    }

    // ── Update — reciclar y reasignar ─────────────────────────────────────

    void Update()
    {
        _timerReciclar += Time.deltaTime;
        if (_timerReciclar < 3f) return;
        _timerReciclar = 0f;

        var jugador = AltsasuCore.Jugador;
        if (jugador == null || _candidatos.Count == 0) return;

        float radioRecicla = radioHD * 1.4f;

        // Reciclar lejanas
        foreach (var r in _pool)
        {
            if (!r.activeSelf) continue;
            if (Vector3.Distance(r.transform.position, jugador.position) > radioRecicla)
                r.SetActive(false);
        }

        // Activar libres en posiciones cercanas
        foreach (var pos in _candidatos)
        {
            float dist = Vector3.Distance(pos, jugador.position);
            if (dist > radioHD || dist < 5f) continue;

            // ¿Ya hay una roca aquí?
            bool ocupado = false;
            foreach (var r in _pool)
                if (r.activeSelf && Vector3.Distance(r.transform.position, pos) < 3f)
                { ocupado = true; break; }
            if (ocupado) continue;

            // Activar libre
            var libre = _pool.Find(r => !r.activeSelf);
            if (libre == null) break;

            libre.transform.position = pos;
            libre.transform.rotation = Quaternion.Euler(
                Random.Range(-15f, 15f), Random.Range(0f, 360f), Random.Range(-15f, 15f));
            libre.transform.localScale = Vector3.one * Random.Range(0.6f, 1.8f);
            libre.SetActive(true);
        }
    }
}
