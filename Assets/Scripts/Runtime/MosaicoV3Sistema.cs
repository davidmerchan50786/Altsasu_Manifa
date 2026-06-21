// Assets/Scripts/Runtime/MosaicoV3Sistema.cs
// ═══════════════════════════════════════════════════════════════════════════
//  MOSAICO V3 SISTEMA — sustituye el render de los 48 Unity Terrain
//
//  Carga el MosaicoV3SO (generado por 🏔️ Hornear Mosaico V3) y, en Play:
//    1. Instancia 3 MeshRenderer (uno por anillo) con las mallas horneadas
//    2. Oculta los renders de los 48 Terrain (t.drawHeightmap = false) pero
//       CONSERVA sus TerrainColliders → física y NavMesh intactos
//    3. Activa BootstrapMuestreadorAltura si no está ya activo
//
//  Resultado: 3 draw calls de terreno vs 48+ con los Terrain de Unity.
//  Los 48 Terrain siguen haciendo su trabajo de física silenciosamente.
//
//  Si no existe MosaicoV3SO (no se ha horneado), el sistema no hace nada →
//  los Terrain originales siguen visibles (sin regresión).
//
//  Capa RUNTIME → solo usa ServiceLocator y Resources.Load. Sin referencias
//  a Systems/Editor.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using UnityEngine;

[DefaultExecutionOrder(-85)]   // justo antes de StreamerMundoEstatico (-90) y después de CargadorMosaico
public sealed class MosaicoV3Sistema : MonoBehaviour
{
    const string SO_RESOURCES = "MosaicoV3/MosaicoV3SO";

    // ── Auto-arranque ─────────────────────────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var so = Resources.Load<MosaicoV3SO>(SO_RESOURCES);
        if (so == null) return;   // no horneado: sin acción

        var go = new GameObject("MosaicoV3Sistema");
        DontDestroyOnLoad(go);
        go.AddComponent<MosaicoV3Sistema>();
    }

    void Start() => StartCoroutine(InicializarAsync());

    IEnumerator InicializarAsync()
    {
        var so = Resources.Load<MosaicoV3SO>(SO_RESOURCES);
        if (so == null || so.mallasPorAnillo == null)
        {
            Debug.LogWarning("[MosaicoV3] SO no encontrado — usa 🏔️ Hornear Mosaico V3 primero.");
            Destroy(gameObject);
            yield break;
        }

        // Esperar hasta 20s a que los terrains Unity estén instanciados
        float deadline = Time.realtimeSinceStartup + 20f;
        while (Terrain.activeTerrains.Length == 0 && Time.realtimeSinceStartup < deadline)
            yield return new WaitForSeconds(0.5f);

        // ── 1. Instanciar las 3 mallas de terreno ───────────────────────
        var raiz = new GameObject("Terreno_MosaicoV3");
        for (int i = 0; i < so.mallasPorAnillo.Length; i++)
        {
            var malla = so.mallasPorAnillo[i];
            if (malla == null) continue;

            var go = new GameObject($"Anillo_{i}");
            go.transform.SetParent(raiz.transform);

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = malla;

            var mr = go.AddComponent<MeshRenderer>();
            if (so.material == null)
                Debug.LogWarning($"[MosaicoV3] Anillo_{i}: material no asignado. " +
                    "Asigna HDRP/Lit en Assets/MosaicoV3/terreno_mat.mat.");
            mr.sharedMaterial    = so.material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            mr.receiveShadows    = true;
            mr.staticShadowCaster = true;
            go.isStatic = true;

            // ── LOD bias per-anillo: cull distance escalado por radio ────
            // Anillo 0 (urbano) cull tardío — siempre visible cerca del jugador.
            // Anillo 2 (sierras) cull muy tardío — backdrop del horizonte.
            // La fracción de pantalla se toma del SO (ajustable en Inspector).
            float cullRatio = (so.cullScreenRatio != null && i < so.cullScreenRatio.Length)
                ? so.cullScreenRatio[i]
                : (0.003f / (i + 1));   // fallback: escalar por índice de anillo

            var lg = go.AddComponent<LODGroup>();
            lg.SetLODs(new[] { new LOD(cullRatio, new[] { (Renderer)mr }) });
            lg.RecalculateBounds();
            // fadeMode = None → sin cross-fade (el terreno no lo necesita)
            lg.fadeMode = LODFadeMode.None;
        }

        // ── 2. Ocultar renders de Unity Terrain y gestionar colisiones ──
        // drawTreesAndFoliage se deja ON — AlsasuaTreeStreamer lo gestiona.
        int terrenosOcultos = 0;
        foreach (var ter in Terrain.activeTerrains)
        {
            if (ter == null) continue;
            ter.drawHeightmap = false;
            ter.drawInstanced = false;

            // Colisión: preservar TerrainColliders por defecto (físicas + NavMesh).
            // Si en el futuro se añaden MeshColliders a las mallas V3, poner
            // MosaicoV3SO.preservarTerrainColliders = false para evitar doble-colisión.
            if (!so.preservarTerrainColliders)
            {
                var tc = ter.GetComponent<TerrainCollider>();
                if (tc != null) tc.enabled = false;
            }
            terrenosOcultos++;
        }

        // ── 3. Verificar muestreador de altura ──────────────────────────
        // MuestreadorAlturaMosaico vive en Systems (fuera de Runtime): la activación
        // opt-in la gestiona BootstrapMuestreadorAltura en SceneBootstrapper.
        if (ServiceLocator.Get<IMuestreadorAlturaPrecisa>() == null)
            Debug.Log("[MosaicoV3] IMuestreadorAlturaPrecisa no registrado — " +
                "activar BootstrapMuestreadorAltura si se necesita altura bit-exacta.");

        Debug.Log($"[MosaicoV3] ✅ {so.mallasPorAnillo.Length} anillos de terreno GPU activos · " +
            $"{terrenosOcultos} Unity Terrain ocultos (colliders preservados).");
        Destroy(gameObject);   // trabajo hecho, no necesitamos Update
    }
}
