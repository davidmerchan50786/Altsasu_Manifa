// Assets/Scripts/Runtime/DiagnosticoRendimiento.cs
// ═══════════════════════════════════════════════════════════════════════════
//  DIAGNÓSTICO DE RENDIMIENTO — vuelca al log el "qué" de un FPS bajo/0
//
//  Se auto-arranca en Play y, a los 8 s (en tiempo REAL → funciona aunque el
//  juego esté a 0 FPS o pausado), vuelca UNA vez al Console los números que
//  identifican el cuello de botella sin necesidad del Profiler:
//    · GPU/CPU/RAM del equipo
//    · nº de Renderers activos (Mesh/Skinned), luces con sombra, Terrains,
//      ParticleSystems, agentes de multitud
//    · quality level y FPS aproximado
//
//  Pega el bloque "[DIAG]" del Console y se sabe si es GPU débil, demasiados
//  draw calls, demasiadas luces con sombra, la multitud, etc.
//
//  Inofensivo: solo lee y loguea una vez. Bórralo cuando el FPS esté resuelto.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using System.Text;
using UnityEngine;

public sealed class DiagnosticoRendimiento : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<DiagnosticoRendimiento>() != null) return;
        var go = new GameObject("DiagnosticoRendimiento");
        DontDestroyOnLoad(go);
        go.AddComponent<DiagnosticoRendimiento>();
    }

    IEnumerator Start()
    {
        // Tiempo REAL: dispara aunque timeScale=0 (pausa) o el frame dure segundos.
        yield return new WaitForSecondsRealtime(8f);
        Volcar();
        // Segundo volcado a los 20 s por si el primero pilló todo a medio cargar.
        yield return new WaitForSecondsRealtime(12f);
        Volcar();
    }

    void Volcar()
    {
        var sb = new StringBuilder(512);
        sb.AppendLine("═══════════ [DIAG] RENDIMIENTO ═══════════");
        sb.AppendLine($"[DIAG] GPU: {SystemInfo.graphicsDeviceName} | {SystemInfo.graphicsMemorySize} MB VRAM | API {SystemInfo.graphicsDeviceType}");
        sb.AppendLine($"[DIAG] CPU: {SystemInfo.processorType} x{SystemInfo.processorCount} | RAM {SystemInfo.systemMemorySize} MB");

        var rends = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        int mesh = 0, skinned = 0, otros = 0;
        for (int i = 0; i < rends.Length; i++)
        {
            if (!rends[i].enabled) continue;
            if (rends[i] is MeshRenderer)             mesh++;
            else if (rends[i] is SkinnedMeshRenderer) skinned++;
            else                                      otros++;
        }

        int lucesSombra = 0, lucesTotal = 0;
        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (!l.isActiveAndEnabled) continue;
            lucesTotal++;
            if (l.shadows != LightShadows.None) lucesSombra++;
        }

        int terrains = Terrain.activeTerrains != null ? Terrain.activeTerrains.Length : 0;
        var pss = FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None);
        int psEmitiendo = 0;
        for (int i = 0; i < pss.Length; i++) if (pss[i].isEmitting) psEmitiendo++;

        int multitud = ServiceLocator.Get<ICrowdDensity>()?.TotalAgentes ?? -1;

        sb.AppendLine($"[DIAG] Renderers ACTIVOS: {mesh + skinned + otros}  (Mesh {mesh}, Skinned {skinned}, otros {otros})");
        sb.AppendLine($"[DIAG] Luces activas: {lucesTotal}  ·  CON SOMBRA: {lucesSombra}");
        sb.AppendLine($"[DIAG] Terrains activos: {terrains}  ·  ParticleSystems: {pss.Length} (emitiendo {psEmitiendo})");
        sb.AppendLine($"[DIAG] Multitud (ICrowdDensity): {(multitud >= 0 ? multitud.ToString() : "no registrada")}");
        sb.AppendLine($"[DIAG] Quality: nivel {QualitySettings.GetQualityLevel()} | shadowDist {QualitySettings.shadowDistance:F0} m | lodBias {QualitySettings.lodBias:F2}");

        // Gobernador de render (GPU) + streaming del mundo estático: si el fix #3/#4
        // está activo, aquí se ve el radio dinámico y cuántos edificios están ocultos.
        var gob = ServiceLocator.Get<IRenderBudgetGovernor>();
        sb.AppendLine(gob != null
            ? $"[DIAG] Render gob: factor {gob.FactorRender:F2} | radio {gob.RadioActivacion:F0} m (impostor {gob.RadioImpostor:F0} m) | GPU {gob.GpuMs:F1} ms {(gob.Saturado ? "SATURADO" : "ok")}"
            : "[DIAG] Render gob: NO registrado");
        var streamer = FindFirstObjectByType<StreamerMundoEstatico>();
        sb.AppendLine(streamer != null
            ? $"[DIAG] Streamer mundo: {streamer.Gestionados} gestionados (Activo {streamer.CuentaEstado(0)}, Impostor {streamer.CuentaEstado(1)}, Oculto {streamer.CuentaEstado(2)})"
            : "[DIAG] Streamer mundo: NO presente");

        sb.AppendLine($"[DIAG] FPS aprox: {(Time.unscaledDeltaTime > 0f ? 1f / Time.unscaledDeltaTime : 0f):F1}  (frame {Time.unscaledDeltaTime * 1000f:F0} ms)");
        sb.Append("══════════════════════════════════════════");

        Debug.Log(sb.ToString());
    }
}
