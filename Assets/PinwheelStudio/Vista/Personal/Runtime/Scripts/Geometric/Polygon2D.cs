#if VISTA
using UnityEngine;

namespace Pinwheel.Vista.Geometric
{
    /// <summary>
    /// Represents a simple 2D polygon defined by an ordered vertex loop.
    /// </summary>
    /// <remarks>
    /// This type is used by Local Procedural Biome overlap tests. It stores vertices as supplied and does not enforce
    /// winding order or convexity.
    /// </remarks>
    public struct Polygon2D
    {
        /// <summary>
        /// Gets or sets the polygon vertices in loop order.
        /// </summary>
        public Vector2[] vertices { get; set; }

        /// <summary>
        /// Creates a polygon from an ordered vertex array.
        /// </summary>
        /// <param name="v">The polygon vertices in loop order.</param>
        /// <exception cref="System.ArgumentException">
        /// Thrown when fewer than three vertices are supplied.
        /// </exception>
        public Polygon2D(Vector2[] v)
        {
            if (v.Length < 3)
                throw new System.ArgumentException("A polygon must has at least 3 vertices");
            vertices = v;
        }

        /// <summary>
        /// Tests whether a point lies inside the polygon or on one of its vertices or edges.
        /// </summary>
        /// <param name="p">The point to test.</param>
        /// <returns><see langword="true"/> when the point is inside or on the polygon boundary; otherwise, <see langword="false"/>.</returns>
        /// <remarks>
        /// The method uses a horizontal ray cast from just left of the polygon's minimum X bound and counts segment
        /// intersections. Exact vertex matches are treated as contained immediately.
        /// </remarks>
        public bool Contains(Vector2 p)
        {
            for (int i = 0; i < vertices.Length; ++i)
            {
                if (vertices[i] == p)
                {
                    return true;
                }
            }

            Vector2 lineStart = new Vector2(GetMinX() - 1, p.y);
            Vector2 lineEnd = p;
            Line2D hLine = new Line2D(lineStart, lineEnd);

            Line2D[] segments = GetSegments();
            Vector2 intersection;
            bool isIntersect;
            int intersectCount = 0;
            for (int i = 0; i < segments.Length; ++i)
            {
                isIntersect = Line2D.Intersect(hLine, segments[i], out intersection);
                intersectCount += isIntersect ? 1 : 0;
                if (isIntersect && intersection == p)
                {
                    return true;
                }
            }
            return intersectCount % 2 != 0;
        }

        private float GetMinX()
        {
            float minX = float.MaxValue;
            foreach (Vector2 v in vertices)
            {
                minX = Mathf.Min(minX, v.x);
            }
            return minX;
        }

        /// <summary>
        /// Gets the polygon edge segments in loop order.
        /// </summary>
        /// <returns>An array of segments connecting each vertex to the previous vertex, with the first edge closing the loop.</returns>
        public Line2D[] GetSegments()
        {
            Line2D[] segments = new Line2D[vertices.Length];
            segments[0] = new Line2D(vertices[0], vertices[vertices.Length - 1]);
            for (int i = 1; i < vertices.Length; ++i)
            {
                segments[i] = new Line2D(vertices[i], vertices[i - 1]);
            }
            return segments;
        }

        /// <summary>
        /// Tests whether two polygons overlap.
        /// </summary>
        /// <param name="polygon0">The first polygon.</param>
        /// <param name="polygon1">The second polygon.</param>
        /// <returns><see langword="true"/> when the polygons overlap or touch; otherwise, <see langword="false"/>.</returns>
        /// <remarks>
        /// Overlap is detected by checking whether either polygon contains a vertex of the other, then by testing every
        /// edge pair for segment intersection.
        /// </remarks>
        public static bool IsOverlap(Polygon2D polygon0, Polygon2D polygon1)
        {
            foreach (Vector2 v in polygon0.vertices)
            {
                if (polygon1.Contains(v))
                    return true;
            }

            foreach (Vector2 v in polygon1.vertices)
            {
                if (polygon0.Contains(v))
                    return true;
            }

            Line2D[] segments0 = polygon0.GetSegments();
            Line2D[] segments1 = polygon1.GetSegments();
            Vector2 intersection;
            for (int i = 0; i < segments0.Length; ++i)
            {
                for (int j = 0; j < segments1.Length; ++j)
                {
                    if (Line2D.Intersect(segments0[i], segments1[j], out intersection))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
#endif


