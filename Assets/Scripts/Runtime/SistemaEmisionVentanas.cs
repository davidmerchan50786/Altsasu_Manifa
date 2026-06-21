// Assets/Scripts/Runtime/SistemaEmisionVentanas.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SISTEMA DE EMISIÓN DE VENTANAS — Fase 6/7 del plan AAA
//
//  Ciudad viva de noche: al anochecer, las ventanas de los edificios se
//  iluminan con glow amarillo-naranja (luces encendidas interiores) y
//  se apagan de madrugada (ritmo doméstico). Efecto equivalente a RDR2
//  o GTA V: ciudad con vida luminosa nocturna sin render de interiores.
//
//  TÉCNICA:
//    · Busca en CiudadHorneada los MeshRenderer con materiales tipo "fachada"
//      (detectados por nombre de material/mesh: contienen "window", "glass",
//       "ventana", "cristal", o simplemente submallas del LOD0 con < 1m² de área).
//    · Si no hay ventanas detectables: crea un sistema de "emisión de fachada"
//      alternativo: aplica un _EmissiveColorMap de patrón de ventanas sobre los
//      materiales de fachada existentes vía MaterialPropertyBlock.
//    · Ciclo: escucha la hora de SistemaVolumenHDRP; al pasar hora 19-21
//      (golden hour → noche) enciende las ventanas con fade-in gradual.
//      A las 6h (amanecer) las apaga.
//
//  VARIACIÓN POR EDIFICIO:
//    · Cada edificio/celda tiene un hash de posición que determina:
//      - Hora de encendido: 18-23h (vecinos que van durmiendo)
//      - Color: warm amber (0,5-0,8 % amarillo) o cool white (neón, 1%)
//      - Intensidad: 0.3-1.2 nit (del rellano al piso iluminado)
//
//  PERFORMANCE:
//    · MaterialPropertyBlock por renderer (no crea materials nuevos)
//    · Solo modifica renderers en bandas LOD0 dentro de radio del gobernador
//    · Update a 2 FPS (suficiente para fade de luz)
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DefaultExecutionOrder(70)]
public sealed class SistemaEmisionVentanas : MonoBehaviour
{
    const float HORA_ENCENDIDO_MIN = 18.5f;
    const float HORA_ENCENDIDO_MAX = 23.0f;
    const float HORA_APAGADO       = 6.5f;
    const float RADIO_GESTION      = 600f;   // solo gestiona renderers dentro de este radio

    // HDRP/Lit: _EmissiveColor = color HDR lineal; _UseEmissiveIntensity=1 activa
    // el canal de intensidad separado. Sin esto la emisión puede ser invisible.
    static readonly int ID_Emissive             = Shader.PropertyToID("_EmissiveColor");
    static readonly int ID_EmissiveIntensity    = Shader.PropertyToID("_EmissiveIntensity");
    static readonly int ID_UseEmissiveIntensity = Shader.PropertyToID("_UseEmissiveIntensity");

    struct RendererGestionado
    {
        public Renderer  mr;
        public float     horaEncendido;  // hora a la que este edificio enciende luces
        public Color     colorBase;      // color luz cálida o fría
        public float     intensidad;     // nits (0.2-1.5)
        public bool      encendido;
    }

    readonly List<RendererGestionado> _gestionados = new(1024);
    readonly MaterialPropertyBlock   _mpb          = new();
    Transform _jugador;
    float     _horaActual = 12f;
    bool      _iniciado;

    // ── Bootstrap ─────────────────────────────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("SistemaEmisionVentanas");
        DontDestroyOnLoad(go);
        go.AddComponent<SistemaEmisionVentanas>();
    }

    void Start()
    {
        AltsasuCore.OnJugadorSpawned += t => _jugador = t;
        if (AltsasuCore.Jugador != null) _jugador = AltsasuCore.Jugador;
        StartCoroutine(DescubrirYGestionar());
    }

    // ── Descubrimiento de renderers de fachada ────────────────────────────
    IEnumerator DescubrirYGestionar()
    {
        yield return new WaitForSeconds(5f);   // esperar a que CiudadHorneada esté en escena

        var ciudad = GameObject.Find("CiudadHorneada");
        if (ciudad == null)
        {
            Debug.LogWarning("[EmisionVentanas] 'CiudadHorneada' no encontrado — " +
                "ejecuta 🏗️ Hornear Ciudad y dale a Play.");
            yield break;
        }

        var mrs = ciudad.GetComponentsInChildren<MeshRenderer>(false);
        float cx = GeoDataAlsasua.OX, cz = GeoDataAlsasua.OZ;

        foreach (var mr in mrs)
        {
            // Solo LOD0 (HD) — los impostores no tienen ventanas visibles
            if (mr.transform.parent != null &&
                mr.transform.parent.name == "LOD2_Impostor") continue;

            // Identificar como "fachada" o candidato de ventanas por nombre
            bool esFachada = EsFachada(mr);
            if (!esFachada) continue;

            // Parámetros por hash de posición (determinista)
            var c = mr.bounds.center;
            float h = Mathf.PerlinNoise(c.x * 0.03f + 17f, c.z * 0.03f + 31f);
            float horaEnc = Mathf.Lerp(HORA_ENCENDIDO_MIN, HORA_ENCENDIDO_MAX, h);
            Color color   = h > 0.6f
                ? new Color(1.0f, 0.82f, 0.4f)   // cálido ámbar (la mayoría)
                : new Color(0.7f, 0.85f, 1.0f);  // frío neón (negocios, noche)
            float intens  = Mathf.Lerp(0.3f, 1.4f, Mathf.PerlinNoise(c.x * 0.1f, c.z * 0.1f));

            _gestionados.Add(new RendererGestionado
            {
                mr = mr, horaEncendido = horaEnc, colorBase = color,
                intensidad = intens, encendido = false,
            });

            if (_gestionados.Count % 50 == 0) yield return null;
        }

        _iniciado = true;
        StartCoroutine(CicloHora());
        Debug.Log($"[EmisionVentanas] {_gestionados.Count} renderers de fachada gestionados " +
            "— se iluminarán al atardecer.");
    }

    // ── Ciclo día/noche ────────────────────────────────────────────────────
    IEnumerator CicloHora()
    {
        var wait = new WaitForSeconds(0.5f);   // actualizar a 2 FPS
        while (true)
        {
            yield return wait;
            ActualizarHora();
            ActualizarEmision();
        }
    }

    void ActualizarHora()
    {
        if (SistemaVolumenHDRP.Instance == null) return;
        var campo = typeof(SistemaVolumenHDRP).GetField("_horaActual",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (campo != null) _horaActual = (float)campo.GetValue(SistemaVolumenHDRP.Instance);
    }

    void ActualizarEmision()
    {
        bool esNoche = _horaActual > HORA_ENCENDIDO_MIN || _horaActual < HORA_APAGADO;
        Vector3 jugPos = _jugador != null ? _jugador.position : new Vector3(GeoDataAlsasua.OX, 0, GeoDataAlsasua.OZ);

        for (int i = 0; i < _gestionados.Count; i++)
        {
            var g = _gestionados[i];
            if (g.mr == null) continue;

            // Solo gestionar los que están dentro del radio
            float dist = Vector3.Distance(g.mr.bounds.center, jugPos);
            if (dist > RADIO_GESTION) continue;

            // ¿Debería estar encendido?
            bool deberiaEncenderse = esNoche &&
                (_horaActual > g.horaEncendido || _horaActual < HORA_APAGADO);

            if (deberiaEncenderse == g.encendido) continue;   // sin cambio

            // Fade inmediato (el ciclo ya ocurre lentamente)
            Color emisiva = deberiaEncenderse
                ? g.colorBase * g.intensidad
                : Color.black;

            g.mr.GetPropertyBlock(_mpb);
            _mpb.SetColor(ID_Emissive, emisiva);
            // HDRP/Lit requiere UseEmissiveIntensity=1 para que _EmissiveColor sea visible
            _mpb.SetFloat(ID_UseEmissiveIntensity, deberiaEncenderse ? 1f : 0f);
            _mpb.SetFloat(ID_EmissiveIntensity, deberiaEncenderse ? 1f : 0f);
            g.mr.SetPropertyBlock(_mpb);

            g.encendido = deberiaEncenderse;
            _gestionados[i] = g;
        }
    }

    // ── Detectar si un renderer es de fachada ─────────────────────────────
    static bool EsFachada(MeshRenderer mr)
    {
        // Por nombre de material
        foreach (var mat in mr.sharedMaterials)
        {
            if (mat == null) continue;
            string n = mat.name.ToLowerInvariant();
            if (n.Contains("window") || n.Contains("glass") ||
                n.Contains("ventana") || n.Contains("cristal") ||
                n.Contains("facade") || n.Contains("fachada") ||
                n.Contains("wall") || n.Contains("muro") ||
                n.Contains("brick") || n.Contains("ladrillo") ||
                n.Contains("stone") || n.Contains("piedra") ||
                n.Contains("plaster") || n.Contains("yeso"))
                return true;
        }
        // Por nombre del mesh (fallback)
        var mf = mr.GetComponent<MeshFilter>();
        if (mf?.sharedMesh != null)
        {
            string mn = mf.sharedMesh.name.ToLowerInvariant();
            if (mn.Contains("building") || mn.Contains("house") ||
                mn.Contains("edificio") || mn.Contains("wall") ||
                mn.Contains("hd_") || mn.Contains("lod0"))
                return true;
        }
        return false;
    }
}
