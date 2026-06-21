// Assets/Scripts/Runtime/MosaicoV3Clipmap.cs
// ═══════════════════════════════════════════════════════════════════════════
//  MOSAICO V3 CLIPMAP — actualización dinámica del anillo central
//
//  Complementa MosaicoV3Sistema (que crea mallas estáticas). Este componente
//  hace el anillo 0 (urbano, mayor detalle) DINÁMICO:
//
//    · Cuando el jugador se aleja más de UPDATE_THRESHOLD metros del centro
//      anterior, re-muestrea las alturas del anillo 0 usando
//      IMuestreadorAlturaPrecisa (ya en RAM) y actualiza los vértices Y.
//
//    · Los anillos 1 y 2 (valle / sierras) siguen estáticos: se generan
//      en el baker y no se mueven (son tan grandes que no hay ganancia
//      visual en actualizarlos dinámicamente).
//
//    · La actualización se reparte sobre múltiples frames (coroutine
//      presupuestada) para no causar picos de CPU.
//
//  LIMITACIÓN ACTUAL:
//    El anillo 0 NO mueve su centro geométrico con el jugador — solo
//    actualiza los valores Y. Para el clipmap COMPLETO (el centro se desplaza
//    para dar más resolución cerca del jugador) se necesita regenerar también
//    las posiciones XZ, lo que implica cambiar la topología. Eso es el
//    "Mosaico V3 full clipmap" — esta clase es la Fase 0 del clipmap dinámico.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using UnityEngine;

[DefaultExecutionOrder(-80)]
public sealed class MosaicoV3Clipmap : MonoBehaviour
{
    const string SO_RESOURCES     = "MosaicoV3/MosaicoV3SO";
    const float  UPDATE_THRESHOLD = 80f;    // m de movimiento del jugador para actualizar
    const float  MS_BUDGET        = 3f;     // ms de CPU por frame durante la actualización
    const int    ROWS_PER_YIELD   = 24;     // filas de vértices procesadas antes de ceder el frame

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var so = Resources.Load<MosaicoV3SO>(SO_RESOURCES);
        if (so == null || so.mallasPorAnillo == null || so.mallasPorAnillo.Length == 0) return;

        var go = new GameObject("MosaicoV3Clipmap");
        DontDestroyOnLoad(go);
        go.AddComponent<MosaicoV3Clipmap>();
    }

    // ── Estado ────────────────────────────────────────────────────────────
    Mesh       _mallaAnillo0;   // copia mutable (no el asset compartido)
    Vector3[]  _vertsAnillo0;   // buffer reutilizable
    int        _gridN;          // número de quads por eje del anillo 0
    Transform  _jugador;
    Vector3    _centroUltimaActualizacion;
    bool       _actualizando;

    IMuestreadorAlturaPrecisa _muestreador;

    // ── Arranque ──────────────────────────────────────────────────────────
    void Start() => StartCoroutine(InicializarAsync());

    IEnumerator InicializarAsync()
    {
        // Esperar a que MosaicoV3Sistema haya instanciado los renderers
        float deadline = Time.realtimeSinceStartup + 20f;
        GameObject terrenoRoot = null;
        while (terrenoRoot == null && Time.realtimeSinceStartup < deadline)
        {
            terrenoRoot = GameObject.Find("Terreno_MosaicoV3");
            yield return new WaitForSeconds(0.5f);
        }
        if (terrenoRoot == null) { yield break; }

        // Esperar al muestreador de alturas (Fase 0 del Mosaico V3)
        deadline = Time.realtimeSinceStartup + 30f;
        while ((_muestreador = ServiceLocator.Get<IMuestreadorAlturaPrecisa>()) == null
               || !_muestreador.Listo)
        {
            yield return new WaitForSeconds(0.5f);
            if (Time.realtimeSinceStartup > deadline)
            {
                Debug.LogWarning("[Clipmap] IMuestreadorAlturaPrecisa no disponible — " +
                    "el anillo central no se actualizará dinámicamente.");
                yield break;
            }
        }

        // Encontrar el renderer del anillo 0
        var anillo0 = terrenoRoot.transform.Find("Anillo_0");
        if (anillo0 == null) { yield break; }
        var mf = anillo0.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) { yield break; }

        // Crear copia mutable para no corromper el asset guardado
        _mallaAnillo0 = Object.Instantiate(mf.sharedMesh);
        _mallaAnillo0.name = "Anillo_0_Dynamic";
        mf.mesh = _mallaAnillo0;   // usar copia, no el asset

        _vertsAnillo0 = _mallaAnillo0.vertices;
        // Calcular gridN: para gridN+1 vértices por eje → (gridN+1)² vértices totales
        _gridN = Mathf.RoundToInt(Mathf.Sqrt(_vertsAnillo0.Length)) - 1;

        // Suscribirse al jugador
        AltsasuCore.OnJugadorSpawned += SetJugador;
        if (AltsasuCore.Jugador != null) SetJugador(AltsasuCore.Jugador);

        _centroUltimaActualizacion = new Vector3(GeoDataAlsasua.OX, 0f, GeoDataAlsasua.OZ);
        Debug.Log($"[Clipmap] Anillo 0 dinámico listo ({_vertsAnillo0.Length} vértices, " +
            $"actualización cada {UPDATE_THRESHOLD}m de movimiento).");
    }

    void OnDestroy() => AltsasuCore.OnJugadorSpawned -= SetJugador;
    void SetJugador(Transform t) => _jugador = t;

    // ── Update: detectar movimiento significativo ─────────────────────────
    void Update()
    {
        if (_actualizando || _jugador == null || _muestreador == null) return;
        if (!_muestreador.Listo) return;

        float dx = _jugador.position.x - _centroUltimaActualizacion.x;
        float dz = _jugador.position.z - _centroUltimaActualizacion.z;
        if (dx * dx + dz * dz < UPDATE_THRESHOLD * UPDATE_THRESHOLD) return;

        _centroUltimaActualizacion = _jugador.position;
        StartCoroutine(ActualizarAlturasAsync());
    }

    // ── Actualización presupuestada de alturas ────────────────────────────
    IEnumerator ActualizarAlturasAsync()
    {
        _actualizando = true;
        float t0 = Time.realtimeSinceStartup;
        int gridVerts = _gridN + 1;

        for (int zi = 0; zi <= _gridN; zi++)
        {
            for (int xi = 0; xi <= _gridN; xi++)
            {
                int idx = zi * gridVerts + xi;
                var v = _vertsAnillo0[idx];
                // Las posiciones XZ son en espacio mundo (bake las grabó así)
                float nuevaY = _muestreador.AlturaMundo(v.x, v.z);
                _vertsAnillo0[idx] = new Vector3(v.x, nuevaY, v.z);
            }

            // Ceder frame cada ROWS_PER_YIELD filas
            if (zi % ROWS_PER_YIELD == 0 && (Time.realtimeSinceStartup - t0) * 1000f > MS_BUDGET)
            {
                yield return null;
                t0 = Time.realtimeSinceStartup;
            }
        }

        _mallaAnillo0.SetVertices(_vertsAnillo0);
        _mallaAnillo0.RecalculateBounds();
        // No recalcular normales cada actualización (caro); se actualizan solo al inicio
        _actualizando = false;
    }
}
