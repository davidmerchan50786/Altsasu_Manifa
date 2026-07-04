#if VISTA
using System;
using UnityEngine;

namespace Pinwheel.Vista
{
    [Serializable]
    /// <summary>
    /// Represents one generated instance sample stored in Vista's instance buffers.
    /// </summary>
    /// <remarks>
    /// The struct is used as a packed GPU/CPU transfer layout for detail-instance and tree-instance outputs. Buffers that
    /// store this data are allocated with <see cref="SIZE"/> float elements per sample, then read back by terrain-system
    /// populators and utility parsers. The field order therefore matters and should stay aligned with the compute shader
    /// layout used by instance output nodes.
    /// </remarks>
    public struct InstanceSample : IEquatable<InstanceSample>
    {
        /// <summary>
        /// The number of float elements used to store one <see cref="InstanceSample"/> in a compute buffer.
        /// </summary>
        /// <remarks>
        /// This is a float-count constant, not a byte size. Code that allocates or validates instance buffers multiplies
        /// sample count by this value when working with Vista's float-based buffer descriptors.
        /// </remarks>
        public static readonly int SIZE = 7; //*sizeof(float)

        /// <summary>
        /// Indicates whether this slot contains a usable instance.
        /// </summary>
        /// <remarks>
        /// A value less than or equal to zero is treated as empty by consumers and skipped during population. The field is
        /// stored as a float because the sample layout is written by compute shaders.
        /// </remarks>
        public float isValid;
        /// <summary>
        /// Gets or sets the normalized instance position within the tile.
        /// </summary>
        /// <remarks>
        /// X and Z are interpreted in normalized tile space and later remapped to terrain-local coordinates by the target
        /// terrain system. Y is typically generated as zero in the graph and replaced by terrain height sampling when the
        /// instance is spawned.
        /// </remarks>
        public Vector3 position;
        /// <summary>
        /// Gets or sets the per-instance scale multiplier applied on the vertical axis.
        /// </summary>
        public float verticalScale;
        /// <summary>
        /// Gets or sets the per-instance scale multiplier applied on the horizontal axes.
        /// </summary>
        /// <remarks>
        /// Consumers typically apply this value to both X and Z so the footprint stays uniform.
        /// </remarks>
        public float horizontalScale;
        /// <summary>
        /// Gets or sets the rotation around the up axis.
        /// </summary>
        /// <remarks>
        /// Instance output nodes write this value in radians. Some consumers convert it to degrees before creating Unity
        /// transforms, so changing the stored unit would break existing parsers and tile populators.
        /// </remarks>
        public float rotationY;

        /// <summary>
        /// Compares this sample with another sample using exact field equality.
        /// </summary>
        /// <param name="other">The sample to compare against.</param>
        /// <returns>
        /// <see langword="true"/> when all fields match exactly; otherwise, <see langword="false"/>.
        /// </returns>
        /// <remarks>
        /// This method performs direct float comparisons with no tolerance, which is suitable for serialized or copied
        /// sample data but may be too strict for numerically unstable workflows.
        /// </remarks>
        public bool Equals(InstanceSample other)
        {
            return this.isValid == other.isValid &&
                this.position == other.position &&
                this.verticalScale == other.verticalScale &&
                this.horizontalScale == other.horizontalScale &&
                this.rotationY == other.rotationY;
        }
    }
}
#endif


