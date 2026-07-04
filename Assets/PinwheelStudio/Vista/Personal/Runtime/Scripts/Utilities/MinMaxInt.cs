#if VISTA
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace Pinwheel.Vista
{
    [System.Serializable]
    /// <summary>
    /// Stores an integer range as a minimum and maximum value.
    /// </summary>
    public struct MinMaxInt
    {
        /// <summary>
        /// Predefined range that spans from <see cref="int.MinValue"/> to <see cref="int.MaxValue"/>.
        /// </summary>
        public static readonly MinMaxInt FULL_RANGE = new MinMaxInt(int.MinValue, int.MaxValue);

        [SerializeField]
        /// <summary>
        /// Lower bound of the range.
        /// </summary>
        public int min;
        [SerializeField]
        /// <summary>
        /// Upper bound of the range.
        /// </summary>
        public int max;

        /// <summary>
        /// Creates a range with the supplied minimum and maximum values.
        /// </summary>
        /// <param name="min">Lower bound of the range.</param>
        /// <param name="max">Upper bound of the range.</param>
        public MinMaxInt(int min, int max)
        {
            this.min = min;
            this.max = max;
        }

        /// <summary>
        /// Compares two ranges by checking both bounds for exact equality.
        /// </summary>
        /// <param name="other">The range to compare against.</param>
        /// <returns><see langword="true"/> if both <see cref="min"/> and <see cref="max"/> are equal.</returns>
        public bool Equals(MinMaxInt other)
        {
            return this.min == other.min && this.max == other.max;
        }

        /// <summary>
        /// Compares two ranges using <see cref="Equals(MinMaxInt)"/>.
        /// </summary>
        public static bool operator ==(MinMaxInt a, MinMaxInt b)
        {
            return a.Equals(b);
        }

        /// <summary>
        /// Compares two ranges using <see cref="Equals(MinMaxInt)"/>.
        /// </summary>
        public static bool operator !=(MinMaxInt a, MinMaxInt b)
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


