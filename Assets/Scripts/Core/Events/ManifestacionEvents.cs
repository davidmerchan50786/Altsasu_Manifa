// Assets/Scripts/Core/Events/ManifestacionEvents.cs
// ═══════════════════════════════════════════════════════════════════════════
//  EVENTOS DE MANIFESTACIÓN — structs para EventBus
//
//  Publicados por: SistemaCargasPoliciales, SistemaMoralManifestacion
//  Suscriptores: SistemaMoralManifestacion, HUDManifestacion, AudioManager,
//  SistemaCamaraCinetica (shake en cargas), SistemaMusicaAdaptativa.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

/// <summary>
/// Aviso previo a una carga policial (ventana de reacción para el jugador).
/// segundosHastaCarga típico: 5–8s. Audio: silbatos, órdenes por megáfono.
/// </summary>
public struct AvisoCargaPolicialEvent
{
    public Vector3 origen;
    public float segundosHastaCarga;
}

/// <summary>Carga policial en curso contra la manifestación.</summary>
public struct CargaPolicialEvent
{
    public Vector3 origen;
    public Vector3 direccion;       // normalizada, hacia la multitud
    public float intensidad;        // 0–1: nº de agentes y agresividad
}

/// <summary>
/// La manifestación se ha iniciado (convocatoria). La disparan los sistemas de
/// manifestación para que NPCs y sistemas reaccionen al foco de la protesta.
/// </summary>
public struct ManifestacionIniciadaEvent
{
    public Vector3 centro;   // posición central de la manifestación
    public float radio;      // radio de influencia en metros
    public int participantes;
}

/// <summary>Cambio en la moral de la manifestación (0–100).</summary>
public struct MoralManifestacionEvent
{
    public float moral;
    public float delta;
}

/// <summary>La manifestación ha terminado.</summary>
public struct ManifestacionTerminadaEvent
{
    public bool dispersadaPorCarga;   // true = derrota; false = fin natural/victoria
    public float duracionSegundos;
    public int participantesFinales;
}
