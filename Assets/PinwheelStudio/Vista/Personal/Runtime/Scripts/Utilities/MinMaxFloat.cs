#if VISTA
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;

namespace Pinwheel.Vista
{
    [System.Serializable]
    /// <summary>
    /// Stores a floating-point range as a minimum and maximum value.
    /// </summary>
    public struct MinMaxFloat : IEquatable<MinMaxFloat>
    {
        /// <summary>
        /// Predefined range that spans from <see cref="float.MinValue"/> to <see cref="float.MaxValue"/>.
        /// </summary>
        public static readonly MinMaxFloat FULL_RANGE = new MinMaxFloat(float.MinValue, float.MaxValue);

        [SerializeField]
        /// <summary>
        /// Lower bound of the range.
        /// </summary>
        public float min;
        [SerializeField]
        /// <summary>
        /// Upper bound of the range.
        /// </summary>
        public float max;

        /// <summary>
        /// Creates a range with the supplied minimum and maximum values.
        /// </summary>
        /// <param name="min">Lower bound of the range.</param>
        /// <param name="max">Upper bound of the range.</param>
        public MinMaxFloat(float min, float max)
        {
            this.min = min;
            this.max = max;
        }

        /// <summary>
        /// Compares two ranges using exact floating-point equality on both bounds.
        /// </summary>
        /// <param name="other">The range to compare against.</param>
        /// <returns><see langword="true"/> if both <see cref="min"/> and <see cref="max"/> match exactly.</returns>
        public bool Equals(MinMaxFloat other)
        {
            return this.min == other.min && this.max == other.max;
        }

        /// <summary>
        /// Compares two ranges using <see cref="Equals(MinMaxFloat)"/>.
        /// </summary>
        public static bool operator ==(MinMaxFloat a, MinMaxFloat b)
        {
            return a.Equals(b);
        }

        /// <summary>
        /// Compares two ranges using <see cref="Equals(MinMaxFloat)"/>.
        /// </summary>
        public static bool operator !=(MinMaxFloat a, MinMaxFloat b)
        {
            return !a.Equals(b);
        }

        /// <summary>
        /// Uses the default boxed-struct equality implementation.
        /// </summary>
        /// <param name="obj">Object to compare against.</param>
        /// <returns>The result returned by the base struct equality implementation.</returns>
        public override bool Equals(object obj)
        {
            return base.Equals(obj);
        }

        /// <summary>
        /// Returns a hash code built from both bounds.
        /// </summary>
        public override int GetHashCode()
        {
            return (min, max).GetHashCode();
        }
    }
}
#endif


