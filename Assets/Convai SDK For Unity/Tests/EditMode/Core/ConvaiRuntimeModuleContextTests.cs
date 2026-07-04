using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Runtime.Core;
using Convai.Runtime.Core.Modules;
using Convai.Runtime.Core.Registry;
using NUnit.Framework;
using ILogger = Convai.Domain.Logging.ILogger;

namespace Convai.Tests.EditMode.Core
{
    [Category("Phase3")]
    public sealed class ConvaiRuntimeModuleContextTests
    {
        [Test]
        public async Task ModuleContext_ProvideModuleService_MakesServiceAvailableToDependentModules()
        {
            var provider = new ModuleServiceProviderModule();
            var consumer = new ModuleServiceConsumerModule();

            ConvaiRuntime runtime = new ConvaiRuntimeBuilder()
                .UseEventHub(new EventHub(new ImmediateScheduler(), new TestLogger()))
                .UseAgentRegistry(new AgentRegistry())
                .AddModule(provider)
                .AddModule(consumer)
                .Build();

            await runtime.StartAsync();

            Assert.That(consumer.ObservedService, Is.Not.Null);
            Assert.That(consumer.ObservedService.Value, Is.EqualTo("module-service"));
        }

        private sealed class ImmediateScheduler : IUnityScheduler
        {
            public void ScheduleOnMainThread(Action action) => action?.Invoke();
            public void ScheduleOnBackground(Action action) => action?.Invoke();
            public bool IsMainThread() => true;
        }

        private sealed class TestLogger : ILogger
        {
            public bool IsEnabled(LogLevel level, LogCategory category) => false;
            public void Log(LogLevel level, string message, LogCategory category = LogCategory.SDK) { }
            public void Log(LogLevel level, string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK)
            { }
            public void Debug(string message, LogCategory category = LogCategory.SDK) { }
            public void Debug(string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK)
            { }
            public void Info(string message, LogCategory category = LogCategory.SDK) { }
            public void Info(string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK)
            { }
            public void Warning(string message, LogCategory category = LogCategory.SDK) { }
            public void Warning(string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK)
            { }
            public void Error(string message, LogCategory category = LogCategory.SDK) { }
            public void Error(string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK)
            { }
            public void Error(Exception exception, string message, LogCategory category = LogCategory.SDK) { }
            public void Error(Exception exception, string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK)
            { }
        }

        [Test]
        public void IModuleContext_Does_Not_Expose_Legacy_Generic_Service_Methods()
        {
            string[] methodNames = typeof(IModuleContext)
                .GetMethods()
                .Select(m => m.Name)
                .ToArray();

            Assert.That(methodNames, Does.Contain("TryGetModuleService"));
            Assert.That(methodNames, Does.Contain("ProvideModuleService"));
            Assert.That(methodNames, Does.Not.Contain("GetService"));
            Assert.That(methodNames, Does.Not.Contain("TryGetService"));
            Assert.That(methodNames, Does.Not.Contain("RegisterService"));
        }

        private sealed class ModuleServiceMarker
        {
            public ModuleServiceMarker(string value) => Value = value;

            public string Value { get; }
        }

        private sealed class ModuleServiceProviderModule : IConvaiModule
        {
            public string ModuleId => "provider";
            public string DisplayName => "provider";
            public IReadOnlyList<string> RequiredModules => Array.Empty<string>();
            public IReadOnlyList<Type> RequiredServices => Array.Empty<Type>();
            public IReadOnlyList<Type> ProvidedServices =>
                new[] { typeof(ModuleServiceMarker) };
            public bool IsActive => true;

            public ValueTask RegisterAsync(IModuleContext context, System.Threading.CancellationToken ct = default)
            {
                context.ProvideModuleService(new ModuleServiceMarker("module-service"));
                return default;
            }

            public ValueTask StartAsync(IModuleContext context, System.Threading.CancellationToken ct = default) => default;
            public ValueTask PauseAsync(RuntimePauseReason reason, System.Threading.CancellationToken ct = default) => default;
            public ValueTask ResumeAsync(System.Threading.CancellationToken ct = default) => default;
            public ValueTask StopAsync(System.Threading.CancellationToken ct = default) => default;
        }

        private sealed class ModuleServiceConsumerModule : IConvaiModule
        {
            public ModuleServiceMarker ObservedService { get; private set; }

            public string ModuleId => "consumer";
            public string DisplayName => "consumer";
            public IReadOnlyList<string> RequiredModules => new[] { "provider" };
            public IReadOnlyList<Type> RequiredServices => Array.Empty<Type>();
            public IReadOnlyList<Type> ProvidedServices => Array.Empty<Type>();
            public bool IsActive => true;

            public ValueTask RegisterAsync(IModuleContext context, System.Threading.CancellationToken ct = default) => default;

            public ValueTask StartAsync(IModuleContext context, System.Threading.CancellationToken ct = default)
            {
                context.TryGetModuleService(out ModuleServiceMarker service);
                ObservedService = service;
                return default;
            }

            public ValueTask PauseAsync(RuntimePauseReason reason, System.Threading.CancellationToken ct = default) => default;
            public ValueTask ResumeAsync(System.Threading.CancellationToken ct = default) => default;
            public ValueTask StopAsync(System.Threading.CancellationToken ct = default) => default;
        }
    }
}
