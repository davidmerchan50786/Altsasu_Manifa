// Assets/Scripts/SistemaEdificios.cs
// Sistema de edificios procedurales de Alsasua/Altsasu.
// Genera edificios basados en manzanas OSM reales (GeoDataCalles) y edificios singulares.
// Los edificios se ajustan automáticamente a la altura del terrain.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[AddComponentMenu("Alsasua/Sistema Edificios")]
public sealed class SistemaEdificios : MonoBehaviour
{
    // ═══════════════════════════════════════════════════════════════════════
    //  INSPECTOR
    // ═══════════════════════════════════════════════════════════════════════

    [Header("═══ PREFABS (opcional) ═══")]
    [Tooltip("Prefabs de edificios reales. Si están vacíos se generan proceduralmente.")]
    public GameObject[] prefabsEdificiosResidencial;
    public GameObject[] prefabsEdificiosCascoViejo;
    public GameObject[] prefabsEdificiosEspeciales;

    [Header("═══ CONFIGURACIÓN ═══")]
    [Tooltip("Si es true, usa el layout real de manzanas y edificios de GeoDataCalles.")]
    public bool usarLayoutReal = true;
    [Tooltip("Distancia de exclusión alrededor de la zona de manifestación.")]
    public float radioExclusion = 150f;
    public Vector3 centroManifestacion = new Vector3(1918f, 0f, 8570f);

    [Header("═══ ZONA POLICIAL ═══")]
    public Vector3 centroAcordonamiento = new Vector3(1918f, 0f, 8570f);
    public float   radioAcordonamiento  = 200f;

    [Header("═══ CALIDAD ═══")]
    [Range(1, 8)] public int maxPlantasFallback = 5;
    public bool generarColisiones = true;

    // ═══════════════════════════════════════════════════════════════════════
    //  ESTADO
    // ═══════════════════════════════════════════════════════════════════════

    int _totalEdificios;

    // Materiales compartidos (pool)
    Material _matResid, _matCasco, _matComercial, _matIndustrial, _matPublico, _matReligioso;

    // ═══════════════════════════════════════════════════════════════════════
    //  LIFECYCLE
    // ═══════════════════════════════════════════════════════════════════════

    void Start()
    {
        CrearMateriales();
        StartCoroutine(GenerarEdificios());
    }

    IEnumerator GenerarEdificios()
    {
        // Esperar a que el terrain esté listo
        yield return new WaitForSeconds(1.5f);

        AlsasuaLogger.Info("SistemaEdificios", "Generando edificios de Alsasua...");

        if (usarLayoutReal && GeoDataCalles.ManzanasAlsasua != null)
        {
            var padre = new GameObject("Edificios_Manzanas");
            padre.transform.SetParent(transform);

            foreach (var m in GeoDataCalles.ManzanasAlsasua)
            {
                // Excluir zona de manifestación
                if (Vector3.Distance(new Vector3(m.Centro.x, 0, m.Centro.z),
                    new Vector3(centroManifestacion.x, 0, centroManifestacion.z)) < radioExclusion)
                    continue;

                SpawnearManzanaReal(m, padre.transform);
                yield return null; // un frame por manzana para no bloquear
            }

            AlsasuaLogger.Info("SistemaEdificios",
                $"✓ {_totalEdificios} edificios colocados en {GeoDataCalles.ManzanasAlsasua.Length} manzanas reales de Alsasua (GeoDataCalles).");
        }

        yield return null;

        SpawnearEdificiosSingulares();

        AlsasuaLogger.Info("SistemaEdificios",
            $"✓ {GeoDataCalles.EdificiosSingulares.Length} edificios singulares colocados " +
            "(iglesia, ayuntamiento, GC, PF, estación, polideportivo...).");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  MANZANAS REALES
    // ═══════════════════════════════════════════════════════════════════════

    private void SpawnearManzanaReal(GeoDataCalles.ManzanaData m, Transform padre)
    {
        float margenCalle = 0.5f;
        float fachadaMin  = 7f;
        float fachadaMax  = 14f;
        float alturaEdificio = m.NumPlantas * 3.2f;

        Quaternion rotManzana = Quaternion.Euler(0f, m.RotacionY, 0f);

        var lados = new (Vector3 inicio, Vector3 fin, float rotFachada, float profundidad)[]
        {
            (new Vector3(-m.TamanoX*0.5f + margenCalle, 0f,  m.TamanoZ*0.5f - margenCalle),
             new Vector3( m.TamanoX*0.5f - margenCalle, 0f,  m.TamanoZ*0.5f - margenCalle), 0f, m.TamanoZ*0.25f),

            (new Vector3( m.TamanoX*0.5f - margenCalle, 0f, -m.TamanoZ*0.5f + margenCalle),
             new Vector3(-m.TamanoX*0.5f + margenCalle, 0f, -m.TamanoZ*0.5f + margenCalle), 180f, m.TamanoZ*0.25f),

            (new Vector3( m.TamanoX*0.5f - margenCalle, 0f,  m.TamanoZ*0.5f - margenCalle),
             new Vector3( m.TamanoX*0.5f - margenCalle, 0f, -m.TamanoZ*0.5f + margenCalle), 90f, m.TamanoX*0.25f),

            (new Vector3(-m.TamanoX*0.5f + margenCalle, 0f, -m.TamanoZ*0.5f + margenCalle),
             new Vector3(-m.TamanoX*0.5f + margenCalle, 0f,  m.TamanoZ*0.5f - margenCalle), 270f, m.TamanoX*0.25f),
        };

        foreach (var (inicio, fin, rotFachada, profEdf) in lados)
        {
            float longitud   = Vector3.Distance(inicio, fin);
            float fachadaObj = Random.Range(fachadaMin, fachadaMax);
            int   numParc    = Mathf.Max(1, Mathf.RoundToInt(longitud / fachadaObj));
            float fachadaReal = longitud / numParc;

            for (int p = 0; p < numParc; p++)
            {
                float t = (p + 0.5f) / numParc;
                Vector3 posLocal = Vector3.Lerp(inicio, fin, t);
                Vector3 posWorld = m.Centro + rotManzana * posLocal;

                float rotWorld = m.RotacionY + rotFachada;
                posWorld.x += Random.Range(-0.5f, 0.5f);
                posWorld.z += Random.Range(-0.5f, 0.5f);

                // ── Ajustar Y al terreno real ──────────────────────────────
                posWorld.y = AltsasuCore.AlturaEn(posWorld.x, posWorld.z);

                bool esCascoViejo = (m.Tipo == GeoDataCalles.TipoEdificio.CascoAntiguo);
                bool esIndustrial = (m.Tipo == GeoDataCalles.TipoEdificio.Industrial);

                if (esIndustrial)
                    CrearNaveIndustrial(posWorld, rotWorld, fachadaReal, profEdf, alturaEdificio * 0.6f, padre);
                else
                    SpawnearEdificioFachada(posWorld, rotWorld, fachadaReal, profEdf, alturaEdificio, esCascoViejo, padre);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  EDIFICIO PROCEDURAL (fachada)
    // ═══════════════════════════════════════════════════════════════════════

    void SpawnearEdificioFachada(Vector3 pos, float rotY, float ancho, float prof,
                                  float altura, bool cascoViejo, Transform padre)
    {
        // Intentar usar prefab
        var prefabs = cascoViejo ? prefabsEdificiosCascoViejo : prefabsEdificiosResidencial;
        if (prefabs != null && prefabs.Length > 0)
        {
            var prefab = prefabs[Random.Range(0, prefabs.Length)];
            if (prefab != null)
            {
                var go = Instantiate(prefab, pos, Quaternion.Euler(0, rotY, 0), padre);
                NormalizarEscalaEdificio(go, ancho);
                _totalEdificios++;
                return;
            }
        }

        // Fallback procedural
        var root = new GameObject($"Edificio_{_totalEdificios}");
        root.transform.SetParent(padre);
        root.transform.position = pos;
        root.transform.rotation = Quaternion.Euler(0f, rotY, 0f);

        // Cuerpo principal
        var cuerpo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cuerpo.transform.SetParent(root.transform);
        cuerpo.transform.localPosition = new Vector3(0, altura * 0.5f, 0);
        cuerpo.transform.localScale    = new Vector3(ancho, altura, prof);
        cuerpo.name = "Cuerpo";

        var mat = cascoViejo ? _matCasco : _matResid;
        AsignarMaterial(cuerpo.GetComponent<Renderer>(), mat);

        if (!generarColisiones)
            Object.Destroy(cuerpo.GetComponent<Collider>());

        // Tejado simple
        var tejado = GameObject.CreatePrimitive(PrimitiveType.Cube);
        tejado.transform.SetParent(root.transform);
        tejado.transform.localPosition = new Vector3(0, altura + 0.2f, 0);
        tejado.transform.localScale    = new Vector3(ancho + 0.2f, 0.4f, prof + 0.2f);
        tejado.name = "Tejado";
        AsignarMaterial(tejado.GetComponent<Renderer>(), _matCasco);
        Object.Destroy(tejado.GetComponent<Collider>());

        _totalEdificios++;
    }

    void CrearNaveIndustrial(Vector3 pos, float rotY, float ancho, float prof,
                              float altura, Transform padre)
    {
        var root = new GameObject($"Nave_{_totalEdificios}");
        root.transform.SetParent(padre);
        root.transform.position = pos;
        root.transform.rotation = Quaternion.Euler(0f, rotY, 0f);

        var nave = GameObject.CreatePrimitive(PrimitiveType.Cube);
        nave.transform.SetParent(root.transform);
        nave.transform.localPosition = new Vector3(0, altura * 0.5f, 0);
        nave.transform.localScale    = new Vector3(ancho, altura, prof);
        AsignarMaterial(nave.GetComponent<Renderer>(), _matIndustrial);

        if (!generarColisiones) Object.Destroy(nave.GetComponent<Collider>());

        _totalEdificios++;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  EDIFICIOS SINGULARES
    // ═══════════════════════════════════════════════════════════════════════

    private void SpawnearEdificiosSingulares()
    {
        var padre = new GameObject("Edificios_Singulares");
        padre.transform.SetParent(transform);

        foreach (var e in GeoDataCalles.EdificiosSingulares)
        {
            // Intentar prefab asignado
            var prefab = e.Prefab;
            if (prefab == null && prefabsEdificiosEspeciales != null && prefabsEdificiosEspeciales.Length > 0)
                prefab = prefabsEdificiosEspeciales[Random.Range(0, prefabsEdificiosEspeciales.Length)];

            if (prefab != null)
            {
                var go = Instantiate(prefab, e.Centro, Quaternion.identity);
                go.name = e.Nombre;
                go.transform.SetParent(padre.transform);
                NormalizarEscalaEdificio(go, e.TamanoX);
            }
            else
            {
                CrearEdificioSingularProcedural(e, padre.transform);
            }
        }
    }

    private void CrearEdificioSingularProcedural(GeoDataCalles.EdificioSingular e, Transform padre)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = e.Nombre;
        go.transform.SetParent(padre);

        // ── Ajustar Y al terreno real ──────────────────────────────────────
        float yBase = AltsasuCore.AlturaEn(e.Centro.x, e.Centro.z);
        go.transform.position   = new Vector3(e.Centro.x, yBase + e.Altura * 0.5f, e.Centro.z);
        go.transform.localScale = new Vector3(e.TamanoX, e.Altura, e.TamanoZ);

        Material mat;
        switch (e.Tipo)
        {
            case GeoDataCalles.TipoEdificio.Institucional: mat = _matPublico;     break;
            case GeoDataCalles.TipoEdificio.Deportivo:     mat = _matComercial;   break;
            case GeoDataCalles.TipoEdificio.Industrial:    mat = _matIndustrial;  break;
            case GeoDataCalles.TipoEdificio.Religioso:     mat = _matReligioso;   break;
            default:                                        mat = _matPublico;     break;
        }
        AsignarMaterial(go.GetComponent<Renderer>(), mat);
        Object.Destroy(go.GetComponent<Collider>());

        // Campanario para iglesias
        if (e.Nombre.ToLower().Contains("iglesia") || e.Nombre.ToLower().Contains("eliza"))
        {
            var torre = GameObject.CreatePrimitive(PrimitiveType.Cube);
            torre.name = "Campanario";
            torre.transform.SetParent(go.transform);
            torre.transform.localPosition = new Vector3(-0.35f, 0.6f, 0f);
            torre.transform.localScale    = new Vector3(0.2f, 1.5f, 0.2f);
            AsignarMaterial(torre.GetComponent<Renderer>(), _matReligioso);
            Object.Destroy(torre.GetComponent<Collider>());
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  MATERIALES
    // ═══════════════════════════════════════════════════════════════════════

    void CrearMateriales()
    {
        var shader = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");

        _matResid     = CrearMat(shader, new Color(0.85f, 0.78f, 0.68f), "Mat_Residencial");
        _matCasco     = CrearMat(shader, new Color(0.80f, 0.70f, 0.55f), "Mat_CascoViejo");
        _matComercial = CrearMat(shader, new Color(0.70f, 0.78f, 0.85f), "Mat_Comercial");
        _matIndustrial= CrearMat(shader, new Color(0.62f, 0.64f, 0.66f), "Mat_Industrial");
        _matPublico   = CrearMat(shader, new Color(0.78f, 0.76f, 0.65f), "Mat_Publico");
        _matReligioso = CrearMat(shader, new Color(0.88f, 0.84f, 0.72f), "Mat_Religioso");
    }

    Material CrearMat(Shader shader, Color color, string nombre)
    {
        var mat = new Material(shader) { name = nombre };
        mat.SetColor("_BaseColor", color);
        mat.SetColor("_Color",     color);
        mat.SetFloat("_Smoothness", 0.1f);
        return mat;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  UTILIDADES
    // ═══════════════════════════════════════════════════════════════════════

    void NormalizarEscalaEdificio(GameObject go, float tamanoObjetivo)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        Bounds b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);

        float escalaActual = b.size.x;
        if (escalaActual > 0.01f)
        {
            float factor = tamanoObjetivo / escalaActual;
            go.transform.localScale *= factor;
        }
    }

    void AsignarMaterial(Renderer r, Material mat)
    {
        if (r == null || mat == null) return;
        r.sharedMaterial = mat;
    }

    void AsignarMaterial(Renderer r, Color color)
    {
        if (r == null) return;
        var shader = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
        var mat = new Material(shader);
        mat.SetColor("_BaseColor", color);
        mat.SetColor("_Color", color);
        r.sharedMaterial = mat;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  GIZMOS
    // ═══════════════════════════════════════════════════════════════════════

    void OnDrawGizmosSelected()
    {
        if (GeoDataCalles.ManzanasAlsasua == null) return;

        foreach (var m in GeoDataCalles.ManzanasAlsasua)
        {
            switch (m.Tipo)
            {
                case GeoDataCalles.TipoEdificio.CascoAntiguo:  Gizmos.color = new Color(0.85f, 0.70f, 0.30f, 0.45f); break;
                case GeoDataCalles.TipoEdificio.Residencial:   Gizmos.color = new Color(0.40f, 0.70f, 0.90f, 0.40f); break;
                case GeoDataCalles.TipoEdificio.Comercial:     Gizmos.color = new Color(0.90f, 0.40f, 0.60f, 0.40f); break;
                case GeoDataCalles.TipoEdificio.Industrial:    Gizmos.color = new Color(0.60f, 0.60f, 0.65f, 0.40f); break;
                case GeoDataCalles.TipoEdificio.Institucional: Gizmos.color = new Color(1.00f, 0.90f, 0.20f, 0.50f); break;
                default: Gizmos.color = new Color(0.60f, 0.80f, 0.60f, 0.35f); break;
            }

            var rot  = Quaternion.Euler(0f, m.RotacionY, 0f);
            float h  = m.NumPlantas * 3.2f;

            DrawGizmoBox(m.Centro + Vector3.up * h * 0.5f,
                         new Vector3(m.TamanoX, h, m.TamanoZ), rot);
        }

        // Zona acordonamiento
        Gizmos.color = new Color(0.2f, 0.4f, 1f, 0.45f);
        DrawCircleGizmo(centroAcordonamiento, radioAcordonamiento, 24);

        // Calles principales
        Gizmos.color = new Color(0.3f, 0.9f, 0.4f, 0.50f);
        foreach (var c in GeoDataCalles.CallesPrincipales)
        {
            for (int i = 0; i < c.Puntos.Length - 1; i++)
                Gizmos.DrawLine(c.Puntos[i], c.Puntos[i+1]);
        }
    }

    void DrawGizmoBox(Vector3 centro, Vector3 size, Quaternion rot)
    {
        var prev = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(centro, rot, size);
        Gizmos.DrawCube(Vector3.zero, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
        Gizmos.matrix = prev;
    }

    private static void DrawCircleGizmo(Vector3 c, float r, int seg)
    {
        for (int i = 0; i < seg; i++)
        {
            float a1 = (float)i       / seg * Mathf.PI * 2f;
            float a2 = (float)(i + 1) / seg * Mathf.PI * 2f;
            Gizmos.DrawLine(
                c + new Vector3(Mathf.Cos(a1) * r, 0, Mathf.Sin(a1) * r),
                c + new Vector3(Mathf.Cos(a2) * r, 0, Mathf.Sin(a2) * r));
        }
    }
}
