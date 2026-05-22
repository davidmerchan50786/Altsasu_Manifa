// Assets/Scripts/FusionadorEdificiosUltra.cs
// ═══════════════════════════════════════════════════════════════════════════
//  FUSIONADOR DE DATOS ULTRA-PRECISOS DE EDIFICIOS
//
//  Carga edificios_ultra.json (fusión de LIDAR + Overture + Catastro + OSM)
//  y enriquece la geometría ya generada por GeneradorGeometriaPrecisa:
//
//  1. ALTURA PRECISA: sustituye altura estimada (niveles×3.2m) por:
//     - lidar_altura  si LIDAR disponible (precisión ~10cm)
//     - overture_height si IA de Microsoft/Meta disponible
//     - catastro height si datos catastrales disponibles
//
//  2. AÑO DE CONSTRUCCIÓN → Material histórico:
//     - Pre-1940 → piedra/ladrillo expuesto (guerra civil, arquitectura vasca)
//     - 1940-1970 → bloque enlucido (desarrollismo)
//     - 1970-2000 → hormigón/ladrillo visto
//     - Post-2000 → hormigón/vidrio moderno
//
//  3. TEJADO PRECISO: si lidar_forma disponible, usa la forma medida
//     en lugar de la estimada por tipo OSM.
//
//  4. VALIDACIÓN CRUZADA: detecta discrepancias entre fuentes y las loguea
//     para revisión manual.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.Linq;

[DefaultExecutionOrder(-53)]  // después de PosicionadorPrecisionUrbana (-54)
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
        public float  overture_height;
        public int    anio_construccion;
        public string material, roof_material, roof_tipo_real;
        public float  roof_r_real, roof_g_real, roof_b_real;
        public float  mat_r, mat_g, mat_b;
    }

    readonly Dictionary<int, EdificioUltra> _ultra = new();
    bool _cargado;
    public bool Cargado => _cargado;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    IEnumerator Start()
    {
        // Cargar datos en frame de fondo
        bool lecto = false;
        System.Threading.ThreadPool.QueueUserWorkItem(_ =>
        {
            CargarDatosUltra();
            lecto = true;
        });

        while (!lecto) yield return new WaitForSeconds(0.1f);

        _cargado = true;
        AlsasuaLogger.Info("EdificiosUltra",
            $"Cargados: {_totalEdificios} edificios | LIDAR:{_conDatosLIDAR} " +
            $"Overture:{_conDatosOverture} Anio:{_conAnioConst}");

        // Esperar a que GeneradorGeometriaPrecisa termine
        yield return new WaitUntil(() =>
            GeneradorGeometriaPrecisa.Instance == null ||
            GeneradorGeometriaPrecisa.Instance.Terminado);

        // Aplicar correcciones de altura a la geometría ya generada
        yield return StartCoroutine(CorregirAlturasGeometria());
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

    static string MaterialPorAnio(int anio, string tipo)
    {
        // Arquitectura vasca por época
        if (tipo is "church" or "chapel") return "stone";  // siempre piedra
        if (anio < 1939) return "stone";    // pre-guerra: piedra arenisca
        if (anio < 1960) return "brick";    // autarquía: ladrillo
        if (anio < 1980) return "plaster";  // desarrollismo: revoco
        if (anio < 2000) return "concrete"; // transición: hormigón
        return "render";                    // post-2000: revoco moderno
    }

    // ── Corrección de alturas en GameObjects ya generados ─────────────────

    IEnumerator CorregirAlturasGeometria()
    {
        if (_ultra.Count == 0) yield break;

        var parentPreciso = GameObject.Find("Edificios_Precisos");
        if (parentPreciso == null) yield break;

        int corregidos = 0;

        foreach (Transform edifGO in parentPreciso.transform)
        {
            if (edifGO == null) continue;

            // Extraer id del nombre "Edif_12345_nombre"
            var partes = edifGO.name.Split('_');
            if (partes.Length < 2 || !int.TryParse(partes[1], out int id)) continue;

            if (!_ultra.TryGetValue(id, out var ed)) continue;

            float altOSM   = edifGO.GetComponentInChildren<MeshFilter>() != null
                ? edifGO.GetComponentInChildren<MeshFilter>().sharedMesh?.bounds.size.y ?? 0
                : 0;
            float altLIDAR = ed.lidar_pts >= 5 ? ed.lidar_altura : 0;

            // Sólo corregir si diferencia > 1m
            if (altLIDAR > 1f && altOSM > 0.5f && Mathf.Abs(altLIDAR - altOSM) > 1f)
            {
                float escala = altLIDAR / altOSM;
                // Escalar solo en Y preservando XZ
                foreach (Transform hijo in edifGO)
                {
                    if (hijo == null) continue;
                    Vector3 s = hijo.localScale;
                    hijo.localScale = new Vector3(s.x, s.y * escala, s.z);
                    corregidos++;
                }
            }

            if (corregidos % 50 == 0) yield return null;
        }

        AlsasuaLogger.Info("EdificiosUltra",
            $"✅ Alturas corregidas LIDAR: {corregidos} sub-meshes");
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
