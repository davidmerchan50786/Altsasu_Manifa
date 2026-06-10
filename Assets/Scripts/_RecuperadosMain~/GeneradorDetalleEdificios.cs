#if UNITY_EDITOR
// Assets/Scripts/Editor/GeneradorDetalleEdificios.cs
// ═══════════════════════════════════════════════════════════════════════════
//  DETALLES ARQUITECTÓNICOS por edificio:
//    · Bajantes (pluviales) en una esquina aleatoria
//    · Aire acondicionado en fachadas (0-2)
//    · Antena parabólica en tejados (40% probabilidad)
//    · Humo saliendo de chimeneas (5%)
//    · Toldos en ventanas planta baja
// ═══════════════════════════════════════════════════════════════════════════

using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public static class GeneradorDetalleEdificios
{
    static Material _matBajante, _matAC, _matAntena;

    public static void Generar()
    {
        var padre = GameObject.Find("Edificios_OSM_Reales");
        if (padre == null)
        {
            EditorUtility.DisplayDialog("Sin edificios",
                "Genera primero los edificios (Paso 5).", "OK");
            return;
        }

        CrearMateriales();

        var edificios = padre.transform.Cast<Transform>().ToArray();
        int total = edificios.Length;
        int bajantes = 0, acs = 0, antenas = 0, humos = 0, toldos = 0;

        try
        {
            for (int i = 0; i < total; i++)
            {
                if (i % 20 == 0 && EditorUtility.DisplayCancelableProgressBar(
                    "Detalles edificios", $"{i}/{total}", (float)i / total))
                    break;

                var ed = edificios[i].gameObject;
                var rends = ed.GetComponentsInChildren<MeshRenderer>();
                if (rends.Length == 0) continue;

                Bounds b = rends[0].bounds;
                foreach (var r in rends) b.Encapsulate(r.bounds);
                if (b.size.y < 3f) continue; // edificios pequeños sin detalles

                // Bajante de PVC en una esquina (siempre)
                AñadirBajante(ed, b);
                bajantes++;

                // AC en fachada (50%, 1-2 unidades)
                if (Random.value < 0.5f)
                {
                    int n = Random.Range(1, 3);
                    for (int j = 0; j < n; j++)
                    {
                        AñadirAC(ed, b);
                        acs++;
                    }
                }

                // Antena parabólica en tejado (40%)
                if (Random.value < 0.4f)
                {
                    AñadirAntena(ed, b);
                    antenas++;
                }

                // Chimenea con humo (5% — solo edificios bajos del casco viejo)
                if (b.size.y < 12f && Random.value < 0.05f)
                {
                    AñadirChimeneaHumo(ed, b);
                    humos++;
                }

                // Toldo planta baja (20%)
                if (Random.value < 0.2f)
                {
                    AñadirToldo(ed, b);
                    toldos++;
                }
            }
        }
        finally { EditorUtility.ClearProgressBar(); }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        EditorUtility.DisplayDialog("✅ Detalles añadidos",
            $"Edificios procesados: {total}\n\n" +
            $"• Bajantes: {bajantes}\n" +
            $"• Aire acondicionados: {acs}\n" +
            $"• Antenas parabólicas: {antenas}\n" +
            $"• Chimeneas con humo: {humos}\n" +
            $"• Toldos: {toldos}", "OK");
    }

    // ─────────────────────────────────────────────────────────────────────

    static void CrearMateriales()
    {
        var sh = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");

        _matBajante = new Material(sh) { name = "Mat_Bajante" };
        _matBajante.SetColor("_BaseColor", new Color(0.12f, 0.12f, 0.13f));
        _matBajante.SetFloat("_Smoothness", 0.4f);

        _matAC = new Material(sh) { name = "Mat_AC" };
        _matAC.SetColor("_BaseColor", new Color(0.88f, 0.88f, 0.85f));
        _matAC.SetFloat("_Smoothness", 0.35f);

        _matAntena = new Material(sh) { name = "Mat_Antena" };
        _matAntena.SetColor("_BaseColor", new Color(0.85f, 0.82f, 0.75f));
        _matAntena.SetFloat("_Smoothness", 0.25f);
    }

    static void AñadirBajante(GameObject ed, Bounds b)
    {
        // Esquina aleatoria
        int esq = Random.Range(0, 4);
        Vector3 dir = esq switch
        {
            0 => new Vector3( 1, 0,  1),
            1 => new Vector3( 1, 0, -1),
            2 => new Vector3(-1, 0,  1),
            _ => new Vector3(-1, 0, -1),
        };
        Vector3 pos = b.center + new Vector3(dir.x * b.extents.x * 0.95f, 0,
                                              dir.z * b.extents.z * 0.95f);
        pos.y = b.min.y;

        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "Bajante";
        go.transform.SetParent(ed.transform);
        go.transform.position = pos + Vector3.up * (b.size.y * 0.5f);
        go.transform.localScale = new Vector3(0.15f, b.size.y * 0.5f, 0.15f);
        go.GetComponent<Renderer>().sharedMaterial = _matBajante;
        Object.DestroyImmediate(go.GetComponent<Collider>());
    }

    static void AñadirAC(GameObject ed, Bounds b)
    {
        // Fachada aleatoria
        int lado = Random.Range(0, 4);
        Vector3 normal = lado switch
        {
            0 => Vector3.right,
            1 => Vector3.forward,
            2 => Vector3.left,
            _ => Vector3.back,
        };

        // Altura: piso 1 o 2 (no PB)
        int piso = Random.Range(1, Mathf.Max(2, Mathf.FloorToInt(b.size.y / 3f)));
        float y = b.min.y + piso * 3f + 1.2f;
        if (y > b.max.y - 1f) return;

        // Desplazamiento aleatorio a lo largo del lado
        float offset = (Random.value - 0.5f) * b.size.x * 0.6f;
        Vector3 pos = b.center + normal * (b.extents.x + 0.25f)
                     + Vector3.up * (y - b.center.y)
                     + new Vector3(-normal.z, 0, normal.x) * offset;

        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "AC";
        go.transform.SetParent(ed.transform);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.LookRotation(normal, Vector3.up);
        go.transform.localScale = new Vector3(0.8f, 0.5f, 0.35f);
        go.GetComponent<Renderer>().sharedMaterial = _matAC;
        Object.DestroyImmediate(go.GetComponent<Collider>());
    }

    static void AñadirAntena(GameObject ed, Bounds b)
    {
        // Posición sobre el tejado, ligeramente desplazada
        Vector3 pos = new Vector3(
            b.center.x + (Random.value - 0.5f) * b.size.x * 0.5f,
            b.max.y + 0.6f,
            b.center.z + (Random.value - 0.5f) * b.size.z * 0.5f);

        var root = new GameObject("Antena_Parabolica");
        root.transform.SetParent(ed.transform);
        root.transform.position = pos;

        // Mástil
        var mastil = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        mastil.transform.SetParent(root.transform);
        mastil.transform.localPosition = new Vector3(0, 0.4f, 0);
        mastil.transform.localScale    = new Vector3(0.04f, 0.4f, 0.04f);
        mastil.GetComponent<Renderer>().sharedMaterial = _matAntena;
        Object.DestroyImmediate(mastil.GetComponent<Collider>());

        // Plato
        var plato = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        plato.transform.SetParent(root.transform);
        plato.transform.localPosition = new Vector3(0.25f, 0.8f, 0);
        plato.transform.localRotation = Quaternion.Euler(0, 0, -25f);
        plato.transform.localScale    = new Vector3(0.55f, 0.08f, 0.55f);
        plato.GetComponent<Renderer>().sharedMaterial = _matAntena;
        Object.DestroyImmediate(plato.GetComponent<Collider>());
    }

    static void AñadirChimeneaHumo(GameObject ed, Bounds b)
    {
        Vector3 pos = new Vector3(b.center.x, b.max.y + 1.5f, b.center.z);

        var go = new GameObject("HumoChimenea");
        go.transform.SetParent(ed.transform);
        go.transform.position = pos;

        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.startLifetime = 6f;
        main.startSpeed    = 1.5f;
        main.startSize     = 0.8f;
        main.startColor    = new Color(0.7f, 0.7f, 0.7f, 0.3f);
        main.maxParticles  = 80;

        var em = ps.emission;
        em.rateOverTime = 8;

        var sh = ps.shape;
        sh.shapeType  = ParticleSystemShapeType.Cone;
        sh.angle  = 8f;
        sh.radius = 0.15f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] {
                new GradientColorKey(new Color(0.4f, 0.4f, 0.4f), 0f),
                new GradientColorKey(new Color(0.8f, 0.8f, 0.8f), 1f) },
            new[] {
                new GradientAlphaKey(0f,   0f),
                new GradientAlphaKey(0.4f, 0.2f),
                new GradientAlphaKey(0f,   1f) });
        col.color = grad;

        var sz = ps.sizeOverLifetime;
        sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.3f, 1, 2.5f));

        var force = ps.forceOverLifetime;
        force.enabled = true;
        force.y = new ParticleSystem.MinMaxCurve(0.5f, 1.5f);
        force.x = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);

        // Material humo (cutout / soft particle)
        var psr = go.GetComponent<ParticleSystemRenderer>();
        var matHumo = new Material(Shader.Find("HDRP/Unlit"));
        matHumo.SetColor("_UnlitColor", new Color(0.7f, 0.7f, 0.7f, 1f));
        matHumo.SetFloat("_SurfaceType", 1); // transparent
        psr.material = matHumo;
        psr.sortingFudge = 1f;
    }

    static void AñadirToldo(GameObject ed, Bounds b)
    {
        int lado = Random.Range(0, 4);
        Vector3 normal = lado switch
        {
            0 => Vector3.right,
            1 => Vector3.forward,
            2 => Vector3.left,
            _ => Vector3.back,
        };

        float anchoFachada = Mathf.Min(b.size.x, b.size.z);
        Vector3 pos = b.center + normal * (b.extents.x + 0.6f);
        pos.y = b.min.y + 3f;

        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Toldo";
        go.transform.SetParent(ed.transform);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.LookRotation(normal, Vector3.up)
                              * Quaternion.Euler(-15f, 0, 0);
        go.transform.localScale = new Vector3(anchoFachada * 0.6f, 0.05f, 1.5f);

        Color[] colores = {
            new Color(0.75f, 0.20f, 0.15f), // rojo
            new Color(0.20f, 0.55f, 0.30f), // verde
            new Color(0.30f, 0.30f, 0.30f), // gris
            new Color(0.20f, 0.30f, 0.60f), // azul
        };
        var mat = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"));
        mat.SetColor("_BaseColor", colores[Random.Range(0, colores.Length)]);
        mat.SetFloat("_Smoothness", 0.15f);
        go.GetComponent<Renderer>().sharedMaterial = mat;
        Object.DestroyImmediate(go.GetComponent<Collider>());
    }
}
#endif
