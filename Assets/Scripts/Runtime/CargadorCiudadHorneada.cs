// Assets/Scripts/Runtime/CargadorCiudadHorneada.cs
// ═══════════════════════════════════════════════════════════════════════════
//  CARGADOR CIUDAD HORNEADA — replay en runtime del bake del editor
//
//  POR QUÉ EXISTE: HorneadorCiudad (editor) genera los prefabs de celda y
//  desactiva los originales EN ESCENA. Esos cambios de escena se pierden al
//  salir de Play (Unity no los guarda). Este loader los reproduce en cada
//  Play automáticamente, sin necesitar que el bake haya ocurrido en editor:
//
//    1. AfterSceneLoad: busca ManifestCiudadSO en Resources/CiudadHorneada/
//    2. Si la raíz "CiudadHorneada" ya existe → no hace nada (bake fue en editor,
//       la escena fue guardada post-bake: estado ideal, no tocar).
//    3. Si no: instancia los prefabs de celda (LOD pyramid por celda) y
//       desactiva con SetActive(false) los originales no denylistados.
//
//  Timing: RuntimeInitializeOnLoadMethod(AfterSceneLoad) crea el componente
//  en el mismo frame antes de cualquier Start() → StreamerMundoEstatico y
//  SistemaOptimizacion ven la escena ya en estado horneado cuando escanean.
//
//  Coste: instantiate de 121 prefabs vacíos (refs a mallas ya en memoria)
//  + un FindObjectsByType de renderers = < 5 ms en el primer frame.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.SceneManagement;

// -180 → antes que StreamerMundoEstatico (-90) y MosaicoV3Sistema (-85)
[DefaultExecutionOrder(-180)]
public sealed class CargadorCiudadHorneada : MonoBehaviour
{
    const string SO_RESOURCES = "CiudadHorneada/ManifestCiudadSO";

    // Denylist centralizada en DenylistUtility.cs (fuente única de verdad)

    // ── Bootstrap (AfterSceneLoad) ────────────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var so = Resources.Load<ManifestCiudadSO>(SO_RESOURCES);
        if (so == null || so.prefabs == null || so.prefabs.Length == 0) return;

        var go = new GameObject("CargadorCiudadHorneada_Boot");
        DontDestroyOnLoad(go);
        go.AddComponent<CargadorCiudadHorneada>();   // Awake dispara aquí, síncronamente
    }

    // ── Awake: único punto de entrada de lógica ────────────────────────────
    void Awake()
    {
        var so = Resources.Load<ManifestCiudadSO>(SO_RESOURCES);
        if (so == null || so.prefabs == null || so.prefabs.Length == 0)
        {
            Debug.LogWarning("[CargadorCiudad] ManifestCiudadSO no encontrado en Resources — " +
                "hornea la ciudad con 🏗️ Hornear Ciudad primero.");
            Destroy(gameObject);
            return;
        }

        // Si el bake fue en EDITOR y la escena se guardó → raíz ya en escena, no hacer nada.
        if (GameObject.Find("CiudadHorneada") != null)
        {
            Debug.Log("[CargadorCiudad] CiudadHorneada ya presente en escena (bake editor). " +
                $"~{so.drawCallsAprox} draw calls, sin acción.");
            Destroy(gameObject);
            return;
        }

        // ── 1. Instanciar prefabs horneados ──────────────────────────────
        var raiz = new GameObject("CiudadHorneada");
        int instanciadas = 0;
        foreach (var prefab in so.prefabs)
        {
            if (prefab == null) continue;
            Instantiate(prefab, raiz.transform);
            instanciadas++;
        }

        // ── 2. Desactivar originales — repartido en coroutine para evitar GPU stall ──
        // SetActive(false) masivo en un frame = 10-50ms de stall. Lo repartimos a
        // 50 objetos/frame con un StartCoroutine lanzado desde Awake síncronamente.
        // Awake se llama durante AfterSceneLoad, antes de cualquier Start, por lo que
        // la corrutina empieza en el mismo frame y termina en los siguientes.
        Debug.Log($"[CargadorCiudad] ✅ {instanciadas}/{so.totalCeldas} celdas horneadas · ~{so.drawCallsAprox} draw calls. Desactivando originales…");

        // NOTA: Awake no puede iniciar coroutines directamente — usamos un MonoBehaviour
        // temporal de un solo frame como host de la coroutine de desactivación.
        var mrs = FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        var host = new GameObject("_DesactivadorHost").AddComponent<DesactivadorMasivo>();
        host.Iniciar(mrs, raiz, () =>
        {
            Debug.Log("[CargadorCiudad] Originales desactivados.");
        });

        Destroy(gameObject);   // el host continúa la corrutine; este loader ya cumplió su función
    }

    static bool EnDenylist(Transform t) => DenylistUtility.EnDenylist(t);
}
