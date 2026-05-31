#if UNITY_EDITOR
// Assets/Scripts/Editor/GeneradorDetallesVivos.cs
// ═══════════════════════════════════════════════════════════════════════════
//  DETALLES VIVOS — añade vida y profundidad visual a la escena:
//    · Ventanas emisivas (35% encendidas con colores cálidos variados)
//    · Cables de luz entre farolas (LineRenderer con catenaria física)
//    · Banderas (ikurriña, Navarra, España) con cloth physics
//    · Banderines en plaza (papelillos con ondulación)
//    · Carteles de tiendas (sprites emissivos en planta baja)
// ═══════════════════════════════════════════════════════════════════════════

using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public static class GeneradorDetallesVivos
{
    public static void Generar()
    {
        int ventanas = 0;
        int cables   = 0;
        int banderas = 0;

        try
        {
            EditorUtility.DisplayProgressBar("Detalles vivos", "Ventanas emisivas...", 0.1f);
            ventanas = GenerarVentanasEmisivas();

            EditorUtility.DisplayProgressBar("Detalles vivos", "Cables entre farolas...", 0.5f);
            cables = GenerarCablesElectricos();

            EditorUtility.DisplayProgressBar("Detalles vivos", "Banderas ondeando...", 0.8f);
            banderas = GenerarBanderas();
        }
        finally { EditorUtility.ClearProgressBar(); }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        EditorUtility.DisplayDialog("✅ Detalles vivos",
            $"• Ventanas emisivas: {ventanas}\n" +
            $"• Cables eléctricos: {cables}\n" +
            $"• Banderas: {banderas}", "OK");
    }

    // =========================================================================
    //  VENTANAS EMISIVAS
    // =========================================================================

    static int GenerarVentanasEmisivas()
    {
        var padre = GameObject.Find("Edificios_OSM_Reales");
        if (padre == null) return 0;

        // Colores cálidos típicos de bombillas/cocinas/salones
        var coloresVentana = new[] {
            new Color(1f, 0.85f, 0.55f) * 3f, // bombilla incandescente
            new Color(1f, 0.92f, 0.75f) * 2.5f, // halógena
            new Color(0.85f, 0.95f, 1f) * 2.5f, // LED frío
            new Color(0.4f, 0.6f, 1f)  * 2f,   // tele azulada
        };

        var sh = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
        var materiales = coloresVentana.Select(c => {
            var m = new Material(sh);
            m.SetColor("_BaseColor", new Color(0.05f, 0.05f, 0.05f));
            m.SetColor("_EmissiveColor", c);
            m.EnableKeyword("_EMISSIVE_COLOR_MAP");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            return m;
        }).ToArray();

        int count = 0;
        // Buscar todos los hijos que se llaman "Ventana_..." dentro de los edificios
        var ventanas = padre.GetComponentsInChildren<MeshRenderer>(true)
                            .Where(r => r.gameObject.name.ToLower().Contains("ventana"))
                            .ToArray();

        foreach (var v in ventanas)
        {
            if (Random.value > 0.35f) continue; // 35% encendidas
            v.sharedMaterial = materiales[Random.Range(0, materiales.Length)];
            count++;
        }
        return count;
    }

    // =========================================================================
    //  CABLES ELÉCTRICOS ENTRE FAROLAS (catenaria)
    // =========================================================================

    static int GenerarCablesElectricos()
    {
        var farolas = GameObject.FindGameObjectsWithTag("Untagged")
                                .Where(g => g.name.StartsWith("Farola"))
                                .Select(g => g.transform)
                                .ToArray();

        if (farolas.Length < 2) return 0;

        var padre = GameObject.Find("Cables_Electricos");
        if (padre != null) Object.DestroyImmediate(padre);
        padre = new GameObject("Cables_Electricos");

        var sh  = Shader.Find("HDRP/Lit") ?? Shader.Find("Sprites/Default");
        var mat = new Material(sh) { name = "Mat_Cable" };
        mat.SetColor("_BaseColor", new Color(0.08f, 0.08f, 0.08f));
        mat.SetFloat("_Smoothness", 0.2f);

        int count = 0;
        for (int i = 0; i < farolas.Length; i++)
        {
            var a = farolas[i];
            // Conectar con la farola más cercana NO ya conectada
            Transform mejorB = null;
            float mejorDist = 80f; // máx 80m entre farolas
            for (int j = i + 1; j < farolas.Length; j++)
            {
                float d = Vector3.Distance(a.position, farolas[j].position);
                if (d < mejorDist) { mejorDist = d; mejorB = farolas[j]; }
            }
            if (mejorB == null) continue;

            CrearCableCatenaria(a.position + Vector3.up * 5.5f,
                                mejorB.position + Vector3.up * 5.5f,
                                padre.transform, mat);
            count++;
            if (count > 800) break; // cap por rendimiento
        }
        return count;
    }

    static void CrearCableCatenaria(Vector3 a, Vector3 b, Transform padre, Material mat)
    {
        var go = new GameObject("Cable");
        go.transform.SetParent(padre);

        var lr = go.AddComponent<LineRenderer>();
        lr.material      = mat;
        lr.startWidth    = 0.03f;
        lr.endWidth      = 0.03f;
        lr.useWorldSpace = true;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows    = false;

        // Catenaria: 8 puntos con sag de ~0.6m en el medio
        const int N = 8;
        lr.positionCount = N;
        for (int i = 0; i < N; i++)
        {
            float t = (float)i / (N - 1);
            Vector3 p = Vector3.Lerp(a, b, t);
            float sag = 0.6f * (1f - Mathf.Abs(t - 0.5f) * 2f); // máximo en el medio
            sag *= sag * 4f; // curva tipo coseno hiperbólico aproximada
            p.y -= sag;
            lr.SetPosition(i, p);
        }
    }

    // =========================================================================
    //  BANDERAS — Ikurriña en Ayuntamiento, Navarra en Plaza
    // =========================================================================

    static int GenerarBanderas()
    {
        var t = Terrain.activeTerrain;
        if (t == null) return 0;

        var padre = GameObject.Find("Banderas");
        if (padre != null) Object.DestroyImmediate(padre);
        padre = new GameObject("Banderas");

        // 3 banderas alrededor de Herriko Plaza
        const float CX = 1918f, CZ = 8570f;
        var datos = new (string nombre, Color colorBase, Color colorDetalle, Vector3 pos)[]
        {
            ("Ikurriña",  new Color(0.7f, 0.05f, 0.05f), new Color(0.05f, 0.4f, 0.05f),
             new Vector3(CX + 15, 0, CZ + 5)),
            ("Navarra",   new Color(0.8f, 0.05f, 0.05f), new Color(1f, 0.85f, 0.1f),
             new Vector3(CX - 12, 0, CZ + 8)),
            ("Bandera_Aralar", new Color(0.1f, 0.3f, 0.6f), new Color(0.95f, 0.95f, 0.95f),
             new Vector3(CX, 0, CZ - 15)),
        };

        int count = 0;
        foreach (var d in datos)
        {
            Vector3 pos = d.pos;
            pos.y = t.SampleHeight(pos);
            CrearBandera(d.nombre, d.colorBase, d.colorDetalle, pos, padre.transform);
            count++;
        }
        return count;
    }

    static void CrearBandera(string nombre, Color baseC, Color detail,
                              Vector3 pos, Transform padre)
    {
        var root = new GameObject(nombre);
        root.transform.SetParent(padre);
        root.transform.position = pos;

        // Mástil
        var mastil = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        mastil.name = "Mastil";
        mastil.transform.SetParent(root.transform);
        mastil.transform.localPosition = new Vector3(0, 3f, 0);
        mastil.transform.localScale    = new Vector3(0.08f, 3f, 0.08f);
        var matMastil = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"));
        matMastil.SetColor("_BaseColor", new Color(0.85f, 0.82f, 0.75f));
        matMastil.SetFloat("_Metallic", 0.5f);
        matMastil.SetFloat("_Smoothness", 0.6f);
        mastil.GetComponent<Renderer>().sharedMaterial = matMastil;
        Object.DestroyImmediate(mastil.GetComponent<Collider>());

        // Bola arriba
        var bola = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        bola.transform.SetParent(root.transform);
        bola.transform.localPosition = new Vector3(0, 6.05f, 0);
        bola.transform.localScale    = Vector3.one * 0.15f;
        bola.GetComponent<Renderer>().sharedMaterial = matMastil;
        Object.DestroyImmediate(bola.GetComponent<Collider>());

        // Tela — Plane con Cloth component para ondulación física
        var tela = GameObject.CreatePrimitive(PrimitiveType.Plane);
        tela.name = "Tela";
        tela.transform.SetParent(root.transform);
        tela.transform.localPosition = new Vector3(0.9f, 5.3f, 0);
        tela.transform.localRotation = Quaternion.Euler(0, 0, 90);
        tela.transform.localScale    = new Vector3(0.12f, 1f, 0.18f);

        var rend = tela.GetComponent<Renderer>();
        var matTela = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"));
        matTela.SetColor("_BaseColor", baseC);
        matTela.SetColor("_EmissiveColor", detail * 0.05f); // ligero glow
        rend.sharedMaterial = matTela;
        Object.DestroyImmediate(tela.GetComponent<MeshCollider>());

        // Añadir Cloth — ondulación con viento (físico real)
        var cloth = tela.AddComponent<Cloth>();
        cloth.useGravity         = true;
        cloth.externalAcceleration = new Vector3(2f, 0, 0); // viento constante
        cloth.randomAcceleration = new Vector3(1.5f, 0.5f, 1f);
        cloth.damping            = 0.3f;
    }
}
#endif
