using System;
using Convai.Domain.DomainEvents.Transcript;
using Convai.Runtime.Components;
using Convai.Runtime.Facades;
using UnityEngine;
using UnityEngine.Events;

namespace Convai.Runtime.Presentation.Events
{
    /// <summary>
    ///     Inspector-friendly relay for transcript callbacks that drive UI, animation, or gameplay.
    /// </summary>
    /// <remarks>
    ///     Use this relay for reactive transcript reactions. For transcript history, room chat, or subtitle state,
    ///     prefer <c>ConvaiManager.Transcripts</c> or the transcript UI components.
    /// </remarks>
    [AddComponentMenu("Convai/Events/Convai Transcript Event Relay")]
    [DisallowMultipleComponent]
    public sealed class ConvaiTranscriptEventRelay : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("Optional explicit manager reference. If omitted, the relay can use ConvaiManager.ActiveManager.")]
        [SerializeField]
        private ConvaiManager _manager;

        [Tooltip("If enabled, the relay uses ConvaiManager.ActiveManager when no manager is assigned.")]
        [SerializeField]
        private bool _autoResolveManager = true;

        [Header("Filters")] [Tooltip("Only forward final transcript updates.")] [SerializeField]
        private bool _finalOnly;

        [Tooltip("Ignore interim updates. Final updates still pass through.")] [SerializeField]
        private bool _ignoreInterimUpdates = true;

        [Tooltip("Optional character ID filter for character transcript callbacks.")] [SerializeField]
        private string _characterIdFilter;

        [Header("Events")] [SerializeField]
        private UnityEvent<CharacterTranscriptRelayData> _onCharacterTranscriptReceived = new();

        [SerializeField] private UnityEvent<PlayerTranscriptRelayData> _onPlayerTranscriptReceived = new();

        [SerializeField] private UnityEvent<CharacterTranscriptRelayData> _onFinalCharacterTranscriptReceived = new();

        [SerializeField] private UnityEvent<PlayerTranscriptRelayData> _onFinalPlayerTranscriptReceived = new();

        private ConvaiEvents _boundEvents;
        private bool _loggedSubscribeRetry;
        private bool _loggedTargetWarning;
        private bool _subscribed;
        private ConvaiEvents _testEvents;

        public ConvaiManager Manager => _manager;
        public bool AutoResolveManager => _autoResolveManager;
        public bool FinalOnly => _finalOnly;
        public bool IgnoreInterimUpdates => _ignoreInterimUpdates;
        public string CharacterIdFilter => _characterIdFilter;
        public UnityEvent<CharacterTranscriptRelayData> OnCharacterTranscriptReceived => _onCharacterTranscriptReceived;
        public UnityEvent<PlayerTranscriptRelayData> OnPlayerTranscriptReceived => _onPlayerTranscriptReceived;

        public UnityEvent<CharacterTranscriptRelayData> OnFinalCharacterTranscriptReceived =>
            _onFinalCharacterTranscriptReceived;

        public UnityEvent<PlayerTranscriptRelayData> OnFinalPlayerTranscriptReceived =>
            _onFinalPlayerTranscriptReceived;

        private void LateUpdate()
        {
            if (!_subscribed) TrySubscribe(false);
        }

        private void OnEnable()
        {
            _loggedTargetWarning = false;
            _loggedSubscribeRetry = false;
            TrySubscribe(true);
        }

        private void OnDisable() => Unsubscribe();

        internal void BindForTesting(ConvaiEvents events)
        {
            Unsubscribe();
            _testEvents = events;
            TrySubscribe(true);
        }

        internal void ConfigureForTesting(bool finalOnly, bool ignoreInterimUpdates, string characterIdFilter = null)
        {
            _finalOnly = finalOnly;
            _ignoreInterimUpdates = ignoreInterimUpdates;
            _characterIdFilter = characterIdFilter;
        }

        internal string GetConfigurationWarning()
        {
            if (_testEvents != null) return string.Empty;

            if (_manager == null && _autoResolveManager)
                return
                    "No explicit ConvaiManager assigned. The relay will use ConvaiManager.ActiveManager at runtime; assign a manager for deterministic scene wiring.";

            if (_manager == null && !_autoResolveManager)
                return "Assign a ConvaiManager or enable Auto Resolve Manager.";

            return string.Empty;
        }

        private void TrySubscribe(bool logFailures)
        {
            if (_subscribed) return;

            if (!TryResolveEvents(out ConvaiEvents events))
            {
                if (logFailures) LogConfigurationWarning();
                return;
            }

            events.OnCharacterTranscriptReceived += HandleCharacterTranscriptReceived;
            events.OnPlayerTranscriptReceived += HandlePlayerTranscriptReceived;

            _boundEvents = events;
            _subscribed = true;
            _loggedSubscribeRetry = false;
        }

        private bool TryResolveEvents(out ConvaiEvents events)
        {
            if (_testEvents != null)
            {
                events = _testEvents;
                return true;
            }

            ConvaiManager manager =
                _manager != null ? _manager : _autoResolveManager ? ConvaiManager.ActiveManager : null;
            if (manager == null)
            {
                events = null;
                return false;
            }

            try
            {
                events = manager.Events;
                return true;
            }
            catch (InvalidOperationException)
            {
                events = null;
                if (!_loggedSubscribeRetry)
                {
                    Debug.LogWarning(
                        $"[{nameof(ConvaiTranscriptEventRelay)}] ConvaiManager is present but not initialized yet. The relay will retry while enabled.",
                        this);
                    _loggedSubscribeRetry = true;
                }

                return false;
            }
        }

        private void Unsubscribe()
        {
            if (!_subscribed || _boundEvents == null) return;

            _boundEvents.OnCharacterTranscriptReceived -= HandleCharacterTranscriptReceived;
            _boundEvents.OnPlayerTranscriptReceived -= HandlePlayerTranscriptReceived;

            _boundEvents = null;
            _subscribed = false;
        }

        private void HandleCharacterTranscriptReceived(CharacterTranscriptReceived e)
        {
            if (!PassesCharacterFilter(e.CharacterId)) return;
            if (ShouldIgnore(e.IsFinal, e.IsInterim)) return;

            var data = new CharacterTranscriptRelayData(e.CharacterId, e.CharacterName, e.Text, e.IsFinal);
            _onCharacterTranscriptReceived?.Invoke(data);
            if (e.IsFinal) _onFinalCharacterTranscriptReceived?.Invoke(data);
        }

        private void HandlePlayerTranscriptReceived(PlayerTranscriptReceived e)
        {
            if (ShouldIgnore(e.IsFinal, e.IsInterim)) return;

            var data = new PlayerTranscriptRelayData(
                e.PlayerId,
                e.PlayerName,
                e.SpeakerInfo.SpeakerId,
                e.SpeakerInfo.SpeakerName,
                e.ParticipantId,
                e.TurnId,
                e.MessageId,
                e.Text,
                e.IsFinal);

            _onPlayerTranscriptReceived?.Invoke(data);
            if (e.IsFinal) _onFinalPlayerTranscriptReceived?.Invoke(data);
        }

        private bool PassesCharacterFilter(string characterId) =>
            string.IsNullOrWhiteSpace(_characterIdFilter) ||
            string.Equals(_characterIdFilter, characterId, StringComparison.OrdinalIgnoreCase);

        private bool ShouldIgnore(bool isFinal, bool isInterim)
        {
            if (_finalOnly && !isFinal) return true;
            if (_ignoreInterimUpdates && isInterim) return true;
            return false;
        }

        private void LogConfigurationWarning()
        {
            if (_loggedTargetWarning) return;

            string warning = GetConfigurationWarning();
            if (string.IsNullOrWhiteSpace(warning)) return;

            Debug.LogWarning($"[{nameof(ConvaiTranscriptEventRelay)}] {warning}", this);
            _loggedTargetWarning = true;
        }
    }
}
