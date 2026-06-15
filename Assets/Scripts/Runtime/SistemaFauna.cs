#pragma warning disable CS0414 // radioMontana: campo [SerializeField] reservado, aun sin consumir
// Assets/Scripts/SistemaFauna.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SISTEMA DE FAUNA — animales urbanos y silvestres en Alsasua
//
//  Usa los packs disponibles en el proyecto:
//    wolf.prefab          → Lobo HDRP (sierras Aralar/Urbasa, noche)
//    dog.prefab           → Perros callejeros (casco urbano)
//    rabbit.prefab        → Conejos (bordes del pueblo, prados)
//    deer-female-anim-*   → Ciervas (laderas norte, madrugada)
//    Oveja.prefab         → Ovejas (prados bajos)
//    Conejo_blend.prefab  → Conejos alternativos
//
//  Zonas de spawn (coordenadas Unity basadas en GeoDataAlsasua):
//    Urbano (radio ~200m del centro)    → perros
//    Montaña (Y > 80 Unity = >591m)    → lobos, ciervas
//    Prados (Y 20-80, pendiente baja)  → ovejas, conejos
//
//  Cada animal tiene un NavMeshAgent simple con deambulación aleatoria.
//  Si el NavMesh no tiene las sierras bakeadas, el animal usa rigidbody
//  con velocidad constante (fallback).
//
//  Quality tier:
//    0-1: 30 animales totales
//    2:   12 animales
//    3:   4 animales (solo perros en el pueblo)
//
//  DirectorMundo:
//    Redada → todos los animales huyen del centro (velocidad × 2.5)
//    Calma  → vuelven a deambular normal
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;

[DefaultExecutionOrder(170)]
public class SistemaFauna : MonoBehaviour
{
    public static SistemaFauna Instance { get; private set; }

    // ── Config ────────────────────────────────────────────────────────────
    [Range(4, 60)]
    [SerializeField] int maxAnimalesUltra = 30;
    [SerializeField] float radioUrbanoDogs  = 250f;
    [SerializeField] float radioMontana     = 600f;

    [Header("Prefabs reales (opcionales — los asigna IntegradorAssets)")]
    public GameObject prefabLobo;
    public GameObject prefabPerro;

    // ── Estado ────────────────────────────────────────────────────────────
    readonly List<AnimalProxy> _animales = new();
    bool _modoRedada;

    static readonly Vector3 CENTRO = new(1918f, 0f, 8570f);   // Herriko Plaza

    // ════════════════════════════════════════════════════════════════════════

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    IEnumerator Start()
    {
        yield return new WaitForSeconds(18f);   // después de NavMesh y terreno
        SpawnFauna();
        StartCoroutine(BucleDeambulacion());
        DirectorMundo.OnEvento += ReaccionarDirector;
    }

    void OnDestroy() => DirectorMundo.OnEvento -= ReaccionarDirector;

    // ════════════════════════════════════════════════════════════════════════
    //  SPAWN
    // ════════════════════════════════════════════════════════════════════════

    void SpawnFauna()
    {
        int max = MaxPorTier();
        var assets = SistemaAssets.Instance;
        if (assets == null) { AlsasuaLogger.Warn("Fauna", "SistemaAssets no disponible"); return; }

        // Distribución por tipo
        int perros   = Mathf.Max(1, max / 4);
        int conejos  = Mathf.Max(1, max / 5);
        int ovejas   = Mathf.Max(1, max / 5);
        int ciervos  = Mathf.Max(1, max / 6);
        int lobos    = max >= 12 ? Mathf.Max(1, max / 8) : 0;

        SpawnGrupo("dog",     perros,  CENTRO,             radioUrbanoDogs, 0f,  30f);
        SpawnGrupo("rabbit",  conejos, CENTRO,             400f,             5f,  40f);
        SpawnGrupo("Oveja",   ovejas,  CENTRO,             500f,             10f, 50f);
        SpawnGrupo("deer",    ciervos, CENTRO + new Vector3(0,0,200), 400f, 60f, 120f);
        if (lobos > 0)
            SpawnGrupo("wolf", lobos,  CENTRO + new Vector3(300,0,300), 500f, 80f, 160f);

        AlsasuaLogger.Info("Fauna", $"Fauna: {_animales.Count} animales spawneados");
    }

    void SpawnGrupo(string tipo, int cantidad, Vector3 centroZona, float radio,
                    float yMin, float yMax)
    {
        var assets = SistemaAssets.Instance;
        var prefab = assets.AnimalPorNombre(tipo);
        if (prefab == null) return;

        for (int i = 0; i < cantidad; i++)
        {
            Vector3 pos = PuntoAleatorio(centroZona, radio, yMin, yMax);
            if (pos == Vector3.zero) continue;

            var go = Instantiate(prefab, pos, Quaternion.Euler(0, Random.Range(0f,360f), 0), transform);
            go.name = $"Fauna_{tipo}_{i}";

            // Escala aleatoria ±10%
            float esc = Random.Range(0.9f, 1.1f);
            go.transform.localScale *= esc;

            // NavMeshAgent si hay NavMesh disponible
            NavMeshAgent agent = null;
            if (SistemaNavMesh.EstaListo && NavMesh.SamplePosition(pos, out _, 2f, NavMesh.AllAreas))
            {
                agent = go.AddComponent<NavMeshAgent>();
                agent.speed        = TipoVelocidad(tipo);
                agent.angularSpeed = 180f;
                agent.radius       = 0.3f;
                agent.height       = TipoAltura(tipo);
                agent.stoppingDistance = 1f;
            }

            _animales.Add(new AnimalProxy { go = go, agent = agent, tipo = tipo, posBase = pos });
        }
    }

    Vector3 PuntoAleatorio(Vector3 centro, float radio, float yMin, float yMax)
    {
        for (int i = 0; i < 8; i++)
        {
            var rnd = Random.insideUnitCircle * radio;
            var cand = centro + new Vector3(rnd.x, 0, rnd.y);

            float y = Terrain.activeTerrain != null
                ? Terrain.activeTerrain.SampleHeight(cand)
                : 0f;

            if (y < yMin || y > yMax) continue;
            cand.y = y + 0.1f;
            return cand;
        }
        return Vector3.zero;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  DEAMBULACIÓN
    // ════════════════════════════════════════════════════════════════════════

    IEnumerator BucleDeambulacion()
    {
        while (true)
        {
            yield return new WaitForSeconds(Random.Range(8f, 15f));

            foreach (var a in _animales)
            {
                if (a.go == null || a.agent == null) continue;
                if (!a.agent.isOnNavMesh) continue;
                if (_modoRedada) continue;   // en redada los animales ya huyeron

                // Elegir destino aleatorio cerca de su posición base
                Vector3 dest = a.posBase + new Vector3(
                    Random.Range(-60f, 60f), 0f, Random.Range(-60f, 60f));

                if (NavMesh.SamplePosition(dest, out NavMeshHit hit, 10f, NavMesh.AllAreas))
                    a.agent.SetDestination(hit.position);
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  DIRECTOR
    // ════════════════════════════════════════════════════════════════════════

    void ReaccionarDirector(DirectorMundo.EventoMundo ev)
    {
        switch (ev)
        {
            case DirectorMundo.EventoMundo.Redada:
            case DirectorMundo.EventoMundo.ControlPolicial:
                _modoRedada = true;
                var jugador = AltsasuCore.Jugador;
                Vector3 origen = jugador != null ? jugador.position : CENTRO;
                foreach (var a in _animales)
                {
                    if (a.go == null || a.agent == null || !a.agent.isOnNavMesh) continue;
                    a.agent.speed *= 2.5f;
                    // Huir en dirección contraria al jugador
                    Vector3 huida = (a.go.transform.position - origen).normalized * 80f;
                    if (NavMesh.SamplePosition(a.go.transform.position + huida,
                        out NavMeshHit hit, 20f, NavMesh.AllAreas))
                        a.agent.SetDestination(hit.position);
                }
                break;

            case DirectorMundo.EventoMundo.Calma:
                _modoRedada = false;
                foreach (var a in _animales)
                    if (a.agent != null) a.agent.speed = TipoVelocidad(a.tipo);
                break;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    int MaxPorTier() => SistemaOptimizacion.TierCalidad switch
    {
        0 => maxAnimalesUltra,
        1 => Mathf.RoundToInt(maxAnimalesUltra * 0.6f),
        2 => Mathf.RoundToInt(maxAnimalesUltra * 0.4f),
        _ => Mathf.Max(2, Mathf.RoundToInt(maxAnimalesUltra * 0.13f)),
    };

    static float TipoVelocidad(string tipo) => tipo switch
    {
        "wolf"   => 4.5f,
        "dog"    => 3.0f,
        "deer"   => 3.5f,
        "rabbit" => 4.0f,
        _        => 1.5f,   // ovejas, conejos
    };

    static float TipoAltura(string tipo) => tipo switch
    {
        "wolf"  => 0.7f,
        "dog"   => 0.4f,
        "deer"  => 1.0f,
        _       => 0.35f,
    };

    struct AnimalProxy
    {
        public GameObject  go;
        public NavMeshAgent agent;
        public string      tipo;
        public Vector3     posBase;
    }
}
