// SistemaFacciones.cs — Reputación con las 8 facciones del movimiento + Coherencia
// ═══════════════════════════════════════════════════════════════════════════
//  Capa GAMEPLAY. Implementa IFactionService (Core), registrado en
//  ServiceLocator. Publica eventos tipados en EventBus.
//
//  - Matriz cruzada: subir reputación con una facción arrastra a las demás
//    (Komuntza ↔ Biltzar se excluyen; Askatu Beharra no penaliza con nadie).
//  - Coherencia 0–100 oculta: sin barra en HUD. Sus efectos se ven en el
//    mundo (mancha de chistorra vía shader, saludos de Felisa, etc.).
//  - Sin allocations en Update (no hay Update). Todo es event-driven.
//
//  Diseño completo: Docs/Narrativa_Facciones_TMEO_Vol2.md
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

public class SistemaFacciones : SingletonMono<SistemaFacciones>, IFactionService
{
    protected override bool DestroyGameObjectOnDuplicate => true;

    const int NUM_FACCIONES = 10;   // 8 base + 2 post-cisma
    const int NUM_BASE      = 8;

    [Header("Estado (debug en Inspector — NO mostrar en HUD)")]
    [SerializeField, Range(0, 100)] float coherencia = 50f;
    [SerializeField] float[] reputacion = null;

    [Header("Tuning")]
    [Tooltip("Multiplicador global de los efectos cruzados de la matriz")]
    [SerializeField] float pesoMatriz = 1f;
    [Tooltip("Umbral bajo de Coherencia: empieza la mancha de chistorra")]
    [SerializeField] float umbralBajo = 40f;
    [Tooltip("Umbral alto de Coherencia: Felisa te saluda por tu nombre")]
    [SerializeField] float umbralAlto = 70f;

    // Matriz cruzada (fila = facción con la que subes, columna = efecto en otra).
    // Valores de diseño: Docs/Narrativa_Facciones_TMEO_Vol2.md §3.1
    // Orden: Sutegi, Coordinadora, AskatuB, Askapen, Morea, Komuntza, Asanblada, Biltzar
    static readonly float[,] MATRIZ = new float[NUM_BASE, NUM_BASE]
    {
        //            Sut   Coor  AskB  Askp  Mor   Kom   Asan  Bilt
        /*Sutegi  */ { 0f,  .2f,  .2f,  .3f,  0f,  -.5f,  .1f, -.2f },
        /*Coordin.*/ { .2f,  0f,  .3f,  .1f, -.1f, -.6f, -.4f,  .3f },
        /*AskatuB */ { .2f,  .3f,  0f,   .2f,  .1f,  0f,   .1f,  .1f },
        /*Askapen */ { .2f,  .1f,  .2f,  0f,   0f,  -.2f,  .1f,  0f  },
        /*Morea   */ { .1f, -.1f,  .1f,  0f,   0f,  -.1f,  .2f,  0f  },
        /*Komuntza*/ {-.5f, -.6f,  0f,  -.2f, -.1f,  0f,  -.3f, -.8f },
        /*Asanblad*/ { .1f, -.4f,  .1f,  .1f,  .2f, -.3f,  0f,  -.3f },
        /*Biltzar */ {-.2f,  .3f,  .1f,  0f,   0f,  -.8f, -.3f,  0f  },
    };

    bool[] _activa;
    bool _sobreUmbralAlto, _bajoUmbralBajo;

    // ── IFactionService ──────────────────────────────────────────────────────

    public float Coherencia => coherencia;
    public bool MatrizDesactivada { get; set; }   // true el día de Lasterka

    public float MultiplicadorReclutamiento
    {
        get
        {
            // Propaganda (rep. con quien moviliza) + bono de coherencia alta
            float rep = (GetReputacion(FaccionId.GazteSutegi) +
                         GetReputacion(FaccionId.MoreaBilgunea)) * 0.5f;
            float mult = 0.5f + rep / 100f;                       // 0.5–1.5
            if (coherencia >= umbralAlto) mult += 0.25f;          // el pueblo confía
            return mult;
        }
    }

    public float GetReputacion(FaccionId f) => reputacion[(int)f];

    public bool EstaActiva(FaccionId f) => _activa[(int)f];

    public void ActivarFaccion(FaccionId f)
    {
        int i = (int)f;
        if (_activa[i]) return;
        _activa[i] = true;
        EventBus.Publish(new FaccionActivadaEvent { faccion = f });
        AlsasuaLogger.Info("Facciones", $"Facción activada: {f}");
    }

    public void ModificarReputacion(FaccionId f, float delta, string razon = "")
    {
        AplicarDelta(f, delta, efectoCruzado: false, razon);

        // Efectos cruzados de la matriz (solo facciones base, solo si está activa la matriz)
        int fila = (int)f;
        if (MatrizDesactivada || fila >= NUM_BASE) return;

        for (int col = 0; col < NUM_BASE; col++)
        {
            if (col == fila || !_activa[col]) continue;
            float cruzado = MATRIZ[fila, col] * delta * pesoMatriz;
            if (Mathf.Abs(cruzado) > 0.001f)
                AplicarDelta((FaccionId)col, cruzado, efectoCruzado: true, razon);
        }
    }

    public void ModificarCoherencia(float delta, MotivoCoherencia motivo)
    {
        float anterior = coherencia;
        coherencia = Mathf.Clamp(coherencia + delta, 0f, 100f);
        if (Mathf.Approximately(anterior, coherencia)) return;

        AlsasuaLogger.Info("Coherencia", $"{(delta >= 0 ? "+" : "")}{delta} ({motivo}) → {coherencia:F0}");

        // Umbrales (edge-trigger, mismo patrón que SistemaApoyoPopular.OnParanoiaCritica)
        bool sobreAlto = coherencia >= umbralAlto;
        if (sobreAlto != _sobreUmbralAlto)
            EventBus.Publish(new CoherenciaUmbralEvent { umbral = (int)umbralAlto, subiendo = sobreAlto });
        _sobreUmbralAlto = sobreAlto;

        bool bajoBajo = coherencia < umbralBajo;
        if (bajoBajo != _bajoUmbralBajo)
            EventBus.Publish(new CoherenciaUmbralEvent { umbral = (int)umbralBajo, subiendo = !bajoBajo });
        _bajoUmbralBajo = bajoBajo;

        // La mancha: 1% de opacidad por punto por debajo del umbral bajo.
        // El juego no lo explica nunca. Los jugadores tardarán en darse cuenta.
        float opacidad = Mathf.Max(0f, umbralBajo - coherencia) / 100f;
        EventBus.Publish(new ManchaChistorraEvent { opacidad = opacidad });
    }

    // ── Internos ─────────────────────────────────────────────────────────────

    void AplicarDelta(FaccionId f, float delta, bool efectoCruzado, string razon)
    {
        int i = (int)f;
        float anterior = reputacion[i];
        reputacion[i] = Mathf.Clamp(anterior + delta, 0f, 100f);
        if (Mathf.Approximately(anterior, reputacion[i])) return;

        EventBus.Publish(new FactionReputationChangedEvent
        {
            faccion        = f,
            valorAnterior  = anterior,
            valorNuevo     = reputacion[i],
            efectoCruzado  = efectoCruzado,
        });

        // Reputación directa alta con facciones transversales alimenta el apoyo popular global.
        // Askatu Beharra es la causa transversal: pesa doble (diseño §3.1).
        if (!efectoCruzado && SistemaApoyoPopular.Instance != null)
        {
            float peso = f == FaccionId.AskatuBeharra ? 0.2f : 0.1f;
            if (delta > 0f) SistemaApoyoPopular.Instance.SumarApoyo(delta * peso, razon);
            else            SistemaApoyoPopular.Instance.RestarApoyo(-delta * peso, razon);
        }
    }

    protected override void OnAwake()
    {
        if (reputacion == null || reputacion.Length != NUM_FACCIONES)
        {
            reputacion = new float[NUM_FACCIONES];
            for (int i = 0; i < NUM_FACCIONES; i++) reputacion[i] = 50f;
        }

        _activa = new bool[NUM_FACCIONES];
        for (int i = 0; i < NUM_BASE; i++) _activa[i] = true;   // las post-cisma, apagadas

        ServiceLocator.Registrar<IFactionService>(this);
    }

    protected override void OnDestroyed()
    {
        ServiceLocator.Desregistrar<IFactionService>();
    }

    // ── Guardado (consumido por SistemaGuardado) ─────────────────────────────

    [System.Serializable]
    public struct SaveData
    {
        public float coherencia;
        public float[] reputacion;
        public bool[] activas;
    }

    public SaveData ObtenerSaveData() => new SaveData
    {
        coherencia = coherencia,
        reputacion = (float[])reputacion.Clone(),
        activas    = (bool[])_activa.Clone(),
    };

    public void CargarSaveData(SaveData data)
    {
        if (data.reputacion != null && data.reputacion.Length == NUM_FACCIONES)
            data.reputacion.CopyTo(reputacion, 0);
        if (data.activas != null && data.activas.Length == NUM_FACCIONES)
            data.activas.CopyTo(_activa, 0);
        coherencia = Mathf.Clamp(data.coherencia, 0f, 100f);
        // Re-publicar estado visual tras cargar
        EventBus.Publish(new ManchaChistorraEvent
        {
            opacidad = Mathf.Max(0f, umbralBajo - coherencia) / 100f
        });
    }
}
