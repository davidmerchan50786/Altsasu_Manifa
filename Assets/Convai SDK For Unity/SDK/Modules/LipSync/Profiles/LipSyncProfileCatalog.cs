using System;
using System.Collections.Generic;
using System.Threading;
using Convai.Domain.Logging;
using Convai.Domain.Models.LipSync;
using Convai.Runtime.Logging;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Convai.Modules.LipSync.Profiles
{
#if UNITY_EDITOR
    [InitializeOnLoad]
#endif
    public static class LipSyncProfileCatalog
    {
        private const string BuiltInRegistryResourcePath = "LipSync/ProfileRegistries/LipSyncBuiltInProfileRegistry";
        private const string RegistryResourcePath = "LipSync/ProfileRegistries";
        private const string LogPrefix = "[Convai LipSync Profiles]";

        private static readonly Dictionary<string, ConvaiLipSyncProfileAsset> ProfilesById =
            new(StringComparer.Ordinal);

        private static readonly Dictionary<string, ConvaiLipSyncProfileAsset> ProfilesByTransportFormat =
            new(StringComparer.Ordinal);

        private static readonly List<ConvaiLipSyncProfileAsset> OrderedProfiles = new();
        private static readonly List<string> ValidationIssues = new();

        private static bool _initialized;
        private static readonly object InitLock = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void DomainReload()
        {
            lock (InitLock)
            {
                ClearCaches();
                Volatile.Write(ref _initialized, false);
            }
        }

        public static IReadOnlyList<ConvaiLipSyncProfileAsset> GetProfiles()
        {
            EnsureInitialized();
            return OrderedProfiles;
        }

        public static bool TryGetProfile(LipSyncProfileId profileId, out ConvaiLipSyncProfileAsset profile)
        {
            EnsureInitialized();
            return ProfilesById.TryGetValue(profileId.Value, out profile);
        }

        public static bool TryGetProfile(string rawProfileId, out ConvaiLipSyncProfileAsset profile) =>
            TryGetProfile(new LipSyncProfileId(rawProfileId), out profile);

        public static IReadOnlyList<string> GetSourceBlendshapeNamesOrEmpty(LipSyncProfileId profileId) =>
            LipSyncBuiltInProfileLibrary.GetSourceBlendshapeNamesOrEmpty(profileId);

        public static IReadOnlyList<string> GetValidationIssues()
        {
            EnsureInitialized();
            return ValidationIssues;
        }

        private static void EnsureInitialized()
        {
            if (Volatile.Read(ref _initialized)) return;

            lock (InitLock)
            {
                if (Volatile.Read(ref _initialized)) return;

                ClearCaches();

                ConvaiLipSyncProfileRegistryAsset builtInRegistry = ResolveBuiltInRegistry();
                if (builtInRegistry == null)
                {
                    AddValidationIssue(
                        "Built-in profile registry is missing at Resources/LipSync/ProfileRegistries/LipSyncBuiltInProfileRegistry.");
                }
                else
                    RegisterRegistryProfiles(builtInRegistry);

                List<ConvaiLipSyncProfileRegistryAsset> extensionRegistries =
                    ResolveExtensionRegistries(builtInRegistry);
                foreach (ConvaiLipSyncProfileRegistryAsset registry in extensionRegistries)
                    RegisterRegistryProfiles(registry);

                RebuildFormatMapAndOrderedList();
                OrderedProfiles.Sort((a, b) => string.CompareOrdinal(a.ProfileId.Value, b.ProfileId.Value));
                Volatile.Write(ref _initialized, true);
            }
        }

        private static void RegisterRegistryProfiles(ConvaiLipSyncProfileRegistryAsset registry)
        {
            IReadOnlyList<ConvaiLipSyncProfileAsset> profiles = registry.Profiles;
            if (profiles == null || profiles.Count == 0) return;

            for (int i = 0; i < profiles.Count; i++)
            {
                ConvaiLipSyncProfileAsset profile = profiles[i];
                if (profile == null) continue;

                LipSyncProfileId profileId = profile.ProfileId;
                if (!profileId.IsValid)
                {
                    AddValidationIssue(
                        $"Skipping profile in '{GetRegistryDisplayName(registry)}' because profile id is empty.");
                    continue;
                }

                if (!profile.IsValid)
                {
                    string issue = profile.DescribeValidationIssue();
                    AddValidationIssue(
                        $"Skipping invalid profile '{profileId}' in '{GetRegistryDisplayName(registry)}': {issue}");
                    continue;
                }

                if (ProfilesById.TryGetValue(profileId.Value, out ConvaiLipSyncProfileAsset existing))
                {
                    AddValidationIssue(
                        $"Duplicate profile id '{profileId}' found. Overriding '{existing.name}' with '{profile.name}'.");
                }

                ProfilesById[profileId.Value] = profile;
            }
        }

        private static void RebuildFormatMapAndOrderedList()
        {
            ProfilesByTransportFormat.Clear();
            OrderedProfiles.Clear();

            foreach (KeyValuePair<string, ConvaiLipSyncProfileAsset> pair in ProfilesById)
            {
                ConvaiLipSyncProfileAsset profile = pair.Value;
                OrderedProfiles.Add(profile);

                string transportFormat = profile.TransportFormat;
                if (string.IsNullOrWhiteSpace(transportFormat)) continue;

                if (ProfilesByTransportFormat.TryGetValue(transportFormat, out ConvaiLipSyncProfileAsset existing))
                {
                    AddValidationIssue(
                        $"Duplicate transport format '{transportFormat}' mapped to both '{existing.name}' and '{profile.name}'. Last one wins.");
                }

                ProfilesByTransportFormat[transportFormat] = profile;
            }
        }

        private static ConvaiLipSyncProfileRegistryAsset ResolveBuiltInRegistry()
        {
#if UNITY_EDITOR
            if (_builtInRegistryOverrideForTests != null) return _builtInRegistryOverrideForTests;
#endif
            return Resources.Load<ConvaiLipSyncProfileRegistryAsset>(BuiltInRegistryResourcePath);
        }

        private static List<ConvaiLipSyncProfileRegistryAsset> ResolveExtensionRegistries(
            ConvaiLipSyncProfileRegistryAsset builtInRegistry)
        {
            List<ConvaiLipSyncProfileRegistryAsset> result = new();

#if UNITY_EDITOR
            if (_extensionRegistryOverridesForTests != null)
            {
                for (int i = 0; i < _extensionRegistryOverridesForTests.Count; i++)
                {
                    ConvaiLipSyncProfileRegistryAsset registry = _extensionRegistryOverridesForTests[i];
                    if (registry != null) result.Add(registry);
                }

                SortRegistries(result);
                return result;
            }
#endif

            ConvaiLipSyncProfileRegistryAsset[] registries =
                Resources.LoadAll<ConvaiLipSyncProfileRegistryAsset>(RegistryResourcePath);

            for (int i = 0; i < registries.Length; i++)
            {
                ConvaiLipSyncProfileRegistryAsset registry = registries[i];
                if (registry != null && !ReferenceEquals(registry, builtInRegistry) && !IsBuiltInRegistry(registry))
                    result.Add(registry);
            }

            SortRegistries(result);
            return result;
        }

        private static void SortRegistries(List<ConvaiLipSyncProfileRegistryAsset> registries)
        {
            registries.Sort((a, b) =>
            {
                int priorityCompare = a.Priority.CompareTo(b.Priority);
                if (priorityCompare != 0) return priorityCompare;

                return string.CompareOrdinal(GetRegistrySortKey(a), GetRegistrySortKey(b));
            });
        }

        private static bool IsBuiltInRegistry(ConvaiLipSyncProfileRegistryAsset registry)
        {
            if (registry == null) return false;

#if UNITY_EDITOR
            string path = AssetDatabase.GetAssetPath(registry);
            if (!string.IsNullOrWhiteSpace(path))
                return path.EndsWith("/LipSyncBuiltInProfileRegistry.asset", StringComparison.OrdinalIgnoreCase);
#endif
            return string.Equals(registry.name, "LipSyncBuiltInProfileRegistry", StringComparison.Ordinal);
        }

        private static string GetRegistrySortKey(ConvaiLipSyncProfileRegistryAsset registry)
        {
#if UNITY_EDITOR
            string path = AssetDatabase.GetAssetPath(registry);
            if (!string.IsNullOrWhiteSpace(path)) return path;
#endif
            return registry.name ?? string.Empty;
        }

        private static string GetRegistryDisplayName(ConvaiLipSyncProfileRegistryAsset registry)
        {
#if UNITY_EDITOR
            string path = AssetDatabase.GetAssetPath(registry);
            if (!string.IsNullOrWhiteSpace(path)) return path;
#endif
            return registry != null ? registry.name : "(null)";
        }

        private static void ClearCaches()
        {
            ProfilesById.Clear();
            ProfilesByTransportFormat.Clear();
            OrderedProfiles.Clear();
            ValidationIssues.Clear();
        }

        private static void AddValidationIssue(string message)
        {
            ValidationIssues.Add(message);
            ConvaiLogger.Warning($"{LogPrefix} {message}", LogCategory.LipSync);
        }

#if UNITY_EDITOR
        private static ConvaiLipSyncProfileRegistryAsset _builtInRegistryOverrideForTests;
        private static IReadOnlyList<ConvaiLipSyncProfileRegistryAsset> _extensionRegistryOverridesForTests;

        static LipSyncProfileCatalog()
        {
            EditorApplication.projectChanged -= InvalidateEditorCaches;
            EditorApplication.projectChanged += InvalidateEditorCaches;

            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }
#endif

#if UNITY_EDITOR
        private static void InvalidateEditorCaches()
        {
            lock (InitLock)
            {
                ClearCaches();
                Volatile.Write(ref _initialized, false);
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange _) => InvalidateEditorCaches();

        public static void SetRegistryOverridesForTests(
            ConvaiLipSyncProfileRegistryAsset builtInRegistry,
            IReadOnlyList<ConvaiLipSyncProfileRegistryAsset> extensionRegistries)
        {
            lock (InitLock)
            {
                _builtInRegistryOverrideForTests = builtInRegistry;
                _extensionRegistryOverridesForTests = extensionRegistries;
                ClearCaches();
                Volatile.Write(ref _initialized, false);
            }
        }

        internal static void InvalidateCachesForEditor() => InvalidateEditorCaches();

        public static void ClearCachesForTests()
        {
            lock (InitLock)
            {
                _builtInRegistryOverrideForTests = null;
                _extensionRegistryOverridesForTests = null;
                ClearCaches();
                Volatile.Write(ref _initialized, false);
            }
        }
#endif
    }
}
