// Assets/Scripts/Core/IInfluenciaSocial.cs
// ═══════════════════════════════════════════════════════════════════════════
//  CONTRATO — Influencia de Gravedad Social (capa CORE)
//
//  La capa MICRO del apoyo popular: cada agente de la multitud DOTS tiene una
//  "opinión" ∈ [-1,+1] que evoluciona por difusión entre vecinos + pozos de
//  evento (acciones del jugador/policía). Lo IMPLEMENTA el sistema de multitud
//  (SistemaMultitudBRG, en el asmdef leaf Alsasua.Crowd) y lo CONSUME el gameplay
//  (Runtime/Gameplay) SIN referenciar Alsasua.Crowd — exactamente igual que
//  ICrowdDensity: se resuelve por ServiceLocator.Get<IInfluenciaSocial>().
//
//  La macro (SistemaApoyoPopular, también Core) sigue siendo la autoridad del
//  agregado global 0-100; la multitud le reporta su media de opinión y lee ese
//  global como baseline de relajación → lazo macro↔micro cerrado, sin acoplar capas.
//
//  Emitir/Reportar SOLO desde el hilo principal (gameplay): el implementador
//  encola y vuelca a NativeArray en su propia ventana segura (jobs no en vuelo).
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

/// <summary>Estímulo puntual que altera la opinión de la multitud cercana.</summary>
public struct EventoInfluencia
{
    public Vector3 pos;     // foco del estímulo (mundo)
    public float   carga;   // signo+fuerza: + radicaliza hacia el movimiento, − enfría
    public float   radio;   // alcance (m); el efecto cae con la distancia hasta 0 en el borde
}

public interface IInfluenciaSocial
{
    /// <summary>Inyecta un pozo de gravedad social (jugador pega → −, carga policial → +…).</summary>
    void Emitir(EventoInfluencia ev);

    /// <summary>La policía se reporta como "antagonista" para que los radicales se interpongan.
    /// id estable (GetInstanceID) → upsert; entradas viejas caducan solas.</summary>
    void ReportarAntagonista(int id, Vector3 pos);

    /// <summary>Media de opinión de TODA la multitud, mapeada a [0,1] (0=hostil, 1=militante).</summary>
    float ApoyoMedio01 { get; }

    /// <summary>Media de opinión local en un radio (XZ), mapeada a [0,1]. Lectura segura (snapshot).</summary>
    float ApoyoLocal01(Vector3 centro, float radio);
}

/// <summary>
/// Fachada estática null-safe para que el gameplay emita sin null-checks ni
/// referencia a Alsasua.Crowd. Si no hay multitud registrada, los Emitir/Reportar
/// son no-ops y ApoyoMedio01 devuelve 0.5 (neutro).
/// </summary>
public static class InfluenciaSocial
{
    public static void Emitir(Vector3 pos, float carga, float radio) =>
        ServiceLocator.Get<IInfluenciaSocial>()?.Emitir(
            new EventoInfluencia { pos = pos, carga = carga, radio = radio });

    public static void ReportarAntagonista(int id, Vector3 pos) =>
        ServiceLocator.Get<IInfluenciaSocial>()?.ReportarAntagonista(id, pos);

    public static float ApoyoMedio01 =>
        ServiceLocator.Get<IInfluenciaSocial>()?.ApoyoMedio01 ?? 0.5f;

    public static float ApoyoLocal01(Vector3 centro, float radio) =>
        ServiceLocator.Get<IInfluenciaSocial>()?.ApoyoLocal01(centro, radio) ?? 0.5f;
}
