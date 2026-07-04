// Assets/Scripts/_Testigos~/TestigoNPC.cs  (STAGING/DRAFT — carpeta ~ no se compila)
// ─────────────────────────────────────────────────────────────────────────────
//  Marca a un NPC civil como posible testigo. El SistemaTestigos decide, ante un
//  delito que el NPC ve, si SE CHIVA (sube wanted/paranoia) o TE CUBRE (apoyo alto).
//  La corrutina de "avisar" se guarda y se cancela en OnDestroy (convención del proyecto).
// ─────────────────────────────────────────────────────────────────────────────
using System.Collections;
using UnityEngine;

public class TestigoNPC : MonoBehaviour
{
    public bool Ocupado { get; private set; }    // ya está chivándose, no spamear
    Coroutine _co;

    void OnEnable()  => SistemaTestigos.Registrar(this);
    void OnDestroy() { if (_co != null) StopCoroutine(_co); SistemaTestigos.Desregistrar(this); }
    void OnDisable() => SistemaTestigos.Desregistrar(this);

    /// <summary>Va a avisar: tras un retardo, aplica el reporte.</summary>
    public void Chivarse(Vector3 lugar, float gravedad, float retardo)
    {
        if (Ocupado || !isActiveAndEnabled) return;
        Ocupado = true;
        _co = StartCoroutine(Reportar(lugar, gravedad, retardo));
    }

    IEnumerator Reportar(Vector3 lugar, float gravedad, float retardo)
    {
        // ★ aquí podrías mover al NPC hacia un guardia o un teléfono y animar "señalar".
        AlsasuaLogger.Info("Testigo", $"{name}: te ha visto. Va a avisar…");
        yield return new WaitForSeconds(retardo);

        ServiceLocator.Get<IWantedSystem>()?.AumentarBusqueda(Mathf.Max(1, Mathf.CeilToInt(gravedad * 2f)));
        SistemaApoyoPopular.Instance?.SumarParanoia(gravedad * 5f);
        AlsasuaLogger.Info("Testigo", $"{name}: ¡te ha delatado!");
        _co = null;
        Ocupado = false;
    }

    /// <summary>Apoyo alto: el vecino te cubre (no reporta; feedback breve).</summary>
    public void Cubrir() => AlsasuaLogger.Info("Testigo", $"{name}: te cubre (apoyo alto).");
}
