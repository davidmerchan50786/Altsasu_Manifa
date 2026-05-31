// Assets/Scripts/BarricadaFuego.cs
// Componente de barricada con soporte de fuego y daño.
// Usado por SistemaBarricadas para gestionar el estado de cada barricada.

using UnityEngine;

public class BarricadaFuego : MonoBehaviour
{
    [SerializeField] private GameObject prefabVFXFuego;

    [Header("Estado")]
    public float vidaMaxima   = 150f;
    public float vidaActual   = 150f;
    public bool  estaArdiendo = false;

    GameObject _vfxInstancia;
    ParticleSystem _humo;
    Light          _luzFuego;

    // ── API pública ────────────────────────────────────────────────────────

    public void PrenderFuego()
    {
        if (estaArdiendo) return;
        estaArdiendo = true;

        if (prefabVFXFuego != null)
        {
            _vfxInstancia = Instantiate(prefabVFXFuego, transform.position + Vector3.up * 0.5f, Quaternion.identity, transform);
        }
        else
        {
            // Fuego procedural si no hay VFX asset
            var go = new GameObject("Fuego_Barricada");
            go.transform.SetParent(transform);
            go.transform.localPosition = Vector3.up * 0.6f;

            _humo = go.AddComponent<ParticleSystem>();
            var main = _humo.main;
            main.startColor    = new Color(1f, 0.4f, 0.05f, 0.8f);
            main.startSize     = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
            main.startSpeed    = new ParticleSystem.MinMaxCurve(1f, 2.5f);
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.5f);
            main.loop          = true;
            main.maxParticles  = 60;

            var emission = _humo.emission;
            emission.rateOverTime = 30f;

            var shape = _humo.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius    = 0.4f;

            _humo.Play();

            // Luz dinámica de fuego
            _luzFuego = go.AddComponent<Light>();
            _luzFuego.type      = LightType.Point;
            _luzFuego.color     = new Color(1f, 0.5f, 0.1f);
            _luzFuego.intensity = 2f;
            _luzFuego.range     = 5f;
        }
    }

    public void ApagarFuego()
    {
        estaArdiendo = false;
        if (_vfxInstancia != null) Destroy(_vfxInstancia);
        if (_humo != null) _humo.Stop();
        if (_luzFuego != null) _luzFuego.enabled = false;
    }

    public void RecibirDano(float cantidad) => RecibirDaño(cantidad);
    public void RecibirDaño(float cantidad)
    {
        vidaActual -= cantidad;
        if (vidaActual <= 0f) Destruir();
        else if (vidaActual < vidaMaxima * 0.5f && !estaArdiendo) PrenderFuego();
    }

    public void Destruir()
    {
        ApagarFuego();
        // Pequeña explosión visual
        var exp = new GameObject("Explosion_Barricada");
        exp.transform.position = transform.position;
        var ps = exp.AddComponent<ParticleSystem>();
        var m  = ps.main;
        m.startColor    = new Color(0.8f, 0.3f, 0f, 1f);
        m.startSize     = new ParticleSystem.MinMaxCurve(0.5f, 1.5f);
        m.startSpeed    = new ParticleSystem.MinMaxCurve(3f, 7f);
        m.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.2f);
        m.loop          = false;
        m.maxParticles  = 40;
        ps.Play();
        Destroy(exp, 2f);
        Destroy(gameObject, 0.1f);
    }

    void Update()
    {
        // Parpadeo de la luz de fuego
        if (_luzFuego != null && estaArdiendo)
            _luzFuego.intensity = 1.8f + Mathf.Sin(Time.time * 7f) * 0.4f;
    }

    // ── Factory estática ──────────────────────────────────────────────────

    /// <summary>Crea una barricada procedural (sin prefab) en la posición dada.</summary>
    public static BarricadaFuego Crear(Vector3 posicion, float rotacionY)
    {
        var go = new GameObject("Barricada_Proc");
        go.transform.position = posicion;
        go.transform.rotation = Quaternion.Euler(0f, rotacionY, 0f);

        // Caja visual (palés apilados)
        for (int i = 0; i < 3; i++)
        {
            var caja = GameObject.CreatePrimitive(PrimitiveType.Cube);
            caja.name = $"Pale_{i}";
            caja.transform.SetParent(go.transform);
            caja.transform.localPosition  = new Vector3(Random.Range(-0.4f, 0.4f), i * 0.35f, 0f);
            caja.transform.localRotation  = Quaternion.Euler(0f, Random.Range(-15f, 15f), 0f);
            caja.transform.localScale     = new Vector3(1.2f, 0.3f, 0.5f);
            var mat = caja.GetComponent<MeshRenderer>().material;
            mat.color = new Color(0.55f, 0.42f, 0.28f); // madera
        }

        // Collider
        var rb  = go.AddComponent<Rigidbody>();
        rb.mass = 80f;
        rb.isKinematic = true;

        return go.AddComponent<BarricadaFuego>();
    }
}
