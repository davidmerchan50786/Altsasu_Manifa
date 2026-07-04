#if VISTA
using UnityEngine;

namespace Pinwheel.Vista
{
    [System.Serializable]
    /// <summary>
    /// Specifies how a biome should be composited with other biomes when multi-biome blending is enabled.
    /// </summary>
    /// <remarks>
    /// These options are consumed by the Big World blending pipeline through
    /// <c>VistaManager.blendBiomeDataCallback</c>. Not every biome output channel is configurable here:
    /// height maps, detail density maps, and instance-like buffers use these settings, while several other
    /// channels are currently blended with fixed behavior.
    /// </remarks>
    public struct BiomeBlendOptions
    {
        [System.Serializable]
        /// <summary>
        /// Blend operators for height map compositing between biomes.
        /// </summary>
        /// <remarks>
        /// Values are used as indices into a kernel name table, not cast directly to kernel indices.
        /// Serialization-compatible with the old <c>TextureBlendMode</c> (same int values 0–4).
        /// </remarks>
        public enum HeightBlendMode
        {
            /// <summary>
            /// Replaces the destination height with the source height, modulated by the biome mask.
            /// </summary>
            Replace = 0,
            /// <summary>
            /// Raises the destination terrain by adding the source height, modulated by the biome mask.
            /// </summary>
            Raise = 1,
            /// <summary>
            /// Lowers the destination terrain by subtracting the source height, modulated by the biome mask.
            /// </summary>
            Lower = 2,
            /// <summary>
            /// Keeps whichever height is greater between destination and source, modulated by the biome mask.
            /// </summary>
            KeepHigher = 3,
            /// <summary>
            /// Keeps whichever height is smaller between destination and source, modulated by the biome mask.
            /// </summary>
            KeepLower = 4
        }

        [System.Serializable]
        /// <summary>
        /// Blend operators for mesh density map compositing between biomes.
        /// </summary>
        /// <remarks>
        /// The mesh density map is a progressive value map: every value from 0 to 1 is meaningful.
        /// It controls how many polygons are added to an area (used by Polaris terrain via the height map B channel).
        /// All five operators are valid: Replace for normal biome transitions, Add or Subtract to layer or reduce
        /// density, Max or Min to enforce floors or caps.
        /// </remarks>
        public enum MeshDensityBlendMode
        {
            /// <summary>
            /// Replaces the destination density with the source density, modulated by the biome mask.
            /// </summary>
            Replace = 0,
            /// <summary>
            /// Adds the source density to the destination, modulated by the biome mask.
            /// Values above 1.0 are clamped after all biomes are blended.
            /// </summary>
            Add = 1,
            /// <summary>
            /// Subtracts the source density from the destination, modulated by the biome mask.
            /// Values below 0.0 are clamped after all biomes are blended.
            /// </summary>
            Subtract = 2,
            /// <summary>
            /// Keeps whichever density is greater between destination and source, modulated by the biome mask.
            /// </summary>
            Max = 3,
            /// <summary>
            /// Keeps whichever density is smaller between destination and source, modulated by the biome mask.
            /// </summary>
            Min = 4
        }

        [System.Serializable]
        /// <summary>
        /// Blend operators for hole map compositing between biomes.
        /// </summary>
        /// <remarks>
        /// Hole map convention: 1.0 = hole, 0.0 = solid surface.
        /// <c>Replace</c> uses linear interpolation — a later biome's hole pattern overwrites an earlier one
        /// in their overlapping region. <c>Accumulate</c> uses Max — any biome that punches a hole keeps it,
        /// which is the correct behavior when the same graph is scattered as multiple biome instances that
        /// each output a single hole at a different location.
        /// </remarks>
        public enum HoleBlendMode
        {
            /// <summary>
            /// Blends hole maps with linear interpolation, modulated by the biome mask.
            /// Later biomes overwrite earlier holes in their coverage area.
            /// </summary>
            Replace = 0,
            /// <summary>
            /// Takes the maximum of the destination and source hole values, modulated by the biome mask.
            /// The highest hole intensity wins at each pixel, so a hole set by any earlier biome is never
            /// filled by a later one. Use this when the same graph is scattered as many instances, each
            /// outputting an isolated hole.
            /// </summary>
            Max = 3
        }

        [System.Serializable]
        /// <summary>
        /// Blend behaviors for terrain texture weights between biomes.
        /// </summary>
        public enum TextureBlendMode
        {
            /// <summary>
            /// Texture weights gradually transition from one biome to the next based on biome masks.
            /// </summary>
            Replace = 0,
            /// <summary>
            /// Texture weights remain only where this biome wins the height blend.
            /// This mode is meaningful when height blending uses <see cref="HeightBlendMode.KeepHigher"/>
            /// or <see cref="HeightBlendMode.KeepLower"/>.
            /// </summary>
            HeightWin = 1
        }

        [System.Serializable]
        /// <summary>
        /// Blend behaviors for all population outputs: detail density maps, detail instance buffers,
        /// tree buffers, object buffers, and generic buffers.
        /// </summary>
        /// <remarks>
        /// Detail density maps are treated as population even though they are stored as textures,
        /// because their values represent instance counts per pixel rather than continuous surface properties.
        /// Buffer blending clones source buffers and applies biome masks to keep or suppress instances.
        /// Density map blending dispatches compute kernels the same way as other texture channels.
        /// </remarks>
        public enum PopulationBlendMode
        {
            /// <summary>
            /// Ecosystems transition from one biome to the next. Each biome claims its mask region,
            /// fading out any populations it does not own, so only one ecosystem dominates at any pixel.
            /// </summary>
            /// <remarks>
            /// For buffers: previously accumulated instances are eroded in the incoming biome's coverage area
            /// before the new biome's instances are added.
            /// For density maps: missing templates are dispatched with a black source, zeroing out their density
            /// in the biome's region.
            /// </remarks>
            Replace = 0,
            /// <summary>
            /// Ecosystems layer on top of each other. A biome only contributes its own populations
            /// and leaves all other populations untouched, so multiple ecosystems can coexist at a pixel.
            /// </summary>
            /// <remarks>
            /// For buffers: previously accumulated instances are not eroded. The new biome's instances
            /// are simply appended.
            /// For density maps: missing templates are skipped entirely, preserving any density already
            /// written to the canvas.
            /// </remarks>
            Coexist = 1,
            /// <summary>
            /// Population outputs appear only where this biome wins height blending.
            /// This mode is meaningful when height blending uses <see cref="HeightBlendMode.KeepHigher"/>
            /// or <see cref="HeightBlendMode.KeepLower"/>.
            /// </summary>
            HeightWin = 2
        }

        [SerializeField]
        private HeightBlendMode m_heightMapBlendMode;
        /// <summary>
        /// Gets or sets the blend operator used for the biome height map.
        /// </summary>
        /// <remarks>
        /// This setting is used when multiple biome height maps are merged into a single output height map.
        /// It only affects the multi-biome blending stage; it does not change how an individual biome graph generates its own height data.
        /// </remarks>
        public HeightBlendMode heightMapBlendMode
        {
            get
            {
                return m_heightMapBlendMode;
            }
            set
            {
                m_heightMapBlendMode = value;
                if (m_textureBlendMode == TextureBlendMode.HeightWin && !SupportsHeightWin(m_heightMapBlendMode))
                {
                    Debug.Log($"[Vista] Texture blend mode Height Win falls back to Replace because height blend mode is '{m_heightMapBlendMode}'. Height Win is only meaningful with KeepHigher or KeepLower.");
                    m_textureBlendMode = TextureBlendMode.Replace;
                }
                if (m_populationBlendMode == PopulationBlendMode.HeightWin && !SupportsHeightWin(m_heightMapBlendMode))
                {
                    Debug.Log($"[Vista] Population blend mode Height Win falls back to Replace because height blend mode is '{m_heightMapBlendMode}'. Height Win is only meaningful with KeepHigher or KeepLower.");
                    m_populationBlendMode = PopulationBlendMode.Replace;
                }
            }
        }

        [SerializeField]
        private TextureBlendMode m_textureBlendMode;
        /// <summary>
        /// Gets or sets how terrain texture weights are chosen when biomes overlap.
        /// </summary>
        public TextureBlendMode textureBlendMode
        {
            get
            {
                return m_textureBlendMode;
            }
            set
            {
                if (value == TextureBlendMode.HeightWin && !SupportsHeightWin(m_heightMapBlendMode))
                {
                    Debug.Log($"[Vista] Texture blend mode Height Win falls back to Replace because height blend mode is '{m_heightMapBlendMode}'. Height Win is only meaningful with KeepHigher or KeepLower.");
                    m_textureBlendMode = TextureBlendMode.Replace;
                }
                else
                {
                    m_textureBlendMode = value;
                }
            }
        }

        [SerializeField]
        private MeshDensityBlendMode m_meshDensityBlendMode;
        /// <summary>
        /// Gets or sets the blend operator used for the biome mesh density map.
        /// </summary>
        /// <remarks>
        /// The mesh density map drives polygon density on Polaris terrain (written to the height map B channel).
        /// All blend modes are meaningful because density is a progressive value, not a binary flag.
        /// </remarks>
        public MeshDensityBlendMode meshDensityBlendMode
        {
            get
            {
                return m_meshDensityBlendMode;
            }
            set
            {
                m_meshDensityBlendMode = value;
            }
        }

        [SerializeField]
        private HoleBlendMode m_holeMapBlendMode;
        /// <summary>
        /// Gets or sets the blend operator used for the biome hole map.
        /// </summary>
        /// <remarks>
        /// Use <see cref="HoleBlendMode.Accumulate"/> when multiple instances of the same biome graph
        /// each punch a single hole at different locations — this prevents later instances from filling
        /// the holes produced by earlier ones.
        /// </remarks>
        public HoleBlendMode holeMapBlendMode
        {
            get
            {
                return m_holeMapBlendMode;
            }
            set
            {
                m_holeMapBlendMode = value;
            }
        }

        [SerializeField]
        private PopulationBlendMode m_populationBlendMode;
        /// <summary>
        /// Gets or sets how all population outputs are combined: detail density maps, detail instance buffers,
        /// tree buffers, object buffers, and generic buffers.
        /// </summary>
        /// <remarks>
        /// <see cref="PopulationBlendMode.Replace"/> makes each biome claim its mask region, eroding or fading
        /// out populations from earlier biomes in that region, so one ecosystem dominates per pixel.
        /// <see cref="PopulationBlendMode.Coexist"/> layers populations additively, so multiple ecosystems
        /// can share a pixel without suppressing each other.
        /// </remarks>
        public PopulationBlendMode populationBlendMode
        {
            get
            {
                return m_populationBlendMode;
            }
            set
            {
                if (value == PopulationBlendMode.HeightWin && !SupportsHeightWin(m_heightMapBlendMode))
                {
                    Debug.Log($"[Vista] Population blend mode Height Win falls back to Replace because height blend mode is '{m_heightMapBlendMode}'. Height Win is only meaningful with KeepHigher or KeepLower.");
                    m_populationBlendMode = PopulationBlendMode.Replace;
                }
                else
                {
                    m_populationBlendMode = value;
                }
            }
        }

        [SerializeField]
        private bool m_useTransformForHeightBlend;
        /// <summary>
        /// Gets or sets whether height blending should interpret this biome using its scene Y position and Y scale.
        /// </summary>
        /// <remarks>
        /// This affects only multi-biome height compositing. It does not change how an individual biome graph generates
        /// its own height output.
        /// </remarks>
        public bool useTransformForHeightBlend
        {
            get
            {
                return m_useTransformForHeightBlend;
            }
            set
            {
                m_useTransformForHeightBlend = value;
            }
        }

        private static bool SupportsHeightWin(HeightBlendMode heightBlendMode)
        {
            return heightBlendMode == HeightBlendMode.KeepHigher || heightBlendMode == HeightBlendMode.KeepLower;
        }

        /// <summary>
        /// Creates the default blend configuration used by newly reset local procedural biomes.
        /// </summary>
        /// <returns>
        /// A blend option set where height, mesh density, hole, texture, and population channels use their conservative
        /// default operators, and height blending interprets biome scene transform by default.
        /// </returns>
        /// <remarks>
        /// Existing serialized biomes that predate a newly added field keep Unity's default value for that field when
        /// deserialized. Newly created or reset biomes receive the values assigned here.
        /// </remarks>
        public static BiomeBlendOptions Default()
        {
            BiomeBlendOptions options = new BiomeBlendOptions();
            options.heightMapBlendMode = HeightBlendMode.Replace;
            options.meshDensityBlendMode = MeshDensityBlendMode.Replace;
            options.holeMapBlendMode = HoleBlendMode.Replace;
            options.textureBlendMode = TextureBlendMode.Replace;
            options.populationBlendMode = PopulationBlendMode.Replace;
            options.useTransformForHeightBlend = true;

            return options;
        }
    }
}
#endif


