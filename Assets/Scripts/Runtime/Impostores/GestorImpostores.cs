// Assets/Scripts/_Impostores~/GestorImpostores.cs  (STAGING/DRAFT — carpeta ~ no compila)
// ─────────────────────────────────────────────────────────────────────────────
//  Pool + actualización central de impostores. El StreamerMundoEstatico pide y
//  devuelve impostores aquí (no hace new/Destroy por edificio → sin GC al
//  streamear cientos de edificios), y un único LateUpdate orienta todos los
//  billboards activos con una sola lectura de Camera.main.
//
//  Es el paso previo al batching BRG (fase 3-4 del ADR): cuando todos los
//  impostores activos compartan atlas, esta lista es justo lo que alimenta un
//  Graphics.RenderMeshIndirect / BatchRendererGroup en 1 draw call. De momento
//  cada billboard dibuja con su MeshRenderer, pero ya sin coste de gestión.
//
//  Uso desde el streamer:
//     var imp = GestorImpostores.Instance.Adquirir(idOSM, transform);
//     ... al volver a 'Activo':
//     GestorImpostores.Instance.Liberar(imp);
// ─────────────────────────────────────────────────────────────────────────────
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(50)]
public class GestorImpostores : MonoBehaviour
{
    public static GestorImpostores Instance { get; private set; }

    [Tooltip("Atlas horneado (ImpostorAtlas.asset). Todos los impostores lo comparten.")]
    public ImpostorAtlasSO atlas;

    readonly List<ImpostorBillboard> _activos = new();
    readonly Stack<ImpostorBillboard> _pool = new();
    Transform _cuna;   // padre de los impostores en pausa (ocultos)

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        _cuna = new GameObject("ImpostoresPool").transform;
        _cuna.SetParent(transform, false);
        _cuna.gameObject.SetActive(false);   // la cuna inactiva oculta el pool
    }

    /// <summary>Da un impostor para 'id' (reusa del pool si hay). Null si no hay atlas/entrada.</summary>
    public ImpostorBillboard Adquirir(long id, Transform parent = null)
    {
        if (atlas == null || !atlas.TryGet(id, out _)) return null;

        ImpostorBillboard imp;
        if (_pool.Count > 0)
        {
            imp = _pool.Pop();
            imp.transform.SetParent(parent, false);
            imp.gameObject.SetActive(true);
            imp.Reasignar(atlas, id);
        }
        else
        {
            imp = ImpostorBillboard.Crear(atlas, id, parent);
        }
        imp.gestionado = true;           // lo orientamos nosotros
        _activos.Add(imp);
        return imp;
    }

    /// <summary>Devuelve el impostor al pool (oculto), listo para reusar.</summary>
    public void Liberar(ImpostorBillboard imp)
    {
        if (imp == null) return;
        _activos.Remove(imp);
        imp.gameObject.SetActive(false);
        imp.transform.SetParent(_cuna, false);
        _pool.Push(imp);
    }

    void LateUpdate()
    {
        var cam = Camera.main;
        if (cam == null) return;
        for (int i = _activos.Count - 1; i >= 0; i--)
        {
            var imp = _activos[i];
            if (imp == null) { _activos.RemoveAt(i); continue; }
            imp.Orientar(cam);
        }
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    public int Activos => _activos.Count;
    public int EnPool  => _pool.Count;
}
