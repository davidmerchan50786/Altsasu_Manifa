// Assets/Scripts/Runtime/SistemaNado.cs
// ═══════════════════════════════════════════════════════════════════════════
//  NADO — traversal en el agua de los ríos.
//
//    · Detecta el cauce con GeneradorRiosYPuentes.EsZonaAgua(x,z) y que el
//      jugador esté por debajo del nivel del terreno (no en un puente).
//    · Al entrar, toma el control del movimiento (CharacterController OFF, como
//      parkour/esquiva): WASD nada relativo a la cámara, ESPACIO sube, CTRL
//      bucea; flotabilidad suave hacia la superficie (tope = cota del terreno).
//    · Al alcanzar la orilla (fuera del cauce), devuelve el control y reactiva
//      la gravedad.
//
//  Capa RUNTIME. Auto-arranque; se engancha al ControladorJugador existente.
//  Velocidades/umbral: punto de partida, ajústalos probando.
// ═══════════════════════════════════════════════════════════════════════════
using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(73)]
public sealed class SistemaNado : MonoBehaviour
{
    public static bool Nadando { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("SistemaNado");
        DontDestroyOnLoad(go);
        go.AddComponent<SistemaNado>();
    }

    const float VEL_NADO  = 3.2f;
    const float VEL_VERT  = 2.4f;
    const float BOYANTE   = 0.6f;   // deriva suave hacia arriba

    Transform           _jugador;
    CharacterController _cc;
    Camera              _cam;
    bool _ccPrevio;

    void Update()
    {
        if (_cc == null) { Buscar(); if (_cc == null) return; }
        if (SistemaParkour.EnParkour || SistemaEsquiva.Esquivando) return;

        Vector3 p = _jugador.position;
        bool enAgua = EnAgua(p);

        if (enAgua && !Nadando) Entrar();
        else if (!enAgua && Nadando) Salir();

        if (Nadando) Nadar(p);
    }

    bool EnAgua(Vector3 p)
    {
        var rios = GeneradorRiosYPuentes.Instance;
        if (rios == null || !rios.EsZonaAgua(p.x, p.z)) return false;
        float terreno = GeoDataAlsasua.AlturaTerreno(p.x, p.z);
        return p.y < terreno + 0.3f;   // dentro del cauce, no sobre un puente
    }

    void Entrar()
    {
        Nadando = true;
        _ccPrevio = _cc.enabled;
        _cc.enabled = false;
    }

    void Salir()
    {
        Nadando = false;
        if (_ccPrevio) _cc.enabled = true;
    }

    void Nadar(Vector3 p)
    {
        var kb = Keyboard.current;
        float dt = Time.deltaTime;

        Vector3 fwd = _cam != null ? _cam.transform.forward : _jugador.forward;
        Vector3 right = _cam != null ? _cam.transform.right : _jugador.right;
        fwd.y = 0; right.y = 0; fwd.Normalize(); right.Normalize();

        float x = 0, z = 0, y = BOYANTE;
        if (kb != null)
        {
            x = (kb.dKey.isPressed ? 1 : 0) - (kb.aKey.isPressed ? 1 : 0);
            z = (kb.wKey.isPressed ? 1 : 0) - (kb.sKey.isPressed ? 1 : 0);
            if (kb.spaceKey.isPressed)   y = VEL_VERT;
            if (kb.leftCtrlKey.isPressed) y = -VEL_VERT;
        }

        Vector3 vel = (right * x + fwd * z) * VEL_NADO + Vector3.up * y;
        Vector3 np = p + vel * dt;

        float techo = GeoDataAlsasua.AlturaTerreno(np.x, np.z);   // no salir por encima de la orilla
        if (np.y > techo) np.y = techo;

        _jugador.position = np;
        if ((x != 0 || z != 0))
        {
            Vector3 mira = (right * x + fwd * z); mira.y = 0;
            if (mira.sqrMagnitude > 0.01f) _jugador.rotation = Quaternion.Slerp(_jugador.rotation, Quaternion.LookRotation(mira), dt * 6f);
        }
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
