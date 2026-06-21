// Assets/Scripts/Runtime/DecimadorMeshLOD1.cs
// ═══════════════════════════════════════════════════════════════════════════
//  DECIMADOR DE MALLA — vertex clustering para LOD1 (HLOD proxy lejano)
//
//  Reduce 60-80% de triángulos en el proxy lejano de cada celda de ciudad,
//  manteniendo la silueta. Algoritmo: Vertex Clustering uniforme.
//
//  ALGORITMO:
//    1. Divide el bounding box en células 3D de tamaño `cellSize`
//    2. Todos los vértices dentro de la misma célula se fusionan en su centroide
//    3. Los triángulos que quedan degenerados (dos vértices iguales) se eliminan
//
//  Resultado para geometría urbana (edificios 6-20m de alto):
//    · cellSize = 2.0m → ~65% reducción de tris (proxy a 200m+)
//    · cellSize = 4.0m → ~80% reducción de tris (proxy a 400m+)
//
//  Solo se usa en Editor (HorneadorCiudad para el LOD1 HLOD).
//  En Runtime, la malla ya está pre-decimada en el prefab de celda.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public static class DecimadorMeshLOD1
{
    /// <summary>
    /// Reduce la malla fusionando vértices dentro de células de <paramref name="cellSize"/> metros.
    /// Retorna null si la malla resultante tiene 0 triángulos.
    /// </summary>
    public static Mesh Decimar(Mesh original, float cellSize = 2.5f)
    {
        if (original == null || original.vertexCount < 3) return null;

        var verts    = original.vertices;
        var trisOrig = original.triangles;
        if (trisOrig.Length < 3) return null;

        var bounds = original.bounds;
        // Tamaño de célula mínimo para evitar eliminar demasiada geometría
        float cs = Mathf.Max(cellSize, 0.05f);

        // ── 1. Agrupar vértices por célula ────────────────────────────────
        var cellMap  = new Dictionary<Vector3Int, int>();    // cell → nuevo índice
        var newVerts = new List<Vector3>(verts.Length / 4);
        var cellAcc  = new List<(Vector3 sum, int count)>(verts.Length / 4);
        var remap    = new int[verts.Length];                // viejo → nuevo índice

        for (int i = 0; i < verts.Length; i++)
        {
            var v    = verts[i];
            var cell = new Vector3Int(
                Mathf.FloorToInt(v.x / cs),
                Mathf.FloorToInt(v.y / cs),
                Mathf.FloorToInt(v.z / cs));

            if (!cellMap.TryGetValue(cell, out int idx))
            {
                idx = newVerts.Count;
                cellMap[cell] = idx;
                newVerts.Add(v);
                cellAcc.Add((v, 1));
            }
            else
            {
                // Actualizar centroide acumulado
                var (sum, count) = cellAcc[idx];
                cellAcc[idx] = (sum + v, count + 1);
                newVerts[idx] = (sum + v) / (count + 1);
            }
            remap[i] = idx;
        }

        // ── 2. Reasignar triángulos eliminando los degenerados ────────────
        var newTris = new List<int>(trisOrig.Length);
        for (int i = 0; i + 2 < trisOrig.Length; i += 3)
        {
            int a = remap[trisOrig[i]];
            int b = remap[trisOrig[i + 1]];
            int c = remap[trisOrig[i + 2]];
            if (a == b || b == c || a == c) continue;   // degenerado → descartar
            newTris.Add(a); newTris.Add(b); newTris.Add(c);
        }

        if (newTris.Count == 0) return null;

        // ── 3. Construir malla decimada ───────────────────────────────────
        var result = new Mesh
        {
            name        = original.name + "_HLOD_Decimated",
            indexFormat = newVerts.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16,
        };
        result.SetVertices(newVerts);
        result.SetTriangles(newTris, 0);
        result.RecalculateNormals();
        result.RecalculateBounds();

        return result;
    }

    /// <summary>Ratio de reducción esperado para una ciudad con <paramref name="cellSize"/> dado.</summary>
    public static string DescripcionRatio(float cellSize)
    {
        if (cellSize <= 1f)  return "~40% reducción (detalles aún visibles)";
        if (cellSize <= 2f)  return "~65% reducción (bueno para 200m+)";
        if (cellSize <= 3f)  return "~75% reducción (bueno para 300m+)";
        return                      "~85% reducción (solo silueta, 500m+)";
    }
}
