using System;
using System.Collections.Generic;
using System.Threading;
using Convai.Domain.DomainEvents.Session;
using Convai.Infrastructure.Networking;
using Convai.Infrastructure.Networking.Transport;
using Convai.Runtime.Core.Async;
using Convai.Runtime.Core.Coordinators;
using Convai.Shared.Types;
using RtviSceneMetadata = Convai.Infrastructure.Protocol.Messages.SceneMetadata;

namespace Convai.Runtime.Room
{
    /// <summary>
    ///     Provides Unity-facing access to Convai room connection state, events, and room surfaces.
    ///     Uses platform-agnostic abstractions for cross-platform compatibility.
    /// </summary>
    public interface IConvaiRoomConnectionService
    {
        /// <summary>
        ///     Gets the connection type configured for this room session.
        ///     Vision publishing is only active when this returns <see cref="ConvaiConnectionType.Video" />.
        /// </summary>
        public ConvaiConnectionType ConnectionType { get; }

        /// <summary>
        ///     Gets the current session state.
        /// </summary>
        public SessionState CurrentState { get; }

        /// <summary>
        ///     Indicates whether the SDK is currently connected to the Convai room.
        /// </summary>
        public bool IsConnected { get; }

        /// <summary>
        ///     Indicates whether the room has valid details (token, session, LiveKit room).
        /// </summary>
        public bool HasRoomDetails { get; }

        /// <summary>
        ///     Indicates whether a room ownership change has been accepted but still requires a disconnect/reconnect
        ///     before it becomes active.
        /// </summary>
        public bool HasPendingOwnershipReconnect { get; }

        /// <summary>
        ///     Gets the active room facade (null until connected).
        ///     Provides platform-agnostic access to room state and participants.
        /// </summary>
        public IRoomFacade CurrentRoom { get; }

        /// <summary>
        ///     Gets the RTVI handler for publishing transport payloads.
        /// </summary>
        public RTVIHandler RtvHandler { get; }

        /// <summary>
        ///     Raised whenever the room connection succeeds.
        /// </summary>
        public event Action Connected;

        /// <summary>
        ///     Raised whenever a lifecycle/session error occurs.
        /// </summary>
        public event Action<SessionError> OnSessionError;

        /// <summary>
        ///     Raised whenever the session state changes.
        ///     Unity-safe convenience event; EventHub also publishes SessionStateChanged for decoupled consumers.
        /// </summary>
        public event Action<SessionStateChanged> OnSessionStateChanged;

        /// <summary>
        ///     Initiates a connection workflow using the configured room manager.
        /// </summary>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <returns>Operation resolving with the established room session.</returns>
        public IConvaiOperation<RoomSession> ConnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        ///     Initiates a connection workflow using per-call session connect options.
        /// </summary>
        /// <param name="options">Per-call connection options that override configured defaults for this connect attempt.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <returns>Operation resolving with the established room session.</returns>
        public IConvaiOperation<RoomSession> ConnectAsync(
            RoomSessionConnectOptions options,
            CancellationToken cancellationToken = default);

        /// <summary>
        ///     Disconnects from the Convai room for the supplied reason.
        /// </summary>
        /// <param name="reason">High-level disconnect reason.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        public IConvaiOperation<Unit> DisconnectAsync(DisconnectReason reason = DisconnectReason.ClientInitiated,
            CancellationToken cancellationToken = default);

        /// <summary>
        ///     Sends a trigger event to the conversation backend.
        /// </summary>
        /// <param name="triggerName">Name of the trigger to send.</param>
        /// <param name="triggerMessage">Optional message payload.</param>
        /// <returns>True if the message was sent; false if the connection is not ready.</returns>
        public bool SendTrigger(string triggerName, string triggerMessage = null);

        /// <summary>
        ///     Sends dynamic context information to the backend.
        ///     This is injected as a context update for the character.
        /// </summary>
        /// <param name="contextText">The dynamic context text to send.</param>
        /// <returns>True if the message was sent; false if the connection is not ready.</returns>
        public bool SendDynamicInfo(string contextText);

        /// <summary>
        ///     Updates scene metadata the backend can use for contextual grounding.
        /// </summary>
        /// <param name="sceneMetadata">Scene metadata entries to send.</param>
        /// <returns>True if the message was sent; false if the connection is not ready.</returns>
        public bool UpdateSceneMetadata(IReadOnlyList<RtviSceneMetadata> sceneMetadata);

        /// <summary>
        ///     Updates template keys for narrative design placeholder resolution.
        ///     Template keys like {PlayerName} in objectives will be replaced with the corresponding value.
        /// </summary>
        /// <param name="templateKeys">Dictionary of key-value pairs to update.</param>
        /// <returns>True if the message was sent; false if the connection is not ready.</returns>
        public bool UpdateTemplateKeys(Dictionary<string, string> templateKeys);

        /// <summary>
        ///     Updates the bot's temporary runtime (ephemeral) context.
        /// </summary>
        /// <param name="text">The new context text to apply. Required when <paramref name="mode" /> is not "reset".</param>
        /// <param name="mode">How to apply: "append" (default), "replace", or "reset".</param>
        /// <param name="runLlm">Whether to trigger an LLM response: "true", "false", or "auto" (default).</param>
        /// <returns>True if the message was sent; false if the connection is not ready.</returns>
        public bool UpdateDynamicContext(string text, string mode = "append", string runLlm = "auto");

        /// <summary>
        ///     Enables or disables server-side text-to-speech output.
        /// </summary>
        public bool SetTtsEnabled(bool ttsEnabled);

        /// <summary>
        ///     Mutes or unmutes server-side speech-to-text processing.
        /// </summary>
        public bool SetSttMuted(bool muted);

        /// <summary>
        ///     Interrupts the bot's current speech output.
        /// </summary>
        public bool InterruptBot();

        /// <summary>
        ///     Terminates the current server-side pipeline/session.
        /// </summary>
        public bool KillPipeline();

        /// <summary>
        ///     Signals that the user has stopped speaking in push-to-talk flows.
        /// </summary>
        public bool ForceUserStoppedSpeaking();

        /// <summary>
        ///     Resets server-side idle timeout tracking for the current session.
        /// </summary>
        public bool ResetIdleTimer();
    }
}
