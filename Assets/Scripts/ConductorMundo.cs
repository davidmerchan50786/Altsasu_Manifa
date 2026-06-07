// Assets/Scripts/ConductorMundo.cs
// ═══════════════════════════════════════════════════════════════════════════
//  CONDUCTOR DEL MUNDO — "el mejor de cada uno"
//
//  Tras unificar sistemas, hay dominios con VARIOS sistemas que hacen lo mismo
//  (vegetación, ríos, mobiliario…). Si dos corren a la vez → dobles spawns.
//  Este conductor: documenta el orden, detecta solapes y (opcional) desactiva
//  los redundantes. Reversible y seguro (component.enabled = false, no borra).
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

[DefaultExecutionOrder(-95)]
public class ConductorMundo : MonoBehaviour
{
    public static ConductorMundo Instance { get; private set; }

    [Header("Comportamiento")]
    [Tooltip("Si está activo, desactiva los sistemas redundantes dejando sólo el elegido. " +
             "Por defecto OFF: primero sólo informa, para que pruebes sin riesgo.")]
    public bool aplicarRecomendaciones = false;

    [Tooltip("Colapsar instancias duplicadas del mismo sistema (siempre seguro).")]
    public bool colapsarDuplicados = true;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void Start()
    {
        AlsasuaLogger.Info("Conductor", "── Mundo: el mejor de cada dominio ──");

        if (colapsarDuplicados)
        {
            ColapsarDuplicados<AlsasuaTreeStreamer>();
            ColapsarDuplicados<MobiliarioUrbano>();
            ColapsarDuplicados<GeneradorRiosYPuentes>();
            ColapsarDuplicados<SistemaMultitud>();
            ColapsarDuplicados<SistemaVientoVegetacion>();
            ColapsarDuplicados<SistemaCharcos>();
            ColapsarDuplicados<SistemaHumoFabricas>();
            ColapsarDuplicados<SistemaTren>();
            ColapsarDuplicados<SistemaTuneles>();
        }

        Resolver("Árboles/vegetación", typeof(AlsasuaTreeStreamer), typeof(SistemaVegetacion));
        Resolver("Río + puentes",      typeof(GeneradorRiosYPuentes), null);
        Resolver("Mobiliario urbano",  typeof(MobiliarioUrbano),     null);

        AlsasuaLogger.Info("Conductor",
            aplicarRecomendaciones
              ? "Recomendaciones APLICADAS (redundantes desactivados)."
              : "Modo informe (no se ha desactivado nada). Activa 'aplicarRecomendaciones' para limpiar.");
    }

    void ColapsarDuplicados<T>() where T : Behaviour
    {
        var todos = FindObjectsByType<T>(FindObjectsSortMode.InstanceID);
        for (int i = 1; i < todos.Length; i++)
        {
            todos[i].enabled = false;
            AlsasuaLogger.Warn("Conductor", $"Duplicado de {typeof(T).Name} desactivado (mantengo 1).");
        }
    }

    void Resolver(string dominio, System.Type preferido, System.Type redundante)
    {
        var pref = FindFirstObjectByType(preferido) as Behaviour;
        if (pref == null)
        {
            AlsasuaLogger.Warn("Conductor", $"[{dominio}] no hay '{preferido.Name}' en escena.");
            return;
        }
        AlsasuaLogger.Info("Conductor", $"[{dominio}] uso: {preferido.Name}");

        if (redundante == null) return;
        var red = FindFirstObjectByType(redundante) as Behaviour;
        if (red == null) return;

        if (aplicarRecomendaciones)
        {
            red.enabled = false;
            AlsasuaLogger.Info("Conductor", $"[{dominio}] desactivado redundante: {redundante.Name}");
        }
        else
        {
            AlsasuaLogger.Warn("Conductor",
                $"[{dominio}] SOLAPE: '{redundante.Name}' también activo. Recomiendo desactivarlo.");
        }
    }

    void OnDestroy() { if (Instance == this) Instance = null; }
}
