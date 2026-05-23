// SistemaArmasExtendido.cs
// Sistema de armas completo para el contexto del juego.
// Extiende el sistema de Weapons.cs del GTA Unity 1.0.
// Armas: Spray (grafiti), Tirachinas, Cóctel Molotov,
//        Bandera Euskadi, Bomba lapa, Coche bomba.

using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class SistemaArmasExtendido : MonoBehaviour
{
    public enum TipoArma
    {
        Puños = 0,
        Spray,        // grafiti político
        Tirachinas,   // piedras, canicas
        Molotov,      // cóctel molotov
        BanderaEuskadi, // bandera ikurriña (sube apoyo al agitar)
        BombaLapa,    // se pega a cualquier superficie o vehículo
        CocheBomba,   // armar un coche cercano como bomba
        // Armas cortas (del sistema GTA existente se añaden):
        Pistola, Escopeta, Fusil
    }

    [Header("Arma equipada")]
    public TipoArma armaActual = TipoArma.Puños;

    [Header("Tirachinas")]
    public float fuerzaTirachinas = 18f;
    public GameObject prefabPiedra;

    [Header("Molotov")]
    public float fuerzaLanzamiento = 12f;
    public GameObject prefabMolotov;

    [Header("Bomba lapa")]
    public GameObject prefabBombaLapa;
    public float temporizadorLapa = 5f;  // segundos hasta detonar

    [Header("Coche bomba")]
    public float radioDeteccionCoche = 8f;

    [Header("Bandera")]
    public float apoyoPorSegundoAgitando = 0.3f;

    [Header("UI")]
    public Text textoArmaActual;
    public Image iconoArma;
    public Texture2D[] iconosArmas;

    // ── Referencias ────────────────────────────────────────────────────────
    SistemaDestruccion _destruccion;
    SistemaGrafitis    _grafitis;
    SistemaApoyoPopular _apoyo;
    bool _agitandoBandera;
    float _cooldown;
    int _municion; // para tirachinas y molotov

    static readonly int[] MUNICION_INICIAL = { 0, 999, 20, 5, 999, 3, 1, 15, 8, 30 };

    // ── Inventario ────────────────────────────────────────────────────────
    bool[] _tiene = new bool[10];

    void Start()
    {
        _destruccion = SistemaDestruccion.Instance;
        _grafitis    = FindFirstObjectByType<SistemaGrafitis>();
        _apoyo       = SistemaApoyoPopular.Instance;
        _tiene[(int)TipoArma.Puños] = true;
        _tiene[(int)TipoArma.Spray] = true; // siempre disponible
        _municion = MUNICION_INICIAL[(int)armaActual];
        ActualizarUI();
    }

    void Update()
    {
        _cooldown -= Time.deltaTime;

        // Cambio de arma: teclas 1-9 o rueda del ratón (new Input System)
        var kb = UnityEngine.InputSystem.Keyboard.current;
        var ms = UnityEngine.InputSystem.Mouse.current;
        if (kb != null)
        {
            if (kb.digit1Key.wasPressedThisFrame && _tiene[1]) CambiarArma((TipoArma)1);
            if (kb.digit2Key.wasPressedThisFrame && _tiene[2]) CambiarArma((TipoArma)2);
            if (kb.digit3Key.wasPressedThisFrame && _tiene[3]) CambiarArma((TipoArma)3);
            if (kb.digit4Key.wasPressedThisFrame && _tiene[4]) CambiarArma((TipoArma)4);
        }
        if (ms != null)
        {
            float scroll = ms.scroll.ReadValue().y;
            if (scroll != 0) CambiarArmaCiclica(scroll > 0 ? 1 : -1);
            if (ms.leftButton.wasPressedThisFrame && _cooldown <= 0) UsarArma();
            if (ms.leftButton.isPressed && armaActual == TipoArma.BanderaEuskadi) AgitarBandera();
            if (ms.leftButton.wasReleasedThisFrame && _agitandoBandera) _agitandoBandera = false;
        }

        ActualizarUI();
    }

    void UsarArma()
    {
        switch (armaActual)
        {
            case TipoArma.Spray:         UsarSpray();          break;
            case TipoArma.Tirachinas:    LanzarTirachinas();   break;
            case TipoArma.Molotov:       LanzarMolotov();      break;
            case TipoArma.BombaLapa:     ColocarBombaLapa();   break;
            case TipoArma.CocheBomba:    ArmarCocheBomba();    break;
            case TipoArma.BanderaEuskadi:AgitarBandera();      break;
        }
    }

    // ── Spray ─────────────────────────────────────────────────────────────

    void UsarSpray()
    {
        if (_grafitis != null) _grafitis.enabled = true;
        // Toggle el modo pintar del sistema de graffitis
        // (el sistema GrafitIs gestiona el input internamente)
        _cooldown = 0.5f;
    }

    // ── Tirachinas ────────────────────────────────────────────────────────

    void LanzarTirachinas()
    {
        if (_municion <= 0) return;
        _cooldown = 0.8f; _municion--;

        var cam = Camera.main;
        if (cam == null) return;

        GameObject piedra = prefabPiedra != null
            ? Instantiate(prefabPiedra, cam.transform.position + cam.transform.forward, Quaternion.identity)
            : CrearPiedra(cam.transform.position + cam.transform.forward);

        var rb = piedra.GetComponent<Rigidbody>() ?? piedra.AddComponent<Rigidbody>();
        rb.mass = 0.08f;
        rb.linearVelocity = cam.transform.forward * fuerzaTirachinas;
        piedra.AddComponent<ImpactoPiedra>();
        Destroy(piedra, 10f);

        _apoyo?.SumarParanoia(3f);
        GameManagerAltsasua.Instance?.AumentarBusqueda(1);
    }

    static GameObject CrearPiedra(Vector3 pos)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Piedra_Tirachinas"; go.transform.position = pos;
        go.transform.localScale = Vector3.one * 0.06f;
        go.GetComponent<MeshRenderer>().material.color = new Color(0.5f, 0.48f, 0.45f);
        return go;
    }

    // ── Cóctel Molotov ────────────────────────────────────────────────────

    void LanzarMolotov()
    {
        if (_municion <= 0) return;
        _cooldown = 1.2f; _municion--;

        var cam = Camera.main;
        if (cam == null) return;

        // Parábola: fuerza diagonal hacia donde mira la cámara + algo arriba
        Vector3 vel = cam.transform.forward * fuerzaLanzamiento + Vector3.up * 4f;
        _destruccion?.LanzarMolotov(cam.transform.position + cam.transform.forward, vel);

        _apoyo?.SumarParanoia(12f);
        GameManagerAltsasua.Instance?.AumentarBusqueda(2);
    }

    // ── Bomba lapa ────────────────────────────────────────────────────────

    void ColocarBombaLapa()
    {
        if (_municion <= 0) return;
        _cooldown = 1f; _municion--;

        var cam = Camera.main;
        if (cam == null) return;
        if (!Physics.Raycast(cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0)), out var hit, 5f)) return;

        GameObject bomba = prefabBombaLapa != null
            ? Instantiate(prefabBombaLapa, hit.point, Quaternion.LookRotation(-hit.normal))
            : CrearBombaFallback(hit.point, hit.normal);

        // Pegar a la superficie/vehículo
        bomba.transform.SetParent(hit.collider.transform);
        bomba.AddComponent<BombaLapaComponent>().Init(temporizadorLapa, _destruccion, hit.collider.gameObject);

        _apoyo?.SumarParanoia(20f);
        GameManagerAltsasua.Instance?.AumentarBusqueda(3);
        Debug.Log($"[Armas] Bomba lapa colocada en {hit.collider.gameObject.name}. Detona en {temporizadorLapa}s");
    }

    static GameObject CrearBombaFallback(Vector3 pos, Vector3 normal)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "BombaLapa";
        go.transform.position = pos + normal * 0.05f;
        go.transform.localScale = new Vector3(0.15f, 0.04f, 0.15f);
        go.GetComponent<MeshRenderer>().material.color = new Color(0.15f, 0.15f, 0.15f);
        return go;
    }

    // ── Coche bomba ───────────────────────────────────────────────────────

    void ArmarCocheBomba()
    {
        if (_municion <= 0) return;

        // Buscar coche cercano
        var cols = Physics.OverlapSphere(transform.position, radioDeteccionCoche);
        GameObject coche = null;
        foreach (var col in cols)
        {
            if (col.gameObject.name.Contains("Coche") || col.gameObject.name.Contains("Car") || col.gameObject.name.Contains("Sport"))
            { coche = col.gameObject; break; }
        }

        if (coche == null) { Debug.Log("[Armas] No hay coche en rango."); return; }
        _municion--; _cooldown = 2f;

        coche.AddComponent<CocheBombaComponent>().Init(_destruccion);
        Debug.Log($"[Armas] ¡Coche bomba armado: {coche.name}! Pulsa G cerca del coche para detonar.");
        GameManagerAltsasua.Instance?.AumentarBusqueda(4);
    }

    // ── Bandera Euskadi (ikurriña) ────────────────────────────────────────

    void AgitarBandera()
    {
        _agitandoBandera = true;
        _apoyo?.SumarApoyo(apoyoPorSegundoAgitando * Time.deltaTime, "Agitando ikurriña");
    }

    // ── Recoger arma ─────────────────────────────────────────────────────

    public void RecogerArma(TipoArma tipo, int municion = -1)
    {
        _tiene[(int)tipo] = true;
        if (municion > 0) _municion = municion;
        CambiarArma(tipo);
        Debug.Log($"[Armas] Recogida: {tipo}");
    }

    void CambiarArma(TipoArma tipo)
    {
        armaActual = tipo;
        _municion  = MUNICION_INICIAL[(int)tipo];
        ActualizarUI();
    }

    void CambiarArmaCiclica(int dir)
    {
        int n = System.Enum.GetValues(typeof(TipoArma)).Length;
        int siguiente = ((int)armaActual + dir + n) % n;
        while (!_tiene[siguiente] && siguiente != (int)armaActual)
            siguiente = (siguiente + dir + n) % n;
        CambiarArma((TipoArma)siguiente);
    }

    void ActualizarUI()
    {
        string nombre = armaActual switch {
            TipoArma.Spray          => "🎨 SPRAY",
            TipoArma.Tirachinas     => $"🪨 TIRACHINAS [{_municion}]",
            TipoArma.Molotov        => $"🔥 MOLOTOV [{_municion}]",
            TipoArma.BanderaEuskadi => "🏴 IKURRIÑA",
            TipoArma.BombaLapa      => $"💣 BOMBA LAPA [{_municion}]",
            TipoArma.CocheBomba     => "🚗💥 COCHE BOMBA",
            TipoArma.Puños          => "👊 PUÑOS",
            _ => armaActual.ToString()
        };
        if (textoArmaActual != null) textoArmaActual.text = nombre;
    }
}

// ── Componente de impacto de piedra ──────────────────────────────────────

public class ImpactoPiedra : MonoBehaviour
{
    SistemaDestruccion _destr;
    void Start() => _destr = SistemaDestruccion.Instance;

    void OnCollisionEnter(Collision col)
    {
        // Romper cristales al impactar ventana
        if (col.gameObject.name.Contains("Ventana") || col.gameObject.name.Contains("Window") || col.gameObject.name.Contains("Glass"))
            _destr?.RomperCristales(transform.position, col.contacts[0].normal);

        // Daño via IDamageable — un solo GetComponent cubre jugador, policía y vehículo NPC
        col.gameObject.GetComponent<IDamageable>()?.RecibirDano(15, transform.position, TipoDano.Impacto);
        // Civil: solo huye (no implementa IDamageable)
        col.gameObject.GetComponent<NPCCivil>()?.AlertarDisparo(transform.position);
        Destroy(gameObject);
    }
}

// ── Bomba lapa ────────────────────────────────────────────────────────────

public class BombaLapaComponent : MonoBehaviour
{
    float              _timer;
    SistemaDestruccion _destr;
    GameObject         _objetivo;
    bool               _detonada;

    public void Init(float t, SistemaDestruccion d, GameObject obj) { _timer = t; _destr = d; _objetivo = obj; }

    void Update()
    {
        _timer -= Time.deltaTime;
        if (_timer <= 0 && !_detonada) Detonar();
    }

    void Detonar()
    {
        _detonada = true;
        if (_objetivo != null && (_objetivo.name.Contains("Coche") || _objetivo.name.Contains("Car")))
            _destr?.ExplotarCoche(_objetivo);
        else
        {
            // Explosión genérica
            var destr = _destr ?? SistemaDestruccion.Instance;
            if (destr != null) destr.ExplotarCoche(new GameObject("Explota"), false);
        }
        Destroy(gameObject);
    }
}

// ── Coche bomba ───────────────────────────────────────────────────────────

public class CocheBombaComponent : MonoBehaviour
{
    SistemaDestruccion _destr;
    bool _armado = true;

    public void Init(SistemaDestruccion d) { _destr = d; }

    void Update()
    {
        // G para detonar si el jugador está cerca
        if (!_armado) return;
        var jug = AltsasuCore.Jugador;
        bool cerca = jug != null && Vector3.Distance(transform.position, jug.position) < 6f;
        if (cerca && UnityEngine.InputSystem.Keyboard.current?.gKey.wasPressedThisFrame == true) Detonar();
    }

    void Detonar()
    {
        _armado = false;
        _destr?.ExplotarCoche(gameObject, esCocheBomba: true);
    }
}
