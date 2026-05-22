// Assets/Scripts/FusionadorEdificiosUltra.cs
// ═══════════════════════════════════════════════════════════════════════════
//  FUSIONADOR DE DATOS ULTRA-PRECISOS — proveedor de datos para GeneradorGeometriaPrecisa
//
//  Orden en el flujo:
//    -62 FusionadorEdificiosUltra  ← carga datos en background thread (Awake→Start)
//    -60 GeneradorGeometriaPrecisa ← espera Cargado, usa GetAlturaOptima / GetFormaOptima
//                                     / GetMaterialParedConAnio durante la generación
//
//  Datos que proporciona por edificio (id OSM):
//    GetAlturaOptima()          LIDAR > Overture > OSM estimada
//    GetFormaOptima()           LIDAR medido > roof:shape OSM > tipo OSM
//    GetMaterialParedConAnio()  material histórico por año de construcción
//    GetRoofColor()             color real del tejado (para tinting del material PBR)
//
//  NO corrige geometría post-generación (eso rompía normales/UVs).
//  ES un proveedor de datos puro: se consulta ANTES de generar.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.Linq;

[DefaultExecutionOrder(-62)]  // ANTES de GeneradorGeometriaPrecisa (-60) — provee datos
public class FusionadorEdificiosUltra : MonoBehaviour
{
    public static FusionadorEdificiosUltra Instance { get; private set; }

    [Header("Estadísticas (solo lectura en Inspector)")]
    [SerializeField] int _totalEdificios;
    [SerializeField] int _conDatosLIDAR;
    [SerializeField] int _conDatosOverture;
    [SerializeField] int _conAnioConst;
    [SerializeField] int _discrepanciasDetectadas;

    public int TotalEdificios       => _totalEdificios;
    public int ConDatosLIDAR        => _conDatosLIDAR;
    public int ConDatosOverture     => _conDatosOverture;
    public int DiscrepanciasAltura  => _discrepanciasDetectadas;

    // ── Datos enriquecidos por id ─────────────────────────────────────────
    [System.Serializable] class EdificioUltra
    {
        public int    id;
        public float  height;
        public float  lidar_z_min, lidar_z_max, lidar_altura;
        public string lidar_forma;
        public int    lidar_pts;
        public float  lidar_eje_x, lidar_eje_z;   // eje PCA de la cumbrera
        public float  overture_height;
        public int    anio_construccion;
        public string material, roof_material, roof_tipo_real;
        public float  roof_r_real, roof_g_real, roof_b_real;
        public float  mat_r, mat_g, mat_b;
        // Puntos reales del tejado como array de objetos (JsonUtility no soporta float[][])
        public PuntoTejado[] puntos_tejado;
    }

    // Clase wrapper para cada punto del tejado — necesaria porque
    // JsonUtility no puede deserializar jagged arrays (float[][])
    [System.Serializable]
    class PuntoTejado { public float x, y, z; }

    readonly Dictionary<int, EdificioUltra> _ultra = new();
    bool _cargado;
    public bool Cargado => _cargado;

    // Carga síncrona en Awake: los datos están listos antes de que Start() de
    // GeneradorGeometriaPrecisa (-60) los necesite, incluso si ambos están en
    // el mismo frame. Start() sólo reporta el resultado.
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;

        // Intentar carga síncrona (el archivo suele ser < 5MB, < 200ms)
        try { CargarDatosUltra(); }
        catch (System.Exception e)
        { AlsasuaLogger.Warn("EdificiosUltra", $"Carga síncrona fallida: {e.Message}"); }
    }

    IEnumerator Start()
    {
        // Si la carga síncrona falló, intentar en background
        if (!_cargado)
        {
            bool lecto = false;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try { CargarDatosUltra(); } catch { }
                lecto = true;
            });
            while (!lecto) yield return new WaitForSeconds(0.05f);
        }

        AlsasuaLogger.Info("EdificiosUltra",
            $"✅ {_totalEdificios} edificios | LIDAR:{_conDatosLIDAR} " +
            $"Overture:{_conDatosOverture} Anio:{_conAnioConst} " +
            $"Discrepancias:{_discrepanciasDetectadas}");
    }

    // ── Carga ─────────────────────────────────────────────────────────────

    void CargarDatosUltra()
    {
        string path = Path.Combine(
            Application.dataPath.Replace("Assets", ""),
            "Assets/AlsasuaData/edificios_ultra.json");

        if (!File.Exists(path))
        {
            path = Path.Combine(
                Application.dataPath.Replace("Assets", ""),
                "Assets/AlsasuaData/buildings_final.json");
        }
        if (!File.Exists(path)) return;

        try
        {
            var texto = File.ReadAllText(path);
            var arr   = JsonHelper.ParseArray<EdificioUltra>(texto);
            if (arr == null) return;

            foreach (var ed in arr)
            {
                _ultra[ed.id] = ed;
                if (ed.lidar_pts > 0)                    _conDatosLIDAR++;
                if (ed.overture_height > 0)              _conDatosOverture++;
                if (ed.anio_construccion > 1800)         _conAnioConst++;

                // Validación: discrepancia > 3m entre LIDAR y OSM estimado
                if (ed.lidar_altura > 0 && ed.height > 0
                    && Mathf.Abs(ed.lidar_altura - ed.height) > 3f)
                    _discrepanciasDetectadas++;
            }
            _totalEdificios = arr.Length;
        }
        catch (System.Exception e)
        {
            AlsasuaLogger.Warn("EdificiosUltra", $"Parse error: {e.Message}");
        }
    }

    // ── API pública ────────────────────────────────────────────────────────

    /// Altura más precisa disponible para un edificio dado su id OSM.
    public float GetAlturaOptima(int id, float alturaOSM)
    {
        if (!_ultra.TryGetValue(id, out var ed)) return alturaOSM;

        // Prioridad: LIDAR > Overture > Catastro/OSM
        if (ed.lidar_pts >= 5 && ed.lidar_altura > 1.5f)
            return ed.lidar_altura;
        if (ed.overture_height > 1.5f)
            return ed.overture_height;
        return alturaOSM;
    }

    /// Forma de tejado más precisa (desde LIDAR o OSM).
    public string GetFormaOptima(int id, string formaOSM)
    {
        if (!_ultra.TryGetValue(id, out var ed)) return formaOSM;
        if (!string.IsNullOrEmpty(ed.lidar_forma) && ed.lidar_forma != "unknown")
            return ed.lidar_forma;
        return formaOSM;
    }

    /// Material de pared corregido por año de construcción.
    public Material GetMaterialParedConAnio(int id, string tipoOSM, string matOSM,
                                             Color colorOrtofoto)
    {
        var gm = GestorMaterialesAlsasua.Instance;
        if (gm == null) return null;

        // Si hay año de construcción, sobreescribir clase de material
        if (_ultra.TryGetValue(id, out var ed) && ed.anio_construccion > 1800)
        {
            string matPorAnio = MaterialPorAnio(ed.anio_construccion, tipoOSM);
            if (!string.IsNullOrEmpty(matPorAnio))
                return gm.GetPared(tipoOSM, matPorAnio, colorOrtofoto);
        }

        return gm.GetPared(tipoOSM, matOSM, colorOrtofoto);
    }

    /// Color real del tejado desde fotogrametría (para tint del material PBR).
    public bool GetRoofColor(int id, out Color color)
    {
        color = default;
        if (!_ultra.TryGetValue(id, out var ed)) return false;
        if (ed.roof_r_real <= 0 && ed.roof_g_real <= 0 && ed.roof_b_real <= 0) return false;
        color = new Color(ed.roof_r_real / 255f, ed.roof_g_real / 255f, ed.roof_b_real / 255f);
        return true;
    }

    /// Eje PCA de la cumbrera del tejado (vector normalizado en XZ planta).
    public Vector2 GetRoofAxis(int id)
    {
        if (!_ultra.TryGetValue(id, out var ed)) return Vector2.right;
        float ex = ed.lidar_eje_x, ez = ed.lidar_eje_z;
        float len = Mathf.Sqrt(ex*ex + ez*ez);
        return len > 0.01f ? new Vector2(ex/len, ez/len) : Vector2.right;
    }

    /// Nube de puntos del tejado en coordenadas relativas al centroide del edificio.
    /// Devuelve true si hay datos; los puntos están en Unity coords (dx, dy_elev, dz).
    public bool GetRoofPoints(int id, out UnityEngine.Vector3[] puntos)
    {
        puntos = null;
        if (!_ultra.TryGetValue(id, out var ed)) return false;
        if (ed.puntos_tejado == null || ed.puntos_tejado.Length < 3) return false;
        puntos = new UnityEngine.Vector3[ed.puntos_tejado.Length];
        for (int i = 0; i < ed.puntos_tejado.Length; i++)
        {
            var p = ed.puntos_tejado[i];
            if (p == null) { puntos = null; return false; }
            puntos[i] = new UnityEngine.Vector3(p.x, p.y, p.z);
        }
        return true;
    }

    /// Color real de pared desde ortofoto.
    public bool GetWallColor(int id, out Color color)
    {
        color = default;
        if (!_ultra.TryGetValue(id, out var ed)) return false;
        if (ed.mat_r <= 0f && ed.mat_g <= 0f && ed.mat_b <= 0f) return false;
        color = new Color(ed.mat_r, ed.mat_g, ed.mat_b);
        return true;
    }

    static string MaterialPorAnio(int anio, string tipo)
    {
        // Arquitectura vasca por época histórica
        if (tipo is "church" or "chapel") return "stone";   // siempre piedra
        if (anio > 0 && anio < 1939)      return "stone";   // pre-guerra: piedra arenisca local
        if (anio < 1960)                  return "brick";   // autarquía: ladrillo rojizo
        if (anio < 1980)                  return "plaster"; // desarrollismo: revoco gris
        if (anio < 2000)                  return "concrete";// transición: hormigón visto
        if (anio >= 2000)                 return "render";  // post-2000: revoco liso moderno
        return "";
    }

    // ── Debug / diagnóstico ───────────────────────────────────────────────

    [ContextMenu("Diagnosticar discrepancias de altura")]
    public void DiagnosticarDiscrepancias()
    {
        int n = 0;
        foreach (var ed in _ultra.Values)
        {
            if (ed.lidar_pts < 5) continue;
            float diff = Mathf.Abs(ed.lidar_altura - ed.height);
            if (diff > 3f)
            {
                AlsasuaLogger.Warn("EdificiosUltra",
                    $"id={ed.id}: OSM={ed.height:F1}m LIDAR={ed.lidar_altura:F1}m " +
                    $"diff={diff:F1}m pts={ed.lidar_pts}");
                n++;
            }
        }
        AlsasuaLogger.Info("EdificiosUltra", $"Total discrepancias >3m: {n}");
    }

    [ContextMenu("Estadísticas fuentes de datos")]
    public void MostrarEstadisticas()
    {
        AlsasuaLogger.Info("EdificiosUltra",
            $"Total edificios: {_totalEdificios}\n" +
            $"  Con LIDAR (>5pts): {_conDatosLIDAR} ({100f*_conDatosLIDAR/_totalEdificios:F0}%)\n" +
            $"  Con Overture ML:   {_conDatosOverture} ({100f*_conDatosOverture/_totalEdificios:F0}%)\n" +
            $"  Con año construc.: {_conAnioConst} ({100f*_conAnioConst/_totalEdificios:F0}%)\n" +
            $"  Discrepancias alt: {_discrepanciasDetectadas}");
    }
}
