// Assets/Scripts/Runtime/SistemaEsquiva.cs
// ═══════════════════════════════════════════════════════════════════════════
//  ESQUIVA / RODAR (dodge roll) — evasión rápida con i-frames.
//
//    · ALT-IZQ + dirección (WASD) → rueda ~4,5 m en esa dirección (relativa a
//      la cámara). Sin dirección → paso atrás.
//    · Fotogramas de invulnerabilidad durante los primeros 0,3 s (no recibes
//      daño de balas/impacto/explosión mientras ruedas).
//    · Enfriamiento 0,8 s. No se puede esquivar en parkour ni en cobertura.
//
//  Usa el patrón seguro del parkour: desactiva el CharacterController durante
//  la maniobra e interpola la posición, así no pelea con ControladorJugador.
//  El daño se anula vía ControladorJugador.RecibirDano (hook estático).
//
//  Capa RUNTIME. Auto-arranque; sin montaje en escena. Ajusta distancia/tiempos
//  probando en Unity.
// ═══════════════════════════════════════════════════════════════════════════
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(76)]
public sealed class SistemaEsquiva : MonoBehaviour
{
    /// <summary>true durante los i-frames de la esquiva (lo lee RecibirDano del jugador).</summary>
    public static bool Invulnerable { get; private set; }
    public static bool Esquivando   { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("SistemaEsquiva");
        DontDestroyOnLoad(go);
        go.AddComponent<SistemaEsquiva>();
    }

    const float DISTANCIA   = 4.5f;
    const float DURACION    = 0.4f;
    const float IFRAMES     = 0.3f;
    const float ENFRIAMIENTO = 0.8f;

    Transform           _jugador;
    CharacterController _cc;
    Camera              _cam;
    float _cooldown;

    void Update()
    {
        if (_cc == null) { Buscar(); if (_cc == null) return; }
        if (_cooldown > 0f) _cooldown -= Time.deltaTime;
        if (Esquivando || _cooldown > 0f) return;
        if (SistemaParkour.EnParkour || SistemaCobertura.JugadorEnCobertura) return;

        var kb = Keyboard.current;
        if (kb == null || !kb.leftAltKey.wasPressedThisFrame) return;

        StartCoroutine(Rodar(DireccionDeseada(kb)));
    }

    Vector3 DireccionDeseada(Keyboard kb)
    {
        Vector3 fwd = _cam != null ? _cam.transform.forward : _jugador.forward;
        Vector3 right = _cam != null ? _cam.transform.right : _jugador.right;
        fwd.y = 0; right.y = 0; fwd.Normalize(); right.Normalize();

        float x = (kb.dKey.isPressed ? 1 : 0) - (kb.aKey.isPressed ? 1 : 0);
        float z = (kb.wKey.isPressed ? 1 : 0) - (kb.sKey.isPressed ? 1 : 0);

        Vector3 dir = right * x + fwd * z;
        if (dir.sqrMagnitude < 0.01f) dir = -fwd;   // sin input → paso atrás
        return dir.normalized;
    }

    IEnumerator Rodar(Vector3 dir)
    {
        Esquivando = true;
        bool ccPrevio = _cc.enabled;
        _cc.enabled = false;

        Vector3 ini = _jugador.position;
        Vector3 fin = ini + dir * DISTANCIA;
        // orientar al jugador hacia la rodada
        if (dir.sqrMagnitude > 0.01f) _jugador.rotation = Quaternion.LookRotation(dir, Vector3.up);

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / DURACION;
            float e = 1f - (1f - t) * (1f - t);            // ease-out
            Invulnerable = (t * DURACION) < IFRAMES;
            // mantener altura del suelo bajo el punto interpolado si hay terreno
            Vector3 p = Vector3.Lerp(ini, fin, e);
            if (Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, out var hit, 4f))
                p.y = hit.point.y;
            _jugador.position = p;
            yield return null;
        }

        Invulnerable = false;
        if (ccPrevio) _cc.enabled = true;
        Esquivando = false;
        _cooldown = ENFRIAMIENTO;
    }

    void Buscar()
    {
        var ctrl = FindObjectOfType<ControladorJugador>();
        if (ctrl == null) return;
        _jugador = ctrl.transform;
        _cc      = ctrl.GetComponent<CharacterController>();
        _cam     = ctrl.CamaraTP;
        if (_cam == null) _cam = Camera.main;
    }
}
