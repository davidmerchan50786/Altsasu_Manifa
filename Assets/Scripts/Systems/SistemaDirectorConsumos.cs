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
                ApagonParcial(false, 0f);
                PararTren(false, 0f);
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
                PararTren(true, 30f);
                break;

            case DirectorMundo.EventoMundo.Disturbio:
                ActivarManifestacion();
                break;

            case DirectorMundo.EventoMundo.Redada:
                SubirWanted(2);
                DisparaSirena(duracionSirenaRedada);
                NotificarHUD("¡REDADA!");
                ApagonParcial(true, 55f);
                PararTren(true, 55f);
                BoostPolicia(true, 55f);
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
        AlsasuaLogger.Info("DirectorConsumos", mensaje);
    }

    // ── Apagón parcial (redada / toque de queda) ──────────────────────────
    // Desactiva SistemaVidaNocturna temporalmente para que ventanas y
    // farolas se apaguen — los vecinos apagan las luces durante la redada.
    Coroutine _apagonCoroutine;

    void ApagonParcial(bool activar, float duracion)
    {
        if (_apagonCoroutine != null) StopCoroutine(_apagonCoroutine);

        var vida = SistemaVidaNocturna.Instance;
        if (vida == null) return;

        if (!activar)
        {
            vida.enabled = true;
            return;
        }
        _apagonCoroutine = StartCoroutine(ApagonCoroutine(vida, duracion));
    }

    System.Collections.IEnumerator ApagonCoroutine(SistemaVidaNocturna vida, float duracion)
    {
        vida.enabled = false;
        AlsasuaLogger.Info("DirectorConsumos", "Apagón parcial activado (redada)");
        yield return new UnityEngine.WaitForSeconds(duracion);
        vida.enabled = true;
        AlsasuaLogger.Info("DirectorConsumos", "Iluminación restaurada");
        _apagonCoroutine = null;
    }

    // ── Tren — suspender servicio durante operaciones policiales ─────────
    Coroutine _trenCoroutine;

    void PararTren(bool parar, float duracion)
    {
        var tren = SistemaTren.Instance;
        if (tren == null) return;

        if (_trenCoroutine != null) StopCoroutine(_trenCoroutine);

        if (!parar)
        {
            tren.enabled = true;
            return;
        }
        _trenCoroutine = StartCoroutine(TrenCoroutine(tren, duracion));
    }

    System.Collections.IEnumerator TrenCoroutine(SistemaTren tren, float duracion)
    {
        tren.enabled = false;
        AlsasuaLogger.Info("DirectorConsumos", "Servicio ferroviario suspendido (operación policial)");
        yield return new UnityEngine.WaitForSeconds(duracion);
        tren.enabled = true;
        AlsasuaLogger.Info("DirectorConsumos", "Servicio ferroviario reanudado");
        _trenCoroutine = null;
    }

    // ── Boost de policía durante redada ───────────────────────────────────
    // Incrementa radioVision y velocidad de persecución temporalmente.
    // No modifica PoliciaForalIA directamente — accede por reflexión de campos.

    Coroutine _boostCoroutine;

    void BoostPolicia(bool activar, float duracion)
    {
        if (_boostCoroutine != null) StopCoroutine(_boostCoroutine);
        if (!activar) return;
        _boostCoroutine = StartCoroutine(BoostCoroutine(duracion));
    }

    System.Collections.IEnumerator BoostCoroutine(float duracion)
    {
        var policias = UnityEngine.Object.FindObjectsByType<PoliciaForalIA>(
            UnityEngine.FindObjectsSortMode.None);

        // Guardar valores originales y aplicar boost
        float[] visionOrig  = new float[policias.Length];
        float[] velOrig     = new float[policias.Length];

        var radioVisionField = typeof(PoliciaForalIA)
            .GetField("radioVision", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var velPersField = typeof(PoliciaForalIA)
            .GetField("velPerseguir", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (radioVisionField == null || velPersField == null)
        {
            AlsasuaLogger.Warn("DirectorConsumos", "Boost policía: campos privados no encontrados");
            yield break;
        }

        for (int i = 0; i < policias.Length; i++)
        {
            visionOrig[i] = (float)radioVisionField.GetValue(policias[i]);
            velOrig[i]    = (float)velPersField.GetValue(policias[i]);
            radioVisionField.SetValue(policias[i], visionOrig[i] * 1.4f);
            velPersField.SetValue(policias[i],    velOrig[i]    * 1.3f);
        }

        AlsasuaLogger.Info("DirectorConsumos",
            $"Boost policía activado: {policias.Length} agentes ({duracion}s)");

        yield return new UnityEngine.WaitForSeconds(duracion);

        for (int i = 0; i < policias.Length; i++)
        {
            if (policias[i] == null) continue;
            radioVisionField.SetValue(policias[i], visionOrig[i]);
            velPersField.SetValue(policias[i],    velOrig[i]);
        }
        AlsasuaLogger.Info("DirectorConsumos", "Boost policía desactivado");
        _boostCoroutine = null;
    }
}
