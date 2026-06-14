// Assets/Scripts/Core/ArranqueMundo.cs
// ═══════════════════════════════════════════════════════════════════════════
//  ARRANQUE DEL MUNDO — señal de Core para el boot dinámico
//
//  Puente entre el streaming (capa Systems) y la pantalla de carga (capa Runtime),
//  que NO pueden referenciarse entre sí. Vive en Core, que ambas ven.
//
//    · El gestor de streaming (Systems) llama RegistrarGestor() al existir y
//      MarcarZonaInicialLista() cuando el contenido del spawn está cargado.
//    · La pantalla de carga (Runtime) consulta ZonaInicialListaONoAplica para
//      saber si puede levantarse sin pop-in — o suscribe OnZonaInicialLista.
//
//  ANTI-REGRESIÓN: si NO hay gestor (no está en escena, o Addressables off y no
//  registra), 'ZonaInicialRequerida' queda false y ZonaInicialListaONoAplica es
//  true → la pantalla NO espera a nadie. Solo se espera si alguien se compromete
//  a reportar.
//
//  Capa CORE: sin dependencias.
// ═══════════════════════════════════════════════════════════════════════════

using System;
using UnityEngine;

public static class ArranqueMundo
{
    /// <summary>True si algún gestor se ha comprometido a reportar la zona inicial.</summary>
    public static bool ZonaInicialRequerida { get; private set; }

    /// <summary>True cuando el contenido de la zona de spawn ya está instanciado.</summary>
    public static bool ZonaInicialLista { get; private set; }

    /// <summary>Se dispara UNA vez al quedar lista la zona inicial (push para suscriptores).</summary>
    public static event Action OnZonaInicialLista;

    /// <summary>Para consumidores: ¿está lista o no hace falta esperarla? (true si no hay gestor).</summary>
    public static bool ZonaInicialListaONoAplica => !ZonaInicialRequerida || ZonaInicialLista;

    /// <summary>Un gestor declara que reportará la zona inicial (llamar en su arranque).</summary>
    public static void RegistrarGestor() => ZonaInicialRequerida = true;

    /// <summary>Marca la zona de spawn como lista e invoca a los suscriptores (idempotente).</summary>
    public static void MarcarZonaInicialLista()
    {
        if (ZonaInicialLista) return;
        ZonaInicialLista = true;
        OnZonaInicialLista?.Invoke();
    }

    // Los statics sobreviven al cambio de escena en el editor sin domain-reload →
    // resetear al entrar en Play para no arrastrar el estado de una sesión anterior.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        ZonaInicialRequerida = false;
        ZonaInicialLista     = false;
        OnZonaInicialLista   = null;
    }
}
