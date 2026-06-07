// Assets/Scripts/IInteractable.cs
// ═══════════════════════════════════════════════════════════════════════════
//  CONTRATO DE INTERACCIÓN — objetos con los que el jugador puede interactuar
//
//  Implementantes actuales:
//    · ControladorVehiculoJugador  — entrar/salir de vehículos (tecla E)
//
//  Implementantes futuros:
//    · ArmaRecogible   — recoger arma del suelo
//    · PuertaInterior  — abrir puertas de edificios
//    · BarricadaFuego  — colocar/recoger barricadas
//
//  El jugador llama a GetComponent<IInteractable>() en un OverlapSphere
//  y selecciona el objeto más cercano — sin conocer el tipo concreto.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

public interface IInteractable
{
    /// <summary>Texto mostrado en el HUD cuando el jugador está en radio de interacción.</summary>
    string TextoInteraccion { get; }

    /// <summary>Radio máximo desde el que el jugador puede interactuar (metros).</summary>
    float RadioInteraccion { get; }

    /// <summary>Si es false, el objeto no aparecerá como interactuable (ya ocupado, roto…).</summary>
    bool PuedeInteractuar { get; }

    /// <summary>El jugador ha pulsado la tecla de interacción sobre este objeto.</summary>
    void OnInteractuar(ControladorJugador jugador);
}
