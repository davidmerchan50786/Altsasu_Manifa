// Assets/Scripts/Runtime/ReaccionAlJugador.cs
// ═══════════════════════════════════════════════════════════════════════════
//  REACCIÓN DINÁMICA AL JUGADOR — los NPCs responden a lo que HACE el jugador
//  (complementa SistemaReaccionNPCs, que solo reaccionaba a eventos globales).
//
//    · Disparo de arma de fuego (evento SistemaArmasExtendido.AlDisparar)
//        → los NPCs en RADIO_DISPARO huyen del jugador (NPCBase.Alertar).
//    · Apuntar con un arma de fuego (clic derecho) cerca de un NPC
//        → ese NPC se aparta (RADIO_APUNTAR).
//    · Nivel de búsqueda > 0 (te buscan)
//        → los civiles cercanos están nerviosos y huyen al verte.
//
//  Usa el hook ya existente NPCBase.Alertar(origen): los civiles lo
//  sobreescriben para huir, la policía para enfrentarse.
//
//  Capa RUNTIME. Auto-arranque; sin montaje en escena. Chequeo periódico y
//  por evento → barato (no escanea NPCs cada frame).
// ═══════════════════════════════════════════════════════════════════════════
using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(185)]
public sealed class ReaccionAlJugador : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("ReaccionAlJugador");
        DontDestroyOnLoad(go);
        go.AddComponent<ReaccionAlJugador>();
    }

    const float RADIO_DISPARO = 35f;   // m: pánico por un disparo
    const float RADIO_APUNTAR = 12f;   // m: NPCs se apartan si les apuntas / te buscan
    const float INTERVALO     = 0.3f;  // s entre chequeos periódicos

    SistemaArmasExtendido _armas;
    float _t;
    readonly Collider[] _buffer = new Collider[64];

    void OnEnable()  => SistemaArmasExtendido.AlDisparar += OnAmenaza;
    void OnDisable() => SistemaArmasExtendido.AlDisparar -= OnAmenaza;

    // ── Por evento: el jugador dispara/golpea → pánico inmediato ──────────
    void OnAmenaza(Vector3 origen)
    {
        Vector3 c = AltsasuCore.Jugador != null ? AltsasuCore.Jugador.position : origen;
        AlertarEnRadio(c, RADIO_DISPARO);
    }

    // ── Periódico: apuntar con arma de fuego o estar buscado ─────────────
    void Update()
    {
        _t -= Time.deltaTime;
        if (_t > 0f) return;
        _t = INTERVALO;

        if (_armas == null)
        {
            _armas = FindObjectOfType<SistemaArmasExtendido>();
            if (_armas == null) return;
        }
        var jug = AltsasuCore.Jugador;
        if (jug == null) return;

        bool armaFuego = _armas.armaActual == SistemaArmasExtendido.TipoArma.Pistola
                      || _armas.armaActual == SistemaArmasExtendido.TipoArma.Escopeta
                      || _armas.armaActual == SistemaArmasExtendido.TipoArma.Fusil;
        var ms = Mouse.current;
        bool apuntando = armaFuego && ms != null && ms.rightButton.isPressed;

        int nivel = ServiceLocator.Get<IWantedSystem>()?.NivelBusqueda ?? 0;

        if (apuntando || nivel > 0)
            AlertarEnRadio(jug.position, RADIO_APUNTAR);
    }

    void AlertarEnRadio(Vector3 centro, float radio)
    {
        int n = Physics.OverlapSphereNonAlloc(centro, radio, _buffer);
        for (int i = 0; i < n; i++)
        {
            if (_buffer[i] == null) continue;
            var npc = _buffer[i].GetComponentInParent<NPCBase>();
            if (npc != null) npc.Alertar(centro);
        }
    }
}
