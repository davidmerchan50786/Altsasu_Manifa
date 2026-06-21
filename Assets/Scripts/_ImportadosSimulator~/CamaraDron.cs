using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Script para controlar una cámara tipo dron con movimiento libre en 3D.
/// Se asigna directamente a la cámara principal y permite navegación estilo FPS.
/// Ideal para simuladores 3D a escala 1:1 (ej. Cesium).
/// Compatible con Input System Package (nuevo).
/// </summary>
public class CamaraDron : MonoBehaviour
{
    // ==================== VARIABLES DE CONTROL ====================

    [Header("Velocidad")]
    [SerializeField] private float velocidadBase = 50f;          // Metros por segundo
    [SerializeField] private float multiplicadorSprint = 10f;    // Multiplicador con Shift
    [SerializeField] private float multiplicadorScroll = 1.5f;   // Factor de ajuste por scroll
    [SerializeField] private float velocidadMinima = 5f;         // Velocidad mínima
    [SerializeField] private float velocidadMaxima = 5000f;      // Velocidad máxima

    [Header("Ratón")]
    [SerializeField] private float sensibilidadRaton = 2f;       // Sensibilidad de rotación
    [SerializeField] private float rotacionMaximaX = 90f;        // Límite de rotación vertical

    // Variables privadas para el control
    private float rotacionX = 0f;       // Rotación vertical (pitch)
    private float velocidadActual = 0f; // Velocidad calculada cada frame
    private bool cursorBloqueado = true;

    // ==================== MÉTODOS DE INICIALIZACIÓN ====================

    private void Start()
    {
        BloquearCursor();
    }

    // ==================== MÉTODOS DE UPDATE ====================

    private void Update()
    {
        GestionarCursor();

        if (cursorBloqueado)
        {
            ProcesarMovimiento();
            ProcesarRotacionRaton();
        }

        ProcesarScrollVelocidad();
    }

    // ==================== MÉTODOS PRIVADOS ====================

    /// <summary>
    /// Gestiona el bloqueo/desbloqueo del cursor.
    /// Escape desbloquea; clic izquierdo o derecho vuelve a bloquear.
    /// </summary>
    private void GestionarCursor()
    {
        var keyboard = Keyboard.current;
        var mouse    = Mouse.current;

        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            DesbloquearCursor();
            return;
        }

        // Re-bloquear al hacer clic cuando el cursor está libre
        if (!cursorBloqueado && mouse != null &&
            (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame))
        {
            BloquearCursor();
        }
    }

    private void BloquearCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
        cursorBloqueado  = true;
    }

    private void DesbloquearCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
        cursorBloqueado  = false;
    }

    /// <summary>
    /// Ajusta la velocidad base con la rueda del ratón (scroll).
    /// Scroll arriba = más rápido; scroll abajo = más lento.
    /// </summary>
    private void ProcesarScrollVelocidad()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) < 0.01f) return;

        if (scroll > 0f)
            velocidadBase = Mathf.Min(velocidadBase * multiplicadorScroll, velocidadMaxima);
        else
            velocidadBase = Mathf.Max(velocidadBase / multiplicadorScroll, velocidadMinima);
    }

    /// <summary>
    /// Procesa la entrada de teclado para el movimiento de la cámara.
    /// Usa Time.deltaTime para movimiento suave independiente de FPS.
    /// </summary>
    private void ProcesarMovimiento()
    {
        var keyboard = Keyboard.current;

        if (keyboard == null)
            return;

        // Calcular velocidad según si Sprint está activo
        velocidadActual = keyboard.leftShiftKey.isPressed ?
            velocidadBase * multiplicadorSprint :
            velocidadBase;

        // Vector de dirección local (relativo a la cámara)
        Vector3 direccion = Vector3.zero;

        // Movimiento horizontal (W, A, S, D)
        if (keyboard.wKey.isPressed) direccion += Vector3.forward;
        if (keyboard.sKey.isPressed) direccion += Vector3.back;
        if (keyboard.aKey.isPressed) direccion += Vector3.left;
        if (keyboard.dKey.isPressed) direccion += Vector3.right;

        // Movimiento vertical (E, Q)
        if (keyboard.eKey.isPressed) direccion += Vector3.up;
        if (keyboard.qKey.isPressed) direccion += Vector3.down;

        // Normalizar para evitar movimiento más rápido en diagonales
        if (direccion.magnitude > 0)
            direccion.Normalize();

        // Aplicar movimiento en coordenadas locales
        transform.Translate(direccion * velocidadActual * Time.deltaTime, Space.Self);
    }

    /// <summary>
    /// Procesa la entrada del ratón para rotar la cámara (estilo FPS).
    /// </summary>
    private void ProcesarRotacionRaton()
    {
        var mouse = Mouse.current;

        if (mouse == null)
            return;

        Vector2 movimientoRaton = mouse.delta.ReadValue();

        float movimientoX = movimientoRaton.x * sensibilidadRaton;
        float movimientoY = movimientoRaton.y * sensibilidadRaton;

        // Rotación vertical (pitch) — limitada para evitar volteos
        rotacionX -= movimientoY;
        rotacionX = Mathf.Clamp(rotacionX, -rotacionMaximaX, rotacionMaximaX);

        transform.localRotation = Quaternion.Euler(rotacionX, transform.localEulerAngles.y, 0f);

        // Rotación horizontal (yaw) — alrededor del eje Y global
        transform.RotateAround(transform.position, Vector3.up, movimientoX);
    }

    // ==================== GIZMOS ====================

    private void OnGUI()
    {
        if (!cursorBloqueado)
        {
            GUI.color = Color.white;
            GUI.Label(new Rect(Screen.width / 2f - 150f, 10f, 300f, 20f),
                "Clic para volver al simulador | Scroll = ajustar velocidad");
        }
        else
        {
            GUI.color = new Color(1f, 1f, 1f, 0.5f);
            GUI.Label(new Rect(10f, Screen.height - 30f, 300f, 20f),
                $"Velocidad: {velocidadBase:F0} m/s  [Scroll para ajustar]");
        }
    }
}
