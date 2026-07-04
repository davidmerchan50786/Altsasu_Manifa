using System;
using LiveKit;
using LiveKit.Internal.FFIClients;
using LiveKit.Proto;
using NUnit.Framework;

namespace Convai.Tests.EditMode.Runtime
{
    public class LiveKitAudioProcessingTests
    {
        [Test]
        public void AudioBuffer_ReadDuration_ReturnsNullUntilEnoughTenMillisecondSamplesExist()
        {
            using var buffer = new AudioBuffer();

            buffer.Write(new float[200], 2, 48000);
            using AudioFrame insufficient = buffer.ReadDuration(AudioProcessingModule.FrameDurationMs);
            Assert.That(insufficient, Is.Null);

            buffer.Write(new float[960], 2, 48000);
            using AudioFrame frame = buffer.ReadDuration(AudioProcessingModule.FrameDurationMs);

            Assert.That(frame, Is.Not.Null);
            Assert.That(frame.NumChannels, Is.EqualTo(2));
            Assert.That(frame.SampleRate, Is.EqualTo(48000));
            Assert.That(frame.SamplesPerChannel, Is.EqualTo(480));
        }

        [Test]
        public void AudioBuffer_Clear_DropsBufferedSamples()
        {
            using var buffer = new AudioBuffer();

            buffer.Write(new float[960], 2, 48000);
            buffer.Clear();

            using AudioFrame frame = buffer.ReadDuration(AudioProcessingModule.FrameDurationMs);
            Assert.That(frame, Is.Null);
        }

        [Test]
        public void FfiRequestExtensions_Inject_ApmRequests_SetsExpectedOneOfField()
        {
            var newApmRequest = new FfiRequest();
            newApmRequest.Inject(new NewApmRequest());
            Assert.That(newApmRequest.NewApm, Is.Not.Null);

            var processRequest = new FfiRequest();
            processRequest.Inject(new ApmProcessStreamRequest());
            Assert.That(processRequest.ApmProcessStream, Is.Not.Null);

            var reverseRequest = new FfiRequest();
            reverseRequest.Inject(new ApmProcessReverseStreamRequest());
            Assert.That(reverseRequest.ApmProcessReverseStream, Is.Not.Null);

            var delayRequest = new FfiRequest();
            delayRequest.Inject(new ApmSetStreamDelayRequest());
            Assert.That(delayRequest.ApmSetStreamDelay, Is.Not.Null);
        }

        [Test]
        public void FfiRequestExtensions_EnsureClean_ThrowsWhenApmFieldsArePresent()
        {
            var request = new FfiRequest { NewApm = new NewApmRequest() };
            Assert.Throws<InvalidOperationException>(() => request.EnsureClean());

            var response = new FfiResponse { ApmSetStreamDelay = new ApmSetStreamDelayResponse() };
            Assert.Throws<InvalidOperationException>(() => response.EnsureClean());
        }
    }
}
