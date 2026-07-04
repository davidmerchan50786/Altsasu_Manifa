// Assets/Scripts/Runtime/SistemaDiaNoche.cs
// ═══════════════════════════════════════════════════════════════════════════
//  DÍA / NOCHE EN GAMEPLAY — la hora del ciclo afecta a las mecánicas.
//
//  Lee la hora real (SistemaAtmosfera.HoraDelDia) y expone:
//    · EsNoche / EsDia / Hora.
//    · FactorIngresoNegocio(tipo): los BARES rinden de noche; comercios,
//      empresas e industria, de día (cerrado de noche).
//    · FactorTrapicheo: el tráfico (droga) cunde más de noche.
//    · DeteccionSigilo: de noche te ven menos (factor < 1 para la IA).
//
//  Lo consultan SistemaEconomiaCriminal y demás. No cambia el render (de eso ya
//  va SistemaVolumenHDRP). Capa RUNTIME. Auto-arranque; sin montaje en escena.
// ═══════════════════════════════════════════════════════════════════════════
using UnityEngine;

[DefaultExecutionOrder(90)]
public sealed class SistemaDiaNoche : MonoBehaviour
{
    public static float Hora { get; private set; } = 12f;
    public static bool  EsNoche => Hora >= 22f || Hora < 6f;
    public static bool  EsDia   => !EsNoche;

    /// <summary>De noche te detectan menos (multiplicador de alcance/visión &lt; 1).</summary>
    public static float DeteccionSigilo => EsNoche ? 0.6f : 1f;

    /// <summary>El tráfico de droga cunde de noche.</summary>
    public static float FactorTrapicheo => EsNoche ? 1.5f : 0.8f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("SistemaDiaNoche");
        DontDestroyOnLoad(go);
        go.AddComponent<SistemaDiaNoche>();
    }

    void Update() { Hora = AltsasuCore.I?.atmosferaSystem?.HoraDelDia ?? Hora; }

    /// <summary>Cuánto rinde un negocio según su tipo y la hora.</summary>
    public static float FactorIngresoNegocio(Negocio.Tipo tipo) => tipo switch
    {
        Negocio.Tipo.Bar       => EsNoche ? 1.6f : 0.6f,   // el bar es de noche
        Negocio.Tipo.Comercio  => EsDia   ? 1.3f : 0.4f,
        Negocio.Tipo.Empresa   => EsDia   ? 1.2f : 0.3f,
        Negocio.Tipo.Industria => EsDia   ? 1.2f : 0.3f,
        _                      => 1f
    };
}
