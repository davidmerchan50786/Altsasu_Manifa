// Assets/Scripts/_ClipmapV3~/ConstructorMallaClipmap.cs  (STAGING — carpeta ~ no se compila)
// ─────────────────────────────────────────────────────────────────────────────
//  Genera la malla de un geometry clipmap: anillos concéntricos de rejilla, cada
//  nivel con el doble de tamaño de celda y un HUECO central (cubierto por el nivel
//  más fino). Geometría pura y determinista; la altura la pone el shader de
//  displacement por vertex-texture-fetch sobre heightmap_unificado.r16 (fase 3).
//
//  Convención: malla LOCAL centrada en origen (Y=0). En runtime, ClipmapTerrenoV3
//  mueve el transform al jugador (snap a rejilla) → worldXZ = local + origen.
// ─────────────────────────────────────────────────────────────────────────────
using System.Collections.Generic;
using UnityEngine;

public static class ConstructorMallaClipmap
{
    /// <summary>
    /// Construye la malla del clipmap.
    /// </summary>
    /// <param name="m">celdas por lado en cada nivel (par; típico 64).</param>
    /// <param name="niveles">número de anillos concéntricos (típico 6).</param>
    /// <param name="cellSize">metros por celda del nivel 0 (más fino).</param>
    public static Mesh Construir(int m = 64, int niveles = 6, float cellSize = 1f)
    {
        if ((m & 1) != 0) m++;                       // m par
        int half = m / 2;
        int hueco = m / 4;                           // semi-anchura del hueco en celdas

        var verts = new List<Vector3>(m * m * niveles);
        var tris  = new List<int>(m * m * niveles * 6);

        for (int k = 0; k < niveles; k++)
        {
            float cs = cellSize * (1 << k);          // tamaño de celda del nivel
            for (int cj = 0; cj < m; cj++)
            for (int ci = 0; ci < m; ci++)
            {
                // índices de celda relativos al centro: [ci-half, ci-half+1]
                int i0 = ci - half, j0 = cj - half;

                // saltar celdas totalmente dentro del hueco (cubiertas por el nivel k-1)
                if (k > 0 &&
                    i0 >= -hueco && (i0 + 1) <= hueco &&
                    j0 >= -hueco && (j0 + 1) <= hueco)
                    continue;

                float x0 = i0 * cs, x1 = (i0 + 1) * cs;
                float z0 = j0 * cs, z1 = (j0 + 1) * cs;

                int b = verts.Count;
                verts.Add(new Vector3(x0, 0f, z0));
                verts.Add(new Vector3(x1, 0f, z0));
                verts.Add(new Vector3(x0, 0f, z1));
                verts.Add(new Vector3(x1, 0f, z1));
                tris.Add(b + 0); tris.Add(b + 2); tris.Add(b + 1);
                tris.Add(b + 2); tris.Add(b + 3); tris.Add(b + 1);
            }
        }

        var mesh = new Mesh { name = $"ClipmapV3_m{m}_l{niveles}" };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; // puede superar 65 k verts
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        // sin normales/uv: el shader de displacement las deriva del heightmap.
        return mesh;
    }
}
