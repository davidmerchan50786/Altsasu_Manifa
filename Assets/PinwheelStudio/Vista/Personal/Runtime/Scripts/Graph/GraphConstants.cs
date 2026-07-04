#if VISTA

namespace Pinwheel.Vista.Graph
{
    /// <summary>
    /// Defines reserved graph input and output names used by Vista runtime integrations.
    /// </summary>
    /// <remarks>
    /// These names are not generic graph labels. They act as fixed handshake identifiers between
    /// graph assets and runtime systems such as <c>LPBInputProvider</c>,
    /// <c>BiomeMaskGraphUtilities</c>, and Local Procedural Biome generation.
    /// </remarks>
    public static class GraphConstants
    {
        /// <summary>
        /// Reserved input name for the combined biome mask supplied to graph execution.
        /// </summary>
        /// <remarks>
        /// <c>LPBInputProvider</c> binds the current Local Procedural Biome mask under this name, and
        /// biome-mask post-process graphs read it back through an input node that requests the same
        /// identifier.
        /// </remarks>
        public const string BIOME_MASK_INPUT_NAME = "Biome Mask Input";
        /// <summary>
        /// Reserved input name for the scene height map captured around a Local Procedural Biome.
        /// </summary>
        /// <remarks>
        /// When scene-height collection is enabled, <c>LPBInputProvider</c> exposes the generated
        /// scene height texture under this name so graph nodes can sample authored scene data.
        /// </remarks>
        public const string SCENE_HEIGHT_INPUT_NAME = "Scene Height";

        /// <summary>
        /// Reserved output name that identifies the final biome mask output in a biome mask graph.
        /// </summary>
        /// <remarks>
        /// <see cref="BiomeMaskGraphUtilities.RequestData(BiomeDataRequest, BiomeMaskGraph, UnityEngine.Bounds, UnityEngine.Space, UnityEngine.RenderTexture)"/>
        /// searches for an <c>OutputNode</c> with this exact name and only extracts that output from
        /// the executed graph.
        /// </remarks>
        public const string BIOME_MASK_OUTPUT_NAME = "Biome Mask Output";
    }
}
#endif


