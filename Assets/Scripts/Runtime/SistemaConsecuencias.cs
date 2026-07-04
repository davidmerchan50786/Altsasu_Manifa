// Assets/Scripts/Runtime/SistemaConsecuencias.cs
// ═══════════════════════════════════════════════════════════════════════════
//  CONSECUENCIAS — el apoyo popular tiene memoria.
//
//    · DAÑOS COLATERALES: matar a un civil BAJA el apoyo (y sube paranoia).
//      Lo registran el disparo y el melee al causar la muerte.
//    · CHIVATOS: si tu apoyo es bajo, los civiles cercanos pueden DELATARTE al
//      oírte disparar → sube la búsqueda. Si tu apoyo es alto, te cubren
//      (no avisan). Depende del nivel de SistemaProgresion.
//
//  Capa RUNTIME. Auto-arranque; sin montaje en escena.
// ═══════════════════════════════════════════════════════════════════════════
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(91)]
public sealed class SistemaConsecuencias : MonoBehaviour
{
    public static SistemaConsecuencias I { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("SistemaConsecuencias");
        DontDestroyOnLoad(go);
        go.AddComponent<SistemaConsecuencias>();
    }

    const float RADIO_CHIVATO = 18f;
    static readonly HashSet<int> _muertesContadas = new();
    readonly Collider[] _buf = new Collider[48];

    void Awake() { if (I != null) { Destroy(gameObject); return; } I = this; }
    void OnEnable()  => SistemaArmasExtendido.AlDisparar += OnRuido;
    void OnDisable() => SistemaArmasExtendido.AlDisparar -= OnRuido;

    // ── Colaterales: ¿acaba de morir un civil por tu mano? ────────────────
    public static void TrasDano(IDamageable d, Vector3 pos)
    {
        if (d == null || !d.EstaMuerto) return;
        var mb = d as MonoBehaviour;
        if (mb == null) return;
        bool civil = mb is NPCBase && !(mb is PoliciaForalIA);
        if (!civil) return;
        if (!_muertesContadas.Add(mb.GetInstanceID())) return;   // contar una sola vez

        SistemaApoyoPopular.Instance?.RestarApoyo(6f, "civil muerto");
        SistemaApoyoPopular.Instance?.SumarParanoia(8f);
        EventBus.Publish(new DelitoEvent { lugar = mb.transform.position, gravedad = 1f });   // testigos: lo más gordo
    }

    // ── Chivatos: al disparar, los civiles cercanos pueden delatarte ──────
    void OnRuido(Vector3 origen)
    {
        // Apoyo alto (nivel ≥ 2) → la gente te cubre, no avisa.
        if (SistemaProgresion.Nivel >= 2) return;

        Vector3 c = AltsasuCore.Jugador != null ? AltsasuCore.Jugador.position : origen;
        int n = Physics.OverlapSphereNonAlloc(c, RADIO_CHIVATO * SistemaDiaNoche.DeteccionSigilo, _buf);
        float probBase = SistemaProgresion.Nivel == 0 ? 0.5f : 0.25f;

        for (int i = 0; i < n; i++)
        {
            if (_buf[i] == null) continue;
            var npc = _buf[i].GetComponentInParent<NPCBase>();
            if (npc == null || npc is PoliciaForalIA) continue;
            if (Random.value < probBase)
            {
                ServiceLocator.Get<IWantedSystem>()?.AumentarBusqueda(1);
                break;   // un chivato basta
            }
        }
    }
}
