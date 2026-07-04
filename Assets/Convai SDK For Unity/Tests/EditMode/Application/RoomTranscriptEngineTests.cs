using System;
using System.Linq;
using Convai.Application.Services.Transcript;
using Convai.Domain.DomainEvents.Runtime;
using Convai.Domain.DomainEvents.Transcript;
using Convai.Domain.EventSystem;
using Convai.Domain.Models;
using NUnit.Framework;

namespace Convai.Tests.EditMode.Application
{
    public class RoomTranscriptEngineTests
    {
        [Test]
        public void Final_Player_Text_Is_Preserved_When_Next_Interim_Arrives_In_Same_Session()
        {
            EventHub eventHub = CreateEventHub();
            using var engine = new RoomTranscriptEngine(eventHub);

            eventHub.Publish(PlayerTranscriptReceived.Create(
                "player-1",
                "You",
                "Hey. Hello.",
                true,
                TranscriptionPhase.AsrFinal,
                turnId: "session-1",
                messageId: "session-1"));
            eventHub.Publish(PlayerTranscriptReceived.Create(
                "player-1",
                "You",
                "You actually",
                false,
                turnId: "session-1",
                messageId: "session-1"));

            TranscriptTurnSnapshot turn = engine.CurrentTimeline.ActiveTurns.Single();

            Assert.AreEqual("session-1", turn.TurnId);
            Assert.AreEqual("Hey. Hello. You actually", turn.DisplayText);
            Assert.AreEqual(TranscriptLifecycle.Streaming, turn.Lifecycle);
        }

        [Test]
        public void Player_Sessions_Append_To_One_Turn_Until_Character_Activity()
        {
            EventHub eventHub = CreateEventHub();
            using var engine = new RoomTranscriptEngine(eventHub);

            eventHub.Publish(PlayerTranscriptReceived.Create(
                "player-1",
                "You",
                "First phrase",
                true,
                TranscriptionPhase.AsrFinal,
                turnId: "session-1",
                messageId: "session-1"));
            eventHub.Publish(PlayerTranscriptReceived.Create(
                "player-1",
                "You",
                string.Empty,
                true,
                TranscriptionPhase.Completed,
                turnId: "session-1",
                messageId: "session-1"));
            eventHub.Publish(PlayerTranscriptReceived.Create(
                "player-1",
                "You",
                "Second phrase",
                false,
                turnId: "session-2",
                messageId: "session-2"));

            TranscriptTurnSnapshot turn = engine.CurrentTimeline.ActiveTurns.Single();

            Assert.AreEqual("session-1", turn.TurnId);
            Assert.AreEqual("First phrase Second phrase", turn.DisplayText);
        }

        [Test]
        public void Character_Response_Completes_Player_Turn_And_Player_Retry_Starts_New_Turn()
        {
            EventHub eventHub = CreateEventHub();
            using var engine = new RoomTranscriptEngine(eventHub);

            eventHub.Publish(PlayerTranscriptReceived.Create(
                "player-1",
                "You",
                "Question one",
                true,
                TranscriptionPhase.AsrFinal,
                turnId: "turn-1",
                messageId: "turn-1"));
            eventHub.Publish(PlayerTranscriptReceived.Create(
                "player-1",
                "You",
                string.Empty,
                true,
                TranscriptionPhase.Completed,
                turnId: "turn-1",
                messageId: "turn-1"));

            eventHub.Publish(CharacterTranscriptReceived.Create("char-1", "Alice", "Answer one", true));

            TranscriptTurnSnapshot completedPlayerTurn = engine.CurrentTimeline.CommittedTurns
                .Single(turn => turn.Participant.Kind == TranscriptParticipantKind.Player);
            Assert.AreEqual("turn-1", completedPlayerTurn.TurnId);

            eventHub.Publish(PlayerTranscriptReceived.Create(
                "player-1",
                "You",
                "Question two",
                false,
                turnId: "turn-2",
                messageId: "turn-2"));

            TranscriptTurnSnapshot newPlayerTurn = engine.CurrentTimeline.ActiveTurns
                .Single(turn => turn.Participant.Kind == TranscriptParticipantKind.Player);

            Assert.AreEqual("turn-2", newPlayerTurn.TurnId);
            Assert.AreEqual("Question two", newPlayerTurn.DisplayText);
        }

        [Test]
        public void Player_Transcript_Interrupts_Active_Character_Turn()
        {
            EventHub eventHub = CreateEventHub();
            using var engine = new RoomTranscriptEngine(eventHub);

            eventHub.Publish(CharacterTranscriptReceived.Create("char-1", "Alice", "Long answer", true));

            TranscriptTurnSnapshot firstCharacterTurn = engine.CurrentTimeline.ActiveTurns
                .Single(turn => turn.Participant.Kind == TranscriptParticipantKind.Character);

            eventHub.Publish(PlayerTranscriptReceived.Create(
                "player-1",
                "You",
                "Please repeat",
                false,
                turnId: "turn-2",
                messageId: "turn-2"));

            TranscriptTurnSnapshot interruptedCharacterTurn = engine.CurrentTimeline.CommittedTurns
                .Single(turn => turn.Participant.Kind == TranscriptParticipantKind.Character);

            Assert.AreEqual(firstCharacterTurn.TurnId, interruptedCharacterTurn.TurnId);
            Assert.IsTrue(interruptedCharacterTurn.WasInterrupted);
        }

        [Test]
        public void Changed_Subscriber_Exception_Does_Not_Block_Other_Subscribers()
        {
            EventHub eventHub = CreateEventHub();
            using var engine = new RoomTranscriptEngine(eventHub);
            bool healthySubscriberCalled = false;

            engine.Changed += _ => throw new NullReferenceException("boom");
            engine.Changed += _ => healthySubscriberCalled = true;

            eventHub.Publish(PlayerTranscriptReceived.Create(
                "player-1",
                "You",
                "Hello",
                false,
                turnId: "turn-1",
                messageId: "turn-1"));

            Assert.IsTrue(healthySubscriberCalled);
        }

        [Test]
        public void Character_TurnCompleted_Can_Close_Turn_By_ParticipantId_When_CharacterId_Is_Missing()
        {
            EventHub eventHub = CreateEventHub();
            using var engine = new RoomTranscriptEngine(eventHub);

            TranscriptMessage message = new(
                string.Empty,
                "Alice",
                "Hello!",
                true,
                DateTime.UtcNow,
                participantId: "participant-1",
                speakerType: SpeakerType.Character);

            eventHub.Publish(new CharacterTranscriptReceived(message));
            eventHub.Publish(CharacterTurnCompleted.Create(string.Empty, "participant-1", false));

            Assert.AreEqual(0, engine.CurrentTimeline.ActiveTurns.Count);
            Assert.AreEqual(1, engine.CurrentTimeline.CommittedTurns.Count);
            Assert.AreEqual("participant-1", engine.CurrentTimeline.CommittedTurns.Single().Participant.ParticipantId);
        }

        [Test]
        public void Empty_Character_Completion_Does_Not_Close_Open_Turns()
        {
            EventHub eventHub = CreateEventHub();
            using var engine = new RoomTranscriptEngine(eventHub);

            TranscriptMessage message = new(
                string.Empty,
                "Alice",
                "Hello!",
                true,
                DateTime.UtcNow,
                participantId: "participant-1",
                speakerType: SpeakerType.Character);

            eventHub.Publish(new CharacterTranscriptReceived(message));
            eventHub.Publish(CharacterTurnCompleted.Create(string.Empty, string.Empty, false));

            Assert.AreEqual(1, engine.CurrentTimeline.ActiveTurns.Count);
            Assert.AreEqual(0, engine.CurrentTimeline.CommittedTurns.Count);
        }

        private static EventHub CreateEventHub() => new(new ImmediateScheduler());

        private sealed class ImmediateScheduler : IUnityScheduler
        {
            public void ScheduleOnMainThread(Action action) => action?.Invoke();

            public void ScheduleOnBackground(Action action) => action?.Invoke();

            public bool IsMainThread() => true;
        }
    }
}
