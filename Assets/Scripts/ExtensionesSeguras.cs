// Assets/Scripts/ExtensionesSeguras.cs
// ═══════════════════════════════════════════════════════════════════════════
//  EXTENSIONES SEGURAS — métodos de extensión null-safe para Unity
//
//  Uso:
//    go.GetSafe<MeshRenderer>()?.material    → nunca lanza NullReferenceException
//    go.Require<Rigidbody>()                 → lanza excepción con mensaje claro
//    AudioManager.I.PlaySafe(clip, pos)      → no hace nada si I es null
//    transform.GetOrAdd<NavMeshAgent>()      → obtiene o añade el componente
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

public static class ExtensionesSeguras
{
    // ── GetComponent null-safe ────────────────────────────────────────────

    /// <summary>GetComponent que devuelve null en vez de lanzar excepción.</summary>
    public static T GetSafe<T>(this GameObject go) where T : Component
    {
        if (go == null) return null;
        return go.GetComponent<T>();
    }

    public static T GetSafe<T>(this Component c) where T : Component
    {
        if (c == null) return null;
        return c.GetComponent<T>();
    }

    /// <summary>GetComponent que lanza excepción clara si falta.</summary>
    public static T Require<T>(this GameObject go) where T : Component
    {
        if (go == null) throw new System.NullReferenceException($"Require<{typeof(T).Name}>: GameObject es null");
        var c = go.GetComponent<T>();
        if (c == null) throw new System.MissingComponentException($"{go.name} requiere componente {typeof(T).Name}");
        return c;
    }

    /// <summary>Obtiene el componente o lo añade si no existe.</summary>
    public static T GetOrAdd<T>(this GameObject go) where T : Component
    {
        if (go == null) return null;
        var c = go.GetComponent<T>();
        return c != null ? c : go.AddComponent<T>();
    }

    public static T GetOrAdd<T>(this Component comp) where T : Component
        => comp?.gameObject.GetOrAdd<T>();

    // ── Material seguro ────────────────────────────────────────────────────

    /// <summary>Establece color de material de forma segura.</summary>
    public static void SetColorSafe(this GameObject go, Color color)
    {
        var r = go?.GetComponent<Renderer>();
        if (r == null || r.material == null) return;
        r.material.color = color;
    }

    public static void SetSharedMaterialSafe(this GameObject go, Material mat)
    {
        var r = go?.GetComponent<Renderer>();
        if (r != null) r.sharedMaterial = mat;
    }

    // ── Transform seguro ──────────────────────────────────────────────────

    /// <summary>SetParent seguro — no lanza si padre o hijo son null.</summary>
    public static void SetParentSafe(this Transform t, Transform padre, bool worldPositionStays = false)
    {
        if (t != null && padre != null) t.SetParent(padre, worldPositionStays);
    }

    // ── NavMeshAgent seguro ───────────────────────────────────────────────

    public static bool SetDestinationSafe(this NavMeshAgent agent, Vector3 dest)
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return false;
        try { return agent.SetDestination(dest); }
        catch { return false; }
    }

    // ── AudioSource seguro ────────────────────────────────────────────────

    public static void PlaySafe(this AudioSource src, AudioClip clip = null)
    {
        if (src == null) return;
        if (clip != null) src.clip = clip;
        if (src.clip != null) src.Play();
    }

    public static void PlayOneShotSafe(this AudioSource src, AudioClip clip, float volume = 1f)
    {
        if (src != null && clip != null) src.PlayOneShot(clip, volume);
    }

    // ── Instantiate seguro ────────────────────────────────────────────────

    public static GameObject InstantiateSafe(GameObject prefab, Vector3 pos, Quaternion rot,
                                              Transform parent = null)
    {
        if (prefab == null) return null;
        try
        {
            var go = parent != null
                ? Object.Instantiate(prefab, pos, rot, parent)
                : Object.Instantiate(prefab, pos, rot);
            return go;
        }
        catch (System.Exception e)
        {
            AlsasuaLogger.Warn("Instantiate", $"Error al instanciar {prefab.name}: {e.Message}");
            return null;
        }
    }

    // ── Physics seguros ───────────────────────────────────────────────────

    /// <summary>OverlapSphere que devuelve array vacío en vez de null.</summary>
    public static Collider[] OverlapSphereSafe(Vector3 pos, float radius, int layerMask = ~0)
    {
        try { return Physics.OverlapSphere(pos, radius, layerMask); }
        catch { return System.Array.Empty<Collider>(); }
    }

    // ── Terrain seguro ────────────────────────────────────────────────────

    /// <summary>SampleHeight seguro — devuelve altitud por defecto si no hay terrain.</summary>
    public static float SampleHeightSafe(Vector3 worldPos, float defaultHeight = 240f)
    {
        var t = Terrain.activeTerrain;
        if (t == null) return defaultHeight;
        try { return t.SampleHeight(worldPos) + t.transform.position.y; }
        catch { return defaultHeight; }
    }

    // ── Singleton seguro ──────────────────────────────────────────────────

    /// <summary>Valida que el singleton es accesible antes de usarlo.</summary>
    public static bool EstaListo<T>(this T singleton) where T : MonoBehaviour
        => singleton != null && singleton.gameObject.activeInHierarchy;

    // ── Collections seguras ───────────────────────────────────────────────

    public static TValue GetSafe<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey key)
    {
        if (dict == null) return default;
        dict.TryGetValue(key, out var val);
        return val;
    }

    public static T GetSafeIndex<T>(this T[] arr, int idx)
    {
        if (arr == null || idx < 0 || idx >= arr.Length) return default;
        return arr[idx];
    }

    public static T GetSafeIndex<T>(this List<T> list, int idx)
    {
        if (list == null || idx < 0 || idx >= list.Count) return default;
        return list[idx];
    }
}
