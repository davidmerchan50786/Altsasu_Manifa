#if VISTA
using System.ComponentModel;
using UnityEngine;

namespace Pinwheel.Vista.Graph
{
    [System.Serializable]
    /// <summary>
    /// Stores the request-wide settings used to execute a terrain graph.
    /// </summary>
    /// <remarks>
    /// <see cref="TerrainGraph"/> converts this struct into the argument table exposed through
    /// <see cref="GraphContext"/>. The values here define the output resolution, evaluation bounds,
    /// terrain height scale, random seed, and a few pipeline control flags for one execution run.
    /// </remarks>
    public struct TerrainGenerationConfigs
    {
        [SerializeField]
        private int m_resolution;
        /// <summary>
        /// Base graph resolution used when nodes allocate outputs relative to the request size.
        /// </summary>
        /// <remarks>
        /// Assigned values are clamped to Vista's supported resolution range and rounded to a
        /// multiple of 8 so they remain compatible with compute-shader dispatch logic.
        /// </remarks>
        public int resolution
        {
            get
            {
                return m_resolution;
            }
            set
            {
                m_resolution = Utilities.MultipleOf8(Mathf.Clamp(value, Constants.RES_MIN, Constants.RES_MAX));
            }
        }

        [SerializeField]
        private Rect m_worldBounds;
        /// <summary>
        /// Horizontal evaluation area of the current graph request.
        /// </summary>
        /// <remarks>
        /// <see cref="TerrainGraph"/> forwards this as <see cref="Args.WORLD_BOUNDS"/> encoded as
        /// <c>(x, y, width, height)</c>. In terrain workflows it usually represents an XZ rectangle in
        /// world or biome-local space.
        /// </remarks>
        public Rect worldBounds
        {
            get
            {
                return m_worldBounds;
            }
            set
            {
                m_worldBounds = value;
            }
        }

        [SerializeField]
        private float m_terrainHeight;
        /// <summary>
        /// Maximum terrain height used to interpret normalized height data in world units.
        /// </summary>
        /// <remarks>
        /// Negative values are clamped to zero.
        /// </remarks>
        public float terrainHeight
        {
            get
            {
                return m_terrainHeight;
            }
            set
            {
                m_terrainHeight = Mathf.Max(0, value);
            }
        }

        [SerializeField]
        private int m_seed;
        /// <summary>
        /// Base random seed for deterministic graph execution.
        /// </summary>
        /// <remarks>
        /// Noise nodes and other stochastic systems combine this with their local seed values to
        /// produce repeatable results for the same request.
        /// </remarks>
        public int seed
        {
            get
            {
                return m_seed;
            }
            set
            {
                m_seed = value;
            }
        }

        [SerializeField]
        private bool m_shouldOutputTempHeight;
        [EditorBrowsable(EditorBrowsableState.Never)]
        /// <summary>
        /// Internal flag that tells the graph pipeline to preserve temporary height outputs.
        /// </summary>
        /// <remarks>
        /// This is not part of the normal public configuration surface. It is used by runtime
        /// execution paths that need intermediate height data in addition to the final outputs.
        /// </remarks>
        internal bool shouldOutputTempHeight
        {
            get
            {
                return m_shouldOutputTempHeight;
            }
            set
            {
                m_shouldOutputTempHeight = value;
            }
        }

        /// <summary>
        /// Creates a default terrain-generation request configuration.
        /// </summary>
        /// <returns>
        /// A config initialized with Vista's standard debug defaults:
        /// 1024 resolution, 1000x1000 bounds at the origin, 600 terrain height, seed 1234, and no
        /// temporary-height output.
        /// </returns>
        public static TerrainGenerationConfigs Create()
        {
            TerrainGenerationConfigs configs = new TerrainGenerationConfigs();
            configs.resolution = Constants.K1K24;
            configs.worldBounds = new Rect(0, 0, 1000, 1000);
            configs.terrainHeight = 600;
            configs.seed = 1234;
            configs.shouldOutputTempHeight = false;
            return configs;
        }
    }
}
#endif


