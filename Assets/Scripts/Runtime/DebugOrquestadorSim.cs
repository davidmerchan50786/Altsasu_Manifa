// Assets/Scripts/Runtime/DebugOrquestadorSim.cs
// ═══════════════════════════════════════════════════════════════════════════
//  DEBUG — overlay del Director de Simulación (capa RUNTIME)
//
//  El GlobalSimulationOrchestrator es una clase plana sin GameObject → no hay
//  inspector que mirar. Arrastra este componente a cualquier GO de la escena para
//  ver en vivo el frame-time, el FactorCarga y el reparto Actor/Proxy/Ghost.
//  Solo lee la API pública del orquestador (Core); no lo modifica.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

public sealed class DebugOrquestadorSim : MonoBehaviour
{
    [Tooltip("Muestra el panel del Director en pantalla.")]
    [SerializeField] bool mostrar = true;

    GUIStyle _st;

    void OnGUI()
    {
        if (!mostrar) return;
        var o = GlobalSimulationOrchestrator.Instancia;
        if (o == null) return;

        _st ??= new GUIStyle(GUI.skin.box)
        {
            fontSize = 12, alignment = TextAnchor.UpperLeft, padding = new RectOffset(8, 8, 6, 6)
        };

        var tele = ServiceLocator.Get<ITelemetryService>();
        float lim = tele?.PresupuestoMs ?? 0f;
        string txt =
            $"DIRECTOR DE SIMULACIÓN\n" +
            $"Frame CPU (EMA): {o.FrameMs:F1} / {lim:F1} ms\n" +
            $"FactorCarga: {o.FactorCarga:P0}\n" +
            $"Tickables: {o.NumTickables}   Simulables: {o.NumSimulables}\n" +
            $"Actores: {o.NumActores}   Proxies: {o.NumProxies}   Ghosts: {o.NumGhosts}";

        GUI.Box(new Rect(Screen.width - 330, 10, 320, 96), txt, _st);
    }
}
