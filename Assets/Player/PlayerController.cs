// Assets/Player/PlayerController.cs
// Punto de entrada principal del sistema de jugador.
// Valida dependencias y actúa como fachada (Facade pattern) para
// sistemas externos (IA, multitudes, cinemáticas, etc.).

using UnityEngine;

namespace Player
{
    /// <summary>
    /// Orquestador del personaje jugador. Agrega y valida todos los
    /// sub-sistemas: entrada, locomoción y animaciones.
    ///
    /// Componentes requeridos en el mismo GameObject:
    ///   • <see cref="CharacterController"/>
    ///   • <see cref="PlayerInputHandler"/>
    ///   • <see cref="PlayerLocomotion"/>
    ///   • <see cref="PlayerAnimatorController"/>
    ///   • <see cref="Animator"/>
    ///
    /// La cámara (<see cref="ThirdPersonCameraController"/>) debe estar en
    /// un GameObject separado con referencia al Transform de este personaje.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(PlayerInputHandler))]
    [RequireComponent(typeof(PlayerLocomotion))]
    [RequireComponent(typeof(PlayerAnimatorController))]
    [RequireComponent(typeof(Animator))]
    [DisallowMultipleComponent]
    public sealed class PlayerController : MonoBehaviour
    {
        // ── Propiedades públicas (fachada para sistemas externos) ─────────────

        /// <summary>Velocidad actual del personaje (útil para IA/multitudes).</summary>
        public float CurrentSpeed => _locomotion != null ? _locomotion.CurrentSpeed : 0f;

        /// <summary>True si el personaje está en el suelo.</summary>
        public bool IsGrounded => _locomotion != null && _locomotion.IsGrounded;

        /// <summary>True si el personaje está corriendo.</summary>
        public bool IsRunning => _locomotion != null && _locomotion.IsRunning;

        /// <summary>True si el personaje está agachado.</summary>
        public bool IsCrouching => _locomotion != null && _locomotion.IsCrouching;

        // ── Privados ──────────────────────────────────────────────────────────
        private PlayerLocomotion _locomotion;

        // ─────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            _locomotion = GetComponent<PlayerLocomotion>();
            ValidateDependencies();
        }

        // ── Validación de dependencias ────────────────────────────────────────

        private void ValidateDependencies()
        {
            if (GetComponent<CharacterController>() == null)
                LogMissingComponent<CharacterController>();

            if (GetComponent<PlayerInputHandler>() == null)
                LogMissingComponent<PlayerInputHandler>();

            if (GetComponent<PlayerLocomotion>() == null)
                LogMissingComponent<PlayerLocomotion>();

            if (GetComponent<PlayerAnimatorController>() == null)
                LogMissingComponent<PlayerAnimatorController>();

            if (GetComponent<Animator>() == null)
                LogMissingComponent<Animator>();
        }

        private void LogMissingComponent<T>() where T : Component
        {
            Debug.LogError($"[PlayerController] Falta el componente requerido: {typeof(T).Name}", this);
        }
    }
}
