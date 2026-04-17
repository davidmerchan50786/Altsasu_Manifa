// Assets/Player/ThirdPersonCameraController.cs
// Responsabilidad única: mover y orientar la cámara en tercera persona
// con órbita por ratón/stick, distancia ajustable y colisión simple.

using UnityEngine;

namespace Player
{
    /// <summary>
    /// Cámara orbital en tercera persona estilo GTA/The Last of Us.
    ///
    /// Configuración en escena:
    ///   1. Crea un GameObject vacío "CameraRig" hijo del Player.
    ///   2. Adjunta este script al CameraRig.
    ///   3. Asigna la Main Camera como hijo del CameraRig y ponla en Z = -distancia.
    ///   4. Asigna el campo "Target" en el Inspector al transform del jugador.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ThirdPersonCameraController : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────
        [Header("Objetivo")]
        [SerializeField] private Transform target;
        [SerializeField] private Vector3   offset = new Vector3(0f, 1.6f, 0f);

        [Header("Distancia")]
        [SerializeField] private float defaultDistance = 4.5f;
        [SerializeField] private float minDistance     = 1.5f;
        [SerializeField] private float maxDistance     = 8.0f;

        [Header("Órbita")]
        [SerializeField] private float sensitivityX   = 3.0f;
        [SerializeField] private float sensitivityY   = 2.0f;
        [SerializeField] private float minPitchAngle  = -25f;
        [SerializeField] private float maxPitchAngle  =  60f;

        [Header("Suavizado")]
        [SerializeField] private float positionDamping  = 10f;
        [SerializeField] private float rotationDamping  = 10f;

        [Header("Colisión")]
        [SerializeField] private LayerMask collisionMask = ~0;
        [SerializeField] private float     collisionRadius = 0.3f;

        // ── Privados ──────────────────────────────────────────────────────────
        private PlayerInputHandler _input;
        private float _yaw;
        private float _pitch;
        private float _currentDistance;

        // ─────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            // El input puede estar en el padre (jugador) o en este mismo GO
            _input = target != null
                ? target.GetComponent<PlayerInputHandler>()
                : GetComponentInParent<PlayerInputHandler>();

            if (_input == null)
                Debug.LogWarning("[ThirdPersonCameraController] No se encontró PlayerInputHandler. La cámara no rotará.", this);

            _currentDistance = defaultDistance;

            // Inicializar ángulos con la rotación actual
            _yaw   = transform.eulerAngles.y;
            _pitch = transform.eulerAngles.x;

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
        }

        private void LateUpdate()
        {
            if (target == null) return;

            HandleInput();
            HandleCollision();
            ApplyTransform();
        }

        // ── Lógica ────────────────────────────────────────────────────────────

        private void HandleInput()
        {
            if (_input == null) return;

            _yaw   += _input.LookInput.x * sensitivityX;
            _pitch -= _input.LookInput.y * sensitivityY;
            _pitch  = Mathf.Clamp(_pitch, minPitchAngle, maxPitchAngle);
        }

        private void HandleCollision()
        {
            Vector3 pivot     = target.position + offset;
            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 direction = rotation * Vector3.back;  // de pivot hacia atrás

            float desiredDistance = defaultDistance;

            // SphereCast desde el pivote hacia la cámara para detectar obstáculos
            if (Physics.SphereCast(pivot, collisionRadius, direction, out RaycastHit hit,
                                   defaultDistance, collisionMask, QueryTriggerInteraction.Ignore))
            {
                desiredDistance = Mathf.Clamp(hit.distance - collisionRadius, minDistance, defaultDistance);
            }

            _currentDistance = Mathf.Lerp(_currentDistance, desiredDistance, positionDamping * Time.deltaTime);
        }

        private void ApplyTransform()
        {
            Vector3 pivot     = target.position + offset;
            Quaternion targetRotation = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 targetPosition    = pivot + targetRotation * Vector3.back * _currentDistance;

            // Suavizar posición y rotación del rig
            transform.position = Vector3.Lerp(transform.position, targetPosition, positionDamping * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationDamping * Time.deltaTime);
        }
    }
}
