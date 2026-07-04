using System;
using System.Collections.Generic;
using System.Linq;
using Convai.Domain.DomainEvents.Runtime;
using Convai.Domain.DomainEvents.Transcript;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Domain.Models;
using Convai.Runtime.Utilities;

namespace Convai.Application.Services.Transcript
{
    internal interface IRoomTranscriptEngine : IDisposable
    {
        public TranscriptTimelineSnapshot CurrentTimeline { get; }

        public event Action<TranscriptUpdateBatch> Changed;

        public IReadOnlyList<TranscriptTurnSnapshot> GetTurns(TranscriptQuery query);

        public TranscriptTurnSnapshot GetTurn(string turnId);

        public TranscriptTurnSnapshot GetLatestTurn(TranscriptParticipantRef participant);
    }

    /// <summary>
    ///     Canonical room-scoped transcript authority that normalizes raw transcript events into turn snapshots.
    /// </summary>
    internal sealed class RoomTranscriptEngine : IRoomTranscriptEngine,
        IEventSubscriber<PlayerTranscriptReceived>,
        IEventSubscriber<CharacterTranscriptReceived>,
        IEventSubscriber<PlayerSpeakingStateChanged>,
        IEventSubscriber<CharacterSpeechStateChanged>,
        IEventSubscriber<CharacterTurnCompleted>
    {
        private readonly SubscriptionToken _characterSpeechToken;
        private readonly SubscriptionToken _characterTranscriptToken;
        private readonly SubscriptionToken _characterTurnCompletedToken;
        private readonly IEventHub _eventHub;
        private readonly object _gate = new();
        private readonly ILogger _logger;
        private readonly Dictionary<string, TurnState> _openCharacterTurnsByActor = new();
        private readonly Dictionary<string, TurnState> _openPlayerTurnsByActor = new();
        private readonly SubscriptionToken _playerSpeakingToken;
        private readonly SubscriptionToken _playerTranscriptToken;
        private readonly Dictionary<string, TurnState> _turnsById = new();
        private TranscriptTimelineSnapshot _currentTimeline = TranscriptTimelineSnapshot.Empty;
        private bool _disposed;
        private bool _isLocalPlayerSpeaking;
        private long _nextRoomSequence = 1;
        private long _nextSyntheticId = 1;
        private long _timelineCursor;

        public RoomTranscriptEngine(IEventHub eventHub, ILogger logger = null)
        {
            _eventHub = eventHub ?? throw new ArgumentNullException(nameof(eventHub));
            _logger = logger;

            _playerTranscriptToken = _eventHub.Subscribe<PlayerTranscriptReceived>(HandlePlayerTranscriptReceived);
            _characterTranscriptToken =
                _eventHub.Subscribe<CharacterTranscriptReceived>(HandleCharacterTranscriptReceived);
            _playerSpeakingToken = _eventHub.Subscribe<PlayerSpeakingStateChanged>(HandlePlayerSpeakingStateChanged);
            _characterSpeechToken =
                _eventHub.Subscribe<CharacterSpeechStateChanged>(HandleCharacterSpeechStateChanged);
            _characterTurnCompletedToken =
                _eventHub.Subscribe<CharacterTurnCompleted>(HandleCharacterTurnCompleted);
        }

        public void OnEvent(CharacterSpeechStateChanged e)
        {
            if (_disposed || !e.IsSpeaking || _isLocalPlayerSpeaking) return;

            TranscriptUpdateBatch batch;

            lock (_gate)
            {
                CompleteOpenPlayerTurnsLocked(e.Timestamp, out List<TurnState> completedTurns);
                batch = BuildBatchLocked(completedTurns, null, null, null, null, null);
            }

            PublishBatch(batch);
        }

        public void OnEvent(CharacterTranscriptReceived e)
        {
            if (_disposed) return;

            if (_isLocalPlayerSpeaking)
            {
                _logger?.Debug(
                    $"[RoomTranscriptEngine] Dropping character transcript while player is speaking: actor={e.CharacterId}, text=\"{e.Text}\"",
                    LogCategory.UI);
                return;
            }

            if (string.IsNullOrWhiteSpace(e.Text)) return;

            TranscriptUpdateBatch batch;

            lock (_gate)
            {
                CompleteOpenPlayerTurnsLocked(e.Timestamp, out List<TurnState> completedPlayerTurns);

                TranscriptParticipantRef actor = BuildCharacterActor(e);
                string actorKey = BuildActorKey(actor.Kind, actor.PlayerOrCharacterId, actor.ParticipantId);

                if (!_openCharacterTurnsByActor.TryGetValue(actorKey, out TurnState turn) || turn.IsCompleted)
                {
                    turn = CreateTurnLocked(actor, actorKey, e.Timestamp);
                    _openCharacterTurnsByActor[actorKey] = turn;
                }

                UpdateTurnActorLocked(turn, actor);

                SegmentState segment = turn.GetOrCreateCharacterSegment(e.Timestamp);
                ApplyCharacterTranscriptLocked(turn, segment, e.Text, e.IsFinal, e.Timestamp);

                List<TurnState> changedTurns = new() { turn };
                if (completedPlayerTurns != null && completedPlayerTurns.Count > 0)
                    changedTurns.AddRange(completedPlayerTurns);

                batch = BuildBatchLocked(changedTurns, null, null, null, null, null);
            }

            PublishBatch(batch);
        }

        public void OnEvent(CharacterTurnCompleted e)
        {
            if (_disposed) return;

            TranscriptUpdateBatch batch;

            lock (_gate)
            {
                CompleteCharacterTurnLocked(
                    e.CharacterId,
                    e.ParticipantId,
                    e.Timestamp,
                    e.WasInterrupted,
                    out List<TurnState> turns);
                batch = BuildBatchLocked(turns, null, null, null, null, null);
            }

            PublishBatch(batch);
        }

        public void OnEvent(PlayerSpeakingStateChanged e)
        {
            if (_disposed) return;

            _isLocalPlayerSpeaking = e.IsSpeaking;
            if (!e.IsSpeaking) return;

            TranscriptUpdateBatch batch;

            lock (_gate)
            {
                InterruptOpenCharacterTurnsLocked(e.Timestamp, out List<TurnState> interruptedTurns);
                batch = BuildBatchLocked(interruptedTurns, null, null, null, null, null);
            }

            PublishBatch(batch);
        }

        public void OnEvent(PlayerTranscriptReceived e)
        {
            if (_disposed) return;

            TranscriptUpdateBatch batch = null;
            bool turnRemoved = false;

            lock (_gate)
            {
                TranscriptParticipantRef actor = BuildPlayerActor(e);
                string actorKey = BuildActorKey(actor.Kind, actor.PlayerOrCharacterId, actor.ParticipantId);
                TurnState turn = GetOrCreatePlayerTurnLocked(actor, actorKey, e.Timestamp, e.MessageId);
                List<TurnState> interruptedCharacterTurns = null;

                if (e.Phase != TranscriptionPhase.Completed)
                    InterruptOpenCharacterTurnsLocked(e.Timestamp, out interruptedCharacterTurns);

                SegmentState segment = GetOrCreatePlayerSegmentLocked(turn, e.MessageId, e.Timestamp, e.Phase);
                UpdateTurnActorLocked(turn, actor);

                switch (e.Phase)
                {
                    case TranscriptionPhase.Interim:
                        ApplyPlayerInterimLocked(turn, segment, e.Text, e.Timestamp);
                        break;

                    case TranscriptionPhase.AsrFinal:
                        ApplyPlayerAsrFinalLocked(turn, segment, e.Text, e.Timestamp);
                        break;

                    case TranscriptionPhase.ProcessedFinal:
                        ApplyPlayerProcessedFinalLocked(turn, segment, e.Text, e.Timestamp);
                        break;

                    case TranscriptionPhase.Completed:
                        FinalizePlayerSegmentLocked(turn, segment, e.Timestamp);
                        if (!turn.HasAnyText)
                        {
                            string removedTurnId = turn.TurnId;
                            RemoveTurnLocked(turn);
                            batch = BuildBatchLocked(
                                interruptedCharacterTurns,
                                null,
                                null,
                                null,
                                null,
                                new[] { removedTurnId });
                            turnRemoved = true;
                        }

                        break;

                    default:
                        return;
                }

                if (!turnRemoved)
                {
                    List<TurnState> changedTurns = new() { turn };
                    if (interruptedCharacterTurns != null && interruptedCharacterTurns.Count > 0)
                        changedTurns.AddRange(interruptedCharacterTurns);

                    batch = BuildBatchLocked(changedTurns, null, null, null, null, null);
                }
            }

            PublishBatch(batch);
        }

        public TranscriptTimelineSnapshot CurrentTimeline
        {
            get
            {
                lock (_gate) return _currentTimeline;
            }
        }

        public event Action<TranscriptUpdateBatch> Changed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _eventHub.Unsubscribe(_playerTranscriptToken);
            _eventHub.Unsubscribe(_characterTranscriptToken);
            _eventHub.Unsubscribe(_playerSpeakingToken);
            _eventHub.Unsubscribe(_characterSpeechToken);
            _eventHub.Unsubscribe(_characterTurnCompletedToken);
        }

        public IReadOnlyList<TranscriptTurnSnapshot> GetTurns(TranscriptQuery query)
        {
            query ??= new TranscriptQuery();
            lock (_gate)
            {
                IEnumerable<TranscriptTurnSnapshot> turns = Enumerable.Empty<TranscriptTurnSnapshot>();

                if (query.IncludeActiveTurns)
                    turns = turns.Concat(_currentTimeline.ActiveTurns);

                if (query.IncludeCommittedTurns)
                    turns = turns.Concat(_currentTimeline.CommittedTurns);

                if (query.ParticipantKind.HasValue)
                    turns = turns.Where(t => t.Participant.Kind == query.ParticipantKind.Value);

                if (!string.IsNullOrWhiteSpace(query.PlayerOrCharacterId))
                {
                    turns = turns.Where(t =>
                        string.Equals(t.Participant.PlayerOrCharacterId, query.PlayerOrCharacterId,
                            StringComparison.Ordinal));
                }

                if (!string.IsNullOrWhiteSpace(query.ParticipantId))
                {
                    turns = turns.Where(t =>
                        string.Equals(t.Participant.ParticipantId, query.ParticipantId, StringComparison.Ordinal));
                }

                return turns
                    .OrderBy(t => t.RoomSequence)
                    .ToArray();
            }
        }

        public TranscriptTurnSnapshot GetTurn(string turnId)
        {
            if (string.IsNullOrWhiteSpace(turnId)) return null;

            lock (_gate)
                return _currentTimeline.TurnsById.TryGetValue(turnId, out TranscriptTurnSnapshot turn) ? turn : null;
        }

        public TranscriptTurnSnapshot GetLatestTurn(TranscriptParticipantRef participant)
        {
            string key = BuildActorKey(participant.Kind, participant.PlayerOrCharacterId, participant.ParticipantId);

            lock (_gate)
            {
                if (_currentTimeline.LatestTurnByParticipant.TryGetValue(key, out TranscriptTurnSnapshot turn))
                    return turn;

                if (!string.IsNullOrWhiteSpace(participant.PlayerOrCharacterId))
                {
                    return _currentTimeline.LatestTurnByParticipant.Values
                        .Where(t => t.Participant.Kind == participant.Kind)
                        .Where(t => string.Equals(t.Participant.PlayerOrCharacterId, participant.PlayerOrCharacterId,
                            StringComparison.Ordinal))
                        .OrderByDescending(t => t.RoomSequence)
                        .FirstOrDefault();
                }

                return null;
            }
        }

        private void HandlePlayerTranscriptReceived(PlayerTranscriptReceived e)
        {
            try
            {
                OnEvent(e);
            }
            catch (Exception ex)
            {
                _logger?.Error(
                    $"[RoomTranscriptEngine] Failed handling PlayerTranscriptReceived: phase={e.Phase}, turnId={e.TurnId}, messageId={e.MessageId}, text=\"{e.Text}\". {ex}",
                    LogCategory.UI);
            }
        }

        private void HandleCharacterTranscriptReceived(CharacterTranscriptReceived e)
        {
            try
            {
                OnEvent(e);
            }
            catch (Exception ex)
            {
                _logger?.Error(
                    $"[RoomTranscriptEngine] Failed handling CharacterTranscriptReceived: characterId='{e.CharacterId}', participantId='{e.Message.ParticipantId}', final={e.IsFinal}, text=\"{e.Text}\". {ex}",
                    LogCategory.UI);
            }
        }

        private void HandlePlayerSpeakingStateChanged(PlayerSpeakingStateChanged e)
        {
            try
            {
                OnEvent(e);
            }
            catch (Exception ex)
            {
                _logger?.Error(
                    $"[RoomTranscriptEngine] Failed handling PlayerSpeakingStateChanged: isSpeaking={e.IsSpeaking}. {ex}",
                    LogCategory.UI);
            }
        }

        private void HandleCharacterSpeechStateChanged(CharacterSpeechStateChanged e)
        {
            try
            {
                OnEvent(e);
            }
            catch (Exception ex)
            {
                _logger?.Error(
                    $"[RoomTranscriptEngine] Failed handling CharacterSpeechStateChanged: characterId='{e.CharacterId}', isSpeaking={e.IsSpeaking}. {ex}",
                    LogCategory.UI);
            }
        }

        private void HandleCharacterTurnCompleted(CharacterTurnCompleted e)
        {
            try
            {
                OnEvent(e);
            }
            catch (Exception ex)
            {
                _logger?.Error(
                    $"[RoomTranscriptEngine] Failed handling CharacterTurnCompleted: characterId='{e.CharacterId}', participantId='{e.ParticipantId}', interrupted={e.WasInterrupted}. {ex}",
                    LogCategory.UI);
            }
        }

        private TurnState GetOrCreatePlayerTurnLocked(
            TranscriptParticipantRef actor,
            string actorKey,
            DateTime timestamp,
            string preferredTurnId)
        {
            if (_openPlayerTurnsByActor.TryGetValue(actorKey, out TurnState existing) && !existing.IsCompleted)
                return existing;

            TurnState turn = CreateTurnLocked(actor, actorKey, timestamp, preferredTurnId);
            _openPlayerTurnsByActor[actorKey] = turn;
            return turn;
        }

        private SegmentState GetOrCreatePlayerSegmentLocked(
            TurnState turn,
            string sourceSessionId,
            DateTime timestamp,
            TranscriptionPhase phase)
        {
            string safeSessionId = string.IsNullOrWhiteSpace(sourceSessionId)
                ? $"player-session-{_nextSyntheticId++:D6}"
                : sourceSessionId;

            SegmentState activeSegment = turn.ActiveSegment;

            bool shouldStartNewSegment = activeSegment == null ||
                                         !string.Equals(activeSegment.SourceSessionId, safeSessionId,
                                             StringComparison.Ordinal) ||
                                         (phase == TranscriptionPhase.Interim && activeSegment.CanStartNewSubsegment);

            if (!shouldStartNewSegment) return activeSegment;

            if (activeSegment != null)
                FinalizePlayerSegmentLocked(turn, activeSegment, timestamp);

            string segmentId = $"{safeSessionId}:{turn.NextSegmentIndex++}";
            SegmentState segment = new(
                segmentId,
                safeSessionId,
                turn.Actor,
                timestamp,
                TranscriptSegmentSourceKind.PlayerAsr);

            turn.AddSegment(segment);
            return segment;
        }

        private void ApplyPlayerInterimLocked(TurnState turn, SegmentState segment, string text, DateTime timestamp)
        {
            segment.UpdatedAtUtc = timestamp;
            segment.InterimText = text ?? string.Empty;
            segment.Lifecycle = TranscriptLifecycle.Streaming;
            segment.Actor = turn.Actor;
            turn.LastUpdatedAtUtc = timestamp;
        }

        private void ApplyPlayerAsrFinalLocked(TurnState turn, SegmentState segment, string text, DateTime timestamp)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            segment.UpdatedAtUtc = timestamp;
            segment.SourceKind = TranscriptSegmentSourceKind.PlayerAsr;
            segment.CommittedText = text;
            segment.ProcessedOverrideText = string.Empty;
            segment.InterimText = string.Empty;
            segment.CanStartNewSubsegment = true;
            segment.Lifecycle = TranscriptLifecycle.Stable;
            segment.Actor = turn.Actor;
            turn.LastUpdatedAtUtc = timestamp;
        }

        private void ApplyPlayerProcessedFinalLocked(TurnState turn, SegmentState segment, string text,
            DateTime timestamp)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            segment.UpdatedAtUtc = timestamp;
            segment.SourceKind = TranscriptSegmentSourceKind.PlayerProcessedFinal;
            segment.ProcessedOverrideText = text;
            segment.InterimText = string.Empty;
            segment.CanStartNewSubsegment = true;
            segment.Lifecycle = TranscriptLifecycle.Stable;
            segment.Actor = turn.Actor;
            turn.LastUpdatedAtUtc = timestamp;
        }

        private void FinalizePlayerSegmentLocked(
            TurnState turn,
            SegmentState segment,
            DateTime timestamp,
            bool keepTurnOpen = true)
        {
            if (segment == null) return;

            segment.UpdatedAtUtc = timestamp;
            segment.StoppedAtUtc = timestamp;

            if (!segment.HasStableText)
            {
                turn.RemoveSegment(segment);
                return;
            }

            segment.InterimText = string.Empty;
            segment.IsClosed = true;
            segment.Lifecycle = keepTurnOpen ? TranscriptLifecycle.Stable : TranscriptLifecycle.Completed;
            turn.LastUpdatedAtUtc = timestamp;
        }

        private void ApplyCharacterTranscriptLocked(
            TurnState turn,
            SegmentState segment,
            string text,
            bool isFinal,
            DateTime timestamp)
        {
            segment.UpdatedAtUtc = timestamp;

            if (isFinal)
            {
                segment.CommittedText = MergeTranscriptText(segment.CommittedText, text);
                segment.InterimText = string.Empty;
                segment.Lifecycle = TranscriptLifecycle.Stable;
                segment.Actor = turn.Actor;
                turn.LastUpdatedAtUtc = timestamp;
                return;
            }

            segment.InterimText = text ?? string.Empty;
            segment.Lifecycle = TranscriptLifecycle.Streaming;
            segment.Actor = turn.Actor;
            turn.LastUpdatedAtUtc = timestamp;
        }

        private void CompleteOpenPlayerTurnsLocked(DateTime timestamp, out List<TurnState> completedTurns)
        {
            completedTurns = null;

            foreach (TurnState turn in _openPlayerTurnsByActor.Values.ToArray())
            {
                if (turn.IsCompleted || !turn.HasAnyText) continue;
                CompleteTurnLocked(turn, timestamp, false);
                completedTurns ??= new List<TurnState>();
                completedTurns.Add(turn);
            }

            if (completedTurns == null) return;

            foreach (TurnState turn in completedTurns)
                _openPlayerTurnsByActor.Remove(turn.ActorKey);
        }

        private void InterruptOpenCharacterTurnsLocked(DateTime timestamp, out List<TurnState> interruptedTurns)
        {
            interruptedTurns = null;

            foreach (TurnState turn in _openCharacterTurnsByActor.Values.ToArray())
            {
                if (turn.IsCompleted) continue;
                CompleteTurnLocked(turn, timestamp, true);
                interruptedTurns ??= new List<TurnState>();
                interruptedTurns.Add(turn);
            }

            if (interruptedTurns == null) return;

            foreach (TurnState turn in interruptedTurns)
                _openCharacterTurnsByActor.Remove(turn.ActorKey);
        }

        private void CompleteCharacterTurnLocked(
            string characterId,
            string participantId,
            DateTime timestamp,
            bool wasInterrupted,
            out List<TurnState> completedTurns)
        {
            completedTurns = null;

            bool hasCharacterId = !string.IsNullOrWhiteSpace(characterId);
            bool hasParticipantId = !string.IsNullOrWhiteSpace(participantId);
            if (!hasCharacterId && !hasParticipantId) return;

            foreach (KeyValuePair<string, TurnState> pair in _openCharacterTurnsByActor.ToArray())
            {
                TurnState turn = pair.Value;
                bool matchesCharacterId = hasCharacterId &&
                                          string.Equals(turn.Actor.PlayerOrCharacterId, characterId,
                                              StringComparison.Ordinal);
                bool matchesParticipantId = hasParticipantId &&
                                            string.Equals(turn.Actor.ParticipantId, participantId,
                                                StringComparison.Ordinal);
                if (!matchesCharacterId && !matchesParticipantId) continue;

                CompleteTurnLocked(turn, timestamp, wasInterrupted);
                completedTurns ??= new List<TurnState>();
                completedTurns.Add(turn);
                _openCharacterTurnsByActor.Remove(pair.Key);
            }
        }

        private void CompleteTurnLocked(TurnState turn, DateTime timestamp, bool wasInterrupted)
        {
            if (turn.IsCompleted) return;

            // ActiveSegment is derived from open segments; cache before closing or the getter returns null after IsClosed.
            SegmentState activeSegment = turn.ActiveSegment;
            if (activeSegment != null)
            {
                activeSegment.StoppedAtUtc = timestamp;
                activeSegment.InterimText = string.Empty;
                activeSegment.Lifecycle = TranscriptLifecycle.Completed;
                activeSegment.IsClosed = true;
            }

            turn.CompletedAtUtc = timestamp;
            turn.WasInterrupted = wasInterrupted;
            turn.LastUpdatedAtUtc = timestamp;
        }

        private TurnState CreateTurnLocked(
            TranscriptParticipantRef actor,
            string actorKey,
            DateTime timestamp,
            string preferredTurnId = null)
        {
            string turnId = string.IsNullOrWhiteSpace(preferredTurnId)
                ? $"turn-{_nextSyntheticId++:D6}"
                : preferredTurnId;

            if (_turnsById.ContainsKey(turnId))
                turnId = $"{turnId}-{_nextSyntheticId++:D6}";

            TurnState turn = new(
                turnId,
                _nextRoomSequence++,
                actor,
                actorKey,
                timestamp);

            _turnsById[turnId] = turn;
            return turn;
        }

        private void UpdateTurnActorLocked(TurnState turn, TranscriptParticipantRef actor)
        {
            string oldActorKey = turn.ActorKey;
            string newActorKey = BuildActorKey(actor.Kind, actor.PlayerOrCharacterId, actor.ParticipantId);
            turn.Actor = actor;
            turn.ActorKey = newActorKey;
            if (turn.ActiveSegment != null) turn.ActiveSegment.Actor = actor;
            if (string.Equals(oldActorKey, newActorKey, StringComparison.Ordinal)) return;

            if (_openPlayerTurnsByActor.TryGetValue(oldActorKey, out TurnState openPlayerTurn) &&
                ReferenceEquals(openPlayerTurn, turn))
            {
                _openPlayerTurnsByActor.Remove(oldActorKey);
                _openPlayerTurnsByActor[newActorKey] = turn;
            }

            if (_openCharacterTurnsByActor.TryGetValue(oldActorKey, out TurnState openCharacterTurn) &&
                ReferenceEquals(openCharacterTurn, turn))
            {
                _openCharacterTurnsByActor.Remove(oldActorKey);
                _openCharacterTurnsByActor[newActorKey] = turn;
            }
        }

        private void RemoveTurnLocked(TurnState turn)
        {
            _turnsById.Remove(turn.TurnId);
            _openPlayerTurnsByActor.Remove(turn.ActorKey);
            _openCharacterTurnsByActor.Remove(turn.ActorKey);
        }

        private TranscriptUpdateBatch BuildBatchLocked(
            IReadOnlyList<TurnState> changedStates,
            IReadOnlyList<string> addedTurnIds,
            IReadOnlyList<string> updatedTurnIds,
            IReadOnlyList<string> completedTurnIds,
            IReadOnlyList<string> interruptedTurnIds,
            IReadOnlyList<string> removedTurnIds)
        {
            _timelineCursor++;
            _currentTimeline = BuildTimelineLocked();

            IReadOnlyList<TranscriptTurnSnapshot> changedTurns = changedStates == null
                ? Array.Empty<TranscriptTurnSnapshot>()
                : changedStates
                    .Distinct()
                    .Select(CreateSnapshotLocked)
                    .OrderBy(t => t.RoomSequence)
                    .ToArray();

            return new TranscriptUpdateBatch(
                _currentTimeline,
                changedTurns,
                addedTurnIds ?? Array.Empty<string>(),
                updatedTurnIds ?? Array.Empty<string>(),
                completedTurnIds ?? Array.Empty<string>(),
                interruptedTurnIds ?? Array.Empty<string>(),
                removedTurnIds ?? Array.Empty<string>());
        }

        private void PublishBatch(TranscriptUpdateBatch batch)
        {
            if (batch == null) return;
            if (batch.ChangedTurns.Count == 0 && batch.RemovedTurnIds.Count == 0) return;

            SafeEventInvoker.Invoke(
                Changed,
                batch,
                _logger,
                "RoomTranscriptEngine.Changed",
                LogCategory.UI);
        }

        private TranscriptTimelineSnapshot BuildTimelineLocked()
        {
            TranscriptTurnSnapshot[] snapshots = _turnsById.Values
                .OrderBy(t => t.RoomSequence)
                .Select(CreateSnapshotLocked)
                .ToArray();

            Dictionary<string, TranscriptTurnSnapshot> turnsById = snapshots.ToDictionary(t => t.TurnId);
            Dictionary<string, TranscriptTurnSnapshot> latestByParticipant = new();

            foreach (TranscriptTurnSnapshot snapshot in snapshots)
            {
                string actorKey = BuildActorKey(snapshot.Participant.Kind, snapshot.Participant.PlayerOrCharacterId,
                    snapshot.Participant.ParticipantId);
                latestByParticipant[actorKey] = snapshot;

                if (!string.IsNullOrWhiteSpace(snapshot.Participant.PlayerOrCharacterId))
                {
                    string actorOnlyKey =
                        $"{snapshot.Participant.Kind}:actor:{snapshot.Participant.PlayerOrCharacterId}";
                    latestByParticipant[actorOnlyKey] = snapshot;
                }
            }

            TranscriptTurnSnapshot[] activeTurns = snapshots
                .Where(t => t.Lifecycle != TranscriptLifecycle.Completed)
                .ToArray();

            TranscriptTurnSnapshot[] committedTurns = snapshots
                .Where(t => t.Lifecycle == TranscriptLifecycle.Completed)
                .ToArray();

            return new TranscriptTimelineSnapshot(
                _timelineCursor,
                activeTurns,
                committedTurns,
                turnsById,
                latestByParticipant);
        }

        private static TranscriptTurnSnapshot CreateSnapshotLocked(TurnState turn)
        {
            List<TranscriptSegmentSnapshot> segments = turn.Segments
                .Select(segment => new TranscriptSegmentSnapshot(
                    segment.SegmentId,
                    turn.TurnId,
                    segment.Actor,
                    segment.CommittedText,
                    segment.InterimText,
                    segment.ProcessedOverrideText,
                    segment.StartedAtUtc,
                    segment.UpdatedAtUtc,
                    segment.StoppedAtUtc,
                    segment.GetLifecycle(turn.IsCompleted),
                    segment.SourceKind))
                .ToList();

            string committedText = string.Empty;
            string interimText = string.Empty;

            foreach (SegmentState segment in turn.Segments)
            {
                bool isActiveSegment = ReferenceEquals(segment, turn.ActiveSegment) && !segment.IsClosed;
                string stableText = segment.StableDisplayText;

                if (isActiveSegment)
                {
                    committedText = AppendTurnText(committedText, stableText);
                    interimText = segment.InterimText ?? string.Empty;
                }
                else
                    committedText = AppendTurnText(committedText, segment.DisplayText);
            }

            TranscriptLifecycle lifecycle = turn.IsCompleted
                ? TranscriptLifecycle.Completed
                : string.IsNullOrWhiteSpace(interimText)
                    ? TranscriptLifecycle.Stable
                    : TranscriptLifecycle.Streaming;

            return new TranscriptTurnSnapshot(
                turn.TurnId,
                turn.RoomSequence,
                turn.Actor,
                turn.StartedAtUtc,
                turn.LastUpdatedAtUtc,
                turn.CompletedAtUtc,
                lifecycle,
                committedText,
                interimText,
                turn.WasInterrupted,
                segments);
        }

        private static TranscriptParticipantRef BuildPlayerActor(PlayerTranscriptReceived e)
        {
            string actorId = !string.IsNullOrWhiteSpace(e.SpeakerInfo.SpeakerId)
                ? e.SpeakerInfo.SpeakerId
                : e.Message.PlayerOrCharacterId;

            string displayName = !string.IsNullOrWhiteSpace(e.SpeakerInfo.SpeakerName)
                ? e.SpeakerInfo.SpeakerName
                : e.Message.DisplayName;

            string participantId = !string.IsNullOrWhiteSpace(e.SpeakerInfo.ParticipantId)
                ? e.SpeakerInfo.ParticipantId
                : e.Message.ParticipantId;

            return new TranscriptParticipantRef(
                TranscriptParticipantKind.Player,
                actorId,
                displayName,
                participantId);
        }

        private static TranscriptParticipantRef BuildCharacterActor(CharacterTranscriptReceived e)
        {
            return new TranscriptParticipantRef(
                TranscriptParticipantKind.Character,
                e.CharacterId,
                e.CharacterName,
                e.Message.ParticipantId);
        }

        private static string BuildActorKey(TranscriptParticipantKind kind, string actorId, string participantId) =>
            !string.IsNullOrWhiteSpace(participantId)
                ? $"{kind}:participant:{participantId}"
                : !string.IsNullOrWhiteSpace(actorId)
                    ? $"{kind}:actor:{actorId}"
                    : $"{kind}:anonymous";

        private static string MergeTranscriptText(string existing, string incoming)
        {
            if (string.IsNullOrWhiteSpace(existing)) return incoming ?? string.Empty;
            if (string.IsNullOrWhiteSpace(incoming)) return existing ?? string.Empty;

            if (existing.EndsWith(incoming, StringComparison.Ordinal)) return existing;
            if (incoming.StartsWith(existing, StringComparison.Ordinal)) return incoming;

            char lastChar = existing[existing.Length - 1];
            return char.IsWhiteSpace(lastChar)
                ? existing + incoming
                : existing + " " + incoming;
        }

        private static string AppendTurnText(string existing, string incoming)
        {
            if (string.IsNullOrWhiteSpace(existing)) return incoming ?? string.Empty;
            if (string.IsNullOrWhiteSpace(incoming)) return existing ?? string.Empty;
            if (existing.EndsWith(incoming, StringComparison.Ordinal)) return existing;

            char lastChar = existing[existing.Length - 1];
            return char.IsWhiteSpace(lastChar)
                ? existing + incoming
                : existing + " " + incoming;
        }

        private sealed class TurnState
        {
            private readonly List<SegmentState> _segments = new();

            public TurnState(
                string turnId,
                long roomSequence,
                TranscriptParticipantRef actor,
                string actorKey,
                DateTime startedAtUtc)
            {
                TurnId = turnId;
                RoomSequence = roomSequence;
                Actor = actor;
                ActorKey = actorKey ?? string.Empty;
                StartedAtUtc = startedAtUtc;
                LastUpdatedAtUtc = startedAtUtc;
            }

            public string TurnId { get; }

            public long RoomSequence { get; }

            public TranscriptParticipantRef Actor { get; set; }

            public string ActorKey { get; set; }

            public DateTime StartedAtUtc { get; }

            public DateTime LastUpdatedAtUtc { get; set; }

            public DateTime? CompletedAtUtc { get; set; }

            public bool WasInterrupted { get; set; }

            public bool IsCompleted => CompletedAtUtc.HasValue;

            public int NextSegmentIndex { get; set; } = 1;

            public IReadOnlyList<SegmentState> Segments => _segments;

            public SegmentState ActiveSegment => _segments.LastOrDefault(segment => !segment.IsClosed);

            public bool HasAnyText => _segments.Any(segment => segment.HasText);

            public void AddSegment(SegmentState segment)
            {
                _segments.Add(segment);
                LastUpdatedAtUtc = segment.UpdatedAtUtc;
            }

            public void RemoveSegment(SegmentState segment)
            {
                _segments.Remove(segment);
                LastUpdatedAtUtc = DateTime.UtcNow;
            }

            public SegmentState GetOrCreateCharacterSegment(DateTime timestamp)
            {
                SegmentState segment = ActiveSegment;
                if (segment != null) return segment;

                segment = new SegmentState(
                    $"character-segment-{TurnId}",
                    TurnId,
                    Actor,
                    timestamp,
                    TranscriptSegmentSourceKind.CharacterTranscript);

                AddSegment(segment);
                return segment;
            }
        }

        private sealed class SegmentState
        {
            public SegmentState(
                string segmentId,
                string sourceSessionId,
                TranscriptParticipantRef actor,
                DateTime startedAtUtc,
                TranscriptSegmentSourceKind sourceKind)
            {
                SegmentId = segmentId;
                SourceSessionId = sourceSessionId;
                Actor = actor;
                StartedAtUtc = startedAtUtc;
                UpdatedAtUtc = startedAtUtc;
                SourceKind = sourceKind;
            }

            public string SegmentId { get; }

            public string SourceSessionId { get; }

            public TranscriptParticipantRef Actor { get; set; }

            public string CommittedText { get; set; } = string.Empty;

            public string InterimText { get; set; } = string.Empty;

            public string ProcessedOverrideText { get; set; } = string.Empty;

            public DateTime StartedAtUtc { get; }

            public DateTime UpdatedAtUtc { get; set; }

            public DateTime? StoppedAtUtc { get; set; }

            public TranscriptLifecycle Lifecycle { get; set; } = TranscriptLifecycle.Streaming;

            public TranscriptSegmentSourceKind SourceKind { get; set; }

            public bool CanStartNewSubsegment { get; set; }

            public bool IsClosed { get; set; }

            public bool HasStableText => !string.IsNullOrWhiteSpace(StableDisplayText);

            public bool HasText => !string.IsNullOrWhiteSpace(DisplayText);

            public string StableDisplayText => !string.IsNullOrWhiteSpace(ProcessedOverrideText)
                ? ProcessedOverrideText
                : CommittedText ?? string.Empty;

            public string DisplayText
            {
                get
                {
                    if (!string.IsNullOrWhiteSpace(ProcessedOverrideText) && string.IsNullOrWhiteSpace(InterimText))
                        return ProcessedOverrideText;

                    if (string.IsNullOrWhiteSpace(CommittedText)) return InterimText ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(InterimText)) return StableDisplayText;

                    char lastChar = CommittedText[CommittedText.Length - 1];
                    return char.IsWhiteSpace(lastChar)
                        ? CommittedText + InterimText
                        : CommittedText + " " + InterimText;
                }
            }

            public TranscriptLifecycle GetLifecycle(bool turnCompleted)
            {
                if (turnCompleted) return TranscriptLifecycle.Completed;
                if (!string.IsNullOrWhiteSpace(InterimText)) return TranscriptLifecycle.Streaming;
                return TranscriptLifecycle.Stable;
            }
        }
    }
}
