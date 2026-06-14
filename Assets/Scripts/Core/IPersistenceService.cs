// Assets/Scripts/Core/IPersistenceService.cs
// ═══════════════════════════════════════════════════════════════════════════
//  CONTRATO — Persistencia por DELTAS del mundo abierto (capa CORE)
//
//  El mundo se regenera procedural y determinista en cada carga (OSM/catastro/
//  LIDAR). Por eso NO guardamos el mundo entero: guardamos solo el DELTA respecto
//  a ese estado base — los objetos que el jugador (o la policía) cambió: una valla
//  rota, un contenedor volcado. Coste de memoria/disco ∝ nº de objetos MODIFICADOS,
//  no nº de objetos del mundo.
//
//  Pieza clave: el ID ESTABLE entre sesiones. GetInstanceID() NO sirve (cambia cada
//  ejecución). Lo derivamos de la POSICIÓN CUANTIZADA + el TIPO, que el generador
//  procedural reproduce idéntico cada vez → el delta guardado vuelve a casar con el
//  objeto re-spawneado, sin importar el orden de instanciación.
//
//  Consumo vía ServiceLocator:
//     var p = ServiceLocator.Get<IPersistenceService>();
//     p?.RegistrarCambio(...);                 // al romperse algo (captura-al-ocurrir)
//     int n = p.ObtenerCambios(coord, buffer); // al recargar el chunk (zero-alloc)
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using UnityEngine;

/// <summary>Naturaleza del cambio persistido. byte→int para JsonUtility.</summary>
public enum TipoCambio
{
    Eliminado = 0,   // el objeto ya no existe (despawn permanente)
    Roto      = 1,   // cambió a su variante "rota" (usa 'estado' para el nivel)
    Desplazado = 2,  // se movió/volcó: usa posDelta + rotDelta
    EstadoCustom = 3 // código libre por-tipo en 'estado'
}

/// <summary>
/// Un delta atómico respecto al estado base procedural. struct de valor,
/// JsonUtility-friendly (sin ulong, sin contenedores anidados).
/// </summary>
[System.Serializable]
public struct PersistentChange
{
    public long       id;        // PersistentId estable (hash determinista)
    public int        chunkX;    // coord de chunk (para indexar y serializar plano)
    public int        chunkZ;
    public int        tipo;      // (TipoCambio)
    public int        estado;    // payload libre por-tipo (nivel de daño, variante…)
    public Vector3    posDelta;  // desplazamiento relativo al base (si Desplazado)
    public Quaternion rotDelta;  // rotación relativa al base (si Desplazado/volcado)

    public Vector2Int Chunk => new Vector2Int(chunkX, chunkZ);
    public TipoCambio Tipo  => (TipoCambio)tipo;
}

/// <summary>
/// Implementado por cualquier objeto persistible del mundo (valla, contenedor…).
/// El restaurador de chunk lo localiza por Id y le aplica su delta ANTES de mostrarlo.
/// </summary>
public interface IPersistente
{
    /// <summary>ID estable entre sesiones (ver PersistentId.DesdePosicion).</summary>
    long IdPersistente { get; }

    /// <summary>Chunk al que pertenece (para indexar el delta).</summary>
    Vector2Int ChunkPersistente { get; }

    /// <summary>Aplica un delta restaurado (lo invoca el command buffer al recargar).</summary>
    void AplicarCambio(in PersistentChange cambio);
}

/// <summary>Servicio Core de persistencia por deltas. Registrado en ServiceLocator.</summary>
public interface IPersistenceService
{
    /// <summary>Captura un cambio en el momento que ocurre (idempotente: upsert por Id).</summary>
    void RegistrarCambio(in PersistentChange cambio);

    /// <summary>True si el chunk tiene algún delta pendiente de aplicar.</summary>
    bool TieneCambios(Vector2Int chunk);

    /// <summary>Vuelca los deltas del chunk en 'buffer' (reutilizable, cero GC). Devuelve cuántos.</summary>
    int ObtenerCambios(Vector2Int chunk, List<PersistentChange> buffer);

    /// <summary>Lookup O(1) del delta de un objeto concreto (para self-restore al spawn).</summary>
    bool TryObtenerCambio(Vector2Int chunk, long id, out PersistentChange cambio);

    /// <summary>Persiste el delta-file a disco.</summary>
    void Guardar();

    /// <summary>Carga el delta-file de disco (en Awake).</summary>
    void Cargar();

    /// <summary>Borra todos los deltas (nueva partida).</summary>
    void Limpiar();
}

/// <summary>
/// Generador de IDs estables. La clave del delta-saving procedural: misma posición
/// + mismo tipo ⇒ mismo Id en toda sesión, independiente del orden de spawn.
/// Requisito: el generador procedural debe colocar el objeto en una posición
/// DETERMINISTA (lo es: viene de OSM/catastro/LIDAR). El grid de cuantización absorbe
/// el ruido de coma flotante.
/// </summary>
public static class PersistentId
{
    /// <summary>FNV-1a 64-bit de (posXZ cuantizada, tipo). 'grid' = tamaño de celda (m).</summary>
    public static long DesdePosicion(Vector3 worldPos, int kind, float grid = 0.1f)
    {
        long qx = (long)System.Math.Round(worldPos.x / grid);
        long qz = (long)System.Math.Round(worldPos.z / grid);
        unchecked
        {
            ulong h = 1469598103934665603UL;            // offset basis
            h = (h ^ (ulong)qx)         * 1099511628211UL;
            h = (h ^ (ulong)qz)         * 1099511628211UL;
            h = (h ^ (ulong)(uint)kind) * 1099511628211UL;
            return (long)h;                              // bits estables; long = JsonUtility-safe
        }
    }
}

/// <summary>
/// Atajo para que un objeto se RESTAURE a sí mismo en el instante de nacer (Awake/
/// Inicializar). Es el camino sin 'pop' y sin carreras para props que spawnean otros
/// sistemas reaccionando a ChunkLoadedEvent (que aún no existen cuando CargarZona acaba).
///     void Inicializar(Vector2Int chunk) { ...calcular Id...; Persistencia.Restaurar(this); }
/// </summary>
public static class Persistencia
{
    public static void Restaurar(IPersistente obj)
    {
        var svc = ServiceLocator.Get<IPersistenceService>();
        if (svc != null && svc.TryObtenerCambio(obj.ChunkPersistente, obj.IdPersistente, out var c))
            obj.AplicarCambio(c);
    }
}
