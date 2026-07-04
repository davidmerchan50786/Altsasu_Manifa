using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Convai.Tests.EditMode.Architecture
{
    /// <summary>
    ///     Architectural guardrails for API exposure, namespace conventions, and error-code ownership.
    /// </summary>
    public class ArchitectureGuardTests
    {
        private static readonly Dictionary<string, string> ArchitecturePrefixes = new(StringComparer.Ordinal)
        {
            ["Domain"] = "Convai.Domain",
            ["Application"] = "Convai.Application",
            ["Shared"] = "Convai.Shared",
            ["Infrastructure"] = "Convai.Infrastructure",
            ["Runtime"] = "Convai.Runtime",
            ["Modules"] = "Convai.Modules",
            ["Editor"] = "Convai.Editor"
        };

        private static string PackageRoot => Path.GetFullPath(Path.Combine(
            UnityEngine.Application.dataPath,
            "..",
            "Packages",
            "com.convai.convai-sdk-for-unity"));

        private static string SdkRoot => Path.Combine(PackageRoot, "SDK");

        private static string ToRelativePath(string fullPath) =>
            Path.GetRelativePath(PackageRoot, fullPath).Replace('\\', '/');

        private static readonly Regex MetaGuidPattern = new(
            @"^guid:\s*([0-9a-f]{32})\s*$",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);

        private static readonly Regex ReferenceGuidPattern = new(
            @"guid:\s*([0-9a-f]{32})",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);

        private static readonly string[] SerializedAssetExtensions =
        {
            ".unity", ".prefab", ".asset", ".mat", ".controller", ".anim", ".overrideController"
        };

        private static readonly string[] ApprovedInternalReadmes =
        {
            "SDK/README.md",
            "SDK/Infrastructure/Networking/README.md",
            "SDK/Modules/Vision/README.md",
            "SDK/Modules/LipSync/README.md",
            "SDK/Modules/Narrative/README.md"
        };

        private static Dictionary<string, string> BuildGuidPathMap()
        {
            return Directory
                .EnumerateFiles(PackageRoot, "*.meta", SearchOption.AllDirectories)
                .Select(path => new { Path = path, Match = MetaGuidPattern.Match(File.ReadAllText(path)) })
                .Where(entry => entry.Match.Success)
                .ToDictionary(
                    entry => entry.Match.Groups[1].Value,
                    entry => Path.ChangeExtension(entry.Path, null),
                    StringComparer.Ordinal);
        }

        private static IEnumerable<string> EnumerateSerializedAssetFiles(string rootPath)
        {
            return Directory
                .EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
                .Where(path => SerializedAssetExtensions.Contains(Path.GetExtension(path),
                    StringComparer.OrdinalIgnoreCase));
        }

        private static IEnumerable<string> GetResolvedAssetReferences(string assetPath,
            IReadOnlyDictionary<string, string> guidPathMap)
        {
            return ReferenceGuidPattern
                .Matches(File.ReadAllText(assetPath))
                .Select(match => match.Groups[1].Value)
                .Distinct(StringComparer.Ordinal)
                .Select(guid => guidPathMap.TryGetValue(guid, out string resolvedPath) ? ToRelativePath(resolvedPath) : null)
                .Where(path => !string.IsNullOrEmpty(path));
        }

        private static Assembly FindOrLoadAssembly(string name)
        {
            Assembly loaded = AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, name, StringComparison.Ordinal));

            if (loaded != null) return loaded;

            try
            {
                return Assembly.Load(name);
            }
            catch
            {
                return null;
            }
        }

        private static Type FindType(string fullName, params string[] preferredAssemblies)
        {
            if (preferredAssemblies != null)
            {
                foreach (string assemblyName in preferredAssemblies)
                {
                    Assembly assembly = FindOrLoadAssembly(assemblyName);
                    Type preferredType = assembly?.GetType(fullName, false);
                    if (preferredType != null) return preferredType;
                }
            }

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null) return type;
            }

            return null;
        }

        private static string FormatViolations(string header, IReadOnlyList<string> violations)
        {
            const int maxToPrint = 20;
            var sb = new StringBuilder();
            sb.AppendLine(header);

            for (int i = 0; i < violations.Count && i < maxToPrint; i++) sb.AppendLine($"- {violations[i]}");

            if (violations.Count > maxToPrint) sb.AppendLine($"... and {violations.Count - maxToPrint} more");

            return sb.ToString();
        }

        [Test]
        [Category("Architecture")]
        public void Namespaces_Use_ArchitectureLayer_Prefixes()
        {
            Assert.IsTrue(Directory.Exists(SdkRoot), $"SDK root not found: {SdkRoot}");

            var violations = new List<string>();
            var namespacePattern = new Regex(@"^\s*namespace\s+([A-Za-z_][A-Za-z0-9_.]*)", RegexOptions.Multiline);

            foreach (string filePath in Directory.EnumerateFiles(SdkRoot, "*.cs", SearchOption.AllDirectories))
            {
                if (string.Equals(Path.GetFileName(filePath), "AssemblyInfo.cs", StringComparison.Ordinal)) continue;

                string relativeToSdk = Path.GetRelativePath(SdkRoot, filePath).Replace('\\', '/');
                string rootFolder = relativeToSdk.Split('/')[0];

                if (!ArchitecturePrefixes.TryGetValue(rootFolder, out string expectedPrefix)) continue;

                string source = File.ReadAllText(filePath);
                Match nsMatch = namespacePattern.Match(source);
                if (!nsMatch.Success) continue;

                string declaredNamespace = nsMatch.Groups[1].Value;
                bool valid = declaredNamespace.Equals(expectedPrefix, StringComparison.Ordinal) ||
                             declaredNamespace.StartsWith(expectedPrefix + ".", StringComparison.Ordinal);

                if (!valid)
                    violations.Add(
                        $"{ToRelativePath(filePath)} declares '{declaredNamespace}' (expected prefix '{expectedPrefix}')");
            }

            Assert.IsEmpty(violations, FormatViolations("Architecture namespace prefix violations:", violations));
        }

        [Test]
        [Category("Architecture")]
        public void Domain_Layer_Must_Not_Reference_Outer_Layers()
        {
            Assembly domainAssembly = FindOrLoadAssembly("Convai.Domain");
            if (domainAssembly == null)
            {
                Assert.Inconclusive("Convai.Domain assembly is not loaded.");
                return;
            }

            string[] forbidden =
            {
                "Convai.Application", "Convai.Infrastructure", "Convai.Infrastructure.Networking",
                "Convai.Infrastructure.Protocol", "Convai.Runtime", "Convai.Runtime.Behaviors",
                "Convai.Modules.Vision", "Convai.Modules.Narrative", "Convai.Editor"
            };

            HashSet<string> referencedNames = domainAssembly
                .GetReferencedAssemblies()
                .Select(a => a.Name)
                .ToHashSet(StringComparer.Ordinal);

            List<string> violations = forbidden
                .Where(referencedNames.Contains)
                .ToList();

            Assert.IsEmpty(violations,
                $"Convai.Domain has forbidden outer-layer dependencies: [{string.Join(", ", violations)}]");
        }

        [Test]
        [Category("Architecture")]
        public void Canonical_Errors_Are_Single_Source_Of_Truth()
        {
            Type canonicalErrorCodesType = FindType(
                "Convai.Domain.Errors.SessionErrorCodes",
                "Convai.Domain");

            Assert.NotNull(canonicalErrorCodesType, "Could not locate SessionErrorCodes.");

            FieldInfo[] canonicalFields = canonicalErrorCodesType.GetFields(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance |
                BindingFlags.DeclaredOnly);

            List<string> localConstStringFields = canonicalFields
                .Where(f => f.FieldType == typeof(string) && f.IsLiteral && !f.IsInitOnly)
                .Select(f => f.Name)
                .ToList();

            Assert.IsNotEmpty(localConstStringFields,
                "SessionErrorCodes should expose canonical string constants.");

            string infraNetworkingRoot = Path.Combine(SdkRoot, "Infrastructure", "Networking");
            Assert.IsTrue(Directory.Exists(infraNetworkingRoot),
                $"Infrastructure networking path not found: {infraNetworkingRoot}");

            var canonicalCodeConstPattern = new Regex(
                @"const\s+string\s+\w+\s*=\s*""([a-z][a-z0-9_]*\.[a-z0-9_.]+)""",
                RegexOptions.Multiline);

            var infraViolations = new List<string>();

            foreach (string filePath in Directory.EnumerateFiles(infraNetworkingRoot, "*.cs",
                         SearchOption.AllDirectories))
            {
                string source = File.ReadAllText(filePath);
                MatchCollection matches = canonicalCodeConstPattern.Matches(source);

                foreach (Match match in matches)
                    if (match.Success)
                        infraViolations.Add($"{ToRelativePath(filePath)} => \"{match.Groups[1].Value}\"");
            }

            Assert.IsEmpty(infraViolations,
                FormatViolations("Infrastructure networking declares canonical-style const error codes:",
                    infraViolations));
        }

        [Test]
        [Category("Architecture")]
        public void LegacyCompatibilityAndLocatorTypes_Are_Removed()
        {
            Type serviceLocator = FindType("Convai.Shared.DependencyInjection.ConvaiServiceLocator");
            Type roomSession = FindType("Convai.Application.ConvaiRoomSession");
            Type transcriptUIServicesInterface = FindType("Convai.Runtime.Presentation.Services.ITranscriptUIServices");
            Type transcriptBroadcaster = FindType("Convai.Infrastructure.Networking.ITranscriptBroadcaster");
            Type compatibilityProvider = FindType("Convai.Runtime.Adapters.Networking.ConvaiRoomManager+CompatibilitySceneOwnershipProvider");

            Assert.IsNull(serviceLocator, "ConvaiServiceLocator should not exist. Use typed dependencies and manager-owned runtime composition.");
            Assert.IsNull(roomSession, "ConvaiRoomSession should not exist. Room connection logic lives in ConvaiRoomManager.");
            Assert.IsNull(transcriptUIServicesInterface, "ITranscriptUIServices should not exist.");
            Assert.IsNull(transcriptBroadcaster, "ITranscriptBroadcaster should not exist.");
            Assert.IsNull(compatibilityProvider, "CompatibilitySceneOwnershipProvider should not exist.");

            Type managerType = FindType("Convai.Runtime.Components.ConvaiManager");
            if (managerType != null)
            {
                PropertyInfo instanceProp = managerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                Assert.IsNull(instanceProp, "ConvaiManager.Instance singleton should not exist.");
            }

            Type roomManagerType = FindType("Convai.Runtime.Adapters.Networking.ConvaiRoomManager");
            if (roomManagerType != null)
            {
                MethodInfo injectMethod = roomManagerType.GetMethod("Inject", BindingFlags.Public | BindingFlags.Instance);
                Assert.IsNull(injectMethod, "ConvaiRoomManager.Inject should not exist.");
            }

            Type bootstrapType = FindType("Convai.Runtime.ConvaiServiceBootstrap");
            if (bootstrapType != null)
            {
                MethodInfo manualBootstrap = bootstrapType.GetMethod("BootstrapManually",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                MethodInfo manualShutdown = bootstrapType.GetMethod("ShutdownManually",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

                Assert.IsNull(manualBootstrap, "ConvaiServiceBootstrap.BootstrapManually should not exist.");
                Assert.IsNull(manualShutdown, "ConvaiServiceBootstrap.ShutdownManually should not exist.");
            }

            Type transcriptControllerType = FindType("Convai.Runtime.Presentation.Services.TranscriptUIController");
            if (transcriptControllerType != null)
            {
                MethodInfo discoverListeners = transcriptControllerType.GetMethod("DiscoverListenersInScene",
                    BindingFlags.Public | BindingFlags.Instance);
                Assert.IsNull(discoverListeners, "TranscriptUIController.DiscoverListenersInScene should not exist.");
            }
        }

        [Test]
        [Category("Architecture")]
        public void LegacyConnectionStrategyLayer_Is_Removed()
        {
            Type strategyInterface = FindType(
                "Convai.Runtime.Strategies.IConvaiConnectionStrategy",
                "Convai.Runtime");
            Type strategyImplementation = FindType(
                "Convai.Runtime.Strategies.DefaultConnectionStrategy",
                "Convai.Runtime");

            Assert.IsNull(strategyInterface,
                "IConvaiConnectionStrategy should not exist once connection retry logic lives in the runtime orchestration path.");
            Assert.IsNull(strategyImplementation,
                "DefaultConnectionStrategy should not exist once connection retry logic lives in the runtime orchestration path.");

            Type legacyResumable = FindType(
                "Convai.Runtime.Strategies.IConvaiResumableConnectionStrategy",
                "Convai.Runtime");
            Type legacyReconnectable = FindType(
                "Convai.Runtime.Strategies.IConvaiReconnectableConnectionStrategy",
                "Convai.Runtime");

            Assert.IsNull(legacyResumable, "IConvaiResumableConnectionStrategy should not exist.");
            Assert.IsNull(legacyReconnectable, "IConvaiReconnectableConnectionStrategy should not exist.");
        }

        [Test]
        [Category("Architecture")]
        public void DeadContainerAndAudioArtifacts_Files_Are_Deleted()
        {
            string[] forbiddenFiles =
            {
                Path.Combine(SdkRoot, "Runtime", "Core", "DependencyInjection", "IServiceContainer.cs"),
                Path.Combine(SdkRoot, "Runtime", "Core", "DependencyInjection", "ServiceContainer.cs"),
                Path.Combine(SdkRoot, "Runtime", "Core", "DependencyInjection", "ServiceDescriptor.cs"),
                Path.Combine(SdkRoot, "Runtime", "Core", "DependencyInjection", "ServiceLifetime.cs"),
                Path.Combine(SdkRoot, "Runtime", "Components", "DefaultAudioManager.cs"),
                Path.Combine(SdkRoot, "Runtime", "Components", "IConvaiAudioManager.cs"),
                Path.Combine(SdkRoot, "Runtime", "Services", "ConvaiApplicationServiceRegistrar.cs"),
                Path.Combine(SdkRoot, "Runtime", "Core", "Providers", "ProviderServiceRegistration.cs"),
                Path.Combine(SdkRoot, "Runtime", "Presentation", "Services", "Utilities",
                    "Convai.Runtime.Presentation.Support.asmref")
            };

            List<string> violations = forbiddenFiles.Where(File.Exists).Select(ToRelativePath).ToList();

            Assert.IsEmpty(violations, FormatViolations("Dead artifact files must be deleted:", violations));
        }

        [Test]
        [Category("Architecture")]
        public void Internal_Readme_Surface_Is_Reduced_To_Approved_Files()
        {
            var readmes = Directory
                .EnumerateFiles(SdkRoot, "README.md", SearchOption.AllDirectories)
                .Select(ToRelativePath)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();

            CollectionAssert.AreEquivalent(ApprovedInternalReadmes, readmes,
                FormatViolations("Internal SDK readme surface must match the approved set:", readmes));
        }

        [Test]
        [Category("Architecture")]
        public void Kept_Internal_Readmes_Do_Not_Use_Removed_Layer_Or_Bootstrap_Wording()
        {
            string[] forbiddenTokens =
            {
                "manager-driven bootstrap pipeline",
                "runtime bootstrap pipeline",
                "DI container",
                "Application/README.md",
                "Shared/README.md"
            };

            var violations = new List<string>();

            foreach (string relativePath in ApprovedInternalReadmes)
            {
                string fullPath = Path.Combine(PackageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
                Assert.IsTrue(File.Exists(fullPath), $"Internal README not found: {relativePath}");

                string content = File.ReadAllText(fullPath);
                if (forbiddenTokens.Any(content.Contains))
                    violations.Add(relativePath);
            }

            Assert.IsEmpty(violations,
                FormatViolations("Kept internal readmes still contain removed layer/bootstrap wording:", violations));
        }

        [Test]
        [Category("Architecture")]
        public void Advanced_RuntimeBuilder_Hooks_Are_Documented_If_They_Remain_Public()
        {
            Type builderType = FindType("Convai.Runtime.Core.ConvaiRuntimeBuilder", "Convai.Runtime");
            Assert.NotNull(builderType, "ConvaiRuntimeBuilder should exist.");

            string apiEntrypointsPath = Path.Combine(PackageRoot, "Documentation~", "API-ENTRYPOINTS.md");
            Assert.IsTrue(File.Exists(apiEntrypointsPath), $"API entrypoints doc not found: {ToRelativePath(apiEntrypointsPath)}");
            string apiEntrypoints = File.ReadAllText(apiEntrypointsPath);

            string[] advancedHooks =
            {
                "WithFeatureVariants",
                "UsePersistence",
                "UseTelemetry"
            };

            var undocumented = new List<string>();
            foreach (string hook in advancedHooks)
            {
                MethodInfo method = builderType.GetMethod(hook, BindingFlags.Public | BindingFlags.Instance);
                if (method != null && !apiEntrypoints.Contains(hook, StringComparison.Ordinal))
                    undocumented.Add(hook);
            }

            Assert.IsEmpty(undocumented,
                $"Advanced ConvaiRuntimeBuilder hooks must be documented if they remain public: [{string.Join(", ", undocumented)}]");
        }

        [Test]
        [Category("Architecture")]
        public void Phase3_RoomManager_Uses_Formal_TypedInjection_And_Vision_Is_A_Module()
        {
            Type roomManagerType = FindType("Convai.Runtime.Adapters.Networking.ConvaiRoomManager", "Convai.Runtime");
            Type roomManagerDepsType =
                FindType("Convai.Runtime.Core.DependencyInjection.IConvaiRoomManagerDependencies", "Convai.Runtime");
            Type injectableOpenType = FindType("Convai.Runtime.Core.DependencyInjection.IInjectable`1", "Convai.Runtime");
            Type visionPublisherType = FindType("Convai.Modules.Vision.ConvaiVisionPublisher", "Convai.Modules.Vision");
            Type moduleType = FindType("Convai.Runtime.Core.Modules.IConvaiModule", "Convai.Runtime");

            Assert.IsNotNull(roomManagerType);
            Assert.IsNotNull(roomManagerDepsType);
            Assert.IsNotNull(injectableOpenType);
            Assert.IsNotNull(visionPublisherType);
            Assert.IsNotNull(moduleType);

            Type expectedInjectable = injectableOpenType.MakeGenericType(roomManagerDepsType);
            Assert.IsTrue(expectedInjectable.IsAssignableFrom(roomManagerType),
                "ConvaiRoomManager must formally implement IInjectable<IConvaiRoomManagerDependencies>.");
            Assert.IsTrue(moduleType.IsAssignableFrom(visionPublisherType),
                "ConvaiVisionPublisher must implement IConvaiModule.");
        }

        [Test]
        [Category("Architecture")]
        public void LegacyRoomOrchestrationLayer_Is_Removed()
        {
            Type orchestrationAdapter = FindType(
                "Convai.Runtime.Adapters.Networking.ConnectionOrchestrationAdapter",
                "Convai.Runtime");
            Type reconnectionServiceInterface = FindType(
                "Convai.Infrastructure.Networking.Services.IReconnectionService",
                "Convai.Infrastructure.Networking.Abstractions",
                "Convai.Infrastructure.Networking");
            Type reconnectionService = FindType(
                "Convai.Infrastructure.Networking.Services.ReconnectionService",
                "Convai.Infrastructure.Networking.Abstractions",
                "Convai.Infrastructure.Networking");

            Assert.IsNull(orchestrationAdapter,
                "ConnectionOrchestrationAdapter should not exist once ConvaiRoomManager owns the connection lifecycle.");
            Assert.IsNull(reconnectionServiceInterface,
                "IReconnectionService should not exist once reconnect state is represented by ConnectionContext and ReconnectPolicy.");
            Assert.IsNull(reconnectionService,
                "ReconnectionService should not exist once reconnect state is represented by ConnectionContext and ReconnectPolicy.");

            var legacyFiles = Directory
                .EnumerateFiles(PackageRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path =>
                {
                    string fileName = Path.GetFileName(path);
                    return fileName == "ConnectionOrchestrationAdapter.cs" ||
                           fileName == "IReconnectionService.cs" ||
                           fileName == "ReconnectionService.cs";
                })
                .Select(ToRelativePath)
                .ToList();

            Assert.IsEmpty(legacyFiles,
                FormatViolations("Legacy room orchestration files should be deleted:", legacyFiles));
        }

        [Test]
        [Category("Architecture")]
        public void SessionResume_Is_PerCharacter_Not_Global()
        {
            Type legacyCapability = FindType(
                "Convai.Runtime.Behaviors.ISessionResumable",
                "Convai.Runtime.Behaviors",
                "Convai.Runtime");
            Assert.IsNull(legacyCapability, "ISessionResumable should not exist.");

            Type characterAgentType = FindType(
                "Convai.Runtime.Behaviors.IConvaiCharacterAgent",
                "Convai.Runtime.Behaviors",
                "Convai.Runtime");
            Assert.NotNull(characterAgentType, "Could not locate IConvaiCharacterAgent.");

            PropertyInfo resumeProperty =
                characterAgentType.GetProperty("EnableSessionResume", BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(resumeProperty, "IConvaiCharacterAgent must expose EnableSessionResume.");
            Assert.AreEqual(typeof(bool), resumeProperty.PropertyType, "EnableSessionResume must be a bool.");

            Type settingsType = FindType(
                "Convai.Runtime.ConvaiSettings",
                "Convai.Runtime");
            Assert.NotNull(settingsType, "Could not locate ConvaiSettings.");

            PropertyInfo legacyGlobalProperty =
                settingsType.GetProperty("SessionResumeEnabled", BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNull(legacyGlobalProperty, "ConvaiSettings.SessionResumeEnabled should not exist.");

            string runtimeRoot = Path.Combine(SdkRoot, "Runtime");
            Assert.IsTrue(Directory.Exists(runtimeRoot), $"Runtime path not found: {runtimeRoot}");

            List<string> legacyReferences = Directory
                .EnumerateFiles(runtimeRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path => File.ReadAllText(path).Contains("ISessionResumable", StringComparison.Ordinal))
                .Select(ToRelativePath)
                .ToList();

            Assert.IsEmpty(legacyReferences,
                FormatViolations("Runtime still references ISessionResumable:", legacyReferences));
        }

        [Test]
        [Category("Architecture")]
        public void FutureMultiplayer_Seams_Are_Internal()
        {
            var targets = new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["Convai.Runtime.Networking.Media.IAudioTrackManager"] = new[] { "Convai.Runtime" },
                ["Convai.Infrastructure.Networking.IRemotePlayerRegistry"] =
                    new[] { "Convai.Infrastructure.Networking.Abstractions", "Convai.Infrastructure.Networking" }
            };

            var missing = new List<string>();
            var publicTypes = new List<string>();

            foreach (KeyValuePair<string, string[]> target in targets)
            {
                Type type = FindType(target.Key, target.Value);
                if (type == null)
                {
                    missing.Add(target.Key);
                    continue;
                }

                if (type.IsPublic) publicTypes.Add(target.Key);
            }

            Assert.IsEmpty(missing, $"Could not find expected seam types: [{string.Join(", ", missing)}]");
            Assert.IsEmpty(publicTypes, $"Future seam types must be internal: [{string.Join(", ", publicTypes)}]");
        }

        [Test]
        [Category("Architecture")]
        public void Internal_Implementations_Are_Not_Public()
        {
            var targets = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Convai.Runtime.Networking.Media.AudioTrackManager"] = "Convai.Runtime",
                ["Convai.Runtime.Vision.Transport.VideoTrackManager"] = "Convai.Runtime",
                ["Convai.Runtime.Adapters.Networking.PlayerSessionAdapter"] = "Convai.Runtime",
                ["Convai.Runtime.Adapters.Networking.CharacterRegistryAdapter"] = "Convai.Runtime",
                ["Convai.Runtime.Adapters.Networking.ConfigurationProviderAdapter"] = "Convai.Runtime",
                ["Convai.Runtime.Adapters.Vision.VideoTrackUnpublisherAdapter"] = "Convai.Runtime",
                ["Convai.Runtime.Adapters.Platform.ConvaiPermissionService"] = "Convai.Runtime"
            };

            var missing = new List<string>();
            var publicTypes = new List<string>();

            foreach (KeyValuePair<string, string> target in targets)
            {
                string fullName = target.Key;
                string assemblyName = target.Value;
                Type type = FindType(fullName, assemblyName);
                if (type == null)
                {
                    missing.Add(fullName);
                    continue;
                }

                if (type.IsPublic) publicTypes.Add(fullName);
            }

            Assert.IsEmpty(missing,
                $"Could not find target internal implementation types: [{string.Join(", ", missing)}]");
            Assert.IsEmpty(publicTypes,
                $"Internal implementation types must not be public: [{string.Join(", ", publicTypes)}]");
        }

        [Test]
        [Category("Architecture")]
        public void Samples_Do_Not_CrossReference_Each_Other()
        {
            string basicRoot = Path.Combine(PackageRoot, "Samples", "BasicSample");
            string lipSyncRoot = Path.Combine(PackageRoot, "Samples", "LipSyncSample");

            Assert.IsTrue(Directory.Exists(basicRoot), $"BasicSample path not found: {basicRoot}");
            Assert.IsTrue(Directory.Exists(lipSyncRoot), $"LipSyncSample path not found: {lipSyncRoot}");

            Dictionary<string, string> guidPathMap = BuildGuidPathMap();
            var violations = new List<string>();

            static void CollectCrossSampleReferences(string sourceRoot, string forbiddenPrefix,
                Dictionary<string, string> map, List<string> results)
            {
                foreach (string filePath in EnumerateSerializedAssetFiles(sourceRoot))
                foreach (string referencePath in GetResolvedAssetReferences(filePath, map))
                    if (referencePath.StartsWith(forbiddenPrefix, StringComparison.Ordinal))
                        results.Add($"{ToRelativePath(filePath)} -> {referencePath}");
            }

            CollectCrossSampleReferences(basicRoot, "Samples/LipSyncSample/", guidPathMap, violations);
            CollectCrossSampleReferences(lipSyncRoot, "Samples/BasicSample/", guidPathMap, violations);

            Assert.IsEmpty(violations,
                FormatViolations("Sample folders must not directly reference each other:", violations));
        }

        [Test]
        [Category("Architecture")]
        public void Shared_Sample_Scene_References_Live_In_Core_Or_SamplesShared()
        {
            string basicScene = Path.Combine(PackageRoot, "Samples", "BasicSample", "Scenes", "Basic Sample.unity");
            string lipSyncScene =
                Path.Combine(PackageRoot, "Samples", "LipSyncSample", "Scenes", "LipSync Sample.unity");

            Assert.IsTrue(File.Exists(basicScene), $"Basic sample scene not found: {basicScene}");
            Assert.IsTrue(File.Exists(lipSyncScene), $"LipSync sample scene not found: {lipSyncScene}");

            Dictionary<string, string> guidPathMap = BuildGuidPathMap();
            var sharedReferences = GetResolvedAssetReferences(basicScene, guidPathMap)
                .Intersect(GetResolvedAssetReferences(lipSyncScene, guidPathMap), StringComparer.Ordinal)
                .Where(path => path.StartsWith("Samples/", StringComparison.Ordinal) &&
                               !path.StartsWith("SamplesShared/", StringComparison.Ordinal))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();

            Assert.IsEmpty(sharedReferences,
                FormatViolations("Assets shared by both sample scenes must live in SamplesShared or core package roots:",
                    sharedReferences));
        }

        [Test]
        [Category("Architecture")]
        public void Sample_Content_Must_Not_Reference_Editor_Roots()
        {
            string[] sampleRoots =
            {
                Path.Combine(PackageRoot, "SamplesShared"),
                Path.Combine(PackageRoot, "Samples", "BasicSample"),
                Path.Combine(PackageRoot, "Samples", "LipSyncSample")
            };

            Dictionary<string, string> guidPathMap = BuildGuidPathMap();
            var violations = new List<string>();

            foreach (string sampleRoot in sampleRoots)
            {
                Assert.IsTrue(Directory.Exists(sampleRoot), $"Sample root not found: {sampleRoot}");

                foreach (string filePath in EnumerateSerializedAssetFiles(sampleRoot))
                foreach (string referencePath in GetResolvedAssetReferences(filePath, guidPathMap))
                {
                    if (referencePath.StartsWith("SDK/Editor/", StringComparison.Ordinal))
                    {
                        // Allow samples to use SDK branding graphics
                        if (referencePath.StartsWith("SDK/Editor/Art/UI/Branding", StringComparison.Ordinal))
                            continue;

                        violations.Add($"{ToRelativePath(filePath)} -> {referencePath}");
                    }
                }
            }

            Assert.IsEmpty(violations,
                FormatViolations("Sample-owned content must not reference editor-only package roots:", violations));
        }

        [Test]
        [Category("Architecture")]
        public void NonSample_Package_Content_Must_Not_Own_URP_Dependencies()
        {
            string[] nonSampleRoots =
            {
                Path.Combine(PackageRoot, "SDK"),
                Path.Combine(PackageRoot, "Prefabs"),
                Path.Combine(PackageRoot, "Resources"),
                Path.Combine(PackageRoot, "Tests"),
                Path.Combine(PackageRoot, "SamplesShared")
            };

            string[] extensions =
            {
                ".cs", ".asmdef", ".unity", ".prefab", ".asset", ".mat", ".controller"
            };

            string[] forbiddenTokens =
            {
                "Unity.RenderPipelines.",
                "com.unity.render-pipelines."
            };

            var violations = new List<string>();

            foreach (string root in nonSampleRoots)
            {
                Assert.IsTrue(Directory.Exists(root), $"Non-sample root not found: {root}");

                foreach (string filePath in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    if (filePath.EndsWith("ArchitectureGuardTests.cs", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!extensions.Contains(Path.GetExtension(filePath), StringComparer.OrdinalIgnoreCase))
                        continue;

                    string content = File.ReadAllText(filePath);
                    if (forbiddenTokens.Any(content.Contains))
                        violations.Add(ToRelativePath(filePath));
                }
            }

            Assert.IsEmpty(violations,
                FormatViolations("Only sample-owned content should carry URP dependencies or serialized URP references:",
                    violations));
        }

        [Test]
        [Category("Architecture")]
        public void RC8_NoRuntimeReferences_IServiceConsumer()
        {
            string sdkRuntimeRoot = Path.Combine(SdkRoot, "Runtime");
            Assert.IsTrue(Directory.Exists(sdkRuntimeRoot), $"SDK Runtime root not found: {sdkRuntimeRoot}");

            var violations = new List<string>();
            foreach (string filePath in Directory.EnumerateFiles(sdkRuntimeRoot, "*.cs", SearchOption.AllDirectories))
            {
                string content = File.ReadAllText(filePath);
                if (content.Contains("IServiceConsumer", StringComparison.Ordinal) ||
                    content.Contains("ConsumeServices(", StringComparison.Ordinal))
                    violations.Add(ToRelativePath(filePath));
            }

            Assert.IsEmpty(violations,
                FormatViolations("Runtime must not reference IServiceConsumer or ConsumeServices:", violations));
        }

        [Test]
        [Category("Architecture")]
        public void RC8_NoRuntimeReferences_TransportProviderRegistry()
        {
            string packageRoot = Path.Combine(PackageRoot);
            var violations = new List<string>();
            foreach (string filePath in Directory.EnumerateFiles(packageRoot, "*.cs", SearchOption.AllDirectories))
            {
                if (filePath.Contains("ArchitectureGuardTests", StringComparison.Ordinal)) continue;
                string content = File.ReadAllText(filePath);
                if (content.Contains("TransportProviderRegistry", StringComparison.Ordinal))
                    violations.Add(ToRelativePath(filePath));
            }

            Assert.IsEmpty(violations,
                FormatViolations("Package must not reference TransportProviderRegistry:", violations));
        }

        [Test]
        [Category("Architecture")]
        public void RC8_NoRuntimeReferences_ConversationProviderRegistry()
        {
            string packageRoot = Path.Combine(PackageRoot);
            var violations = new List<string>();
            foreach (string filePath in Directory.EnumerateFiles(packageRoot, "*.cs", SearchOption.AllDirectories))
            {
                if (filePath.Contains("ArchitectureGuardTests", StringComparison.Ordinal)) continue;
                string content = File.ReadAllText(filePath);
                if (content.Contains("ConversationProviderRegistry", StringComparison.Ordinal))
                    violations.Add(ToRelativePath(filePath));
            }

            Assert.IsEmpty(violations,
                FormatViolations("Package must not reference ConversationProviderRegistry:", violations));
        }

        [Test]
        [Category("Architecture")]
        public void RC8_NoProductionStartup_FindObjectsByType_MonoBehaviour()
        {
            string managerPath = Path.Combine(SdkRoot, "Runtime", "Components", "ConvaiManager.cs");
            Assert.IsTrue(File.Exists(managerPath), "ConvaiManager.cs not found.");
            string content = File.ReadAllText(managerPath);
            bool hasFindMonoBehaviour = content.Contains("FindObjectsByType<MonoBehaviour>", StringComparison.Ordinal) ||
                                       content.Contains("FindObjectOfType<MonoBehaviour>", StringComparison.Ordinal);
            Assert.IsFalse(hasFindMonoBehaviour,
                "Production startup must not use FindObjectsByType<MonoBehaviour> or FindObjectOfType<MonoBehaviour> in ConvaiManager.");
        }

        [Test]
        [Category("Architecture")]
        public void Phase3_ShowcaseCameraController_Does_Not_Fallback_To_SceneWide_Search()
        {
            string controllerPath = Path.Combine(PackageRoot, "Samples", "LipSyncSample", "Scripts", "Showcase",
                "Runtime", "ShowcaseCameraController.cs");
            Assert.IsTrue(File.Exists(controllerPath), "ShowcaseCameraController.cs not found.");

            string content = File.ReadAllText(controllerPath);
            Assert.IsFalse(content.Contains("FindObjectsByType<Animator>", StringComparison.Ordinal),
                "ShowcaseCameraController must not scan the scene for Animator components.");
            Assert.IsFalse(content.Contains("FindObjectsByType<SkinnedMeshRenderer>", StringComparison.Ordinal),
                "ShowcaseCameraController must not scan the scene for SkinnedMeshRenderer components.");
        }

        [Test]
        [Category("Architecture")]
        public void TestTree_Has_No_Legacy_Remnants_Or_MacOs_Junk()
        {
            string testsRoot = Path.Combine(PackageRoot, "Tests");
            Assert.IsTrue(Directory.Exists(testsRoot), $"Tests root not found: {testsRoot}");

            string legacySdkTestsRoot = Path.Combine(PackageRoot, "SDK", "Tests");
            Assert.IsFalse(Directory.Exists(legacySdkTestsRoot),
                $"Legacy SDK/Tests tree should be deleted: {ToRelativePath(legacySdkTestsRoot)}");

            var dsStoreFiles = Directory
                .EnumerateFiles(testsRoot, ".DS_Store", SearchOption.AllDirectories)
                .Select(ToRelativePath)
                .ToList();

            Assert.IsEmpty(dsStoreFiles,
                FormatViolations("Tests tree must not contain macOS .DS_Store junk files:", dsStoreFiles));
        }

        [Test]
        [Category("Architecture")]
        public void PackageTree_Has_No_MacOs_Junk()
        {
            var dsStoreFiles = Directory
                .EnumerateFiles(PackageRoot, ".DS_Store", SearchOption.AllDirectories)
                .Select(ToRelativePath)
                .ToList();

            Assert.IsEmpty(dsStoreFiles,
                FormatViolations("Package tree must not contain macOS .DS_Store junk files:", dsStoreFiles));
        }

        [Test]
        [Category("Architecture")]
        public void RC8_LegacyIdentityBridges_Removed()
        {
            Type adapter = FindType("Convai.Runtime.Identity.LegacyEndUserIdentityProviderAdapter", "Convai.Runtime");
            Type bridge = FindType("Convai.Runtime.Identity.LegacyEndUserIdProviderBridge", "Convai.Runtime");
            Assert.IsNull(adapter, "LegacyEndUserIdentityProviderAdapter must be deleted.");
            Assert.IsNull(bridge, "LegacyEndUserIdProviderBridge must be deleted.");
        }

        [Test]
        [Category("Architecture")]
        public void WorkingWithEvents_Doc_Is_Present_And_Linked_From_Primary_Public_Docs()
        {
            string workingWithEventsPath = Path.Combine(PackageRoot, "Documentation~", "WORKING-WITH-EVENTS.md");
            Assert.IsTrue(File.Exists(workingWithEventsPath),
                $"Working-with-events doc not found: {ToRelativePath(workingWithEventsPath)}");

            string[] linkedDocs =
            {
                Path.Combine(PackageRoot, "Documentation~", "README.md"),
                Path.Combine(PackageRoot, "Documentation~", "Convai SDK For Unity.md"),
                Path.Combine(PackageRoot, "Documentation~", "SETUP.md"),
                Path.Combine(PackageRoot, "Documentation~", "API-ENTRYPOINTS.md"),
                Path.Combine(PackageRoot, "Documentation~", "TROUBLESHOOTING.md")
            };

            var missingLinks = new List<string>();
            foreach (string docPath in linkedDocs)
            {
                Assert.IsTrue(File.Exists(docPath), $"Public doc not found: {ToRelativePath(docPath)}");
                string content = File.ReadAllText(docPath);
                if (!content.Contains("WORKING-WITH-EVENTS.md", StringComparison.Ordinal))
                    missingLinks.Add(ToRelativePath(docPath));
            }

            Assert.IsEmpty(missingLinks,
                FormatViolations("Primary public docs must link WORKING-WITH-EVENTS.md:", missingLinks));
        }

        [Test]
        [Category("Architecture")]
        public void Public_Event_Docs_Describe_Approved_Event_Hierarchy()
        {
            string apiEntrypointsPath = Path.Combine(PackageRoot, "Documentation~", "API-ENTRYPOINTS.md");
            string workingWithEventsPath = Path.Combine(PackageRoot, "Documentation~", "WORKING-WITH-EVENTS.md");

            string apiEntrypoints = File.ReadAllText(apiEntrypointsPath);
            string workingWithEvents = File.ReadAllText(workingWithEventsPath);

            StringAssert.Contains("ConvaiManager.Events", apiEntrypoints);
            StringAssert.Contains("ConvaiManager.Transcripts", apiEntrypoints);
            StringAssert.Contains("ConvaiSessionEventRelay", apiEntrypoints);
            StringAssert.Contains("ConvaiTranscriptEventRelay", apiEntrypoints);
            StringAssert.Contains("ConvaiCharacterEventRelay", apiEntrypoints);
            StringAssert.Contains("ConvaiCharacter", apiEntrypoints);
            StringAssert.Contains("character-scoped", apiEntrypoints);

            StringAssert.Contains("canonical typed reactive API", workingWithEvents);
            StringAssert.Contains("canonical transcript timeline and history API", workingWithEvents);
            StringAssert.Contains("no-code UnityEvent relays", workingWithEvents);
            StringAssert.Contains("local character convenience callbacks", workingWithEvents);
        }

        [Test]
        [Category("Architecture")]
        public void Package_Docs_And_Runtime_Code_Do_Not_Reference_Removed_Facade_Subscription_Names()
        {
            string[] roots =
            {
                Path.Combine(PackageRoot, "SDK"),
                Path.Combine(PackageRoot, "Documentation~")
            };

            string[] extensions =
            {
                ".cs", ".md"
            };

            string[] forbiddenTokens =
            {
                "manager.Events.OnCharacterTranscript",
                "manager.Events.OnPlayerTranscript",
                "manager.Events.OnActionReceived",
                "manager.Events.OnModerationResponse",
                "manager.Events.OnLlmNoResponse",
                "manager.Events.OnUserIdleWarning",
                "ConvaiEvents.OnCharacterTranscript",
                "ConvaiEvents.OnPlayerTranscript",
                "ConvaiEvents.OnActionReceived",
                "ConvaiEvents.OnModerationResponse",
                "ConvaiEvents.OnLlmNoResponse",
                "ConvaiEvents.OnUserIdleWarning"
            };

            var violations = new List<string>();

            foreach (string root in roots)
            {
                Assert.IsTrue(Directory.Exists(root), $"Documentation/runtime root not found: {root}");

                foreach (string filePath in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    if (!extensions.Contains(Path.GetExtension(filePath), StringComparer.OrdinalIgnoreCase))
                        continue;

                    if (filePath.EndsWith("ArchitectureGuardTests.cs", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string content = File.ReadAllText(filePath);
                    if (forbiddenTokens.Any(content.Contains))
                        violations.Add(ToRelativePath(filePath));
                }
            }

            Assert.IsEmpty(violations,
                FormatViolations("Package docs/runtime code still reference removed ConvaiEvents subscription names:", violations));
        }

        [Test]
        [Category("Architecture")]
        public void Package_Docs_And_Runtime_Code_Do_Not_Reference_Removed_Compatibility_Apis()
        {
            string[] roots =
            {
                Path.Combine(PackageRoot, "SDK"),
                Path.Combine(PackageRoot, "Documentation~")
            };

            string[] extensions =
            {
                ".cs", ".md"
            };

            string[] forbiddenTokens =
            {
                "ConvaiServiceLocator",
                "ConvaiRoomSession",
                "ConvaiManager.Instance",
                "ConvaiCompositionRoot",
                "Phase 09",
                "Phase 10",
                "Application/README.md",
                "Shared/README.md",
                "manager-driven bootstrap pipeline",
                "runtime bootstrap pipeline",
                "DI container"
            };

            var violations = new List<string>();

            foreach (string root in roots)
            {
                Assert.IsTrue(Directory.Exists(root), $"Documentation/runtime root not found: {root}");

                foreach (string filePath in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    if (!extensions.Contains(Path.GetExtension(filePath), StringComparer.OrdinalIgnoreCase))
                        continue;

                    if (filePath.EndsWith("ArchitectureGuardTests.cs", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string content = File.ReadAllText(filePath);
                    if (forbiddenTokens.Any(content.Contains))
                        violations.Add(ToRelativePath(filePath));
                }
            }

            Assert.IsEmpty(violations,
                FormatViolations("Package docs/runtime code still reference removed compatibility APIs:", violations));
        }
    }
}
