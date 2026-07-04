using System;
using System.Collections.Generic;
using Convai.Domain.DomainEvents.Transcript;
using Convai.Domain.EventSystem;
using Convai.Runtime.Services.Transcript;
using NUnit.Framework;
using TransportPhase = Convai.Domain.Models.TranscriptionPhase;
using DomainPhase = Convai.Domain.Models.TranscriptionPhase;

namespace Convai.Tests.EditMode
{
    /// <summary>
    ///     Unit tests for <see cref="PlayerTranscriptAdapter" />.
    ///     Verifies that the adapter forwards ALL phases to the Application layer
    ///     without any filtering (per the architectural principle that adapters should be "dumb").
    /// </summary>
    public class PlayerTranscriptAdapterTests
    {
        [Test]
        public void Interim_Phase_Is_Forwarded_With_Correct_Phase()
        {
            var hub = new CapturingEventHub();
            using var adapter = new PlayerTranscriptAdapter(hub, "player-1", "TestPlayer");

            adapter.OnPlayerTranscriptionReceived("Hello", TransportPhase.Interim);

            Assert.AreEqual(1, hub.CapturedEvents.Count);
            Assert.AreEqual(DomainPhase.Interim, hub.CapturedEvents[0].Phase);
            Assert.AreEqual("Hello", hub.CapturedEvents[0].Message.Text);
            Assert.AreEqual("player-1", hub.CapturedEvents[0].Message.PlayerOrCharacterId);
            Assert.AreEqual("TestPlayer", hub.CapturedEvents[0].Message.DisplayName);
        }

        [Test]
        public void AsrFinal_Phase_Is_Forwarded()
        {
            var hub = new CapturingEventHub();
            using var adapter = new PlayerTranscriptAdapter(hub, "player-1", "TestPlayer");

            adapter.OnPlayerTranscriptionReceived("Hello world", TransportPhase.AsrFinal);

            Assert.AreEqual(1, hub.CapturedEvents.Count, "AsrFinal should be forwarded to Application layer");
            Assert.AreEqual(DomainPhase.AsrFinal, hub.CapturedEvents[0].Phase);
            Assert.AreEqual("Hello world", hub.CapturedEvents[0].Message.Text);
        }

        [Test]
        public void ProcessedFinal_Phase_Is_Forwarded()
        {
            var hub = new CapturingEventHub();
            using var adapter = new PlayerTranscriptAdapter(hub, "player-1", "TestPlayer");

            adapter.OnPlayerTranscriptionReceived("Processed text", TransportPhase.ProcessedFinal);

            Assert.AreEqual(1, hub.CapturedEvents.Count, "ProcessedFinal should be forwarded to Application layer");
            Assert.AreEqual(DomainPhase.ProcessedFinal, hub.CapturedEvents[0].Phase);
            Assert.AreEqual("Processed text", hub.CapturedEvents[0].Message.Text);
        }

        [Test]
        public void Completed_Phase_Is_Forwarded()
        {
            var hub = new CapturingEventHub();
            using var adapter = new PlayerTranscriptAdapter(hub, "player-1", "TestPlayer");

            adapter.OnPlayerTranscriptionReceived("Final text", TransportPhase.Completed);

            Assert.AreEqual(1, hub.CapturedEvents.Count);
            Assert.AreEqual(DomainPhase.Completed, hub.CapturedEvents[0].Phase);
        }

        [Test]
        public void Listening_Phase_Is_Forwarded()
        {
            var hub = new CapturingEventHub();
            using var adapter = new PlayerTranscriptAdapter(hub, "player-1", "TestPlayer");

            adapter.OnPlayerTranscriptionReceived("", TransportPhase.Listening);

            Assert.AreEqual(1, hub.CapturedEvents.Count, "Listening phase should be forwarded to Application layer");
            Assert.AreEqual(DomainPhase.Listening, hub.CapturedEvents[0].Phase);
        }

        [Test]
        public void Idle_Phase_Is_Forwarded()
        {
            var hub = new CapturingEventHub();
            using var adapter = new PlayerTranscriptAdapter(hub, "player-1", "TestPlayer");

            adapter.OnPlayerTranscriptionReceived("", TransportPhase.Idle);

            Assert.AreEqual(1, hub.CapturedEvents.Count, "Idle phase should be forwarded to Application layer");
            Assert.AreEqual(DomainPhase.Idle, hub.CapturedEvents[0].Phase);
        }

        [Test]
        public void Empty_Transcript_Is_Forwarded_With_Empty_String()
        {
            var hub = new CapturingEventHub();
            using var adapter = new PlayerTranscriptAdapter(hub, "player-1", "TestPlayer");

            adapter.OnPlayerTranscriptionReceived("", TransportPhase.Interim);
            adapter.OnPlayerTranscriptionReceived("   ", TransportPhase.Completed);
            adapter.OnPlayerTranscriptionReceived(null, TransportPhase.Completed);

            Assert.AreEqual(3, hub.CapturedEvents.Count, "All events should be forwarded");
            Assert.AreEqual("", hub.CapturedEvents[0].Message.Text);
            Assert.AreEqual("   ", hub.CapturedEvents[1].Message.Text);
            Assert.AreEqual("", hub.CapturedEvents[2].Message.Text);
        }

        [Test]
        public void Disposed_Adapter_Does_Not_Publish()
        {
            var hub = new CapturingEventHub();
            var adapter = new PlayerTranscriptAdapter(hub, "player-1", "TestPlayer");
            adapter.Dispose();

            adapter.OnPlayerTranscriptionReceived("Hello", TransportPhase.Interim);

            Assert.AreEqual(0, hub.CapturedEvents.Count, "Disposed adapter should not publish");
        }

        [Test]
        public void Default_PlayerName_Is_You()
        {
            var hub = new CapturingEventHub();
            using var adapter = new PlayerTranscriptAdapter(hub, "player-1");

            Assert.AreEqual("You", adapter.PlayerName);
        }

        [Test]
        public void Dynamic_PlayerNameProvider_Is_Used_At_Publish_Time()
        {
            var hub = new CapturingEventHub();
            string runtimeName = "Risha";
            string name = runtimeName;
            using var adapter = new PlayerTranscriptAdapter(
                hub,
                "player-1",
                "Fallback",
                () => name);

            adapter.OnPlayerTranscriptionReceived("First", TransportPhase.Interim);
            runtimeName = "Updated";
            adapter.OnPlayerTranscriptionReceived("Second", TransportPhase.Completed);

            Assert.AreEqual(2, hub.CapturedEvents.Count);
            Assert.AreEqual("Risha", hub.CapturedEvents[0].Message.DisplayName);
            Assert.AreEqual("Updated", hub.CapturedEvents[1].Message.DisplayName);
            Assert.AreEqual("Updated", adapter.PlayerName);
        }

        [Test]
        public void Constructor_Throws_On_Null_EventHub()
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                using var adapter = new PlayerTranscriptAdapter(null, "player-1");
            });
        }

        [Test]
        public void Constructor_Throws_On_Empty_PlayerId()
        {
            var hub = new CapturingEventHub();
            Assert.Throws<ArgumentException>(() =>
            {
                using var adapter = new PlayerTranscriptAdapter(hub, "");
            });
        }

        [Test]
        public void Phase_Mapping_Is_Correct()
        {
            var hub = new CapturingEventHub();
            using var adapter = new PlayerTranscriptAdapter(hub, "player-1", "TestPlayer");

            adapter.OnPlayerTranscriptionReceived("idle", TransportPhase.Idle);
            adapter.OnPlayerTranscriptionReceived("listening", TransportPhase.Listening);
            adapter.OnPlayerTranscriptionReceived("interim", TransportPhase.Interim);
            adapter.OnPlayerTranscriptionReceived("asrfinal", TransportPhase.AsrFinal);
            adapter.OnPlayerTranscriptionReceived("processedfinal", TransportPhase.ProcessedFinal);
            adapter.OnPlayerTranscriptionReceived("completed", TransportPhase.Completed);

            Assert.AreEqual(6, hub.CapturedEvents.Count);
            Assert.AreEqual(DomainPhase.Idle, hub.CapturedEvents[0].Phase);
            Assert.AreEqual(DomainPhase.Listening, hub.CapturedEvents[1].Phase);
            Assert.AreEqual(DomainPhase.Interim, hub.CapturedEvents[2].Phase);
            Assert.AreEqual(DomainPhase.AsrFinal, hub.CapturedEvents[3].Phase);
            Assert.AreEqual(DomainPhase.ProcessedFinal, hub.CapturedEvents[4].Phase);
            Assert.AreEqual(DomainPhase.Completed, hub.CapturedEvents[5].Phase);
        }

        private sealed class CapturingEventHub : IEventHub
        {
            public List<PlayerTranscriptReceived> CapturedEvents { get; } = new();

            public void Publish<TEvent>(TEvent @event) where TEvent : notnull
            {
                if (@event is PlayerTranscriptReceived ptr)
                    CapturedEvents.Add(ptr);
            }

            public SubscriptionToken Subscribe<TEvent>(IEventSubscriber<TEvent> subscriber,
                EventDeliveryPolicy deliveryPolicy = EventDeliveryPolicy.MainThread) where TEvent : notnull
                => SubscriptionToken.Create();

            public SubscriptionToken Subscribe<TEvent>(Action<TEvent> handler,
                EventDeliveryPolicy deliveryPolicy = EventDeliveryPolicy.MainThread) where TEvent : notnull
                => SubscriptionToken.Create();

            public SubscriptionToken Subscribe<TEvent>(IEventSubscriber<TEvent> subscriber, IEventFilter<TEvent> filter,
                EventDeliveryPolicy deliveryPolicy = EventDeliveryPolicy.MainThread) where TEvent : notnull
                => SubscriptionToken.Create();

            public SubscriptionToken Subscribe<TEvent>(Action<TEvent> handler, IEventFilter<TEvent> filter,
                EventDeliveryPolicy deliveryPolicy = EventDeliveryPolicy.MainThread) where TEvent : notnull
                => SubscriptionToken.Create();

            public void Unsubscribe(SubscriptionToken token) { }

            public bool TryPeriodicCleanup(float currentTimeSeconds, float cleanupIntervalSeconds = 60f) => false;
        }
    }
}
