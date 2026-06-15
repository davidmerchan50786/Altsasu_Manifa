// Assets/Scripts/Core/Simulacion/GlobalSimulationOrchestrator.cs
// ═══════════════════════════════════════════════════════════════════════════
//  GLOBAL SIMULATION ORCHESTRATOR — el "Director" (capa CORE)
//
//  Clase plana (NO MonoBehaviour) inyectada en el PlayerLoop de Unity 6 → un único
//  punto de entrada por frame, independiente del orden de los Update y sin un
//  GameObject que mantener. Se expone por ServiceLocator<IGlobalSimulationOrchestrator>.
//
//  Cada frame:
//    1. Muestrea telemetría (CPU frame-time EMA)
//    2. Ajusta FactorCarga (degrade dinámico con histéresis)
//    3. Despacha ticks: downsample por frecuencia + striding por FASE (reparte la
//       carga de los cientos de NPC entre frames) + slice de presupuesto blando
//    4. Evalúa Sim-LOD de una VENTANA ROTATORIA (no todos cada frame) con histéresis,
//       caps escalados por FactorCarga y oclusión aproximada (tras la cámara)
//
//  Layer-safe: solo toca ITickable / ISimulable. Nunca conoce PoliciaForalIA & co.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

public sealed class GlobalSimulationOrchestrator : IGlobalSimulationOrchestrator
{
    public static GlobalSimulationOrchestrator Instancia { get; private set; }
    public ConfiguracionSimulacion Config { get; } = new();

    sealed class EntradaTick { public ITickable t; public int fase; public float acum; }
    readonly List<EntradaTick> _ticks = new(256);
    readonly Dictionary<ITickable, EntradaTick> _idx = new(256);
    readonly List<ISimulable> _sims = new(1024);

    int _faseSiguiente, _frame, _cursorLOD;
    int _nActores, _nProxies, _nGhosts;
    float _factor = 1f;
    ServicioTelemetriaFrames _tele;
    GobernadorRender _render;        // gobernador de GPU: produce el radio de mundo dinámico
    Camera _cam;
    int _frameCam = -999;

    public float FactorCarga => _factor;
    public event System.Action<float> OnFactorCargaCambia;

    // ── Lectura de debug (para un overlay opcional) ──
    public int   NumTickables => _ticks.Count;
    public int   NumSimulables => _sims.Count;
    public int   NumActores   => _nActores;
    public int   NumProxies   => _nProxies;
    public int   NumGhosts    => _nGhosts;
    public float FrameMs      => _tele?.FrameMsSuavizado ?? 0f;

    // ════════════════════════════════════════════════════════════════════════
    //  BOOT — instala en el PlayerLoop y se registra como servicio
    // ════════════════════════════════════════════════════════════════════════
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        Desinstalar();                       // limpia cualquier delegado viejo (re-Play sin domain reload)
        Instancia = new GlobalSimulationOrchestrator();
        Instancia._tele = new ServicioTelemetriaFrames(
            Instancia.Config.emaAlpha, Instancia.Config.presupuestoMs);

        ServiceLocator.Registrar<IGlobalSimulationOrchestrator>(Instancia);
        ServiceLocator.Registrar<ITelemetryService>(Instancia._tele);

        // Hermano de GPU: misma autoridad de frame, misma telemetría (ya muestreada aquí).
        Instancia._render = GobernadorRender.CrearYRegistrar();

        Instalar(Instancia.TickFrame);
        Application.quitting += Desinstalar;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  REGISTRO
    // ════════════════════════════════════════════════════════════════════════
    public void Registrar(ITickable t)
    {
        if (t == null || _idx.ContainsKey(t)) return;
        var e = new EntradaTick { t = t, fase = _faseSiguiente++ };  // fase incremental → carga plana
        _ticks.Add(e); _idx[t] = e;
    }

    public void Desregistrar(ITickable t)
    {
        if (t != null && _idx.TryGetValue(t, out var e)) { _ticks.Remove(e); _idx.Remove(t); }
    }

    public void Registrar(ISimulable s)   { if (s != null && !_sims.Contains(s)) _sims.Add(s); }
    public void Desregistrar(ISimulable s){ if (s != null) _sims.Remove(s); }

    // ════════════════════════════════════════════════════════════════════════
    //  FRAME
    // ════════════════════════════════════════════════════════════════════════
    void TickFrame()
    {
        _frame++;
        _tele.Muestrear();
        AjustarFactor();
        // Gobernador de GPU: con la telemetría ya muestreada, recalcula el radio de mundo.
        _render?.Actualizar(_tele.FrameMsSuavizado, _tele.GpuMsSuavizado);
        DespacharTicks();
        EvaluarLOD();
    }

    void AjustarFactor()
    {
        float ema = _tele.FrameMsSuavizado;
        float lim = Config.presupuestoMs;
        float prev = _factor;
        if      (ema > lim * Config.degradeMul) _factor -= Config.pasoDegrade;  // sobrecarga → rápido
        else if (ema < lim * Config.recoverMul) _factor += Config.pasoRecover;  // holgura  → lento
        _factor = Mathf.Clamp(_factor, Config.factorMin, 1f);
        if (!Mathf.Approximately(prev, _factor)) OnFactorCargaCambia?.Invoke(_factor);
    }

    // Downsample por frecuencia + striding por fase + slice de presupuesto blando.
    void DespacharTicks()
    {
        float dt = Time.deltaTime;
        var sw = Stopwatch.StartNew();
        bool sobrePresupuesto = false;
        int umbralDiferible = Frecuencia.Hz5.PeriodoFrames();   // ≥ Hz5 (lejano/lento) es diferible

        for (int i = _ticks.Count - 1; i >= 0; i--)
        {
            var e = _ticks[i];
            if (e.t is Object o && o == null) { _ticks.RemoveAt(i); _idx.Remove(e.t); continue; }

            e.acum += dt;
            int periodo = e.t.Frecuencia.PeriodoFrames();
            if ((_frame + e.fase) % periodo != 0) continue;             // no le toca este frame

            // Bajo presión, los de baja frecuencia esperan a su próxima ventana (degrade limpio).
            if (sobrePresupuesto && periodo >= umbralDiferible) continue;

            e.t.Tick(e.acum);
            e.acum = 0f;

            if (!sobrePresupuesto && sw.Elapsed.TotalMilliseconds > Config.sliceSimMs)
                sobrePresupuesto = true;
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  SIM-LOD — ventana rotatoria (no todos cada frame)
    // ════════════════════════════════════════════════════════════════════════
    void EvaluarLOD()
    {
        int n = _sims.Count;
        if (n == 0) { _nActores = _nProxies = _nGhosts = 0; return; }

        // Sin cámara no degradamos a nadie (evita ghostear el mundo entero por error).
        if (_cam == null || _frame - _frameCam > 120) { _cam = Camera.main; _frameCam = _frame; }
        if (_cam == null) return;
        Vector3 camPos = _cam.transform.position;
        Vector3 camFwd = _cam.transform.forward;

        RecontarNiveles();
        int capActores = Mathf.RoundToInt(Config.maxActores * _factor);
        int capProxies = Mathf.RoundToInt(Config.maxProxies * _factor);

        int porFrame = Mathf.Max(1, Mathf.CeilToInt(n / (float)Config.ventanaLODFrames));
        int promos = 0;

        for (int k = 0; k < porFrame && _sims.Count > 0; k++)
        {
            if (_cursorLOD >= _sims.Count) _cursorLOD = 0;
            var s = _sims[_cursorLOD];
            if (s is Object o && o == null) { _sims.RemoveAt(_cursorLOD); continue; }

            NivelSim actual = s.Nivel;
            NivelSim deseado = Calcular(s, camPos, camFwd, actual);

            // Caps (escalados por carga): si no hay cupo para subir, se queda un nivel por debajo.
            if (deseado == NivelSim.Actor && _nActores >= capActores) deseado = NivelSim.Proxy;
            if (deseado == NivelSim.Proxy && _nProxies >= capProxies) deseado = NivelSim.Ghost;

            if (deseado != actual)
            {
                bool promocion = deseado < actual;   // menor enum = más detalle
                if (promocion && promos >= Config.maxPromosFrame) { _cursorLOD++; continue; }
                if (promocion) promos++;
                s.AplicarNivel(deseado);
                // mantener los contadores aproximados durante la pasada
                Ajustar(ref actual, -1); Ajustar(ref deseado, +1);
            }
            _cursorLOD++;
        }
    }

    NivelSim Calcular(ISimulable s, Vector3 camPos, Vector3 camFwd, NivelSim actual)
    {
        Vector3 dir = s.Posicion - camPos;
        float d = dir.magnitude;

        // Histéresis pegajosa: el nivel ACTUAL aguanta un poco más lejos → sin parpadeo en el borde.
        float rA = Config.radioActor * _factor + (actual == NivelSim.Actor ? Config.histActor : 0f);
        float rP = Config.radioProxy * _factor + (actual == NivelSim.Proxy ? Config.histProxy : 0f);

        NivelSim nivel = d <= rA ? NivelSim.Actor : (d <= rP ? NivelSim.Proxy : NivelSim.Ghost);

        // Oclusión aproximada GRATIS: lo que está detrás de la cámara y lejos baja un nivel.
        // (La oclusión real por edificios usaría el culling del BRG — trabajo futuro.)
        if (nivel != NivelSim.Ghost && d > Config.oclusionDesde && Vector3.Dot(camFwd, dir) < 0f)
            nivel = (NivelSim)((int)nivel + 1);

        return nivel;
    }

    void RecontarNiveles()
    {
        int a = 0, p = 0, g = 0;
        for (int i = 0; i < _sims.Count; i++)
        {
            var s = _sims[i];
            if (s is Object o && o == null) continue;
            switch (s.Nivel) { case NivelSim.Actor: a++; break; case NivelSim.Proxy: p++; break; default: g++; break; }
        }
        _nActores = a; _nProxies = p; _nGhosts = g;
    }

    void Ajustar(ref NivelSim nivel, int delta)
    {
        switch (nivel) { case NivelSim.Actor: _nActores += delta; break; case NivelSim.Proxy: _nProxies += delta; break; default: _nGhosts += delta; break; }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  INYECCIÓN EN EL PLAYERLOOP
    // ════════════════════════════════════════════════════════════════════════
    static void Instalar(PlayerLoopSystem.UpdateFunction fn)
    {
        var loop = PlayerLoop.GetCurrentPlayerLoop();
        var nuestro = new PlayerLoopSystem { type = typeof(GlobalSimulationOrchestrator), updateDelegate = fn };

        for (int i = 0; i < loop.subSystemList.Length; i++)
        {
            if (loop.subSystemList[i].type != typeof(Update)) continue;
            var sub = new List<PlayerLoopSystem>(loop.subSystemList[i].subSystemList);
            sub.Add(nuestro);                                  // tras los Update normales
            loop.subSystemList[i].subSystemList = sub.ToArray();
            break;
        }
        PlayerLoop.SetPlayerLoop(loop);
    }

    static void Desinstalar()
    {
        var loop = PlayerLoop.GetCurrentPlayerLoop();
        bool tocado = false;
        for (int i = 0; i < loop.subSystemList.Length; i++)
        {
            var sub = loop.subSystemList[i].subSystemList;
            if (sub == null) continue;
            var lst = new List<PlayerLoopSystem>(sub);
            if (lst.RemoveAll(x => x.type == typeof(GlobalSimulationOrchestrator)) > 0)
            {
                loop.subSystemList[i].subSystemList = lst.ToArray();
                tocado = true;
            }
        }
        if (tocado) PlayerLoop.SetPlayerLoop(loop);
    }

#if UNITY_EDITOR
    // En el editor, Stop NO dispara Application.quitting → desinstalar a mano para no
    // dejar un delegado muerto llamándose en edit mode.
    [UnityEditor.InitializeOnLoadMethod]
    static void HookEditor()
    {
        UnityEditor.EditorApplication.playModeStateChanged += s =>
        {
            if (s == UnityEditor.PlayModeStateChange.ExitingPlayMode) Desinstalar();
        };
    }
#endif
}
