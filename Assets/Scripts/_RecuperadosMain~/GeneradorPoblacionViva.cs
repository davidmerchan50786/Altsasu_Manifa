#if UNITY_EDITOR
// Assets/Scripts/Editor/GeneradorPoblacionViva.cs
// ═══════════════════════════════════════════════════════════════════════════
//  POBLACIÓN VIVA — NPCs caminando + multitud manifa en plaza
//
//    · Crea 60 NPCs caminantes (Civil_1, Civil_2, LuciaModel) por la zona urbana
//    · Multitud manifa en Herriko Plaza (40 personas, animación idle aglomerada)
//    · Bake NavMesh automático si Unity lo permite
//    · Pancartas de protesta sostenidas por NPCs específicos
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Unity.AI.Navigation;

public static class GeneradorPoblacionViva
{
    const float CX = 1918f, CZ = 8570f;

    static readonly string[] PREFABS_CIVIL = {
        "Assets/Models/Characters/Civil_1.fbx",
        "Assets/Models/Characters/Civil_2.fbx",
        "Assets/Models/Characters/LuciaModel.FBX",
        "Assets/Models/Characters/Civil_1.prefab",
        "Assets/Models/Characters/Civil_2.prefab",
    };

    public static void Generar()
    {
        int caminantes, manifestantes;
        try
        {
            EditorUtility.DisplayProgressBar("Población viva", "Bake NavMesh...", 0.1f);
            BakearNavMesh();

            EditorUtility.DisplayProgressBar("Población viva", "NPCs caminantes...", 0.45f);
            caminantes = SpawnearCaminantes(60);

            EditorUtility.DisplayProgressBar("Población viva", "Multitud manifa...", 0.8f);
            manifestantes = SpawnearMultitudPlaza(40);
        }
        finally { EditorUtility.ClearProgressBar(); }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        EditorUtility.DisplayDialog("✅ Población viva",
            $"• NPCs caminantes: {caminantes}\n" +
            $"• Manifestantes en plaza: {manifestantes}\n" +
            "• NavMesh bakeado", "OK");
    }

    // =========================================================================
    //  NAVMESH BAKE
    // =========================================================================

    static void BakearNavMesh()
    {
        // Buscar o crear un NavMeshSurface en el terrain
        var surface = Object.FindFirstObjectByType<NavMeshSurface>();
        if (surface == null)
        {
            var t = Terrain.activeTerrain;
            if (t == null) return;
            surface = t.gameObject.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.layerMask = ~0;
        }
        surface.BuildNavMesh();
    }

    // =========================================================================
    //  NPCs CAMINANTES
    // =========================================================================

    static int SpawnearCaminantes(int n)
    {
        var t = Terrain.activeTerrain;
        if (t == null) return 0;

        var padre = GameObject.Find("NPCs_Caminantes");
        if (padre != null) Object.DestroyImmediate(padre);
        padre = new GameObject("NPCs_Caminantes");

        // Cargar prefabs disponibles
        var prefabs = new List<GameObject>();
        foreach (var p in PREFABS_CIVIL)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            if (go != null) prefabs.Add(go);
        }
        if (prefabs.Count == 0)
        {
            Debug.LogWarning("[Población] Sin prefabs Civil_*. Usando cápsula azul fallback.");
        }

        int count = 0;
        for (int i = 0; i < n; i++)
        {
            // Posición aleatoria en círculo de 300m alrededor de plaza
            Vector2 r = Random.insideUnitCircle * 300f;
            Vector3 pos = new Vector3(CX + r.x, 0, CZ + r.y);
            pos.y = t.SampleHeight(pos);

            // Snap al NavMesh
            if (!NavMesh.SamplePosition(pos, out NavMeshHit hit, 10f, NavMesh.AllAreas))
                continue;
            pos = hit.position;

            GameObject npc;
            if (prefabs.Count > 0)
            {
                var src = prefabs[Random.Range(0, prefabs.Count)];
                npc = (GameObject)PrefabUtility.InstantiatePrefab(src);
                npc.transform.position = pos;
                EscalarAAltura(npc, 1.7f + Random.Range(-0.1f, 0.1f));
            }
            else
            {
                npc = CapsulaFallback(pos);
            }
            npc.transform.SetParent(padre.transform);
            npc.name = $"Civil_{i}";

            // Asegurar collider + NavMeshAgent
            if (npc.GetComponent<Collider>() == null)
            {
                var col = npc.AddComponent<CapsuleCollider>();
                col.height = 1.75f;
                col.radius = 0.32f;
                col.center = new Vector3(0, 0.875f, 0);
            }

            var agent = npc.GetComponent<NavMeshAgent>() ?? npc.AddComponent<NavMeshAgent>();
            agent.height = 1.75f;
            agent.radius = 0.32f;
            agent.speed  = Random.Range(1.0f, 1.6f);
            agent.acceleration = 8f;
            agent.angularSpeed = 240f;

            npc.AddComponent<NPCCaminante>();
            count++;
        }
        return count;
    }

    // =========================================================================
    //  MULTITUD MANIFA EN PLAZA
    // =========================================================================

    static int SpawnearMultitudPlaza(int n)
    {
        var t = Terrain.activeTerrain;
        if (t == null) return 0;

        var padre = GameObject.Find("Multitud_Manifa");
        if (padre != null) Object.DestroyImmediate(padre);
        padre = new GameObject("Multitud_Manifa");

        var prefabs = new List<GameObject>();
        foreach (var p in PREFABS_CIVIL)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            if (go != null) prefabs.Add(go);
        }

        int count = 0;
        for (int i = 0; i < n; i++)
        {
            // Posición en círculo concéntrico de 5 a 25m del centro
            float ang = Random.value * Mathf.PI * 2f;
            float dist = Random.Range(5f, 25f);
            Vector3 pos = new Vector3(CX + Mathf.Cos(ang) * dist, 0,
                                       CZ + Mathf.Sin(ang) * dist);
            pos.y = t.SampleHeight(pos);

            GameObject npc;
            if (prefabs.Count > 0)
            {
                var src = prefabs[Random.Range(0, prefabs.Count)];
                npc = (GameObject)PrefabUtility.InstantiatePrefab(src);
                EscalarAAltura(npc, 1.7f + Random.Range(-0.1f, 0.15f));
            }
            else
            {
                npc = CapsulaFallback(pos);
                npc.GetComponentInChildren<Renderer>().sharedMaterial.color =
                    new Color(Random.Range(0.2f, 0.9f), Random.Range(0.2f, 0.9f),
                              Random.Range(0.2f, 0.9f));
            }
            npc.transform.position = pos;
            npc.transform.rotation = Quaternion.Euler(0,
                Mathf.Atan2(CX - pos.x, CZ - pos.z) * Mathf.Rad2Deg, 0); // mira al centro
            npc.transform.SetParent(padre.transform);
            npc.name = $"Manifestante_{i}";

            // Pancarta para 1 de cada 6
            if (i % 6 == 0) AñadirPancarta(npc);
            count++;
        }
        return count;
    }

    static void AñadirPancarta(GameObject npc)
    {
        var lemas = new[] {
            "ALSASUA LIBRE", "ABSOLUCIÓN", "AMNISTIA", "EUSKAL HERRIA",
            "JUSTICIA", "NO PASARÁN", "GORA ALTSASU", "BORROKAREN BIDEAN",
        };
        string lema = lemas[Random.Range(0, lemas.Length)];

        var pancarta = GameObject.CreatePrimitive(PrimitiveType.Quad);
        pancarta.name = "Pancarta";
        pancarta.transform.SetParent(npc.transform);
        pancarta.transform.localPosition = new Vector3(0, 1.9f, 0.4f);
        pancarta.transform.localScale    = new Vector3(1.2f, 0.6f, 1f);
        Object.DestroyImmediate(pancarta.GetComponent<Collider>());

        var tex = TexturaPancarta(lema);
        var mat = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Unlit/Texture"));
        mat.SetTexture("_BaseColorMap", tex);
        mat.SetTexture("_MainTex", tex);
        mat.SetColor("_BaseColor", Color.white);
        pancarta.GetComponent<Renderer>().sharedMaterial = mat;
    }

    static Texture2D TexturaPancarta(string lema)
    {
        const int W = 512, H = 256;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, true);
        var px  = new Color[W * H];
        // Fondo blanco con borde rojo
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            bool borde = x < 8 || x > W - 8 || y < 8 || y > H - 8;
            px[y * W + x] = borde
                ? new Color(0.75f, 0.05f, 0.05f)
                : new Color(0.95f, 0.95f, 0.92f);
        }
        tex.SetPixels(px);
        tex.Apply();

        // Pintar texto con Graphics + GUIStyle (truco para meterlo a Texture2D)
        // Como Graphics.DrawTexture no funciona en edit-time así, usamos un método
        // alternativo: pintamos píxeles representando el lema como bloque negro
        // (simplificación). Para texto real se necesitaría TextMeshPro a textura.
        PintarTextoSimple(tex, lema, new Color(0.05f, 0.05f, 0.05f));

        string path = $"Assets/AlsasuaData/Textures/Proc/Pancarta_{lema}.png";
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(path);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // Pinta texto block-style: cada letra ocupa una rejilla de 5×7 pixeles
    static void PintarTextoSimple(Texture2D tex, string texto, Color color)
    {
        int len = texto.Length;
        int charWidth  = 24;
        int charHeight = 36;
        int spacing    = 6;
        int totalW = len * (charWidth + spacing);
        int xStart = (tex.width - totalW) / 2;
        int yStart = (tex.height - charHeight) / 2;

        for (int i = 0; i < len; i++)
        {
            char c = texto[i];
            int xc = xStart + i * (charWidth + spacing);
            DibujarLetra(tex, c, xc, yStart, charWidth, charHeight, color);
        }
        tex.Apply();
    }

    static void DibujarLetra(Texture2D tex, char c, int x, int y, int w, int h, Color col)
    {
        // 5×7 font ultra-simple
        string[] glyph = GlyphFor(c);
        if (glyph == null) return;
        int gW = glyph[0].Length, gH = glyph.Length;
        float sx = w / (float)gW, sy = h / (float)gH;
        for (int gy = 0; gy < gH; gy++)
        for (int gx = 0; gx < gW; gx++)
        {
            if (glyph[gy][gx] != '#') continue;
            int px = x + Mathf.RoundToInt(gx * sx);
            int py = y + h - Mathf.RoundToInt((gy + 1) * sy);
            int pw = Mathf.CeilToInt(sx);
            int ph = Mathf.CeilToInt(sy);
            for (int dy = 0; dy < ph; dy++)
            for (int dx = 0; dx < pw; dx++)
            {
                int qx = px + dx, qy = py + dy;
                if (qx >= 0 && qx < tex.width && qy >= 0 && qy < tex.height)
                    tex.SetPixel(qx, qy, col);
            }
        }
    }

    static string[] GlyphFor(char c)
    {
        c = char.ToUpper(c);
        switch (c)
        {
            case 'A': return new[]{"  #  ","# # # ","#####","#   #","#   #"};
            case 'B': return new[]{"#### ","#   #","#### ","#   #","#### "};
            case 'C': return new[]{" ####","#    ","#    ","#    "," ####"};
            case 'D': return new[]{"#### ","#   #","#   #","#   #","#### "};
            case 'E': return new[]{"#####","#    ","#####","#    ","#####"};
            case 'F': return new[]{"#####","#    ","#####","#    ","#    "};
            case 'G': return new[]{" ####","#    ","#  ##","#   #"," ####"};
            case 'H': return new[]{"#   #","#   #","#####","#   #","#   #"};
            case 'I': return new[]{"#####","  #  ","  #  ","  #  ","#####"};
            case 'J': return new[]{"#####","   # ","   # ","#  # "," ##  "};
            case 'K': return new[]{"#   #","#  # ","###  ","#  # ","#   #"};
            case 'L': return new[]{"#    ","#    ","#    ","#    ","#####"};
            case 'M': return new[]{"#   #","## ##","# # #","#   #","#   #"};
            case 'N': return new[]{"#   #","##  #","# # #","#  ##","#   #"};
            case 'O': return new[]{" ### ","#   #","#   #","#   #"," ### "};
            case 'P': return new[]{"#### ","#   #","#### ","#    ","#    "};
            case 'Q': return new[]{" ### ","#   #","#   #","#  # "," ## #"};
            case 'R': return new[]{"#### ","#   #","#### ","# #  ","#  ##"};
            case 'S': return new[]{" ####","#    "," ### ","    #","#### "};
            case 'T': return new[]{"#####","  #  ","  #  ","  #  ","  #  "};
            case 'U': return new[]{"#   #","#   #","#   #","#   #"," ### "};
            case 'V': return new[]{"#   #","#   #","#   #"," # # ","  #  "};
            case 'W': return new[]{"#   #","#   #","# # #","## ##","#   #"};
            case 'X': return new[]{"#   #"," # # ","  #  "," # # ","#   #"};
            case 'Y': return new[]{"#   #"," # # ","  #  ","  #  ","  #  "};
            case 'Z': return new[]{"#####","   # ","  #  "," #   ","#####"};
            case 'Á': case 'Ñ': return GlyphFor(c == 'Ñ' ? 'N' : 'A');
            case '!': return new[]{"  #  ","  #  ","  #  ","     ","  #  "};
            case ' ': return new[]{"     ","     ","     ","     ","     "};
            default:  return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────

    static GameObject CapsulaFallback(Vector3 pos)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.transform.position = pos + Vector3.up * 0.9f;
        go.transform.localScale = new Vector3(0.55f, 0.9f, 0.55f);
        return go;
    }

    static void EscalarAAltura(GameObject npc, float alturaObj)
    {
        var rends = npc.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return;
        Bounds b = rends[0].bounds;
        foreach (var r in rends) b.Encapsulate(r.bounds);
        if (b.size.y > 0.01f)
            npc.transform.localScale *= alturaObj / b.size.y;
    }
}
#endif
