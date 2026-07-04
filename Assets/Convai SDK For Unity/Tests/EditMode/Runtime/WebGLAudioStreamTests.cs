using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using Convai.Infrastructure.Networking.WebGL;
using LiveKit;
using NUnit.Framework;

namespace Convai.Tests.EditMode.Runtime
{
    public class WebGLAudioStreamTests
    {
        private static readonly BindingFlags s_instanceFlags =
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private Func<JSHandle, string, JSNative.JSDelegate, JSHandle, JSRef> _originalRegistrar;
        private Action<JSHandle, string, JSRef> _originalRemover;
        private Action<UnityEngine.Texture2D> _originalTextureDestroyer;
        private Action<int> _originalNativeTextureDestroyer;
        private Func<HTMLAudioElement, bool> _originalElementPlayingEvaluator;

        [SetUp]
        public void SetUp()
        {
            _originalRegistrar = HTMLElement.EventListenerRegistrar;
            _originalRemover = HTMLElement.EventListenerRemover;
            _originalTextureDestroyer = HTMLVideoElement.TextureDestroyer;
            _originalNativeTextureDestroyer = HTMLVideoElement.NativeTextureDestroyer;
            _originalElementPlayingEvaluator = WebGLAudioStream.ElementPlayingEvaluator;
        }

        [TearDown]
        public void TearDown()
        {
            HTMLElement.EventListenerRegistrar = _originalRegistrar;
            HTMLElement.EventListenerRemover = _originalRemover;
            HTMLVideoElement.TextureDestroyer = _originalTextureDestroyer;
            HTMLVideoElement.NativeTextureDestroyer = _originalNativeTextureDestroyer;
            WebGLAudioStream.ElementPlayingEvaluator = _originalElementPlayingEvaluator;
            HTMLElement.ResetEventListenerHooks();
            HTMLVideoElement.ResetTestHooks();
            WebGLAudioStream.ResetTestHooks();
        }

        [Test]
        public void ShouldInvokePlaybackStartedImmediately_WhenTrackingWasJustInitialized_ReturnsFalse()
        {
            Assert.IsFalse(WebGLAudioStream.ShouldInvokePlaybackStartedImmediately(false, 1));
        }

        [Test]
        public void ShouldInvokePlaybackStartedImmediately_WhenTrackingWasAlreadyInitializedAndPlaying_ReturnsTrue()
        {
            Assert.IsTrue(WebGLAudioStream.ShouldInvokePlaybackStartedImmediately(true, 1));
        }

        [Test]
        public void PlaybackStarted_WhenLateSubscriberAddedWhileAlreadyPlaying_InvokesOnce()
        {
            WebGLAudioStream stream = CreateUninitializedStream();
            SetField(stream, "_playbackTrackingInitialized", true);
            GetPlayingElementHandles(stream).Add((IntPtr)101);

            int invokeCount = 0;

            stream.PlaybackStarted += () => invokeCount++;

            Assert.AreEqual(1, invokeCount);
        }

        [Test]
        public void RegisterAndUnregisterElement_RemovesAllEventListeners()
        {
            List<string> addedEvents = new();
            List<(string eventName, JSRef listenerRef)> removedEvents = new();
            Dictionary<string, JSRef> createdRefs = new();

            HTMLElement.EventListenerRegistrar = (_, eventName, _, _) =>
            {
                JSRef listenerRef = CreateListenerRef();
                addedEvents.Add(eventName);
                createdRefs[eventName] = listenerRef;
                return listenerRef;
            };

            HTMLElement.EventListenerRemover = (_, eventName, listenerRef) =>
            {
                removedEvents.Add((eventName, listenerRef));
            };

            WebGLAudioStream.ElementPlayingEvaluator = _ => false;

            WebGLAudioStream stream = CreateUninitializedStream();
            HTMLAudioElement audioElement = CreateAudioElement((IntPtr)201);

            InvokePrivate(stream, "RegisterAttachedElement", audioElement);
            InvokePrivate(stream, "UnregisterElement", (IntPtr)201);

            CollectionAssert.AreEquivalent(new[] { "playing", "pause", "ended" }, addedEvents);
            Assert.AreEqual(3, removedEvents.Count);
            CollectionAssert.AreEquivalent(new[] { "playing", "pause", "ended" }, removedEvents.ConvertAll(x => x.eventName));
            Assert.AreSame(createdRefs["playing"], removedEvents.Find(x => x.eventName == "playing").listenerRef);
            Assert.AreSame(createdRefs["pause"], removedEvents.Find(x => x.eventName == "pause").listenerRef);
            Assert.AreSame(createdRefs["ended"], removedEvents.Find(x => x.eventName == "ended").listenerRef);
        }

        [Test]
        public void RepeatedRegisterAndUnregister_DoesNotAccumulateListenerRemovals()
        {
            int addCount = 0;
            int removeCount = 0;

            HTMLElement.EventListenerRegistrar = (_, _, _, _) =>
            {
                addCount++;
                return CreateListenerRef();
            };

            HTMLElement.EventListenerRemover = (_, _, _) => removeCount++;
            WebGLAudioStream.ElementPlayingEvaluator = _ => false;

            WebGLAudioStream stream = CreateUninitializedStream();
            HTMLAudioElement audioElement = CreateAudioElement((IntPtr)301);

            InvokePrivate(stream, "RegisterAttachedElement", audioElement);
            InvokePrivate(stream, "UnregisterElement", (IntPtr)301);
            InvokePrivate(stream, "RegisterAttachedElement", audioElement);
            InvokePrivate(stream, "UnregisterElement", (IntPtr)301);

            Assert.AreEqual(6, addCount);
            Assert.AreEqual(6, removeCount);
        }

        [Test]
        public void HTMLVideoElement_Dispose_RemovesResizeListener()
        {
            JSRef resizeListenerRef = CreateListenerRef();
            List<(string eventName, JSRef listenerRef)> removedEvents = new();

            HTMLElement.EventListenerRemover = (_, eventName, listenerRef) =>
            {
                removedEvents.Add((eventName, listenerRef));
            };
            HTMLVideoElement.TextureDestroyer = _ => { };
            HTMLVideoElement.NativeTextureDestroyer = _ => { };

            HTMLVideoElement element = CreateVideoElement((IntPtr)401);
            SetField(element, "_resizeListenerRef", resizeListenerRef);
            SetField(element, "m_TextureId", 0);

            InvokeDispose(element);

            Assert.AreEqual(1, removedEvents.Count);
            Assert.AreEqual("resize", removedEvents[0].eventName);
            Assert.AreSame(resizeListenerRef, removedEvents[0].listenerRef);
        }

        private static WebGLAudioStream CreateUninitializedStream()
        {
            var stream = (WebGLAudioStream)FormatterServices.GetUninitializedObject(typeof(WebGLAudioStream));
            SetField(stream, "_registeredElements",
                Activator.CreateInstance(GetFieldInfo(typeof(WebGLAudioStream), "_registeredElements").FieldType));
            SetField(stream, "_playingElementHandles", new HashSet<IntPtr>());
            SetField(stream, "_disposed", false);
            SetField(stream, "_playbackTrackingInitialized", false);
            return stream;
        }

        private static HTMLAudioElement CreateAudioElement(IntPtr handleValue)
        {
            var element = (HTMLAudioElement)FormatterServices.GetUninitializedObject(typeof(HTMLAudioElement));
            SetNativeHandle(element, handleValue);
            return element;
        }

        private static HTMLVideoElement CreateVideoElement(IntPtr handleValue)
        {
            var element = (HTMLVideoElement)FormatterServices.GetUninitializedObject(typeof(HTMLVideoElement));
            SetNativeHandle(element, handleValue);
            return element;
        }

        private static void SetNativeHandle(object target, IntPtr handleValue)
        {
            SetField(target, "NativeHandle", new JSHandle(handleValue, false), typeof(JSRef));
        }

        private static HashSet<IntPtr> GetPlayingElementHandles(WebGLAudioStream stream) =>
            (HashSet<IntPtr>)GetFieldInfo(typeof(WebGLAudioStream), "_playingElementHandles").GetValue(stream);

        private static JSRef CreateListenerRef() =>
            (JSRef)FormatterServices.GetUninitializedObject(typeof(JSRef));

        private static void InvokeDispose(HTMLVideoElement element)
        {
            MethodInfo disposeMethod = typeof(HTMLVideoElement).GetMethod("Dispose", s_instanceFlags);
            disposeMethod.Invoke(element, new object[] { true });
        }

        private static void InvokePrivate(object target, string methodName, params object[] args)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, s_instanceFlags);
            Assert.NotNull(method, $"Could not find method '{methodName}'.");
            method.Invoke(target, args);
        }

        private static void SetField(object target, string fieldName, object value, Type declaringType = null)
        {
            GetFieldInfo(declaringType ?? target.GetType(), fieldName).SetValue(target, value);
        }

        private static FieldInfo GetFieldInfo(Type type, string fieldName)
        {
            FieldInfo field = type.GetField(fieldName, s_instanceFlags);
            Assert.NotNull(field, $"Could not find field '{fieldName}' on '{type.Name}'.");
            return field;
        }
    }
}
