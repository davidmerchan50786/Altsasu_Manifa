// Assets/Scripts/SistemaMisiones.cs
// ═══════════════════════════════════════════════════════════════════════════
//  Sistema de misiones mínimo — estructura event-driven.
//
//  Una misión = lista de objetivos secuenciales.
//  Cada objetivo tiene: descripción, condición (delegate bool), callback al completar.
//
//  Misiones incluidas:
//    M01_RobarCoche    — "Roba el Interceptor" → wanted 1★
//    M02_HuirPolicia   — "Escapa de la zona" → bonus dinero
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SistemaMisiones : SingletonMono<SistemaMisiones>
{
    protected override bool DestroyGameObjectOnDuplicate => true;

    // UI: OnGUI puro — sin dependencia de TextMeshPro
    // Si en el futuro se añade TMPro, crear un CanvasGUI hijo y asignar manualmente.

    // ── Estado ───────────────────────────────────────────────────────────
    private Mision _misionActual;
    private int    _objetivoActual;
    private bool   _enMision;

    public static event System.Action<string> OnMisionIniciada;
    public static event System.Action<string> OnObjetivoCompletado;
    public static event System.Action<string> OnMisionCompletada;

    // ─────────────────────────────────────────────────────────────────────

    private void Start()
    {
        // Auto-iniciar M01 cuando el juego arranca
        StartCoroutine(IniciarM01ConDelay());
    }

    private IEnumerator IniciarM01ConDelay()
    {
        yield return new WaitForSeconds(3f); // dar tiempo al terreno y al jugador
        IniciarMision(new Mision_RobarCoche());
    }

    private void Update()
    {
        if (!_enMision || _misionActual == null) return;
        var obj = _misionActual.Objetivos[_objetivoActual];
        if (obj.Condicion != null && obj.Condicion())
            StartCoroutine(CompletarObjetivo());
    }

    // ─────────────────────────────────────────────────────────────────────

    public void IniciarMision(Mision mision)
    {
        _misionActual  = mision;
        _objetivoActual = 0;
        _enMision = true;
        mision.AlIniciar?.Invoke();
        MostrarTexto(mision.Nombre, mision.Objetivos[0].Descripcion);
        OnMisionIniciada?.Invoke(mision.Nombre);
        AlsasuaLogger.Info("Misiones", $"Iniciada: {mision.Nombre}");
    }

    private IEnumerator CompletarObjetivo()
    {
        var obj = _misionActual.Objetivos[_objetivoActual];
        obj.AlCompletar?.Invoke();
        OnObjetivoCompletado?.Invoke(obj.Descripcion);
        AlsasuaLogger.Info("Misiones", $"✅ Objetivo: {obj.Descripcion}");
        MostrarTexto(_misionActual.Nombre, $"✅ {obj.Descripcion}");

        yield return new WaitForSeconds(2f);

        _objetivoActual++;
        if (_objetivoActual >= _misionActual.Objetivos.Count)
        {
            _enMision = false;
            _misionActual.AlCompletar?.Invoke();
            OnMisionCompletada?.Invoke(_misionActual.Nombre);
            AlsasuaLogger.Info("Misiones", $"🏆 Misión completada: {_misionActual.Nombre}");
            MostrarTexto("¡MISIÓN COMPLETADA!", _misionActual.Nombre);
            yield return new WaitForSeconds(4f);
            OcultarTexto();

            // Encadenar siguiente misión
            if (_misionActual.SiguienteMision != null)
                IniciarMision(_misionActual.SiguienteMision);
        }
        else
        {
            MostrarTexto(_misionActual.Nombre, _misionActual.Objetivos[_objetivoActual].Descripcion);
        }
    }

    // ─────────────────────────────────────────────────────────────────────

    // Texto en pantalla (sin dependencia TMPro)
    private string _guiTitulo = "", _guiObjetivo = "";
    private float  _guiAlpha  = 0f;

    private void MostrarTexto(string titulo, string objetivo)
    {
        _guiTitulo   = titulo;
        _guiObjetivo = objetivo;
        _guiAlpha    = 1f;
        Debug.Log($"[MISIÓN] {titulo} — {objetivo}");
    }

    private void OcultarTexto() => _guiAlpha = 0f;

    private void OnGUI()
    {
        if (!_enMision || _guiAlpha <= 0f) return;
        // Fade out gradual
        _guiAlpha = Mathf.MoveTowards(_guiAlpha, _enMision ? 1f : 0f, Time.deltaTime * 0.3f);

        var col  = new Color(0f, 0f, 0f, 0.65f * _guiAlpha);
        var rect = new Rect(20, Screen.height - 110, 440, 90);
        GUI.color = col;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = new Color(1f, 1f, 1f, _guiAlpha);
        var style = new GUIStyle(GUI.skin.label)
            { fontSize = 13, richText = true, alignment = TextAnchor.MiddleLeft,
              padding   = new RectOffset(10, 10, 8, 8) };
        GUI.Label(rect, $"<b>{_guiTitulo}</b>\n<color=#FFDD44>▶ {_guiObjetivo}</color>", style);
        GUI.color = Color.white;
    }
}

// ─────────────────────────────────────────────────────────────────────────
//  CLASES BASE
// ─────────────────────────────────────────────────────────────────────────

public class Objetivo
{
    public string       Descripcion;
    public System.Func<bool>   Condicion;   // se evalúa cada frame — devuelve true cuando se cumple
    public System.Action       AlCompletar;
}

public abstract class Mision
{
    public abstract string         Nombre { get; }
    public abstract List<Objetivo> Objetivos { get; }
    public virtual  Mision         SiguienteMision => null;
    public virtual  System.Action  AlIniciar       => null;
    public virtual  System.Action  AlCompletar     => null;
}

// ─────────────────────────────────────────────────────────────────────────
//  M01 — ROBAR EL INTERCEPTOR
// ─────────────────────────────────────────────────────────────────────────

public class Mision_RobarCoche : Mision
{
    public override string Nombre => "El Coche de la Ertzaintza";

    private bool _dentroDelCoche = false;
#pragma warning disable CS0414
    private bool _escapado       = false;
#pragma warning restore CS0414
    private Vector3 _posInicio;

    public override System.Action AlIniciar => () =>
    {
        _posInicio = Vector3.zero;
        var jGO = GameObject.FindGameObjectWithTag("Player");
        if (jGO != null) _posInicio = jGO.transform.position;

        // Suscribirse al evento de entrar en vehículo
        ControladorVehiculoJugador.OnJugadorEntro += _ => _dentroDelCoche = true;
        ControladorVehiculoJugador.OnJugadorSalio += _ => _dentroDelCoche = false;
    };

    public override List<Objetivo> Objetivos => new List<Objetivo>
    {
        new Objetivo
        {
            Descripcion = "Roba el coche de la Policía Foral",
            Condicion   = () => _dentroDelCoche,
            AlCompletar = () =>
            {
                ServiceLocator.Get<IWantedSystem>()?.AumentarBusqueda(1);
                // Alertar a los civiles cercanos
                var jug = AltsasuCore.Jugador;
                if (jug != null) SistemaIA.AlertarCercanos(jug.position, 30f);
            }
        },
        new Objetivo
        {
            Descripcion = "Escapa de la zona — alejar 150m del punto de inicio",
            Condicion   = () =>
            {
                var veh = Object.FindFirstObjectByType<ControladorVehiculoJugador>();
                if (veh == null || !veh.JugadorDentro) return false;
                return Vector3.Distance(veh.transform.position, _posInicio) > 150f;
            },
            AlCompletar = () =>
            {
                ServiceLocator.Get<IEconomyService>()?.GanarDinero(500);
                ServiceLocator.Get<IWantedSystem>()?.AumentarBusqueda(1);
            }
        }
    };

    public override Mision SiguienteMision => new Mision_HuirPolicia();
    // Cadena completa: M01 → M02 → M03…M12 (ver MisionesAltsasua.cs)
}

// ─────────────────────────────────────────────────────────────────────────
//  M02 — HUIR DE LA POLICÍA
// ─────────────────────────────────────────────────────────────────────────

public class Mision_HuirPolicia : Mision
{
    public override string Nombre => "No te atrapen";
    // Cadena: M02 → M03 (ver MisionesAltsasua.cs)
    public override Mision SiguienteMision => new Mision_PintadaPlaza();

    private float _timerSinBuscado = 0f;
    private const float TIEMPO_PARA_ESCAPAR = 30f; // 30 segundos sin nivel wanted

    public override List<Objetivo> Objetivos => new List<Objetivo>
    {
        new Objetivo
        {
            Descripcion = $"Mantén el nivel de búsqueda bajo durante {TIEMPO_PARA_ESCAPAR}s",
            Condicion   = () =>
            {
                var wanted = ServiceLocator.Get<IWantedSystem>();
                if (wanted == null) return false;
                if (wanted.NivelBusqueda == 0)
                    _timerSinBuscado += Time.deltaTime;
                else
                    _timerSinBuscado = 0f;
                return _timerSinBuscado >= TIEMPO_PARA_ESCAPAR;
            },
            AlCompletar = () =>
            {
                ServiceLocator.Get<IEconomyService>()?.GanarDinero(1000);
            }
        }
    };
}
