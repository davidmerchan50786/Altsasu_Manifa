// Assets/Scripts/Editor/DirectorMundoWindow.cs
// ═══════════════════════════════════════════════════════════════════════════
//  DIRECTOR DE MUNDO — centro de mando GIS (UI Toolkit / UIElements)
//
//  Unifica el pipeline del Mosaico V2 en un EditorWindow con dos paneles:
//    · IZQUIERDA: botones (USS) que lanzan los scripts Python nativos
//      (DescargarMDT_Mosaico.py / ValidarMosaicoV2.py) vía System.Diagnostics.Process,
//      capturando StandardOutput en streaming → log + barra de progreso real.
//    · DERECHA: GridTilesElement, un VisualElement custom que pinta los 48 tiles
//      del mundo 14.4×14.4 km (posición real desde manifest_v2.json) y los colorea
//      según validation_report.json (verde = auditoría pasada).
//
//  ── PROCESOS EXTERNOS ─────────────────────────────────────────────────────────
//  Process corre en hilos del pool; UI Toolkit SOLO se toca en el hilo principal.
//  Las líneas de salida se encolan (ConcurrentQueue) y se drenan en
//  EditorApplication.update → cero acceso a UI desde hilos secundarios.
//
//  ── PROGRESO ──────────────────────────────────────────────────────────────────
//  Tus scripts no emiten una barra estándar (no tqdm). El parser busca '%' o
//  '[n/m]'; si no hay, avanza un "latido" suave por línea (estimado, tope 95 %) y
//  salta a 100 % al terminar. Si añades 'print(f"[{i}/{n}] …")' a los .py, la barra
//  pasa a ser exacta sin tocar este código.
//
//  Capa EDITOR.
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

public sealed class DirectorMundoWindow : EditorWindow
{
    const string RUTA_UXML = "Assets/Scripts/Editor/DirectorMundo.uxml";
    const string RUTA_USS  = "Assets/Scripts/Editor/DirectorMundo.uss";
    const string PREF_PYTHON = "Alsasua.DirectorMundo.Python";
    const string FIN = " __FIN__";   // centinela: ambos streams cerrados

    // UI
    ProgressBar  _barra;
    Label        _estado;
    ScrollView   _log;
    Button       _btnDescargar, _btnValidar, _btnCancelar, _btnRefrescar;
    TextField    _pyExe;
    GridTilesElement _grid;

    // Proceso
    Process _proc;
    readonly ConcurrentQueue<string> _cola = new ConcurrentQueue<string>();
    int   _streamsAbiertos;
    bool  _corriendo;
    float _progreso;
    bool  _validando;        // el proceso en curso es la validación (→ refrescar grid al acabar)

    static readonly Regex RX_PCT  = new Regex(@"(\d{1,3})\s*%", RegexOptions.Compiled);
    static readonly Regex RX_FRAC = new Regex(@"\[(\d+)\s*/\s*(\d+)\]", RegexOptions.Compiled);

    [MenuItem("Tools/Alsasua/🌍 Director de Mundo (GIS)", priority = -20)]
    public static void Abrir()
    {
        var w = GetWindow<DirectorMundoWindow>();
        w.titleContent = new GUIContent("Director de Mundo");
        w.minSize = new Vector2(820, 480);
    }

    void CreateGUI()
    {
        var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(RUTA_UXML);
        if (uxml == null)
        {
            rootVisualElement.Add(new Label($"No se encontró {RUTA_UXML}"));
            return;
        }
        uxml.CloneTree(rootVisualElement);

        var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(RUTA_USS);
        if (uss != null) rootVisualElement.styleSheets.Add(uss);

        // ── Referencias ──
        _barra        = rootVisualElement.Q<ProgressBar>("barra");
        _estado       = rootVisualElement.Q<Label>("lbl-estado");
        _log          = rootVisualElement.Q<ScrollView>("log");
        _btnDescargar = rootVisualElement.Q<Button>("btn-descargar");
        _btnValidar   = rootVisualElement.Q<Button>("btn-validar");
        _btnCancelar  = rootVisualElement.Q<Button>("btn-cancelar");
        _btnRefrescar = rootVisualElement.Q<Button>("btn-refrescar");
        _pyExe        = rootVisualElement.Q<TextField>("py-exe");

        _pyExe.value = EditorPrefs.GetString(PREF_PYTHON, "python");
        _pyExe.RegisterValueChangedCallback(e => EditorPrefs.SetString(PREF_PYTHON, e.newValue));

        _btnDescargar.clicked += () => EjecutarPython("Tools/DescargarMDT_Mosaico.py", "", "Descargando MDT", false);
        _btnValidar.clicked   += () => EjecutarPython("Tools/ValidarMosaicoV2.py",    "", "Validando mosaico", true);
        _btnCancelar.clicked  += Cancelar;
        _btnRefrescar.clicked += () => _grid?.Refrescar();

        // ── Grid custom en el panel derecho ──
        _grid = new GridTilesElement();
        _grid.style.flexGrow = 1;
        rootVisualElement.Q<VisualElement>("contenedor-grid").Add(_grid);

        ActualizarBotones();
    }

    void OnDisable()
    {
        if (_corriendo) { EditorApplication.update -= DrenarCola; MatarProceso(); }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  EJECUCIÓN DE PROCESOS EXTERNOS
    // ═════════════════════════════════════════════════════════════════════════

    void EjecutarPython(string scriptRel, string args, string etiqueta, bool esValidacion)
    {
        if (_corriendo) { LogLinea("⚠ Ya hay un proceso en curso."); return; }

        string root   = Directory.GetParent(Application.dataPath)!.FullName;
        string script = Path.GetFullPath(Path.Combine(root, scriptRel));
        if (!File.Exists(script)) { LogLinea($"✗ No existe el script: {script}"); return; }

        var psi = new ProcessStartInfo
        {
            FileName               = string.IsNullOrWhiteSpace(_pyExe.value) ? "python" : _pyExe.value,
            Arguments              = $"-u \"{script}\" {args}",   // -u = stdout sin buffer → streaming real
            WorkingDirectory       = root,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding  = Encoding.UTF8,
        };

        _proc = new Process { StartInfo = psi };
        _proc.OutputDataReceived += (s, e) => { if (e.Data == null) CerrarStream(); else _cola.Enqueue(e.Data); };
        _proc.ErrorDataReceived  += (s, e) => { if (e.Data == null) CerrarStream(); else _cola.Enqueue("⚠ " + e.Data); };

        try { _proc.Start(); }
        catch (Exception ex) { LogLinea($"✗ No se pudo lanzar '{psi.FileName}': {ex.Message}"); _proc = null; return; }

        _streamsAbiertos = 2;
        _proc.BeginOutputReadLine();
        _proc.BeginErrorReadLine();

        _corriendo = true;
        _validando = esValidacion;
        _progreso  = 0f;
        SetBarra(0f, etiqueta);
        _estado.text = etiqueta + "…";
        LimpiarLog();
        LogLinea($"▶ {psi.FileName} {psi.Arguments}");
        EditorApplication.update += DrenarCola;
        ActualizarBotones();
    }

    // Llamado desde hilos del pool: cuando AMBOS streams cierran, encola el centinela.
    void CerrarStream()
    {
        if (Interlocked.Decrement(ref _streamsAbiertos) == 0) _cola.Enqueue(FIN);
    }

    // Hilo principal (EditorApplication.update): drena la cola y toca la UI.
    void DrenarCola()
    {
        while (_cola.TryDequeue(out string linea))
        {
            if (linea == FIN) { FinalizarProceso(); return; }
            LogLinea(linea);
            ActualizarProgreso(linea);
        }
    }

    void FinalizarProceso()
    {
        EditorApplication.update -= DrenarCola;

        int codigo = -1;
        try { _proc?.WaitForExit(2000); codigo = _proc?.ExitCode ?? -1; } catch { /* ya cerrado */ }
        _proc?.Dispose();
        _proc = null;
        _corriendo = false;

        bool ok = codigo == 0;
        SetBarra(1f, ok ? "Completado" : $"Terminó con código {codigo}");
        _estado.text = ok ? "✅ Proceso completado" : $"✗ Falló (código {codigo})";
        LogLinea(ok ? "■ FIN (código 0)" : $"■ FIN (código {codigo})");

        if (_validando) _grid?.Refrescar();   // recolorea el mapa con el nuevo informe
        ActualizarBotones();
    }

    void Cancelar()
    {
        if (!_corriendo) return;
        EditorApplication.update -= DrenarCola;
        MatarProceso();
        _corriendo = false;
        _estado.text = "⏹ Cancelado";
        SetBarra(0f, "Cancelado");
        ActualizarBotones();
    }

    void MatarProceso()
    {
        try { if (_proc != null && !_proc.HasExited) _proc.Kill(); } catch { }
        _proc?.Dispose();
        _proc = null;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  PROGRESO / LOG / BOTONES
    // ═════════════════════════════════════════════════════════════════════════

    void ActualizarProgreso(string linea)
    {
        var mp = RX_PCT.Match(linea);
        if (mp.Success && int.TryParse(mp.Groups[1].Value, out int pct))
        { SetBarra(Mathf.Clamp01(pct / 100f)); return; }

        var mf = RX_FRAC.Match(linea);
        if (mf.Success &&
            float.TryParse(mf.Groups[1].Value, out float n) &&
            float.TryParse(mf.Groups[2].Value, out float d) && d > 0f)
        { SetBarra(Mathf.Clamp01(n / d)); return; }

        // Sin patrón parseable → latido suave (estimación; salta a 100 % al terminar).
        SetBarra(Mathf.Min(0.95f, _progreso + 0.01f));
    }

    void SetBarra(float v01, string titulo = null)
    {
        _progreso = v01;
        if (_barra == null) return;
        _barra.value = v01 * 100f;
        _barra.title = titulo ?? $"{Mathf.RoundToInt(v01 * 100f)} %";
    }

    void LogLinea(string txt)
    {
        if (_log == null) return;
        var l = new Label(txt);
        l.AddToClassList("log-linea");
        if (txt.StartsWith("✗") || txt.StartsWith("⚠")) l.AddToClassList("log-error");
        else if (txt.StartsWith("✅") || txt.StartsWith("■")) l.AddToClassList("log-ok");
        _log.Add(l);

        // Cap: conservar las últimas ~600 líneas.
        var cont = _log.contentContainer;
        while (cont.childCount > 600) cont.RemoveAt(0);
        _log.schedule.Execute(() => _log.scrollOffset = new Vector2(0, float.MaxValue));
    }

    void LimpiarLog() => _log?.Clear();

    void ActualizarBotones()
    {
        if (_btnDescargar == null) return;
        _btnDescargar.SetEnabled(!_corriendo);
        _btnValidar.SetEnabled(!_corriendo);
        _btnCancelar.SetEnabled(_corriendo);
    }
}
