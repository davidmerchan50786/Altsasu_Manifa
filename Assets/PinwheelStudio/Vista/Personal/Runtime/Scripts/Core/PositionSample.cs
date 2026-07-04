#if VISTA
using System;
using UnityEngine;

namespace Pinwheel.Vista
{
    [Serializable]
    /// <summary>
    /// Represents one packed point sample stored in Vista position buffers.
    /// </summary>
    /// <remarks>
    /// This struct is the point-only buffer layout used by Local Procedural Biome position inputs and by graph nodes that
    /// generate, transform, append, or filter point sets. Buffers that store this data are allocated with
    /// <see cref="SIZE"/> float elements per sample, so the field order must remain aligned with the compute shader layout.
    /// </remarks>
    public struct PositionSample : IEquatable<PositionSample>
    {
        /// <summary>
        /// The number of float elements used to store one <see cref="PositionSample"/> in a compute buffer.
        /// </summary>
        /// <remarks>
        /// This is a float-count constant, not a byte size. Vista buffer allocation and validation code multiplies sample
        /// count by this value when creating or checking point buffers.
        /// </remarks>
        public static readonly int SIZE = 4; //*sizeof(float)

        /// <summary>
        /// Indicates whether this slot contains a usable point.
        /// </summary>
        /// <remarks>
        /// A value less than or equal to zero is treated as empty by downstream consumers. The field is stored as a float
        /// because the sample layout is shared with compute-shader workflows.
        /// </remarks>
        public float isValid;
        /// <summary>
        /// Gets or sets the sample position carried through the point-processing graph.
        /// </summary>
        /// <remarks>
        /// The exact coordinate interpretation depends on the node or input that produced the sample, but the value is
        /// passed through buffers without additional metadata. For Local Procedural Biome position inputs, the sample is
        /// uploaded as authored and later consumed directly by graph nodes.
        /// </remarks>
        public Vector3 position;

        /// <summary>
        /// Compares this sample with another sample using exact field equality.
        /// </summary>
        /// <param name="other">The sample to compare against.</param>
        /// <returns>
        /// <see langword="true"/> when both the validity flag and position match exactly; otherwise, <see langword="false"/>.
        /// </returns>
        /// <remarks>
        /// This method performs direct float comparisons and does not apply any tolerance.
        /// </remarks>
        public bool Equals(PositionSample other)
        {
            return this.isValid == other.isValid && this.position == other.position;
        }
    }
}
#endif


