// Assets/Scripts/Runtime/SistemaParkour.cs
// ═══════════════════════════════════════════════════════════════════════════
//  PARKOUR Y ESCALADA — vault / climb sobre bordes.
//
//  · Al pulsar SALTO (Espacio) mirando hacia un muro escalable, el jugador
//    trepa o salta por encima del borde: detecta la pared, busca la cota
//    superior (el "ledge") y sube si está a una altura alcanzable y hay hueco.
//  · Muros bajos (<1 m) → vault rápido por encima. Muros medios (hasta 2,4 m)
//    → trepar y quedarse encima.
//  · Durante la maniobra se desactiva el CharacterController (sin gravedad) y
//    se interpola la posición → no pelea con ControladorJugador.
//
//  Capa RUNTIME. Auto-arranque; se engancha al ControladorJugador existente.
//  Las distancias son un punto de partida; ajústalas en Unity.
// ═══════════════════════════════════════════════════════════════════════════
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(75)]
public sealed class SistemaParkour : MonoBehaviour
{
    public static bool EnParkour { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("SistemaParkour");
        DontDestroyOnLoad(go);
        go.AddComponent<SistemaParkour>();
    }

    const float DIST_MURO   = 0.85f;  // m: alcance para detectar la pared
    const float ALTURA_MAX  = 2.4f;   // m: borde más alto trepable
    const float ALTURA_MIN  = 0.4f;   // m: por debajo no merece animación
    const float TIEMPO_VAULT = 0.35f; // s: muros bajos
    const float TIEMPO_CLIMB = 0.55f; // s: muros altos
    const float RADIO_HUECO = 0.35f;  // m: comprobar que cabe encima

    Transform           _jugador;
    CharacterController _cc;
    Camera              _cam;

    void Update()
    {
        if (EnParkour) return;
        if (_cc == null) { Buscar(); if (_cc == null) return; }

        var kb = Keyboard.current;
        if (kb == null || !kb.spaceKey.wasPressedThisFrame) return;

        if (DetectarBorde(out Vector3 destino, out float altura))
            StartCoroutine(Maniobra(destino, altura));
    }

    bool DetectarBorde(out Vector3 destino, out float altura)
    {
        destino = Vector3.zero; altura = 0f;
        Vector3 fwd = _jugador.forward;
        Vector3 piesY = new Vector3(0, _jugador.position.y, 0);

        // 1) ¿Hay pared delante, a la altura del pecho?
        Vector3 oPecho = _jugador.position + Vector3.up * 1.0f;
        if (!Physics.Raycast(oPecho, fwd, out RaycastHit muro, DIST_MURO))
            return false;
        if (Vector3.Dot(muro.normal, fwd) > -0.3f) return false; // pared casi de frente

        // 2) Buscar la cota superior del borde (rayo hacia abajo desde arriba)
        Vector3 oArriba = muro.point + fwd * 0.35f + Vector3.up * (ALTURA_MAX + 0.5f);
        if (!Physics.Raycast(oArriba, Vector3.down, out RaycastHit techo, ALTURA_MAX + 0.6f))
            return false;

        altura = techo.point.y - _jugador.position.y;
        if (altura < ALTURA_MIN || altura > ALTURA_MAX) return false;

        // 3) ¿Cabe el jugador encima del borde? (esfera libre)
        Vector3 encima = techo.point + Vector3.up * (RADIO_HUECO + 0.1f);
        if (Physics.CheckSphere(encima, RADIO_HUECO, ~0, QueryTriggerInteraction.Ignore))
            return false;

        destino = techo.point + fwd * 0.45f + Vector3.up * 0.05f;
        return true;
    }

    IEnumerator Maniobra(Vector3 destino, float altura)
    {
        EnParkour = true;
        bool ccPrevio = _cc.enabled;
        _cc.enabled = false;

        Vector3 ini = _jugador.position;
        Vector3 medio = new Vector3(ini.x, destino.y + 0.1f, ini.z); // primero sube
        float dur = altura > 1.0f ? TIEMPO_CLIMB : TIEMPO_VAULT;

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / dur;
            float e = Mathf.SmoothStep(0f, 1f, t);
            // Fase 1 (0-0.5): sube en vertical · Fase 2 (0.5-1): avanza al borde
            Vector3 p = e < 0.5f
                ? Vector3.Lerp(ini, medio, e * 2f)
                : Vector3.Lerp(medio, destino, (e - 0.5f) * 2f);
            _jugador.position = p;
            yield return null;
        }

        _jugador.position = destino;
        if (ccPrevio) _cc.enabled = true;
        EnParkour = false;
    }

    void Buscar()
    {
        var ctrl = FindObjectOfType<ControladorJugador>();
        if (ctrl == null) return;
        _jugador = ctrl.transform;
        _cc      = ctrl.GetComponent<CharacterController>();
        _cam     = ctrl.CamaraTP;
    }
}
