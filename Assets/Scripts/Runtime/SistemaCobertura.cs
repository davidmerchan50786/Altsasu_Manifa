// Assets/Scripts/Runtime/SistemaCobertura.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SISTEMA DE COBERTURA — disparo desde cobertura estilo GTA / shooter.
//
//  · Detecta una pared delante del jugador (< 1.3 m).
//  · Mantén CTRL-IZQ (o el control configurado) pegado a la cobertura: el
//    jugador se AGACHA detrás (baja la altura del CharacterController) →
//    queda cubierto.
//  · Mantén CLIC DERECHO para ASOMARTE (vuelve a altura completa) y disparar
//    con CLIC IZQ (lo gestiona SistemaArmasExtendido). Al soltar, te agachas
//    de nuevo.
//  · Expone JugadorEnCobertura / Asomado (static) para que la IA reduzca la
//    probabilidad de acierto cuando estás cubierto y no asomado.
//
//  Capa RUNTIME. Auto-arranque; se engancha al ControladorJugador existente
//  (su CharacterController y CamaraTP). No requiere montaje en escena.
//  NOTA: las distancias/alturas son un punto de partida; ajústalas en Unity.
// ═══════════════════════════════════════════════════════════════════════════
using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(70)]
public sealed class SistemaCobertura : MonoBehaviour
{
    /// <summary>true mientras el jugador está pegado a una cobertura.</summary>
    public static bool JugadorEnCobertura { get; private set; }
    /// <summary>true cuando el jugador se asoma desde la cobertura (expuesto para disparar).</summary>
    public static bool Asomado { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("SistemaCobertura");
        DontDestroyOnLoad(go);
        go.AddComponent<SistemaCobertura>();
    }

    const float DIST_COVER      = 1.3f;   // m de detección de pared
    const float ALTURA_AGACHADO = 1.15f;  // altura del CC al estar cubierto
    const float VEL_AGACHE      = 8f;     // suavizado de la transición

    Transform           _jugador;
    CharacterController _cc;
    float   _alturaOrig;
    Vector3 _centroOrig;
    bool    _enCobertura;
    bool    _capturadoOriginal;

    void Update()
    {
        if (_cc == null) { Buscar(); if (_cc == null) return; }

        var kb = Keyboard.current;
        var ms = Mouse.current;

        bool hayCover = DetectarCover();
        bool quiere   = kb != null && kb.leftCtrlKey.isPressed;

        if (quiere && hayCover && !_enCobertura)        Entrar();
        else if (_enCobertura && (!quiere || !hayCover)) Salir();

        if (_enCobertura)
        {
            Asomado = ms != null && ms.rightButton.isPressed;
            float objetivo = Asomado ? _alturaOrig : ALTURA_AGACHADO;
            AplicarAltura(objetivo);
        }
        else Asomado = false;

        JugadorEnCobertura = _enCobertura;
    }

    bool DetectarCover()
    {
        if (_jugador == null) return false;
        Vector3 origen = _jugador.position + Vector3.up * 0.9f;
        return Physics.Raycast(origen, _jugador.forward, DIST_COVER)
            || Physics.Raycast(origen, _jugador.forward, out _, DIST_COVER);
    }

    void Entrar()
    {
        if (!_capturadoOriginal)
        {
            _alturaOrig = _cc.height;
            _centroOrig = _cc.center;
            _capturadoOriginal = true;
        }
        _enCobertura = true;
    }

    void Salir()
    {
        _enCobertura = false;
        Asomado = false;
        if (_capturadoOriginal) AplicarAltura(_alturaOrig);
    }

    void AplicarAltura(float objetivo)
    {
        float h = Mathf.MoveTowards(_cc.height, objetivo, VEL_AGACHE * Time.deltaTime);
        _cc.height = h;
        var c = _centroOrig;
        c.y = _centroOrig.y - (_alturaOrig - h) * 0.5f;   // mantiene los pies en el suelo
        _cc.center = c;
    }

    void Buscar()
    {
        var ctrl = FindObjectOfType<ControladorJugador>();
        if (ctrl == null) return;
        _jugador = ctrl.transform;
        _cc      = ctrl.GetComponent<CharacterController>();
    }
}
