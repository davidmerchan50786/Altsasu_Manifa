#if VISTA
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;

namespace Pinwheel.Vista.Geometric
{
    [System.Serializable]
    /// <summary>
    /// Represents a regular hexagon in 2D space.
    /// </summary>
    /// <remarks>
    /// This type is used mainly by Local Procedural Biome hex-layout tooling. The hexagon is defined by a center, a
    /// circumradius, and an orientation that decides whether a vertex points to the right or upward.
    /// </remarks>
    public struct Hexagon2D 
    {
        [SerializeField]
        private Vector2 m_center;
        /// <summary>
        /// Gets or sets the center of the hexagon.
        /// </summary>
        public Vector2 center
        {
            get
            {
                return m_center;
            }
            set
            {
                m_center = value;
            }
        }

        [SerializeField]
        private float m_radius;
        /// <summary>
        /// Gets or sets the circumradius of the hexagon.
        /// </summary>
        /// <remarks>
        /// Values below zero are rejected through a debug assertion.
        /// </remarks>
        public float radius
        {
            get
            {
                return m_radius;
            }
            set
            {
                Debug.Assert(value >= 0);
                m_radius = value;
            }
        }

        /// <summary>
        /// Describes which axis the hexagon is visually aligned to.
        /// </summary>
        public enum Orientation { Right, Top }

        [SerializeField]
        private Orientation m_orientation;
        /// <summary>
        /// Gets or sets the orientation used to place the first vertex.
        /// </summary>
        public Orientation orientation
        {
            get
            {
                return m_orientation;
            }
            set
            {
                m_orientation = value;
            }
        }

        /// <summary>
        /// Creates a regular hexagon from center, radius, and orientation.
        /// </summary>
        /// <param name="center">The center of the hexagon.</param>
        /// <param name="radius">The circumradius of the hexagon.</param>
        /// <param name="orientation">The orientation used to place the first vertex.</param>
        public Hexagon2D(Vector2 center, float radius, Orientation orientation)
        {
            Debug.Assert(radius >= 0);
            m_center = center;
            m_radius = radius;
            m_orientation = orientation;
        }

        /// <summary>
        /// Gets one vertex of the hexagon.
        /// </summary>
        /// <param name="index">The zero-based vertex index in the range 0 to 5.</param>
        /// <returns>The requested vertex position.</returns>
        /// <remarks>
        /// Vertex order advances in 60-degree steps starting from 0 degrees for <see cref="Orientation.Right"/> and
        /// -30 degrees for <see cref="Orientation.Top"/>.
        /// </remarks>
        public Vector2 GetPoint(int index)
        {
            Debug.Assert(index >= 0 && index < 6);
            float startAngle = m_orientation == Orientation.Right ? 0 : -30;
            float angle = startAngle + index * 60;
            float rad = angle * Mathf.Deg2Rad;
            Vector2 p = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * m_radius + center;
            return p;
        }

        /// <summary>
        /// Writes all six vertices into a caller-provided array.
        /// </summary>
        /// <param name="points">The destination array.</param>
        /// <param name="writeStartIndex">The index at which writing begins.</param>
        /// <remarks>
        /// The destination array must have room for six entries starting at <paramref name="writeStartIndex"/>.
        /// </remarks>
        public void GetPoints(Vector2[] points, int writeStartIndex = 0)
        {
            Debug.Assert(points != null && points.Length >= 6);
            for (int i = 0; i < 6; ++i)
            {
                points[i + writeStartIndex] = GetPoint(i);
            }
        }

        /// <summary>
        /// Gets one edge segment of the hexagon.
        /// </summary>
        /// <param name="index">The zero-based edge index in the range 0 to 5.</param>
        /// <returns>The segment from the indexed vertex to the next vertex, wrapping on the last edge.</returns>
        public Line2D GetSegment(int index)
        {
            Debug.Assert(index >= 0 && index < 6);
            if (index < 5)
            {
                Vector2 start = GetPoint(index);
                Vector2 end = GetPoint(index + 1);
                return new Line2D(start, end);
            }
            else
            {
                Vector2 start = GetPoint(5);
                Vector2 end = GetPoint(0);
                return new Line2D(start, end);
            }
        }

        /// <summary>
        /// Writes all six edge segments into a caller-provided array.
        /// </summary>
        /// <param name="segments">The destination array.</param>
        /// <param name="writeStartIndex">The index at which writing begins.</param>
        public void GetSegments(Line2D[] segments, int writeStartIndex = 0)
        {
            Debug.Assert(segments != null && segments.Length >= 6);
            for (int i = 0; i < 6; ++i)
            {
                segments[i + writeStartIndex] = GetSegment(i);
            }
        }

        /// <summary>
        /// Returns a hash code derived from center, radius, and orientation.
        /// </summary>
        /// <returns>A hash code for this hexagon value.</returns>
        public override int GetHashCode()
        {
            return center.GetHashCode() ^ radius.GetHashCode() ^ orientation.GetHashCode();
        }
    }
}
#endif


