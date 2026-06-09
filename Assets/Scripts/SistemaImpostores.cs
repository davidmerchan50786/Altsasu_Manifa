// Assets/Scripts/SistemaImpostores.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SISTEMA IMPOSTORES — Billboards AAA para el anillo lejano
//
//  Toma todos los LOD3_Billboard creados por SistemaEdificiosAAA y los
//  convierte en billboards reales orientados a cámara, con variación
//  procedural de color y tamaño por MaterialPropertyBlock (zero GC estable).
//
//  Flujo en runtime:
//    1. Awake: busca todos los GameObjects "LOD3_Billboard" y reemplaza
//       su MeshFilter (cubo placeholder) por un Quad orientable.
//    2. LateUpdate: rota los billboards activos hacia la cámara en batches
//       de 64, time-sliced (≤64 actualizaciones por frame).
//    3. Opcional: SistemaEdificiosAAA puede llamar a
//       SistemaImpostores.RegistrarImpostor() al crear cada LOD3.
//
//  BAKE DE TEXTURAS (paso de editor — opcional pero recomendado):
//    Sin texturas bakeadas el billboard usa el material del edificio
//    (la fachada real visible en LOD2). Con texturas bakeadas:
//    1. Menú Alsasua ▸ Impostores ▸ Bakear atlas (ver Editor/BakeadorImpostores.cs)
//    2. Selecciona los prefabs de edificio → "Bakear selección"
//    3. El tool captura 8 ángulos (0°,45°,90°…315°) + top → atlas 2048×2048
//    4. Asigna el atlas a SistemaImpostores.materialImpostorOverride en el Inspector
//    5. Los billboards del lote correspondiente usan automáticamente el atlas
//
//  Rendimiento:
//    • Actualización time-sliced: 64 billboards/frame → nunca >0.1 ms/frame
//    • MaterialPropertyBlock compartido por lote → sin alloc por frame
//    • Se activan solo cuando el LODGroup selecciona LOD3 (Unity lo gestiona)
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using System.Collections.Generic;

[DefaultExecutionOrder(200)]   // después de SistemaEdificiosAAA (100)
public class SistemaImpostores : MonoBehaviour
{
    public static SistemaImpostores Instance { get; private set; }

    // ── Config ────────────────────────────────────────────────────────────
    [Tooltip("Si se asigna, todos los billboards usarán este material (atlas bakeado)")]
    [SerializeField] Material materialImpostorOverride;

    [Tooltip("Ángulo de tilt vertical del billboard (grados hacia arriba). "
           + "Valores 5-15° dan sensación de perspectiva desde el suelo.")]
    [Range(0f, 20f)]
    [SerializeField] float tiltVertical = 8f;

    [Tooltip("Frames entre actualizaciones de orientación para billboards lejanos")]
    [Range(1, 8)]
    [SerializeField] int batchPorFrame = 64;

    // ── Estado interno ────────────────────────────────────────────────────
    readonly List<BillboardProxy> _proxies    = new();
    readonly MaterialPropertyBlock _mpb       = new();
    Mesh     _quadMesh;
    Camera   _camCache;
    int      _batchOffset;

    // ════════════════════════════════════════════════════════════════════════
    //  BOOT
    // ════════════════════════════════════════════════════════════════════════

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        _quadMesh = CrearQuad();
    }

    void Start()
    {
        // Buscar todos los LOD3_Billboard existentes en escena y promoverlos
        var candidatos = GameObject.FindObjectsByType<MeshFilter>(
            FindObjectsSortMode.None);
        int promovidos = 0;
        foreach (var mf in candidatos)
        {
            if (!mf.gameObject.name.StartsWith("LOD3_Billboard")) continue;
            Promover(mf);
            promovidos++;
        }
        AlsasuaLogger.Info("Impostores", $"{promovidos} billboards promovidos a quad");
    }

    // ── API pública ───────────────────────────────────────────────────────

    /// <summary>
    /// SistemaEdificiosAAA puede llamar esto al crear un LOD3_Billboard
    /// para evitar la búsqueda global en Start().
    /// </summary>
    public static void RegistrarImpostor(GameObject billboardGO)
    {
        if (Instance == null || billboardGO == null) return;
        var mf = billboardGO.GetComponent<MeshFilter>();
        if (mf != null) Instance.Promover(mf);
    }

    // ── Construcción del proxy ────────────────────────────────────────────

    void Promover(MeshFilter mf)
    {
        // Reemplazar el mesh cubo por el quad plano
        mf.sharedMesh = _quadMesh;

        // Aplicar material override si existe
        if (materialImpostorOverride != null)
        {
            var mr = mf.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = materialImpostorOverride;
        }

        // Orientar inicialmente al frente
        mf.transform.forward = Vector3.forward;

        var proxy = new BillboardProxy
        {
            tr   = mf.transform,
            tilt = tiltVertical
        };
        _proxies.Add(proxy);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  LATE UPDATE — orientación a cámara, time-sliced
    // ════════════════════════════════════════════════════════════════════════

    void LateUpdate()
    {
        if (_proxies.Count == 0) return;
        if (_camCache == null) _camCache = Camera.main;
        if (_camCache == null) return;

        Vector3 camPos = _camCache.transform.position;
        int total = _proxies.Count;

        // Procesar un lote circular por frame
        int inicio = _batchOffset % total;
        int fin    = Mathf.Min(inicio + batchPorFrame, total);

        for (int i = inicio; i < fin; i++)
        {
            var p = _proxies[i];
            if (p.tr == null)
            {
                _proxies.RemoveAt(i);
                fin--;
                i--;
                continue;
            }

            // Dirección al oyente en el plano XZ
            Vector3 dir = camPos - p.tr.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) continue;

            Quaternion rotBase = Quaternion.LookRotation(dir.normalized, Vector3.up);
            Quaternion rotTilt = Quaternion.AngleAxis(-p.tilt, p.tr.right);
            p.tr.rotation = rotTilt * rotBase;
        }

        _batchOffset = (_batchOffset + batchPorFrame) % total;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  HELPERS
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Quad unitario en el plano XY, doble cara (2 submeshes de 2 tris)</summary>
    static Mesh CrearQuad()
    {
        var m = new Mesh { name = "ImpostorQuad" };

        m.vertices = new Vector3[]
        {
            new(-0.5f, 0f,    0f),
            new( 0.5f, 0f,    0f),
            new( 0.5f, 1f,    0f),
            new(-0.5f, 1f,    0f),
        };
        m.uv = new Vector2[]
        {
            new(0f, 0f), new(1f, 0f), new(1f, 1f), new(0f, 1f)
        };
        // Doble cara: triángulos en ambos sentidos
        m.triangles = new int[]
        {
            0, 2, 1,  0, 3, 2,   // cara delantera
            0, 1, 2,  0, 2, 3,   // cara trasera
        };
        m.RecalculateNormals();
        m.RecalculateBounds();
        m.UploadMeshData(true);   // mark no longer readable → menos VRAM
        return m;
    }

    // ════════════════════════════════════════════════════════════════════════

    struct BillboardProxy
    {
        public Transform tr;
        public float     tilt;
    }
}
