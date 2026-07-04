#if VISTA
using System.Collections.Generic;
using UnityEngine;

namespace Pinwheel.Vista
{
    /// <summary>
    /// Provides polygon-anchor helpers used by Local Procedural Biome authoring.
    /// </summary>
    /// <remarks>
    /// These helpers operate on the ordered anchor arrays that define biome polygons and their falloff
    /// outlines. They are primarily used to insert or remove anchors, derive falloff polygons, and
    /// align authored points to world-space terrain geometry.
    /// </remarks>
    public static class AnchorUtilities
    {
        /// <summary>
        /// Inserts a new anchor into an ordered anchor loop at the segment whose midpoint is closest to the new point.
        /// </summary>
        /// <param name="srcAnchors">
        /// Source anchor loop to copy and extend.
        /// </param>
        /// <param name="newAnchor">
        /// Anchor to insert.
        /// </param>
        /// <returns>
        /// A new anchor array containing <paramref name="newAnchor"/> at the computed insertion point.
        /// </returns>
        /// <remarks>
        /// For loops with fewer than two anchors, the new anchor is appended because there is not yet a
        /// meaningful segment to split.
        /// </remarks>
        public static Vector3[] Insert(Vector3[] srcAnchors, Vector3 newAnchor)
        {
            List<Vector3> anchors = new List<Vector3>(srcAnchors);
            if (anchors.Count < 2)
            {
                anchors.Add(newAnchor);
            }
            else
            {
                int insertIndex = GetInsertIndex(anchors, newAnchor);
                anchors.Insert(insertIndex, newAnchor);
            }
            return anchors.ToArray();
        }

        /// <summary>
        /// Finds the insertion index that best preserves the current polygon order for a new anchor.
        /// </summary>
        /// <param name="anchors">
        /// Existing ordered anchor loop.
        /// </param>
        /// <param name="newAnchor">
        /// Anchor to insert.
        /// </param>
        /// <returns>
        /// The index before which the new anchor should be inserted, or <c>-1</c> when there are not
        /// enough anchors to define a segment.
        /// </returns>
        /// <remarks>
        /// The method compares the new point against the midpoint of every polygon edge, including the
        /// closing edge from the last anchor back to the first.
        /// </remarks>
        public static int GetInsertIndex(List<Vector3> anchors, Vector3 newAnchor)
        {
            int insertIndex = -1;
            if (anchors.Count < 2)
            {
                return insertIndex;
            }

            float d = 0;
            float minDistance = float.MaxValue;
            Vector3 center;

            center = (anchors[0] + anchors[anchors.Count - 1]) * 0.5f;
            d = Vector3.Distance(newAnchor, center);
            if (d < minDistance)
            {
                minDistance = d;
                insertIndex = 0;
            }
            for (int i = 1; i < anchors.Count; ++i)
            {
                center = (anchors[i] + anchors[i - 1]) * 0.5f;
                d = Vector3.Distance(newAnchor, center);
                if (d < minDistance)
                {
                    minDistance = d;
                    insertIndex = i;
                }
            }
            return insertIndex;
        }

        /// <summary>
        /// Removes the first matching anchor from an anchor array copy.
        /// </summary>
        public static Vector3[] Remove(Vector3[] srcAnchors, Vector3 anchorToRemove)
        {
            List<Vector3> anchors = new List<Vector3>(srcAnchors);
            anchors.Remove(anchorToRemove);
            return anchors.ToArray();
        }

        /// <summary>
        /// Removes the anchor at a specific index from an anchor array copy.
        /// </summary>
        public static Vector3[] RemoveAt(Vector3[] srcAnchors, int i)
        {
            List<Vector3> anchors = new List<Vector3>(srcAnchors);
            anchors.RemoveAt(i);
            return anchors.ToArray();
        }

        /// <summary>
        /// Transforms every anchor in place by a matrix.
        /// </summary>
        /// <param name="srcAnchor">
        /// Anchor array to modify in place.
        /// </param>
        /// <param name="matrix">
        /// Transform matrix applied with <see cref="Matrix4x4.MultiplyPoint(Vector3)"/>.
        /// </param>
        public static void Transform(Vector3[] srcAnchor, Matrix4x4 matrix)
        {
            for (int i = 0; i < srcAnchor.Length; ++i)
            {
                srcAnchor[i] = matrix.MultiplyPoint(srcAnchor[i]);
            }
        }

        /// <summary>
        /// Forces every anchor in an array to the same Y coordinate.
        /// </summary>
        public static void FlattenY(Vector3[] srcAnchor, float y)
        {
            for (int i = 0; i < srcAnchor.Length; ++i)
            {
                srcAnchor[i].y = y;
            }
        }

        /// <summary>
        /// Builds a falloff polygon by offsetting each anchor along the averaged corner normal.
        /// </summary>
        /// <param name="srcAnchors">
        /// Base polygon anchors.
        /// </param>
        /// <param name="distance">
        /// Offset distance used to build the falloff polygon.
        /// </param>
        /// <param name="direction">
        /// Whether the falloff polygon should expand outward or inward from the base polygon.
        /// </param>
        /// <returns>
        /// A new anchor array describing the derived falloff polygon.
        /// </returns>
        /// <remarks>
        /// For polygons with fewer than three anchors, the source anchors are copied unchanged because
        /// there is not enough topology to compute corner normals reliably.
        /// </remarks>
        public static Vector3[] GetFalloff(Vector3[] srcAnchors, float distance, FalloffDirection direction)
        {
            if (srcAnchors.Length < 3)
            {
                Vector3[] falloff = new Vector3[srcAnchors.Length];
                srcAnchors.CopyTo(falloff, 0);
                return falloff;
            }
            else
            {
                Vector3[] falloff = new Vector3[srcAnchors.Length];
                bool reverse = false;
                for (int i = 0; i < falloff.Length; ++i)
                {
                    Vector3[] segments = GetAdjacentSegments(srcAnchors, i);
                    Vector2 dir0 = (segments[1].XZ() - segments[0].XZ()).normalized;
                    Vector2 dir1 = (segments[2].XZ() - segments[1].XZ()).normalized;
                    Vector2 dir = (dir0 + dir1).normalized;
                    if (i == 0)
                    {
                        Vector3 cross = Vector3.Cross(dir0, dir1);
                        reverse = cross.z >= 0;
                    }
                    Vector3 normal;
                    int mul = direction == FalloffDirection.Outer ? 1 : -1;
                    if (reverse)
                    {
                        normal = new Vector3(dir.y, 0, -dir.x) * mul;
                    }
                    else
                    {
                        normal = new Vector3(-dir.y, 0, dir.x) * mul;
                    }
                    falloff[i] = srcAnchors[i] + normal * distance;
                }
                return falloff;
            }
        }

        private static Vector3[] GetAdjacentSegments(Vector3[] srcAnchors, int index)
        {
            if (srcAnchors.Length < 3)
                throw new System.Exception("Can't get segments, vertex count must >= 3");
            if (index < 0 || index >= srcAnchors.Length)
                throw new System.Exception("Invalid vertex index");
            if (index == 0)
            {
                return new Vector3[3]
                {
                    srcAnchors[srcAnchors.Length - 1],
                    srcAnchors[0],
                    srcAnchors[1]
                };
            }
            else if (index == srcAnchors.Length - 1)
            {
                return new Vector3[3]
                {
                    srcAnchors[srcAnchors.Length - 2],
                    srcAnchors[srcAnchors.Length - 1],
                    srcAnchors[0]
                };
            }
            else
            {
                return new Vector3[3]
                {
                    srcAnchors[index - 1],
                    srcAnchors[index],
                    srcAnchors[index + 1]
                };
            }
        }

        /// <summary>
        /// Snaps every anchor in an array to the first collider hit below it.
        /// </summary>
        /// <param name="srcAnchor">
        /// Anchor array to modify in place.
        /// </param>
        /// <remarks>
        /// Each anchor casts a ray downward from a point 10,000 units above its current position and
        /// updates only its Y coordinate when a hit is found.
        /// </remarks>
        public static void SnapToWorld(Vector3[] srcAnchor)
        {
            for (int i = 0; i < srcAnchor.Length; ++i)
            {
                Vector3 p = srcAnchor[i];
                Vector3 rayOrigin = p;
                rayOrigin.y += 10000;
                Ray r = new Ray(rayOrigin, Vector3.down);
                RaycastHit hit;
                if (Physics.Raycast(r, out hit, 20000))
                {
                    p.y = hit.point.y;
                }
                srcAnchor[i] = p;
            }
        }

        /// <summary>
        /// Snaps one anchor to the first collider hit below it and returns the adjusted point.
        /// </summary>
        public static Vector3 SnapToWorld(Vector3 srcAnchor)
        {
            Vector3 rayOrigin = srcAnchor;
            rayOrigin.y = 10000;
            Ray r = new Ray(rayOrigin, Vector3.down);
            RaycastHit hit;
            if (Physics.Raycast(r, out hit, 20000))
            {
                srcAnchor.y = hit.point.y;
            }
            return srcAnchor;
        }
    }
}
#endif


