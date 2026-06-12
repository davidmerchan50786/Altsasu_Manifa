// Assets/Scripts/SistemaCalidadGate.cs
// ═══════════════════════════════════════════════════════════════════════════
//  CALIDAD GATE — activa/desactiva sistemas costosos según _GlobalQualityTier
//
//  Lee SistemaOptimizacion.TierCalidad cada 2 s y ajusta:
//
//    Tier 0 Ultra      → todo activo, probes real-time, IK full, neblina full
//    Tier 1 Alto       → todo activo, probes cada 2s en lugar de real-time
//    Tier 2 Medio      → VidaNocturna reducida (solo farolas), IK desactivado,
//                        reflexiones solo baked, neblina con actualización lenta
//    Tier 3 Performance → VidaNocturna off, IKProcedural off, Reflexiones off,
//                         Neblina off, PostProcesoAAA solo efectos básicos
//
//  Cada sistema expone un bool `enabled` o un método público para ser
//  controlado — este gate lo llama sin acoplar los sistemas entre sí.
//
//  No modifica ningún sistema existente — solo activa/desactiva componentes.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

[DefaultExecutionOrder(300)]   // después de SistemaOptimizacion (que publica el tier)
public class SistemaCalidadGate : MonoBehaviour
{
    public static SistemaCalidadGate Instance { get; private set; }

    [Tooltip("Segundos entre evaluaciones del tier (no hace falta hacerlo cada frame)")]
    [SerializeField] float intervalo = 2f;

    int   _tierAnterior = -1;
    float _timer;

    // ════════════════════════════════════════════════════════════════════════

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void Update()
    {
        _timer += Time.deltaTime;
        if (_timer < intervalo) return;
        _timer = 0f;

        int tier = SistemaOptimizacion.TierCalidad;
        if (tier == _tierAnterior) return;
        _tierAnterior = tier;
        AplicarTier(tier);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  APLICAR TIER
    // ════════════════════════════════════════════════════════════════════════

    void AplicarTier(int tier)
    {
        AlsasuaLogger.Info("CalidadGate", $"Aplicando tier {tier}");

        // ── VidaNocturna ──────────────────────────────────────────────────
        var vida = SistemaVidaNocturna.Instance;
        if (vida != null)
            vida.enabled = tier <= 2;   // Off en tier 3

        // ── IK Procedural ─────────────────────────────────────────────────
        var ik = FindFirstObjectByType<SistemaIKProcedural>();
        if (ik != null)
        {
            ik.footIKActivo = tier <= 1;
            ik.lookAtActivo = tier <= 2;
        }

        // ── Reflexiones ───────────────────────────────────────────────────
        var refl = SistemaReflexiones.Instance;
        if (refl != null)
        {
            refl.enabled = tier <= 2;
            if (tier == 1)
                refl.intervaloBake = 2f;   // probes más lentos en tier 1
            else if (tier == 0)
                refl.intervaloBake = 0.3f; // casi real-time en ultra
        }

        // ── Neblina ───────────────────────────────────────────────────────
        var neblina = SistemaNeblina.Instance;
        if (neblina != null)
            neblina.enabled = tier <= 2;

        // ── Impostores (solo activar en tier 2-3 donde hay LOD agresivo) ─
        var impostores = SistemaImpostores.Instance;
        if (impostores != null)
            impostores.enabled = tier >= 1;   // en Ultra (0) el LOD3 raramente activa

        // ── Fachadas dinámicas ────────────────────────────────────────────
        var fachadas = SistemaFachadasDinamicas.Instance;
        if (fachadas != null)
            fachadas.enabled = tier <= 1;   // MPB por renderer es caro en tier 2+

        // ── Tren ──────────────────────────────────────────────────────────
        var tren = SistemaTren.Instance;
        if (tren != null)
            tren.enabled = tier <= 2;   // en Performance el tren desaparece

        // ── ClimaEfectos ──────────────────────────────────────────────────
        var climaEfectos = SistemaClimaEfectos.Instance;
        if (climaEfectos != null)
            climaEfectos.enabled = tier <= 2;

        // ── ReaccionNPCs (LookAt) — desactivar en Performance ────────────
        var reaccion = SistemaReaccionNPCs.Instance;
        if (reaccion != null)
            reaccion.enabled = tier <= 2;

        // ── Tráfico: reducir pool de vehículos en tiers bajos ─────────────
        // SistemaTrafico gestiona internamente la densidad, pero podemos
        // forzar una actualización si el tier cambia drásticamente.
        // (SistemaTrafico ajusta su densidad por hora cada 5s — no hace falta aquí)

        // ── Post-proceso AAA: efectos caros solo en tier 0-1 ─────────────
        var postProceso = SistemaPostProcesoAAA.Instance;
        if (postProceso != null)
            postProceso.enabled = tier <= 1;

        // ── ParticleSystem globales: reducir en tiers altos ───────────────
        var ambient = SistemaAmbientParticulas.Instance;
        if (ambient != null)
            ambient.enabled = tier <= 2;
    }

    // ── API para forzar una reevaluación inmediata ────────────────────────
    public static void Refrescar()
    {
        if (Instance != null)
        {
            Instance._tierAnterior = -1;
            Instance._timer = Instance.intervalo;
        }
    }
}
