// Assets/Scripts/Runtime/SistemaCombateMelee.cs
// ═══════════════════════════════════════════════════════════════════════════
//  COMBATE CUERPO A CUERPO AVANZADO — combos, ataque fuerte, bloqueo/parry y
//  ejecuciones. Se activa con los Puños equipados (SistemaArmasExtendido).
//
//    · CLIC IZQ           → ataque ligero. Encadena combo de 3 (12/14/22 daño);
//                           el 3º empuja. Ventana de combo ~0,8 s.
//    · F (o CLIC IZQ man.) → ataque FUERTE (30 daño, derribo, recuperación lenta).
//    · CLIC DCHO (mantener)→ BLOQUEO: reduce el daño melee recibido un 80 %.
//    · PARRY: pulsar bloqueo justo antes de recibir el golpe (ventana 0,22 s)
//             → anula el daño y ATURDE al atacante (apertura para ejecutar).
//    · EJECUCIÓN: golpear a un enemigo con vida < 25 % → finisher letal.
//
//  El bloqueo se aplica vía ControladorJugador.RecibirDano (un hook estático).
//  Capa RUNTIME. Auto-arranque; se engancha al ControladorJugador existente.
//  Valores de daño/tiempos: punto de partida, ajústalos probando.
// ═══════════════════════════════════════════════════════════════════════════
using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(74)]
public sealed class SistemaCombateMelee : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("SistemaCombateMelee");
        DontDestroyOnLoad(go);
        go.AddComponent<SistemaCombateMelee>();
    }

    // ── Estado estático (para el guard del arma y el hook de bloqueo) ─────
    static bool  _esPunos;
    static bool  _bloqueando;
    static float _tBloqueoInicio;
    static bool  _pedirParry;
    static Vector3 _origenParry;

    /// <summary>true cuando este sistema gestiona el melee (Puños equipados).</summary>
    public static bool Activo => _esPunos;

    const float PARRY_WINDOW   = 0.22f;
    const float ALCANCE        = 2.3f;
    const float RADIO          = 0.55f;
    const float COMBO_VENTANA  = 0.8f;
    const float UMBRAL_EJEC    = 0.25f;   // vida < 25% → ejecutable

    static readonly int[] DANO_COMBO = { 12, 14, 22 };

    SistemaArmasExtendido _armas;
    Transform _jugador;
    Camera    _cam;

    int   _combo;
    float _ventana;
    float _recuperacion;

    void Update()
    {
        if (_armas == null) { Buscar(); if (_armas == null) return; }

        _esPunos = _armas.armaActual == SistemaArmasExtendido.TipoArma.Puños;
        if (!_esPunos) { _bloqueando = false; return; }

        var kb = Keyboard.current;
        var ms = Mouse.current;
        float dt = Time.deltaTime;

        if (_recuperacion > 0f) _recuperacion -= dt;
        if (_ventana > 0f) { _ventana -= dt; if (_ventana <= 0f) _combo = 0; }

        // ── Bloqueo / parry ───────────────────────────────────────────────
        bool quiereBloquear = ms != null && ms.rightButton.isPressed && !SistemaCobertura.JugadorEnCobertura;
        if (quiereBloquear && !_bloqueando) _tBloqueoInicio = Time.time;   // inicio → ventana de parry
        _bloqueando = quiereBloquear;

        if (_pedirParry)
        {
            _pedirParry = false;
            AturdirAtacante(_origenParry);
        }

        // ── Ataques ───────────────────────────────────────────────────────
        if (_recuperacion > 0f || _bloqueando) return;

        bool fuerte = kb != null && kb.fKey.wasPressedThisFrame;
        bool ligero = ms != null && ms.leftButton.wasPressedThisFrame;

        if (fuerte)       AtaqueFuerte();
        else if (ligero)  AtaqueLigero();
    }

    void AtaqueLigero()
    {
        int dano = DANO_COMBO[Mathf.Clamp(_combo, 0, DANO_COMBO.Length - 1)];
        bool ultimo = _combo >= DANO_COMBO.Length - 1;
        Golpear(dano, empuje: ultimo ? 6f : 2.5f, aturde: ultimo);
        _recuperacion = 0.32f;
        _combo = ultimo ? 0 : _combo + 1;
        _ventana = COMBO_VENTANA;
    }

    void AtaqueFuerte()
    {
        Golpear(30, empuje: 9f, aturde: true);
        _recuperacion = 0.8f;
        _combo = 0; _ventana = 0f;
    }

    void Golpear(int dano, float empuje, bool aturde)
    {
        if (_cam == null) return;
        Vector3 dirM = SistemaLockOn.AsistirDireccion(_cam.transform.position, _cam.transform.forward, 40f);
        var ray = new Ray(_cam.transform.position, dirM);
        if (!Physics.SphereCast(ray, RADIO, out var hit, ALCANCE)) return;

        var d = hit.collider.GetComponentInParent<IDamageable>();
        if (d != null && !d.EstaMuerto)
        {
            bool ejecuta = d.VidaMax > 0 && d.Vida <= d.VidaMax * UMBRAL_EJEC;
            int danoFinal = ejecuta ? Mathf.Max(d.Vida, 999) : dano;
            d.RecibirDano(danoFinal, hit.point, TipoDano.Impacto);
            SistemaGameFeel.Impacto(hit.point, danoFinal, ejecuta);
            SistemaConsecuencias.TrasDano(d, hit.point);
            if (ejecuta) Debug.Log("[Melee] ¡EJECUCIÓN!");
        }

        var rb = hit.collider.attachedRigidbody;
        if (rb != null) rb.AddForce(_cam.transform.forward * empuje, ForceMode.Impulse);

        if (aturde)
        {
            var npc = hit.collider.GetComponentInParent<NPCBase>();
            if (npc != null) npc.Alertar(_jugador.position);
        }
        ServiceLocator.Get<IWantedSystem>()?.AumentarBusqueda(1);
    }

    void AturdirAtacante(Vector3 origen)
    {
        // Parry: aturde al enemigo más cercano al origen del golpe parado.
        var cols = Physics.OverlapSphere(origen, 2.5f);
        foreach (var c in cols)
        {
            var npc = c.GetComponentInParent<NPCBase>();
            if (npc != null) { npc.Alertar(_jugador != null ? _jugador.position : origen); break; }
        }
        Debug.Log("[Melee] ¡PARRY!");
    }

    void Buscar()
    {
        _armas = FindObjectOfType<SistemaArmasExtendido>();
        var ctrl = FindObjectOfType<ControladorJugador>();
        if (ctrl != null) { _jugador = ctrl.transform; _cam = ctrl.CamaraTP; }
        if (_cam == null) _cam = Camera.main;
    }

    // ── Hook estático: lo llama ControladorJugador.RecibirDano ────────────
    /// <summary>Filtra el daño entrante según el bloqueo/parry. Solo melee/impacto.</summary>
    public static int AplicarBloqueo(int dano, Vector3 origen, TipoDano tipo)
    {
        if (!_bloqueando) return dano;
        if (tipo != TipoDano.Impacto) return dano;   // no se bloquean balas/explosiones con los puños
        if (Time.time - _tBloqueoInicio < PARRY_WINDOW)
        {
            _pedirParry = true; _origenParry = origen;
            return 0;                                  // parry perfecto
        }
        return Mathf.RoundToInt(dano * 0.2f);          // bloqueo: -80%
    }
}
