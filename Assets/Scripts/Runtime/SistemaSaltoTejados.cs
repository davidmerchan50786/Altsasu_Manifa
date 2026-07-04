// Assets/Scripts/Runtime/SistemaSaltoTejados.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SALTO ENTRE TEJADOS — leap asistido sobre huecos.
//
//    · Al pulsar SALTO (Espacio) estando en el suelo/azotea, si delante hay un
//      HUECO y una superficie alcanzable (otra azotea/cornisa) dentro de ~7 m
//      y sin caída excesiva, el jugador hace un salto guiado en arco hasta el
//      borde de aterrizaje.
//    · Si delante hay un MURO, no actúa (lo gestiona el parkour). Si no hay
//      destino válido, tampoco (salto normal del controlador).
//
//  Patrón seguro: CharacterController OFF + interpolación parabólica (como el
//  parkour) → no pelea con ControladorJugador.
//  Capa RUNTIME. Auto-arranque; sin montaje en escena. Ajusta rangos probando.
// ═══════════════════════════════════════════════════════════════════════════
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(77)]
public sealed class SistemaSaltoTejados : MonoBehaviour
{
    public static bool Saltando { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("SistemaSaltoTejados");
        DontDestroyOnLoad(go);
        go.AddComponent<SistemaSaltoTejados>();
    }

    const float MIN_HUECO = 2.5f, MAX_SALTO = 7.5f, MAX_CAIDA = 5f, MAX_SUBIDA = 1.6f;
    const float MURO_DIST = 1.4f, TIEMPO = 0.5f;

    Transform           _jugador;
    CharacterController _cc;
    Camera              _cam;

    void Update()
    {
        if (Saltando) return;
        if (_cc == null) { Buscar(); if (_cc == null) return; }
        if (SistemaParkour.EnParkour || SistemaNado.Nadando || SistemaEsquiva.Esquivando) return;
        if (!_cc.isGrounded) return;

        var kb = Keyboard.current;
        if (kb == null || !kb.spaceKey.wasPressedThisFrame) return;

        Vector3 fwd = _jugador.forward; fwd.y = 0; fwd.Normalize();

        // ¿muro delante? → lo deja para el parkour
        if (Physics.Raycast(_jugador.position + Vector3.up * 1.0f, fwd, MURO_DIST)) return;

        if (BuscarAterrizaje(fwd, out Vector3 destino))
            StartCoroutine(Saltar(destino));
    }

    bool BuscarAterrizaje(Vector3 fwd, out Vector3 destino)
    {
        destino = Vector3.zero;
        float piesY = _jugador.position.y;

        for (float d = MIN_HUECO; d <= MAX_SALTO; d += 0.7f)
        {
            Vector3 origen = _jugador.position + fwd * d + Vector3.up * (MAX_SUBIDA + 0.5f);
            if (!Physics.Raycast(origen, Vector3.down, out var hit, MAX_SUBIDA + MAX_CAIDA + 0.5f)) continue;
            if (hit.normal.y < 0.6f) continue;                          // superficie pisable
            float dh = hit.point.y - piesY;
            if (dh > MAX_SUBIDA || dh < -MAX_CAIDA) continue;           // altura alcanzable
            destino = hit.point + Vector3.up * 0.05f;
            return true;
        }
        return false;
    }

    IEnumerator Saltar(Vector3 destino)
    {
        Saltando = true;
        bool ccPrevio = _cc.enabled;
        _cc.enabled = false;

        Vector3 ini = _jugador.position;
        float arco = Mathf.Max(1.2f, Vector3.Distance(ini, destino) * 0.25f);

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / TIEMPO;
            float e = Mathf.Clamp01(t);
            Vector3 p = Vector3.Lerp(ini, destino, e);
            p.y += Mathf.Sin(e * Mathf.PI) * arco;     // arco parabólico
            _jugador.position = p;
            yield return null;
        }

        _jugador.position = destino;
        if (ccPrevio) _cc.enabled = true;
        Saltando = false;
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
