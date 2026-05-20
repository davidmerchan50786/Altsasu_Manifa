// Assets/Scripts/SistemaAgendaNPC.cs
// ═══════════════════════════════════════════════════════════════════════════
//  AGENDA DE NPCS — rutinas diarias por hora del día
//
//  Cada NPC tiene un horario con destinos y comportamientos:
//    6:00  → ir al trabajo (polígono)
//    9:00  → descanso en bar
//    13:00 → bar/restaurante (comida)
//    15:00 → regreso trabajo
//    18:00 → paseo por la plaza
//    20:00 → bar tarde
//    22:00 → casa
//
//  Los destinos se asignan proceduralmente según la zona del NPC.
//  Usa NavMesh para pathfinding.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

public class SistemaAgendaNPC : MonoBehaviour
{
    public static SistemaAgendaNPC Instance { get; private set; }

    // ── Horarios ──────────────────────────────────────────────────────────
    public struct FranjaHoraria
    {
        public float  hora;           // 0-24
        public string actividad;
        public Vector3 destino;
        public float  radio;          // radio de deambulación en esa zona
        public float  velocidad;
    }

    // Zonas de destino en Alsasua (coordenadas terreno)
    static readonly Vector3[] ZONAS_BAR       = { new(1918,0,8570), new(1850,0,8600), new(1970,0,8540) };
    static readonly Vector3[] ZONAS_TRABAJO   = { new(2300,0,8400), new(2350,0,8450), new(1650,0,8200) };
    static readonly Vector3[] ZONAS_CASA      = { new(1820,0,8850), new(1900,0,8700), new(2000,0,8600) };
    static readonly Vector3[] ZONAS_PASEO     = { new(1918,0,8570), new(1880,0,8620), new(1960,0,8580) };

    // ── NPCs gestionados ──────────────────────────────────────────────────
    readonly List<NPCAgenda> _npcs = new();
    float _horaAnterior = -1f;

    // ════════════════════════════════════════════════════════════════════════

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void Start() => InvokeRepeating(nameof(ActualizarAgendas), 5f, 60f); // cada minuto real

    void ActualizarAgendas()
    {
        float hora = AltsasuCore.I?.atmosferaSystem?.HoraDelDia ?? 12f;
        if (Mathf.Approximately(hora, _horaAnterior)) return;
        _horaAnterior = hora;

        // Recopilar NPCs activos
        _npcs.RemoveAll(n => n == null || n.agente == null);
        var civiles = FindObjectsByType<NPCCivil>(FindObjectsSortMode.None);
        foreach (var c in civiles) RegistrarSiNuevo(c);

        // Aplicar agenda
        foreach (var npc in _npcs)
        {
            var franja = ObtenerFranja(hora, npc.seed);
            AsignarDestino(npc, franja);
        }
    }

    void RegistrarSiNuevo(NPCCivil c)
    {
        bool yaRegistrado = _npcs.Exists(n => n.civil == c);
        if (!yaRegistrado && c.GetComponent<NavMeshAgent>() != null)
        {
            _npcs.Add(new NPCAgenda
            {
                civil  = c,
                agente = c.GetComponent<NavMeshAgent>(),
                seed   = c.GetInstanceID() % 1000
            });
        }
    }

    FranjaHoraria ObtenerFranja(float hora, int seed)
    {
        // Seleccionar destino con variación por seed
        int idx = seed % 3;

        if (hora >= 22f || hora < 6f)
            return new FranjaHoraria { hora=22f, actividad="Casa", destino=ZONAS_CASA[idx], radio=30f, velocidad=1.2f };
        if (hora < 7f)
            return new FranjaHoraria { hora=6f,  actividad="Trabajo", destino=ZONAS_TRABAJO[idx], radio=40f, velocidad=1.4f };
        if (hora < 9.5f)
            return new FranjaHoraria { hora=9f,  actividad="Bar mañana", destino=ZONAS_BAR[idx], radio=20f, velocidad=1.3f };
        if (hora < 13f)
            return new FranjaHoraria { hora=9.5f,actividad="Trabajo", destino=ZONAS_TRABAJO[idx], radio=40f, velocidad=1.4f };
        if (hora < 15f)
            return new FranjaHoraria { hora=13f, actividad="Comida", destino=ZONAS_BAR[idx], radio=25f, velocidad=1.3f };
        if (hora < 18.5f)
            return new FranjaHoraria { hora=15f, actividad="Trabajo tarde", destino=ZONAS_TRABAJO[idx], radio=40f, velocidad=1.4f };
        if (hora < 20.5f)
            return new FranjaHoraria { hora=18.5f,actividad="Paseo", destino=ZONAS_PASEO[idx], radio=60f, velocidad=1.1f };
        return new FranjaHoraria { hora=20.5f, actividad="Bar tarde", destino=ZONAS_BAR[idx], radio=25f, velocidad=1.2f };
    }

    void AsignarDestino(NPCAgenda npc, FranjaHoraria franja)
    {
        if (npc.agente == null || !npc.agente.isOnNavMesh) return;
        if (npc.actividadActual == franja.actividad) return; // ya en esa actividad

        npc.actividadActual = franja.actividad;
        npc.agente.speed    = franja.velocidad;

        // Destino aleatorio dentro del radio
        Vector3 dest = franja.destino
            + new Vector3(Random.Range(-franja.radio, franja.radio), 0,
                          Random.Range(-franja.radio, franja.radio));

        if (NavMesh.SamplePosition(dest, out var hit, franja.radio * 2f, NavMesh.AllAreas))
            npc.agente.SetDestination(hit.position);
    }

    // ── API pública ───────────────────────────────────────────────────────
    public static void RegistrarNPC(NPCCivil c) => Instance?.RegistrarSiNuevo(c);

    // ── Tipos ─────────────────────────────────────────────────────────────
    class NPCAgenda
    {
        public NPCCivil    civil;
        public NavMeshAgent agente;
        public int          seed;
        public string       actividadActual = "";
    }
}
