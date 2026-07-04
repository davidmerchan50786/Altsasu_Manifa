using Convai.Infrastructure.Networking.Native;
using Convai.Infrastructure.Networking.Transport;
using Convai.Infrastructure.Networking.WebGL;
using NUnit.Framework;

namespace Convai.Tests.EditMode.Release
{
    [Category("Release")]
    public sealed class PlatformSmokeTests
    {
        [Test]
        public void DesktopCapabilities_AreNativeDesktop_InEditor()
        {
            // Phase 3.8: Use ITransportProvider singleton instead of constructor
            var provider = NativeTransportProvider.Instance;
            TransportCapabilities capabilities = provider.Capabilities;
            Assert.AreEqual(TransportPlatform.Desktop, capabilities.Platform);
            Assert.IsTrue(capabilities.SupportsVideo);
            Assert.IsFalse(capabilities.RequiresUserGestureForAudio);
        }

        [Test]
        public void WebGLTransportAccessor_ExposesWebGLCapabilities()
        {
            using var accessor = new WebGLRealtimeTransportAccessor(createTransport: static () => null);

            Assert.AreEqual(TransportPlatform.WebGL, accessor.Capabilities.Platform);
            Assert.IsTrue(accessor.Capabilities.SupportsVideo);
            Assert.IsTrue(accessor.Capabilities.RequiresUserGestureForAudio);
            Assert.IsFalse(accessor.Capabilities.SupportsUnityAudioSource);
        }

        [Test]
        public void WebGLRoomControllerFactory_IsConstructible_WithTransportDelegate()
        {
            using var factory = new WebGLRoomControllerFactory(createTransport: static () => null);
            Assert.IsNotNull(factory);
        }

        [Test]
        public void NativeTransportProvider_ExposesNativeCapabilities()
        {
            var provider = NativeTransportProvider.Instance;
            Assert.AreEqual(TransportPlatform.Desktop, provider.Capabilities.Platform);
            Assert.IsTrue(provider.Capabilities.SupportsVideo);
            Assert.IsFalse(provider.Capabilities.RequiresUserGestureForAudio);
        }

        [Test]
        public void WebGLTransportProvider_ExposesWebGLCapabilities()
        {
            var provider = WebGLTransportProvider.Instance;
            Assert.AreEqual(TransportPlatform.WebGL, provider.Capabilities.Platform);
            Assert.IsTrue(provider.Capabilities.SupportsVideo);
            Assert.IsTrue(provider.Capabilities.RequiresUserGestureForAudio);
            Assert.IsFalse(provider.Capabilities.SupportsUnityAudioSource);
        }
    }
}
