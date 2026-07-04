// Assets/Scripts/Runtime/SistemaEconomiaCriminal.cs
// ═══════════════════════════════════════════════════════════════════════════
//  ECONOMÍA CRIMINAL — impuesto revolucionario (extorsión) + tráfico (droga).
//  Mecánica de juego ficticia, estilo mundo abierto.
//
//    · Negocios bajo control pagan un INGRESO PERIÓDICO (cada 60 s de juego)
//      al dinero del jugador (IEconomyService).
//    · Extorsionar un negocio: pago inicial + sube la búsqueda y la paranoia,
//      y baja algo el apoyo popular (coacción).
//    · Trapichear (tecla N de prueba): golpe rápido de dinero con riesgo —
//      sube mucho la búsqueda/paranoia y, con probabilidad, hay redada y
//      pierdes el alijo.
//
//  Capa RUNTIME. Auto-arranque; sin montaje en escena.
// ═══════════════════════════════════════════════════════════════════════════
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(93)]
public sealed class SistemaEconomiaCriminal : MonoBehaviour
{
    public static SistemaEconomiaCriminal I { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("SistemaEconomiaCriminal");
        DontDestroyOnLoad(go);
        go.AddComponent<SistemaEconomiaCriminal>();
    }

    const float PERIODO = 60f;   // s entre cobros

    readonly List<Negocio> _negocios = new();
    float _t;
    float _tAviso; string _aviso;

    void Awake() { if (I != null) { Destroy(gameObject); return; } I = this; }

    public void Registrar(Negocio n) { if (n != null && !_negocios.Contains(n)) _negocios.Add(n); }
    public void Quitar(Negocio n) { _negocios.Remove(n); }

    // ── Extorsión ─────────────────────────────────────────────────────────
    public void Extorsionar(Negocio n)
    {
        if (n == null || n.estado != Negocio.Estado.Libre) return;
        n.PonerBajoControl();

        var eco = ServiceLocator.Get<IEconomyService>();
        int inicial = Mathf.RoundToInt(n.IngresoMin * 2 * SistemaProgresion.MultiplicadorIngresos);
        eco?.GanarDinero(inicial);

        ServiceLocator.Get<IWantedSystem>()?.AumentarBusqueda(Mathf.Max(0, 1 - SistemaProgresion.ReduccionCalor));
        SistemaApoyoPopular.Instance?.SumarParanoia(10f);
        SistemaApoyoPopular.Instance?.RestarApoyo(2f, "extorsión");

        Avisar($"Impuesto cobrado: {n.nombre}  (+{inicial} €, paga {n.IngresoMin}/min)");
    }

    // ── Tráfico ───────────────────────────────────────────────────────────
    public void Trapichear()
    {
        var eco = ServiceLocator.Get<IEconomyService>();
        if (eco == null) return;

        bool redada = Random.value < 0.2f;
        ServiceLocator.Get<IWantedSystem>()?.AumentarBusqueda(redada ? 3 : 2);
        SistemaApoyoPopular.Instance?.SumarParanoia(15f);
        SistemaApoyoPopular.Instance?.RestarApoyo(3f, "tráfico");

        if (redada) { Avisar("¡Redada! Pierdes el alijo y te buscan más."); return; }

        int ganancia = Mathf.RoundToInt(Random.Range(150, 400) * SistemaDiaNoche.FactorTrapicheo);
        eco.GanarDinero(ganancia);
        Avisar($"Trapicheo cerrado: +{ganancia} €");
    }

    // ── Cobro periódico ───────────────────────────────────────────────────
    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb.nKey.wasPressedThisFrame) Trapichear();

        _t += Time.deltaTime;
        if (_t >= PERIODO)
        {
            _t = 0f;
            int total = 0;
            foreach (var n in _negocios)
                if (n != null && n.estado == Negocio.Estado.Extorsionado)
                    total += Mathf.RoundToInt(n.IngresoMin * SistemaDiaNoche.FactorIngresoNegocio(n.tipo));
            total = Mathf.RoundToInt(total * SistemaProgresion.MultiplicadorIngresos);
            if (total > 0)
            {
                ServiceLocator.Get<IEconomyService>()?.GanarDinero(total);
                Avisar($"Impuesto revolucionario: +{total} € de tus negocios");
            }
        }

        if (_tAviso > 0f) _tAviso -= Time.unscaledDeltaTime;
    }

    void Avisar(string s) { _aviso = s; _tAviso = 3f; }
    void OnGUI()
    {
        if (_tAviso <= 0f) return;
        var st = new GUIStyle(GUI.skin.box) { fontSize = 15, alignment = TextAnchor.MiddleCenter };
        st.normal.textColor = Color.white;
        GUI.Box(new Rect(Screen.width * 0.5f - 230, Screen.height - 90, 460, 34), _aviso, st);
    }
}
