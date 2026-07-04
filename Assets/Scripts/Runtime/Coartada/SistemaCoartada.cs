// Assets/Scripts/_Coartada~/SistemaCoartada.cs  (STAGING/DRAFT — carpeta ~ no se compila)
// ─────────────────────────────────────────────────────────────────────────────
//  "Perderse entre la gente": si el jugador está dentro de una ZonaCoartada y
//  NINGUNA autoridad lo ve, baja el wanted y la paranoia a un ritmo escalado por
//  la calidad del refugio y el apoyo popular (calle alta = te tapa mejor).
//  Al bajar la paranoia, los GuardiaCivil convertidos revierten solos
//  (SistemaParanoiaGuardiaCivil va por paranoia). Sinergia limpia.
//
//  Sin allocs en Update (buffer reutilizado). HUD mínimo en OnGUI.
// ─────────────────────────────────────────────────────────────────────────────
using System.Collections.Generic;
using UnityEngine;

public class SistemaCoartada : MonoBehaviour
{
    public static SistemaCoartada Instance { get; private set; }

    [Header("Detección de autoridad")]
    [Tooltip("Capa de policía / Guardia Civil que 'rompe' la coartada si te ve.")]
    public LayerMask capaAutoridad;
    [Tooltip("Capa de muros/obstáculos para la línea de visión.")]
    public LayerMask capaObstaculos;
    public float rangoVision = 25f;
    public float alturaOjos = 1.6f;

    [Header("Ritmo de enfriado (a calidad 1, apoyo 0)")]
    public float estrellasPorSeg = 0.6f;
    public float paranoiaPorSeg  = 6f;

    [Tooltip("Panel único de tuning. Si se asigna, manda sobre el factor de apoyo del enfriado.")]
    public SintoniaAltsasu sintonia;

    public bool Escondido { get; private set; }
    public ZonaCoartada ZonaActual { get; private set; }

    static readonly List<ZonaCoartada> _zonas = new();
    public static void Registrar(ZonaCoartada z)   { if (!_zonas.Contains(z)) _zonas.Add(z); }
    public static void Desregistrar(ZonaCoartada z) => _zonas.Remove(z);

    readonly Collider[] _buf = new Collider[16];
    float _acumEstrellas;

    void Awake() { if (Instance != null && Instance != this) { Destroy(this); return; } Instance = this; }

    void Update()
    {
        Vector3 jug = GeoDataAlsasua.JugadorPos();
        if (jug == Vector3.zero) { Escondido = false; ZonaActual = null; return; }

        ZonaActual = ZonaEn(jug);
        Escondido = ZonaActual != null && !VistoPorAutoridad(jug);
        if (!Escondido) return;

        float apoyo = SistemaApoyoPopular.Instance != null ? SistemaApoyoPopular.Instance.apoyo : 0f;
        float factorApoyo = sintonia != null
            ? sintonia.CoartadaRitmo(apoyo) / Mathf.Max(0.001f, sintonia.coartadaRitmoBase)
            : (1f + apoyo / 100f);
        float mult = ZonaActual.calidad * factorApoyo;   // calidad + calle = más rápido

        // enfriar paranoia (revierte tricornios de rebote)
        SistemaApoyoPopular.Instance?.RestarParanoia(paranoiaPorSeg * mult * Time.deltaTime);

        // enfriar wanted: acumular hasta una estrella entera
        _acumEstrellas += estrellasPorSeg * mult * Time.deltaTime;
        if (_acumEstrellas >= 1f)
        {
            _acumEstrellas -= 1f;
            ServiceLocator.Get<IWantedSystem>()?.AumentarBusqueda(-1);   // clampa a 0
        }
    }

    ZonaCoartada ZonaEn(Vector3 p)
    {
        for (int i = 0; i < _zonas.Count; i++)
            if (_zonas[i] && _zonas[i].Contiene(p)) return _zonas[i];
        return null;
    }

    bool VistoPorAutoridad(Vector3 jug)
    {
        Vector3 ojoJug = jug + Vector3.up * alturaOjos;
        int n = Physics.OverlapSphereNonAlloc(jug, rangoVision, _buf, capaAutoridad);
        for (int i = 0; i < n; i++)
        {
            Vector3 ojoGC = _buf[i].transform.position + Vector3.up * alturaOjos;
            // si NO hay obstáculo entre el guardia y el jugador → te ve → no hay coartada
            if (!Physics.Linecast(ojoGC, ojoJug, capaObstaculos)) return true;
        }
        return false;
    }

    void OnGUI()
    {
        if (!Escondido) return;
        var st = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 13 };
        bool parpadeo = (Time.unscaledTime % 1f) < 0.5f;
        GUI.color = parpadeo ? new Color(0.3f, 0.9f, 0.3f) : Color.white;
        GUI.Label(new Rect(16, Screen.height - 40, 420, 24),
            $"🫥  ESCONDIDO en {ZonaActual.nombre} — enfriando…", st);
        GUI.color = Color.white;
    }
}
