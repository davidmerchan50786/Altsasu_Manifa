// Assets/Scripts/Editor/GridTilesElement.cs
// ═══════════════════════════════════════════════════════════════════════════
//  GRID TILES ELEMENT — VisualElement custom para DirectorMundoWindow (capa EDITOR)
//
//  Pinta los tiles del Mosaico V2 (posición real desde manifest_v2.json) en el
//  panel derecho del Director de Mundo, tintados por anillo. `Refrescar()` recarga
//  el manifest y repinta.
//
//  NOTA: el coloreado por validation_report.json (verde = auditoría pasada) que
//  menciona DirectorMundoWindow queda como TODO — aquí se tinta por anillo, que es
//  suficiente para ubicar visualmente los 48 tiles. Schema del informe no asumido.
// ═══════════════════════════════════════════════════════════════════════════

using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class GridTilesElement : VisualElement
{
    const string MANIFEST_REL = "AlsasuaData/terrain_tiles_v2/manifest_v2.json";

    MosaicoManifest _man;

    public GridTilesElement()
    {
        style.minHeight = 200;
        generateVisualContent += Pintar;
        Refrescar();
    }

    /// <summary>Recarga el manifest y repinta. Lo llama el botón Refrescar y el fin
    /// de la validación en DirectorMundoWindow.</summary>
    public void Refrescar()
    {
        _man = null;
        try
        {
            string ruta = Path.Combine(Application.dataPath, MANIFEST_REL);
            if (File.Exists(ruta)) _man = MosaicoManifest.Cargar(ruta);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[GridTilesElement] No se pudo leer el manifest: " + e.Message);
        }
        MarkDirtyRepaint();
    }

    void Pintar(MeshGenerationContext ctx)
    {
        Rect r = contentRect;
        if (r.width <= 1f || r.height <= 1f) return;

        var p = ctx.painter2D;

        // Fondo
        Rellenar(p, new Color(0.12f, 0.12f, 0.14f), 0f, 0f, r.width, r.height);

        if (_man?.tiles == null || _man.tiles.Count == 0) return;

        // Bounding box (mundo) de todos los tiles
        float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
        foreach (var t in _man.tiles)
        {
            minX = Mathf.Min(minX, t.x);            minZ = Mathf.Min(minZ, t.z);
            maxX = Mathf.Max(maxX, t.x + t.ancho);  maxZ = Mathf.Max(maxZ, t.z + t.ancho);
        }
        float w = Mathf.Max(1f, maxX - minX), h = Mathf.Max(1f, maxZ - minZ);
        const float pad = 8f;
        float esc = Mathf.Min((r.width - 2f * pad) / w, (r.height - 2f * pad) / h);

        foreach (var t in _man.tiles)
        {
            // mundo → pixel (Z hacia arriba → invertir Y)
            float px = pad + (t.x - minX) * esc;
            float py = pad + (maxZ - (t.z + t.ancho)) * esc;
            float side = t.ancho * esc;

            Color c = t.anillo == 0 ? new Color(0.30f, 0.55f, 0.30f)   // urbano
                    : t.anillo == 1 ? new Color(0.38f, 0.42f, 0.30f)   // valle
                    :                 new Color(0.46f, 0.36f, 0.26f);  // sierras

            Rellenar(p, c, px, py, side, side);
            Borde(p, new Color(0f, 0f, 0f, 0.5f), px, py, side, side);
        }
    }

    static void Rellenar(Painter2D p, Color color, float x, float y, float w, float h)
    {
        p.fillColor = color;
        p.BeginPath();
        p.MoveTo(new Vector2(x, y));
        p.LineTo(new Vector2(x + w, y));
        p.LineTo(new Vector2(x + w, y + h));
        p.LineTo(new Vector2(x, y + h));
        p.ClosePath();
        p.Fill();
    }

    static void Borde(Painter2D p, Color color, float x, float y, float w, float h)
    {
        p.strokeColor = color;
        p.lineWidth = 1f;
        p.BeginPath();
        p.MoveTo(new Vector2(x, y));
        p.LineTo(new Vector2(x + w, y));
        p.LineTo(new Vector2(x + w, y + h));
        p.LineTo(new Vector2(x, y + h));
        p.ClosePath();
        p.Stroke();
    }
}
