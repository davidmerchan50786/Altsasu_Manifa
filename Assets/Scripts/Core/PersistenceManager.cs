// Assets/Scripts/Core/PersistenceManager.cs
// ═══════════════════════════════════════════════════════════════════════════
//  PERSISTENCE MANAGER — almacén de deltas indexado por chunk (capa CORE)
//
//  Implementa IPersistenceService. Mantiene SOLO los cambios respecto al estado
//  base procedural, indexados por coord de chunk para que recargar un chunk sea
//  O(k) con k = deltas de ESE chunk (no del mundo entero).
//
//  Modelo "captura-al-ocurrir": el delta se graba en el instante en que el objeto
//  cambia (p.ej. SistemaDestruccion al romper la valla). Así, aunque el chunk se
//  descargue, el dato ya está guardado — nunca hay que leer el estado de un objeto
//  ya despawneado.
//
//  Se auto-arranca (RuntimeInitializeOnLoadMethod) y se registra en ServiceLocator
//  ANTES de que el streaming pida nada. Persiste a Application.persistentDataPath.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using System.IO;
using UnityEngine;

public sealed class PersistenceManager : MonoBehaviour, IPersistenceService
{
    const string ARCHIVO = "world_deltas.json";

    // chunk → (id → delta). Dict anidado: query por chunk O(1), upsert por id O(1).
    readonly Dictionary<Vector2Int, Dictionary<long, PersistentChange>> _porChunk = new();

    string Ruta => Path.Combine(Application.persistentDataPath, ARCHIVO);

    // ── Auto-arranque: existe y está registrado antes que el streaming ──────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        if (ServiceLocator.Get<IPersistenceService>() != null) return;
        var go = new GameObject("PersistenceManager");
        DontDestroyOnLoad(go);
        go.AddComponent<PersistenceManager>();
    }

    void Awake()
    {
        if (ServiceLocator.Get<IPersistenceService>() != null && !ReferenceEquals(ServiceLocator.Get<IPersistenceService>(), this))
        { Destroy(gameObject); return; }

        ServiceLocator.Registrar<IPersistenceService>(this);
        Cargar();
    }

    void OnDestroy()
    {
        if (ReferenceEquals(ServiceLocator.Get<IPersistenceService>(), this))
        {
            Guardar();
            ServiceLocator.Desregistrar<IPersistenceService>();
        }
    }

    void OnApplicationQuit() => Guardar();
    void OnApplicationPause(bool pausado) { if (pausado) Guardar(); } // móvil/alt-tab

    // ── API ─────────────────────────────────────────────────────────────────

    public void RegistrarCambio(in PersistentChange cambio)
    {
        var chunk = cambio.Chunk;
        if (!_porChunk.TryGetValue(chunk, out var porId))
            _porChunk[chunk] = porId = new Dictionary<long, PersistentChange>();
        porId[cambio.id] = cambio;   // upsert idempotente
    }

    public bool TieneCambios(Vector2Int chunk)
        => _porChunk.TryGetValue(chunk, out var d) && d.Count > 0;

    public int ObtenerCambios(Vector2Int chunk, List<PersistentChange> buffer)
    {
        buffer.Clear();
        if (_porChunk.TryGetValue(chunk, out var porId))
            foreach (var c in porId.Values) buffer.Add(c);
        return buffer.Count;
    }

    public bool TryObtenerCambio(Vector2Int chunk, long id, out PersistentChange cambio)
    {
        cambio = default;
        return _porChunk.TryGetValue(chunk, out var porId) && porId.TryGetValue(id, out cambio);
    }

    public void Limpiar() => _porChunk.Clear();   // nueva partida (el próximo Guardar reescribe)

    // ── Serialización (delta-file plano; JsonUtility no serializa diccionarios) ──
    [System.Serializable] class Archivo { public List<PersistentChange> cambios = new(); }

    public void Guardar()
    {
        try
        {
            var arch = new Archivo();
            foreach (var porId in _porChunk.Values)
                foreach (var c in porId.Values) arch.cambios.Add(c);

            File.WriteAllText(Ruta, JsonUtility.ToJson(arch));
            AlsasuaLogger.Info("Persistencia", $"Guardados {arch.cambios.Count} deltas → {ARCHIVO}.");
        }
        catch (System.Exception e)
        {
            AlsasuaLogger.Warn("Persistencia", $"No se pudo guardar: {e.Message}");
        }
    }

    public void Cargar()
    {
        _porChunk.Clear();
        try
        {
            if (!File.Exists(Ruta)) return;
            var arch = JsonUtility.FromJson<Archivo>(File.ReadAllText(Ruta));
            if (arch?.cambios == null) return;
            for (int i = 0; i < arch.cambios.Count; i++) RegistrarCambio(arch.cambios[i]);
            AlsasuaLogger.Info("Persistencia", $"Cargados {arch.cambios.Count} deltas de {ARCHIVO}.");
        }
        catch (System.Exception e)
        {
            AlsasuaLogger.Warn("Persistencia", $"No se pudo cargar (se ignora el delta-file): {e.Message}");
            _porChunk.Clear();
        }
    }
}
