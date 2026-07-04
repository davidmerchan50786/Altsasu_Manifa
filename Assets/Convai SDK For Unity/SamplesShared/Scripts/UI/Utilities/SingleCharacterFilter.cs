using System.Collections.Generic;
using Convai.Runtime.Behaviors;
using Convai.Runtime.Components;
using Convai.Runtime.Presentation.Services;
using Convai.Runtime.Presentation.Services.Utilities;
using UnityEngine;

namespace Convai.Sample.UI.Utilities
{
    /// <summary>
    ///     Sample filter that tracks a single Character - the nearest one within the player's vision cone.
    ///     Useful for subtitle-style UIs where only one character's transcript is shown at a time.
    /// </summary>
    /// <remarks>
    ///     This is a reference implementation in the Sample layer.
    ///     Inherits from TranscriptFilterBase (SDK infrastructure).
    /// </remarks>
    public class SingleCharacterFilter : TranscriptFilterBase
    {
        /// <summary>
        ///     Player input service for accessing player agent capabilities.
        /// </summary>
        private IPlayerInputService _playerInputService;
        private IConvaiPlayerAgent _playerAgent;

        private void FixedUpdate()
        {
            IConvaiCharacterAgent selectedCharacter = null;
            Transform selectedCharacterTransform = null;

            if (CharactersInsideColliderList.Count == 0) return;

            float nearestDistance = float.MaxValue;
            Transform viewer = GetViewerTransform();

            if (viewer == null) return;

            float visionConeAngle = 90f;

            if (ResolvePlayer() is IVisionConeProvider visionProvider)
                visionConeAngle = visionProvider.VisionConeAngle;

            List<IConvaiCharacterAgent> withInVisionCone = CharactersInsideColliderList.FindAll(x =>
            {
                Transform characterTransform = (x as MonoBehaviour)?.transform;
                return IsLookingAtTarget(viewer, characterTransform, visionConeAngle);
            });

            foreach (IConvaiCharacterAgent characterAgent in withInVisionCone)
            {
                Transform characterTransform = (characterAgent as MonoBehaviour)?.transform;
                if (characterTransform == null) continue;

                float distance = Vector3.Distance(transform.position, characterTransform.position);
                if (Mathf.Approximately(distance, nearestDistance))
                {
                    if (selectedCharacter == null) continue;

                    if (GetDotProduct(viewer, characterTransform) >
                        GetDotProduct(viewer, selectedCharacterTransform)) continue;

                    selectedCharacter = characterAgent;
                    selectedCharacterTransform = characterTransform;
                    nearestDistance = distance;
                }
                else if (distance < nearestDistance)
                {
                    selectedCharacter = characterAgent;
                    selectedCharacterTransform = characterTransform;
                    nearestDistance = distance;
                }
            }

            if (VisibilityService == null) return;

            if (selectedCharacter == null)
            {
                if (VisibilityService.Count > 0)
                    VisibilityService.RemoveAt(0);
                return;
            }

            string firstVisible = VisibilityService.GetFirst();
            if (firstVisible == selectedCharacter.CharacterId) return;

            if (VisibilityService.Count > 0)
                VisibilityService.RemoveAt(0);
            VisibilityService.AddCharacter(selectedCharacter.CharacterId);
        }

        /// <summary>
        ///     Injects dependencies for the filter.
        /// </summary>
        /// <param name="visibilityService">The visibility service.</param>
        /// <param name="playerInputService">The player input service.</param>
        public void Inject(IVisibleCharacterService visibilityService, IPlayerInputService playerInputService)
        {
            base.Inject(visibilityService);
            _playerInputService = playerInputService;
            _playerAgent = playerInputService?.Player;
        }

        /// <summary>
        ///     Gets the viewer transform from the player input service.
        /// </summary>
        private Transform GetViewerTransform()
        {
            if (ResolvePlayer() is MonoBehaviour playerMono) return playerMono.transform;

            return null;
        }

        private IConvaiPlayerAgent ResolvePlayer()
        {
            if (_playerInputService?.Player != null)
            {
                _playerAgent = _playerInputService.Player;
                return _playerAgent;
            }

            _playerAgent ??= GetComponentInParent<ConvaiPlayer>();
            if (_playerAgent == null)
                Debug.LogWarning($"[{nameof(SingleCharacterFilter)}] No ConvaiPlayer found in hierarchy. Assign via Inject() or place this component under the player.", this);
            return _playerAgent;
        }

        /// <summary>
        ///     Checks if the source transform is looking at the target within the specified angle.
        /// </summary>
        private static bool IsLookingAtTarget(Transform source, Transform target, float angle)
        {
            if (source == null || target == null) return false;

            Vector3 directionToTarget = (target.position - source.position).normalized;
            float dotProduct = Vector3.Dot(source.forward, directionToTarget);
            float angleToTarget = Mathf.Acos(dotProduct) * Mathf.Rad2Deg;
            return angleToTarget <= angle / 2f;
        }

        /// <summary>
        ///     Gets the dot product between the source's forward direction and the direction to the target.
        /// </summary>
        private static float GetDotProduct(Transform source, Transform target)
        {
            if (source == null || target == null) return 0f;

            Vector3 directionToTarget = (target.position - source.position).normalized;
            return Vector3.Dot(source.forward, directionToTarget);
        }
    }
}
