#if VISTA
using UnityEngine;
using System;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Pinwheel.Vista.UnityTerrain
{
    /// <summary>
    /// Represents unity terrain system.
    /// </summary>
    public class UnityTerrainSystem : ITerrainSystem
    {
        /// <summary>
        /// Gets or sets member.
        /// </summary>
        public string terrainLabel
        {
            get
            {
                return "Unity Terrain";
            }
        }

#if UNITY_EDITOR
        [InitializeOnLoadMethod]
#else
        [RuntimeInitializeOnLoadMethod]
#endif
        /// <summary>
        /// Handles the initialize callback.
        /// </summary>
        public static void OnInitialize()
        {
            VistaManager.RegisterTerrainSystem<UnityTerrainSystem>();
        }

        /// <summary>
        /// Gets terrain component type.
        /// </summary>
        /// <returns>Get terrain component type result.</returns>
        public Type GetTerrainComponentType()
        {
            return typeof(Terrain);
        }

        /// <summary>
        /// Gets tile component type.
        /// </summary>
        /// <returns>Get tile component type result.</returns>
        public Type GetTileComponentType()
        {
            return typeof(TerrainTile);
        }

        /// <summary>
        /// Sets up tile.
        /// </summary>
        /// <param name="manager">Manager instance coordinating generation.</param>
        /// <param name="target">Target object to read from or write to.</param>
        /// <returns>Setup tile result.</returns>
        public ITile SetupTile(VistaManager manager, GameObject target)
        {
            Terrain terrain = target.GetComponent<Terrain>();
            if (terrain == null)
            {
                return null;
            }
            TerrainTile tile = target.GetComponent<TerrainTile>();
            if (tile == null)
            {
#if UNITY_EDITOR
                tile = Undo.AddComponent<TerrainTile>(target);
#else
                tile = target.AddComponent<TerrainTile>();
#endif
            }
#if UNITY_EDITOR
            Undo.RecordObject(tile, "Modify Terrain Tile");
#endif
            tile.managerId = manager.id;
            return tile;
        }
    }
}
#endif


