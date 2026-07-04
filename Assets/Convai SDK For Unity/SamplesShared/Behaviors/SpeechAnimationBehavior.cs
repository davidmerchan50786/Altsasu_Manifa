using Convai.Runtime.Behaviors;
using UnityEngine;

namespace Convai.Sample.Behaviors
{
    /// <summary>
    ///     Sample behavior that demonstrates how to integrate Convai speech events with Unity's animation system.
    ///     Sets a bool parameter on an Animator when the character starts/stops speaking.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Setup:</b>
    ///         <list type="number">
    ///             <item>Add this component to the same GameObject as your ConvaiCharacter.</item>
    ///             <item>Ensure an Animator component exists (will be auto-discovered).</item>
    ///             <item>Add a bool parameter named "IsSpeaking" to your Animator Controller.</item>
    ///             <item>Create transitions between idle and speaking states driven by the parameter.</item>
    ///         </list>
    ///     </para>
    /// </remarks>
    public class SpeechAnimationBehavior : ConvaiCharacterBehaviorBase
    {
        private static readonly int _isSpeaking = Animator.StringToHash(IsSpeakingParameter);
        private const string IsSpeakingParameter = "IsSpeaking";

        [SerializeField]
        [Tooltip("Reference to the Animator component. If not assigned, will auto-discover on this GameObject.")]
        private Animator _animator;

        /// <summary>
        ///     Gets whether the character is currently in the speaking state.
        /// </summary>
        public bool IsSpeaking { get; private set; }

        /// <inheritdoc />
        public override void OnCharacterInitialized(IConvaiCharacterAgent agent)
        {
            if (_animator == null) _animator = GetComponent<Animator>();

            if (_animator == null)
            {
                Debug.LogWarning(
                    $"[SpeechAnimationBehavior] No Animator found on {gameObject.name}. Speech animations will not work.");
            }
        }

        /// <inheritdoc />
        public override bool OnSpeechStarted(IConvaiCharacterAgent agent)
        {
            IsSpeaking = true;
            SetSpeakingState(true);
            return false;
        }

        /// <inheritdoc />
        public override bool OnSpeechStopped(IConvaiCharacterAgent agent)
        {
            IsSpeaking = false;
            SetSpeakingState(false);
            return false;
        }

        private void SetSpeakingState(bool isSpeaking)
        {
            if (_animator == null || !_animator.isActiveAndEnabled) return;

            if (_animator.runtimeAnimatorController == null) return;

            _animator.SetBool(_isSpeaking, isSpeaking);
        }
    }
}
