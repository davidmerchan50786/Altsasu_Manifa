// Assets/Scripts/SistemaIA.cs
// Registry central de agentes IA — evita FindObjectsByType<T>() en misiones y sistemas.
//
// Uso:
//   SistemaIA.Registrar(this);     // en Start() de cada agente
//   SistemaIA.Desregistrar(this);  // en OnDestroy() de cada agente
//   SistemaIA.AlertarCercanos(pos, radio); // alerta a todos los agentes en rango

using System.Collections.Generic;
using UnityEngine;

public class SistemaIA : SingletonMono<SistemaIA>
{
    private readonly List<IAgente> _agentes = new List<IAgente>(128);

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

    /// <summary>Devuelve todos los agentes activos dentro de radio.</summary>
    public static List<IAgente> EnRango(Vector3 centro, float radio)
    {
        var resultado = new List<IAgente>();
        if (Instance == null) return resultado;
        float r2 = radio * radio;
        foreach (var a in Instance._agentes)
            if (a.EstaActivo && (a.Posicion - centro).sqrMagnitude <= r2)
                resultado.Add(a);
        return resultado;
    }

    /// <summary>Alerta a todos los agentes activos dentro de radio.</summary>
    public static void AlertarCercanos(Vector3 origen, float radio)
    {
        if (Instance == null) return;
        float r2 = radio * radio;
        foreach (var a in Instance._agentes)
            if (a.EstaActivo && (a.Posicion - origen).sqrMagnitude <= r2)
                a.Alertar(origen);
    }

    /// <summary>Número de agentes activos registrados (para debug/diagnóstico).</summary>
    public static int TotalActivos
    {
        get
        {
            if (Instance == null) return 0;
            int n = 0;
            foreach (var a in Instance._agentes) if (a.EstaActivo) n++;
            return n;
        }
    }

    protected override void OnDestroyed()
    {
        _agentes.Clear();
    }
}
