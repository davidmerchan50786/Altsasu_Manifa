// Assets/Scripts/ProcesadorNubePuntos.cs
// ═══════════════════════════════════════════════════════════════════════════
//  PROCESADOR DE NUBE DE PUNTOS — reconstruye tejados desde DSM/LIDAR real
//
//  Entrada:
//    dsm_alsasua_5m.asc  — DSM ASCII Grid (superficie con tejados)
//    dtm_alsasua_5m.asc  — DTM ASCII Grid (terreno sin edificios)
//    lidar_alsasua.laz   — nube de puntos PNOA (opcional)
//
//  Proceso para cada edificio OSM:
//    1. Cargar grid DSM en memoria como NativeArray
//    2. Para cada footprint OSM: extraer puntos DSM dentro del polígono
//    3. nDSM = DSM - DTM → altura sobre el suelo (elimina pendiente terrain)
//    4. Filtrar puntos de tejado (nDSM > 1.5m = sobre nivel suelo)
//    5. Delaunay triangulación 2D de los puntos del tejado
//    6. Generar mesh Unity con las alturas reales del tejado
//
//  Si no hay DSM disponible → fallback al generador matemático
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;

public class ProcesadorNubePuntos : MonoBehaviour
{
    public static ProcesadorNubePuntos Instance { get; private set; }

    // ── Archivos DSM/DTM ──────────────────────────────────────────────────
    const string PATH_DSM = "Assets/AlsasuaData/dsm_alsasua_5m.asc";
    const string PATH_DTM = "Assets/AlsasuaData/dtm_alsasua_5m.asc";

    // ── Coordenadas del grid (ETRS89 → Unity) ────────────────────────────
    // El ASC del IGN usa coordenadas geográficas en EPSG:4326 o UTM ETRS89
    // Las convertimos a Unity XZ con el mismo offset que el DEM
    const float OX = GeoDataAlsasua.OX, OZ = GeoDataAlsasua.OZ;

    // Coordenadas GPS del punto inferior-izquierdo del DSM (se lee del .asc)
    float _dsmXllCorner, _dsmYllCorner;
    float _dsmCellSize;
    int   _dsmNcols, _dsmNrows;
    float _dsmNodata;
    float[] _dsm, _dtm;
    bool  _dsmCargado;

    // ── Conversión GPS ↔ Unity ────────────────────────────────────────────
    // Basada en el mismo cálculo que DescargarDatosAlsasua.ps1
    const double LAT0 = 42.8987, LON0 = -2.1677;
    const float  M_PER_DEG_LAT = 111320f, M_PER_DEG_LON = 76400f;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    IEnumerator Start()
    {
        yield return null;
        if (CargarDSM())
        {
            AlsasuaLogger.Info("NubePuntos",
                $"DSM cargado: {_dsmNcols}×{_dsmNrows} @ {_dsmCellSize}m/px");
        }
        else
        {
            AlsasuaLogger.Warn("NubePuntos",
                "DSM no encontrado. Ejecuta DescargarLIDARAlsasua.ps1 " +
                "para obtener datos reales del IGN. Usando geometría matemática.");
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  CARGA DEL DSM/DTM (formato ASCII Grid)
    // ════════════════════════════════════════════════════════════════════════

    bool CargarDSM()
    {
        string dsmPath = Path.Combine(Application.dataPath.Replace("Assets",""), PATH_DSM);
        string dtmPath = Path.Combine(Application.dataPath.Replace("Assets",""), PATH_DTM);

        if (!File.Exists(dsmPath)) return false;

        try
        {
            _dsm = LeerASCGrid(dsmPath,
                out _dsmNcols, out _dsmNrows,
                out _dsmXllCorner, out _dsmYllCorner,
                out _dsmCellSize, out _dsmNodata);

            if (File.Exists(dtmPath))
                _dtm = LeerASCGrid(dtmPath,
                    out _, out _, out _, out _, out _, out _);

            _dsmCargado = _dsm != null;
            return _dsmCargado;
        }
        catch (System.Exception e)
        {
            AlsasuaLogger.Error("NubePuntos", $"Error cargando DSM: {e.Message}");
            return false;
        }
    }

    static float[] LeerASCGrid(string path,
        out int ncols, out int nrows,
        out float xllCorner, out float yllCorner,
        out float cellSize, out float nodata)
    {
        ncols = nrows = 0;
        xllCorner = yllCorner = cellSize = 0f;
        nodata = -9999f;

        using var reader = new StreamReader(path);
        // Cabecera de 6 líneas
        ncols     = int.Parse(  reader.ReadLine().Split()[1]);
        nrows     = int.Parse(  reader.ReadLine().Split()[1]);
        xllCorner = float.Parse(reader.ReadLine().Split()[1], System.Globalization.CultureInfo.InvariantCulture);
        yllCorner = float.Parse(reader.ReadLine().Split()[1], System.Globalization.CultureInfo.InvariantCulture);
        cellSize  = float.Parse(reader.ReadLine().Split()[1], System.Globalization.CultureInfo.InvariantCulture);
        nodata    = float.Parse(reader.ReadLine().Split()[1], System.Globalization.CultureInfo.InvariantCulture);

        float[] data = new float[ncols * nrows];
        int idx = 0;
        string line;
        while ((line = reader.ReadLine()) != null)
        {
            foreach (var tok in line.Split(' ', '\t'))
            {
                if (string.IsNullOrWhiteSpace(tok)) continue;
                data[idx++] = float.Parse(tok, System.Globalization.CultureInfo.InvariantCulture);
                if (idx >= data.Length) break;
            }
            if (idx >= data.Length) break;
        }
        return data;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  MUESTREO DEL DSM EN UN FOOTPRINT
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Para un footprint OSM (polígono XZ Unity), devuelve los puntos 3D
    /// del DSM real que caen dentro del polígono.
    /// Los puntos tienen Y = altura real del tejado (nDSM = DSM - DTM).
    /// </summary>
    public List<Vector3> MuestrarTejadoReal(Vector2[] footprintUnity)
    {
        if (!_dsmCargado) return null;

        var resultado = new List<Vector3>();

        // Bounding box del footprint
        float minX = footprintUnity.Min(v => v.x), maxX = footprintUnity.Max(v => v.x);
        float minZ = footprintUnity.Min(v => v.y), maxZ = footprintUnity.Max(v => v.y);

        // Recorrer puntos del DSM en el bounding box
        int col0 = UnityXToCol(minX - _dsmCellSize);
        int col1 = UnityXToCol(maxX + _dsmCellSize);
        int row0 = UnityZToRow(maxZ + _dsmCellSize);
        int row1 = UnityZToRow(minZ - _dsmCellSize);

        col0 = Mathf.Clamp(col0, 0, _dsmNcols - 1);
        col1 = Mathf.Clamp(col1, 0, _dsmNcols - 1);
        row0 = Mathf.Clamp(row0, 0, _dsmNrows - 1);
        row1 = Mathf.Clamp(row1, 0, _dsmNrows - 1);

        for (int row = row0; row <= row1; row++)
        for (int col = col0; col <= col1; col++)
        {
            float dsmH = _dsm[row * _dsmNcols + col];
            if (Mathf.Approximately(dsmH, _dsmNodata)) continue;

            float unityX = ColToUnityX(col);
            float unityZ = RowToUnityZ(row);
            var p = new Vector2(unityX, unityZ);

            if (!PuntoEnPoligono(p, footprintUnity)) continue;

            // nDSM = DSM - DTM (eliminar pendiente del terreno)
            float ndsm = dsmH;
            if (_dtm != null)
            {
                float dtmH = _dtm[row * _dsmNcols + col];
                if (!Mathf.Approximately(dtmH, _dsmNodata))
                    ndsm = dsmH - dtmH;
            }

            // Solo puntos sobre 1.5m (tejado, no suelo ni vegetación baja)
            if (ndsm < 1.5f) continue;

            resultado.Add(new Vector3(unityX, ndsm, unityZ));
        }

        return resultado.Count >= 3 ? resultado : null;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  TRIANGULACIÓN DELAUNAY 2D → MESH DE TEJADO
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Toma una nube de puntos 3D del tejado y genera una mesh Unity
    /// usando triangulación de Bowyer-Watson (Delaunay 2D).
    /// </summary>
    public Mesh GenerarMeshDesdePuntos(List<Vector3> puntos3D, float yBase)
    {
        if (puntos3D == null || puntos3D.Count < 3) return null;

        // Proyectar a 2D para la triangulación
        var pts2D = puntos3D.Select(p => new Vector2(p.x, p.z)).ToList();
        var tris  = DelaunayBowyerWatson(pts2D);

        if (tris == null || tris.Count == 0) return null;

        // Elevar los puntos al terreno + nDSM
        var terrain = Terrain.activeTerrain;
        var verts3D = puntos3D.Select(p =>
        {
            float yTerrain = terrain != null
                ? terrain.SampleHeight(new Vector3(p.x, 0, p.z))
                : yBase;
            return new Vector3(p.x, yTerrain + p.y, p.z);
        }).ToList();

        var mesh = new Mesh { name = "TejadoDSM" };
        mesh.indexFormat = verts3D.Count > 65535
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;

        mesh.SetVertices(verts3D);
        mesh.SetTriangles(tris, 0);

        // UVs desde posición XZ normalizada
        float minX = pts2D.Min(p => p.x), rangeX = pts2D.Max(p => p.x) - minX + 0.001f;
        float minZ = pts2D.Min(p => p.y), rangeZ = pts2D.Max(p => p.y) - minZ + 0.001f;
        mesh.SetUVs(0, verts3D.Select(v =>
            new Vector2((v.x - minX) / rangeX, (v.z - minZ) / rangeZ)).ToList());

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  BOWYER-WATSON — TRIANGULACIÓN DELAUNAY 2D
    // ════════════════════════════════════════════════════════════════════════
    // Implementación del algoritmo de Bowyer-Watson para triangulación Delaunay.
    // Garantiza que ningún punto esté dentro del circuncírculo de ningún triángulo.
    // Resultado: indices de vértices (grupos de 3) para Unity.

    public static List<int> DelaunayBowyerWatson(List<Vector2> points)
    {
        int n = points.Count;
        if (n < 3) return null;

        // Super-triángulo que contiene todos los puntos
        float minX = points.Min(p => p.x), maxX = points.Max(p => p.x);
        float minY = points.Min(p => p.y), maxY = points.Max(p => p.y);
        float dx = maxX - minX, dy = maxY - minY;
        float deltaMax = Mathf.Max(dx, dy) * 3f;
        float midX = (minX + maxX) / 2f, midY = (minY + maxY) / 2f;

        var superA = new Vector2(midX - deltaMax,     midY - deltaMax);
        var superB = new Vector2(midX,                midY + deltaMax);
        var superC = new Vector2(midX + deltaMax,     midY - deltaMax);

        var allPoints = new List<Vector2>(points) { superA, superB, superC };
        int sA = n, sB = n + 1, sC = n + 2;

        // Triángulos como tuplas de índices
        var triangles = new List<(int a, int b, int c)> { (sA, sB, sC) };

        for (int i = 0; i < n; i++)
        {
            var p = allPoints[i];
            var malos = new List<(int a, int b, int c)>();

            // Encontrar triángulos cuyo circuncírculo contiene el punto
            foreach (var tri in triangles)
                if (EnCircuncirculo(allPoints[tri.a], allPoints[tri.b], allPoints[tri.c], p))
                    malos.Add(tri);

            // Encontrar el polígono hueco (aristas no compartidas)
            var borde = new List<(int a, int b)>();
            foreach (var tri in malos)
            {
                (int a, int b)[] aristas = { (tri.a,tri.b),(tri.b,tri.c),(tri.c,tri.a) };
                foreach (var ar in aristas)
                {
                    bool compartida = malos.Any(t => t != tri &&
                        ((t.a==ar.a&&t.b==ar.b)||(t.a==ar.b&&t.b==ar.a)||
                         (t.b==ar.a&&t.c==ar.b)||(t.b==ar.b&&t.c==ar.a)||
                         (t.c==ar.a&&t.a==ar.b)||(t.c==ar.b&&t.a==ar.a)));
                    if (!compartida) borde.Add(ar);
                }
            }

            foreach (var tri in malos) triangles.Remove(tri);
            foreach (var (a, b) in borde) triangles.Add((a, b, i));
        }

        // Eliminar triángulos que comparten vértice con el super-triángulo
        triangles.RemoveAll(t => t.a >= n || t.b >= n || t.c >= n);

        // Convertir a lista de índices Unity (asegurar orientación CCW para normales up)
        var result = new List<int>(triangles.Count * 3);
        foreach (var (a, b, c) in triangles)
        {
            // Verificar orientación (CCW = normal apunta arriba)
            Vector2 pa = allPoints[a], pb = allPoints[b], pc = allPoints[c];
            float cross = (pb.x - pa.x) * (pc.y - pa.y) - (pb.y - pa.y) * (pc.x - pa.x);
            if (cross > 0) { result.Add(a); result.Add(b); result.Add(c); }
            else           { result.Add(a); result.Add(c); result.Add(b); }
        }
        return result;
    }

    static bool EnCircuncirculo(Vector2 a, Vector2 b, Vector2 c, Vector2 p)
    {
        // Determinante 3×3 para comprobar si p está dentro del circuncírculo (a,b,c)
        double ax = a.x - p.x, ay = a.y - p.y;
        double bx = b.x - p.x, by = b.y - p.y;
        double cx = c.x - p.x, cy = c.y - p.y;
        double det = ax*(by*(cx*cx+cy*cy) - cy*(bx*bx+by*by))
                   - ay*(bx*(cx*cx+cy*cy) - cx*(bx*bx+by*by))
                   + (ax*ax+ay*ay)*(bx*cy - by*cx);
        return det > 0;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  CONVERSIÓN DE COORDENADAS
    // ════════════════════════════════════════════════════════════════════════

    // El DSM del IGN usa coordenadas geográficas (lon, lat) en EPSG:4326
    // Convertimos a Unity XZ con el mismo sistema que el OSM generator

    float ColToUnityX(int col)
    {
        float lon = _dsmXllCorner + col * _dsmCellSize;
        return (float)((lon - LON0) * M_PER_DEG_LON) + OX;
    }

    float RowToUnityZ(int row)
    {
        // En ASCII Grid, row 0 = fila superior (mayor latitud)
        float lat = _dsmYllCorner + (_dsmNrows - 1 - row) * _dsmCellSize;
        return (float)((lat - LAT0) * M_PER_DEG_LAT) + OZ;
    }

    int UnityXToCol(float x)
    {
        float lon = (x - OX) / M_PER_DEG_LON + (float)LON0;
        return Mathf.RoundToInt((lon - _dsmXllCorner) / _dsmCellSize);
    }

    int UnityZToRow(float z)
    {
        float lat = (z - OZ) / M_PER_DEG_LAT + (float)LAT0;
        return _dsmNrows - 1 - Mathf.RoundToInt((lat - _dsmYllCorner) / _dsmCellSize);
    }

    // ── Point-in-polygon (ray casting) ───────────────────────────────────

    static bool PuntoEnPoligono(Vector2 p, Vector2[] poly)
    {
        bool inside = false;
        int n = poly.Length;
        for (int i = 0, j = n-1; i < n; j = i++)
        {
            float xi = poly[i].x, yi = poly[i].y;
            float xj = poly[j].x, yj = poly[j].y;
            if (((yi > p.y) != (yj > p.y)) &&
                (p.x < (xj - xi) * (p.y - yi) / (yj - yi) + xi))
                inside = !inside;
        }
        return inside;
    }
}
