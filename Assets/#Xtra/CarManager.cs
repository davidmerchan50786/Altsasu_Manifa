// Assets/#Xtra/CarManager.cs
// Zona de entrada a vehículo (trigger en puerta/asiento).
// Integrado con ControladorVehiculoJugador (sistema principal) y GameManagerAltsasua.
// Migrado de Input.GetKeyDown (legacy) al nuevo Input System de Unity.

using UnityEngine;
using UnityEngine.InputSystem;
using UnityStandardAssets.Vehicles.Car;

public class CarManager : MonoBehaviour
{
    [Header("Referencias de escena")]
    public GameObject MainCam;
    public GameObject Player;
    public GameObject CarCamera;
    public GameObject Car;
    public Transform  ExitPos;
    public GameObject FalsePlayer;
    public CarRadio   Radio;

    // Estado actual
    private bool _enCoche;

    // ── Entrada al coche por trigger ──────────────────────────────────────────
    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player") || _enCoche) return;
        EntrarAlCoche();
    }

    // ── Salida con tecla J (nuevo Input System) ────────────────────────────────
    private void Update()
    {
        if (!_enCoche) return;

        // Compatibilidad: nuevo Input System y legacy Input juntos
        bool salir = (Keyboard.current != null && Keyboard.current.jKey.wasPressedThisFrame)
                  || Input.GetKeyDown(KeyCode.J);

        if (salir) SalirDelCoche();
    }

    // ─────────────────────────────────────────────────────────────────────────

    private void EntrarAlCoche()
    {
        _enCoche = true;

        if (MainCam != null)   MainCam.SetActive(false);
        if (Player  != null)   Player.SetActive(false);
        if (CarCamera != null) CarCamera.SetActive(true);

        if (Car != null)
        {
            SetCarComponents(true);
        }

        if (FalsePlayer != null) FalsePlayer.SetActive(true);

        // Notificar al GameManager
        GameManagerAltsasua.Instance?.SetJugadorEnVehiculo(true);

        // Notificar al ControladorVehiculoJugador si existe en el coche
        if (Car != null && Player != null)
        {
            var cvj = Car.GetComponent<ControladorVehiculoJugador>();
            var ctrl = Player.GetComponent<ControladorJugador>();
            if (cvj != null && ctrl != null) cvj.EntraJugador(ctrl);
        }
    }

    private void SalirDelCoche()
    {
        _enCoche = false;

        if (CarCamera != null) CarCamera.SetActive(false);

        if (Car != null) SetCarComponents(false);

        if (Player != null)
        {
            if (ExitPos != null)
            {
                Player.transform.position = ExitPos.position;
                Player.transform.rotation = ExitPos.rotation;
            }
            Player.SetActive(true);
        }

        if (MainCam    != null) MainCam.SetActive(true);
        if (FalsePlayer != null) FalsePlayer.SetActive(false);

        // Notificar al GameManager
        GameManagerAltsasua.Instance?.SetJugadorEnVehiculo(false);
    }

    private void SetCarComponents(bool activo)
    {
        if (Car == null) return;
        var ctrl  = Car.GetComponent<CarController>();
        var ucont = Car.GetComponent<CarUserControl>();
        var audio = Car.GetComponent<CarAudio>();

        if (ctrl  != null) ctrl.enabled  = activo;
        if (ucont != null) ucont.enabled = activo;
        if (audio != null) audio.enabled = activo;
    }
}
