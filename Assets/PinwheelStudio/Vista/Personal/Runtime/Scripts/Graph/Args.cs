#if VISTA
using UnityEngine;

namespace Pinwheel.Vista.Graph
{
    /// <summary>
    /// Stores one value in the graph execution argument table used by <see cref="GraphContext"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Terrain graph execution passes request-wide settings through a dictionary keyed by the
    /// constants in this type. Nodes then fetch those values with <c>context.GetArg(...)</c> while
    /// evaluating outputs.
    /// </para>
    /// <para>
    /// The struct works as a small untagged payload container rather than a strongly typed variant.
    /// Each key has an implied payload field, so producers and consumers must agree whether a given
    /// argument lives in <see cref="intValue"/>, <see cref="floatValue"/>,
    /// <see cref="vectorValue"/>, or <see cref="boolValue"/>.
    /// </para>
    /// </remarks>
    public struct Args
    {
        /// <summary>
        /// Key for the base resolution of the current graph request.
        /// </summary>
        /// <remarks>
        /// Stored in <see cref="intValue"/>. Most nodes use this as the default texture size for
        /// allocating transient render targets and compute buffers.
        /// </remarks>
        public const int RESOLUTION = 0;
        /// <summary>
        /// Key for the terrain-space bounds covered by the current graph request.
        /// </summary>
        /// <remarks>
        /// Stored in <see cref="vectorValue"/> as <c>(x, y, width, height)</c>. In Vista terrain
        /// generation this rectangle represents the request footprint on the horizontal terrain
        /// plane and is consumed by nodes that convert between normalized UVs and world-space
        /// sampling coordinates.
        /// </remarks>
        public const int WORLD_BOUNDS = 1;
        /// <summary>
        /// Key for the maximum terrain height of the destination terrain system.
        /// </summary>
        /// <remarks>
        /// Stored in <see cref="floatValue"/>. Height-related nodes use it to interpret normalized
        /// height data in world-space units.
        /// </remarks>
        public const int TERRAIN_HEIGHT = 2;
        /// <summary>
        /// Key for the base random seed of the current graph execution.
        /// </summary>
        /// <remarks>
        /// Stored in <see cref="intValue"/>. Noise, scattering, and other stochastic nodes derive
        /// repeatable variation from this seed.
        /// </remarks>
        public const int SEED = 3;
        /// <summary>
        /// Key for the flag that enables temporary height output generation.
        /// </summary>
        /// <remarks>
        /// Stored in <see cref="boolValue"/>. The graph pipeline uses this when intermediate height
        /// data must be preserved for later processing instead of only producing final terrain
        /// outputs.
        /// </remarks>
        public const int OUTPUT_TEMP_HEIGHT = 4;
        /// <summary>
        /// Key for the transform scale of the current Local Procedural Biome.
        /// </summary>
        /// <remarks>
        /// Stored in <see cref="vectorValue"/>. Biome-aware nodes use it when converting authored
        /// local-space data into the space used during tile generation.
        /// </remarks>
        public const int BIOME_SCALE = 5;
        /// <summary>
        /// Key for the coordinate space mode of the current Local Procedural Biome.
        /// </summary>
        /// <remarks>
        /// Stored in <see cref="intValue"/> as the numeric value of <see cref="Space"/>. This tells
        /// biome-aware nodes whether authored coordinates should be interpreted in local or world
        /// space.
        /// </remarks>
        public const int BIOME_SPACE = 6;
        /// <summary>
        /// Key for the world-space bounds of the current Local Procedural Biome cache area.
        /// </summary>
        /// <remarks>
        /// Stored in <see cref="vectorValue"/> as <c>(min.x, min.z, size.x, size.z)</c>. Unlike
        /// <see cref="WORLD_BOUNDS"/>, this rectangle describes the biome cache footprint supplied by
        /// <c>LPBInputProvider</c>, which allows nodes to evaluate against biome-local coverage while
        /// the result is later remapped into tile bounds.
        /// </remarks>
        public const int BIOME_WORLD_BOUNDS = 7;

        /// <summary>
        /// Stores the integer payload for keys that use discrete values.
        /// </summary>
        /// <remarks>
        /// Built-in uses include <see cref="RESOLUTION"/>, <see cref="SEED"/>, and
        /// <see cref="BIOME_SPACE"/>.
        /// </remarks>
        public int intValue { get; set; }
        /// <summary>
        /// Stores the floating-point payload for keys that use a scalar value.
        /// </summary>
        /// <remarks>
        /// The built-in terrain graph pipeline uses this field for <see cref="TERRAIN_HEIGHT"/>.
        /// </remarks>
        public float floatValue { get; set; }
        /// <summary>
        /// Stores the vector payload for keys that encode bounds or transform-related data.
        /// </summary>
        /// <remarks>
        /// Built-in uses include <see cref="WORLD_BOUNDS"/>, <see cref="BIOME_SCALE"/>, and
        /// <see cref="BIOME_WORLD_BOUNDS"/>.
        /// </remarks>
        public Vector4 vectorValue { get; set; }
        /// <summary>
        /// Stores the boolean payload for keys that act as execution flags.
        /// </summary>
        /// <remarks>
        /// The built-in terrain graph pipeline uses this field for
        /// <see cref="OUTPUT_TEMP_HEIGHT"/>.
        /// </remarks>
        public bool boolValue { get; set; }

        /// <summary>
        /// Creates an argument payload that stores an integer value.
        /// </summary>
        /// <param name="v">
        /// The value to place in <see cref="intValue"/> for the associated argument key.
        /// </param>
        /// <returns>
        /// A new <see cref="Args"/> instance with <see cref="intValue"/> initialized.
        /// </returns>
        public static Args Create(int v)
        {
            Args args = new Args();
            args.intValue = v;
            return args;
        }

        /// <summary>
        /// Creates an argument payload that stores a floating-point value.
        /// </summary>
        /// <param name="v">
        /// The value to place in <see cref="floatValue"/> for the associated argument key.
        /// </param>
        /// <returns>
        /// A new <see cref="Args"/> instance with <see cref="floatValue"/> initialized.
        /// </returns>
        public static Args Create(float v)
        {
            Args args = new Args();
            args.floatValue = v;
            return args;
        }

        /// <summary>
        /// Creates an argument payload that stores a vector value.
        /// </summary>
        /// <param name="v">
        /// The value to place in <see cref="vectorValue"/> for the associated argument key.
        /// </param>
        /// <returns>
        /// A new <see cref="Args"/> instance with <see cref="vectorValue"/> initialized.
        /// </returns>
        public static Args Create(Vector4 v)
        {
            Args args = new Args();
            args.vectorValue = v;
            return args;
        }

        /// <summary>
        /// Creates an argument payload that stores a boolean flag.
        /// </summary>
        /// <param name="v">
        /// The value to place in <see cref="boolValue"/> for the associated argument key.
        /// </param>
        /// <returns>
        /// A new <see cref="Args"/> instance with <see cref="boolValue"/> initialized.
        /// </returns>
        public static Args Create(bool v)
        {
            Args args = new Args();
            args.boolValue = v;
            return args;
        }
    }
}
#endif


