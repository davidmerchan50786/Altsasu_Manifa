using System;
using Convai.Domain.DomainEvents.Session;
using UnityEngine;

namespace Convai.Runtime.Presentation.Events
{
    [Serializable]
    public sealed class SessionStateChangedRelayData
    {
        [SerializeField] private SessionState _oldState;
        [SerializeField] private SessionState _newState;
        [SerializeField] private string _sessionId;
        [SerializeField] private string _errorCode;

        public SessionStateChangedRelayData(SessionStateChanged e)
        {
            _oldState = e.OldState;
            _newState = e.NewState;
            _sessionId = e.SessionId ?? string.Empty;
            _errorCode = e.ErrorCode ?? string.Empty;
        }

        public SessionState OldState => _oldState;
        public SessionState NewState => _newState;
        public string SessionId => _sessionId;
        public string ErrorCode => _errorCode;
        public bool IsError => _newState == SessionState.Error;
        public bool IsReconnecting => _oldState == SessionState.Connected && _newState == SessionState.Reconnecting;

        public bool IsConnectionEstablished =>
            _oldState == SessionState.Connecting && _newState == SessionState.Connected;

        public bool IsReconnectionSuccessful =>
            _oldState == SessionState.Reconnecting && _newState == SessionState.Connected;

        public bool IsDisconnected => _newState == SessionState.Disconnected;
    }

    [Serializable]
    public sealed class SessionErrorRelayData
    {
        [SerializeField] private string _errorCode;
        [SerializeField] private string _message;
        [SerializeField] private string _sessionId;
        [SerializeField] private bool _isRecoverable;
        [SerializeField] private SessionErrorStage _stage;
        [SerializeField] private int _httpStatusCode;

        public SessionErrorRelayData(SessionError e)
        {
            _errorCode = e.ErrorCode ?? string.Empty;
            _message = e.Message ?? string.Empty;
            _sessionId = e.SessionId ?? string.Empty;
            _isRecoverable = e.IsRecoverable;
            _stage = e.Stage;
            _httpStatusCode = e.HttpStatusCode ?? 0;
        }

        public string ErrorCode => _errorCode;
        public string Message => _message;
        public string SessionId => _sessionId;
        public bool IsRecoverable => _isRecoverable;
        public SessionErrorStage Stage => _stage;
        public int HttpStatusCode => _httpStatusCode;
        public bool HasHttpStatusCode => _httpStatusCode > 0;
    }

    [Serializable]
    public sealed class CharacterTranscriptRelayData
    {
        [SerializeField] private string _characterId;
        [SerializeField] private string _characterName;
        [SerializeField] private string _text;
        [SerializeField] private bool _isFinal;

        public CharacterTranscriptRelayData(string characterId, string characterName, string text, bool isFinal)
        {
            _characterId = characterId ?? string.Empty;
            _characterName = characterName ?? string.Empty;
            _text = text ?? string.Empty;
            _isFinal = isFinal;
        }

        public string CharacterId => _characterId;
        public string CharacterName => _characterName;
        public string Text => _text;
        public bool IsFinal => _isFinal;
    }

    [Serializable]
    public sealed class PlayerTranscriptRelayData
    {
        [SerializeField] private string _playerId;
        [SerializeField] private string _playerName;
        [SerializeField] private string _speakerId;
        [SerializeField] private string _speakerName;
        [SerializeField] private string _participantId;
        [SerializeField] private string _turnId;
        [SerializeField] private string _messageId;
        [SerializeField] private string _text;
        [SerializeField] private bool _isFinal;

        public PlayerTranscriptRelayData(
            string playerId,
            string playerName,
            string speakerId,
            string speakerName,
            string participantId,
            string turnId,
            string messageId,
            string text,
            bool isFinal)
        {
            _playerId = playerId ?? string.Empty;
            _playerName = playerName ?? string.Empty;
            _speakerId = speakerId ?? string.Empty;
            _speakerName = speakerName ?? string.Empty;
            _participantId = participantId ?? string.Empty;
            _turnId = turnId ?? string.Empty;
            _messageId = messageId ?? string.Empty;
            _text = text ?? string.Empty;
            _isFinal = isFinal;
        }

        public string PlayerId => _playerId;
        public string PlayerName => _playerName;
        public string SpeakerId => _speakerId;
        public string SpeakerName => _speakerName;
        public string ParticipantId => _participantId;
        public string TurnId => _turnId;
        public string MessageId => _messageId;
        public string Text => _text;
        public bool IsFinal => _isFinal;
    }

    [Serializable]
    public sealed class CharacterEmotionRelayData
    {
        [SerializeField] private string _characterId;
        [SerializeField] private string _characterName;
        [SerializeField] private string _emotion;
        [SerializeField] private int _intensity;

        public CharacterEmotionRelayData(string characterId, string characterName, string emotion, int intensity)
        {
            _characterId = characterId ?? string.Empty;
            _characterName = characterName ?? string.Empty;
            _emotion = emotion ?? string.Empty;
            _intensity = intensity;
        }

        public string CharacterId => _characterId;
        public string CharacterName => _characterName;
        public string Emotion => _emotion;
        public int Intensity => _intensity;
    }

    [Serializable]
    public sealed class CharacterTurnCompletedRelayData
    {
        [SerializeField] private string _characterId;
        [SerializeField] private string _characterName;
        [SerializeField] private bool _wasInterrupted;

        public CharacterTurnCompletedRelayData(string characterId, string characterName, bool wasInterrupted)
        {
            _characterId = characterId ?? string.Empty;
            _characterName = characterName ?? string.Empty;
            _wasInterrupted = wasInterrupted;
        }

        public string CharacterId => _characterId;
        public string CharacterName => _characterName;
        public bool WasInterrupted => _wasInterrupted;
    }
}
