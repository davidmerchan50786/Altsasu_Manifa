// Assets/Scripts/SistemaFarolas.cs
// ═══════════════════════════════════════════════════════════════════════════
//  Colocador de farolas urbanas usando el asset SpaceZeta_StreetLamps2.
//
//  Distribuye farolas a lo largo de las principales vías de Alsasua:
//    · Calle principal (eje Z)
//    · Carretera N-1 (eje X)
//
//  Usa los prefabs StreetLampRound1A / StreetLampRound2A del paquete
//  SpaceZeta_StreetLamps2 (incluido en el proyecto).
//
//  Si el prefab no está disponible, crea farolas procedurales simples
//  (cilindro + esfera emisiva) para mantener la coherencia visual.
//
//  SistemaAssets inyecta el prefab vía AsignarPrefab() antes de Start().
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using System.Collections.Generic;

[AddComponentMenu("Alsasua/Sistema Farolas")]
public sealed class SistemaFarolas : MonoBehaviour
{
    // ───────────────────────────────────────────────────────────────────────
    //  INSPECTOR
    // ───────────────────────────────────────────────────────────────────────

    [Header("═══ PREFAB (asignado por SistemaAssets) ═══")]
    [Tooltip("Prefab farola urbana (SpaceZeta_StreetLamps2/Prefabs/StreetLampRound1A.prefab). " +
             "Null → farola procedural.")]
    [SerializeField] private GameObject prefabFarola;

    [Header("═══ CONFIGURACIÓN ═══")]
    [Tooltip("Separación entre farolas (metros).")]
    [Range(8f, 30f)]
    [SerializeField] private float separacion = 15f;

    [Tooltip("Longitud de cada tramo de calle (metros). " +
             "Se colocarán Math.Floor(longitud / separacion) farolas por tramo.")]
    [Range(50f, 500f)]
    [SerializeField] private float longitudTramo = 250f;

    [Tooltip("Altura de la farola si se crea proceduralmente (m).")]
    [Range(3f, 8f)]
    [SerializeField] private float alturaFarolaProcedural = 5f;

    [Tooltip("Offset lateral de las farolas respecto al eje de la calle (m). " +
             "Las farolas se colocan a ambos lados.")]
    [Range(2f, 8f)]
    [SerializeField] private float offsetLateral = 4f;

    [Tooltip("Encender las luces nocturnas al inicio (solo si el prefab tiene Light integrada).")]
    [SerializeField] private bool lucesNocturnas = true;

    // ───────────────────────────────────────────────────────────────────────
    //  ESTADO INTERNO
    // ───────────────────────────────────────────────────────────────────────
    private readonly List<Material> _matsCreados = new List<Material>();
    private int _totalFarolas = 0;

    // ───────────────────────────────────────────────────────────────────────
    //  UNITY
    // ───────────────────────────────────────────────────────────────────────
    private void Start()
    {
        ColocarFarolas();
    }

    private void OnDestroy()
    {
        foreach (var m in _matsCreados)
            if (m != null) Object.Destroy(m);
        _matsCreados.Clear();
    }

    // ───────────────────────────────────────────────────────────────────────
    //  API PÚBLICA
    // ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Inyectado por SistemaAssets.PropagarAssets() con el prefab cargado.
    /// </summary>
    public void AsignarPrefab(GameObject prefab)
    {
        prefabFarola = prefab;
        AlsasuaLogger.Info("SistemaFarolas", $"Prefab farola asignado: {prefab?.name ?? "null"}");
    }

    // ───────────────────────────────────────────────────────────────────────
    //  COLOCACIÓN
    // ───────────────────────────────────────────────────────────────────────
    private void ColocarFarolas()
    {
        // ── Calle principal de Alsasua: eje Z (Norte-Sur) ──
        // Tramo desde -125m hasta +125m con centro en (0,0,0)
        ColocarTramo(
            inicio:    new Vector3(0f, 0f, -longitudTramo * 0.5f),
            fin:       new Vector3(0f, 0f,  longitudTramo * 0.5f),
            ejePerp:   Vector3.right,
            nombreTramo: "CallePrincipal");

        // ── Carretera N-1: eje X (Este-Oeste) ──
        ColocarTramo(
            inicio:    new Vector3(-longitudTramo * 0.5f, 0f, 20f),
            fin:       new Vector3( longitudTramo * 0.5f, 0f, 20f),
            ejePerp:   Vector3.forward,
            nombreTramo: "CarreteraN1");

        // ── Calle secundaria zona manifestación ──
        ColocarTramo(
            inicio:    new Vector3(-80f,  0f, -60f),
            fin:       new Vector3(-80f,  0f,  60f),
            ejePerp:   Vector3.right,
            nombreTramo: "CalleManifestacion");

        AlsasuaLogger.Info("SistemaFarolas",
            $"✓ {_totalFarolas} farolas colocadas. " +
            $"Asset: {(prefabFarola != null ? prefabFarola.name : "procedural")}.");
    }

    private void ColocarTramo(Vector3 inicio, Vector3 fin, Vector3 ejePerp, string nombreTramo)
    {
        float longitud  = Vector3.Distance(inicio, fin);
        int   cantidad  = Mathf.Max(2, Mathf.FloorToInt(longitud / separacion));
        var   direction = (fin - inicio).normalized;

        var padre = new GameObject($"Farolas_{nombreTramo}");
        padre.transform.SetParent(transform);

        for (int i = 0; i <= cantidad; i++)
        {
            float t     = (float)i / cantidad;
            var   base_ = Vector3.Lerp(inicio, fin, t);

            // Lado derecho
            SpawnFarola(base_ + ejePerp * offsetLateral, direction, $"Farola_{nombreTramo}_R{i}", padre.transform);
            // Lado izquierdo
            SpawnFarola(base_ - ejePerp * offsetLateral, direction, $"Farola_{nombreTramo}_L{i}", padre.transform);
        }
    }

    private void SpawnFarola(Vector3 posicion, Vector3 direccionCalle, string nombre, Transform padre)
    {
        if (prefabFarola != null)
        {
            // Instanciar el prefab SpaceZeta_StreetLamps2
            var go = Instantiate(prefabFarola, posicion, Quaternion.LookRotation(direccionCalle));
            go.name = nombre;
            go.transform.SetParent(padre);

            // Activar/desactivar luces del prefab según lucesNocturnas
            if (!lucesNocturnas)
            {
                foreach (var luz in go.GetComponentsInChildren<Light>())
                    luz.enabled = false;
            }
        }
        else
        {
            // Fallback procedural: poste + bombilla
            CrearFarolaProcedural(posicion, nombre, padre);
        }

        _totalFarolas++;
    }

    // ───────────────────────────────────────────────────────────────────────
    //  FALLBACK PROCEDURAL
    // ───────────────────────────────────────────────────────────────────────
    private void CrearFarolaProcedural(Vector3 posicion, string nombre, Transform padre)
    {
        var raiz = new GameObject(nombre);
        raiz.transform.position = posicion;
        raiz.transform.SetParent(padre);

        // Poste
        var poste = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        poste.name = "Poste";
        poste.transform.SetParent(raiz.transform);
        poste.transform.localPosition = new Vector3(0f, alturaFarolaProcedural * 0.5f, 0f);
        poste.transform.localScale    = new Vector3(0.1f, alturaFarolaProcedural * 0.5f, 0.1f);
        AsignarMaterialGris(poste.GetComponent<Renderer>(), new Color(0.35f, 0.35f, 0.35f));
        Object.Destroy(poste.GetComponent<Collider>()); // sin colisión

        // Brazo horizontal
        var brazo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        brazo.name = "Brazo";
        brazo.transform.SetParent(raiz.transform);
        brazo.transform.localPosition = new Vector3(0.6f, alturaFarolaProcedural, 0f);
        brazo.transform.localScale    = new Vector3(1.2f, 0.06f, 0.06f);
        AsignarMaterialGris(brazo.GetComponent<Renderer>(), new Color(0.3f, 0.3f, 0.3f));
        Object.Destroy(brazo.GetComponent<Collider>());

        // Bombilla (esfera emisiva)
        var bombilla = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        bombilla.name = "Bombilla";
        bombilla.transform.SetParent(raiz.transform);
        bombilla.transform.localPosition = new Vector3(1.1f, alturaFarolaProcedural - 0.2f, 0f);
        bombilla.transform.localScale    = Vector3.one * 0.25f;
        AsignarMaterialEmisivo(bombilla.GetComponent<Renderer>(), new Color(1f, 0.95f, 0.7f));
        Object.Destroy(bombilla.GetComponent<Collider>());

        // Luz puntual
        if (lucesNocturnas)
        {
            var luzGO   = new GameObject("Luz");
            luzGO.transform.SetParent(raiz.transform);
            luzGO.transform.localPosition = new Vector3(1.1f, alturaFarolaProcedural - 0.25f, 0f);
            var luz     = luzGO.AddComponent<Light>();
            luz.type    = LightType.Point;
            luz.color   = new Color(1f, 0.95f, 0.75f);
            luz.intensity = 1.5f;
            luz.range   = 12f;
            luz.shadows = LightShadows.None; // sin sombras → rendimiento OK con muchas farolas
        }
    }

    private void AsignarMaterialGris(Renderer r, Color color)
    {
        var shader = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Standard");
        if (shader == null) return;
        var mat = new Material(shader) { color = color };
        r.sharedMaterial = mat;
        _matsCreados.Add(mat);
    }

    private void AsignarMaterialEmisivo(Renderer r, Color colorEmisivo)
    {
        var shader = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Standard");
        if (shader == null) return;
        var mat = new Material(shader) { color = colorEmisivo };
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", colorEmisivo * 2f);
        r.sharedMaterial = mat;
        _matsCreados.Add(mat);
    }

    // ───────────────────────────────────────────────────────────────────────
    //  GIZMOS
    // ───────────────────────────────────────────────────────────────────────
#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 1f, 0.3f, 0.5f);

        // Ejes de las calles donde se colocan farolas
        Gizmos.DrawLine(new Vector3(0f, 1f, -longitudTramo * 0.5f),
                        new Vector3(0f, 1f,  longitudTramo * 0.5f));
        Gizmos.DrawLine(new Vector3(-longitudTramo * 0.5f, 1f, 20f),
                        new Vector3( longitudTramo * 0.5f, 1f, 20f));
    }
#endif
}
