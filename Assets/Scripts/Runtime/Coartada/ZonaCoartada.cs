// Assets/Scripts/_Coartada~/ZonaCoartada.cs  (STAGING/DRAFT — carpeta ~ no se compila)
// ─────────────────────────────────────────────────────────────────────────────
//  Refugio donde el jugador puede "perderse entre la gente" para enfriar el
//  wanted y la paranoia: txosna, sociedad gastronómica, bar, callejón. La
//  `calidad` indica lo bien que tapa (txosna llena = alta; callejón = baja).
//  Va en un GameObject con Collider (se fuerza a trigger).
// ─────────────────────────────────────────────────────────────────────────────
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class ZonaCoartada : MonoBehaviour
{
    [Tooltip("Lo bien que tapa: 1 = txosna a tope, 0.3 = callejón.")]
    [Range(0f, 1f)] public float calidad = 0.7f;
    public string nombre = "Txosna";

    Collider _col;

    void Awake()
    {
        _col = GetComponent<Collider>();
        if (_col) _col.isTrigger = true;
    }

    void OnEnable()  => SistemaCoartada.Registrar(this);
    void OnDisable() => SistemaCoartada.Desregistrar(this);

    /// <summary>True si la posición (mundo) está dentro del refugio.</summary>
    public bool Contiene(Vector3 p)
    {
        if (_col == null) _col = GetComponent<Collider>();
        if (_col == null) return false;
        // ClosestPoint == p ⇒ dentro (funciona con box/sphere/capsule/convex).
        return (_col.ClosestPoint(p) - p).sqrMagnitude < 0.0004f;
    }
}
