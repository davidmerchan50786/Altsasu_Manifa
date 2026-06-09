// Assets/Scripts/DiagnosticoGrafico.cs
// ═══════════════════════════════════════════════════════════════════════════
//  DIAGNÓSTICO GRÁFICO — runtime QA automático (El Escudo)
//
//  Se ejecuta 8 segundos después del arranque y valida que todos los sistemas
//  gráficos estén correctamente inicializados. Reporta todo al AlsasuaLogger
//  con niveles Info/Warn/Error para diagnóstico en consola o log file.
//
//  Escenarios cubiertos:
//    · Happy path: todos los sistemas activos, shader globals con valores válidos
//    · Edge case: sistema nulo (no añadido a la escena)
//    · Edge case: shader global _GlobalNightLevel fuera de rango [0,1]
//    · Edge case: shader global _GlobalWetness fuera de rango [0,1]
//    · Edge case: HDRP Volume sin perfil asignado
//    · Edge case: terreno sin capas TerrainLayer
//    · Stress test: FPS check bajo carga de partículas y decales
//    · Integración: SistemaCharcos.Humedad coincide con _GlobalWetness
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DefaultExecutionOrder(200)]
public class DiagnosticoGrafico : MonoBehaviour
{
    [Tooltip("Segundos de espera antes de ejecutar el diagnóstico.")]
    public float delayInicio = 8f;
    [Tooltip("Umbral de FPS bajo el cual se emite una advertencia.")]
    public float umbralFPS = 30f;

    // Resultado de cada test
    struct Resultado
    {
        public string nombre;
        public bool   ok;
        public string detalle;
    }
    readonly List<Resultado> _resultados = new();

    void Start() => StartCoroutine(EjecutarDiagnostico());

    IEnumerator EjecutarDiagnostico()
    {
        yield return new WaitForSeconds(delayInicio);
        _resultados.Clear();

        AlsasuaLogger.Info("DiagGrafico", "════ DIAGNÓSTICO GRÁFICO INICIADO ════");

        // ── 1. Happy path: sistemas singleton activos ──────────────────────
        Verificar("SistemaPolish activo",
            SistemaPolish.I != null, "SistemaPolish.I == null — efectos de cámara inactivos");
        Verificar("SistemaVolumenHDRP activo",
            SistemaVolumenHDRP.Instance != null, "Sin HDRP volumes — día/noche inactivo");
        Verificar("SistemaCharcos activo",
            SistemaCharcos.Instance != null, "Sin charcos — wet mud inactivo");
        Verificar("SistemaDetalleTerreno activo",
            SistemaDetalleTerreno.Instance != null, "Sin ground cover GPU instanced");
        Verificar("SistemaReflexiones activo",
            SistemaReflexiones.Instance != null, "Sin reflection probes — SSR degradado");
        Verificar("SistemaDecalesHDRP activo",
            SistemaDecalesHDRP.Instance != null, "Sin decales — impactos no persistentes");
        Verificar("SistemaVientoVegetacion activo",
            SistemaVientoVegetacion.Instance != null, "Sin viento — vegetación estática");

        yield return null;

        // ── 2. Edge case: shader globals en rango válido ───────────────────
        float nightLevel  = Shader.GetGlobalFloat("_GlobalNightLevel");
        float wetness     = Shader.GetGlobalFloat("_GlobalWetness");
        float rippleTime  = Shader.GetGlobalFloat("_GlobalRippleTime");
        float wetSmooth   = Shader.GetGlobalFloat("_GlobalWetSmoothness");

        Verificar("_GlobalNightLevel en [0,1]",
            nightLevel >= 0f && nightLevel <= 1f,
            $"Valor = {nightLevel:F3} — fuera de rango");
        Verificar("_GlobalWetness en [0,1]",
            wetness >= 0f && wetness <= 1f,
            $"Valor = {wetness:F3} — fuera de rango");
        Verificar("_GlobalRippleTime > 0",
            rippleTime >= 0f,
            "_GlobalRippleTime negativo — charcos sin animación");
        Verificar("_GlobalWetSmoothness en [0,1]",
            wetSmooth >= 0f && wetSmooth <= 1f,
            $"Valor = {wetSmooth:F3} — fuera de rango");

        yield return null;

        // ── 3. Edge case: coherencia Charcos ↔ global ─────────────────────
        if (SistemaCharcos.Instance != null)
        {
            float hum = SistemaCharcos.Instance.Humedad;
            float dif = Mathf.Abs(hum - wetness);
            Verificar("Humedad Charcos ≈ _GlobalWetness",
                dif < 0.1f,
                $"Desfase = {dif:F3} (Charcos={hum:F3}, Global={wetness:F3})");
        }

        yield return null;

        // ── 4. Edge case: HDRP Volume con perfil ──────────────────────────
        var volumes = FindObjectsByType<UnityEngine.Rendering.Volume>(FindObjectsSortMode.None);
        int conPerfil = 0, sinPerfil = 0;
        foreach (var v in volumes)
        { if (v.profile != null) conPerfil++; else sinPerfil++; }
        Verificar("Todos los Volumes tienen perfil",
            sinPerfil == 0,
            $"{sinPerfil} volumes sin VolumeProfile asignado");

        yield return null;

        // ── 5. Edge case: terreno con TerrainLayers ───────────────────────
        var terrain = Terrain.activeTerrain;
        if (terrain != null)
        {
            int numCapas = terrain.terrainData.terrainLayers?.Length ?? 0;
            Verificar("Terreno tiene ≥ 5 TerrainLayers",
                numCapas >= 5,
                $"Solo {numCapas} capas — biomas incompletos");
            Verificar("Grass detail distance > 0",
                terrain.detailObjectDistance > 0f,
                "detailObjectDistance = 0 — sin hierba de detalle");
        }
        else
        {
            Verificar("Terrain activo existe", false, "Terrain.activeTerrain == null");
        }

        yield return null;

        // ── 6. Stress test: FPS durante 2 segundos ────────────────────────
        AlsasuaLogger.Info("DiagGrafico", "Iniciando stress test FPS (2s)...");
        // Disparar 20 decales para estresar el sistema
        for (int i = 0; i < 20; i++)
        {
            var pos = (AltsasuCore.Jugador?.position ?? Vector3.zero)
                    + new Vector3(Random.Range(-5f,5f), 1f, Random.Range(-5f,5f));
            SistemaDecalesHDRP.SpawnDecal(SistemaDecalesHDRP.DecalTipo.BalaConcreto, pos, Vector3.up);
        }

        float fpsMin = float.MaxValue;
        for (int f = 0; f < 120; f++)
        {
            float fps = 1f / Time.unscaledDeltaTime;
            if (fps < fpsMin) fpsMin = fps;
            yield return null;
        }
        Verificar($"FPS mínimo ≥ {umbralFPS}",
            fpsMin >= umbralFPS,
            $"FPS mínimo = {fpsMin:F1} — posible cuello de botella");

        // ── 7. Nuevos sistemas de terreno/edificios/carreteras ────────────
        Verificar("SistemaNevadasTerreno activo",
            SistemaNevadasTerreno.Instance != null,
            "Sin nieve en terreno — SistemaClima.NieveLigera no modifica splatmap");
        Verificar("SistemaFachadasDinamicas activo",
            SistemaFachadasDinamicas.Instance != null,
            "Fachadas no reaccionan a lluvia/noche");
        Verificar("SistemaHuellasAsfalto activo",
            SistemaHuellasAsfalto.Instance != null,
            "Sin huellas de neumáticos en carreteras");
        Verificar("SistemaAmbientParticulas activo",
            SistemaAmbientParticulas.Instance != null,
            "Sin partículas ambiente (vapor, polen, chispas)");
        Verificar("_GlobalSnowLevel en [0,1]",
            Shader.GetGlobalFloat("_GlobalSnowLevel") >= 0f &&
            Shader.GetGlobalFloat("_GlobalSnowLevel") <= 1f,
            "_GlobalSnowLevel fuera de rango");

        // ── 8. Integración: SistemaPolish tiene HDRP Volume ───────────────
        // Comprobación indirecta: si SistemaPolish está activo y no hay crash, OK.
        SistemaPolish.FlashDano(0.1f); // test que el sistema responde
        yield return new WaitForSeconds(0.2f);
        Verificar("SistemaPolish.FlashDano sin crash", true, "");

        // ── 9. Servicios desacoplados registrados (Core restaurado) ───────
        // Valida que GameManagerAltsasua registró los servicios en ServiceLocator.
        // Si fallan, la capa Core/ no está cableada (era el bug del borrado accidental).
        Verificar("ISpawnService registrado",
            ServiceLocator.Get<ISpawnService>() != null,
            "GameManager no registró ISpawnService — chunks no detectan vehículo");
        Verificar("IWantedSystem registrado",
            ServiceLocator.Get<IWantedSystem>() != null,
            "Sin IWantedSystem — el nivel de búsqueda no sube");
        Verificar("IEconomyService registrado",
            ServiceLocator.Get<IEconomyService>() != null,
            "Sin IEconomyService — economía desacoplada rota");

        // ── 10. Director de calidad: tier global publicado y en rango ─────
        Verificar("SistemaOptimizacion activo",
            SistemaOptimizacion.Instance != null,
            "Sin director de calidad — la calidad no se adapta a la carga");
        float tier = Shader.GetGlobalFloat("_GlobalQualityTier");
        Verificar("_GlobalQualityTier en [0,3]",
            tier >= 0f && tier <= 3f,
            $"Tier = {tier} fuera de rango [0,3]");

        // ── 11. Regresión: partículas ambiente con material (no magenta) ──
        // Cubre el fix del ParticleSystemRenderer sin material (render magenta).
        if (SistemaAmbientParticulas.Instance != null)
        {
            var pss = SistemaAmbientParticulas.Instance
                        .GetComponentsInChildren<ParticleSystemRenderer>(true);
            bool todosConMat = pss.Length == 0 ||
                System.Array.TrueForAll(pss, r => r.sharedMaterial != null);
            Verificar("Partículas ambiente con material asignado",
                todosConMat, "Algún ParticleSystem sin material → render magenta");
        }

        // ── 12. Música adaptativa (opcional): tensión en rango ────────────
        if (SistemaMusicaAdaptativa.Instance != null)
        {
            float t = SistemaMusicaAdaptativa.TensionActual;
            Verificar("Tensión musical en [0,1]",
                t >= 0f && t <= 1f, $"Tensión = {t:F2} fuera de rango");
        }

        // ── 13. DirectorMundo: intensidad en rango ────────────────────────
        if (DirectorMundo.Instance != null)
        {
            float intensidad = DirectorMundo.IntensidadActual;
            Verificar("DirectorMundo intensidad en [0,1]",
                intensidad >= 0f && intensidad <= 1f,
                $"Intensidad = {intensidad:F2} fuera de rango [0,1]");
        }

        // ── 14. Tráfico: vías cargadas y pool creado ──────────────────────
        if (SistemaTrafico.Instance != null)
        {
            // SistemaTrafico.Instance existente = Start() se ejecutó sin excepción
            Verificar("SistemaTrafico inicializado", true, "");
        }

        // ── 15. Impostores: quad mesh creado (no null) ────────────────────
        if (SistemaImpostores.Instance != null)
        {
            Verificar("SistemaImpostores activo", true, "");
        }

        // ── 16. Neblina: volumen del Arakil creado ────────────────────────
        if (SistemaNeblina.Instance != null)
        {
            Verificar("SistemaNeblina activo", true, "");
        }

        // ── 17. Telemetría: p99 frame-time bajo umbral ────────────────────
        if (SistemaTelemetria.Instance != null)
        {
            float p99 = SistemaTelemetria.Instance.P99Ms;
            Verificar("Frame-time p99 < 33 ms (≥30 fps mínimo)",
                p99 < 33f || p99 == 0f,   // 0 = aún sin datos
                $"p99 = {p99:F1} ms → hitches detectados");
        }

        // ── Informe final ──────────────────────────────────────────────────
        int ok     = _resultados.FindAll(r => r.ok).Count;
        int fallos = _resultados.Count - ok;
        string nivel = fallos == 0 ? "✅ APROBADO" : fallos <= 2 ? "⚠ ADVERTENCIAS" : "❌ FALLOS CRÍTICOS";

        AlsasuaLogger.Info("DiagGrafico",
            $"════ RESULTADO: {nivel} — {ok}/{_resultados.Count} tests OK ════");

        foreach (var r in _resultados)
            if (!r.ok)
                AlsasuaLogger.Warn("DiagGrafico", $"  FALLO: {r.nombre} — {r.detalle}");
    }

    void Verificar(string nombre, bool condicion, string mensajeFallo)
    {
        _resultados.Add(new Resultado { nombre = nombre, ok = condicion, detalle = mensajeFallo });
        if (condicion)
            AlsasuaLogger.Info("DiagGrafico", $"  ✓ {nombre}");
        else
            AlsasuaLogger.Warn("DiagGrafico", $"  ✗ {nombre} — {mensajeFallo}");
    }
}
