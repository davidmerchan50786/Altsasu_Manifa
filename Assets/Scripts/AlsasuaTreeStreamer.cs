// Assets/Scripts/AlsasuaTreeStreamer.cs  (also houses GeoDataAlsasua constants)
// ═══════════════════════════════════════════════════════════════════════════
//  ÁRBOL STREAMER — coloca prefabs de árbol en radio cercano al jugador
//  usando los datos OSM de trees_unity.json.
//
//  Capa de árboles de rango medio (100-400m):
//    - SistemaVegetacion gestiona hierba/arbustos GPU instanced (< 100m)
//    - AlsasuaTreeStreamer gestiona árboles individuales prefab (100-400m)
//    - Cesium Google Tiles gestiona bosques lejanos (> 400m)
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AlsasuaTreeStreamer : MonoBehaviour
{
    [Header("Prefabs de árbol")]
    public GameObject[] treePrefabs;

    [Header("Radio de streaming")]
    [Tooltip("Distancia a la que se instancian árboles (m). Alias: radioStreaming.")]
    public float radioVisible  = 250f;   // CesiumCapasAlsasua lo amplía a 400m si no hay Cesium

    [Tooltip("Distancia a la que se destruyen árboles fuera de rango (m). Debe ser > radioVisible.")]
    public float radioDestroir = 350f;   // alias radioDestruir (nombre histórico del campo)

    [Tooltip("Distancia mínima — árboles más cercanos los gestiona SistemaVegetacion.")]
    public float radioMinimo   = 30f;

    [Tooltip("Máximo de árboles instanciados simultáneamente.")]
    public int maxArboles = 300;

    // Alias de compatibilidad con código antiguo
    public float radioStreaming { get => radioVisible; set => radioVisible = value; }

    [Header("Datos OSM")]
    [Tooltip("Ruta al JSON con posiciones de árboles OSM.\n" +
             "Generado por DescargarDatosAlsasua.ps1 → Assets/AlsasuaData/trees_unity.json")]
    public string rutaJSON = "Assets/AlsasuaData/trees_unity.json";

    // ── Estado interno ─────────────────────────────────────────────────────
    readonly List<GameObject> _instancias = new();
    readonly List<Vector3>    _posiciones  = new();
    Transform                 _jugador;
    bool                      _cargado;

    // ──────────────────────────────────────────────────────────────────────

    void Start()
    {
        CargarPosicionesOSM();
        StartCoroutine(BucleSteaming());
    }

    void CargarPosicionesOSM()
    {
        var ta = Resources.Load<TextAsset>(
            System.IO.Path.ChangeExtension(rutaJSON, null)
                .Replace("Assets/Resources/", "")
                .Replace("Assets/", ""));

        if (ta == null)
        {
            // Intentar carga directa via File.ReadAllText en editor
#if UNITY_EDITOR
            if (System.IO.File.Exists(rutaJSON))
            {
                ParseJSON(System.IO.File.ReadAllText(rutaJSON));
                return;
            }
#endif
            AlsasuaLogger.Warn("TreeStreamer",
                $"No se pudo cargar {rutaJSON}. Streaming desactivado.");
            return;
        }
        ParseJSON(ta.text);
    }

    void ParseJSON(string json)
    {
        try
        {
            var wrapper = JsonUtility.FromJson<TreesWrapper>(
                "{\"items\":" + json + "}");
            foreach (var t in wrapper.items)
                _posiciones.Add(new Vector3(t.x, t.y, t.z));
            _cargado = true;
            AlsasuaLogger.Info("TreeStreamer",
                $"{_posiciones.Count} árboles OSM cargados.");
        }
        catch (System.Exception e)
        {
            AlsasuaLogger.Error("TreeStreamer", $"Error parsing trees JSON: {e.Message}");
        }
    }

    IEnumerator BucleSteaming()
    {
        while (true)
        {
            yield return new WaitForSeconds(2f);

            if (!_cargado || treePrefabs == null || treePrefabs.Length == 0) continue;

            // Buscar jugador si no se tiene
            if (_jugador == null)
            {
                var jGO = GameObject.FindGameObjectWithTag("Player");
                if (jGO != null) _jugador = jGO.transform;
                else continue;
            }

            Vector3 posJ = _jugador.position;

            // Destruir instancias fuera del radio
            for (int i = _instancias.Count - 1; i >= 0; i--)
            {
                if (_instancias[i] == null) { _instancias.RemoveAt(i); continue; }
                float d = Vector3.Distance(_instancias[i].transform.position, posJ);
                if (d > radioDestroir)
                {
                    Destroy(_instancias[i]);
                    _instancias.RemoveAt(i);
                }
            }

            // Instanciar árboles cercanos que falten
            if (_instancias.Count >= maxArboles) continue;

            foreach (var pos in _posiciones)
            {
                if (_instancias.Count >= maxArboles) break;
                float d = Vector3.Distance(pos, posJ);
                if (d < radioMinimo || d > radioVisible) continue;

                // Comprobar si ya hay un árbol cerca
                bool yaExiste = false;
                foreach (var inst in _instancias)
                    if (inst != null && Vector3.Distance(inst.transform.position, pos) < 3f)
                    { yaExiste = true; break; }
                if (yaExiste) continue;

                // Snap al terreno
                float y = Terrain.activeTerrain != null
                    ? Terrain.activeTerrain.SampleHeight(pos) + pos.y
                    : pos.y;

                var prefab = treePrefabs[Random.Range(0, treePrefabs.Length)];
                if (prefab == null) continue;
                var go = Instantiate(prefab,
                    new Vector3(pos.x, y, pos.z),
                    Quaternion.Euler(0, Random.Range(0f, 360f), 0),
                    transform);
                go.isStatic = false; // no estático — se destruye al salir del radio
                _instancias.Add(go);

                yield return null; // distribuir instantiate en frames
            }
        }
    }

    // ── Clases de deserialización ──────────────────────────────────────────

    [System.Serializable] class TreeEntry  { public float x, y, z; }
    [System.Serializable] class TreesWrapper { public List<TreeEntry> items; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  CONSTANTES GEOGRÁFICAS — Alsasua / Altsasu (lat 42.9°N)
// ─────────────────────────────────────────────────────────────────────────────
public static class GeoDataAlsasua
{
    /// <summary>Latitud del centro de Alsasua (Herriko Plaza).</summary>
    public const double LATITUD_CENTRO  = 42.9003;
    /// <summary>Longitud del centro de Alsasua (Herriko Plaza).</summary>
    public const double LONGITUD_CENTRO = -2.1665;

    /// <summary>Metros por grado de latitud (constante global ~111 320 m/°).</summary>
    public const double M_POR_GRADO_LAT = 111_320.0;

    /// <summary>
    /// Metros por grado de longitud a lat 42.9°N.
    /// = 111 320 × cos(42.9°) ≈ 81 560 m/°
    /// </summary>
    public const double M_POR_GRADO_LON = 81_560.0;
}
