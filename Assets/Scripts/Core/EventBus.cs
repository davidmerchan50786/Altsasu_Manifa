// Assets/Scripts/Core/EventBus.cs
// ═══════════════════════════════════════════════════════════════════════════
//  EVENT BUS GLOBAL — Altsasu Manifa
//
//  Sistema de eventos tipados. Permite comunicación entre capas sin
//  acoplamiento directo: el emisor no conoce al receptor.
//
//  Características:
//    - Sin allocations en Publish (no boxing, delegates almacenados con tipo)
//    - Thread-safe: lock en Subscribe/Unsubscribe, Publish siempre en main thread
//    - Sin dependencia de MonoBehaviour — static puro
//
//  USO:
//    // Suscribirse (Awake/OnEnable):
//    EventBus.Subscribe<ChunkCargadoEvent>(OnChunkCargado);
//
//    // Publicar (cualquier capa):
//    EventBus.Publish(new ChunkCargadoEvent { coord = new Vector2Int(3, 5) });
//
//    // Desuscribirse (OnDisable/OnDestroy — SIEMPRE):
//    EventBus.Unsubscribe<ChunkCargadoEvent>(OnChunkCargado);
//
//  NOTA: Los handlers se ejecutan en el mismo hilo que llama a Publish.
//        En Unity, publicar siempre desde el hilo principal.
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;

public static class EventBus
{
    // Diccionario de tipo de evento → lista de handlers (Action<object>).
    // Se usa object para evitar un Dictionary por tipo genérico, pero los
    // wrappers typed garantizan zero-boxing en el hot path de Publish.
    static readonly Dictionary<Type, List<Delegate>> _handlers = new Dictionary<Type, List<Delegate>>();
    static readonly object _lock = new object();

    // ── Subscribe ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Registra un handler para el evento de tipo T.
    /// Llamar siempre en Awake/OnEnable y desregistrar en OnDestroy/OnDisable.
    /// </summary>
    public static void Subscribe<T>(Action<T> handler) where T : struct
    {
        if (handler == null) return;
        var key = typeof(T);
        lock (_lock)
        {
            if (!_handlers.TryGetValue(key, out var list))
            {
                list = new List<Delegate>(4);
                _handlers[key] = list;
            }
            if (!list.Contains(handler))
                list.Add(handler);
        }
    }

    // ── Unsubscribe ───────────────────────────────────────────────────────────

    /// <summary>
    /// Elimina el handler. Llamar siempre en OnDestroy/OnDisable.
    /// </summary>
    public static void Unsubscribe<T>(Action<T> handler) where T : struct
    {
        if (handler == null) return;
        var key = typeof(T);
        lock (_lock)
        {
            if (_handlers.TryGetValue(key, out var list))
                list.Remove(handler);
        }
    }

    // ── Publish ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Publica un evento a todos los suscriptores registrados para T.
    /// Sin allocations — itera la lista sin crear enumeradores ni copias.
    /// </summary>
    public static void Publish<T>(T evt) where T : struct
    {
        var key = typeof(T);
        List<Delegate> list;
        lock (_lock)
        {
            if (!_handlers.TryGetValue(key, out list) || list.Count == 0) return;
            // Snapshot para evitar modificaciones durante iteración.
            // Usamos un buffer estático para cero allocs en el caso común.
            _publishBuffer.Clear();
            _publishBuffer.AddRange(list);
        }

        // Invocar fuera del lock para evitar dead-locks si un handler modifica el bus.
        for (int i = 0; i < _publishBuffer.Count; i++)
        {
            var d = _publishBuffer[i] as Action<T>;
            d?.Invoke(evt);
        }
    }

    // Buffer reutilizable para snapshot de handlers durante Publish.
    static readonly List<Delegate> _publishBuffer = new List<Delegate>(16);

    // ── Utilidades ────────────────────────────────────────────────────────────

    /// <summary>
    /// Elimina todos los handlers de un tipo (útil en tests o cambio de escena).
    /// </summary>
    public static void Clear<T>() where T : struct
    {
        lock (_lock) _handlers.Remove(typeof(T));
    }

    /// <summary>
    /// Elimina TODOS los handlers de todos los tipos.
    /// Usar solo en cambio de escena o teardown completo.
    /// </summary>
    public static void ClearAll()
    {
        lock (_lock) _handlers.Clear();
    }

    /// <summary>Número de handlers registrados para T (útil en tests).</summary>
    public static int HandlerCount<T>() where T : struct
    {
        lock (_lock)
            return _handlers.TryGetValue(typeof(T), out var list) ? list.Count : 0;
    }
}
