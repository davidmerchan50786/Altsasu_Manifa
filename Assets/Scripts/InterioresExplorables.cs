// Assets/Scripts/InterioresExplorables.cs
// ═══════════════════════════════════════════════════════════════════════════
//  INTERIORES EXPLORABLES — habitaciones 3D reales y caminables
//
//  El interior mapping (GeneradorInterioresAAA) da profundidad VISUAL a las
//  1.140 ventanas a coste casi cero. Pero para los pocos edificios CLAVE
//  (bar de la manifestación, comisaría, ayuntamiento, tienda de misión)
//  generamos un interior 3D REAL en el que el jugador entra y camina.
//
//  Estrategia AAA realista: interior "instanciado" (como GTA/Cyberpunk):
//   • La sala se construye una sola vez con suelo/paredes/techo/muebles
//     (primitivas + kit modular si está disponible) en una zona apartada.
//   • Una puerta-trigger en la fachada teletransporta al jugador dentro/fuera
//     con un fundido. Mientras está dentro, el exterior se puede ocultar.
//   • Iluminación HDRP interior (point lights con HDAdditionalLightData).
//
//  Uso: añade este componente a un GameObject vacío y registra puntos con
//  RegistrarPuerta() o deja que auto-detecte por nombre ("Bar_", "Comisaria"…).
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Rendering.HighDefinition;

[DefaultExecutionOrder(-20)]
public class InterioresExplorables : MonoBehaviour
{
    public static InterioresExplorables Instance { get; private set; }

    [Header("Zona de interiores instanciados")]
    [Tooltip("Origen donde se construyen las salas, lejos del mundo visible")]
    public Vector3 origenInteriores = new Vector3(0f, -500f, 0f);
    public float separacionSalas = 40f;

    [Header("Tipos a auto-detectar (por nombre de edificio)")]
    public string[] clavesEdificios = { "BAR", "TABERNA", "COMISARIA", "AYUNT", "TIENDA_MISION" };

    [Header("Fundido")]
    public float duracionFundido = 0.35f;

    Transform _jugador;
    int _salasCreadas;
    readonly List<Sala> _salas = new();
    Coroutine _crTransicion;
    CanvasGroup _fundido;

    public class Sala
    {
        public string  tipo;
        public Transform raiz;       // contenedor de la sala instanciada
        public Vector3   puntoSpawn; // dónde aparece el jugador dentro
        public Vector3   puntoSalida;// dónde reaparece fuera
        public Transform puerta;     // trigger de entrada en la fachada
    }

    Sala _salaActual; // null = jugador fuera

    // ════════════════════════════════════════════════════════════════════════
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void Start()
    {
        var jugadorGO = GameObject.FindWithTag("Player");
        if (jugadorGO != null) _jugador = jugadorGO.transform;
        CrearFundido();
        AutoDetectarEdificiosClave();
    }

    // ── Auto-detección de edificios clave ─────────────────────────────────────
    void AutoDetectarEdificiosClave()
    {
        var raiz = GameObject.Find("Edificios_AAA");
        if (raiz == null) { Debug.LogWarning("[InterioresExplorables] No hay 'Edificios_AAA'."); return; }

        int creados = 0;
        foreach (Transform hijo in raiz.transform)
        {
            if (hijo == null || creados >= 12) continue; // límite sensato
            string n = hijo.name.ToUpperInvariant();
            string tipo = TipoSegunNombre(n);
            if (tipo == null) continue;
            CrearPuertaYSala(hijo, tipo);
            creados++;
        }
        Debug.Log($"[InterioresExplorables] Interiores explorables creados: {creados}");
    }

    string TipoSegunNombre(string n)
    {
        foreach (var c in clavesEdificios)
            if (n.Contains(c)) return c;
        return null;
    }

    // ── Construcción de puerta + sala ─────────────────────────────────────────
    void CrearPuertaYSala(Transform edificio, string tipo)
    {
        Transform fachada = EncontrarFachada(edificio);
        if (fachada == null) return;

        // Puerta-trigger en la base de la fachada
        var puertaGO = new GameObject($"Puerta_{tipo}_{edificio.name}");
        puertaGO.transform.position = fachada.position - fachada.forward * 0.4f
                                      + Vector3.up * 1.0f;
        puertaGO.transform.rotation = Quaternion.LookRotation(fachada.forward);
        var box = puertaGO.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(2f, 2.2f, 1.2f);

        // Construir sala instanciada
        Vector3 origen = origenInteriores + Vector3.right * (separacionSalas * _salasCreadas);
        var sala = ConstruirSala(tipo, origen);
        sala.tipo        = tipo;
        sala.puerta      = puertaGO.transform;
        sala.puntoSalida = fachada.position - fachada.forward * 2.5f + Vector3.up * 1f;
        _salas.Add(sala);
        _salasCreadas++;

        // Disparador
        var trig = puertaGO.AddComponent<DisparadorPuerta>();
        trig.sistema = this;
        trig.sala    = sala;
    }

    Sala ConstruirSala(string tipo, Vector3 origen)
    {
        var raiz = new GameObject($"Sala_{tipo}_{_salasCreadas}").transform;
        raiz.position = origen;
        raiz.SetParent(transform);

        float ancho = 8f, fondo = 8f, alto = 3.2f;

        // Suelo, techo, 4 paredes (con hueco de puerta en la frontal)
        Caja(raiz, "Suelo",  new Vector3(0, 0, 0),          new Vector3(ancho, 0.2f, fondo), ColorTipo(tipo, 1));
        Caja(raiz, "Techo",  new Vector3(0, alto, 0),       new Vector3(ancho, 0.2f, fondo), ColorTipo(tipo, 2));
        Caja(raiz, "ParedF", new Vector3(0, alto/2, fondo/2), new Vector3(ancho, alto, 0.2f), ColorTipo(tipo, 0));
        Caja(raiz, "ParedI", new Vector3(-ancho/2, alto/2, 0), new Vector3(0.2f, alto, fondo), ColorTipo(tipo, 0));
        Caja(raiz, "ParedD", new Vector3(ancho/2, alto/2, 0),  new Vector3(0.2f, alto, fondo), ColorTipo(tipo, 0));
        // Pared frontal partida (hueco de puerta en el centro)
        Caja(raiz, "FrenteIzq", new Vector3(-ancho*0.32f, alto/2, -fondo/2), new Vector3(ancho*0.36f, alto, 0.2f), ColorTipo(tipo, 0));
        Caja(raiz, "FrenteDer", new Vector3( ancho*0.32f, alto/2, -fondo/2), new Vector3(ancho*0.36f, alto, 0.2f), ColorTipo(tipo, 0));
        Caja(raiz, "FrenteSup", new Vector3(0, alto*0.85f, -fondo/2),        new Vector3(ancho, alto*0.3f, 0.2f), ColorTipo(tipo, 0));

        AmueblarSala(raiz, tipo, ancho, fondo);
        AnadirLuzInterior(raiz, origen + Vector3.up * (alto - 0.4f), tipo);

        var sala = new Sala
        {
            raiz       = raiz,
            puntoSpawn = origen + new Vector3(0, 1.0f, -fondo * 0.35f),
        };
        return sala;
    }

    void AmueblarSala(Transform raiz, string tipo, float ancho, float fondo)
    {
        switch (tipo)
        {
            case "BAR":
            case "TABERNA":
                Caja(raiz, "Barra", new Vector3(0, 0.55f, fondo*0.2f), new Vector3(ancho*0.7f, 1.1f, 0.8f), Hex("#5a3010"));
                for (int i = 0; i < 4; i++)
                    Caja(raiz, $"Taburete{i}", new Vector3(-ancho*0.25f + i*1.4f, 0.45f, fondo*0.05f), new Vector3(0.4f,0.9f,0.4f), Hex("#222"));
                break;
            case "COMISARIA":
                Caja(raiz, "Mostrador", new Vector3(0, 0.55f, fondo*0.25f), new Vector3(ancho*0.6f, 1.1f, 0.7f), Hex("#3a4a5a"));
                Caja(raiz, "Banco", new Vector3(0, 0.25f, -fondo*0.25f), new Vector3(ancho*0.5f, 0.5f, 0.5f), Hex("#555"));
                break;
            case "TIENDA_MISION":
                for (int i = 0; i < 3; i++)
                    Caja(raiz, $"Estante{i}", new Vector3(-ancho*0.3f + i*ancho*0.3f, 1.0f, fondo*0.2f), new Vector3(0.6f,2f,1.2f), Hex("#888"));
                break;
            default: // AYUNT u otros
                Caja(raiz, "Mesa", new Vector3(0, 0.5f, 0), new Vector3(ancho*0.5f, 1f, fondo*0.3f), Hex("#6b4a1a"));
                break;
        }
    }

    GameObject Caja(Transform padre, string nombre, Vector3 posLocal, Vector3 escala, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = nombre;
        go.transform.SetParent(padre);
        go.transform.localPosition = posLocal;
        go.transform.localScale    = escala;
        var rend = go.GetComponent<Renderer>();
        var sh = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
        var mat = new Material(sh);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        else mat.color = color;
        mat.enableInstancing = true;
        rend.sharedMaterial = mat;
        return go;
    }

    void AnadirLuzInterior(Transform raiz, Vector3 posMundo, string tipo)
    {
        var go = new GameObject("Luz_Interior");
        go.transform.SetParent(raiz);
        go.transform.position = posMundo;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.range = 14f;
        l.color = tipo == "BAR" ? new Color(1f,0.85f,0.6f) : Color.white;
        var hd = go.AddComponent<HDAdditionalLightData>();
        hd.SetIntensity(3000f, LightUnit.Lux);
    }

    Transform EncontrarFachada(Transform raiz)
    {
        foreach (Transform t in raiz.GetComponentsInChildren<Transform>(true))
        {
            string n = t.name.ToUpperInvariant();
            if (n.Contains("FACHADA") || n.Contains("FRENTE") || n.Contains("PUERTA")) return t;
        }
        var r = raiz.GetComponentsInChildren<Renderer>();
        return r.Length > 0 ? r[0].transform : null;
    }

    // ── Entrar / salir ─────────────────────────────────────────────────────────
    public void Entrar(Sala sala)
    {
        if (_jugador == null || _salaActual != null) return;
        if (_crTransicion != null) StopCoroutine(_crTransicion);
        _crTransicion = StartCoroutine(Transicion(sala.puntoSpawn, sala, true));
    }

    public void Salir()
    {
        if (_jugador == null || _salaActual == null) return;
        var destino = _salaActual.puntoSalida;
        if (_crTransicion != null) StopCoroutine(_crTransicion);
        _crTransicion = StartCoroutine(Transicion(destino, null, false));
    }

    IEnumerator Transicion(Vector3 destino, Sala nueva, bool entrando)
    {
        yield return Fundido(1f);
        var cc = _jugador.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        _jugador.position = destino;
        if (cc != null) cc.enabled = true;
        _salaActual = nueva;
        yield return Fundido(0f);
        _crTransicion = null;
    }

    // ── Fundido a negro ─────────────────────────────────────────────────────────
    void CrearFundido()
    {
        var go = new GameObject("FundidoInteriores");
        go.transform.SetParent(transform);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        _fundido = go.AddComponent<CanvasGroup>();
        _fundido.alpha = 0f;
        _fundido.blocksRaycasts = false;
        var img = new GameObject("Negro");
        img.transform.SetParent(go.transform);
        var rt = img.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        var image = img.AddComponent<UnityEngine.UI.Image>();
        image.color = Color.black;
    }

    IEnumerator Fundido(float target)
    {
        float ini = _fundido.alpha, t = 0f;
        while (t < duracionFundido)
        {
            t += Time.deltaTime;
            _fundido.alpha = Mathf.Lerp(ini, target, t / duracionFundido);
            yield return null;
        }
        _fundido.alpha = target;
    }

    void OnDestroy()
    {
        if (_crTransicion != null) StopCoroutine(_crTransicion);
    }

    static Color ColorTipo(string tipo, int parte)
    {
        // 0=pared, 1=suelo, 2=techo
        switch (tipo)
        {
            case "BAR": case "TABERNA":
                return parte == 1 ? Hex("#2a1810") : parte == 2 ? Hex("#1a1008") : Hex("#3a2414");
            case "COMISARIA":
                return parte == 1 ? Hex("#3a4046") : parte == 2 ? Hex("#dde2e6") : Hex("#aeb6bc");
            default:
                return parte == 1 ? Hex("#8b6914") : parte == 2 ? Hex("#f2ece0") : Hex("#e8dfc8");
        }
    }
    static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

    // ── Trigger de puerta ─────────────────────────────────────────────────────
    public class DisparadorPuerta : MonoBehaviour
    {
        [HideInInspector] public InterioresExplorables sistema;
        [HideInInspector] public Sala sala;

        void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            if (sistema._salaActual == null) sistema.Entrar(sala);
        }
    }
}
