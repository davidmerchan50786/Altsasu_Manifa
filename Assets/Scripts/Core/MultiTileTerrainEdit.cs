// Assets/Scripts/Core/MultiTileTerrainEdit.cs
// ═══════════════════════════════════════════════════════════════════════════
//  MULTI-TILE TERRAIN EDIT — utilidad para ESCRITORES del terreno mosaico
//
//  Problema: con 48 tiles, un escritor (ríos, splatmaps) que edite un solo
//  Terrain rompe las costuras bit-exactas en cuanto su área cruza un borde.
//
//  Solución: editar SIEMPRE en coordenadas de MUNDO a través de esta utilidad.
//  Itera los tiles intersectados por el Rect y aplica la misma función f a
//  cada vértice. Los vértices de borde compartidos entre dos tiles tienen
//  coordenadas mundo IDÉNTICAS (posiciones diádicas exactas) ⇒ f devuelve el
//  mismo valor en ambos ⇒ la costura se preserva por construcción.
//
//  IMPORTANTE para escritores: f debe ser determinista respecto a (x, z,
//  alturaActual). Kernels idempotentes (p.ej. h' = min(h, perfilCauce)) pueden
//  re-aplicarse sin acumular error.
//
//  Capa CORE (Runtime y Systems la comparten; Runtime no referencia Systems).
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using UnityEngine;

public static class MultiTileTerrainEdit
{
    /// <summary>Devuelve la NUEVA altura mundo del vértice en (xMundo, zMundo).</summary>
    public delegate float FuncAlturaMundo(float xMundo, float zMundo, float alturaMundoActual);

    /// <summary>Modifica in situ los pesos de capas del texel en (xMundo, zMundo).</summary>
    public delegate void FuncAlphamap(float xMundo, float zMundo, float[] pesos);

    /// <summary>
    /// Aplica f a todos los vértices de heightmap dentro de mundoXZ, en todos
    /// los tiles intersectados. Usa SetHeightsDelayLOD + SyncHeightmap (un solo
    /// sync por tile al final).
    /// </summary>
    public static void ModificarAlturas(ITerrainService svc, Rect mundoXZ, FuncAlturaMundo f)
    {
        if (svc == null || f == null) return;
        var tocados = new List<Terrain>();

        foreach (var terr in svc.Tiles)
        {
            if (terr == null || terr.terrainData == null) continue;
            var td = terr.terrainData;
            Vector3 p = terr.transform.position;
            int res = td.heightmapResolution;
            float pasoX = td.size.x / (res - 1);
            float pasoZ = td.size.z / (res - 1);

            // índices de vértice que cubren el rect (incluye bordes)
            int i0 = Mathf.Clamp(Mathf.FloorToInt((mundoXZ.xMin - p.x) / pasoX), 0, res - 1);
            int i1 = Mathf.Clamp(Mathf.CeilToInt((mundoXZ.xMax - p.x) / pasoX), 0, res - 1);
            int j0 = Mathf.Clamp(Mathf.FloorToInt((mundoXZ.yMin - p.z) / pasoZ), 0, res - 1);
            int j1 = Mathf.Clamp(Mathf.CeilToInt((mundoXZ.yMax - p.z) / pasoZ), 0, res - 1);
            if (i1 <= i0 && (mundoXZ.xMax < p.x || mundoXZ.xMin > p.x + td.size.x)) continue;
            if (j1 <= j0 && (mundoXZ.yMax < p.z || mundoXZ.yMin > p.z + td.size.z)) continue;
            int w = i1 - i0 + 1, h = j1 - j0 + 1;
            if (w <= 0 || h <= 0) continue;

            float[,] alturas = td.GetHeights(i0, j0, w, h); // [fila(z), col(x)] normalizado
            bool cambio = false;
            for (int j = 0; j < h; j++)
            {
                float z = p.z + (j0 + j) * pasoZ;
                for (int i = 0; i < w; i++)
                {
                    float x = p.x + (i0 + i) * pasoX;
                    float actual = p.y + alturas[j, i] * td.size.y;
                    float nueva = f(x, z, actual);
                    if (!Mathf.Approximately(nueva, actual))
                    {
                        alturas[j, i] = Mathf.Clamp01((nueva - p.y) / td.size.y);
                        cambio = true;
                    }
                }
            }
            if (cambio)
            {
                td.SetHeightsDelayLOD(i0, j0, alturas);
                tocados.Add(terr);
            }
        }

        foreach (var terr in tocados)
            terr.terrainData.SyncHeightmap();
    }

    /// <summary>
    /// Aplica f a los texels de alphamap dentro de mundoXZ en todos los tiles
    /// intersectados. f recibe el array de pesos del texel (uno por TerrainLayer)
    /// y lo modifica in situ; la utilidad renormaliza a suma 1.
    /// </summary>
    public static void ModificarAlphamaps(ITerrainService svc, Rect mundoXZ, FuncAlphamap f)
    {
        if (svc == null || f == null) return;

        foreach (var terr in svc.Tiles)
        {
            if (terr == null || terr.terrainData == null) continue;
            var td = terr.terrainData;
            int capas = td.alphamapLayers;
            if (capas == 0) continue;
            Vector3 p = terr.transform.position;
            int aw = td.alphamapWidth, ah = td.alphamapHeight;
            float celX = td.size.x / aw;
            float celZ = td.size.z / ah;

            // texels cuyo CENTRO cae dentro del rect
            int i0 = Mathf.Clamp(Mathf.FloorToInt((mundoXZ.xMin - p.x) / celX - 0.5f), 0, aw - 1);
            int i1 = Mathf.Clamp(Mathf.CeilToInt((mundoXZ.xMax - p.x) / celX - 0.5f), 0, aw - 1);
            int j0 = Mathf.Clamp(Mathf.FloorToInt((mundoXZ.yMin - p.z) / celZ - 0.5f), 0, ah - 1);
            int j1 = Mathf.Clamp(Mathf.CeilToInt((mundoXZ.yMax - p.z) / celZ - 0.5f), 0, ah - 1);
            if (p.x + (i1 + 0.5f) * celX < mundoXZ.xMin || p.x + (i0 + 0.5f) * celX > mundoXZ.xMax) continue;
            if (p.z + (j1 + 0.5f) * celZ < mundoXZ.yMin || p.z + (j0 + 0.5f) * celZ > mundoXZ.yMax) continue;
            int w = i1 - i0 + 1, h = j1 - j0 + 1;
            if (w <= 0 || h <= 0) continue;

            float[,,] alfas = td.GetAlphamaps(i0, j0, w, h); // [fila, col, capa]
            var pesos = new float[capas];
            bool cambio = false;
            for (int j = 0; j < h; j++)
            {
                float z = p.z + (j0 + j + 0.5f) * celZ;
                for (int i = 0; i < w; i++)
                {
                    float x = p.x + (i0 + i + 0.5f) * celX;
                    for (int c = 0; c < capas; c++) pesos[c] = alfas[j, i, c];
                    f(x, z, pesos);
                    float suma = 0f;
                    for (int c = 0; c < capas; c++) suma += pesos[c];
                    if (suma <= 0f) continue;
                    for (int c = 0; c < capas; c++)
                    {
                        float v = pesos[c] / suma;
                        if (!Mathf.Approximately(v, alfas[j, i, c])) { alfas[j, i, c] = v; cambio = true; }
                    }
                }
            }
            if (cambio)
                td.SetAlphamaps(i0, j0, alfas);
        }
    }
}
