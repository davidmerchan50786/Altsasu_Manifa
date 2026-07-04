#if VISTA
#if GRIFFIN
using UnityEngine;
using System;
using Pinwheel.Griffin;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Pinwheel.Vista.PolarisTerrain
{
    /// <summary>
    /// Registers and configures Vista support for Polaris terrains.
    /// </summary>
    /// <remarks>
    /// This terrain-system adapter exposes the Polaris terrain and tile component types to Vista and ensures a compatible
    /// <see cref="PolarisTile"/> is attached to supported targets when a manager sets up terrain integration.
    /// </remarks>
    public class PolarisTerrainSystem : ITerrainSystem
    {
        /// <summary>
        /// Gets the display name used for this terrain-system integration.
        /// </summary>
        public string terrainLabel
        {
            get
            {
                return "Polaris Terrain";
            }
        }

#if UNITY_EDITOR
        [InitializeOnLoadMethod]
#else
        [RuntimeInitializeOnLoadMethod]
#endif
        private static void OnInitialize()
        {
            VistaManager.RegisterTerrainSystem<PolarisTerrainSystem>();
        }

        /// <summary>
        /// Gets the Polaris terrain component type recognized by this integration.
        /// </summary>
        /// <returns><see cref="GStylizedTerrain"/>.</returns>
        public Type GetTerrainComponentType()
        {
            return typeof(GStylizedTerrain);
        }

        /// <summary>
        /// Gets the Vista tile component type used for Polaris terrains.
        /// </summary>
        /// <returns><see cref="PolarisTile"/>.</returns>
        public Type GetTileComponentType()
        {
            return typeof(PolarisTile);
        }

        /// <summary>
        /// Ensures a compatible Polaris tile component is attached to a target terrain object and bound to a manager.
        /// </summary>
        /// <param name="manager">The manager that should own the configured tile.</param>
        /// <param name="target">The GameObject expected to contain a <see cref="GStylizedTerrain"/> component.</param>
        /// <returns>
        /// The configured <see cref="PolarisTile"/> when the target contains a Polaris terrain; otherwise,
        /// <see langword="null"/>.
        /// </returns>
        /// <remarks>
        /// If the target does not already have a <see cref="PolarisTile"/>, one is added. In the Unity Editor the component
        /// add and manager-ID assignment are routed through Undo so the setup operation integrates with editor history.
        /// </remarks>
        public ITile SetupTile(VistaManager manager, GameObject target)
        {
            GStylizedTerrain terrainComponent = target.GetComponent<GStylizedTerrain>();
            if (terrainComponent == null)
            {
                return null;
            }
            PolarisTile tile = target.GetComponent<PolarisTile>();
            if (tile == null)
            {
#if UNITY_EDITOR
                tile = Undo.AddComponent<PolarisTile>(target);
#else
                tile = target.AddComponent<PolarisTile>();
#endif
            }
#if UNITY_EDITOR
            Undo.RecordObject(tile, "Modify Polaris Tile");
#endif
            tile.managerId = manager.id;
            return tile;
        }
    }
}
#endif
#endif


