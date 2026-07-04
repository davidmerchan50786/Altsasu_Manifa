#if UNITY_EDITOR
using UnityEditor;

namespace Convai.Modules.LipSync.Editor.UI
{
    internal static class ConvaiLipSyncSectionStateStore
    {
        private const string Prefix = "Convai.LipSync";
        private const string Suffix = "Expanded";

        public static bool Get(string hostId, string sectionId, bool defaultValue)
        {
            string key = BuildKey(hostId, sectionId);
            return EditorPrefs.GetBool(key, defaultValue);
        }

        public static void Set(string hostId, string sectionId, bool value)
        {
            string key = BuildKey(hostId, sectionId);
            EditorPrefs.SetBool(key, value);
        }

        internal static string BuildKey(string hostId, string sectionId) =>
            $"{Prefix}.{Normalize(hostId)}.{Normalize(sectionId)}.{Suffix}";

        private static string Normalize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "Unknown";

            return raw.Trim().Replace(" ", string.Empty);
        }
    }
}
#endif
