// Assets/Scripts/_Impostores~/ImpostorBillboard.cs  (STAGING/DRAFT — Unity no compila carpetas con ~)
// ─────────────────────────────────────────────────────────────────────────────
//  Fase 2: impostor en runtime. Un quad orientado a cámara que muestra la vista
//  yaw del atlas más cercana a la dirección cámara→edificio. La selección de
//  vista y el billboard se calculan en CPU (robusto); el shader solo muestrea la
//  celda del atlas (ver ImpostorUnlit.shader → convertir a ShaderGraph HDRP Unlit).
//
//  Lo crea/destruye el streamer en la banda media (ver LEEME_impostores.md):
//    var imp = ImpostorBillboard.Crear(atlasSO, idOSM, parent);
//    ...al volver a 'Activo': Destroy(imp.gameObject);
// ─────────────────────────────────────────────────────────────────────────────
using UnityEngine;

[ExecuteAlways]
public class ImpostorBillboard : MonoBehaviour
{
    public ImpostorAtlasSO atlas;
    public long id;

    static Mesh _quad;            // quad compartido (pivote en la base, 0..1 en Y)
    static Material _matBase;     // material compartido (shader unlit del atlas)
    MeshRenderer _mr;
    MaterialPropertyBlock _mpb;
    ImpostorAtlasSO.Entrada _e;
    bool _ok;

    static readonly int IdAtlas = Shader.PropertyToID("_Atlas");
    static readonly int IdUvCell = Shader.PropertyToID("_UvCell");

    public static ImpostorBillboard Crear(ImpostorAtlasSO atlas, long id, Transform parent = null)
    {
        var go = new GameObject($"Impostor_{id}");
        if (parent) go.transform.SetParent(parent, false);
        var imp = go.AddComponent<ImpostorBillboard>();
        imp.atlas = atlas; imp.id = id;
        imp.Inicializar();
        return imp;
    }

    void OnEnable() { if (!_ok) Inicializar(); }

    void Inicializar()
    {
        if (atlas == null || !atlas.TryGet(id, out _e)) { enabled = false; return; }

        if (_quad == null) _quad = CrearQuad();
        if (_matBase == null)
        {
            var sh = Shader.Find("Alsasua/ImpostorUnlit");
            _matBase = new Material(sh != null ? sh : Shader.Find("Unlit/Transparent"));
        }

        var mf = GetComponent<MeshFilter>() ?? gameObject.AddComponent<MeshFilter>();
        mf.sharedMesh = _quad;
        _mr = GetComponent<MeshRenderer>() ?? gameObject.AddComponent<MeshRenderer>();
        _mr.sharedMaterial = _matBase;
        _mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // sombra fake = fase 4
        _mpb = new MaterialPropertyBlock();
        transform.localScale = new Vector3(_e.anchoMundo, _e.altoMundo, 1f);
        _ok = true;
        Orientar(); // primera pose
    }

    void LateUpdate() => Orientar();

    void Orientar()
    {
        if (!_ok) return;
        var cam = Camera.main;
        if (cam == null) return;

        transform.position = _e.pivotMundo;

        // dirección horizontal cámara→edificio
        Vector3 d = cam.transform.position - transform.position; d.y = 0f;
        if (d.sqrMagnitude < 1e-4f) return;
        d.Normalize();

        // billboard: el quad mira a la cámara, manteniéndose vertical
        transform.rotation = Quaternion.LookRotation(d, Vector3.up);

        // vista yaw más cercana (0 = baker miró desde -Z)
        float ang = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;   // [-180,180]
        if (ang < 0f) ang += 360f;
        int vista = Mathf.RoundToInt(ang / 360f * atlas.vistasYaw) % atlas.vistasYaw;

        Rect uv = atlas.UvDeVista(_e, vista);
        _mpb.SetTexture(IdAtlas, atlas.albedoAtlas);
        _mpb.SetVector(IdUvCell, new Vector4(uv.x, uv.y, uv.width, uv.height));
        _mr.SetPropertyBlock(_mpb);
    }

    static Mesh CrearQuad()
    {
        var m = new Mesh { name = "ImpostorQuad" };
        m.vertices = new[] {
            new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f),
            new Vector3(-0.5f, 1f, 0f), new Vector3(0.5f, 1f, 0f),
        };
        m.uv = new[] { new Vector2(0,0), new Vector2(1,0), new Vector2(0,1), new Vector2(1,1) };
        m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        m.RecalculateBounds();
        return m;
    }
}
