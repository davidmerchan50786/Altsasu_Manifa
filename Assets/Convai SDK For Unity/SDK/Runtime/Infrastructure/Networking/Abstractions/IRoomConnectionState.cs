namespace Convai.Infrastructure.Networking
{
    /// <summary>
    ///     Focused read-only view of the room connection state.
    ///     Extracted from <see cref="IConvaiRoomController" /> so consumers that only
    ///     need to inspect state (e.g. guards, diagnostics) can depend on this narrow contract.
    /// </summary>
    public interface IRoomConnectionState
    {
        /// <summary>
        ///     Gets whether room connection details have been successfully retrieved.
        /// </summary>
        public bool HasRoomDetails { get; }

        /// <summary>
        ///     Gets whether currently connected to the room.
        /// </summary>
        public bool IsConnectedToRoom { get; }

        /// <summary>
        ///     Gets whether the microphone is currently muted.
        /// </summary>
        public bool IsMicMuted { get; }

        /// <summary>
        ///     Gets the session ID for the current connection.
        /// </summary>
        public string SessionID { get; }

        /// <summary>
        ///     Gets the character session ID for the current connection.
        /// </summary>
        public string CharacterSessionID { get; }

        /// <summary>
        ///     Gets the room name for the current connection.
        /// </summary>
        public string RoomName { get; }

        /// <summary>
        ///     Gets the room URL for the current connection.
        /// </summary>
        public string RoomURL { get; }

        /// <summary>
        ///     Gets the authentication token for the current connection.
        /// </summary>
        public string Token { get; }

        /// <summary>
        ///     Gets the optional backend-resolved speaker ID for the local participant.
        ///     This is diagnostics-only state and may be empty when the backend does not return it.
        /// </summary>
        public string ResolvedSpeakerId { get; }

        /// <summary>
        ///     Gets the active RTVI handler for the current room.
        /// </summary>
        public RTVIHandler RTVIHandler { get; }

        /// <summary>
        ///     Gets the current room facade for room-backed runtime services.
        ///     Null until a room is available for the active platform/runtime path.
        /// </summary>
        public IRoomFacade CurrentRoom { get; }
    }
}
