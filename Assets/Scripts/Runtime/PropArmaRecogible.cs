// Assets/Scripts/Runtime/PropArmaRecogible.cs
// ═══════════════════════════════════════════════════════════════════════════
//  PROP ARMA RECOGIBLE — objeto en el mundo que el jugador recoge con [E]
//
//  • Implementa IInteractable → ControladorJugador.TentarEntrarVehiculo() lo
//    detecta con OverlapSphereNonAlloc y llama OnInteractuar(jugador).
//  • Pulso emissive: MPB._EmissiveColor oscila sinusoidalmente para que
//    el arma destaque sin crear instancias de Material.
//  • Se destruye al recogerla; no requiere prefab — usa un cubo como fallback.
//
//  USO:
//    // En escena o por código:
//    PropArmaRecogible.Spawn(SistemaArmasExtendido.TipoArma.Pistola, 15, pos);
//
//  El drop automático al morir la policía lo hace PoliciaForalIA.Morir().
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

[DisallowMultipleComponent]
public sealed class PropArmaRecogible : MonoBehaviour, IInteractable
{
    // ── Config ────────────────────────────────────────────────────────────────
    [Header("Arma")]
    public SistemaArmasExtendido.TipoArma tipo    = SistemaArmasExtendido.TipoArma.Pistola;
    [Tooltip("Munición que se añade al recoger. -1 = usar el máximo del arma.")]
    public int municion = -1;

    [Header("Visual")]
    [Tooltip("Color del glow emissive. Naranja para munición, blanco para arma nueva.")]
    public Color colorGlow = new Color(1f, 0.55f, 0.10f);
    [Tooltip("Velocidad del pulso de glow (ciclos por segundo).")]
    public float velocidadPulso = 1.8f;
    [Tooltip("Rotación por segundo en Y (0 = sin rotación).")]
    public float velocidadRotacion = 45f;

    // ── IInteractable ─────────────────────────────────────────────────────────
    public string TextoInteraccion => $"[E] Recoger {NombreArma()}";
    public float  RadioInteraccion => 2.2f;
    public bool   PuedeInteractuar => true;

    public void OnInteractuar(ControladorJugador jugador)
    {
        var armas = jugador.GetComponent<SistemaArmasExtendido>()
                 ?? FindFirstObjectByType<SistemaArmasExtendido>();
        if (armas == null) return;

        armas.RecogerArma(tipo, municion);
        AlsasuaLogger.Info("PropArma", $"Recogida: {NombreArma()} ({municion} balas).");
        Destroy(gameObject);
    }

    // ── Estado ────────────────────────────────────────────────────────────────
    Renderer[]            _renderers;
    MaterialPropertyBlock _mpb;
    float                 _fase;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Awake()
    {
        _renderers = GetComponentsInChildren<Renderer>(true);
        _mpb       = new MaterialPropertyBlock();

        // Si no hay malla visual, crear un cubo de fallback
        if (_renderers.Length == 0)
            CrearVisualizacionFallback();
    }

    void Update()
    {
        // Rotación continua
        if (velocidadRotacion != 0f)
            transform.Rotate(Vector3.up, velocidadRotacion * Time.deltaTime, Space.World);

        // Pulso emissive sinusoidal
        _fase += Time.deltaTime * velocidadPulso;
        float t      = (Mathf.Sin(_fase * Mathf.PI * 2f) + 1f) * 0.5f; // 0..1
        Color glow   = colorGlow * Mathf.Lerp(1f, 4f, t);              // HDR
        foreach (var r in _renderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor("_EmissiveColor", glow);
            r.SetPropertyBlock(_mpb);
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(colorGlow.r, colorGlow.g, colorGlow.b, 0.3f);
        Gizmos.DrawWireSphere(transform.position, RadioInteraccion);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    string NombreArma()
    {
        int idx = (int)tipo;
        return idx < SistemaArmasExtendido.NombresArma.Length
            ? SistemaArmasExtendido.NombresArma[idx]
            : tipo.ToString();
    }

    void CrearVisualizacionFallback()
    {
        var cubo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cubo.transform.SetParent(transform, false);
        cubo.transform.localScale    = new Vector3(0.15f, 0.08f, 0.40f);
        cubo.transform.localPosition = Vector3.zero;
        // Quitar el collider del cubo (el pick-up usa OverlapSphere, no triggers)
        var col = cubo.GetComponent<Collider>();
        if (col) Destroy(col);

        _renderers = GetComponentsInChildren<Renderer>(true);

        // Color de carrocería según tipo de arma
        Color baseColor = tipo switch
        {
            SistemaArmasExtendido.TipoArma.Pistola   => new Color(0.20f, 0.20f, 0.20f),
            SistemaArmasExtendido.TipoArma.Escopeta  => new Color(0.35f, 0.22f, 0.10f),
            SistemaArmasExtendido.TipoArma.Fusil     => new Color(0.18f, 0.25f, 0.18f),
            SistemaArmasExtendido.TipoArma.Molotov   => new Color(0.25f, 0.20f, 0.10f),
            _                                        => new Color(0.20f, 0.20f, 0.20f),
        };
        foreach (var r in _renderers)
        {
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor("_BaseColor", baseColor);
            _mpb.SetColor("_Color",     baseColor);
            r.SetPropertyBlock(_mpb);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  FACTORY — spawn por código (drop de enemigo, misión, etc.)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Crea un prop de arma recogible en <paramref name="posicion"/>.
    /// Si no hay prefab disponible, usa el cubo de fallback integrado.
    /// </summary>
    public static PropArmaRecogible Spawn(
        SistemaArmasExtendido.TipoArma tipo,
        int   municion,
        Vector3 posicion,
        float   alturaOffset = 0.25f)
    {
        // Intentar cargar prefab desde Resources (opcional)
        string nombre = $"Arma_{tipo}";
        var prefab = Resources.Load<GameObject>($"Prefabs/Armas/{nombre}");

        GameObject go;
        if (prefab != null)
        {
            go = Instantiate(prefab, posicion + Vector3.up * alturaOffset, Quaternion.identity);
        }
        else
        {
            go = new GameObject($"PropArma_{tipo}");
            go.transform.position = posicion + Vector3.up * alturaOffset;
        }

        var prop = go.GetComponent<PropArmaRecogible>()
                ?? go.AddComponent<PropArmaRecogible>();
        prop.tipo    = tipo;
        prop.municion = municion;
        return prop;
    }
}
