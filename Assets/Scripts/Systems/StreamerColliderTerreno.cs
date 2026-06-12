// Assets/Scripts/Systems/StreamerColliderTerreno.cs
// ═══════════════════════════════════════════════════════════════════════════
//  STREAMER DE COLLIDERS DEL MOSAICO — física por demanda en anillos 1-2
//
//  Los TerrainCollider de los 48 tiles costarían ~150 MB de física. El anillo 0
//  (urbano) queda SIEMPRE activo; los de los anillos 1-2 se encienden solo
//  cuando el jugador se acerca, con histéresis para no parpadear en el borde:
//      distancia al tile < RADIO_ON  (1500 m) → collider ON
//      distancia al tile > RADIO_OFF (2000 m) → collider OFF
//
//  Mismo patrón de streaming por distancia que SistemaChunks, pero NO se
//  reutiliza ese sistema: aquel desactiva GameObjects enteros (los tiles deben
//  seguir RENDERIZÁNDOSE siempre — solo se apaga su física).
//
//  Lo crea ServicioTerreno/CargadorMosaicoTerreno no — se auto-instancia al
//  recibir MosaicoCompletoEvent.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using UnityEngine;

public class StreamerColliderTerreno : MonoBehaviour
{
    const float RADIO_ON = 1500f;
    const float RADIO_OFF = 2000f;
    const float PERIODO_S = 0.5f;

    struct Entrada
    {
        public TerrainCollider collider;
        public Rect bounds; // XZ mundo
    }

    readonly List<Entrada> _streamed = new();
    float _siguienteCheck;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        EventBus.Subscribe<MosaicoCompletoEvent>(OnMosaicoCompleto);
    }

    static void OnMosaicoCompleto(MosaicoCompletoEvent _)
    {
        if (FindFirstObjectByType<StreamerColliderTerreno>() != null) return;
        new GameObject("StreamerColliderTerreno").AddComponent<StreamerColliderTerreno>();
    }

    void Start()
    {
        var svc = ServiceLocator.Get<ITerrainService>();
        if (svc == null || !svc.EsMosaico) { Destroy(gameObject); return; }

        foreach (var t in svc.Tiles)
        {
            if (t == null) continue;
            var marca = t.GetComponent<MarcadorTerrenoAltsasua>();
            if (marca == null || marca.anillo <= 0) continue; // anillo 0: siempre ON
            var col = t.GetComponent<TerrainCollider>();
            if (col == null) continue;
            Vector3 p = t.transform.position; Vector3 s = t.terrainData.size;
            _streamed.Add(new Entrada
            {
                collider = col,
                bounds = new Rect(p.x, p.z, s.x, s.z)
            });
        }
        AlsasuaLogger.Info("Mosaico",
            $"StreamerColliderTerreno: {_streamed.Count} colliders bajo streaming " +
            $"(ON<{RADIO_ON} m, OFF>{RADIO_OFF} m).");
    }

    void Update()
    {
        if (Time.time < _siguienteCheck) return;
        _siguienteCheck = Time.time + PERIODO_S;

        Vector3 jugador = GeoDataAlsasua.JugadorPos();
        if (jugador == Vector3.zero)
        {
            var cam = Camera.main;
            if (cam == null) return;
            jugador = cam.transform.position;
        }

        foreach (var e in _streamed)
        {
            if (e.collider == null) continue;
            float d = DistanciaARect(jugador, e.bounds);
            if (e.collider.enabled)
            {
                if (d > RADIO_OFF) e.collider.enabled = false;
            }
            else if (d < RADIO_ON)
            {
                e.collider.enabled = true;
            }
        }
    }

    static float DistanciaARect(Vector3 p, Rect r)
    {
        float dx = Mathf.Max(r.xMin - p.x, 0f, p.x - r.xMax);
        float dz = Mathf.Max(r.yMin - p.z, 0f, p.z - r.yMax);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }
}
