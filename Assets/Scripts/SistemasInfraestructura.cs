// SistemasInfraestructura.cs — Infraestructura de Alsasua
// SistemaFarolas · SistemaFerroviario · TrenEnMovimiento · SistemaEdificios
// SonidosAmbienteAltsasu · HUDJugador · SistemaPostProcesoAAA
// SistemaTerreno · SistemaMusica · SistemaLuzHDRP · EnemigoPatrulla · SistemaBombas · SistemaBarricadas

using UnityEngine;
using UnityEngine.AI;
using System.Collections;

// ─────────────────────────────────────────────────────────────────────────────
//  FAROLAS
// ─────────────────────────────────────────────────────────────────────────────
public class SistemaFarolas : MonoBehaviour
{
    public GameObject prefabsFarola;
    public int numFarolas = 80;
    void Start() => AlsasuaLogger.Info("Farolas", $"Iniciado: {numFarolas} farolas");
}

// ─────────────────────────────────────────────────────────────────────────────
//  FERROVIARIO
// ─────────────────────────────────────────────────────────────────────────────
public class SistemaFerroviario : MonoBehaviour
{
    public Transform[] waypoints;
    void Start() => AlsasuaLogger.Info("Ferroviario", "Iniciado");
}

// ─────────────────────────────────────────────────────────────────────────────
//  TREN
// ─────────────────────────────────────────────────────────────────────────────
public class TrenEnMovimiento : MonoBehaviour
{
    public float velocidad = 15f;
    public Transform[] ruta;
    int _idx;

    void Start() => AlsasuaLogger.Info("Tren", "Tren iniciado");

    void Update()
    {
        if (ruta == null || ruta.Length == 0) return;
        transform.position = Vector3.MoveTowards(
            transform.position, ruta[_idx].position, velocidad * Time.deltaTime);
        if (Vector3.Distance(transform.position, ruta[_idx].position) < 1f)
            _idx = (_idx + 1) % ruta.Length;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  EDIFICIOS
// ─────────────────────────────────────────────────────────────────────────────
public class SistemaEdificios : MonoBehaviour
{
    public int numEdificios => transform.childCount;
    void Start() => AlsasuaLogger.Info("Edificios", $"Sistema de edificios activo");
}

// ─────────────────────────────────────────────────────────────────────────────
//  SONIDOS AMBIENTE
// ─────────────────────────────────────────────────────────────────────────────
public class SonidosAmbienteAltsasu : MonoBehaviour
{
    public AudioClip clipTrafico;
    public AudioClip clipAves;
    public AudioClip clipViento;
    AudioSource _src;

    void Start()
    {
        _src = gameObject.AddComponent<AudioSource>();
        _src.loop = true; _src.spatialBlend = 0f; _src.volume = 0.3f;
        AlsasuaLogger.Info("Audio", "Sonidos de ambiente iniciados");
    }

    public void ActualizarSegunNivel(int nivel)
    {
        if (_src == null) return;
        // Más tensión en wanted alto
        _src.volume = Mathf.Lerp(0.3f, 0.6f, nivel / 5f);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  HUD JUGADOR (munición, mira, daño)
// ─────────────────────────────────────────────────────────────────────────────
public class HUDJugador : MonoBehaviour
{
    // Datos que muestra
    public int   municionActual  = 30;
    public int   municionReserva = 90;
    public float vida            = 100f;
    public float vidaMax         = 100f;
    bool  _mostrando = true;

    void OnGUI()
    {
        if (!_mostrando) return;
        // Munición esquina inferior derecha
        GUI.color = Color.white;
        var estilo = new GUIStyle(GUI.skin.label)
        { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
        GUI.Label(new Rect(Screen.width - 160, Screen.height - 50, 150, 36),
            $"{municionActual} / {municionReserva}", estilo);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  POST PROCESO AAA
// ─────────────────────────────────────────────────────────────────────────────
public class SistemaPostProcesoAAA : MonoBehaviour
{
    void Start() => AlsasuaLogger.Info("PostProceso", "HDRP post-proceso activo");
    public static void FlashExplosion() { } // llamado desde SistemaExplosion
}

// ─────────────────────────────────────────────────────────────────────────────
//  TERRENO
// ─────────────────────────────────────────────────────────────────────────────
public class SistemaTerreno : MonoBehaviour
{
    void Start() => AlsasuaLogger.Info("Terreno", "Terreno iniciado");
}

// ─────────────────────────────────────────────────────────────────────────────
//  MÚSICA ADAPTATIVA
// ─────────────────────────────────────────────────────────────────────────────
public class SistemaMusica : MonoBehaviour
{
    public static SistemaMusica Instance { get; private set; }
    public AudioClip stingEvento;
    AudioSource _src;

    void Awake() { if (Instance && Instance != this) { Destroy(this); return; } Instance = this; }
    void Start()
    {
        _src = gameObject.AddComponent<AudioSource>();
        _src.spatialBlend = 0f;
        AlsasuaLogger.Info("Musica", "Sistema de música iniciado");
    }

    public static void TocarEvento(AudioClip clip)
    {
        if (Instance == null || clip == null) return;
        Instance._src?.PlayOneShot(clip, 0.7f);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  LUZ HDRP
// ─────────────────────────────────────────────────────────────────────────────
public class SistemaLuzHDRP : MonoBehaviour
{
    void Start() => AlsasuaLogger.Info("Luz", "Iluminación HDRP iniciada");
}

// ─────────────────────────────────────────────────────────────────────────────
//  ENEMIGO PATRULLA
// ─────────────────────────────────────────────────────────────────────────────
public class EnemigoPatrulla : MonoBehaviour
{
    NavMeshAgent _agente;
    int _vida = 100;

    void Start()
    {
        _agente = GetComponent<NavMeshAgent>();
        if (_agente == null) _agente = gameObject.AddComponent<NavMeshAgent>();
    }

    public void RecibirDano(int cantidad)
    {
        _vida -= cantidad;
        if (_vida <= 0) SistemaRagdoll.Activar(transform, 3f);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  BOMBAS
// ─────────────────────────────────────────────────────────────────────────────
public class SistemaBombas : MonoBehaviour
{
    public float radioBomba  = 8f;
    public float fuerzaBomba = 350f;
    public float danoBomba   = 90f;

    readonly System.Collections.Generic.List<Vector3> _bombasColocadas = new();

    void Update()
    {
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) return;
        if (kb.bKey.wasPressedThisFrame) ColocarBomba();
        if (kb.gKey.wasPressedThisFrame) DetonarUltima();
    }

    public void ColocarBomba()
    {
        var j = AltsasuCore.Jugador;
        if (j == null) return;
        _bombasColocadas.Add(j.position);
        AlsasuaLogger.Info("Bombas", $"Bomba colocada en {j.position}");
    }

    public void DetonarUltima()
    {
        if (_bombasColocadas.Count == 0) return;
        var pos = _bombasColocadas[^1];
        _bombasColocadas.RemoveAt(_bombasColocadas.Count - 1);
        SistemaExplosion.Explotar(pos, radioBomba, fuerzaBomba, danoBomba);
    }

    public void DetonarTodas()
    {
        foreach (var pos in _bombasColocadas)
            SistemaExplosion.Explotar(pos, radioBomba, fuerzaBomba, danoBomba);
        _bombasColocadas.Clear();
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  BARRICADAS
// ─────────────────────────────────────────────────────────────────────────────
public class SistemaBarricadas : MonoBehaviour
{
    public GameObject prefabBarricada;
    readonly System.Collections.Generic.List<GameObject> _barricadas = new();

    public void AsignarPrefabs(GameObject hormigon, GameObject metal, GameObject vfxFuego)
        => prefabBarricada = hormigon ?? metal;

    public void PrenderTodas()
    {
        foreach (var b in _barricadas) if (b != null) b.GetComponent<BarricadaFuego>()?.PrenderFuego();
    }

    public void DanoArea(Vector3 centro, float radio, int daño)
    {
        foreach (var b in _barricadas)
        {
            if (b == null) continue;
            if (Vector3.Distance(b.transform.position, centro) <= radio)
                b.GetComponent<BarricadaFuego>()?.RecibirDano(daño);
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  BARRICADA DE FUEGO (usada por SistemaExplosion)
// ─────────────────────────────────────────────────────────────────────────────
public class BarricadaFuego : MonoBehaviour
{
    float _vida = 200f;
    public void RecibirDano(float cantidad) => RecibirDaño(cantidad);
    public void RecibirDaño(float cantidad)
    {
        _vida -= cantidad;
        if (_vida <= 0) Destroy(gameObject, 0.5f);
    }
    public void PrenderFuego() => AlsasuaLogger.Info("Barricada", "Fuego encendido");
}
