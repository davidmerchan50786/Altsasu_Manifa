// Assets/Scripts/Runtime/SistemaHUDCombate.cs
// ═══════════════════════════════════════════════════════════════════════════
//  HUD DE COMBATE — barras de vida sobre enemigos heridos cercanos.
//  Escanea IDamageable en radio cada 0,25 s; muestra una barra (proyectada a
//  pantalla) sobre los que están a tiro y con vida < máxima. Sin tocar cámara.
//  Capa RUNTIME. Auto-arranque; sin montaje en escena.
// ═══════════════════════════════════════════════════════════════════════════
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(118)]
public sealed class SistemaHUDCombate : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("SistemaHUDCombate");
        DontDestroyOnLoad(go);
        go.AddComponent<SistemaHUDCombate>();
    }

    const float RADIO = 38f, INTERVALO = 0.25f;

    Canvas _canvas; Camera _cam;
    float _t;
    readonly Collider[] _buf = new Collider[64];

    class Barra { public RectTransform rt; public Image fill; public MonoBehaviour mb; public IDamageable dmg; }
    readonly List<Barra> _activas = new();
    readonly Stack<Barra> _pool = new();

    void Awake() { ConstruirCanvas(); }

    void Update()
    {
        _t -= Time.deltaTime;
        if (_t > 0f) return;
        _t = INTERVALO;
        Reescanear();
    }

    void Reescanear()
    {
        var jug = AltsasuCore.Jugador;
        if (jug == null) return;

        var vistos = new HashSet<MonoBehaviour>();
        int n = Physics.OverlapSphereNonAlloc(jug.position, RADIO, _buf);
        for (int i = 0; i < n; i++)
        {
            if (_buf[i] == null) continue;
            var d = _buf[i].GetComponentInParent<IDamageable>();
            if (d == null || d.EstaMuerto) continue;
            var mb = d as MonoBehaviour;
            if (mb == null || mb.GetComponent<ControladorJugador>() != null) continue;  // no el jugador
            if (d.VidaMax <= 0 || d.Vida >= d.VidaMax) continue;                          // solo heridos
            if (!vistos.Add(mb)) continue;
            if (!_activas.Exists(b => b.mb == mb)) Asignar(mb, d);
        }
        // soltar barras de enemigos ya fuera/muertos
        for (int i = _activas.Count - 1; i >= 0; i--)
        {
            var b = _activas[i];
            if (b.mb == null || b.dmg.EstaMuerto || !vistos.Contains(b.mb)) Soltar(i);
        }
    }

    void Asignar(MonoBehaviour mb, IDamageable d)
    {
        var b = _pool.Count > 0 ? _pool.Pop() : CrearBarra();
        b.mb = mb; b.dmg = d; b.rt.gameObject.SetActive(true);
        _activas.Add(b);
    }
    void Soltar(int i) { var b = _activas[i]; b.rt.gameObject.SetActive(false); b.mb = null; b.dmg = null; _activas.RemoveAt(i); _pool.Push(b); }

    void LateUpdate()
    {
        if (_cam == null) { _cam = Camera.main; if (_cam == null) return; }
        for (int i = _activas.Count - 1; i >= 0; i--)
        {
            var b = _activas[i];
            if (b.mb == null || b.dmg.EstaMuerto) { Soltar(i); continue; }
            Vector3 sp = _cam.WorldToScreenPoint(b.mb.transform.position + Vector3.up * 2.0f);
            if (sp.z < 0f) { b.rt.gameObject.SetActive(false); continue; }
            b.rt.gameObject.SetActive(true);
            b.rt.position = sp;
            b.fill.fillAmount = Mathf.Clamp01((float)b.dmg.Vida / b.dmg.VidaMax);
        }
    }

    Barra CrearBarra()
    {
        var go = new GameObject("Barra", typeof(RectTransform));
        var rt = (RectTransform)go.transform; rt.SetParent(_canvas.transform, false);
        rt.sizeDelta = new Vector2(64, 7);
        go.AddComponent<Image>().color = new Color(0, 0, 0, 0.6f);   // fondo

        var fgGO = new GameObject("Fill", typeof(RectTransform));
        var frt = (RectTransform)fgGO.transform; frt.SetParent(rt, false);
        frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one; frt.offsetMin = new Vector2(1,1); frt.offsetMax = new Vector2(-1,-1);
        var fill = fgGO.AddComponent<Image>();
        fill.color = new Color(0.85f, 0.2f, 0.18f);
        fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Horizontal; fill.fillOrigin = 0; fill.fillAmount = 1f;

        return new Barra { rt = rt, fill = fill };
    }

    void ConstruirCanvas()
    {
        var go = new GameObject("HUDCombate_Canvas");
        go.transform.SetParent(transform, false);
        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 4700;
        _cam = Camera.main;
    }
}
