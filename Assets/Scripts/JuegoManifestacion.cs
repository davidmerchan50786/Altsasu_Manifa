// Assets/Scripts/JuegoManifestacion.cs
// ═══════════════════════════════════════════════════════════════════════════
//  Lógica de juego de la manifestación
//
//  Flujo:
//    1. Fase CONCENTRACIÓN  — jugador llega al punto de reunión, apoyo crece
//    2. Fase MARCHA         — columna avanza hacia Herriko Plaza
//    3. Fase CONFRONTACIÓN  — Guardia Civil responde si apoyo > 40%
//    4. Fase VICTORIA       — llegar a la plaza con apoyo ≥ 50%
//    5. Fase DERROTA        — salud=0 o apoyo<10% antes de llegar
//
//  Conecta con: SistemaApoyoPopular, SistemaManifestacion, HUDManifestacion
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using UnityEngine;
using UnityEngine.AI;

[DefaultExecutionOrder(30)]
public class JuegoManifestacion : MonoBehaviour
{
    public static JuegoManifestacion Instance { get; private set; }

    // ── Inspector ───────────────────────────────────────────────────────────

    [Header("Puntos clave (Unity XZ)")]
    public Vector3 puntoReunion  = new(1750f, 0f, 8490f); // Nafarroa Kalea
    public Vector3 puntoPlaza    = new(GeoDataAlsasua.OX, 0f, GeoDataAlsasua.OZ); // Herriko Plaza
    public Vector3 puntoCorte    = new(1850f, 0f, 8540f); // cruce N-1

    [Header("Parámetros de juego")]
    [Range(10, 100)] public int    npcIniciales    = 30;
    [Range(0f, 5f)]  public float  apoyoPorSegundo = 1.2f;  // pasivo mientras activa
    [Range(0f,30f)]  public float  radioActivacion = 25f;   // jugador dentro = cuenta
    [Range(0f,50f)]  public float  apoyoVictoria   = 50f;   // % mínimo para ganar

    [Header("Guardia Civil")]
    public GameObject prefabGuardiaCivil;
    [Range(0f,100f)] public float apoyoActivaGC    = 40f;   // GC aparece a este %
    [Range(3, 20)]   public int   numGuardias      = 6;

    [Header("Player")]
    public Transform jugador;
    [Range(0f,100f)] public float saludMaxima = 100f;

    // ── Estado ─────────────────────────────────────────────────────────────

    public enum Fase { Espera, Concentracion, Marcha, Confrontacion, Victoria, Derrota }

    public Fase       FaseActual { get; private set; } = Fase.Espera;
    public float      Salud      { get; private set; }

    SistemaApoyoPopular  _apoyo;
    SistemaManifestacion _manifa;
    HUDManifestacion     _hud;
    bool _gcSpawneado;
    bool _jugadorEnPlaza;

    // ── Arranque ────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        Salud    = saludMaxima;
    }

    IEnumerator Start()
    {
        // Esperar sistemas críticos
        float t = 0f;
        while ((SistemaApoyoPopular.Instance == null
             || SistemaManifestacion.Instance == null) && t < 10f)
        { yield return new WaitForSeconds(0.5f); t += 0.5f; }

        _apoyo  = SistemaApoyoPopular.Instance;
        _manifa = SistemaManifestacion.Instance;

        // Crear HUD si no existe
        _hud = HUDManifestacion.Instance;
        if (_hud == null)
            _hud = new GameObject("HUDManifestacion").AddComponent<HUDManifestacion>();

        // Encontrar jugador si no asignado
        if (jugador == null)
        {
            var go = GameObject.FindWithTag("Player");
            if (go != null) jugador = go.transform;
        }

        // Fijar la Y de los puntos sobre el terreno
        var ter = Terrain.activeTerrain;
        if (ter != null)
        {
            SetY(ref puntoReunion, ter);
            SetY(ref puntoPlaza,   ter);
            SetY(ref puntoCorte,   ter);
        }

        // Apoyo inicial bajo para que el juego arranque limpio
        if (_apoyo != null) _apoyo.apoyo = 10f;

        _hud.Mostrar(true);
        _hud.SetFase("Concentración");
        _hud.SetObjetivo($"Ve al punto de reunión (Nafarroa Kalea)");

        FaseActual = Fase.Concentracion;
        StartCoroutine(BucleJuego());
    }

    // ── Bucle principal ─────────────────────────────────────────────────────

    IEnumerator BucleJuego()
    {
        while (FaseActual != Fase.Victoria && FaseActual != Fase.Derrota)
        {
            yield return new WaitForSeconds(0.5f);

            if (jugador == null) continue;
            if (_apoyo  == null) continue;

            float apoyo = _apoyo.apoyo;

            // Sincronizar HUD
            _hud.SetApoyo(apoyo);
            _hud.SetSalud(Salud);

            // Comprobar derrota
            if (Salud <= 0f)
            {
                yield return StartCoroutine(Derrota("Has caído"));
                yield break;
            }
            if (apoyo < 5f && FaseActual == Fase.Marcha)
            {
                yield return StartCoroutine(Derrota("El apoyo popular se ha derrumbado"));
                yield break;
            }

            float distReunion = DistXZ(jugador.position, puntoReunion);
            float distPlaza   = DistXZ(jugador.position, puntoPlaza);

            switch (FaseActual)
            {
                case Fase.Concentracion:
                    // Jugador llega al punto de reunión → sumar apoyo y empezar marcha
                    if (distReunion < radioActivacion)
                    {
                        _apoyo.SumarApoyo(apoyoPorSegundo * 0.5f, "Concentración");
                        _hud.SetObjetivo("Lidera la marcha hasta Herriko Plaza");
                        if (apoyo >= 20f)
                        {
                            FaseActual = Fase.Marcha;
                            _hud.SetFase("Marcha");
                            AlsasuaLogger.Info("Manifa", "▶ Marcha iniciada");
                        }
                    }
                    break;

                case Fase.Marcha:
                    // Apoyo crece mientras jugador va con la columna
                    if (distReunion < radioActivacion * 3f || distPlaza < radioActivacion * 5f)
                        _apoyo.SumarApoyo(apoyoPorSegundo * 0.5f, "Marcha avanza");

                    // Spawn Guardia Civil al superar umbral
                    if (!_gcSpawneado && apoyo >= apoyoActivaGC)
                    {
                        yield return StartCoroutine(SpawnGuardiaCivil());
                        FaseActual = Fase.Confrontacion;
                        _hud.SetFase("Confrontación");
                        _hud.SetObjetivo($"Sigue adelante — apoyo: {apoyo:F0}%");
                    }

                    // Victoria: llegar a la plaza con suficiente apoyo
                    if (distPlaza < radioActivacion && apoyo >= apoyoVictoria)
                    {
                        yield return StartCoroutine(Victoria());
                        yield break;
                    }
                    break;

                case Fase.Confrontacion:
                    _apoyo.SumarApoyo(apoyoPorSegundo * 0.3f, "Resistencia");
                    _hud.SetObjetivo($"Llega a la plaza ({distPlaza:F0}m)  apoyo {apoyo:F0}%");

                    if (distPlaza < radioActivacion && apoyo >= apoyoVictoria)
                    {
                        yield return StartCoroutine(Victoria());
                        yield break;
                    }
                    break;
            }
        }
    }

    // ── Victoria / Derrota ──────────────────────────────────────────────────

    IEnumerator Victoria()
    {
        FaseActual = Fase.Victoria;
        if (_apoyo != null) _apoyo.SumarApoyo(30f, "Victoria — plaza alcanzada");
        _hud.SetFase("¡Victoria!");
        _hud.SetObjetivo("¡Herriko Plaza conquistada! Gora Euskal Herria!");
        AlsasuaLogger.Info("Manifa", $"🏆 VICTORIA — apoyo {_apoyo?.apoyo:F0}%");
        _manifa?.BroadcastMessage("OnManifaVictoria", SendMessageOptions.DontRequireReceiver);
        yield return new WaitForSeconds(5f);
        _hud.Mostrar(false);
    }

    IEnumerator Derrota(string razon)
    {
        FaseActual = Fase.Derrota;
        _hud.SetFase("Derrota");
        _hud.SetObjetivo(razon);
        AlsasuaLogger.Warn("Manifa", $"☠ Derrota — {razon}");
        yield return new WaitForSeconds(4f);

        // Reiniciar partida (soft reset sin recargar escena)
        Salud = saludMaxima;
        if (_apoyo != null) _apoyo.apoyo = 10f;
        FaseActual = Fase.Concentracion;
        _hud.SetFase("Concentración");
        _hud.SetObjetivo("Ve al punto de reunión (Nafarroa Kalea)");
        _hud.Mostrar(true);
        StartCoroutine(BucleJuego());
    }

    // ── Spawn Guardia Civil ──────────────────────────────────────────────────

    IEnumerator SpawnGuardiaCivil()
    {
        _gcSpawneado = true;
        AlsasuaLogger.Info("Manifa", $"🚔 Guardia Civil desplegada ({numGuardias} agentes)");

        var prefab = prefabGuardiaCivil
            ?? (SistemaAssets.Instance != null ? SistemaAssets.Instance.GuardiaAleatorio() : null)  // Guardia Civil real
            ?? ConfiguradorAssetsAAA.Instance?.GetPrefabCivil();  // último recurso: civil

        if (prefab == null) yield break;

        // Posicionar GC en el cruce de la N-1 (delante de la marcha)
        var ter = Terrain.activeTerrain;
        for (int i = 0; i < numGuardias; i++)
        {
            var offset = new Vector3(
                UnityEngine.Random.Range(-8f, 8f), 0f,
                UnityEngine.Random.Range(-5f, 5f));
            var pos = puntoCorte + offset;
            if (ter != null) pos.y = ter.SampleHeight(pos) + 0.1f;

            var go = Instantiate(prefab, pos, Quaternion.LookRotation(puntoReunion - pos));
            go.name = $"GuardiaCivil_{i}";

            // Si tiene NavMeshAgent, activarlo
            var nav = go.GetComponent<NavMeshAgent>();
            if (nav != null) nav.enabled = true;

            yield return null;  // un por frame para no bloquear
        }
    }

    // ── Daño al jugador (llamado desde sistema de combate) ───────────────────

    public void RecibirDaño(float cantidad)
    {
        Salud = Mathf.Max(0f, Salud - cantidad);
        _hud?.SetSalud(Salud);
        if (Salud <= 0f)
            StartCoroutine(Derrota("Has caído en combate"));
    }

    public void Curar(float cantidad)
        => Salud = Mathf.Min(saludMaxima, Salud + cantidad);

    // ── Helpers ─────────────────────────────────────────────────────────────

    static float DistXZ(Vector3 a, Vector3 b)
        => Mathf.Sqrt((a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z));

    static void SetY(ref Vector3 p, Terrain ter)
        => p.y = ter.SampleHeight(p) + 0.1f;
}
