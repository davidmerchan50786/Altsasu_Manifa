// Assets/Scripts/MisionesSec.cs — Misiones secundarias opcionales
// ═══════════════════════════════════════════════════════════════════════════
//  Misiones que pueden activarse en cualquier orden:
//    MS01  Fotógrafo       — fotografía la represión (mecánica nueva)
//    MS02  Músico          — alcanza el ensayo con tu banda
//    MS03  Cocinero        — reparte txikiteo antes de la marcha
//    MS04  Corredor        — llega al monte Aralar en <5 min corriendo
//    MS05  Grafitero Pro   — pinta 3 superficies específicas marcadas
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using UnityEngine;

// ── MS01 — FOTÓGRAFO ──────────────────────────────────────────────────────
public class MisionSec_Fotografo : Mision
{
    public override string Nombre => "[SECUNDARIA] Fotógrafo de la Resistencia";

    int   _fotos = 0;
    const int META = 3;

    // Puntos de "fotografía" — cerca del cuartel, la N-1, la plaza
    static readonly Vector3[] PUNTOS = {
        new(2180f, 0f, 8720f),   // cuartel GC
        new(1900f, 0f, 8000f),   // N-1
        new(1918f, 0f, 8570f),   // Herriko Plaza
    };

    public override System.Action AlIniciar => () =>
    {
        AlsasuaLogger.Info("MS01", $"Fotógrafo: acércate a {META} puntos de interés y pulsa F");
        AlsasuaLogger.Info("MS01", "Modo fotógrafo activo"); // logro se dispara al completar
    };

    public override List<Objetivo> Objetivos => new()
    {
        new Objetivo
        {
            Descripcion = $"Fotografía {META} momentos de la represión (acércate y pulsa F)",
            Condicion   = () =>
            {
                var j = AltsasuCore.Jugador;
                if (j == null) return false;

                // Comprobar si está cerca de algún punto y pulsa F
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb == null || !kb.fKey.wasPressedThisFrame) return false;

                foreach (var p in PUNTOS)
                    if (PuntosAlsasua.Dist2D(j.position, p) < 60f)
                    {
                        _fotos++;
                        SistemaPolish.Shake(0.08f);
                        AudioManager.Play(AudioManager.Clip.UIClick, j.position);
                        AlsasuaLogger.Info("MS01", $"Foto {_fotos}/{META}");
                        return _fotos >= META;
                    }
                return false;
            },
            AlCompletar = () =>
            {
                GameManagerAltsasua.Instance?.GanarDinero(400);
                SistemaApoyoPopular.Instance?.SumarApoyo(100f, "Fotos de denuncia publicadas");
            }
        }
    };
}

// ── MS02 — MÚSICO ────────────────────────────────────────────────────────
public class MisionSec_Musico : Mision
{
    public override string Nombre => "[SECUNDARIA] Musika — El Ensayo";

    static readonly Vector3 PUNTO_ENSAYO = new(1820f, 0f, 8700f); // barrio norte
    float _timerEnEnsayo = 0f;
    const float DURACION = 60f;

    public override List<Objetivo> Objetivos => new()
    {
        new Objetivo
        {
            Descripcion = "Llega al local de ensayo (barrio norte, zona cultural)",
            Condicion   = () => PuntosAlsasua.Dist2D(
                PuntosAlsasua.JugadorPos(), PUNTO_ENSAYO) < 40f,
            AlCompletar = () => AlsasuaLogger.Info("MS02", "¡En el local! Toca 60 segundos")
        },
        new Objetivo
        {
            Descripcion = $"Permanece en el ensayo {DURACION}s (no te vayas)",
            Condicion   = () =>
            {
                bool cerca = PuntosAlsasua.Dist2D(
                    PuntosAlsasua.JugadorPos(), PUNTO_ENSAYO) < 60f;
                if (cerca) _timerEnEnsayo += Time.deltaTime;
                else       _timerEnEnsayo -= Time.deltaTime * 0.5f;
                return Mathf.Max(0f, _timerEnEnsayo) >= DURACION;
            },
            AlCompletar = () =>
            {
                GameManagerAltsasua.Instance?.GanarDinero(300);
                SistemaApoyoPopular.Instance?.SumarApoyo(80f, "Concierto de resistencia");
            }
        }
    };
}

// ── MS03 — COCINERO / TXIKITEO ────────────────────────────────────────────
public class MisionSec_Txikiteo : Mision
{
    public override string Nombre => "[SECUNDARIA] Txikiteo Solidario";

    // 4 bares en Alsasua (puntos aproximados)
    static readonly Vector3[] BARES = {
        new(1918f, 0f, 8575f),
        new(1880f, 0f, 8610f),
        new(1950f, 0f, 8545f),
        new(1905f, 0f, 8590f),
    };
    readonly bool[] _visitados = new bool[4];

    public override List<Objetivo> Objetivos => new()
    {
        new Objetivo
        {
            Descripcion = "Visita los 4 bares del pueblo antes de la marcha",
            Condicion   = () =>
            {
                var j = AltsasuCore.Jugador;
                if (j == null) return false;
                for (int i = 0; i < BARES.Length; i++)
                    if (!_visitados[i] && PuntosAlsasua.Dist2D(j.position, BARES[i]) < 25f)
                    {
                        _visitados[i] = true;
                        int n = System.Array.FindAll(_visitados, v => v).Length;
                        AlsasuaLogger.Info("MS03", $"Bar visitado {n}/4");
                        AudioManager.Play(AudioManager.Clip.UINotificacion);
                    }
                return System.Array.TrueForAll(_visitados, v => v);
            },
            AlCompletar = () =>
            {
                GameManagerAltsasua.Instance?.GanarDinero(200);
                SistemaApoyoPopular.Instance?.SumarApoyo(60f, "Txikiteo solidario completado");
            }
        }
    };
}

// ── MS04 — CORREDOR ───────────────────────────────────────────────────────
public class MisionSec_Corredor : Mision
{
    public override string Nombre => "[SECUNDARIA] Mendi-korrika — A la Cima";

    float _timerTotal  = 0f;
    bool  _corriendo   = false;
    const float LIMITE = 300f; // 5 minutos

    static readonly Vector3 META_MONTE = new(3200f, 0f, 9400f);

    public override System.Action AlIniciar => () =>
    {
        _timerTotal = 0f; _corriendo = true;
        AlsasuaLogger.Info("MS04", "¡Corre! Llega al monte Aralar en 5 minutos");
    };

    public override List<Objetivo> Objetivos => new()
    {
        new Objetivo
        {
            Descripcion = $"Llega corriendo al monte Aralar en menos de {LIMITE}s",
            Condicion   = () =>
            {
                if (!_corriendo) return false;
                _timerTotal += Time.deltaTime;
                if (_timerTotal >= LIMITE)
                {
                    AlsasuaLogger.Info("MS04", "Tiempo agotado");
                    _corriendo = false;
                    return true; // falla pero avanza
                }
                return PuntosAlsasua.Dist2D(PuntosAlsasua.JugadorPos(), META_MONTE) < 80f;
            },
            AlCompletar = () =>
            {
                bool enTiempo = _timerTotal < LIMITE;
                int recomp = enTiempo ? 800 : 100;
                GameManagerAltsasua.Instance?.GanarDinero(recomp);
                SistemaApoyoPopular.Instance?.SumarApoyo(
                    enTiempo ? 150f : 20f,
                    enTiempo ? "Mendi-korrika completada" : "Llegaste pero tarde");
            }
        }
    };
}

// ── MS05 — GRAFITERO PRO ──────────────────────────────────────────────────
public class MisionSec_GrafiteroPro : Mision
{
    public override string Nombre => "[SECUNDARIA] Grafitero Profesional";

    // Superficies específicas marcadas con marcador visual
    static readonly Vector3[] OBJETIVOS = {
        new(2180f, 0f, 8720f),  // cuartel GC — máximo impacto
        new(1900f, 0f, 8000f),  // N-1 — visibilidad máxima
        new(1820f, 0f, 8850f),  // barrio norte
    };
    readonly bool[] _pintado = new bool[3];
    bool _activo;

    public override System.Action AlIniciar => () =>
    {
        _activo = true;
        SistemaGrafitis.OnPintadaRealizada += OnPintada;
        AlsasuaLogger.Info("MS05", "Pinta en 3 localizaciones específicas (G + click)");
    };

    void OnPintada()
    {
        if (!_activo) return;
        var j = AltsasuCore.Jugador;
        if (j == null) return;
        for (int i = 0; i < OBJETIVOS.Length; i++)
            if (!_pintado[i] && PuntosAlsasua.Dist2D(j.position, OBJETIVOS[i]) < 80f)
            {
                _pintado[i] = true;
                int n = System.Array.FindAll(_pintado, v => v).Length;
                AlsasuaLogger.Info("MS05", $"Localización {n}/3 pintada");
            }
    }

    public override List<Objetivo> Objetivos => new()
    {
        new Objetivo
        {
            Descripcion = "Pinta en las 3 localizaciones de alto impacto",
            Condicion   = () => System.Array.TrueForAll(_pintado, v => v),
            AlCompletar = () =>
            {
                _activo = false;
                SistemaGrafitis.OnPintadaRealizada -= OnPintada;
                GameManagerAltsasua.Instance?.GanarDinero(600);
                SistemaApoyoPopular.Instance?.SumarApoyo(200f, "Grafiti en puntos estratégicos");
                SistemaLogros.Instance?.GetType()
                    .GetMethod("OnGraffiti",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    ?.Invoke(SistemaLogros.Instance, null);
            }
        }
    };
}

// ── Sistema de misiones secundarias ───────────────────────────────────────
public class GestorMisionesSecundarias : MonoBehaviour
{
    public static GestorMisionesSecundarias Instance { get; private set; }

    readonly List<Mision> _disponibles = new();
    readonly List<Mision> _activas     = new();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        _disponibles.AddRange(new Mision[]
        {
            new MisionSec_Fotografo(),
            new MisionSec_Musico(),
            new MisionSec_Txikiteo(),
            new MisionSec_Corredor(),
            new MisionSec_GrafiteroPro(),
        });
    }

    void Start()
    {
        // Las misiones secundarias se desbloquean progresivamente
        AltsasuCore.OnWorldReady += DesbloquearPrimeras;
        SistemaMisiones.OnMisionCompletada += OnMisionPrincipalCompletada;
    }

    void DesbloquearPrimeras()
    {
        // MS01 y MS02 disponibles desde el inicio
        IniciarSecundaria<MisionSec_Fotografo>();
        IniciarSecundaria<MisionSec_Txikiteo>();
    }

    void OnMisionPrincipalCompletada(string nombre)
    {
        // Desbloquear secundarias según progreso
        int completadas = PlayerPrefs.GetInt("misiones_completadas", 0);
        if (completadas >= 3) IniciarSecundaria<MisionSec_Musico>();
        if (completadas >= 6) IniciarSecundaria<MisionSec_Corredor>();
        if (completadas >= 9) IniciarSecundaria<MisionSec_GrafiteroPro>();
    }

    void IniciarSecundaria<T>() where T : Mision, new()
    {
        var ya = _activas.Find(m => m is T);
        if (ya != null) return; // ya activa
        var m = _disponibles.Find(m => m is T);
        if (m == null) return;
        _activas.Add(m);
        m.AlIniciar?.Invoke();
        AlsasuaLogger.Info("MisionSec", $"Misión secundaria disponible: {m.Nombre}");
    }

    void Update()
    {
        foreach (var m in _activas)
        {
            if (m.Objetivos.Count == 0) continue;
            var obj = m.Objetivos[0];
            if (obj.Condicion != null && obj.Condicion())
            {
                obj.AlCompletar?.Invoke();
                _activas.Remove(m);
                AlsasuaLogger.Info("MisionSec", $"✅ {m.Nombre}");
                break;
            }
        }
    }

    void OnDestroy()
    {
        AltsasuCore.OnWorldReady -= DesbloquearPrimeras;
        SistemaMisiones.OnMisionCompletada -= OnMisionPrincipalCompletada;
    }
}
