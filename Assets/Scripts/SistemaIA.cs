// Assets/Scripts/SistemaIA.cs
// Registry central de agentes IA — evita FindObjectsByType<T>() en misiones y sistemas.
//
// Uso:
//   SistemaIA.Registrar(this);     // en Start() de cada agente
//   SistemaIA.Desregistrar(this);  // en OnDestroy() de cada agente
//   SistemaIA.AlertarCercanos(pos, radio); // alerta a todos los agentes en rango
//
// NOTA sobre EnRango:
//   Devuelve un IReadOnlyList tomado de un pool interno.
//   Cada llamada obtiene una lista distinta — seguro si dos sistemas
//   llaman EnRango en el mismo frame. Devolver la lista con DevolverBuffer()
//   es opcional pero recomendable para reducir presión del GC a largo plazo.

using System.Collections.Generic;
using UnityEngine;

public class SistemaIA : SingletonMono<SistemaIA>
{
    private readonly List<IAgente> _agentes = new List<IAgente>(128);

    // ── Pool de buffers para EnRango ──────────────────────────────────────
    // BUG FIX: el buffer único anterior (_enRangoBuffer) era sobrescrito si dos
    // sistemas llamaban EnRango() en el mismo frame — el resultado de la primera
    // llamada quedaba corrupto. Ahora se usa un pool de listas: cada llamada
    // reserva una lista propia y puede usarla de forma independiente.
    private readonly Stack<List<IAgente>> _pool = new Stack<List<IAgente>>(4);

    private List<IAgente> TomarBuffer()
    {
        return _pool.Count > 0 ? _pool.Pop() : new List<IAgente>(32);
    }

    /// <summary>
    /// Devuelve un buffer al pool. Llamar tras consumir el resultado de EnRango
    /// si quieres evitar que el GC cree listas nuevas a largo plazo.
    /// </summary>
    public static void DevolverBuffer(IReadOnlyList<IAgente> lista)
    {
        if (Instance == null || lista is not List<IAgente> l) return;
        l.Clear();
        Instance._pool.Push(l);
    }

    // ── API pública ───────────────────────────────────────────────────────

    public static void Registrar(IAgente agente)
    {
        if (Instance != null && agente != null && !Instance._agentes.Contains(agente))
            Instance._agentes.Add(agente);
    }

    public static void Desregistrar(IAgente agente)
    {
        if (Instance != null) Instance._agentes.Remove(agente);
    }

    /// <summary>
    /// Devuelve todos los agentes activos dentro de radio.
    /// Cada llamada devuelve una lista independiente tomada del pool —
    /// es seguro llamar EnRango varias veces en el mismo frame.
    /// Opcionalmente llama DevolverBuffer(result