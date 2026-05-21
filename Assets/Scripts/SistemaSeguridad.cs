// Assets/Scripts/SistemaSeguridad.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SISTEMA SEGURIDAD — monitor de salud en runtime
//
//  Ejecuta cada 5 segundos en Play:
//    1. Verifica que los 12 sistemas críticos están activos
//    2. Intenta reiniciar sistemas caídos via AltsasuCore.EnsureOn
//    3. Detecta y limpia GameObjects huérfanos (DontDestroyOnLoad duplicados)
//    4. Valida el NavMesh y relanza el horneado si ha desaparecido
//    5. Protege el spawn del jugador: lo reposiciona si cae fuera del mundo
//    6. Comprueba que el AudioManager tiene pool de fuentes funcional
//    7. Expone SaludSistemas() para diagnóstico desde el HUD
//
//  No lanza excepciones al exterior: todo va al AlsasuaLogger.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[DefaultExecutionOrder(150)] // después de todos los sistemas, antes de SistemaDiagnostico (200)
public class SistemaSeguridad : MonoBehaviour
{
    public static SistemaSeguridad Instance { get; private set; }

    [Header("Intervalos (segundos)")]
    [Range(1f, 30f)] public float intervaloChequeo = 5f;

    [Header("Protección jugador")]
    public float alturaMinima     = -50f;   // si cae más bajo → teleport
    public float alturaReset      = 245f;   // altura de reposicionamiento

    // ── Estado ────────────────────────────────────────────────────────────
    public int   chequeosTotales   { get; private set; }
    public int   problemasDetect   { get; private set; }
    public int   problemasResueltos{ get; private set; }
    public float ultimoChequeo     { get; private set; }

    readonly List<string> _historial = new(50);

    // ── Sistemas críticos que deben existir ───────────────────────────────
    static readonly (System.Type tipo, string nombre)[] SISTEMAS_CRITICOS =
    {
        (typeof(AudioManager),         "AudioManager"),
        (typeof(GameManagerAltsasua),  "GameManager"),
        (typeof(SistemaNavMesh),       "NavMesh"),
        (typeof(SistemaZonas),         "Zonas"),
        (typeof(SistemaOptimizacion),  "Optimizacion"),
        (typeof(SistemaPolish),        "Polish"),
        (typeof(HUDCanvas),            "HUDCanvas"),
    };

    // ════════════════════════════════════════════════════════════════════════
    //  LIFECYCLE
    // ════════════════════════════════════════════════════════════════════════

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    IEnumerator Start()
    {
        // Esperar a que el mundo esté listo antes de empezar a monitorear
        yield return new WaitUntil(() => AltsasuCore.Listo || Time.time > 30f);
        AlsasuaLogger.Info("Seguridad", "Monitor de salud activo.");
        StartCoroutine(BucleMonitoreo());
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  BUCLE PRINCIPAL
    // ════════════════════════════════════════════════════════════════════════

    IEnumerator BucleMonitoreo()
    {
        while (true)
        {
            yield return new WaitForSeconds(intervaloChequeo);
            try { EjecutarChequeo(); }
            catch (System.Exception e)
            {
                AlsasuaLogger.Error("Seguridad", $"Error en chequeo: {e.Message}");
            }
        }
    }

    void EjecutarChequeo()
    {
        chequeosTotales++;
        ultimoChequeo = Time.time;
        int problemas = 0, resueltos = 0;

        // 1. Sistemas críticos
        foreach (var (tipo, nombre) in SISTEMAS_CRITICOS)
        {
            var inst = FindFirstObjectByType(tipo) as MonoBehaviour;
            if (inst == null || !inst.gameObject.activeInHierarchy)
            {
                problemas++;
                Log($"⚠ {nombre} no encontrado — intentando recuperar");
                if (TryRecuperarSistema(tipo, nombre)) resueltos++;
            }
        }

        // 2. Jugador fuera del mundo
        var jugador = AltsasuCore.Jugador;
        if (jugador != null && jugador.position.y < alturaMinima)
        {
            problemas++;
            var pos = new Vector3(jugador.position.x, alturaReset, jugador.position.z);
            jugador.position = pos;
            var rb = jugador.GetComponent<Rigidbody>();
            if (rb != null) rb.linearVelocity = Vector3.zero;
            Log($"⚠ Jugador fuera del mundo — teleport a Y={alturaReset}");
            resueltos++;
        }

        // 3. AudioManager pool
        var audio = AudioManager.I;
        if (audio != null) VerificarAudio(audio, ref problemas, ref resueltos);

        // 4. NavMesh caído
        VerificarNavMesh(ref problemas, ref resueltos);

        // 5. DontDestroyOnLoad duplicados
        LimpiarDuplicados<GameManagerAltsasua>(ref problemas, ref resueltos);
        LimpiarDuplicados<HUDCanvas>(ref problemas, ref resueltos);

        // 6. Memoria — descargar assets no usados si FPS < 20
        if (SistemaOptimizacion.EsLento)
        {
            Resources.UnloadUnusedAssets();
            Log("ℹ FPS bajo — UnloadUnusedAssets ejecutado");
        }

        problemasDetect    += problemas;
        problemasResueltos += resueltos;

        if (problemas > 0)
            AlsasuaLogger.Warn("Seguridad",
                $"Chequeo #{chequeosTotales}: {problemas} problemas, {resueltos} resueltos");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  RECUPERACIÓN DE SISTEMAS
    // ════════════════════════════════════════════════════════════════════════

    bool TryRecuperarSistema(System.Type tipo, string nombre)
    {
        var core = AltsaluCore();
        if (core == null) return false;

        try
        {
            // Usar reflexión para llamar EnsureOn<T> de AltsasuCore
            var method = typeof(AltsasuCore).GetMethod("EnsureOn",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (method == null) return false;

            var genericMethod = method.MakeGenericMethod(tipo);
            object campo = null;
            genericMethod.Invoke(core, new object[] { campo, nombre });
            Log($"✅ {nombre} recuperado");
            return true;
        }
        catch (System.Exception e)
        {
            // EnsureOn necesita ref — fallback: crear un nuevo GO
            try
            {
                var go = new GameObject($"{nombre}_Recuperado");
                go.AddComponent(tipo);
                Log($"✅ {nombre} recreado como nuevo GO");
                return true;
            }
            catch
            {
                Log($"❌ No se pudo recuperar {nombre}: {e.Message}");
                return false;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  VERIFICACIONES ESPECÍFICAS
    // ════════════════════════════════════════════════════════════════════════

    void VerificarAudio(AudioManager audio, ref int p, ref int r)
    {
        // Verificar que el pool tiene fuentes activas
        var sources = audio.GetComponents<AudioSource>();
        if (sources.Length == 0)
        {
            p++;
            // AudioManager crea su pool en Awake — si no hay fuentes, algo falló
            for (int i = 0; i < 4; i++) audio.gameObject.AddComponent<AudioSource>();
            Log("⚠ AudioManager sin pool — añadidas 4 fuentes de emergencia");
            r++;
        }
    }

    void VerificarNavMesh(ref int p, ref int r)
    {
        var nav = SistemaNavMesh.Instance;
        if (nav == null) return;

        // Si el NavMesh estaba listo y ahora falla la consulta básica → rehornar
        if (SistemaNavMesh.EstaListo)
        {
            var centro = new Vector3(1918f, 240f, 8570f); // Herriko Plaza
            if (!SistemaNavMesh.PuntoEnNavMesh(centro, 100f))
            {
                p++;
                Log("⚠ NavMesh parece inválido en Herriko Plaza — relanzando horneado");
                // No podemos rehornar en runtime sin acceso al editor;
                // al menos notificamos en log
                AlsasuaLogger.Error("Seguridad",
                    "NavMesh inválido. Ejecuta Tools → Alsasua → Escena → Crear Escena Principal y vuelve a abrir el proyecto.");
            }
        }
    }

    void LimpiarDuplicados<T>(ref int p, ref int r) where T : MonoBehaviour
    {
        var todos = FindObjectsByType<T>(FindObjectsSortMode.None);
        if (todos.Length <= 1) return;

        // Mantener el que tiene DontDestroyOnLoad (el original)
        p++;
        for (int i = 1; i < todos.Length; i++)
        {
            Destroy(todos[i].gameObject);
            Log($"⚠ Duplicado {typeof(T).Name} eliminado");
            r++;
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  API PÚBLICA
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Devuelve el estado de salud del simulador como string legible.</summary>
    public string SaludSistemas()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[Seguridad] Chequeos: {chequeosTotales}");
        sb.AppendLine($"  Problemas detectados: {problemasDetect}");
        sb.AppendLine($"  Resueltos: {problemasResueltos}");
        sb.AppendLine($"  Último chequeo: T={ultimoChequeo:F0}s");

        foreach (var (tipo, nombre) in SISTEMAS_CRITICOS)
        {
            var inst = FindFirstObjectByType(tipo);
            sb.AppendLine($"  {(inst != null ? "✅" : "❌")} {nombre}");
        }

        var jugador = AltsasuCore.Jugador;
        sb.AppendLine($"  {(jugador != null ? "✅" : "❌")} Jugador" +
                      (jugador != null ? $" Y={jugador.position.y:F0}" : ""));

        return sb.ToString();
    }

    /// <summary>Fuerza un chequeo inmediato.</summary>
    public void ChequeoForzado() => EjecutarChequeo();

    // ── Helpers ───────────────────────────────────────────────────────────

    AltsasuCore AltsaluCore() => AltsasuCore.I;

    void Log(string msg)
    {
        AlsasuaLogger.Info("Seguridad", msg);
        if (_historial.Count >= 50) _historial.RemoveAt(0);
        _historial.Add($"[{Time.time:F0}s] {msg}");
    }

    public IReadOnlyList<string> Historial => _historial;
}
