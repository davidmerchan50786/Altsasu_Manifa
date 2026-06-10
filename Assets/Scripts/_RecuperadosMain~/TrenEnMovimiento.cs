// TrenEnMovimiento.cs
// Tren en las vías reales de la línea Madrid-Hendaia y Castejón
// que pasan por Alsasua (datos de railways_unity.json).
// El tren sigue los waypoints de las vías con suavizado Catmull-Rom.

using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class TrenEnMovimiento : MonoBehaviour
{
    [Header("Modelo del tren")]
    public GameObject prefabLocomotora;
    public GameObject prefabVagon;
    [Range(1, 8)] public int numVagones = 4;
    public float separacionVagones = 12f;  // metros entre vagones

    [Header("Movimiento")]
    public float velocidadKmh = 90f;       // velocidad crucero en vías
    public float velocidadPuebloKmh = 40f; // ralentiza al pasar por el pueblo
    public bool  pitarAlPasarPueblo = true;

    [Header("Audio")]
    public AudioClip clipTren;             // sonido locomotora
    public AudioClip clipPito;             // silbato
    public AudioClip clipFrenos;           // frenos

    [Header("Datos")]
    public string rutaRailways = "Assets/AlsasuaData/railways_unity.json";

    // ── Estado ────────────────────────────────────────────────────────────
    List<Vector3>   _via       = new();
    List<Transform> _partes    = new();   // locomotora + vagones
    List<float>     _offsets   = new();   // distancia desde el frente
    int             _idx       = 0;
    float           _distTotal = 0f;
    float           _distRecorrida = 0f;
    AudioSource     _srcTren, _srcPito;
    Terrain         _terrain;
    bool            _enPueblo;

    const float DIST_PUEBLO   = 600f;     // radio del "paso por pueblo"
    readonly Vector3 CENTRO   = new(1918, 0, 8570);

    // =========================================================================

    IEnumerator Start()
    {
        _terrain = Terrain.activeTerrain;
        yield return StartCoroutine(CargarVia());
        if (_via.Count < 2) { Debug.LogWarning("[Tren] Vías no cargadas."); yield break; }

        CrearComposicion();
        InicializarAudio();
    }

    void Update()
    {
        if (_via.Count < 2 || _partes.Count == 0) return;

        float vel = EstaEnPueblo() ? velocidadPuebloKmh / 3.6f : velocidadKmh / 3.6f;
        _distRecorrida += vel * Time.deltaTime;
        if (_distRecorrida > _distTotal) _distRecorrida -= _distTotal; // loop circular

        // Mover cada parte (locomotora + vagones)
        for (int i = 0; i < _partes.Count; i++)
        {
            float d = (_distRecorrida - _offsets[i] + _distTotal) % _distTotal;
            MoverAlDistancia(_partes[i], d);
        }

        // Audio
        if (_srcTren != null)
        {
            var pos3d = _partes.Count > 0 ? _partes[0].position : Vector3.zero;
            _srcTren.transform.position = pos3d;
            var jugador = AltsasuCore.Jugador;
            if (jugador != null)
            {
                float dist = Vector3.Distance(pos3d, jugador.position);
                _srcTren.volume = Mathf.Clamp01(1f - dist / 500f);
            }
        }

        // Pitar al entrar al pueblo
        bool enPuebloAhora = EstaEnPueblo();
        if (pitarAlPasarPueblo && enPuebloAhora && !_enPueblo && _srcPito != null)
            _srcPito.PlayOneShot(_srcPito.clip);
        _enPueblo = enPuebloAhora;
    }

    // ── Carga de vías desde JSON ─────────────────────────────────────────

    IEnumerator CargarVia()
    {
        string abs = Path.Combine(Application.dataPath.Replace("Assets",""), rutaRailways);
        if (!File.Exists(abs)) { Debug.LogWarning("[Tren] railways_unity.json no encontrado."); yield break; }

        string json = File.ReadAllText(abs);
        yield return null;

        // Parsear la primera vía (Madrid-Hendaia — la más larga)
        int railsIdx = json.IndexOf("\"rails\":[");
        if (railsIdx < 0) { railsIdx = 0; }
        int start = json.IndexOf("\"pts\":[", railsIdx);
        if (start < 0) yield break;
        start += 7;
        int end = json.IndexOf("]", start);
        string ptsStr = json.Substring(start, end - start);
        string[] nums = ptsStr.Split(',');

        var raw = new List<Vector3>();
        for (int i = 0; i + 2 < nums.Length; i += 3)
        {
            if (!float.TryParse(nums[i].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x)) continue;
            if (!float.TryParse(nums[i+2].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z)) continue;
            float y = _terrain != null ? _terrain.SampleHeight(new Vector3(x, 0, z)) + 0.15f : 240f;
            raw.Add(new Vector3(x, y, z));
        }

        // Suavizar con Catmull-Rom (200 puntos entre waypoints)
        for (int i = 0; i < raw.Count - 1; i++)
        {
            var p0 = raw[Mathf.Max(0, i-1)];
            var p1 = raw[i];
            var p2 = raw[Mathf.Min(raw.Count-1, i+1)];
            var p3 = raw[Mathf.Min(raw.Count-1, i+2)];
            for (int t = 0; t < 20; t++)
            {
                float ft = t / 20f;
                _via.Add(CatmullRom(p0, p1, p2, p3, ft));
            }
        }
        _via.Add(raw[raw.Count-1]);

        // Calcular distancia total
        for (int i = 1; i < _via.Count; i++)
            _distTotal += Vector3.Distance(_via[i-1], _via[i]);

        Debug.Log($"[Tren] ✓ Vía cargada: {_via.Count} puntos, {_distTotal:F0}m de longitud.");
    }

    static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        return 0.5f * ((2f*p1) + (-p0+p2)*t + (2f*p0-5f*p1+4f*p2-p3)*t*t + (-p0+3f*p1-3f*p2+p3)*t*t*t);
    }

    // ── Creación de la composición ───────────────────────────────────────

    void CrearComposicion()
    {
        // Locomotora
        CrearParte(prefabLocomotora, 0f, "Locomotora");
        // Vagones
        for (int i = 0; i < numVagones; i++)
            CrearParte(prefabVagon, separacionVagones * (i + 1), $"Vagon_{i+1}");
    }

    void CrearParte(GameObject prefab, float offset, string nombre)
    {
        GameObject go;
        if (prefab != null)
        {
            go = Instantiate(prefab);
            go.name = nombre;
        }
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = nombre + "_Placeholder";
            go.transform.localScale = nombre.StartsWith("Loco") ? new Vector3(3, 3, 10) : new Vector3(2.8f, 2.8f, 9f);
            go.GetComponent<MeshRenderer>().material.color = nombre.StartsWith("Loco") ? Color.red : new Color(0.3f, 0.3f, 0.3f);
        }
        _partes.Add(go.transform);
        _offsets.Add(offset);
        MoverAlDistancia(go.transform, offset);
    }

    void MoverAlDistancia(Transform t, float dist)
    {
        if (_via.Count < 2) return;
        float acum = 0f;
        for (int i = 1; i < _via.Count; i++)
        {
            float seg = Vector3.Distance(_via[i-1], _via[i]);
            if (acum + seg >= dist)
            {
                float f = (dist - acum) / seg;
                Vector3 pos = Vector3.Lerp(_via[i-1], _via[i], f);
                Vector3 dir = (_via[i] - _via[i-1]).normalized;
                t.position = pos;
                if (dir != Vector3.zero) t.rotation = Quaternion.LookRotation(dir);
                return;
            }
            acum += seg;
        }
        t.position = _via[_via.Count-1];
    }

    bool EstaEnPueblo()
    {
        if (_partes.Count == 0) return false;
        return Vector3.Distance(new Vector3(_partes[0].position.x, 0, _partes[0].position.z), new Vector3(CENTRO.x, 0, CENTRO.z)) < DIST_PUEBLO;
    }

    void InicializarAudio()
    {
        if (clipTren != null)
        {
            _srcTren = gameObject.AddComponent<AudioSource>();
            _srcTren.clip = clipTren; _srcTren.loop = true;
            _srcTren.spatialBlend = 1f; _srcTren.volume = 0.7f; _srcTren.Play();
        }
        if (clipPito != null)
        {
            _srcPito = gameObject.AddComponent<AudioSource>();
            _srcPito.clip = clipPito; _srcPito.spatialBlend = 1f; _srcPito.volume = 0.9f;
        }
    }
}
