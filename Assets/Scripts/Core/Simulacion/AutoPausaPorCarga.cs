// Assets/Scripts/Core/Simulacion/AutoPausaPorCarga.cs
// ═══════════════════════════════════════════════════════════════════════════
//  AUTO-PAUSA POR CARGA — productores opcionales que se apagan bajo presión
//  (capa CORE) — completa el Pilar 3 del Orquestador (degrade dinámico)
//
//  Componente DROP-ON: arrástralo al GameObject de un productor opcional
//  (generación de escombros, partículas ambientales, proc-gen no esencial) y
//  asígnale qué pausar. Se suscribe a IGlobalSimulationOrchestrator.OnFactorCargaCambia
//  y, cuando el FactorCarga cae por debajo del umbral (el frame se va de 16.6 ms),
//  apaga sus objetivos ANTES de que el jugador note el tirón. Con histéresis para
//  no parpadear en el borde.
//
//  No fuerza editar los sistemas existentes: es aditivo y opcional.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

[AddComponentMenu("Alsasua/Simulación/Auto-Pausa por Carga")]
public sealed class AutoPausaPorCarga : MonoBehaviour
{
    [Tooltip("FactorCarga por debajo del cual se PAUSA (1 = todo a tope; <1 = degradado).")]
    [SerializeField, Range(0.5f, 1f)] float umbralPausa = 0.85f;
    [Tooltip("FactorCarga por encima del cual se REANUDA (mayor que umbralPausa → histéresis).")]
    [SerializeField, Range(0.5f, 1f)] float umbralReanuda = 0.95f;

    [Tooltip("Behaviours a desactivar al pausar (emisores de escombros, generadores, etc.).")]
    [SerializeField] Behaviour[] objetivos;
    [Tooltip("Sistemas de partículas a pausar/reanudar.")]
    [SerializeField] ParticleSystem[] particulas;

    bool _pausado;
    IGlobalSimulationOrchestrator _orq;

    void OnEnable()
    {
        _orq = ServiceLocator.Get<IGlobalSimulationOrchestrator>();
        if (_orq != null)
        {
            _orq.OnFactorCargaCambia += AlCambiarCarga;
            AlCambiarCarga(_orq.FactorCarga);     // estado inicial coherente
        }
    }

    void OnDisable()
    {
        if (_orq != null) _orq.OnFactorCargaCambia -= AlCambiarCarga;
        if (_pausado) Aplicar(false);             // no dejar nada apagado al desactivarse
    }

    void AlCambiarCarga(float factor)
    {
        if      (!_pausado && factor < umbralPausa)   Aplicar(true);
        else if ( _pausado && factor > umbralReanuda) Aplicar(false);
    }

    void Aplicar(bool pausar)
    {
        _pausado = pausar;

        if (objetivos != null)
            for (int i = 0; i < objetivos.Length; i++)
                if (objetivos[i] != null) objetivos[i].enabled = !pausar;

        if (particulas != null)
            for (int i = 0; i < particulas.Length; i++)
            {
                if (particulas[i] == null) continue;
                if (pausar) particulas[i].Pause();
                else        particulas[i].Play();
            }
    }
}
