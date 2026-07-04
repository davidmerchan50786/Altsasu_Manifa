using System;
using System.Collections.Generic;
using Convai.Application.Services.Transcript;
using Convai.Domain.DomainEvents.Transcript;
using Convai.Domain.EventSystem;
using Convai.Domain.Models;
using Convai.Runtime.Presentation.Presenters;
using Convai.Runtime.Presentation.Services;
using NUnit.Framework;

namespace Convai.Tests.EditMode
{
    public class TranscriptUIControllerTests
    {
        [Test]
        public void Multiple_Player_Segments_Before_Character_Response_Stay_In_One_Bubble()
        {
            EventHub eventHub = CreateEventHub();
            using var engine = new RoomTranscriptEngine(eventHub);
            using var controller = new TranscriptUIController(engine);
            var ui = new FakeTranscriptUI();
            controller.Register(ui);

            eventHub.Publish(PlayerTranscriptReceived.Create(
                "player-1",
                "You",
                "Hey. Hello.",
                false,
                TranscriptionPhase.AsrFinal,
                turnId: "session-1",
                messageId: "session-1"));
            eventHub.Publish(PlayerTranscriptReceived.Create(
                "player-1",
                "You",
                string.Empty,
                false,
                TranscriptionPhase.Completed,
                turnId: "session-1",
                messageId: "session-1"));
            eventHub.Publish(PlayerTranscriptReceived.Create(
                "player-1",
                "You",
                "You actually",
                false,
                turnId: "session-2",
                messageId: "session-2"));

            Assert.AreEqual("session-1", ui.LastDisplayed.MessageId);
            Assert.AreEqual("Hey. Hello. You actually", ui.LastDisplayed.Text);
        }

        [Test]
        public void Character_Transcript_Arrival_Completes_Pending_Player_Bubble()
        {
            EventHub eventHub = CreateEventHub();
            using var engine = new RoomTranscriptEngine(eventHub);
            using var controller = new TranscriptUIController(engine);
            var ui = new FakeTranscriptUI();
            controller.Register(ui);

            eventHub.Publish(PlayerTranscriptReceived.Create(
                "player-1",
                "You",
                "First phrase",
                false,
                TranscriptionPhase.AsrFinal,
                turnId: "turn-1",
                messageId: "turn-1"));
            eventHub.Publish(PlayerTranscriptReceived.Create(
                "player-1",
                "You",
                string.Empty,
                false,
                TranscriptionPhase.Completed,
                turnId: "turn-1",
                messageId: "turn-1"));

            eventHub.Publish(CharacterTranscriptReceived.Create("npc-1", "Camila", "Response", true));
            eventHub.Publish(PlayerTranscriptReceived.Create(
                "player-1",
                "You",
                "New question",
                false,
                turnId: "turn-2",
                messageId: "turn-2"));

            CollectionAssert.Contains(ui.CompletedMessageIds, "turn-1");
            Assert.AreEqual(TranscriptSpeaker.Player, ui.LastDisplayed.Speaker);
            Assert.AreEqual("turn-2", ui.LastDisplayed.MessageId);
            Assert.AreEqual("New question", ui.LastDisplayed.Text);
        }

        [Test]
        public void Player_Transcript_Completes_Active_Character_Bubble_When_Player_Interrupts()
        {
            EventHub eventHub = CreateEventHub();
            using var engine = new RoomTranscriptEngine(eventHub);
            using var controller = new TranscriptUIController(engine);
            var ui = new FakeTranscriptUI();
            controller.Register(ui);

            eventHub.Publish(CharacterTranscriptReceived.Create("npc-1", "Camila", "First response", true));
            string firstCharacterMessageId = ui.LastDisplayed.MessageId;

            eventHub.Publish(PlayerTranscriptReceived.Create(
                "player-1",
                "You",
                "Please repeat",
                false,
                turnId: "turn-2",
                messageId: "turn-2"));

            CollectionAssert.Contains(ui.CompletedMessageIds, firstCharacterMessageId);
            Assert.AreEqual(TranscriptSpeaker.Player, ui.LastDisplayed.Speaker);
            Assert.AreEqual("turn-2", ui.LastDisplayed.MessageId);
        }

        [Test]
        public void Repeated_Character_Response_Starts_A_Fresh_Bubble_After_Player_Retry()
        {
            EventHub eventHub = CreateEventHub();
            using var engine = new RoomTranscriptEngine(eventHub);
            using var controller = new TranscriptUIController(engine);
            var ui = new FakeTranscriptUI();
            controller.Register(ui);

            eventHub.Publish(CharacterTranscriptReceived.Create("npc-1", "Camila", "First response", true));
            string firstCharacterMessageId = ui.LastDisplayed.MessageId;

            eventHub.Publish(PlayerTranscriptReceived.Create(
                "player-1",
                "You",
                "Can you repeat that?",
                false,
                turnId: "turn-2",
                messageId: "turn-2"));
            eventHub.Publish(PlayerTranscriptReceived.Create(
                "player-1",
                "You",
                "Can you repeat that?",
                true,
                TranscriptionPhase.AsrFinal,
                turnId: "turn-2",
                messageId: "turn-2"));
            eventHub.Publish(PlayerTranscriptReceived.Create(
                "player-1",
                "You",
                string.Empty,
                true,
                TranscriptionPhase.Completed,
                turnId: "turn-2",
                messageId: "turn-2"));

            eventHub.Publish(CharacterTranscriptReceived.Create("npc-1", "Camila", "Second response", true));

            Assert.AreEqual(TranscriptSpeaker.Character, ui.LastDisplayed.Speaker);
            Assert.AreEqual("Second response", ui.LastDisplayed.Text);
            Assert.AreNotEqual(firstCharacterMessageId, ui.LastDisplayed.MessageId);
            CollectionAssert.Contains(ui.CompletedMessageIds, firstCharacterMessageId);
        }

        private static EventHub CreateEventHub() => new(new ImmediateScheduler());

        private sealed class ImmediateScheduler : IUnityScheduler
        {
            public void ScheduleOnMainThread(Action action) => action?.Invoke();

            public void ScheduleOnBackground(Action action) => action?.Invoke();

            public bool IsMainThread() => true;
        }

        private sealed class FakeTranscriptUI : ITranscriptUI
        {
            public readonly List<string> CompletedMessageIds = new();
            public readonly List<TranscriptViewModel> DisplayedMessages = new();
            private bool _isActive;

            public string Identifier => "Chat";
            public bool IsActive => _isActive;
            public TranscriptViewModel LastDisplayed => DisplayedMessages[^1];

            public void DisplayMessage(TranscriptViewModel viewModel) => DisplayedMessages.Add(viewModel);

            public void CompleteMessage(string messageId) => CompletedMessageIds.Add(messageId);

            public void CompletePlayerTurn()
            {
            }

            public void ClearAll()
            {
                DisplayedMessages.Clear();
                CompletedMessageIds.Clear();
            }

            public void SetActive(bool active) => _isActive = active;
        }
    }
}
