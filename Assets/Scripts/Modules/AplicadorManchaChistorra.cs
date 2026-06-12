// AplicadorManchaChistorra.cs — La corrupción se ve antes de contarse
// ═══════════════════════════════════════════════════════════════════════════
//  Capa ENTITIES/visual. Colocar en el GameObject del jugador (o en el root
//  de su modelo). Suscribe a ManchaChistorraEvent y aplica la opacidad al
//  parámetro _ChistorraOpacity de los materiales del personaje vía
//  MaterialPropertyBlock (sin instanciar materiales, sin GC).
//
//  El shader del personaje debe exponer:
//    _ChistorraOpacity ("Mancha", Range(0,1)) = 0
//    _ChistorraMask    ("Máscara solapa", 2D) = "black"
//  (Shader Graph HDRP: multiplicar máscara*opacidad sobre el BaseColor
//   con un tinte rojizo-graso #8a3a1e.)
//
//  REGLA DE DISEÑO: ningún texto del juego menciona la mancha. Jamás.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

public class AplicadorManchaChistorra : MonoBehaviour
{
    static readonly int PropOpacidad = Shader.PropertyToID("_ChistorraOpacity");

    [Tooltip("Renderers del personaje. Si está vacío, se buscan en Awake (no en Update).")]
    [SerializeField] Renderer[] renderers;

    [Tooltip("Velocidad de transición de la mancha (no aparece de golpe: crece)")]
    [SerializeField] float velocidadTransicion = 0.05f;

    MaterialPropertyBlock _mpb;
    float _opacidadObjetivo;
    float _opacidadActual;
    bool _transicionando;

    void Awake()
    {
        _mpb = new MaterialPropertyBlock();
        if (renderers == null || renderers.Length == 0)
            renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
    }

    void OnEnable()  => EventBus.Subscribe<ManchaChistorraEvent>(OnMancha);
    void OnDisable() => EventBus.Unsubscribe<ManchaChistorraEvent>(OnMancha);

    void OnMancha(ManchaChistorraEvent evt)
    {
        _opacidadObjetivo = Mathf.Clamp01(evt.opacidad);
        _transicionando = !Mathf.Approximately(_opacidadActual, _opacidadObjetivo);
        enabled = true;   // Update solo corre mientras hay transición
    }

    void Update()
    {
        if (!_transicionando) { enabled = false; return; }

        _opacidadActual = Mathf.MoveTowards(
            _opacidadActual, _opacidadObjetivo, velocidadTransicion * Time.deltaTime);

        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null) continue;
            r.GetPropertyBlock(_mpb);
            _mpb.SetFloat(PropOpacidad, _opacidadActual);
            r.SetPropertyBlock(_mpb);
        }

        if (Mathf.Approximately(_opacidadActual, _opacidadObjetivo))
            _transicionando = false;
    }
}
