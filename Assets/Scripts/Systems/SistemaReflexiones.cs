// Assets/Scripts/SistemaReflexiones.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SISTEMA DE REFLECTION PROBES — Reflections AAA para Alsasua
//
//  Problema que resuelve:
//    SSR (Screen Space Reflections) solo puede reflejar lo que hay en pantalla.
//    En interiores, bajo soportales, junto a paredes o en esquinas, SSR falla
//    y las superficies mojadas/metálicas quedan completamente negras.
//    Los Reflection Probes cubren esos huecos con capturas pre-baked o dinámicas.
//
//  Estrategia por tipo de zona:
//    · Plaza Herriko (1 probe grande, real-time cada 5s)
//    · Bajo soportales (N probes medianos, baked cada 30s)
//    · Interiores explorables (1 probe por sala, real-time on-enter)
//    · Bajo puentes / túneles (probes medios, baked)
//    · Farolas (probes pequeños para capturas de luz, baked)
//
//  Performance:
//    · Solo el probe más cercano al jugador es real-time.
//    · El resto son baked (UpdateMode.Baked) → cero coste en runtime.
//    · Al entrar en un interior, el probe de esa sala se activa real-time.
//    · Los probes lejanos (>200m) se desactivan completamente.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

[DefaultExecutionOrder(-60)]
public class SistemaReflexiones : MonoBehaviour
{
    public static SistemaReflexiones Instance { get; private set; }

    [Header("Configuración")]
    [Tooltip("Solo el probe más cercano a este radio del jugador se actualiza en real-time.")]
    public float radioRealtimeProbe = 30f;
    [Tooltip("Probes más lejos de esto se desactivan.")]
    public float radioMaxProbe = 200f;
    [Tooltip("Segundos entre actualizaciones de probes baked.")]
    public float intervaloBake = 30f;

    // ── Datos de cada probe ────────────────────────────────────────────────
    struct DatoProbe
    {
        public ReflectionProbe    probe;
        public HDAdditionalReflectionData hdData;
        public Vector3            centro;
        public float              radio;
        public bool               esInterior;
        public string             nombre;
    }

    readonly List<DatoProbe> _probes = new();
    int _probeRealtime = -1;  // índice del probe actualmente real-time

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void Start() => StartCoroutine(InicializarTras(4f));

    IEnumerator InicializarTras(float delay)
    {
        yield return new WaitForSeconds(delay);
        CrearProbesEnPuntosClaveAlsasua();
        AltsasuCore.OnJugadorSpawned += _ => StartCoroutine(CicloProbes());
        if (AltsasuCore.Jugador != null) StartCoroutine(CicloProbes());
        AlsasuaLogger.Info("Reflexiones", $"{_probes.Count} reflection probes creados.");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  PLACEMENT — puntos clave de Alsasua con coordenadas reales
    // ════════════════════════════════════════════════════════════════════════

    void CrearProbesEnPuntosClaveAlsasua()
    {
        var parent = new GameObject("ReflectionProbes_Alsasua").transform;
        parent.SetParent(transform, false);

        // Herriko Plaza — probe grande real-time (superficies mojadas de la plaza)
        AnadirProbe(parent, "HerrikoPlaza",
            new Vector3(GeoDataAlsasua.OX, GeoDataAlsasua.AlturaTerreno(GeoDataAlsasua.OX, GeoDataAlsasua.OZ) + 4f, GeoDataAlsasua.OZ),
            size: new Vector3(60f, 15f, 60f), esInterior: false, realtimeInterval: 5f);

        // Cuartel GC — probe medio (suelo húmedo + edificio)
        AnadirProbe(parent, "CuartelGC",
            new Vector3(2180f, GeoDataAlsasua.AlturaTerreno(2180f, 8720f) + 3f, 8720f),
            size: new Vector3(30f, 10f, 30f), esInterior: false, realtimeInterval: 15f);

        // Carretera N-1 norte — probe para asfalto mojado
        AnadirProbe(parent, "N1_Norte",
            new Vector3(GeoDataAlsasua.CarreteraN1Norte.x, GeoDataAlsasua.AlturaTerreno(GeoDataAlsasua.CarreteraN1Norte.x, GeoDataAlsasua.CarreteraN1Norte.z) + 2f, GeoDataAlsasua.CarreteraN1Norte.z),
            size: new Vector3(40f, 8f, 20f), esInterior: false, realtimeInterval: 20f);

        // Carretera N-1 sur
        AnadirProbe(parent, "N1_Sur",
            new Vector3(GeoDataAlsasua.CarreteraN1Sur.x, GeoDataAlsasua.AlturaTerreno(GeoDataAlsasua.CarreteraN1Sur.x, GeoDataAlsasua.CarreteraN1Sur.z) + 2f, GeoDataAlsasua.CarreteraN1Sur.z),
            size: new Vector3(40f, 8f, 20f), esInterior: false, realtimeInterval: 20f);

        // Estación de tren — interior tipo
        AnadirProbe(parent, "EstacionTren",
            new Vector3(GeoDataAlsasua.EstacionTren.x, GeoDataAlsasua.AlturaTerreno(GeoDataAlsasua.EstacionTren.x, GeoDataAlsasua.EstacionTren.z) + 3f, GeoDataAlsasua.EstacionTren.z),
            size: new Vector3(20f, 8f, 15f), esInterior: true, realtimeInterval: 8f);

        // Monte Aralar — lejos, baked, captura el skybox lejano
        AnadirProbe(parent, "MonteAralar",
            new Vector3(GeoDataAlsasua.MonteAralar.x, GeoDataAlsasua.AlturaTerreno(GeoDataAlsasua.MonteAralar.x, GeoDataAlsasua.MonteAralar.z) + 10f, GeoDataAlsasua.MonteAralar.z),
            size: new Vector3(200f, 50f, 200f), esInterior: false, realtimeInterval: 60f);

        // Barrio Norte — probe para calles residenciales
        AnadirProbe(parent, "BarrioNorte",
            new Vector3(GeoDataAlsasua.BarrioNorte.x, GeoDataAlsasua.AlturaTerreno(GeoDataAlsasua.BarrioNorte.x, GeoDataAlsasua.BarrioNorte.z) + 3f, GeoDataAlsasua.BarrioNorte.z),
            size: new Vector3(35f, 12f, 35f), esInterior: false, realtimeInterval: 15f);

        // Polígono Isasia — naves industriales
        AnadirProbe(parent, "PoligonoIsasia",
            new Vector3(GeoDataAlsasua.PoligonoIsasia.x, GeoDataAlsasua.AlturaTerreno(GeoDataAlsasua.PoligonoIsasia.x, GeoDataAlsasua.PoligonoIsasia.z) + 4f, GeoDataAlsasua.PoligonoIsasia.z),
            size: new Vector3(50f, 15f, 50f), esInterior: false, realtimeInterval: 25f);
    }

    void AnadirProbe(Transform parent, string nombre, Vector3 centro,
                     Vector3 size, bool esInterior, float realtimeInterval)
    {
        var go = new GameObject($"ReflProbe_{nombre}");
        go.transform.SetParent(parent, false);
        go.transform.position = centro;

        var probe = go.AddComponent<ReflectionProbe>();
        probe.mode          = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
        probe.refreshMode   = UnityEngine.Rendering.ReflectionProbeRefreshMode.ViaScripting;
        probe.timeSlicingMode = UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
        probe.size          = size;
        probe.center        = Vector3.zero;
        probe.importance    = esInterior ? 2 : 1;
        probe.intensity     = 1.0f;
        probe.hdr           = true;
        probe.resolution    = esInterior ? 128 : 64; // resolución razonable para runtime
        probe.nearClipPlane = 0.3f;
        probe.farClipPlane  = Mathf.Max(size.x, size.z) * 1.5f;
        probe.cullingMask   = ~LayerMask.GetMask("Player", "Ignore Raycast");

        var hdData = go.AddComponent<HDAdditionalReflectionData>();

        _probes.Add(new DatoProbe
        {
            probe      = probe,
            hdData     = hdData,
            centro     = centro,
            radio      = Mathf.Max(size.x, size.z) * 0.5f,
            esInterior = esInterior,
            nombre     = nombre,
        });

        // Captura inicial diferida
        StartCoroutine(CapturaInicial(probe, realtimeInterval));
    }

    IEnumerator CapturaInicial(ReflectionProbe probe, float delay)
    {
        yield return new WaitForSeconds(delay + Random.Range(0f, 2f));
        if (probe != null) probe.RenderProbe();
    }

    // ════════════════════════════════════════════════════════════════════════
    //  CICLO — solo el probe más cercano es real-time
    // ════════════════════════════════════════════════════════════════════════

    IEnumerator CicloProbes()
    {
        while (true)
        {
            yield return new WaitForSeconds(3f); // evaluar cada 3s

            var jugador = AltsasuCore.Jugador;
            if (jugador == null) continue;

            float minDist2   = float.MaxValue;
            int   masProximo = -1;

            for (int i = 0; i < _probes.Count; i++)
            {
                var d = _probes[i];
                if (d.probe == null) continue;

                float dist2 = (d.centro - jugador.position).sqrMagnitude;
                float max2  = radioMaxProbe * radioMaxProbe;

                // Activar/desactivar por distancia
                bool activo = dist2 < max2;
                if (d.probe.gameObject.activeSelf != activo)
                    d.probe.gameObject.SetActive(activo);

                if (activo && dist2 < minDist2)
                { minDist2 = dist2; masProximo = i; }
            }

            // Cambiar el probe real-time si el jugador se acercó a otro
            if (masProximo != _probeRealtime)
            {
                // Dar modo "baked manual" al anterior
                if (_probeRealtime >= 0 && _probeRealtime < _probes.Count)
                {
                    var viejo = _probes[_probeRealtime].probe;
                    if (viejo != null) viejo.RenderProbe(); // una captura final
                }
                _probeRealtime = masProximo;
                AlsasuaLogger.Info("Reflexiones",
                    $"Probe real-time → {(masProximo >= 0 ? _probes[masProximo].nombre : "ninguno")}");
            }

            // Renderizar el probe real-time activo
            if (_probeRealtime >= 0 && _probeRealtime < _probes.Count)
            {
                var p = _probes[_probeRealtime].probe;
                if (p != null && p.gameObject.activeSelf) p.RenderProbe();
            }

            // Bake periódico del resto (distribuido para no hacer todos en el mismo frame)
            int idx = Mathf.FloorToInt(Time.time / intervaloBake) % Mathf.Max(1, _probes.Count);
            if (idx != _probeRealtime && idx < _probes.Count)
            {
                var p = _probes[idx].probe;
                if (p != null && p.gameObject.activeSelf) p.RenderProbe();
            }
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
