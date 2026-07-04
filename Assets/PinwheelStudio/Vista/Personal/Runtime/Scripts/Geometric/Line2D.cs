#if VISTA
using System;
using UnityEngine;

namespace Pinwheel.Vista.Geometric
{
    /// <summary>
    /// Represents a finite 2D line segment.
    /// </summary>
    /// <remarks>
    /// Even though some helper methods derive an infinite line equation from the segment endpoints, intersection tests in
    /// this type treat the segment as bounded by <see cref="startPoint"/> and <see cref="endPoint"/>.
    /// </remarks>
    public struct Line2D : IEquatable<Line2D>
    {
        /// <summary>
        /// Gets or sets the segment start point.
        /// </summary>
        public Vector2 startPoint { get; set; }
        /// <summary>
        /// Gets or sets the segment end point.
        /// </summary>
        public Vector2 endPoint { get; set; }

        /// <summary>
        /// Gets the midpoint of the segment.
        /// </summary>
        public Vector2 Center
        {
            get
            {
                return startPoint * 0.5f + endPoint * 0.5f;
            }
        }

        /// <summary>
        /// Gets the normalized direction from <see cref="startPoint"/> to <see cref="endPoint"/>.
        /// </summary>
        public Vector2 Direction
        {
            get
            {
                return (endPoint - startPoint).normalized;
            }
        }

        /// <summary>
        /// Gets the segment length.
        /// </summary>
        public float Length
        {
            get
            {
                return (startPoint - endPoint).magnitude;
            }
        }

        /// <summary>
        /// Gets the squared segment length.
        /// </summary>
        public float SqrLength
        {
            get
            {
                return (startPoint - endPoint).sqrMagnitude;
            }
        }

        /// <summary>
        /// Creates a segment from two endpoints.
        /// </summary>
        /// <param name="start">The segment start point.</param>
        /// <param name="end">The segment end point.</param>
        public Line2D(Vector2 start, Vector2 end)
        {
            startPoint = start;
            endPoint = end;
        }

        /// <summary>
        /// Creates a segment from four coordinates.
        /// </summary>
        /// <param name="x1">The X coordinate of the start point.</param>
        /// <param name="y1">The Y coordinate of the start point.</param>
        /// <param name="x2">The X coordinate of the end point.</param>
        /// <param name="y2">The Y coordinate of the end point.</param>
        public Line2D(float x1, float y1, float x2, float y2)
        {
            startPoint = new Vector2(x1, y1);
            endPoint = new Vector2(x2, y2);
        }

        /// <summary>
        /// Solves the infinite line equation for X at a given Y.
        /// </summary>
        /// <param name="y">The Y value to evaluate.</param>
        /// <returns>The corresponding X value on the infinite line through this segment.</returns>
        /// <remarks>
        /// This method does not clamp the result to the finite segment and may divide by zero for horizontal lines.
        /// </remarks>
        public float GetX(float y)
        {
            Vector2 dir = endPoint - startPoint;
            float a = -dir.y;
            float b = dir.x;
            float c = -(a * startPoint.x + b * startPoint.y);
            float x = (-b * y - c) / a;
            return x;
        }

        /// <summary>
        /// Solves the infinite line equation for Y at a given X.
        /// </summary>
        /// <param name="x">The X value to evaluate.</param>
        /// <returns>The corresponding Y value on the infinite line through this segment.</returns>
        /// <remarks>
        /// This method does not clamp the result to the finite segment and may divide by zero for vertical lines.
        /// </remarks>
        public float GetY(float x)
        {
            Vector2 dir = endPoint - startPoint;
            float a = -dir.y;
            float b = dir.x;
            float c = -(a * startPoint.x + b * startPoint.y);
            float y = (-a * x - c) / b;
            return y;
        }

        /// <summary>
        /// Tests whether two finite segments intersect and returns the intersection point when they do.
        /// </summary>
        /// <param name="l1">The first segment.</param>
        /// <param name="l2">The second segment.</param>
        /// <param name="point">The intersection point when the segments intersect; otherwise an unspecified value.</param>
        /// <returns>
        /// <see langword="true"/> when the segments intersect within both finite segment extents; otherwise,
        /// <see langword="false"/>.
        /// </returns>
        /// <remarks>
        /// Parallel or coincident lines are reported as non-intersecting by this method. The extent test uses squared
        /// distances from the computed intersection point to each endpoint rather than parametric coordinates.
        /// </remarks>
        public static bool Intersect(Line2D l1, Line2D l2, out Vector2 point)
        {
            bool result = false;
            float x1 = l1.startPoint.x;
            float x2 = l1.endPoint.x;
            float x3 = l2.startPoint.x;
            float x4 = l2.endPoint.x;
            float y1 = l1.startPoint.y;
            float y2 = l1.endPoint.y;
            float y3 = l2.startPoint.y;
            float y4 = l2.endPoint.y;

            float denominator = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4);
            if (denominator == 0)
            {
                point = new Vector2(0, 0);
                result = false;
            }
            else
            {
                float xNumerator = (x1 * y2 - y1 * x2) * (x3 - x4) - (x1 - x2) * (x3 * y4 - y3 * x4);
                float yNumerator = (x1 * y2 - y1 * x2) * (y3 - y4) - (y1 - y2) * (x3 * y4 - y3 * x4);
                point = new Vector2(xNumerator / denominator, yNumerator / denominator);
                float sqrLength1 = l1.SqrLength;
                float sqrLength2 = l2.SqrLength;
                if ((point - l1.startPoint).sqrMagnitude > sqrLength1 || (point - l1.endPoint).sqrMagnitude > sqrLength1)
                {
                    result = false;
                }
                else if ((point - l2.startPoint).sqrMagnitude > sqrLength2 || (point - l2.endPoint).sqrMagnitude > sqrLength2)
                {
                    result = false;
                }
                else
                {
                    result = true;
                }
            }

            return result;
        }

        /// <summary>
        /// Tests whether two segments represent the same endpoints, ignoring direction.
        /// </summary>
        /// <param name="other">The segment to compare against.</param>
        /// <returns>
        /// <see langword="true"/> when both endpoints match in either forward or reversed order; otherwise,
        /// <see langword="false"/>.
        /// </returns>
        /// <remarks>
        /// The comparison first uses <see cref="Mathf.Approximately(float, float)"/> on component values and then falls back
        /// to exact vector equality for the forward direction.
        /// </remarks>
        public bool Equals(Line2D other)
        {
            if (Mathf.Approximately(this.startPoint.x, other.startPoint.x) &&
                Mathf.Approximately(this.startPoint.y, other.startPoint.y) &&
                Mathf.Approximately(this.endPoint.x, other.endPoint.x) &&
                Mathf.Approximately(this.endPoint.y, other.endPoint.y))
                return true;

            if (Mathf.Approximately(this.startPoint.x, other.endPoint.x) &&
                Mathf.Approximately(this.startPoint.y, other.endPoint.y) &&
                Mathf.Approximately(this.endPoint.x, other.startPoint.x) &&
                Mathf.Approximately(this.endPoint.y, other.startPoint.y))
                return true;

            if (this.startPoint == other.startPoint &&
                this.endPoint == other.endPoint)
                return true;

            return false;
        }
    }
}
#endif


