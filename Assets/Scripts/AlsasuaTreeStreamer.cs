// Assets/Scripts/AlsasuaTreeStreamer.cs
// Port de AlsasuaTreeLoader del proyecto simulador.
// Carga 20.000+ árboles OSM en streaming según distancia al jugador.
// Solo visibles en radio configurable — no carga todo a la vez.

using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class AlsasuaTreeStreamer : MonoBehaviour
{
    [Header("Datos")]
    public string dataPath = "Assets/AlsasuaData/trees_unity.json";

    [Header("Prefabs (asignar en Inspector)")]
    [Tooltip("protoIdx 0=roble/haritz, 1=haya, 2=pino, 3=álamo/chopo")]
    public GameObject[] treePrefabs;

    [Header("Streaming")]
    public int   maxArboles    = 500;   // máx visibles simultáneamente
    public float radioVisible  = 250f;  // cargar en 250m
    public float radioDestroir = 350f;  // destruir más allá de 350m
    public float intervaloActualizacion = 3f; // segundos entre updates de streaming

    // ── Interno ────────────────────────────────────────────────────────────
    struct DatoArbol { public float x, z, h, r, rot; public int proto; }

    List<DatoArbol>              _todos = new();
    Dictionary<int, GameObject>  _activos = new();
    Transform                    _jugador;
    bool                         _cargado;
    Transform                    _contenedor;
    Terrain                      _terrain;

    // =========================================================================
    //  UNITY
    // =========================================================================

    IEnumerator Start()
    {
        _terrain   = Terrain.activeTerrain;
        _contenedor = new GameObject("=== Árboles Streaming ===").transform;

        yield return StartCoroutine(CargarDatos());
        yield return null;

        _jugador = AltsasuCore.Jugador;
        if (_jugador == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) _jugador = p.transform;
        }

        StartCoroutine(BucleStreaming());
    }

    void Update()
    {
        // Re-buscar jugador si cambió (respawn)
        if (_jugador == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) _jugador = p.transform;
        }
    }

    // =========================================================================
    //  CARGA
    // =========================================================================

    IEnumerator CargarDatos()
    {
        string abs = Path.Combine(Application.dataPath.Replace("Assets",""), dataPath);
        if (!File.Exists(abs)) { Debug.LogWarning("[Trees] JSON no encontrado: " + dataPath); yield break; }

        string json = File.ReadAllText(abs);
        yield return null;

        // Parseo manual del JSON de árboles
        // Formato: [{"protoIdx":0,"x":1766.4,"z":15785.9,"y":104.6,"h":11.2,"r":3.5,"rot":263.4}, ...]
        int i = json.IndexOf('[');
        if (i < 0) yield break;
        json = json.Substring(i);

        // Split por objetos individuales
        int pos = 0;
        int parsed = 0;
        while (pos < json.Length)
        {
            int start = json.IndexOf('{', pos);
            if (start < 0) break;
            int end = json.IndexOf('}', start);
            if (end < 0) break;

            string obj = json.Substring(start, end - start + 1);
            var d = ParseArbol(obj);
            if (d.HasValue && d.Value.x > 0 && d.Value.z > 0)
                _todos.Add(d.Value);

            pos = end + 1;
            parsed++;
            if (parsed % 1000 == 0) yield return null; // no bloquear
        }

        _cargado = true;
        Debug.Log($"[Trees] ✓ {_todos.Count} árboles OSM cargados para streaming.");
    }

    DatoArbol? ParseArbol(string obj)
    {
        try
        {
            float GetF(string key) {
                int ki = obj.IndexOf("\"" + key + "\":");
                if (ki < 0) return 0f;
                ki += key.Length + 3;
                int end = obj.IndexOfAny(new[]{',', '}'}, ki);
                return float.Parse(obj.Substring(ki, end - ki).Trim(),
                    System.Globalization.CultureInfo.InvariantCulture);
            }
            int GetI(string key) { return (int)GetF(key); }

            return new DatoArbol {
                proto = GetI("protoIdx"), x = GetF("x"), z = GetF("z"),
                h = GetF("h"), r = GetF("r"), rot = GetF("rot")
            };
        }
        catch { return null; }
    }

    // =========================================================================
    //  STREAMING
    // =========================================================================

    IEnumerator BucleStreaming()
    {
        while (true)
        {
            yield return new WaitForSeconds(intervaloActualizacion);
            if (!_cargado || _jugador == null) continue;

            Vector3 posJugador = _jugador.position;

            // Destruir árboles lejanos
            var lejanos = new List<int>();
            foreach (var kv in _activos)
            {
                if (kv.Value == null) { lejanos.Add(kv.Key); continue; }
                if (Vector3.Distance(kv.Value.transform.position, posJugador) > radioDestroir)
                    lejanos.Add(kv.Key);
            }
            foreach (int idx in lejanos)
            {
                if (_activos[idx] != null) Destroy(_activos[idx]);
                _activos.Remove(idx);
            }

            if (_activos.Count >= maxArboles) continue;

            // Buscar árboles cercanos no activos
            int añadidos = 0;
            for (int i = 0; i < _todos.Count && añadidos < 20; i++)
            {
                if (_activos.ContainsKey(i)) continue;
                var d = _todos[i];
                float dist = Vector2.Distance(new Vector2(d.x, d.z), new Vector2(posJugador.x, posJugador.z));
                if (dist > radioVisible) continue;

                SpawnArbol(i, d);
                añadidos++;
            }

            yield return null;
        }
    }

    void SpawnArbol(int idx, DatoArbol d)
    {
        GameObject prefab = treePrefabs != null && d.proto < treePrefabs.Length
            ? treePrefabs[d.proto] : (treePrefabs?.Length > 0 ? treePrefabs[0] : null);

        if (prefab == null) return;

        float y = _terrain != null
            ? _terrain.SampleHeight(new Vector3(d.x, 0, d.z))
            : 240f;

        var go = Instantiate(prefab, new Vector3(d.x, y, d.z), Quaternion.Euler(0, d.rot, 0), _contenedor);
        float esc = d.h > 0 ? Mathf.Clamp(d.h / 10f, 0.3f, 3f) : 1f;
        go.transform.localScale = Vector3.one * esc;
        go.isStatic = true;

        _activos[idx] = go;
    }
}
