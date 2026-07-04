// Assets/Scripts/_ClipmapV3~/ClipmapTerrenoV3.cs  (STAGING/DRAFT — carpeta ~ no se compila)
// ─────────────────────────────────────────────────────────────────────────────
//  Holder runtime del clipmap V3. Construye la malla, la engancha bajo el jugador
//  con SNAP a rejilla (sin "swimming") y la dibuja en 1 draw call. La altura la
//  pone el shader de displacement (fase 3) muestreando heightmap_unificado.r16.
//
//  Fase 2 (esto): geometría + follow + 1 material/draw call. Plano (Y=0) hasta
//  enchufar el shader.
//  Fase 3 (pendiente, ver LEEME_clipmapV3.md):
//    - Material/ShaderGraph HDRP que en VERTEX hace SampleLevel del R16 → worldY=q/64.
//    - ServicioTerreno.AlturaMundo muestrea el R16 en CPU (bilineal) → ITerrainService.
//    - Collider-parche que sigue al jugador (física), no 48 TerrainColliders.
// ─────────────────────────────────────────────────────────────────────────────
using UnityEngine;

// NOTA: SIN [ExecuteAlways] a propósito — corría en modo edición y, al reconstruir
// la malla+material en cada recompilación sin liberar los previos, fugaba memoria
// (OOM). El clipmap es terreno de runtime: solo se construye en Play.
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ClipmapTerrenoV3 : MonoBehaviour
{
    [Header("Geometría")]
    public int m = 64;                 // celdas por lado y nivel
    public int niveles = 6;            // anillos concéntricos
    public float cellSize = 1f;        // metros/celda del nivel 0 (más fino)

    [Header("Follow")]
    public Transform jugador;          // si null, usa Camera.main
    [Tooltip("Material de displacement (fase 3). Si null, placeholder plano.")]
    public Material material;

    Mesh _mesh;
    MeshFilter _mf;
    MeshRenderer _mr;
    float _snap;

    void OnEnable()
    {
        _mesh = ConstructorMallaClipmap.Construir(m, niveles, cellSize);
        _mf = GetComponent<MeshFilter>();
        if (_mf == null) _mf = gameObject.AddComponent<MeshFilter>();
        _mr = GetComponent<MeshRenderer>();
        if (_mr == null) _mr = gameObject.AddComponent<MeshRenderer>();
        if (_mf != null) _mf.sharedMesh = _mesh;
        if (material == null)
        {
            var sh = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
            material = new Material(sh) { name = "ClipmapV3_placeholder" };
        }
        if (_mr != null) _mr.sharedMaterial = material;
        // Cargador GPU del heightmap V3 → fija constantes del material (Base, ZMin, Half…)
        GetComponent<CargadorTexturaHeightmapV3>()?.Configurar(material);
        // el nivel más grueso define el paso de snap (evita swimming)
        _snap = cellSize * (1 << (Mathf.Max(1, niveles) - 1));
        Recolocar();
    }

    void LateUpdate() => Recolocar();

    void Recolocar()
    {
        Transform t = jugador != null ? jugador
                    : (Camera.main != null ? Camera.main.transform : null);
        if (t == null) return;
        if (_snap <= 0f) _snap = cellSize * (1 << (Mathf.Max(1, niveles) - 1));
        if (_snap <= 0f) return;                      // sin paso válido, no recolocar

        Vector3 p = t.position;
        // El objetivo aún no tiene posición válida (NaN/Inf en arranque) → no tocar.
        if (float.IsNaN(p.x) || float.IsNaN(p.z) || float.IsInfinity(p.x) || float.IsInfinity(p.z)) return;

        float x = Mathf.Round(p.x / _snap) * _snap;
        float z = Mathf.Round(p.z / _snap) * _snap;
        if (float.IsNaN(x) || float.IsNaN(z)) return; // cinturón y tirantes
        transform.position = new Vector3(x, 0f, z);
        // Origen al shader de displacement: mapea worldXZ → UV del heightmap
        if (material != null && material.HasProperty("_ClipmapOrigen"))
            material.SetVector("_ClipmapOrigen", new Vector4(x, 0f, z, 0f));
    }

    void OnDisable()
    {
        if (Application.isPlaying && _mesh != null) Destroy(_mesh);
    }
}
