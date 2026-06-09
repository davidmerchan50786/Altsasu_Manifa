// Assets/Scripts/SistemaClimaEfectos.cs
// ═══════════════════════════════════════════════════════════════════════════
//  CLIMA EFECTOS — bridge entre SistemaClima y los sistemas visuales
//
//  Lee SistemaClima.climaActual cada 3 s y ajusta:
//
//    SistemaHumoFabricas:
//      · Sol/Nublado   → emisión base (14 p/s)
//      · Lluvia        → emisión reducida (8 p/s, el humo se aplasta con la lluvia)
//      · Tormenta      → emisión alta + velocidad (el viento arrastra el humo, 22 p/s)
//      · Nieve         → emisión media, humo más blanco
//
//    SistemaNeblina:
//      · Ya reacciona al clima en su propio Update — aquí no hace falta nada
//
//    SistemaTren:
//      · Nieve intensa → tren reduce velocidad (realismo ferroviario)
//
//    Quality tier:
//      · Tier 3 → HumoFabricas desactivado (puro eye-candy)
//      · Tier 2 → emisión ×0.5
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using System.Collections;

[DefaultExecutionOrder(200)]
public class SistemaClimaEfectos : MonoBehaviour
{
    public static SistemaClimaEfectos Instance { get; private set; }

    [SerializeField] float intervalo = 3f;

    // Emisión base de SistemaHumoFabricas (leída en Start si el sistema existe)
    float _emisionBase = 14f;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    IEnumerator Start()
    {
        yield return new WaitForSeconds(5f);   // esperar a que los sistemas arranquen

        // Capturar emisión base
        if (SistemaHumoFabricas.Instance != null)
            _emisionBase = SistemaHumoFabricas.Instance.emision;

        while (true)
        {
            yield return new WaitForSeconds(intervalo);
            AplicarEfectosClima();
        }
    }

    void AplicarEfectosClima()
    {
        var clima = SistemaClimaExtension.EstadoActual;
        int  tier  = SistemaOptimizacion.TierCalidad;

        AjustarHumoFabricas(clima, tier);
        AjustarVelocidadTren(clima);
    }

    // ── Humo de fábricas ──────────────────────────────────────────────────

    void AjustarHumoFabricas(SistemaClima.EstadoClima clima, int tier)
    {
        var humo = SistemaHumoFabricas.Instance;
        if (humo == null) return;

        // Tier 3: apagar completamente
        if (tier >= 3) { humo.enabled = false; return; }
        humo.enabled = true;

        float emisionObj = clima switch
        {
            SistemaClima.EstadoClima.Tormenta      => _emisionBase * 1.6f,
            SistemaClima.EstadoClima.LluviaLigera  => _emisionBase * 0.55f,
            SistemaClima.EstadoClima.NieveLigera   => _emisionBase * 0.75f,
            _                                      => _emisionBase
        };

        if (tier == 2) emisionObj *= 0.5f;

        // Aplicar a todos los ParticleSystem del humo
        var sistemas = humo.GetComponentsInChildren<ParticleSystem>(true);
        foreach (var ps in sistemas)
        {
            var em = ps.emission;
            em.rateOverTime = emisionObj;
        }
    }

    // ── Velocidad del tren en nieve ───────────────────────────────────────

    void AjustarVelocidadTren(SistemaClima.EstadoClima clima)
    {
        var tren = SistemaTren.Instance;
        if (tren == null || !tren.enabled) return;

        // En nieve el tren va un 35% más lento (realismo de seguridad ferroviaria)
        tren.velocidadCrucero = clima == SistemaClima.EstadoClima.NieveLigera
            ? tren.velocidadCrucero * 0.65f
            : 25f;   // velocidad nominal (mismo valor que el SerializeField default)
    }
}
