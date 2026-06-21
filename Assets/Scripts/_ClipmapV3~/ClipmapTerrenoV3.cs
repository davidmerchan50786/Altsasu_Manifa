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

[ExecuteAlways]
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
        _mf = GetComponent<MeshFilter>() ?? gameObject.AddComponent<MeshFilter>();
        _mr = GetComponent<MeshRenderer>() ?? gameObject.AddComponent<MeshRenderer>();
        _mf.sharedMesh = _mesh;
        if (material == null)
        {
            var sh = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
            material = new Material(sh) { name = "ClipmapV3_placeholder" };
        }
        _mr.sharedMaterial = material;
        // el nivel más grueso define el paso de snap (evita swimming)
        _snap = cellSize * (1 << (niveles - 1));
        Recolocar();
    }

    void LateUpdate() => Recolocar();

    void Recolocar()
    {
        Transform t = jugador != null ? jugador
                    : (Camera.main != null ? Camera.main.transform : null);
        if (t == null) return;
        float x = Mathf.Round(t.position.x / _snap) * _snap;
        float z = Mathf.Round(t.position.z / _snap) * _snap;
        transform.position = new Vector3(x, 0f, z);
        // Nota: cuando exista el shader de displacement, pásale el origen para
        // mapear worldXZ→UV del heightmap:
        //   material.SetVector("_ClipmapOrigen", new Vector4(x, 0, z, 0));
    }

    void OnDisable()
    {
        if (Application.isPlaying && _mesh != null) Destroy(_mesh);
    }
}
