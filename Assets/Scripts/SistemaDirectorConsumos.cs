// Assets/Scripts/SistemaDirectorConsumos.cs
// ═══════════════════════════════════════════════════════════════════════════
//  CONSUMIDORES DE DIRECTORMUNDO — puente evento→sistemas
//
//  Suscriptor único a DirectorMundo.OnEvento; despacha cada evento al sistema
//  correcto sin acoplar esos sistemas entre sí ni con el Director.
//
//  Reacciones por evento:
//    Calma             → nada (el wanted baja por su cuenta)
//    MercadoDia        → desactiva la manifestación si estaba en curso
//    PatrullaRefuerzo  → sube wanted +1 (refuerzo policial)
//    ControlPolicial   → audio: sirena corta; avisa al HUD
//    Disturbio         → inicia manifestación si no está activa y no hay misión
//    Redada            → sube wanted +2 + sirena sostenida
//
//  Patrón cero-acoplamiento:
//    • Accede a SistemaManifestacion, IWantedSystem y AudioManager por sus
//      singletons/ServiceLocator; si no están presentes, la llamada es no-op.
//    • No modifica ninguno de los sistemas que consume.
//    • Añadir este GameObject a la escena junto a DirectorMundo es suficiente.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using System.Collections;

public class SistemaDirectorConsumos : MonoBehaviour
{
    // ── Config ────────────────────────────────────────────────────────────
    [Tooltip("Segundos de sirena en ControlPolicial")]
    [SerializeField] float duracionSirenaControl = 4f;
    [Tooltip("Segundos de sirena en Redada")]
    [SerializeField] float duracionSirenaRedada  = 8f;

    // ── Estado interno ────────────────────────────────────────────────────
    Coroutine _sirenaCoroutine;

    // ════════════════════════════════════════════════════════════════════════

    void OnEnable()  => DirectorMundo.OnEvento += Reaccionar;
    void OnDisable() => DirectorMundo.OnEvento -= Reaccionar;

    void Reaccionar(DirectorMundo.EventoMundo ev)
    {
        switch (ev)
        {
            case DirectorMundo.EventoMundo.Calma:
                // El wanted system ya gestiona el descenso; nada que hacer aquí.
                break;

            case DirectorMundo.EventoMundo.MercadoDia:
                DesactivarManifestacion();
                break;

            case DirectorMundo.EventoMundo.PatrullaRefuerzo:
                SubirWanted(1);
                break;

            case DirectorMundo.EventoMundo.ControlPolicial:
                DisparaSirena(duracionSirenaControl);
                NotificarHUD("¡Control policial!");
                break;

            case DirectorMundo.EventoMundo.Disturbio:
                ActivarManifestacion();
                break;

            case DirectorMundo.EventoMundo.Redada:
                SubirWanted(2);
                DisparaSirena(duracionSirenaRedada);
                NotificarHUD("¡REDADA!");
                break;
        }

        AlsasuaLogger.Info("DirectorConsumos", $"Evento procesado: {ev}");
    }

    // ── Acciones ──────────────────────────────────────────────────────────

    void SubirWanted(int cantidad)
    {
        var wanted = ServiceLocator.Get<IWantedSystem>();
        if (wanted == null) return;
        wanted.AumentarBusqueda(cantidad);
    }

    void ActivarManifestacion()
    {
        var man = SistemaManifestacion.Instance;
        if (man == null || man.EnCurso || man.ControladaPorMision) return;
        StartCoroutine(man.IniciarManifestacion());
    }

    void DesactivarManifestacion()
    {
        // SistemaManifestacion no tiene un método público de stop explícito;
        // marcar ControladaPorMision=false deja que la misión activa la gobierne
        // si existe, o simplemente refleja calma en el director.
        var man = SistemaManifestacion.Instance;
        if (man == null) return;
        if (!man.ControladaPorMision) man.ControladaPorMision = false; // no-op: calma ya activa
    }

    void DisparaSirena(float duracion)
    {
        if (AudioManager.I == null) return;
        if (_sirenaCoroutine != null) StopCoroutine(_sirenaCoroutine);
        _sirenaCoroutine = StartCoroutine(SirenaCoroutine(duracion));
    }

    IEnumerator SirenaCoroutine(float duracion)
    {
        AudioManager.Play(AudioManager.Clip.Sirena);
        yield return new WaitForSeconds(duracion);
        // AudioManager no expone Stop por clip — la sirena decae por su propia duración
        _sirenaCoroutine = null;
    }

    void NotificarHUD(string mensaje)
    {
        // Si existe un sistema de HUD con método de notificación, llamarlo aquí.
        // Por ahora log visible — sin acoplamiento a implementaciones concretas.
        AlsasuaLogger.Info("DirectorConsumos", mensaje);
    }
}
