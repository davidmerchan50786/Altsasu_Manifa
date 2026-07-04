#if VISTA
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Pinwheel.Vista
{
    /// <summary>
    /// Exposes Vista product and version constants and registers them with <see cref="VersionManager"/>.
    /// </summary>
    public static class VersionInfo
    {
#if UNITY_EDITOR
        [InitializeOnLoadMethod]
#else
        [RuntimeInitializeOnLoadMethod]
#endif
        /// <summary>
        /// Registers this module's version-string provider during editor or runtime initialization.
        /// </summary>
        public static void OnInitialize()
        {
            VersionManager.collectVersionInfoCallback += OnCollectVersionInfo;
        }

        private static void OnCollectVersionInfo(Collector<string> versionStrings)
        {
            versionStrings.Add($"{productName} {versionLabel}");
        }

        /// <summary>
        /// Major version component of the current Vista build.
        /// </summary>
        public static int major
        {
            get
            {
                return 3000;
            }
        }

        /// <summary>
        /// Minor version component of the current Vista build.
        /// </summary>
        public static int minor
        {
            get
            {
                return 1;
            }
        }

        /// <summary>
        /// Patch version component of the current Vista build.
        /// </summary>
        public static int patch
        {
            get
            {
                return 0;
            }
        }

        /// <summary>
        /// Indicates whether the current version label should include the beta suffix.
        /// </summary>
        public static bool isBeta
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Human-readable version label built from the numeric version parts.
        /// </summary>
        public static string versionLabel
        {
            get
            {
                return $"{major}.{minor}.{patch}{(isBeta ? "b" : "")}";
            }
        }

        /// <summary>
        /// Product name reported by this version provider.
        /// </summary>
        public static string productName
        {
            get
            {
                return "Vista";
            }
        }
    }
}
#endif


