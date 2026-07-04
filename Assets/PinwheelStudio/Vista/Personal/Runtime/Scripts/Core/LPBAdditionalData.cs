#if VISTA
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Pinwheel.Vista;
using Pinwheel.Vista.Graphics;
using Pinwheel.Vista.Geometric;
using System.Linq;

namespace Pinwheel.Vista
{
    [AddComponentMenu("")]
    /// <summary>
    /// Stores auxiliary shape data for a Local Procedural Biome that is authored as a hexagon path.
    /// </summary>
    /// <remarks>
    /// LPB stands for <see cref="LocalProceduralBiome"/>. This component keeps the editable hex-walk description separate
    /// from the biome itself, then reconstructs the visited hex cells or their outer contour on demand.
    /// </remarks>
    public class LPBAdditionalData : MonoBehaviour
    {
        [SerializeField]
        private List<int> m_hexagonTrace = new List<int>();

        [SerializeField]
        private float m_hexagonRadius;
        /// <summary>
        /// Gets or sets the radius used when reconstructing the hexagon chain.
        /// </summary>
        /// <remarks>
        /// The value is used as the radius of every generated <see cref="Hexagon2D"/> cell and as the tolerance scale for
        /// contour stitching and duplicate detection.
        /// </remarks>
        public float hexagonRadius
        {
            get
            {
                return m_hexagonRadius;
            }
            set
            {
                m_hexagonRadius = value;
            }
        }

        [SerializeField]
        private Hexagon2D.Orientation m_hexagonOrientation;
        /// <summary>
        /// Gets or sets the orientation of the authored hex grid.
        /// </summary>
        /// <remarks>
        /// This value determines how direction indices in the stored hex trace are interpreted and how the nearest grid
        /// point is computed by <see cref="FindNearestPointOnHexGrid(Vector2, float, Hexagon2D.Orientation)"/>.
        /// </remarks>
        public Hexagon2D.Orientation hexagonOrientation
        {
            get
            {
                return m_hexagonOrientation;
            }
            set
            {
                m_hexagonOrientation = value;
            }
        }

        private void Reset()
        {
            m_hexagonRadius = 100;
        }

        /// <summary>
        /// Appends one step to the stored hex-walk trace.
        /// </summary>
        /// <param name="direction">
        /// The side index to walk across from the current hexagon. Valid values are 0 through 5.
        /// </param>
        /// <remarks>
        /// The trace always starts from a hex centered at the local origin. Each stored direction moves to the adjacent
        /// hexagon that shares the corresponding segment of the current cell.
        /// </remarks>
        public void AddHexTrace(int direction)
        {
            Debug.Assert(direction >= 0 && direction < 6);
            m_hexagonTrace.Add(direction);
        }

        /// <summary>
        /// Removes the last recorded step from the hex trace.
        /// </summary>
        /// <remarks>
        /// If the trace is already empty, the method does nothing.
        /// </remarks>
        public void RemoveLastHexagon()
        {
            if (m_hexagonTrace.Count > 0)
            {
                m_hexagonTrace.RemoveAt(m_hexagonTrace.Count - 1);
            }
        }

        /// <summary>
        /// Gets the number of recorded moves in the hex trace.
        /// </summary>
        /// <returns>The number of direction entries currently stored in the trace.</returns>
        public int GetHexagonTraceCount()
        {
            return m_hexagonTrace.Count;
        }

        /// <summary>
        /// Clears all recorded trace steps.
        /// </summary>
        /// <remarks>
        /// After clearing, reconstructed geometry contains only the initial origin hexagon.
        /// </remarks>
        public void ClearHexagons()
        {
            m_hexagonTrace.Clear();
        }

        /// <summary>
        /// Reconstructs the hexagon cells described by the current trace.
        /// </summary>
        /// <param name="makeUnique">
        /// When <see langword="true"/>, revisited cells are omitted from the returned list; otherwise, the returned sequence
        /// preserves the authored walk, including repeated visits.
        /// </param>
        /// <returns>
        /// A list beginning with the origin hexagon, followed by each walked-to cell reconstructed from the stored
        /// directions.
        /// </returns>
        /// <remarks>
        /// Uniqueness is determined by approximate center distance, not exact structural equality. Two cells are considered
        /// the same when their centers are closer than one hex radius.
        /// </remarks>
        public List<Hexagon2D> GetHexagons(bool makeUnique = false)
        {
            List<Hexagon2D> hexagons = new List<Hexagon2D>();
            Hexagon2D hex = new Hexagon2D(Vector2.zero, m_hexagonRadius, m_hexagonOrientation);
            hexagons.Add(hex);

            CustomHexComparer comp = new CustomHexComparer();

            foreach (int t in m_hexagonTrace)
            {
                Line2D segment = hex.GetSegment(t);
                Vector2 segmentCenter = segment.startPoint * 0.5f + segment.endPoint * 0.5f;
                Vector2 nextHexCenter = 2 * segmentCenter - hex.center;
                hex = new Hexagon2D(nextHexCenter, m_hexagonRadius, m_hexagonOrientation);
                if (makeUnique)
                {
                    if (hexagons.Exists(h => comp.Equals(hex, h)))
                    {
                        continue;
                    }
                }
                hexagons.Add(hex);
            }

            return hexagons;
        }

        private struct CustomHexComparer : IEqualityComparer<Hexagon2D>
        {
            /// <summary>
            /// Tests whether two hexagons should be treated as the same authored cell.
            /// </summary>
            /// <param name="x">The first hexagon.</param>
            /// <param name="y">The second hexagon.</param>
            /// <returns>
            /// <see langword="true"/> when the hexagon centers are closer than one radius; otherwise, <see langword="false"/>.
            /// </returns>
            /// <remarks>
            /// This comparer is intentionally tolerant and is used only to collapse revisited cells when building unique
            /// hexagon lists.
            /// </remarks>
            public bool Equals(Hexagon2D x, Hexagon2D y)
            {
                return Vector2.Distance(x.center, y.center) < x.radius;
            }

            /// <summary>
            /// Returns the hash code of the supplied hexagon.
            /// </summary>
            /// <param name="obj">The hexagon whose hash code should be returned.</param>
            /// <returns>The hash code produced by <paramref name="obj"/>.</returns>
            public int GetHashCode(Hexagon2D obj)
            {
                return obj.GetHashCode();
            }
        }

        /// <summary>
        /// Generates the outer contour of the unique authored hexagon cluster.
        /// </summary>
        /// <returns>
        /// A list of contour vertices ordered from the stitched boundary segments of the reconstructed hex cells.
        /// </returns>
        /// <remarks>
        /// The method removes shared internal edges by counting overlapping segment centers, then attempts to stitch the
        /// remaining boundary segments into a continuous outline. The output is intended for shape reconstruction rather
        /// than exact computational-geometry guarantees.
        /// </remarks>
        public List<Vector2> GenerateHexContour()
        {
            List<Hexagon2D> uniqueHex = GetHexagons(true);

            List<Line2D> allSegments = new List<Line2D>();
            Line2D[] tmpSegments = new Line2D[6];
            foreach (Hexagon2D h in uniqueHex)
            {
                h.GetSegments(tmpSegments);
                allSegments.AddRange(tmpSegments);
            }

            int[] counts = new int[allSegments.Count];
            for (int i = 0; i < allSegments.Count; ++i)
            {
                Line2D s0 = allSegments[i];
                Vector2 s0c = s0.Center;
                int count = 0;
                for (int j = 0; j < allSegments.Count; ++j)
                {
                    Line2D s1 = allSegments[j];
                    Vector2 s1c = s1.Center;
                    if (Vector2.Distance(s0c, s1c) < m_hexagonRadius * 0.1f)
                        count += 1;
                }
                counts[i] = count;
            }

            List<Line2D> nonOverlappedSegment = new List<Line2D>();
            for (int i = 0; i < allSegments.Count; ++i)
            {
                if (counts[i] == 1)
                    nonOverlappedSegment.Add(allSegments[i]);
            }

            for (int i = 0; i < nonOverlappedSegment.Count - 1; ++i)
            {
                Line2D s0 = nonOverlappedSegment[i];
                for (int j = i + 1; j < nonOverlappedSegment.Count; ++j)
                {
                    Line2D s1 = nonOverlappedSegment[j];
                    if (Vector2.Distance(s0.endPoint, s1.startPoint) < m_hexagonRadius * 0.1f)
                    {
                        Line2D tmp = nonOverlappedSegment[i + 1];
                        nonOverlappedSegment[i + 1] = nonOverlappedSegment[j];
                        nonOverlappedSegment[j] = tmp;
                    }
                }
            }

            List<Vector2> contour = new List<Vector2>();
            for (int i = nonOverlappedSegment.Count - 1; i >= 0; --i)
            {
                Line2D s = nonOverlappedSegment[i];
                contour.Add(s.startPoint);
            }

            return contour;
        }

        /// <summary>
        /// Finds the nearest snapped center position on a hex grid for an arbitrary point.
        /// </summary>
        /// <param name="p">The point to snap.</param>
        /// <param name="hexRadius">The radius of the target hex grid cells.</param>
        /// <param name="hexOrientation">The orientation of the target hex grid.</param>
        /// <returns>The closest candidate center among the four neighboring snap solutions sampled around <paramref name="p"/>.</returns>
        /// <remarks>
        /// The method samples four offset positions around the query point, snaps each one independently, then returns the
        /// closest result. This avoids some orientation-dependent edge cases that arise when snapping directly once.
        /// </remarks>
        public static Vector2 FindNearestPointOnHexGrid(Vector2 p, float hexRadius, Hexagon2D.Orientation hexOrientation)
        {
            Vector2[] hx = new Vector2[4];
            hx[0] = new Vector2(p.x - hexRadius * 0.5f, p.y);
            hx[0] = LPBAdditionalData.FindNearestPointOnHexGridInternal(hx[0], hexRadius, hexOrientation);
            hx[1] = new Vector2(p.x + hexRadius * 0.5f, p.y);
            hx[1] = LPBAdditionalData.FindNearestPointOnHexGridInternal(hx[1], hexRadius, hexOrientation);
            hx[2] = new Vector2(p.x, p.y - hexRadius * 0.5f);
            hx[2] = LPBAdditionalData.FindNearestPointOnHexGridInternal(hx[2], hexRadius, hexOrientation);
            hx[3] = new Vector2(p.x, p.y + hexRadius * 0.5f);
            hx[3] = LPBAdditionalData.FindNearestPointOnHexGridInternal(hx[3], hexRadius, hexOrientation);

            float dMin = float.MaxValue;
            Vector2 hexPoint = new Vector2();
            for (int i = 0; i < 4; ++i)
            {
                float sqrMag = Vector2.SqrMagnitude(p - hx[i]);
                if (sqrMag < dMin)
                {
                    dMin = sqrMag;
                    hexPoint = hx[i];
                }
            }
            return hexPoint;
        }

        private static Vector2 FindNearestPointOnHexGridInternal(Vector2 p, float hexRadius, Hexagon2D.Orientation hexOrientation)
        {
            if (hexOrientation == Hexagon2D.Orientation.Right)
            {
                float s = Mathf.Sqrt(3.0f) * 0.5f;
                float fy = Mathf.Round(p.y / (hexRadius * s));

                float px = p.x + (fy % 2 != 0 ? hexRadius * 1.5f : 0);
                float fx = Mathf.Round(px / (hexRadius * 3.0f));
                float offsetX = fy % 2 != 0 ? -hexRadius * 1.5f : 0;

                float y = fy * hexRadius * s;
                float x = fx * hexRadius * 3.0f + offsetX;

                return new Vector2(x, y);
            }
            else
            {
                float s = Mathf.Sqrt(3.0f) * 0.5f;
                float fx = Mathf.Round(p.x / (hexRadius * s));

                float py = p.y + (fx % 2 != 0 ? hexRadius * 1.5f : 0);
                float fy = Mathf.Round(py / (hexRadius * 3.0f));
                float offsetY = fx % 2 != 0 ? -hexRadius * 1.5f : 0;

                float x = fx * hexRadius * s;
                float y = fy * hexRadius * 3.0f + offsetY;

                return new Vector2(x, y);
            }

        }
    }
}
#endif


