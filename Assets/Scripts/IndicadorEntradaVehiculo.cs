// IndicadorEntradaVehiculo.cs
// ═══════════════════════════════════════════════════════════════════════════
//  Componente que se añade al GO raíz de cada prefab de coche.
//  Muestra un prompt worldspace "[E] Entrar" cuando el jugador está cerca
//  y el coche está libre (nadie dentro).
//
//  Requisitos:
//    • El GO del jugador lleva el tag "Player".
//    • El coche tiene ControladorVehiculoJugador en el mismo GO.
//    • Funciona sin Canvas ni prefabs UI — usa OnGUI (worldspace → screen).
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

[RequireComponent(typeof(ControladorVehiculoJugador))]
public class IndicadorEntradaVehiculo : MonoBehaviour
{
    // ── Config ─────────────────────────────────────────────────────────────
    [Header("═══ INDICADOR ENTRADA ═══")]
    [Tooltip("Radio máximo (m) a partir del cual se muestra el indicador.")]
    [SerializeField] private float radioDeteccion = 3.5f;

    [Tooltip("Offset vertical (m) sobre la posición del coche para colocar el texto.")]
    [SerializeField] private float alturaOffset = 2.2f;

    [Tooltip("Tamaño de fuente del prompt worldspace.")]
    [SerializeField] private int   tamañoFuente = 22;

    // ── Estado interno ──────────────────────────────────────────────────────
    private IInteractable _interactable;
    private Transform     _jugador;
    private bool          _mostrar;
    private string        _textoPrompt = "[E] Entrar";

    // Estilo GUI pre-construido para evitar allocations en cada frame
    private GUIStyle _estilo;

    // ── Lifecycle ───────────────────────────────────────────────────────────

    private void Awake()
    {
        _interactable = GetComponent<IInteractable>();
    }

    private void Start()
    {
        // Buscar jugador por tag una sola vez; si cambia de escena se refrescará
        BuscarJugador();
        AlsasuaLogger.Info("IndicadorVehiculo", $"Indicador registrado en '{name}'.");
    }

    private void Update()
    {
        // Refrescar referencia al jugador si se perdió (respawn, carga de escena)
        if (_jugador == null) BuscarJugador();
        if (_jugador == null) { _mostrar = false; return; }

        if (_interactable == null) { _mostrar = false; return; }

        float distancia = Vector3.Distance(transform.position, _jugador.position);
        _mostrar = distancia <= radioDeteccion && _interactable.PuedeInteractuar;
        if (_mostrar) _textoPrompt = _interactable.TextoInteraccion;
    }

    private void OnGUI()
    {
        if (!_mostrar) return;
        if (Camera.main == null) return;

        // Punto worldspace donde aparecerá el texto (sobre el techo del coche)
        Vector3 posicionMundo = transform.position + Vector3.up * alturaOffset;
        Vector3 posicionPantalla = Camera.main.WorldToScreenPoint(posicionMundo);

        // Si el punto está detrás de la cámara, no dibujar
        if (posicionPantalla.z <= 0f) return;

        // Unity GUI tiene Y invertido respecto a la cámara
        float screenY = Screen.height - posicionPantalla.y;

        // Construir estilo la primera vez (no en Awake para que Resources esté listo)
        if (_estilo == null) InicializarEstilo();

        // Rectángulo centrado horizontalmente en el punto proyectado
        const float anchoRect  = 160f;
        const float altoRect   = 34f;
        var rect = new Rect(
            posicionPantalla.x - anchoRect * 0.5f,
            screenY            - altoRect  * 0.5f,
            anchoRect,
            altoRect
        );

        // Sombra (desplazada 1 px)
        var shadowStyle = _estilo;
        Color colorPrevio = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.85f);
        GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height),
                  _textoPrompt, shadowStyle);

        GUI.color = new Color(1f, 0.96f, 0.40f);
        GUI.Label(rect, _textoPrompt, _estilo);

        GUI.color = colorPrevio;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private void BuscarJugador()
    {
        var jugadorGO = GameObject.FindWithTag("Player");
        if (jugadorGO != null)
        {
            _jugador = jugadorGO.transform;
        }
        else
        {
            AlsasuaLogger.Warn("IndicadorVehiculo",
                "No se encontró ningún GO con tag 'Player'. " +
                "Asegúrate de que el jugador lleva ese tag.");
        }
    }

    private void InicializarEstilo()
    {
        _estilo = new GUIStyle(GUI.skin.label)
        {
            fontSize  = tamañoFuente,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            wordWrap  = false
        };
        _estilo.normal.textColor = Color.white;
    }

    private void OnDrawGizmosSelected()
    {
        // Visualizar radio de detección en el Editor
        Gizmos.color = new Color(0f, 1f, 1f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, radioDeteccion);
        // Punto de anclaje del texto worldspace
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(transform.position + Vector3.up * alturaOffset, 0.08f);
    }
}
