// Assets/Scripts/Runtime/BootstrapMisiones.cs
// ═══════════════════════════════════════════════════════════════════════════
//  BOOTSTRAP MISIONES — garantiza que los sistemas críticos para M00→M12
//  existen en escena antes de que SistemaMisiones los necesite.
//
//  POR QUÉ: SistemaGrafitis, SistemaApoyoPopular y RadioAskatasuna son
//  SingletonMono<T> sin RuntimeInitializeOnLoadMethod. Si no están en escena,
//  las misiones M03/M09 (grafitis) y M06 (radio) se bloquean silenciosamente —
//  los eventos nunca se disparan y la condición nunca se cumple.
//
//  ESTRATEGIA SEGURA: crea los componentes SOLO si no existen ya en escena.
//  Si el diseñador los ha colocado manualmente con configuración específica
//  (capas de física, referencias al jugador, etc.), este bootstrap los respeta.
//
//  Orden -195: antes que SistemaMisiones (que arranca en Awake estándar) y
//  antes que SceneBootstrapper (-200 no queremos competir, -195 es suficiente).
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

[DefaultExecutionOrder(-195)]
public sealed class BootstrapMisiones : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (SistemaMisiones.Instance == null) return;   // sin sistema de misiones → no hacer nada

        int creados = 0;

        // SistemaGrafitis — misiones M03, M09 (pintadas, pegatinas)
        if (FindFirstObjectByType<SistemaGrafitis>() == null)
        {
            new GameObject("SistemaGrafitis").AddComponent<SistemaGrafitis>();
            creados++;
        }

        // SistemaApoyoPopular — todas las misiones (recompensas de apoyo)
        if (FindFirstObjectByType<SistemaApoyoPopular>() == null)
        {
            new GameObject("SistemaApoyoPopular").AddComponent<SistemaApoyoPopular>();
            creados++;
        }

        // M06 RadioAskatasuna es una clase Mision (no un MonoBehaviour),
        // no necesita instanciación independiente — la maneja SistemaMisiones.

        if (creados > 0)
            Debug.Log($"[BootstrapMisiones] {creados} sistemas de misiones auto-creados " +
                "(no estaban en escena). Colócalos manualmente si necesitan config específica.");
    }
}
