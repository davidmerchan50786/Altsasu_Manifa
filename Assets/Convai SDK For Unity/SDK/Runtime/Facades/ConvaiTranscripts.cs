using System;
using System.Collections.Generic;
using Convai.Application.Services.Transcript;
using Convai.Domain.Logging;
using Convai.Domain.Models;
using Convai.Runtime.Utilities;

namespace Convai.Runtime.Facades
{
    /// <summary>
    ///     Canonical transcript state and history facade exposed from <c>ConvaiManager.Transcripts</c>.
    /// </summary>
    /// <remarks>
    ///     Use this for room transcript timelines, turn history, chat logs, subtitle state, and replayable transcript UI.
    ///     For simple reactive callbacks to individual transcript messages, use <c>ConvaiManager.Events</c> or the
    ///     transcript relay components.
    /// </remarks>
    public sealed class ConvaiTranscripts : IDisposable
    {
        private readonly IRoomTranscriptEngine _engine;
        private bool _disposed;

        internal ConvaiTranscripts(IRoomTranscriptEngine engine)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _engine.Changed += HandleChanged;
        }

        /// <summary>Gets the current room transcript timeline snapshot.</summary>
        public TranscriptTimelineSnapshot CurrentTimeline => _engine.CurrentTimeline;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _engine.Changed -= HandleChanged;
        }

        /// <summary>Raised whenever the transcript timeline changes.</summary>
        public event Action<TranscriptUpdateBatch> Changed;

        /// <summary>Gets transcript turns that match the optional query.</summary>
        public IReadOnlyList<TranscriptTurnSnapshot> GetTurns(TranscriptQuery query = null) => _engine.GetTurns(query);

        /// <summary>Gets a specific transcript turn by turn ID.</summary>
        public TranscriptTurnSnapshot GetTurn(string turnId) => _engine.GetTurn(turnId);

        /// <summary>Gets the latest transcript turn for a participant.</summary>
        public TranscriptTurnSnapshot GetLatestTurn(TranscriptParticipantRef participant) =>
            _engine.GetLatestTurn(participant);

        private void HandleChanged(TranscriptUpdateBatch batch) =>
            SafeEventInvoker.Invoke(Changed, batch, null, "ConvaiTranscripts.Changed", LogCategory.Events);
    }
}
