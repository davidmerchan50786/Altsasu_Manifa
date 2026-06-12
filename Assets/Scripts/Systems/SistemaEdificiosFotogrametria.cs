// Assets/Scripts/SistemaEdificiosFotogrametria.cs
// ═══════════════════════════════════════════════════════════════════════════
//  EDIFICIOS FOTOGRAMÉTRICOS — anclas foto-reales en el Alsasua procedural
//
//  PARA QUÉ:
//    El mundo se genera de OSM + LIDAR (geometría real, superficies genéricas).
//    El único camino a foto-realismo tipo Street View es FOTOGRAMETRÍA: malla +
//    textura reconstruidas de fotos reales (con Meshroom, que ya tienes). Este
//    sistema es el RECEPTOR de ese pipeline:
//
//    1. Procesas un edificio real en Meshroom → obtienes una malla texturizada
//       (.obj/.fbx/.glb → impórtala en Unity).
//    2. La metes en  Assets/Resources/Fotogrametria/<nombre>
//    3. Añades una entrada aquí (Inspector) con su GPS (lat/lon), rotación y
//       escala. Al entrar en Play, el sistema la coloca SOLA en su sitio real
//       sobre el terreno, convirtiendo lat/lon → coords Unity de Alsasua.
//
//  Así puedes ir sustituyendo edificios procedurales por sus gemelos foto-reales
//  uno a uno, sin tocar código. Si la carpeta está vacía, el sistema no hace
//  nada (inofensivo).
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[DefaultExecutionOrder(205)]   // tras terreno
public class SistemaEdificiosFotogrametria : MonoBehaviour
{
    public static SistemaEdificiosFotogrametria Instance { get; private set; }

    [System.Serializable]
    public struct EdificioFoto
    {
        [Tooltip("Nombre del recurso en Resources/Fotogrametria/ (sin extensión).")]
        public string recurso;
        [Tooltip("Latitud real del edificio (grados, WGS84).")]
        public double latitud;
        [Tooltip("Longitud real del edificio (grados, WGS84).")]
        public double longitud;
        [Tooltip("Rotación Y para alinear la malla con la calle (grados).")]
        public float rotacionY;
        [Tooltip("Escala (1 = tamaño original del modelo).")]
        public float escala;
        [Tooltip("Ajuste vertical fino sobre el terreno (m).")]
        public float offsetY;
    }

    [Header("Edificios foto-reales a colocar")]
    [Tooltip("Cada entrada coloca un modelo de Resources/Fotogrametria/ en su GPS real. " +
             "Ejemplo pre-cargado: la estación de tren (deja una malla llamada 'estacion' ahí).")]
    [SerializeField] List<EdificioFoto> edificios = new()
    {
        // EJEMPLO pre-cargado — coords ≈ estación de Alsasua (GeoDataAlsasua.EstacionTren).
        // Deja una malla en Resources/Fotogrametria/estacion y aparecerá aquí.
        new EdificioFoto { recurso = "estacion", latitud = 42.89698, longitud = -2.16979,
                           rotacionY = 0f, escala = 1f, offsetY = 0f },
    };

    readonly List<GameObject> _colocados = new();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    IEnumerator Start()
    {
        // Esperar terreno (para apoyar los modelos en su cota).
        float t = 0f;
        while (Terrain.activeTerrain == null && t < 15f) { t += 0.5f; yield return new WaitForSeconds(0.5f); }

        int ok = 0;
        foreach (var e in edificios)
        {
            if (string.IsNullOrEmpty(e.recurso)) continue;
            var modelo = Resources.Load<GameObject>($"Fotogrametria/{e.recurso}");
            if (modelo == null)
            {
                AlsasuaLogger.Info("Fotogrametria",
                    $"'{e.recurso}' aún no está en Resources/Fotogrametria/ — se omite (mete la malla y reentra a Play).");
                continue;
            }
            ColocarEdificio(modelo, e);
            ok++;
        }

        if (ok > 0)
            AlsasuaLogger.Info("Fotogrametria", $"{ok} edificio(s) foto-real(es) colocado(s) en su GPS real.");
    }

    void ColocarEdificio(GameObject modelo, EdificioFoto e)
    {
        // lat/lon → metros relativos al centro (Herriko Plaza) → coords Unity.
        double dLat = e.latitud  - GeoDataAlsasua.LATITUD_CENTRO;
        double dLon = e.longitud - GeoDataAlsasua.LONGITUD_CENTRO;
        float ux = GeoDataAlsasua.OX + (float)(dLon * GeoDataAlsasua.M_POR_GRADO_LON);
        float uz = GeoDataAlsasua.OZ + (float)(dLat * GeoDataAlsasua.M_POR_GRADO_LAT);
        float uy = GeoDataAlsasua.AlturaTerreno(ux, uz) + e.offsetY;

        var go = Instantiate(modelo,
            new Vector3(ux, uy, uz),
            Quaternion.Euler(0f, e.rotacionY, 0f));
        go.name = $"Foto_{e.recurso}";
        if (e.escala > 0f) go.transform.localScale = Vector3.one * e.escala;
        go.isStatic = true;

        _colocados.Add(go);
        AlsasuaLogger.Info("Fotogrametria",
            $"'{e.recurso}' → Unity({ux:F0},{uy:F1},{uz:F0})");
    }
}
