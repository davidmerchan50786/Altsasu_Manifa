// Assets/Scripts/MeshBuilder.cs
// ═══════════════════════════════════════════════════════════════════════════
//  MESH BUILDER — generación de meshes procedurales centralizada
//
//  Antes: 5 implementaciones idénticas o casi idénticas repartidas en:
//    GeneradorMundoOSM, GeneradorGeometriaPrecisa, SistemaEdificiosAAA,
//    GeneradorCallesAltsasu, GeneradorFachadasAAA
//
//  Ahora: una sola implementación reutilizable.
//
//  Uso:
//    var mesh = MeshBuilder.Edificio(planta, suelo, altura);
//    var mesh = MeshBuilder.Banda(puntos, ancho);
//    var mesh = MeshBuilder.Finalizar(verts, tris, uvs, "nombre");
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public static class MeshBuilder
{
    // ── Búferes reutilizables (evita allocations en builds frecuentes) ────
    // IMPORTANTE: no son thread-safe — usar solo desde el main thread.
    static readonly List<Vector3> _v  = new(4096);
    static readonly List<int>     _t  = new(8192);
    static readonly List<Vector2> _uv = new(4096);

    // ════════════════════════════════════════════════════════════════════════
    //  EDIFICIO — extrusión de polígono 2D con techo en abanico
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Genera el mesh de un edificio extruido desde una planta 2D (XZ).
    /// Incluye paredes laterales y techo triangulado en abanico.
    /// </summary>
    /// <param name="planta">Vértices del polígono base en coordenadas XZ (Vector2).</param>
    /// <param name="suelo">Altura Y del suelo (sampleo de terreno).</param>
    /// <param name="altura">Altura total del edificio en metros.</param>
    public static Mesh Edificio(IList<Vector2> planta, float suelo, float altura)
    {
        int n = planta.Count;
        if (n < 3) return null;

        _v.Clear(); _t.Clear(); _uv.Clear();

        // ── Paredes (quad strip por cada arista) ──────────────────────────
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            var p0 = new Vector3(planta[i].x, suelo,          planta[i].y);
            var p1 = new Vector3(planta[j].x, suelo,          planta[j].y);
            var p2 = new Vector3(planta[j].x, suelo + altura, planta[j].y);
            var p3 = new Vector3(planta[i].x, suelo + altura, planta[i].y);

            // UV en METROS (1 tile = 3m ≈ una planta): la textura de fachada ya
            // no se estira con la altura del edificio. Antes V era 0..1 fijo →
            // un edificio de 12m estiraba el ladrillo ×4 (efecto "gigante").
            float u = Vector2.Distance(planta[i], planta[j]) / 3f;
            float vTop = altura / 3f;
            int b = _v.Count;
            _v.Add(p0); _v.Add(p1); _v.Add(p2); _v.Add(p3);
            _uv.Add(new Vector2(0, 0));    _uv.Add(new Vector2(u, 0));
            _uv.Add(new Vector2(u, vTop)); _uv.Add(new Vector2(0, vTop));
            _t.Add(b); _t.Add(b+2); _t.Add(b+1);
            _t.Add(b); _t.Add(b+3); _t.Add(b+2);
        }

        // ── Techo (triangulación en abanico desde el primer vértice) ──────
        int techoBase = _v.Count;
        for (int i = 0; i < n; i++)
        {
            _v.Add(new Vector3(planta[i].x, suelo + altura, planta[i].y));
            _uv.Add(new Vector2(planta[i].x * 0.1f, planta[i].y * 0.1f));
        }
        for (int i = 1; i < n - 1; i++)
        {
            _t.Add(techoBase);
            _t.Add(techoBase + i);
            _t.Add(techoBase + i + 1);
        }

        return Finalizar("Edificio");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  BANDA DE CALLE — strip a lo largo de una polilínea
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Genera una banda plana (calle, acera, carril) a lo largo de una secuencia de puntos.
    /// Los puntos deben tener Y ya ajustada al terreno.
    /// </summary>
    /// <param name="pts">Eje central de la calle en coordenadas mundo.</param>
    /// <param name="ancho">Ancho total de la banda en metros.</param>
    public static Mesh Banda(IList<Vector3> pts, float ancho)
    {
        int n = pts.Count;
        if (n < 2) return null;

        _v.Clear(); _t.Clear(); _uv.Clear();
        float uAcum = 0f;

        for (int i = 0; i < n; i++)
        {
            // Dirección local del eje
            Vector3 dir;
            if      (i == 0)   dir = (pts[1]   - pts[0]).normalized;
            else if (i == n-1) dir = (pts[i]   - pts[i-1]).normalized;
            else               dir = ((pts[i+1] - pts[i-1]) * 0.5f).normalized;

            Vector3 right = Vector3.Cross(Vector3.up, dir).normalized * (ancho * 0.5f);
            _v.Add(pts[i] - right);
            _v.Add(pts[i] + right);

            if (i > 0) uAcum += Vector3.Distance(pts[i], pts[i-1]);
            _uv.Add(new Vector2(0, uAcum / ancho));
            _uv.Add(new Vector2(1, uAcum / ancho));

            if (i > 0)
            {
                int b = (i - 1) * 2;
                _t.Add(b);   _t.Add(b+2); _t.Add(b+1);
                _t.Add(b+1); _t.Add(b+2); _t.Add(b+3);
            }
        }

        return Finalizar("Calle");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  SOBRECARGA con array de Vector3 (para compatibilidad con código existente)
    // ════════════════════════════════════════════════════════════════════════

    public static Mesh Banda(Vector3[] pts, float ancho) => Banda((IList<Vector3>)pts, ancho);

    // ════════════════════════════════════════════════════════════════════════
    //  BANDA CONFORMADA AL TERRENO — fidelidad real (calles de Altsasu)
    //
    //  Igual que Banda(), pero muestrea la altura del terreno en CADA vértice
    //  final, incluidos los bordes laterales. En cuestas, los dos bordes de la
    //  calle siguen el terreno real en vez de heredar la altura del eje →
    //  sin calles flotando ni hundidas en las laderas.
    // ════════════════════════════════════════════════════════════════════════

    /// <param name="alturaEn">Función (x, z) → altura del terreno en mundo.</param>
    /// <param name="offsetY">Elevación sobre el terreno (anti z-fighting).</param>
    public static Mesh BandaConformada(
        IList<Vector3> pts, float ancho,
        System.Func<float, float, float> alturaEn, float offsetY)
    {
        int n = pts.Count;
        if (n < 2) return null;
        if (alturaEn == null) return Banda(pts, ancho);

        _v.Clear(); _t.Clear(); _uv.Clear();
        float uAcum = 0f;

        for (int i = 0; i < n; i++)
        {
            Vector3 dir;
            if      (i == 0)   dir = (pts[1]   - pts[0]).normalized;
            else if (i == n-1) dir = (pts[i]   - pts[i-1]).normalized;
            else               dir = ((pts[i+1] - pts[i-1]) * 0.5f).normalized;

            Vector3 right = Vector3.Cross(Vector3.up, dir).normalized * (ancho * 0.5f);

            var izq = pts[i] - right;
            var der = pts[i] + right;
            izq.y = alturaEn(izq.x, izq.z) + offsetY;
            der.y = alturaEn(der.x, der.z) + offsetY;
            _v.Add(izq);
            _v.Add(der);

            if (i > 0) uAcum += Vector3.Distance(pts[i], pts[i-1]);
            _uv.Add(new Vector2(0, uAcum / ancho));
            _uv.Add(new Vector2(1, uAcum / ancho));

            if (i > 0)
            {
                int b = (i - 1) * 2;
                _t.Add(b);   _t.Add(b+2); _t.Add(b+1);
                _t.Add(b+1); _t.Add(b+2); _t.Add(b+3);
            }
        }

        return Finalizar("Calle");
    }

    /// <summary>
    /// Subdivide una polilínea para que ningún segmento supere maxSegmento
    /// metros. Imprescindible con ejes OSM: sus nodos pueden distar 50–170m
    /// y sin subdividir la calle corta las curvas y no sigue el relieve.
    /// El buffer destino se limpia y rellena (cero allocs si se reutiliza).
    /// </summary>
    public static void SubdividirPolilinea(
        IList<Vector3> origen, List<Vector3> destino, float maxSegmento)
    {
        destino.Clear();
        if (origen == null || origen.Count == 0) return;
        destino.Add(origen[0]);

        for (int i = 1; i < origen.Count; i++)
        {
            var a = origen[i - 1];
            var b = origen[i];
            float dist = Vector3.Distance(a, b);
            int divisiones = Mathf.Max(1, Mathf.CeilToInt(dist / maxSegmento));
            for (int d = 1; d <= divisiones; d++)
                destino.Add(Vector3.Lerp(a, b, d / (float)divisiones));
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  FINALIZAR — construye el Mesh desde listas externas
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Construye un Mesh desde listas ya rellenas externamente.
    /// Selecciona automáticamente UInt16/UInt32 según número de vértices.
    /// Calcula normales y bounds.
    /// </summary>
    public static Mesh Finalizar(
        IList<Vector3> verts, IList<int> tris, IList<Vector2> uvs, string nombre = "Mesh")
    {
        if (verts.Count < 3 || tris.Count < 3) return null;

        var mesh = new Mesh { name = nombre };
        mesh.indexFormat = verts.Count > 65535
            ? IndexFormat.UInt32
            : IndexFormat.UInt16;
        mesh.SetVertices((List<Vector3>)verts);
        mesh.SetTriangles((List<int>)tris, 0);
        mesh.SetUVs(0, (List<Vector2>)uvs);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // ── Overload interno que usa los búferes estáticos ────────────────────
    static Mesh Finalizar(string nombre)
    {
        if (_v.Count < 3 || _t.Count < 3) return null;

        var mesh = new Mesh { name = nombre };
        mesh.indexFormat = _v.Count > 65535
            ? IndexFormat.UInt32
            : IndexFormat.UInt16;
        mesh.SetVertices(_v);
        mesh.SetTriangles(_t, 0);
        mesh.SetUVs(0, _uv);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  HELPERS DE GEOMETRÍA
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Crea el GO hijo con MeshFilter + MeshRenderer + MeshCollider.</summary>
    public static GameObject CrearGO(string nombre, Transform padre, Mesh mesh, Material mat,
                                     bool isStatic = true, bool conCollider = true)
    {
        if (mesh == null) return null;
        var go = new GameObject(nombre);
        go.transform.SetParent(padre, false);
        go.isStatic = isStatic;
        go.AddComponent<MeshFilter>().sharedMesh       = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        if (conCollider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
        return go;
    }
}
