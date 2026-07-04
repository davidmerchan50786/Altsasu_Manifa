using System.Collections.Generic;
using UnityEngine;

namespace Convai.Modules.LipSync.Profiles
{
    /// <summary>
    ///     Registry asset that groups profile assets and defines merge precedence for catalog loading.
    /// </summary>
    [CreateAssetMenu(fileName = "LipSyncProfileRegistry", menuName = "Convai/Lip Sync/Profile Registry")]
    public class ConvaiLipSyncProfileRegistryAsset : ScriptableObject
    {
        private static readonly List<ConvaiLipSyncProfileAsset> EmptyProfiles = new();

        [SerializeField] private int _priority;
        [SerializeField] private List<ConvaiLipSyncProfileAsset> _profiles = new();

        /// <summary>
        ///     Merge priority used by the catalog. Lower values are applied first.
        /// </summary>
        public int Priority => _priority;

        /// <summary>
        ///     Ordered list of profile assets contributed by this registry.
        /// </summary>
        public IReadOnlyList<ConvaiLipSyncProfileAsset> Profiles => _profiles ?? EmptyProfiles;

        private void OnValidate()
        {
#if UNITY_EDITOR
            LipSyncProfileCatalog.InvalidateCachesForEditor();
#endif
        }
    }
}
