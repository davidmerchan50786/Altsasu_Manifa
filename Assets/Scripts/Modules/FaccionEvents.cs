// Assets/Scripts/Core/Events/FaccionEvents.cs
// ═══════════════════════════════════════════════════════════════════════════
//  EVENTOS DE FACCIONES — structs para EventBus (zero-boxing)
//
//  Publicados por: SistemaFacciones (GAMEPLAY)
//  Suscriptores típicos: HUDCanvas, AudioManager, AplicadorManchaChistorra,
//  SistemaApoyoPopular, SistemaManifestacion.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>Cambio de reputación con una facción (uno por facción afectada por la matriz).</summary>
public struct FactionReputationChangedEvent
{
    public FaccionId faccion;
    public float valorAnterior;
    public float valorNuevo;
    public bool efectoCruzado;   // true si vino de la matriz, no de acción directa
}

/// <summary>La Coherencia cruzó un umbral (40 o 70). rising = cruzó hacia arriba.</summary>
public struct CoherenciaUmbralEvent
{
    public int umbral;
    public bool subiendo;
}

/// <summary>
/// Opacidad de la mancha de chistorra en la solapa del jugador (0–1).
/// Consumido por AplicadorManchaChistorra → parámetro _ChistorraOpacity del
/// material HDRP. El juego NUNCA la menciona en texto. Esa es la gracia.
/// </summary>
public struct ManchaChistorraEvent
{
    public float opacidad;
}

/// <summary>Una facción se activa en runtime (cisma de "El Congreso").</summary>
public struct FaccionActivadaEvent
{
    public FaccionId faccion;
}
