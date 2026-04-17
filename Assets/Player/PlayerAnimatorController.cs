// Assets/Player/PlayerAnimatorController.cs
// Responsabilidad única: sincronizar los parámetros del Animator con el
// estado de locomoción del personaje.

using UnityEngine;

namespace Player
{
    /// <summary>
    /// Puente entre <see cref="PlayerLocomotion"/> y el Animator Controller.
    /// Usa hashes para máxima eficiencia en tiempo de ejecución.
    ///
    /// Parámetros esperados en el Animator Controller:
    ///   • Float  "Speed"       — velocidad normalizada 0‒1 (idle→walk→run)
    ///   • Bool   "IsGrounded"  — verdadero cuando el personaje toca suelo
    ///   • Bool   "IsCrouching" — verdadero al agacharse
    ///   • Trigger "Jump"       — dispara la animación de salto
    /// </summary>
    [RequireComponent(typeof(Animator))]
    [DisallowMultipleComponent]
    public sealed class PlayerAnimatorController : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────
        [Header("Suavizado de transición")]
        [Tooltip("Tiempo de mezcla para el parámetro Speed del Animator.")]
        [SerializeField] private float dampTime = 0.1f;

        // ── Hashes de parámetros (evitan string lookups en Update) ────────────
        private static readonly int SpeedHash      = Animator.StringToHash("Speed");
        private static readonly int IsGroundedHash = Animator.StringToHash("IsGrounded");
        private static readonly int IsCrouchHash   = Animator.StringToHash("IsCrouching");
        private static readonly int JumpHash       = Animator.StringToHash("Jump");

        // ── Privados ──────────────────────────────────────────────────────────
        private Animator        _animator;
        private PlayerLocomotion _locomotion;

        private bool _wasJumping;

        // ─────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            _animator   = GetComponent<Animator>();
            _locomotion = GetComponent<PlayerLocomotion>();

            if (_locomotion == null)
                Debug.LogError($"[PlayerAnimatorController] Se requiere {nameof(PlayerLocomotion)} en el mismo GameObject.", this);
        }

        private void Update()
        {
            if (_locomotion == null) return;

            UpdateSpeed();
            UpdateGrounded();
            UpdateCrouch();
            UpdateJump();
        }

        // ── Métodos internos ──────────────────────────────────────────────────

        private void UpdateSpeed()
        {
            // Normaliza a 0 (idle) → 0.5 (walk) → 1 (run).
            // Las velocidades de referencia provienen de PlayerLocomotion para
            // evitar duplicación de magic numbers.
            float referenceSpeed = _locomotion.IsRunning ? _locomotion.RunSpeed : _locomotion.WalkSpeed;
            float normalized     = _locomotion.CurrentSpeed > 0.01f
                ? (_locomotion.IsRunning ? 1f : _locomotion.CurrentSpeed / referenceSpeed * 0.5f)
                : 0f;

            _animator.SetFloat(SpeedHash, normalized, dampTime, Time.deltaTime);
        }

        private void UpdateGrounded()
        {
            _animator.SetBool(IsGroundedHash, _locomotion.IsGrounded);
        }

        private void UpdateCrouch()
        {
            _animator.SetBool(IsCrouchHash, _locomotion.IsCrouching);
        }

        private void UpdateJump()
        {
            // Dispara el trigger solo en el frame en que IsJumping pasa a true
            if (_locomotion.IsJumping && !_wasJumping)
                _animator.SetTrigger(JumpHash);

            _wasJumping = _locomotion.IsJumping;
        }
    }
}
