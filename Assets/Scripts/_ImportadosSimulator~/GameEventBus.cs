using System;

namespace AlsasuaSimulator.Scripts
{
    /// <summary>
    /// Bus de eventos global para desacoplar sistemas.
    /// Reemplaza la necesidad de que los sistemas se refieran directamente entre sí.
    /// </summary>
    public static class GameEventBus
    {
        /// <summary>
        /// Se dispara cuando el estado de alerta general de Alsasua cambia.
        /// True = Alerta activa (GC y PF patrullando), False = Alerta desactivada.
        /// </summary>
        public static event Action<bool> OnAlertaCambio;

        public static void TriggerAlerta(bool estado)
        {
            OnAlertaCambio?.Invoke(estado);
        }
    }
}
