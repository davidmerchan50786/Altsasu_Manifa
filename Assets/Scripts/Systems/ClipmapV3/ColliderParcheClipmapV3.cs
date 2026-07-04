// Assets/Scripts/_ClipmapV3~/ColliderParcheClipmapV3.cs  (STAGING — fuera del build)
// ─────────────────────────────────────────────────────────────────────────────
//  FASE 5 — física del clipmap. Mantiene UN MeshCollider-parche bajo el jugador,
//  generado desde el heightmap V3 (MuestreadorHeightmapV3), que sustituye a los
//  48 TerrainColliders del Mosaico V2 (el clipmap GPU no tiene colliders).
//
//  El parche es una rejilla NxN de ~tamMundo m que SIGUE al jugador con snap a
//  rejilla: solo se reconstruye cuando el jugador cruza de celda (no cada frame).
//  Los triángulos son fijos; solo se recalculan las alturas de los vértices.
//  Coste: NxN+1 vértices (def. 33² = 1089), cook de Mesh esporádico → barato.
//
//  Lejos del jugador no hace falta collider: la IA y el spawn usan la altura
//  matemática (IMuestreadorAlturaPrecisa), no raycasts. Solo el jugador y la
//  física cercana necesitan superficie sólida.
// ─────────────────────────────────────────────────────────────────────────────
using UnityEngine;

[RequireComponent(typeof(MeshCollider))]
public class ColliderParcheClipmapV3 : MonoBehaviour
{
    [Tooltip("A quién sigue (si null, usa Camera.main).")]
    public Transform jugador;
    [Tooltip("Quads por lado (vértices = celdas+1).")]
    public int celdas = 32;
    [Tooltip("Lado del parche en metros.")]
    public float tamMundo = 128f;

    readonly MuestreadorHeightmapV3 _h = new();
    MeshCollider _mc;
    Mesh _mesh;
    Vector3[] _verts;
    bool _ok;
    Vector2 _centroSnap = new(float.NaN, float.NaN);

    void Awake()
    {
        _ok = _h.Cargar();
        if (!_ok) { AlsasuaLogger.Warn("ColliderV3", "heightmap V3 no disponible — parche inactivo."); enabled = false; return; }
        _mc = GetComponent<MeshCollider>();
        ConstruirTopologia();
    }

    void LateUpdate()
    {
        if (!_ok) return;
        Transform t = jugador != null ? jugador : (Camera.main ? Camera.main.transform : null);
        if (t == null) return;

        float paso = tamMundo / celdas;
        // snap del centro a la rejilla de celdas → reconstruir solo al cruzar celda
        float cx = Mathf.Round(t.position.x / paso) * paso;
        float cz = Mathf.Round(t.position.z / paso) * paso;
        if (cx == _centroSnap.x && cz == _centroSnap.y) return;

        _centroSnap = new Vector2(cx, cz);
        Reconstruir(cx, cz, paso);
    }

    void ConstruirTopologia()
    {
        int n = celdas + 1;
        _verts = new Vector3[n * n];
        var tris = new int[celdas * celdas * 6];
        int ti = 0;
        for (int z = 0; z < celdas; z++)
            for (int x = 0; x < celdas; x++)
            {
                int a = z * n + x, b = a + 1, c = a + n, d = c + 1;
                tris[ti++] = a; tris[ti++] = c; tris[ti++] = b;
                tris[ti++] = b; tris[ti++] = c; tris[ti++] = d;
            }
        _mesh = new Mesh { name = "ColliderParcheV3" };
        _mesh.MarkDynamic();
        _mesh.vertices = _verts;       // se rellenan en el primer Reconstruir
        _mesh.triangles = tris;        // fijos para siempre
    }

    void Reconstruir(float cx, float cz, float paso)
    {
        int n = celdas + 1;
        float half = tamMundo * 0.5f;
        // El GameObject se coloca en el centro snap (Y=0); vértices en local.
        transform.position = new Vector3(cx, 0f, cz);

        for (int z = 0; z < n; z++)
        {
            float lz = -half + z * paso;
            float wz = cz + lz;
            for (int x = 0; x < n; x++)
            {
                float lx = -half + x * paso;
                float wx = cx + lx;
                _verts[z * n + x] = new Vector3(lx, _h.AlturaMundo(wx, wz), lz);
            }
        }

        _mesh.vertices = _verts;
        _mesh.RecalculateBounds();
        // Forzar el re-cook del MeshCollider con la malla nueva.
        _mc.sharedMesh = null;
        _mc.sharedMesh = _mesh;
    }

    void OnDestroy()
    {
        if (Application.isPlaying && _mesh != null) Destroy(_mesh);
    }
}
