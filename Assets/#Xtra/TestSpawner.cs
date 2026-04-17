using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TestSpawner : MonoBehaviour
{
    public GameObject Spawnee;

    public Transform SpawnPos;

    [Tooltip("Number of instances pre-warmed in the pool at startup")]
    public int PoolInitialSize = 5;

    private ObjectPool _pool;

    void Start()
    {
        if (Spawnee != null)
            _pool = new ObjectPool(Spawnee, PoolInitialSize);
    }

    public void Spawn()
    {
        if (_pool != null)
            _pool.Get(SpawnPos.position, SpawnPos.rotation);
        else
            Instantiate(Spawnee, SpawnPos.position, SpawnPos.rotation);
    }

    public void Despawn(GameObject instance)
    {
        if (_pool != null)
            _pool.Return(instance);
        else
            Destroy(instance);
    }
}
