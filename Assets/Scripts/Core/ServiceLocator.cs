// Assets/Scripts/Core/ServiceLocator.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SERVICE LOCATOR — punto central de acceso a interfaces de servicio
//
//  Evita que los sistemas de gameplay dependan de clases concretas del
//  GameManager o del Core. Los sistemas registran sus implementaciones
//  en Awake (orden -100) y los consumidores acceden en Start/Update.
//
//  Uso:
//    // Registro (GameManagerAltsasua.Awake):
//    ServiceLocator.Registrar<IWantedSystem>(this);
//    ServiceLocator.Registrar<IEconomyService>(this);
//
//    // Consumo (MisionesAltsasua, PoliciaForalIA…):
//    ServiceLocator.Get<IWantedSystem>()?.AumentarBusqueda(2);
//    ServiceLocator.Get<IEconomyService>()?.GanarDinero(500);
//
//  Si el servicio no está registrado, Get<T>() devuelve null y el consumidor
//  puede hacer null-check seguro — no lanza excepción.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;

public static class ServiceLocator
{
    static readonly Dictionary<System.Type, object> _servicios = new();

    /// <summary>Registra una implementación para la interfaz T.</summary>
    public static void Registrar<T>(T instancia) where T : class
        => _servicios[typeof(T)] = instancia;

    /// <summary>Devuelve la implementación registrada de T, o null si no existe.</summary>
    public static T Get<T>() where T : class
        => _servicios.TryGetValue(typeof(T), out var obj) ? obj as T : null;

    /// <summary>Elimina el registro de T (útil en OnDestroy).</summary>
    public static void Desregistrar<T>() where T : class
        => _servicios.Remove(typeof(T));

    /// <summary>Limpia todos los servicios (útil al cambiar de escena).</summary>
    public static void LimpiarTodo()
        => _servicios.Clear();
}
