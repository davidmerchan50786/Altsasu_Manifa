// Assets/Scripts/SemaforoNodo.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SEMÁFORO NODO — control de tráfico en intersecciones
//
//  Funciona como obstáculo físico para VehiculoNPC:
//    • En ROJO activa un BoxCollider invisible que el raycast frontal de
//      VehiculoNPC detecta y frena el vehículo sin modificar su código.
//    • En VERDE desactiva el collider → camino libre.
//
//  Visual: esfera procedural rojo/naranja/verde sobre el semáforo.
//  Ciclo: Verde → Ámbar → Rojo, con duración configurable.
//
//  Uso:
//    SistemaTrafico crea estos nodos en las intersecciones y los une en
//    grupos: cuando un grupo está verde, los grupos cruzados están en rojo.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

public class SemaforoNodo : MonoBehaviour
{
    // ── Config ────────────────────────────────────────────────────────────
    [SerializeField] float duracionVerde  = 12f;
    [SerializeField] float duracionAmbar  =  3f;
    [SerializeField] float duracionRojo   = 10f;
    [SerializeField] float alturaLuz      =  3.5f;   // m sobre el suelo

    // ── Estado ────────────────────────────────────────────────────────────
    public enum FaseSemaforo { Verde, Ambar, Rojo }
    public FaseSemaforo Fase { get; private set; } = FaseSemaforo.Rojo;

    BoxCollider  _colObstaculo;
    MeshRenderer _mrLuz;
    float        _timer;

    // ── Materiales de luz (creados una sola vez) ──────────────────────────
    static Material _matVerde, _matAmbar, _matRojo;

    // ════════════════════════════════════════════════════════════════════════

    void Awake()
    {
        CrearObstaculo();
        CrearLuzVisual();
        AplicarFase(FaseSemaforo.Rojo);
    }

    // ── API pública ───────────────────────────────────────────────────────

    /// <summary>Arranca el ciclo automático con un offset de fase inicial.</summary>
    public void IniciarCiclo(float offsetInicial = 0f)
    {
        _timer = offsetInicial;
        enabled = true;
    }

    /// <summary>Fuerza una fase (SistemaTrafico puede sincronizar grupos).</summary>
    public void SetFase(FaseSemaforo fase)
    {
        _timer = 0f;
        AplicarFase(fase);
    }

    // ── Ciclo ─────────────────────────────────────────────────────────────

    void Update()
    {
        _timer += Time.deltaTime;
        float duracion = Fase switch
        {
            FaseSemaforo.Verde => duracionVerde,
            FaseSemaforo.Ambar => duracionAmbar,
            _                  => duracionRojo,
        };

        if (_timer >= duracion)
        {
            _timer = 0f;
            FaseSemaforo siguiente = Fase switch
            {
                FaseSemaforo.Verde => FaseSemaforo.Ambar,
                FaseSemaforo.Ambar => FaseSemaforo.Rojo,
                _                  => FaseSemaforo.Verde,
            };
            AplicarFase(siguiente);
        }
    }

    // ── Aplicar fase ──────────────────────────────────────────────────────

    void AplicarFase(FaseSemaforo fase)
    {
        Fase = fase;

        // El collider solo existe en Rojo y Ámbar (frena al vehículo)
        if (_colObstaculo != null)
            _colObstaculo.enabled = fase != FaseSemaforo.Verde;

        // Visual
        if (_mrLuz != null)
        {
            EnsureMateriales();
            _mrLuz.sharedMaterial = fase switch
            {
                FaseSemaforo.Verde => _matVerde,
                FaseSemaforo.Ambar => _matAmbar,
                _                  => _matRojo,
            };
        }
    }

    // ── Construcción ──────────────────────────────────────────────────────

    void CrearObstaculo()
    {
        // BoxCollider justo delante del semáforo (ancho de carril ~3.5 m)
        _colObstaculo = gameObject.AddComponent<BoxCollider>();
        _colObstaculo.size   = new Vector3(3.5f, 2f, 0.3f);
        _colObstaculo.center = new Vector3(0f, 1f, 0f);
        _colObstaculo.isTrigger = false;   // sólido para el raycast de obstáculos
        _colObstaculo.enabled = false;
    }

    void CrearLuzVisual()
    {
        var go = new GameObject("LuzSemaforo");
        go.transform.SetParent(transform);
        go.transform.localPosition = new Vector3(0f, alturaLuz, 0f);
        go.transform.localScale    = Vector3.one * 0.4f;

        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = PrimitivaMesh(PrimitiveType.Sphere);

        _mrLuz = go.AddComponent<MeshRenderer>();
        _mrLuz.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _mrLuz.receiveShadows = false;
    }

    static void EnsureMateriales()
    {
        if (_matVerde != null) return;
        _matVerde = new Material(Shader.Find("HDRP/Unlit")) { name = "SemaforoVerde" };
        _matAmbar = new Material(Shader.Find("HDRP/Unlit")) { name = "SemaforoAmbar" };
        _matRojo  = new Material(Shader.Find("HDRP/Unlit")) { name = "SemaforoRojo"  };
        _matVerde.SetColor("_UnlitColor", new Color(0.1f, 1.0f, 0.1f));
        _matAmbar.SetColor("_UnlitColor", new Color(1.0f, 0.7f, 0.0f));
        _matRojo .SetColor("_UnlitColor", new Color(1.0f, 0.1f, 0.1f));
    }

    static Mesh PrimitivaMesh(PrimitiveType tipo)
    {
        var go = GameObject.CreatePrimitive(tipo);
        var m  = go.GetComponent<MeshFilter>().sharedMesh;
        DestroyImmediate(go);
        return m;
    }
}
