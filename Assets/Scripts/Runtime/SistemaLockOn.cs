// Assets/Scripts/Runtime/SistemaLockOn.cs
// ═══════════════════════════════════════════════════════════════════════════
//  LOCK-ON — fijado de objetivo + asistencia de puntería (sin mover la cámara).
//
//    · CLIC CENTRAL (rueda) → fija/suelta el enemigo más centrado en vista
//      (≤40 m, ángulo ≤50°). Se marca con un retículo en pantalla.
//    · El melee y el disparo sesgan su dirección hacia el objetivo si está
//      dentro de un cono (aim assist) vía SistemaLockOn.AsistirDireccion.
//    · Se suelta solo si el objetivo muere, se aleja o lo pierdes de vista.
//
//  NO toca la cámara → no pelea con ControladorJugador. Capa RUNTIME.
//  Auto-arranque; sin montaje en escena.
// ═══════════════════════════════════════════════════════════════════════════
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(72)]
public sealed class SistemaLockOn : MonoBehaviour
{
    public static Transform   Objetivo    { get; private set; }
    public static IDamageable ObjetivoDmg { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("SistemaLockOn");
        DontDestroyOnLoad(go);
        go.AddComponent<SistemaLockOn>();
    }

    const float RADIO = 40f, ANG_MAX = 50f, ANG_PERDER = 70f;

    Camera _cam;
    Canvas _canvas; RectTransform _retic;
    readonly Collider[] _buf = new Collider[64];

    void Awake() { ConstruirUI(); }

    void Update()
    {
        if (_cam == null) { _cam = Camera.main; if (_cam == null) return; }
        var ms = Mouse.current;

        if (ms != null && ms.middleButton.wasPressedThisFrame)
        {
            if (Objetivo != null) Soltar(); else Adquirir();
        }

        // soltar si deja de ser válido
        if (Objetivo != null)
        {
            if (ObjetivoDmg == null || ObjetivoDmg.EstaMuerto) { Soltar(); return; }
            Vector3 v = Objetivo.position - _cam.transform.position;
            if (v.magnitude > RADIO * 1.3f || Vector3.Angle(_cam.transform.forward, v) > ANG_PERDER) Soltar();
        }
    }

    void LateUpdate()
    {
        if (Objetivo == null) { _retic.gameObject.SetActive(false); return; }
        if (_cam == null) return;
        Vector3 sp = _cam.WorldToScreenPoint(Objetivo.position + Vector3.up * 1.1f);
        if (sp.z < 0f) { _retic.gameObject.SetActive(false); return; }
        _retic.gameObject.SetActive(true);
        _retic.position = sp;
    }

    void Adquirir()
    {
        var jug = AltsasuCore.Jugador;
        if (jug == null) return;
        int n = Physics.OverlapSphereNonAlloc(jug.position, RADIO, _buf);
        float mejorAng = ANG_MAX; Transform mejor = null; IDamageable mejorD = null;
        for (int i = 0; i < n; i++)
        {
            if (_buf[i] == null) continue;
            var d = _buf[i].GetComponentInParent<IDamageable>();
            if (d == null || d.EstaMuerto) continue;
            var mb = d as MonoBehaviour;
            if (mb == null || mb.GetComponent<ControladorJugador>() != null) continue;
            Vector3 v = mb.transform.position - _cam.transform.position;
            float ang = Vector3.Angle(_cam.transform.forward, v);
            if (ang < mejorAng) { mejorAng = ang; mejor = mb.transform; mejorD = d; }
        }
        Objetivo = mejor; ObjetivoDmg = mejorD;
    }

    void Soltar() { Objetivo = null; ObjetivoDmg = null; }

    /// <summary>Sesga una dirección de ataque hacia el objetivo fijado si está dentro del cono.</summary>
    public static Vector3 AsistirDireccion(Vector3 origen, Vector3 dirOriginal, float maxAng)
    {
        if (Objetivo == null) return dirOriginal;
        Vector3 a = (Objetivo.position + Vector3.up * 1.0f) - origen;
        if (a.sqrMagnitude < 0.01f) return dirOriginal;
        a.Normalize();
        return Vector3.Angle(dirOriginal, a) > maxAng ? dirOriginal : a;
    }

    void ConstruirUI()
    {
        var go = new GameObject("LockOn_Canvas");
        go.transform.SetParent(transform, false);
        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 4750;

        var r = new GameObject("Retic", typeof(RectTransform));
        _retic = (RectTransform)r.transform; _retic.SetParent(_canvas.transform, false);
        _retic.sizeDelta = new Vector2(34, 34);
        var img = r.AddComponent<Text>();
        img.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        img.text = "[ + ]"; img.alignment = TextAnchor.MiddleCenter; img.fontSize = 22;
        img.color = new Color(1f, 0.35f, 0.2f);
        _retic.gameObject.SetActive(false);
    }
}
