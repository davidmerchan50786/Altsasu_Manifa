// Assets/Player/PlayerInputHandler.cs
// Responsabilidad única: leer y exponer el estado de entrada del jugador.
// Depende del asset InputSystem_Actions generado por Unity Input System.

using UnityEngine;
using UnityEngine.InputSystem;

namespace Player
{
    /// <summary>
    /// Lee los controles del jugador desde InputSystem_Actions y los expone
    /// como propiedades inmutables para el resto del sistema.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerInputHandler : MonoBehaviour
    {
        // ── Propiedades públicas de solo lectura ──────────────────────────────
        public Vector2 MoveInput    { get; private set; }
        public Vector2 LookInput    { get; private set; }
        public bool    JumpPressed  { get; private set; }
        public bool    CrouchHeld   { get; private set; }
        public bool    SprintHeld   { get; private set; }

        // ── Privados ──────────────────────────────────────────────────────────
        private InputSystem_Actions _actions;

        // ─────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            _actions = new InputSystem_Actions();
        }

        private void OnEnable()
        {
            _actions.Enable();

            _actions.Player.Move.performed  += OnMove;
            _actions.Player.Move.canceled   += OnMove;
            _actions.Player.Look.performed  += OnLook;
            _actions.Player.Look.canceled   += OnLook;
            _actions.Player.Jump.performed  += OnJump;
            _actions.Player.Jump.canceled   += OnJumpCanceled;
            _actions.Player.Crouch.performed += OnCrouch;
            _actions.Player.Crouch.canceled  += OnCrouchCanceled;
            _actions.Player.Sprint.performed += OnSprint;
            _actions.Player.Sprint.canceled  += OnSprintCanceled;
        }

        private void OnDisable()
        {
            _actions.Player.Move.performed  -= OnMove;
            _actions.Player.Move.canceled   -= OnMove;
            _actions.Player.Look.performed  -= OnLook;
            _actions.Player.Look.canceled   -= OnLook;
            _actions.Player.Jump.performed  -= OnJump;
            _actions.Player.Jump.canceled   -= OnJumpCanceled;
            _actions.Player.Crouch.performed -= OnCrouch;
            _actions.Player.Crouch.canceled  -= OnCrouchCanceled;
            _actions.Player.Sprint.performed -= OnSprint;
            _actions.Player.Sprint.canceled  -= OnSprintCanceled;

            _actions.Disable();
        }

        private void OnDestroy() => _actions?.Dispose();

        // ── Limpiar el flag de salto tras un frame para que sea un "pressed" ──
        private void LateUpdate() => JumpPressed = false;

        // ── Callbacks ─────────────────────────────────────────────────────────
        private void OnMove(InputAction.CallbackContext ctx)           => MoveInput   = ctx.ReadValue<Vector2>();
        private void OnLook(InputAction.CallbackContext ctx)           => LookInput   = ctx.ReadValue<Vector2>();
        private void OnJump(InputAction.CallbackContext ctx)           => JumpPressed = true;
        private void OnJumpCanceled(InputAction.CallbackContext ctx)   { /* reservado */ }
        private void OnCrouch(InputAction.CallbackContext ctx)         => CrouchHeld  = true;
        private void OnCrouchCanceled(InputAction.CallbackContext ctx) => CrouchHeld  = false;
        private void OnSprint(InputAction.CallbackContext ctx)         => SprintHeld  = true;
        private void OnSprintCanceled(InputAction.CallbackContext ctx) => SprintHeld  = false;
    }
}
