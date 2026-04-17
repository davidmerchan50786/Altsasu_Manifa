// Assets/Player/PlayerLocomotion.cs
// Responsabilidad única: trasladar al personaje con aceleración suave y gravedad.
// Usa CharacterController; no contiene lógica de cámara ni de animaciones.

using UnityEngine;

namespace Player
{
    /// <summary>
    /// Mueve al personaje con aceleración/deceleración suave, soporte de
    /// carrera, agachado, salto y gravedad realista.
    /// Requiere un <see cref="CharacterController"/> en el mismo GameObject.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DisallowMultipleComponent]
    public sealed class PlayerLocomotion : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────
        [Header("Velocidades")]
        [SerializeField] private float walkSpeed    = 3.5f;
        [SerializeField] private float runSpeed     = 7.0f;
        [SerializeField] private float crouchSpeed  = 1.8f;

        [Header("Aceleración")]
        [SerializeField] private float acceleration = 10f;
        [SerializeField] private float deceleration = 15f;

        [Header("Salto y gravedad")]
        [SerializeField] private float jumpHeight   = 1.4f;
        [SerializeField] private float gravity      = -19.6f;

        [Header("Rotación")]
        [SerializeField] private float rotationSpeed = 12f;

        // ── Propiedades públicas (usadas por animaciones / IA) ────────────────
        public float CurrentSpeed   { get; private set; }
        public bool  IsGrounded     { get; private set; }
        public bool  IsRunning      { get; private set; }
        public bool  IsCrouching    { get; private set; }
        public bool  IsJumping      { get; private set; }

        /// <summary>Velocidad de caminar configurada en el Inspector.</summary>
        public float WalkSpeed  => walkSpeed;
        /// <summary>Velocidad de correr configurada en el Inspector.</summary>
        public float RunSpeed   => runSpeed;

        // ── Privados ──────────────────────────────────────────────────────────
        private CharacterController    _cc;
        private PlayerInputHandler     _input;
        private Transform              _cameraTransform;

        private Vector3  _velocity;          // velocidad acumulada (horizontal + vertical)
        private float    _verticalVelocity;  // componente Y independiente para gravedad

        // ─────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            _cc    = GetComponent<CharacterController>();
            _input = GetComponent<PlayerInputHandler>();

            if (_input == null)
                Debug.LogError($"[PlayerLocomotion] Se requiere {nameof(PlayerInputHandler)} en el mismo GameObject.", this);
        }

        private void Start()
        {
            // La cámara principal sirve para orientar el movimiento al mundo.
            if (Camera.main != null)
                _cameraTransform = Camera.main.transform;
            else
                Debug.LogWarning("[PlayerLocomotion] No se encontró Camera.main. El movimiento será relativo al mundo.", this);
        }

        private void Update()
        {
            IsGrounded = _cc.isGrounded;

            HandleGravityAndJump();
            HandleHorizontalMovement();
        }

        // ── Gravedad y salto ──────────────────────────────────────────────────

        private void HandleGravityAndJump()
        {
            if (IsGrounded && _verticalVelocity < 0f)
            {
                _verticalVelocity = -2f;  // pequeña fuerza hacia abajo para pegarse al suelo
                IsJumping = false;
            }

            if (_input.JumpPressed && IsGrounded && !IsCrouching)
            {
                // v² = 2·g·h  →  v = √(2·|g|·h)
                _verticalVelocity = Mathf.Sqrt(-2f * gravity * jumpHeight);
                IsJumping = true;
            }

            _verticalVelocity += gravity * Time.deltaTime;
        }

        // ── Movimiento horizontal ─────────────────────────────────────────────

        private void HandleHorizontalMovement()
        {
            IsCrouching = _input.CrouchHeld && IsGrounded;
            IsRunning   = _input.SprintHeld && IsGrounded && !IsCrouching && _input.MoveInput.sqrMagnitude > 0.01f;

            float targetSpeed = IsCrouching ? crouchSpeed
                              : IsRunning   ? runSpeed
                              : walkSpeed;

            Vector2 raw = _input.MoveInput;
            Vector3 desiredDirection = Vector3.zero;

            if (raw.sqrMagnitude > 0.01f)
            {
                desiredDirection = GetCameraRelativeDirection(raw);
            }

            // Acelerar o desacelerar suavemente
            float rate = (desiredDirection.sqrMagnitude > 0.01f) ? acceleration : deceleration;
            CurrentSpeed = Mathf.MoveTowards(CurrentSpeed, desiredDirection.sqrMagnitude > 0.01f ? targetSpeed : 0f, rate * Time.deltaTime);

            Vector3 motion = desiredDirection * CurrentSpeed;
            motion.y = _verticalVelocity;

            _cc.Move(motion * Time.deltaTime);

            // Rotar al personaje hacia la dirección de movimiento
            if (desiredDirection.sqrMagnitude > 0.01f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(desiredDirection);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
            }
        }

        // ── Utilidades ────────────────────────────────────────────────────────

        /// <summary>
        /// Convierte el input 2D en una dirección 3D relativa a la cámara,
        /// proyectada sobre el plano horizontal.
        /// </summary>
        private Vector3 GetCameraRelativeDirection(Vector2 rawInput)
        {
            Vector3 forward = _cameraTransform != null ? _cameraTransform.forward : Vector3.forward;
            Vector3 right   = _cameraTransform != null ? _cameraTransform.right   : Vector3.right;

            forward.y = 0f;
            right.y   = 0f;
            forward.Normalize();
            right.Normalize();

            return (forward * rawInput.y + right * rawInput.x).normalized;
        }
    }
}
