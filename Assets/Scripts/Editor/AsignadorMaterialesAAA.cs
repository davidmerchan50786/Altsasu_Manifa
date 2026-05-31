#if UNITY_EDITOR
// Assets/Scripts/Editor/AsignadorMaterialesAAA.cs
// ═══════════════════════════════════════════════════════════════════════════
//  ASIGNADOR DE MATERIALES AAA — pone texturas reales a TODO
//
//  Identifica objetos en la escena por nombre y aplica el material adecuado:
//   • Edificios casco antiguo  → piedra
//   • Edificios modernos        → ladrillo / plaster
//   • Iglesia                   → piedra antigua
//   • Nave industrial           → hormigón
//   • Carreteras                → asfalto (mantiene color base)
//   • Aceras                    → hormigón con baldosas
//   • Zonas peatonales/plaza    → adoquines
//
//  MENÚ: Altsasu GTA → Territorio Real → ★ Aplicar Materiales AAA a Todo
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public static class AsignadorMaterialesAAA
{
    const string PROC = "Assets/AlsasuaData/Textures/Proc";
    const string PBR  = "Assets/AlsasuaData/Materials/PBR";

    // Mapeo: nombre interno → archivo M_*.mat en la carpeta PBR (si existe lo usamos en lugar
    // de generar un material desde la textura procedural).
    static readonly System.Collections.Generic.Dictionary<string, string> PBR_MAP =
        new System.Collections.Generic.Dictionary<string, string>
    {
        { "Mat_Piedra",         "M_Piedra_Sillar.mat"      },
        { "Mat_LadrilloRojo",   "M_Bricks_Rojo_Casco.mat"  },
        { "Mat_LadrilloOcre",   "M_Bricks_Moderno.mat"     },
        { "Mat_PlasterBlanco",  "M_Plaster_Blanco.mat"     },
        { "Mat_PlasterCrema",   "M_Plaster_Crema.mat"      },
        { "Mat_Asfalto",        "M_Asfalto_Carretera.mat"  },
        { "Mat_Hormigon",       "M_Hormigon_Acera.mat"     },
        { "Mat_Adoquines",      "M_Adoquin_Plaza.mat"      },
        { "Mat_TejaRoja",       "M_Tejado_Teja_Roja.mat"   },
    };

    // Materiales globales (se crean una vez)
    static Material _matPiedra, _matLadrilloRojo, _matLadrilloOcre, _matPlasterBlanco,
                    _matPlasterCrema, _matAsfalto, _matHormigon, _matAdoquines,
                    _matTejaRoja, _matVentanaCristal;

    // MENÚ-LEGACY: [MenuItem("Altsasu GTA/Territorio Real/★ Aplicar Materiales AAA a Todo", false, 11)]
    public static void AplicarTodo()
    {
        if (!TexturasDisponibles())
        {
            EditorUtility.DisplayDialog("Sin texturas",
                "Primero genera las texturas:\n" +
                "Altsasu GTA → Territorio Real → ★ Generar Texturas Procedurales",
                "OK");
            return;
        }

        try
        {
            EditorUtility.DisplayProgressBar("Materiales AAA", "Creando materiales...", 0.05f);
            CrearMateriales();

            EditorUtility.DisplayProgressBar("Materiales AAA", "Edificios...", 0.30f);
            AplicarAEdificios();

            EditorUtility.DisplayProgressBar("Materiales AAA", "Carreteras y aceras...", 0.55f);
            AplicarACarreteras();

            EditorUtility.DisplayProgressBar("Materiales AAA", "Plaza adoquines...", 0.75f);
            AplicarPlazaAdoquines();

            EditorUtility.DisplayProgressBar("Materiales AAA", "Estación...", 0.9f);
            AplicarAEstacion();
        }
        finally { EditorUtility.ClearProgressBar(); }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();

        EditorUtility.DisplayDialog("✅ Materiales AAA aplicados",
            "Todos los objetos de la escena con texturas reales:\n\n" +
            "• Edificios: piedra/ladrillo/plaster según tipo\n" +
            "• Carreteras: asfalto procedural\n" +
            "• Aceras: hormigón con baldosas\n" +
            "• Plaza: adoquines (Herriko Plaza)\n" +
            "• Estación: piedra arenisca",
            "OK");
    }

    static bool TexturasDisponibles()
    {
        return AssetDatabase.LoadAssetAtPath<Texture2D>($"{PROC}/Adoquines.png") != null;
    }

    // =========================================================================
    //  CREAR MATERIALES
    // =========================================================================

    static void CrearMateriales()
    {
        string dir = "Assets/AlsasuaData/Materiales_AAA";
        if (!AssetDatabase.IsValidFolder(dir))
            AssetDatabase.CreateFolder("Assets/AlsasuaData", "Materiales_AAA");

        bool hayPBR = AssetDatabase.IsValidFolder(PBR);
        if (hayPBR) Debug.Log("[Mat] Detectada carpeta PBR — se usarán materiales hiperrealistas.");

        _matPiedra        = ObtenerMat("Mat_Piedra",       Cargar("Piedra"),    new Vector2(2, 2),
                                       new Color(0.95f, 0.92f, 0.85f), 0.1f, 0f);
        _matLadrilloRojo  = ObtenerMat("Mat_LadrilloRojo", Cargar("Ladrillo"),  new Vector2(2, 1.5f),
                                       new Color(0.85f, 0.55f, 0.42f), 0.12f, 0f);
        _matLadrilloOcre  = ObtenerMat("Mat_LadrilloOcre", Cargar("Ladrillo"),  new Vector2(2, 1.5f),
                                       new Color(0.78f, 0.68f, 0.50f), 0.12f, 0f);
        _matPlasterBlanco = ObtenerMat("Mat_PlasterBlanco",Cargar("Plaster"),   new Vector2(1.5f, 1.5f),
                                       new Color(0.92f, 0.90f, 0.85f), 0.08f, 0f);
        _matPlasterCrema  = ObtenerMat("Mat_PlasterCrema", Cargar("Plaster"),   new Vector2(1.5f, 1.5f),
                                       new Color(0.88f, 0.78f, 0.62f), 0.08f, 0f);
        _matAsfalto       = ObtenerMat("Mat_Asfalto",      Cargar("Asfalto"),   new Vector2(0.25f, 1f),
                                       Color.white, 0.05f, 0f);
        _matHormigon      = ObtenerMat("Mat_Hormigon",     Cargar("Hormigon"),  new Vector2(0.5f, 0.5f),
                                       new Color(0.85f, 0.85f, 0.83f), 0.08f, 0f);
        _matAdoquines     = ObtenerMat("Mat_Adoquines",    Cargar("Adoquines"), new Vector2(1f, 1f),
                                       Color.white, 0.15f, 0f);
        // Normal map de adoquines procedural si existe (solo aplica al fallback)
        if (!EsMaterialPBR(_matAdoquines))
        {
            var adqNormal = Cargar("Adoquines_N");
            if (adqNormal != null) _matAdoquines.SetTexture("_NormalMap", adqNormal);
        }

        _matTejaRoja      = ObtenerMat("Mat_TejaRoja",     null, Vector2.one,
                                       new Color(0.62f, 0.30f, 0.20f), 0.08f, 0f);
        _matVentanaCristal= ObtenerMat("Mat_VentanaCristal", null, Vector2.one,
                                       new Color(0.20f, 0.35f, 0.50f), 0.92f, 0.4f);

        // Guardar SOLO los procedurales (los PBR ya están en su carpeta)
        var generados = new System.Collections.Generic.List<Material>();
        foreach (var m in new[] { _matPiedra, _matLadrilloRojo, _matLadrilloOcre,
                                   _matPlasterBlanco, _matPlasterCrema, _matAsfalto,
                                   _matHormigon, _matAdoquines, _matTejaRoja, _matVentanaCristal })
            if (!EsMaterialPBR(m)) generados.Add(m);

        if (generados.Count > 0) GuardarMat(dir, generados.ToArray());
    }

    // Carga el material PBR si existe en Assets/AlsasuaData/Materials/PBR/.
    // Si no, devuelve null y se construye uno procedural con CrearMat.
    static Material ObtenerMat(string nombreInterno, Texture2D tex, Vector2 scale,
                                Color color, float smooth, float metal)
    {
        if (PBR_MAP.TryGetValue(nombreInterno, out var fichero))
        {
            var pbr = AssetDatabase.LoadAssetAtPath<Material>($"{PBR}/{fichero}");
            if (pbr != null) return pbr;
        }
        return CrearMat(nombreInterno, tex, scale, color, smooth, metal);
    }

    static bool EsMaterialPBR(Material m)
    {
        if (m == null) return false;
        string path = AssetDatabase.GetAssetPath(m);
        return !string.IsNullOrEmpty(path) && path.StartsWith(PBR);
    }

    static Texture2D Cargar(string nombre) =>
        AssetDatabase.LoadAssetAtPath<Texture2D>($"{PROC}/{nombre}.png");

    static Material CrearMat(string nombre, Texture2D tex, Vector2 scale, Color color,
                              float smooth, float metal)
    {
        var sh = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
        var m = new Material(sh) { name = nombre };
        if (tex != null)
        {
            m.SetTexture("_BaseColorMap", tex);
            m.SetTexture("_MainTex",      tex);
            m.mainTextureScale = scale;
        }
        m.SetColor("_BaseColor", color);
        m.SetColor("_Color",     color);
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Metallic",   metal);
        return m;
    }

    static void GuardarMat(string dir, params Material[] mats)
    {
        foreach (var m in mats)
        {
            string path = $"{dir}/{m.name}.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null)
                AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(m, path);
        }
        AssetDatabase.SaveAssets();
    }

    // =========================================================================
    //  EDIFICIOS — asigna material según nombre/tipo
    // =========================================================================

    static void AplicarAEdificios()
    {
        var edificiosOSM = GameObject.Find("Edificios_OSM_Reales");
        if (edificiosOSM == null) return;

        var rends = edificiosOSM.GetComponentsInChildren<MeshRenderer>();
        foreach (var r in rends)
        {
            string n = r.gameObject.name.ToLower();
            string parent = r.transform.parent != null ? r.transform.parent.name.ToLower() : "";

            // Tejados rojos teja
            if (n.Contains("aguas") || n.Contains("tejad"))
            {
                r.sharedMaterial = _matTejaRoja;
                continue;
            }
            // Ventanas
            if (n.StartsWith("ven_") || n.Contains("ventana"))
            {
                r.sharedMaterial = _matVentanaCristal;
                continue;
            }
            // Plano tejado moderno
            if (n.Contains("plano"))
            {
                r.sharedMaterial = _matHormigon;
                continue;
            }

            // Decidir por tipo de edificio (lo guardamos en el nombre del padre)
            if (parent.Contains("historic") || parent.Contains("church") || parent.Contains("chapel"))
                r.sharedMaterial = _matPiedra;
            else if (parent.Contains("industrial") || parent.Contains("warehouse"))
                r.sharedMaterial = _matHormigon;
            else if (parent.Contains("retail") || parent.Contains("commercial"))
                r.sharedMaterial = _matLadrilloOcre;
            else if (Random.value < 0.3f)
                r.sharedMaterial = _matLadrilloRojo;
            else if (Random.value < 0.6f)
                r.sharedMaterial = _matPlasterCrema;
            else
                r.sharedMaterial = _matPlasterBlanco;
        }
    }

    // =========================================================================
    //  CARRETERAS Y ACERAS
    // =========================================================================

    static void AplicarACarreteras()
    {
        // Carreteras OSM
        var roads = GameObject.Find("Carreteras_OSM");
        if (roads != null)
        {
            foreach (var r in roads.GetComponentsInChildren<MeshRenderer>())
            {
                string n = r.gameObject.name.ToLower();
                if (n.Contains("acera"))
                    r.sharedMaterial = _matHormigon;
                else if (n.Contains("linea"))
                    continue; // dejar línea blanca como está
                else
                {
                    // Mantener la coloración base (oscura) pero con textura de asfalto
                    var mat = new Material(_matAsfalto);
                    if (r.sharedMaterial != null && r.sharedMaterial.HasColor("_BaseColor"))
                        mat.SetColor("_BaseColor", r.sharedMaterial.GetColor("_BaseColor"));
                    r.sharedMaterial = mat;
                }
            }
        }

        // Autovía N-1
        var n1 = GameObject.Find("Autovia_N1");
        if (n1 != null)
            foreach (var r in n1.GetComponentsInChildren<MeshRenderer>())
                if (!r.gameObject.name.ToLower().Contains("linea"))
                    r.sharedMaterial = _matAsfalto;
    }

    // =========================================================================
    //  PLAZA HERRIKO — adoquines
    // =========================================================================

    static void AplicarPlazaAdoquines()
    {
        // Crear un plano de adoquines en Herriko Plaza
        var terrain = Object.FindFirstObjectByType<Terrain>();
        if (terrain == null) return;

        var antiguo = GameObject.Find("HerrikoPlaza_Adoquines");
        if (antiguo != null) Undo.DestroyObjectImmediate(antiguo);

        float yPlaza = terrain.SampleHeight(new Vector3(1918f, 0, 8570f)) + 0.05f;

        var plaza = new GameObject("HerrikoPlaza_Adoquines");
        plaza.transform.position = new Vector3(1918f, yPlaza, 8570f);

        // Hexágono de 40m de radio aprox para la plaza
        var mesh = CrearPlanoCircular(40f, 16);
        var mf = plaza.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;
        var mr = plaza.AddComponent<MeshRenderer>();
        mr.sharedMaterial = _matAdoquines;

        Undo.RegisterCreatedObjectUndo(plaza, "Plaza Adoquines");
        Debug.Log("[Materiales] ✓ Herriko Plaza con adoquines (40m).");
    }

    static Mesh CrearPlanoCircular(float radio, int segmentos)
    {
        var verts = new List<Vector3>();
        var tris  = new List<int>();
        var uvs   = new List<Vector2>();
        verts.Add(Vector3.zero);
        uvs.Add(new Vector2(0.5f, 0.5f));
        for (int i = 0; i <= segmentos; i++)
        {
            float a = (float)i / segmentos * Mathf.PI * 2f;
            float x = Mathf.Cos(a) * radio;
            float z = Mathf.Sin(a) * radio;
            verts.Add(new Vector3(x, 0, z));
            uvs.Add(new Vector2(x / radio * 4f, z / radio * 4f)); // tiling 4x4
            if (i < segmentos)
            {
                tris.Add(0); tris.Add(i + 1); tris.Add(i + 2);
            }
        }
        var m = new Mesh();
        m.SetVertices(verts);
        m.SetTriangles(tris, 0);
        m.SetUVs(0, uvs);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    // =========================================================================
    //  ESTACIÓN
    // =========================================================================

    static void AplicarAEstacion()
    {
        var estacion = GameObject.Find("Estacion_Tren");
        if (estacion == null) return;
        foreach (var r in estacion.GetComponentsInChildren<MeshRenderer>())
        {
            string n = r.gameObject.name.ToLower();
            if (n.Contains("anden"))
                r.sharedMaterial = _matHormigon;
            else
                r.sharedMaterial = _matPiedra;
        }
    }
}
#endif
