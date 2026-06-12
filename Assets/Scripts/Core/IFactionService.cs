// Assets/Scripts/Core/IFactionService.cs
// ═══════════════════════════════════════════════════════════════════════════
//  IFACTIONSERVICE — servicio de facciones del movimiento popular
//
//  Capa CORE (interfaz pura, sin dependencias de gameplay).
//  Implementado por SistemaFacciones (capa GAMEPLAY), registrado en
//  ServiceLocator en Awake. Consumidores: SistemaManifestacion, misiones,
//  HUD (vía eventos EventBus, no vía polling).
//
//  Diseño: Docs/Narrativa_Facciones_TMEO_Vol2.md (Partes III y IV)
// ═══════════════════════════════════════════════════════════════════════════

public enum FaccionId
{
    GazteSutegi      = 0,
    Coordinadora     = 1,
    AskatuBeharra    = 2,
    AskapenTours     = 3,
    MoreaBilgunea    = 4,
    Komuntza         = 5,
    Asanblada        = 6,
    Biltzar          = 7,
    // Post-cisma de "El Congreso" — inactivas hasta que el evento las habilite:
    KomuntzaML       = 8,
    KomuntzaRecon    = 9,
}

/// <summary>Motivos de cambio de Coherencia — para logging y logros.</summary>
public enum MotivoCoherencia
{
    CompromisoCumplido,     // +5  Manifiesto del Frontón
    CorrupcionDestapada,    // +10 destapar corrupción propia
    SillaRechazada,         // +5  rechazar la Sexta Silla
    CampanaLimpia,          // +15 ganar sin dossier
    VasoFregado,            // +1  sí, está trackeado
    ChantajeSebas,          // -15
    PactoUnai,              // -5
    DossierUsado,           // -10
    PromesaIncumplida,      // -3
    Mentira,                // -1  opción [Mentir] en diálogo
}

public interface IFactionService
{
    /// <summary>Reputación 0–100 con la facción. 50 = neutral.</summary>
    float GetReputacion(FaccionId f);

    /// <summary>
    /// Modifica reputación. Aplica internamente la matriz cruzada
    /// (subir con Komuntza baja con Biltzar, etc.). Publica
    /// FactionReputationChangedEvent por cada facción afectada.
    /// </summary>
    void ModificarReputacion(FaccionId f, float delta, string razon = "");

    /// <summary>¿Está la facción activa en la partida? (las post-cisma empiezan inactivas).</summary>
    bool EstaActiva(FaccionId f);

    /// <summary>Activa una facción en runtime (cisma de "El Congreso").</summary>
    void ActivarFaccion(FaccionId f);

    /// <summary>Coherencia 0–100 del jugador. Oculta: sin barra en HUD jamás.</summary>
    float Coherencia { get; }

    /// <summary>Modifica Coherencia. Publica CoherenciaUmbralEvent y ManchaChistorraEvent.</summary>
    void ModificarCoherencia(float delta, MotivoCoherencia motivo);

    /// <summary>Multiplicador de reclutamiento para manifestaciones (propaganda de Txerra, etc.).</summary>
    float MultiplicadorReclutamiento { get; }

    /// <summary>Día de Lasterka u otro evento que desactiva la matriz cruzada.</summary>
    bool MatrizDesactivada { get; set; }
}
