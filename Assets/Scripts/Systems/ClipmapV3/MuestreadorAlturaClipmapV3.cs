// Assets/Scripts/_ClipmapV3~/MuestreadorAlturaClipmapV3.cs  (STAGING — fuera del build)
// ─────────────────────────────────────────────────────────────────────────────
//  FASE 4 — integración del clipmap V3 con el resto del juego SIN tocar nada.
//
//  Implementa el contrato Core IMuestreadorAlturaPrecisa (el MISMO que usa el
//  Mosaico V2 vía MuestreadorAlturaMosaico). ServicioTerreno.AlturaMundo ya
//  PREFIERE este servicio si está registrado → con esto, edificios, NavMesh,
//  árboles, spawn, Foot IK y Cesium leen la altura del heightmap unificado V3
//  sin cambiar una línea. Si no se registra (o falla el gate), el juego sigue
//  con el camino Terrain de siempre: CERO regresión.
//
//  Respaldo: MuestreadorHeightmapV3 (CPU, bilineal, ya validado). Misma fórmula
//  de decode que el .py y que el HLSL de GPU → las tres fuentes coinciden.
//
//  Gate de auto-validación (igual filosofía que MuestreadorAlturaMosaico):
//  comprueba la cota de Herriko Plaza contra GeoDataAlsasua.COTA_PLAZA antes de
//  registrarse. Si se desviara > 3 m, NO se registra y avisa.
//
//  Aditivo y opt-in: pon el componente en la escena (activarEnStart = true) o
//  registra a mano. Memoria ~34 MB (un R16 de 4097²), muy por debajo de los
//  ~126 MB del muestreador V2 multi-tile.
// ─────────────────────────────────────────────────────────────────────────────
using UnityEngine;

public sealed class MuestreadorAlturaClipmapV3 : MonoBehaviour, IMuestreadorAlturaPrecisa
{
    [Tooltip("Si está OFF, no carga nada ni se registra como servicio.")]
    [SerializeField] bool activarEnStart = true;
    [Tooltip("Carpeta del heightmap (vacío = Assets/AlsasuaData/terrain_clipmap_v3).")]
    [SerializeField] string carpeta = "";
    [Tooltip("Paso (m) de NormalMundo para diferencias finitas.")]
    [SerializeField, Range(0.1f, 5f)] float pasoNormalM = 1f;

    public bool Listo { get; private set; }
    readonly MuestreadorHeightmapV3 _h = new();

    void Start()
    {
        if (!activarEnStart) return;
        Activar();
    }

    /// <summary>Carga el R16, valida la cota de plaza y, si pasa, se registra.</summary>
    public bool Activar()
    {
        if (!_h.Cargar(string.IsNullOrEmpty(carpeta) ? null : carpeta))
        {
            AlsasuaLogger.Warn("AlturaV3", "heightmap_unificado.r16 no disponible — no se activa.");
            return false;
        }

        // ── GATE: cota de Herriko Plaza contra la verdad del proyecto ──
        // AlturaMundo devuelve (altitudReal - Z_MIN); la esperada es COTA_PLAZA - Z_MIN.
        float yPlaza      = _h.AlturaMundo(GeoDataAlsasua.OX, GeoDataAlsasua.OZ);
        float yEsperada   = GeoDataAlsasua.COTA_PLAZA - GeoDataAlsasua.Z_MIN;
        float error       = Mathf.Abs(yPlaza - yEsperada);
        if (error > 3f)
        {
            AlsasuaLogger.Error("AlturaV3",
                $"Auto-validación FALLÓ: cota plaza={yPlaza + GeoDataAlsasua.Z_MIN:F2} m vs " +
                $"esperada {GeoDataAlsasua.COTA_PLAZA:F2} m (error {error:F1} m > 3 m). NO se registra; " +
                "el juego sigue con ITerrainService/TerrenoGlobal (fallback seguro).");
            return false;
        }

        Listo = true;
        ServiceLocator.Registrar<IMuestreadorAlturaPrecisa>(this);
        AlsasuaLogger.Info("AlturaV3",
            $"Clipmap V3 listo como fuente de altura (un R16, ~34 MB). " +
            $"Cota plaza {yPlaza + GeoDataAlsasua.Z_MIN:F2} m (✓ vs {GeoDataAlsasua.COTA_PLAZA:F2} m).");
        return true;
    }

    void OnDestroy()
    {
        if (ReferenceEquals(ServiceLocator.Get<IMuestreadorAlturaPrecisa>(), this))
            ServiceLocator.Desregistrar<IMuestreadorAlturaPrecisa>();
        Listo = false;
    }

    // ── IMuestreadorAlturaPrecisa ───────────────────────────────────────────
    public float AlturaMundo(Vector3 p) => AlturaMundo(p.x, p.z);
    public float AlturaMundo(float x, float z) => Listo ? _h.AlturaMundo(x, z) : 0f;

    public Vector3 NormalMundo(float x, float z)
    {
        if (!Listo) return Vector3.up;
        float e = pasoNormalM;
        float yL = _h.AlturaMundo(x - e, z), yR = _h.AlturaMundo(x + e, z);
        float yD = _h.AlturaMundo(x, z - e), yU = _h.AlturaMundo(x, z + e);
        return new Vector3(yL - yR, 2f * e, yD - yU).normalized;
    }
}
