// Assets/Scripts/SistemaBarricadas.cs
// ═══════════════════════════════════════════════════════════════════════════
//  Sistema de barricadas para el simulador de Alsasua.
//
//  Combina los nuevos assets del proyecto:
//    · Abandoned World / Metal and Concrete Barrier  → barricadas de hormigón y metal
//    · BarrierPack / Barricada Concreto              → barricada alternativa
//    · Vefects / Free Fire VFX  o  LiteFireEffect    → VFX de fuego
//
//  Las barricadas se colocan en líneas alrededor de la zona de manifestación,
//  bloqueando las entradas principales al pueblo. Cada barricada puede estar
//  ardiendo (con el VFX asignado) o sin fuego.
//
//  Uso desde GestorEscena:
//    sistemaBarricadas.AsignarPrefabs(hormigon, metal, vfxFuego);
//    // (SistemaAssets lo hace automáticamente en PropagarAssets)
//
//  Fallback: si ningún prefab está disponible, cada barricada usa el
//  sistema procedural de BarricadaFuego (cajas + partículas por código).
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using System.Collections.Generic;

[AddComponentMenu("Alsasua/Sistema Barricadas")]
public sealed class SistemaBarricadas : MonoBehaviour
{
    // ───────────────────────────────────────────────────────────────────────
    //  INSPECTOR
    // ───────────────────────────────────────────────────────────────────────

    [Header("═══ PREFABS (se asignan desde SistemaAssets) ═══")]
    [Tooltip("Prefab barricada de hormigón (Concrete_Barrier_1.prefab). " +
             "Null → fallback procedural BarricadaFuego.")]
    [SerializeField] private GameObject prefabHormigon;

    [Tooltip("Prefab barricada metálica (Metal_Barrier_1.prefab). " +
             "Null → usa el prefab de hormigón.")]
    [SerializeField] private GameObject prefabMetal;

    [Tooltip("Prefab VFX de fuego (VFX_Fire_Floor_01 o BaseFire000). " +
             "Se instancia sobre cada barricada ardiendo. Null → partículas procedurales.")]
    [SerializeField] private GameObject prefabVFXFuego;

    [Header("═══ CONFIGURACIÓN ═══")]
    [Tooltip("Centro de la zona de manifestación en coordenadas mundo.")]
    [SerializeField] private Vector3 centroManifestacion = new Vector3(-50f, 0f, 0f);

    [Tooltip("Radio de la zona donde se colocan las líneas de barricada (metros).")]
    [Range(20f, 200f)]
    [SerializeField] private float radioZona = 60f;

    [Tooltip("Número de barricadas por línea.")]
    [Range(2, 12)]
    [SerializeField] private int barricadasPorLinea = 5;

    [Tooltip("Separación entre barricadas de la misma línea (metros).")]
    [Range(1f, 5f)]
    [SerializeField] private float separacion = 2.2f;

    [Tooltip("Porcentaje de barricadas que empiezan ardiendo (0 = ninguna, 1 = todas).")]
    [Range(0f, 1f)]
    [SerializeField] private float porcentajeArdiendo = 0.6f;

    [Tooltip("Mezclar tipos: hormigón y metal aleatorio. False = solo hormigón.")]
    [SerializeField] private bool mezclarTipos = true;

    [Header("═══ ENTRADAS A BLOQUEAR ═══")]
    [Tooltip("Ángulos (grados desde Norte) de las entradas al pueblo a bloquear. " +
             "Por defecto: Norte (0°), Sur (180°), Este (90°), Noroeste (315°).")]
    [SerializeField] private float[] angulosEntradas = { 0f, 90f, 180f, 315f };

    // ───────────────────────────────────────────────────────────────────────
    //  ESTADO INTERNO
    // ───────────────────────────────────────────────────────────────────────
    private readonly List<BarricadaFuego> _barricadas = new List<BarricadaFuego>();

    // ───────────────────────────────────────────────────────────────────────
    //  UNITY
    // ───────────────────────────────────────────────────────────────────────
    private void Start()
    {
        // SistemaAssets ya debería haber llamado a AsignarPrefabs() en su Awake().
        // Si no, SpawnearBarricadas usa el fallback procedural.
        SpawnearBarricadas();
    }

    // ───────────────────────────────────────────────────────────────────────
    //  API PÚBLICA
    // ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Llamado por SistemaAssets.PropagarAssets() para inyectar los prefabs
    /// de los nuevos assets (Abandoned World Barriers + VFX).
    /// </summary>
    public void AsignarPrefabs(GameObject hormigon, GameObject metal, GameObject vfxFuego)
    {
        prefabHormigon = hormigon;
        prefabMetal    = metal;
        prefabVFXFuego = vfxFuego;


        AlsasuaLogger.Info("SistemaBarricadas",
            $"Prefabs asignados → hormigón: {NombreO(hormigon)}, " +
            $"metal: {NombreO(metal)}, VFX fuego: {NombreO(vfxFuego)}");
    }

    /// <summary>
    /// Prender fuego a todas las barricadas activas.
    /// </summary>
    public void PrenderTodas()
    {
        foreach (var b in _barricadas)
            if (b != null) b.PrenderFuego();
        AlsasuaLogger.Info("SistemaBarricadas", $"🔥 {_barricadas.Count} barricadas prendidas.");
    }

    /// <summary>
    /// Aplica daño a todas las barricadas (por explosión de área, etc.).
    /// </summary>
    public void DanoArea(Vector3 centro, float radio, int daño)
    {
        int afectadas = 0;
        foreach (var b in _barricadas)
        {
            if (b == null) continue;
            if (Vector3.Distance(b.transform.position, centro) <= radio)
            {
                b.RecibirDano(daño);
                afectadas++;
            }
        }
        if (afectadas > 0)
            AlsasuaLogger.Info("SistemaBarricadas", $"💥 {afectadas} barricadas dañadas en radio {radio}m.");
    }

    // ───────────────────────────────────────────────────────────────────────
    //  SPAWN
    // ───────────────────────────────────────────────────────────────────────
    private void SpawnearBarricadas()
    {
        if (angulosEntradas == null || angulosEntradas.Length == 0)
        {
            AlsasuaLogger.Warn("SistemaBarricadas", "No hay ángulos de entrada configurados.");
            return;
        }

        int totalSpawneadas = 0;

        foreach (float angulo in angulosEntradas)
        {
            // Dirección de la entrada (Unity: Z = Norte)
            float rad = angulo * Mathf.Deg2Rad;
            var direccion = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));

            // Centro de la línea de barricada
            var centroLinea = centroManifestacion + direccion * radioZona;

            // Vector perpendicular para distribuir las barricadas en línea
            var perp = new Vector3(-direccion.z, 0f, direccion.x);

            for (int i = 0; i < barricadasPorLinea; i++)
            {
                float offset = (i - (barricadasPorLinea - 1) / 2f) * separacion;
                var posicion = centroLinea + perp * offset;

                // Pequeña variación de altura para terreno irregular
                posicion.y += Random.Range(-0.1f, 0.1f);

                float rotY = angulo + 90f + Random.Range(-5f, 5f);
                bool ardiendo = Random.value < porcentajeArdiendo;

                SpawnearBarricada(posicion, rotY, ardiendo);
                totalSpawneadas++;
            }
        }

        AlsasuaLogger.Info("SistemaBarricadas",
            $"✓ {totalSpawneadas} barricadas colocadas en {angulosEntradas.Length} entradas. " +
            $"Assets: {(prefabHormigon != null ? "hormigón ✓" : "hormigón → procedural")}, " +
            $"{(prefabMetal != null ? "metal ✓" : "metal → procedural")}, " +
            $"{(prefabVFXFuego != null ? "VFX fuego ✓" : "VFX → partículas")}.");
    }

    private void SpawnearBarricada(Vector3 posicion, float rotacionY, bool ardiendo)
    {
        // Elegir tipo: hormigón o metal (aleatoriamente si mezclarTipos está activo)
        bool usarMetal = mezclarTipos && prefabMetal != null && Random.value > 0.5f;
        GameObject prefabElegido = usarMetal ? prefabMetal : prefabHormigon;

        if (prefabElegido != null)
        {
            // Instanciar el prefab real del asset (Abandoned World / BarrierPack)
            var go = Instantiate(prefabElegido, posicion, Quaternion.Euler(0f, rotacionY, 0f));
            go.name = usarMetal ? "BarricadaMetal" : "BarricadaHormigon";
            go.transform.SetParent(transform);

            // Añadir BarricadaFuego para gestión de daño y fuego
            var bf = go.AddComponent<BarricadaFuego>();

            // Inyectar VFX externo si está disponible
            if (prefabVFXFuego != null)
                InjectarVFXFuego(bf, prefabVFXFuego);

            if (ardiendo) bf.PrenderFuego();
            _barricadas.Add(bf);
        }
        else
        {
            // Fallback: barricada 100% procedural (cajas + partículas)
            var bf = BarricadaFuego.Crear(posicion, rotacionY);
            bf.transform.SetParent(transform);
            if (ardiendo) bf.PrenderFuego();
            _barricadas.Add(bf);
        }
    }

    /// <summary>
    /// Inyecta el campo privado prefabVFXFuego de BarricadaFuego via reflexión.
    /// Esto evita tener que modificar BarricadaFuego solo para exponer el campo.
    /// </summary>
    private static void InjectarVFXFuego(BarricadaFuego barricada, GameObject prefab)
    {
        // BarricadaFuego ya expone el campo [SerializeField] prefabVFXFuego (privado).
        // Usamos reflexión para inyectarlo antes de que Start() lo lea.
        var campo = typeof(BarricadaFuego).GetField(
            "prefabVFXFuego",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (campo != null)
            campo.SetValue(barricada, prefab);
        else
            AlsasuaLogger.Warn("SistemaBarricadas",
                "No se encontró el campo 'prefabVFXFuego' en BarricadaFuego. " +
                "El VFX externo no se asignará automáticamente.");
    }

    // ───────────────────────────────────────────────────────────────────────
    //  GIZMOS
    // ───────────────────────────────────────────────────────────────────────
#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (angulosEntradas == null) return;

        // Círculo de zona de manifestación
        Gizmos.color = new Color(1f, 0.4f, 0f, 0.3f);
        DrawCircleGizmo(centroManifestacion, radioZona, 32);

        // Líneas de barricada en cada entrada
        Gizmos.color = new Color(0.8f, 0.2f, 0.1f, 0.8f);
        foreach (float angulo in angulosEntradas)
        {
            float rad      = angulo * Mathf.Deg2Rad;
            var dir        = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
            var centroLinea = centroManifestacion + dir * radioZona;
            var perp       = new Vector3(-dir.z, 0f, dir.x);

            float longitud = (barricadasPorLinea - 1) * separacion;
            Gizmos.DrawLine(centroLinea - perp * longitud * 0.5f,
                            centroLinea + perp * longitud * 0.5f);
            Gizmos.DrawWireCube(centroLinea, new Vector3(longitud, 1.5f, 0.5f));
        }
    }

    private static void DrawCircleGizmo(Vector3 centro, float radio, int segmentos)
    {
        for (int i = 0; i < segmentos; i++)
        {
            float a1 = (float)i       / segmentos * Mathf.PI * 2f;
            float a2 = (float)(i + 1) / segmentos * Mathf.PI * 2f;
            Gizmos.DrawLine(
                centro + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * radio,
                centro + new Vector3(Mathf.Cos(a2), 0f, Mathf.Sin(a2)) * radio);
        }
    }
#endif

    // ───────────────────────────────────────────────────────────────────────
    //  HELPERS
    // ───────────────────────────────────────────────────────────────────────
    private static string NombreO(Object o) => o != null ? o.name : "null";
}
