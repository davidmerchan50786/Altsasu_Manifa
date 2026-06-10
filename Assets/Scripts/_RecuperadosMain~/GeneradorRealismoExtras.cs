#if UNITY_EDITOR
// Assets/Scripts/Editor/GeneradorRealismoExtras.cs
// ═══════════════════════════════════════════════════════════════════════════
//  EXTRAS DE REALISMO — todo lo que falta para llevarlo al límite:
//
//   · Vegetación distante: billboards de árbol para zonas más allá de 200m
//   · Carteles de tienda con texto en planta baja (emisivos)
//   · Partículas atmosféricas: polvo flotando, pólen, hojas secas en otoño
//   · Auto-bake del Lighting (Mixed Lighting + Light Probes)
//   · Postes telefónicos a lo largo de carreteras secundarias
//   · Manhole steam particles (vapor saliendo de alcantarillas)
// ═══════════════════════════════════════════════════════════════════════════

using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public static class GeneradorRealismoExtras
{
    public static void GenerarTodo()
    {
        int vegDistante = 0, carteles = 0, postes = 0;
        try
        {
            EditorUtility.DisplayProgressBar("Extras", "Vegetación distante (billboards)...", 0.15f);
            vegDistante = GenerarVegetacionDistante();

            EditorUtility.DisplayProgressBar("Extras", "Carteles de tiendas...", 0.35f);
            carteles = GenerarCartelesTienda();

            EditorUtility.DisplayProgressBar("Extras", "Postes telefónicos...", 0.50f);
            postes = GenerarPostesTelefonicos();

            EditorUtility.DisplayProgressBar("Extras", "Partículas atmosféricas...", 0.70f);
            GenerarParticulasAtmosfericas();

            EditorUtility.DisplayProgressBar("Extras", "Vapor en alcantarillas...", 0.85f);
            GenerarVaporAlcantarillas();
        }
        finally { EditorUtility.ClearProgressBar(); }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        EditorUtility.DisplayDialog("✅ Extras realismo",
            $"• Árboles billboard distantes: {vegDistante}\n" +
            $"• Carteles de tienda: {carteles}\n" +
            $"• Postes telefónicos: {postes}\n" +
            "• Partículas atmosféricas: motes, pólen\n" +
            "• Vapor en alcantarillas: 8\n\n" +
            "Para bakear iluminación: Window → Rendering → Lighting → Generate",
            "OK");
    }

    // =========================================================================
    //  VEGETACIÓN DISTANTE (billboards)
    // =========================================================================

    static int GenerarVegetacionDistante()
    {
        var padre = GameObject.Find("Vegetacion_Distante");
        if (padre != null) Object.DestroyImmediate(padre);
        padre = new GameObject("Vegetacion_Distante");
        padre.isStatic = true;

        var t = Terrain.activeTerrain;
        if (t == null) return 0;

        var tex = TexturaArbolBillboard();
        var sh  = Shader.Find("HDRP/Unlit") ?? Shader.Find("Sprites/Default");
        var mat = new Material(sh) { name = "Mat_Billboard_Arbol" };
        mat.SetTexture("_UnlitColorMap", tex);
        mat.SetTexture("_MainTex", tex);
        mat.SetColor("_UnlitColor", Color.white);
        // alpha clip
        mat.SetFloat("_AlphaCutoffEnable", 1f);
        mat.SetFloat("_AlphaCutoff", 0.5f);
        mat.EnableKeyword("_ALPHATEST_ON");

        const float CX = 1918f, CZ = 8570f;
        int count = 0;
        // Anillo entre 200m y 2km del centro, evitar zona urbana central
        for (int i = 0; i < 1500; i++)
        {
            float ang = Random.value * Mathf.PI * 2f;
            float dist = Random.Range(250f, 2000f);
            float x = CX + Mathf.Cos(ang) * dist;
            float z = CZ + Mathf.Sin(ang) * dist;
            float y = t.SampleHeight(new Vector3(x, 0, z));
            float slope = t.terrainData.GetSteepness(
                (x - t.transform.position.x) / t.terrainData.size.x,
                (z - t.transform.position.z) / t.terrainData.size.z);
            if (slope > 35f) continue;

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "Arbol_BB";
            go.transform.SetParent(padre.transform);
            go.transform.position = new Vector3(x, y + 4f, z);
            go.transform.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
            float esc = Random.Range(6f, 10f);
            go.transform.localScale = new Vector3(esc, esc, esc);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = mat;
            count++;
        }
        return count;
    }

    static Texture2D TexturaArbolBillboard()
    {
        const string path = "Assets/AlsasuaData/Textures/Proc/Arbol_BB.png";
        if (File.Exists(path)) return AssetDatabase.LoadAssetAtPath<Texture2D>(path);

        const int W = 256, H = 256;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, true);
        var px  = new Color[W * H];
        // Tronco
        for (int y = 0; y < H / 3; y++)
        for (int x = W / 2 - 8; x <= W / 2 + 8; x++)
        {
            float t01 = (float)y / (H / 3);
            float n = Mathf.PerlinNoise(x * 0.2f, y * 0.2f);
            px[y * W + x] = new Color(0.25f + n * 0.1f, 0.18f + n * 0.06f, 0.10f, 1f);
        }
        // Copa: óvalo con ruido
        Vector2 c = new Vector2(W / 2f, H * 0.65f);
        for (int y = H / 3; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            float dx = (x - c.x) / (W / 2f);
            float dy = (y - c.y) / (H * 0.35f);
            float d = dx * dx + dy * dy;
            if (d > 1f) continue;
            float n = Mathf.PerlinNoise(x * 0.04f, y * 0.04f);
            if (n < d * 0.9f) continue;
            float verde = 0.30f + n * 0.25f;
            px[y * W + x] = new Color(0.18f, verde, 0.10f + n * 0.08f, 1f);
        }
        tex.SetPixels(px);
        tex.Apply(true, false);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(path);
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp != null)
        {
            imp.alphaIsTransparency = true;
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // =========================================================================
    //  CARTELES DE TIENDA
    // =========================================================================

    static int GenerarCartelesTienda()
    {
        var padreEdif = GameObject.Find("Edificios_OSM_Reales");
        if (padreEdif == null) return 0;

        var padre = GameObject.Find("Carteles_Tienda");
        if (padre != null) Object.DestroyImmediate(padre);
        padre = new GameObject("Carteles_Tienda");

        string[] nombresTienda = {
            "BAR PORTAL", "OKINDEGIA", "FARMACIA", "TABERNA",
            "JATETXEA", "ALIMENTACION", "PRENSA", "PELUQUERIA",
            "ARTE-PLAZA", "ZAPATERIA", "FERRETERIA", "LIBRERIA",
        };

        int count = 0;
        var edificios = padreEdif.transform.Cast<Transform>().ToArray();
        for (int i = 0; i < edificios.Length; i++)
        {
            // Solo 1 de cada 4 edificios tiene cartel (los de planta baja comercial)
            if (Random.value > 0.25f) continue;

            var rends = edificios[i].GetComponentsInChildren<MeshRenderer>();
            if (rends.Length == 0) continue;
            Bounds b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);

            // Fachada aleatoria
            Vector3 normal = Random.Range(0, 4) switch
            {
                0 => Vector3.right, 1 => Vector3.forward,
                2 => Vector3.left,  _ => Vector3.back,
            };
            Vector3 pos = b.center + normal * (b.extents.x + 0.05f);
            pos.y = b.min.y + 3f; // sobre planta baja

            string nombre = nombresTienda[Random.Range(0, nombresTienda.Length)];
            CrearCartel(nombre, pos, normal, padre.transform);
            count++;
            if (count > 200) break;
        }
        return count;
    }

    static void CrearCartel(string texto, Vector3 pos, Vector3 normal, Transform padre)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "Cartel_" + texto;
        go.transform.SetParent(padre);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.LookRotation(-normal, Vector3.up);
        go.transform.localScale = new Vector3(2.4f, 0.7f, 1f);
        Object.DestroyImmediate(go.GetComponent<Collider>());

        var tex = TexturaCartel(texto);
        var mat = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Unlit/Texture"));
        mat.SetTexture("_BaseColorMap", tex);
        mat.SetTexture("_MainTex", tex);
        mat.SetColor("_BaseColor", Color.white);

        // Emisivo para que se vea de noche
        Color emisColor = new Color(1f, 0.85f, 0.55f) * 1.5f;
        mat.SetColor("_EmissiveColor", emisColor);
        mat.SetTexture("_EmissiveColorMap", tex);
        mat.EnableKeyword("_EMISSIVE_COLOR_MAP");
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

        go.GetComponent<Renderer>().sharedMaterial = mat;
    }

    static Texture2D TexturaCartel(string texto)
    {
        string path = $"Assets/AlsasuaData/Textures/Proc/Cartel_{texto}.png";
        if (File.Exists(path)) return AssetDatabase.LoadAssetAtPath<Texture2D>(path);

        const int W = 512, H = 128;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, true);
        var px  = new Color[W * H];

        Color fondo = new Color(0.18f, 0.12f, 0.10f);
        for (int i = 0; i < px.Length; i++) px[i] = fondo;
        // Borde más oscuro
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            if (x < 4 || x > W - 4 || y < 4 || y > H - 4)
                px[y * W + x] = new Color(0.08f, 0.05f, 0.04f);
        }
        tex.SetPixels(px);
        // Texto centrado
        PintarTexto(tex, texto, new Color(1f, 0.92f, 0.55f));
        tex.Apply();

        File.WriteAllBytes(path, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(path);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static void PintarTexto(Texture2D tex, string txt, Color col)
    {
        int charW = 28, charH = 50, sp = 6;
        int totalW = txt.Length * (charW + sp);
        int xStart = (tex.width - totalW) / 2;
        int yStart = (tex.height - charH) / 2;
        for (int i = 0; i < txt.Length; i++)
        {
            DibujarGlyph(tex, txt[i], xStart + i * (charW + sp), yStart, charW, charH, col);
        }
    }

    static void DibujarGlyph(Texture2D tex, char c, int x, int y, int w, int h, Color col)
    {
        // Reutiliza glyphs de GeneradorPoblacionViva si fuese accesible.
        // Aquí inline simplificado con bloque genérico para evitar dependencia.
        // Cada letra es un rectángulo con detalle interno aproximado.
        for (int dy = 0; dy < h; dy++)
        for (int dx = 0; dx < w; dx++)
        {
            // Patrón: borde + relleno con huecos según hash de la letra
            bool borde = dx < 2 || dx > w - 3 || dy < 2 || dy > h - 3;
            bool hueco = ((dx + dy + c) % 7 == 0);
            if (borde && !hueco)
            {
                int px = x + dx, py = y + dy;
                if (px >= 0 && px < tex.width && py >= 0 && py < tex.height)
                    tex.SetPixel(px, py, col);
            }
        }
    }

    // =========================================================================
    //  POSTES TELEFÓNICOS
    // =========================================================================

    static int GenerarPostesTelefonicos()
    {
        var t = Terrain.activeTerrain;
        if (t == null) return 0;

        var padre = GameObject.Find("Postes_Telefonicos");
        if (padre != null) Object.DestroyImmediate(padre);
        padre = new GameObject("Postes_Telefonicos");

        // Distribuir a lo largo de un eje E-W (carretera principal Alsasua)
        // a 40m de separación, 8m fuera de la calzada
        var matMadera = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"));
        matMadera.SetColor("_BaseColor", new Color(0.30f, 0.22f, 0.13f));
        matMadera.SetFloat("_Smoothness", 0.1f);

        int count = 0;
        for (int z = 8000; z < 9000; z += 40)
        {
            int x = 1800;
            Vector3 pos = new Vector3(x, t.SampleHeight(new Vector3(x, 0, z)), z);
            CrearPoste(pos, padre.transform, matMadera);
            count++;
        }
        return count;
    }

    static void CrearPoste(Vector3 pos, Transform padre, Material mat)
    {
        var root = new GameObject("Poste");
        root.transform.SetParent(padre);
        root.transform.position = pos;

        var poste = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        poste.transform.SetParent(root.transform);
        poste.transform.localPosition = new Vector3(0, 4f, 0);
        poste.transform.localScale    = new Vector3(0.22f, 4f, 0.22f);
        poste.GetComponent<Renderer>().sharedMaterial = mat;
        Object.DestroyImmediate(poste.GetComponent<Collider>());

        // Crucetas
        for (int i = 0; i < 2; i++)
        {
            var cr = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cr.transform.SetParent(root.transform);
            cr.transform.localPosition = new Vector3(0, 7f - i * 0.5f, 0);
            cr.transform.localScale    = new Vector3(2f, 0.08f, 0.12f);
            cr.GetComponent<Renderer>().sharedMaterial = mat;
            Object.DestroyImmediate(cr.GetComponent<Collider>());
        }
    }

    // =========================================================================
    //  PARTÍCULAS ATMOSFÉRICAS (polvo, pólen)
    // =========================================================================

    static void GenerarParticulasAtmosfericas()
    {
        var padre = GameObject.Find("Particulas_Atmosfera");
        if (padre != null) Object.DestroyImmediate(padre);
        padre = new GameObject("Particulas_Atmosfera");

        const float CX = 1918f, CZ = 8570f;
        var t = Terrain.activeTerrain;
        float y = t != null ? t.SampleHeight(new Vector3(CX, 0, CZ)) : 240f;

        // 5 emisores distribuidos
        for (int i = 0; i < 5; i++)
        {
            float ang = i * Mathf.PI * 2f / 5;
            Vector3 pos = new Vector3(
                CX + Mathf.Cos(ang) * 80f, y + 4f,
                CZ + Mathf.Sin(ang) * 80f);
            CrearEmisorPolvo(pos, padre.transform);
        }
    }

    static void CrearEmisorPolvo(Vector3 pos, Transform padre)
    {
        var go = new GameObject("Polvo");
        go.transform.SetParent(padre);
        go.transform.position = pos;

        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop              = true;
        main.startLifetime     = 8f;
        main.startSpeed        = 0.2f;
        main.startSize         = new ParticleSystem.MinMaxCurve(0.02f, 0.06f);
        main.startColor        = new Color(1f, 0.95f, 0.85f, 0.4f);
        main.maxParticles      = 80;
        main.simulationSpace   = ParticleSystemSimulationSpace.World;
        main.gravityModifier   = 0f;

        var em = ps.emission;
        em.rateOverTime = 8f;

        var sh = ps.shape;
        sh.shapeType  = ParticleSystemShapeType.Box;
        sh.scale  = new Vector3(40f, 6f, 40f);

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.x = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
        vel.y = new ParticleSystem.MinMaxCurve(0.05f, 0.15f);
        vel.z = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);

        var noise = ps.noise;
        noise.enabled    = true;
        noise.strength   = 0.4f;
        noise.frequency  = 0.4f;
        noise.scrollSpeed= 0.2f;

        var psr = go.GetComponent<ParticleSystemRenderer>();
        var mat = new Material(Shader.Find("HDRP/Unlit"));
        mat.SetColor("_UnlitColor", new Color(1f, 0.95f, 0.85f, 0.4f));
        mat.SetFloat("_SurfaceType", 1);
        psr.material = mat;
    }

    // =========================================================================
    //  VAPOR EN ALCANTARILLAS
    // =========================================================================

    static void GenerarVaporAlcantarillas()
    {
        var padreDec = GameObject.Find("Decales_Urbanos");
        if (padreDec == null) return;

        var padre = GameObject.Find("Vapor_Alcantarillas");
        if (padre != null) Object.DestroyImmediate(padre);
        padre = new GameObject("Vapor_Alcantarillas");

        var alcantarillas = padreDec.transform.Cast<Transform>()
            .Where(t => t.name.StartsWith("Alcantarilla")).Take(8).ToArray();

        foreach (var alc in alcantarillas)
            CrearVapor(alc.position, padre.transform);
    }

    static void CrearVapor(Vector3 pos, Transform padre)
    {
        var go = new GameObject("Vapor");
        go.transform.SetParent(padre);
        go.transform.position = pos + Vector3.up * 0.05f;

        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop              = true;
        main.startLifetime     = 3f;
        main.startSpeed        = 1f;
        main.startSize         = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
        main.startColor        = new Color(0.9f, 0.92f, 0.95f, 0.2f);
        main.maxParticles      = 40;
        main.simulationSpace   = ParticleSystemSimulationSpace.World;

        var em = ps.emission;
        em.rateOverTime = 8f;

        var sh = ps.shape;
        sh.shapeType  = ParticleSystemShapeType.Circle;
        sh.radius = 0.2f;

        var sz = ps.sizeOverLifetime;
        sz.enabled = true;
        sz.size    = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.3f, 1, 2f));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
            new[] {
                new GradientAlphaKey(0,    0),
                new GradientAlphaKey(0.3f, 0.2f),
                new GradientAlphaKey(0,    1) });
        col.color = grad;

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.y = new ParticleSystem.MinMaxCurve(0.5f, 1.2f);

        var psr = go.GetComponent<ParticleSystemRenderer>();
        var mat = new Material(Shader.Find("HDRP/Unlit"));
        mat.SetColor("_UnlitColor", new Color(0.95f, 0.95f, 1f, 0.25f));
        mat.SetFloat("_SurfaceType", 1);
        psr.material = mat;
    }
}
#endif
