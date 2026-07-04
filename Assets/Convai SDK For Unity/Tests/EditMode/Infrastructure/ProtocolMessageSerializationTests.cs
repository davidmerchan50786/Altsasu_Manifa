using System.Collections.Generic;
using Convai.Infrastructure.Protocol.Messages;
using Convai.Tests.EditMode.Mocks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Convai.Tests.EditMode.Infrastructure
{
    [TestFixture]
    public class ProtocolMessageSerializationTests
    {
        [Test]
        public void Serialize_RTVITriggerMessage_ContainsExpectedKeys()
        {
            var message = new RTVITriggerMessage("wake_up", "hello");
            JObject obj = JObject.Parse(JsonConvert.SerializeObject(message));

            Assert.AreEqual("trigger-message", obj["type"]?.ToString());
            Assert.AreEqual("rtvi-ai", obj["label"]?.ToString());
            Assert.IsFalse(string.IsNullOrEmpty(obj["id"]?.ToString()));
            Assert.AreEqual("wake_up", obj["data"]?["trigger_name"]?.ToString());
            Assert.AreEqual("hello", obj["data"]?["trigger_message"]?.ToString());
        }

        [Test]
        public void Serialize_RTVIUpdateTemplateKeys_ContainsExpectedKeys()
        {
            var message = new RTVIUpdateTemplateKeys(
                new Dictionary<string, string> { { "foo", "bar" } });
            JObject obj = JObject.Parse(JsonConvert.SerializeObject(message));

            Assert.AreEqual("update-template-keys", obj["type"]?.ToString());
            Assert.AreEqual("bar", obj["data"]?["template_keys"]?["foo"]?.ToString());
        }

        [Test]
        public void Serialize_RTVIUpdateSceneMetadata_ContainsExpectedKeys()
        {
            var message = new RTVIUpdateSceneMetadata(
                new List<SceneMetadata> { new() { Name = "Town", Description = "Center square" } });
            JObject obj = JObject.Parse(JsonConvert.SerializeObject(message));

            Assert.AreEqual("update-scene-metadata", obj["type"]?.ToString());
            Assert.AreEqual("Town", obj["data"]?[0]?["name"]?.ToString());
            Assert.AreEqual("Center square", obj["data"]?[0]?["description"]?.ToString());
        }

        [Test]
        public void Serialize_RTVIUpdateDynamicContext_ContainsExpectedKeys()
        {
            var message = new RTVIUpdateDynamicContext("The player is in the town square.");
            JObject obj = JObject.Parse(JsonConvert.SerializeObject(message));

            Assert.AreEqual("context-update", obj["type"]?.ToString());
            Assert.AreEqual("rtvi-ai", obj["label"]?.ToString());
            Assert.IsFalse(string.IsNullOrEmpty(obj["id"]?.ToString()));
            var data = obj["data"];
            Assert.NotNull(data);
            Assert.AreEqual("The player is in the town square.", data["text"]?.ToString());
            Assert.AreEqual("append", data["mode"]?.ToString());
            Assert.AreEqual("auto", data["run_llm"]?.ToString());
        }

        [Test]
        public void Serialize_RTVIUpdateDynamicContext_ReplaceMode_SerializesCorrectly()
        {
            var message = new RTVIUpdateDynamicContext("New full context.", "replace", "true");
            JObject obj = JObject.Parse(JsonConvert.SerializeObject(message));

            Assert.AreEqual("replace", obj["data"]?["mode"]?.ToString());
            Assert.AreEqual("true", obj["data"]?["run_llm"]?.ToString());
        }

        [Test]
        public void Serialize_RTVIUpdateDynamicContext_ResetMode_SerializesCorrectly()
        {
            var message = new RTVIUpdateDynamicContext(null, "reset", "false");
            JObject obj = JObject.Parse(JsonConvert.SerializeObject(message));

            Assert.AreEqual("reset", obj["data"]?["mode"]?.ToString());
            Assert.AreEqual("false", obj["data"]?["run_llm"]?.ToString());
        }

        [Test]
        public void Serialize_RTVIResetIdleTimer_ContainsExpectedKeys()
        {
            var message = new RTVIResetIdleTimer();
            JObject obj = JObject.Parse(JsonConvert.SerializeObject(message));

            Assert.AreEqual("reset-idle-timer", obj["type"]?.ToString());
            Assert.AreEqual("rtvi-ai", obj["label"]?.ToString());
            Assert.NotNull(obj["data"]);
        }

        [Test]
        public void Serialize_RTVITtsToggle_ContainsExpectedKeys()
        {
            var message = new RTVITtsToggle(true);
            JObject obj = JObject.Parse(JsonConvert.SerializeObject(message));

            Assert.AreEqual("tts-toggle", obj["type"]?.ToString());
            Assert.AreEqual("True", obj["data"]?["enabled"]?.ToString());
        }

        [Test]
        public void Serialize_RTVISttToggle_ContainsExpectedKeys()
        {
            var message = new RTVISttToggle(true);
            JObject obj = JObject.Parse(JsonConvert.SerializeObject(message));

            Assert.AreEqual("stt-toggle", obj["type"]?.ToString());
            Assert.AreEqual("True", obj["data"]?["muted"]?.ToString());
        }

        [Test]
        public void Serialize_RTVIInterruptBot_ContainsExpectedKeys()
        {
            var message = new RTVIInterruptBot();
            JObject obj = JObject.Parse(JsonConvert.SerializeObject(message));

            Assert.AreEqual("interrupt-bot", obj["type"]?.ToString());
            Assert.NotNull(obj["data"]);
        }

        [Test]
        public void Serialize_RTVIKillPipeline_ContainsExpectedKeys()
        {
            var message = new RTVIKillPipeline();
            JObject obj = JObject.Parse(JsonConvert.SerializeObject(message));

            Assert.AreEqual("kill-pipeline", obj["type"]?.ToString());
            Assert.NotNull(obj["data"]);
        }

        [Test]
        public void Serialize_RTVIForceUserStoppedSpeaking_ContainsExpectedKeys()
        {
            var message = new RTVIForceUserStoppedSpeaking();
            JObject obj = JObject.Parse(JsonConvert.SerializeObject(message));

            Assert.AreEqual("force-user-stopped-speaking", obj["type"]?.ToString());
            Assert.NotNull(obj["data"]);
        }

        [Test]
        public void MockRoomConnectionService_UpdateDynamicContext_RecordsCallsWithCorrectParameters()
        {
            var mock = new MockRoomConnectionService();
            mock.RaiseConnected();

            Assert.IsTrue(mock.UpdateDynamicContext("First line of context."));
            Assert.IsTrue(mock.UpdateDynamicContext("Replace everything.", "replace", "false"));
            Assert.IsTrue(mock.UpdateDynamicContext(null, "reset"));

            Assert.AreEqual(3, mock.SentDynamicContextUpdates.Count);
            Assert.AreEqual("First line of context.", mock.SentDynamicContextUpdates[0].Text);
            Assert.AreEqual("append", mock.SentDynamicContextUpdates[0].Mode);
            Assert.AreEqual("auto", mock.SentDynamicContextUpdates[0].RunLlm);
            Assert.AreEqual("Replace everything.", mock.SentDynamicContextUpdates[1].Text);
            Assert.AreEqual("replace", mock.SentDynamicContextUpdates[1].Mode);
            Assert.AreEqual("false", mock.SentDynamicContextUpdates[1].RunLlm);
            Assert.IsNull(mock.SentDynamicContextUpdates[2].Text);
            Assert.AreEqual("reset", mock.SentDynamicContextUpdates[2].Mode);
            Assert.AreEqual("auto", mock.SentDynamicContextUpdates[2].RunLlm);
        }

        [Test]
        public void Serialize_AnyOutboundMessage_ContainsEnvelopeKeys()
        {
            var message = new RTVIUserTextMessage("hello");
            JObject obj = JObject.Parse(JsonConvert.SerializeObject(message));

            Assert.AreEqual("rtvi-ai", obj["label"]?.ToString());
            Assert.AreEqual("user_text_message", obj["type"]?.ToString());
            Assert.IsFalse(string.IsNullOrEmpty(obj["id"]?.ToString()));
            Assert.NotNull(obj["data"]);
        }
    }
}
