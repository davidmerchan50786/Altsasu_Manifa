using System;
using Convai.Application.Services.Transcript;
using Convai.Domain.DomainEvents.Runtime;
using Convai.Domain.DomainEvents.Transcript;
using Convai.Domain.EventSystem;
using Convai.Domain.Models;
using NUnit.Framework;

namespace Convai.Tests.EditMode.Application
{
    /// <summary>
    ///     Unit tests for <see cref="ConversationHistoryService" />.
    /// </summary>
    public class ConversationHistoryServiceTests
    {
        [Test]
        public void Stores_Completed_Character_Turns()
        {
            EventHub hub = CreateEventHub();
            using var engine = new RoomTranscriptEngine(hub);
            using var history = new ConversationHistoryService(engine);

            hub.Publish(CharacterTranscriptReceived.Create("char-1", "Alice", "Hello!", true));
            hub.Publish(CharacterTurnCompleted.Create("char-1", "char-1", false));

            Assert.AreEqual(1, history.Count);
            Assert.AreEqual("Hello!", history.Entries[0].Message.Text);
            Assert.AreEqual(SpeakerType.Character, history.Entries[0].Speaker);
        }

        [Test]
        public void Ignores_Interim_Character_Transcripts_Until_Turn_Completes()
        {
            EventHub hub = CreateEventHub();
            using var engine = new RoomTranscriptEngine(hub);
            using var history = new ConversationHistoryService(engine);

            hub.Publish(CharacterTranscriptReceived.Create("char-1", "Alice", "Hello...", false));
            hub.Publish(CharacterTurnCompleted.Create("char-1", "char-1", false));

            Assert.AreEqual(0, history.Count);
        }

        [Test]
        public void Stores_Player_Transcript_When_Character_Response_Closes_Player_Turn()
        {
            EventHub hub = CreateEventHub();
            using var engine = new RoomTranscriptEngine(hub);
            using var history = new ConversationHistoryService(engine);

            hub.Publish(PlayerTranscriptReceived.Create(
                "player-1",
                "You",
                "Hi there!",
                true,
                TranscriptionPhase.ProcessedFinal,
                turnId: "turn-1",
                messageId: "turn-1"));
            hub.Publish(PlayerTranscriptReceived.Create(
                "player-1",
                "You",
                string.Empty,
                true,
                TranscriptionPhase.Completed,
                turnId: "turn-1",
                messageId: "turn-1"));

            Assert.AreEqual(0, history.Count);

            hub.Publish(CharacterTranscriptReceived.Create("char-1", "Alice", "Hello!", true));

            Assert.AreEqual(1, history.Count);
            Assert.AreEqual("Hi there!", history.Entries[0].Message.Text);
            Assert.AreEqual(SpeakerType.Player, history.Entries[0].Speaker);
        }

        [Test]
        public void Deduplicates_Completed_Turns_By_TurnId()
        {
            EventHub hub = CreateEventHub();
            using var engine = new RoomTranscriptEngine(hub);
            using var history = new ConversationHistoryService(engine);

            hub.Publish(PlayerTranscriptReceived.Create(
                "player-1",
                "You",
                "Hi there!",
                true,
                TranscriptionPhase.AsrFinal,
                turnId: "turn-1",
                messageId: "turn-1"));
            hub.Publish(PlayerTranscriptReceived.Create(
                "player-1",
                "You",
                string.Empty,
                true,
                TranscriptionPhase.Completed,
                turnId: "turn-1",
                messageId: "turn-1"));

            hub.Publish(CharacterTranscriptReceived.Create("char-1", "Alice", "Hello!", true));
            hub.Publish(CharacterTurnCompleted.Create("char-1", "char-1", false));
            hub.Publish(CharacterTranscriptReceived.Create("char-1", "Alice", "Hello!", true));

            Assert.AreEqual(2, history.Count);
            Assert.AreEqual("Hi there!", history.Entries[0].Message.Text);
            Assert.AreEqual("Hello!", history.Entries[1].Message.Text);
        }

        [Test]
        public void Ignores_Interim_Player_Transcripts_Without_Final_Commit()
        {
            EventHub hub = CreateEventHub();
            using var engine = new RoomTranscriptEngine(hub);
            using var history = new ConversationHistoryService(engine);

            hub.Publish(PlayerTranscriptReceived.Create(
                "player-1",
                "You",
                "Hi...",
                false,
                turnId: "turn-1",
                messageId: "turn-1"));
            hub.Publish(PlayerTranscriptReceived.Create(
                "player-1",
                "You",
                string.Empty,
                false,
                TranscriptionPhase.Completed,
                turnId: "turn-1",
                messageId: "turn-1"));
            hub.Publish(CharacterTranscriptReceived.Create("char-1", "Alice", "Hello!", true));

            Assert.AreEqual(0, history.Count);
        }

        [Test]
        public void Respects_MaxEntries_Limit()
        {
            EventHub hub = CreateEventHub();
            using var engine = new RoomTranscriptEngine(hub);
            using var history = new ConversationHistoryService(engine, 2);

            hub.Publish(CharacterTranscriptReceived.Create("char-1", "Alice", "Message 1", true));
            hub.Publish(CharacterTurnCompleted.Create("char-1", "char-1", false));
            hub.Publish(CharacterTranscriptReceived.Create("char-1", "Alice", "Message 2", true));
            hub.Publish(CharacterTurnCompleted.Create("char-1", "char-1", false));
            hub.Publish(CharacterTranscriptReceived.Create("char-1", "Alice", "Message 3", true));
            hub.Publish(CharacterTurnCompleted.Create("char-1", "char-1", false));

            Assert.AreEqual(2, history.Count);
            Assert.AreEqual("Message 2", history.Entries[0].Message.Text);
            Assert.AreEqual("Message 3", history.Entries[1].Message.Text);
        }

        [Test]
        public void Clear_Removes_All_Entries()
        {
            EventHub hub = CreateEventHub();
            using var engine = new RoomTranscriptEngine(hub);
            using var history = new ConversationHistoryService(engine);

            hub.Publish(CharacterTranscriptReceived.Create("char-1", "Alice", "Hello!", true));
            hub.Publish(CharacterTurnCompleted.Create("char-1", "char-1", false));

            history.Clear();

            Assert.AreEqual(0, history.Count);
        }

        [Test]
        public void GetEntriesByActor_Filters_Correctly()
        {
            EventHub hub = CreateEventHub();
            using var engine = new RoomTranscriptEngine(hub);
            using var history = new ConversationHistoryService(engine);

            hub.Publish(CharacterTranscriptReceived.Create("char-1", "Alice", "Hello!", true));
            hub.Publish(CharacterTurnCompleted.Create("char-1", "char-1", false));
            hub.Publish(CharacterTranscriptReceived.Create("char-2", "Bob", "Hi!", true));
            hub.Publish(CharacterTurnCompleted.Create("char-2", "char-2", false));
            hub.Publish(CharacterTranscriptReceived.Create("char-1", "Alice", "How are you?", true));
            hub.Publish(CharacterTurnCompleted.Create("char-1", "char-1", false));

            var aliceEntries = history.GetEntriesByPlayerOrCharacterId("char-1");

            Assert.AreEqual(2, aliceEntries.Count);
            Assert.AreEqual("Hello!", aliceEntries[0].Message.Text);
            Assert.AreEqual("How are you?", aliceEntries[1].Message.Text);
        }

        [Test]
        public void Export_PlainText_Format()
        {
            EventHub hub = CreateEventHub();
            using var engine = new RoomTranscriptEngine(hub);
            using var history = new ConversationHistoryService(engine);

            hub.Publish(CharacterTranscriptReceived.Create("char-1", "Alice", "Hello!", true));
            hub.Publish(CharacterTurnCompleted.Create("char-1", "char-1", false));

            string exported = history.Export(ConversationExportFormat.PlainText);

            Assert.IsTrue(exported.Contains("Alice"));
            Assert.IsTrue(exported.Contains("Hello!"));
        }

        [Test]
        public void Export_Json_Format()
        {
            EventHub hub = CreateEventHub();
            using var engine = new RoomTranscriptEngine(hub);
            using var history = new ConversationHistoryService(engine);

            hub.Publish(CharacterTranscriptReceived.Create("char-1", "Alice", "Hello!", true));
            hub.Publish(CharacterTurnCompleted.Create("char-1", "char-1", false));

            string exported = history.Export(ConversationExportFormat.Json);

            Assert.IsTrue(exported.StartsWith("["));
            Assert.IsTrue(exported.EndsWith("]"));
            Assert.IsTrue(exported.Contains("\"speaker\":\"Alice\""));
            Assert.IsTrue(exported.Contains("\"text\":\"Hello!\""));
        }

        [Test]
        public void Export_Markdown_Format()
        {
            EventHub hub = CreateEventHub();
            using var engine = new RoomTranscriptEngine(hub);
            using var history = new ConversationHistoryService(engine);

            hub.Publish(CharacterTranscriptReceived.Create("char-1", "Alice", "Hello!", true));
            hub.Publish(CharacterTurnCompleted.Create("char-1", "char-1", false));

            string exported = history.Export(ConversationExportFormat.Markdown);

            Assert.IsTrue(exported.Contains("# Conversation Transcript"));
            Assert.IsTrue(exported.Contains("**Alice**"));
            Assert.IsTrue(exported.Contains("> Hello!"));
        }

        [Test]
        public void EntryAdded_Event_Fires()
        {
            EventHub hub = CreateEventHub();
            using var engine = new RoomTranscriptEngine(hub);
            using var history = new ConversationHistoryService(engine);

            TranscriptEntry capturedEntry = null;
            history.EntryAdded += entry => capturedEntry = entry;

            hub.Publish(CharacterTranscriptReceived.Create("char-1", "Alice", "Hello!", true));
            hub.Publish(CharacterTurnCompleted.Create("char-1", "char-1", false));

            Assert.IsNotNull(capturedEntry);
            Assert.AreEqual("Hello!", capturedEntry.Message.Text);
        }

        [Test]
        public void Ignores_Empty_Text()
        {
            EventHub hub = CreateEventHub();
            using var engine = new RoomTranscriptEngine(hub);
            using var history = new ConversationHistoryService(engine);

            hub.Publish(CharacterTranscriptReceived.Create("char-1", "Alice", string.Empty, true));
            hub.Publish(CharacterTurnCompleted.Create("char-1", "char-1", false));
            hub.Publish(CharacterTranscriptReceived.Create("char-1", "Alice", "   ", true));
            hub.Publish(CharacterTurnCompleted.Create("char-1", "char-1", false));

            Assert.AreEqual(0, history.Count);
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
