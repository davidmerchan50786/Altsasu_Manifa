using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generic object pool. Eliminates per-shot Instantiate/Destroy allocations.
/// Usage:
///   var pool = new ObjectPool(prefab, initialSize, parent);
///   GameObject obj = pool.Get(position, rotation);
///   pool.Return(obj);
/// </summary>
public class ObjectPool
{
    private readonly GameObject _prefab;
    private readonly Transform _parent;
    private readonly Queue<GameObject> _available = new Queue<GameObject>();

    public ObjectPool(GameObject prefab, int initialSize, Transform parent = null)
    {
        _prefab = prefab;
        _parent = parent;

        for (int i = 0; i < initialSize; i++)
            Return(Create());
    }

    public GameObject Get(Vector3 position, Quaternion rotation)
    {
        GameObject obj = _available.Count > 0 ? _available.Dequeue() : Create();
        obj.transform.SetPositionAndRotation(position, rotation);
        obj.SetActive(true);
        return obj;
    }

    public void Return(GameObject obj)
    {
        obj.SetActive(false);
        if (_parent != null)
            obj.transform.SetParent(_parent);
        _available.Enqueue(obj);
    }

    private GameObject Create()
    {
        GameObject obj = Object.Instantiate(_prefab, _parent);
        obj.SetActive(false);
        return obj;
    }
}
