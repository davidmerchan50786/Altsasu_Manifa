// Assets/Scripts/Editor/FachadasVascas.cs
// ═══════════════════════════════════════════════════════════════════════════
//  Tools → Alsasua → 🏛 Aplicar Fachadas Vascas a Zone_01
//
//  Actualiza los materiales de los edificios generados con:
//    · Paleta de colores real de Alsasua (extraída de Street View/PNOA)
//    · Variación por tipo de edificio y posición (no todos iguales)
//    · Balcones de madera en fachada principal (casas + apartamentos)
//    · Pintadas políticas en euskera en ~20% de fachadas (decals de texto)
//    · Tejados de teja árabe roja diferenciados de pizarra para iglesia
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

public static class FachadasVascas
{
    // ── Paleta real de Alsasua (valores extraídos de ortofoto PNOA + Street View) ──
    // Alsasua tiene arquitectura popular navarra: piedra caliza beige-crema,
    // enlucido blanco envejecido, carpintería de madera oscura, teja árabe.

    static readonly Color[] PALETA_CASCO = {
        new Color(0.90f, 0.86f, 0.76f),   // piedra caliza beige (dominante)
        new Color(0.85f, 0.82f, 0.74f),   // caliza más clara
        new Color(0.96f, 0.94f, 0.88f),   // enlucido blanco envejecido
        new Color(0.78f, 0.74f, 0.68f),   // gris piedra oscuro
        new Color(0.88f, 0.80f, 0.70f),   // caliza con tono cálido
    };

    static readonly Color[] PALETA_APARTAMENTOS = {
        new Color(0.93f, 0.93f, 0.90f),   // hormigón blanco moderno
        new Color(0.85f, 0.87f, 0.88f),   // gris azulado moderno
        new Color(0.90f, 0.88f, 0.85f),   // crema moderno
        new Color(0.80f, 0.82f, 0.80f),   // gris medio
    };

    static readonly Color[] PALETA_INDUSTRIAL = {
        new Color(0.65f, 0.63f, 0.60f),
        new Color(0.58f, 0.60f, 0.58f),
        new Color(0.72f, 0.68f, 0.62f),
    };

    // Pintadas políticas reales de Alsasua y entorno vasco
    static readonly string[] PINTADAS = {
        "AMNISTIA", "PRESOAK ETXERA", "INDEPENDENTZIA",
        "ETA ET BIZIRIK", "EUSKAL HERRIA", "ERREPUBLIKA",
        "AZATUTA", "GORA EUSKADI", "ABS", "NAZ",
    };

    static readonly Color[] COLORES_PINTADA = {
        Color.white, Color.yellow, new Color(1f,0.3f,0.3f),
        new Color(0.3f,1f,0.4f), Color.cyan,
    };

    static readonly Color COLOR_MADERA_OSCURA  = new Color(0.22f, 0.14f, 0.08f);
    static readonly Color COLOR_TEJA_ROJA      = new Color(0.62f, 0.25f, 0.14f);
    static readonly Color COLOR_PIZARRA        = new Color(0.28f, 0.30f, 0.32f);

    // ─────────────────────────────────────────────────────────────────────────

    [MenuItem("Tools/Alsasua/🏛 Aplicar Fachadas Vascas", priority = 35)]
    public static void AplicarFachadas()
    {
        var zona = GameObject.Find("Zone_01_HerrikoPlaza_Centro");
        if (zona == null) { Debug.LogWarning("Zone_01 no encontrada."); return; }

        bool activa = zona.activeSelf; zona.SetActive(true);

        var shader = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
        int edificios = 0, pintadas = 0, balcones = 0;
        var rnd = new System.Random(42); // semilla fija → resultado reproducible

        // Snapshot de hijos para evitar modificar la colección mientras iteramos
        var hijos = new Transform[zona.transform.childCount];
        for (int hi = 0; hi < zona.transform.childCount; hi++)
            hijos[hi] = zona.transform.GetChild(hi);

        foreach (Transform hijo in hijos)
        {
            if (hijo == null) continue;
            // Saltar contenedores que no son edificios
            string hn = hijo.name;
            if (hn == "_Marcador" || hn == "_Label") continue;
            if (hn.StartsWith("Carreteras") || hn.StartsWith("Waypoints") ||
                hn.StartsWith("Civiles")    || hn.StartsWith("Batch_")   ||
                hn == "SistemaTrafico") continue;
            // Solo procesar GOs que tengan algún MeshRenderer en sus hijos
            if (hijo.GetComponentInChildren<MeshRenderer>() == null) continue;

            var tipo = DetectarTipo(hijo.name);

            // ── Material de pared ─────────────────────────────────────────
            var pared = hijo.Find("LOD0/Paredes") ?? hijo.Find("Paredes");
            if (pared != null)
            {
                var mr = pared.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    var mat = new Material(shader);
                    Color col = ElegirColor(tipo, rnd);
                    SetColor(mat, "_BaseColor", col);
                    SetFloat(mat, "_Smoothness", 0.08f + (float)rnd.NextDouble() * 0.06f);
                    SetFloat(mat, "_Metallic", 0f);

                    // Normal map de piedra si existe
                    var nm = AssetDatabase.LoadAssetAtPath<Texture2D>(
                        "Assets/wall_cobblestone_cracks_1024px.png");
                    if (nm != null && mat.HasProperty("_NormalMap"))
                    {
                        mat.SetTexture("_NormalMap", nm);
                        mat.SetFloat("_NormalScale", tipo == TipoFachada.Casco ? 0.8f : 0.3f);
                    }
                    mr.sharedMaterial = mat;
                    EditorUtility.SetDirty(mr);
                }
            }

            // ── Material de tejado ────────────────────────────────────────
            var tejado = hijo.Find("LOD0/Tejado") ?? hijo.Find("Tejado");
            if (tejado != null)
            {
                var mr = tejado.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    var mat = new Material(shader);
                    Color teja = tipo == TipoFachada.Iglesia ? COLOR_PIZARRA : COLOR_TEJA_ROJA;
                    // Variación sutil: 30% usan teja más oscura
                    if (rnd.NextDouble() < 0.3f) teja = Color.Lerp(teja, Color.gray, 0.25f);
                    SetColor(mat, "_BaseColor", teja);
                    SetFloat(mat, "_Smoothness", tipo == TipoFachada.Iglesia ? 0.2f : 0.12f);
                    mr.sharedMaterial = mat;
                    EditorUtility.SetDirty(mr);
                }
            }

            // ── Balcones de madera (casas y apartamentos) ─────────────────
            bool tieneBalcon = tipo == TipoFachada.Casco || tipo == TipoFachada.Apartamentos;
            if (tieneBalcon && rnd.NextDouble() < 0.75f)
            {
                AnadirBalcones(hijo, shader, rnd);
                balcones++;
            }

            // ── Pintadas políticas (~18% de edificios) ────────────────────
            if (tipo != TipoFachada.Iglesia && rnd.NextDouble() < 0.18f)
            {
                AnadirPintada(hijo, rnd);
                pintadas++;
            }

            edificios++;
        }

        zona.SetActive(activa);
        EditorUtility.SetDirty(zona);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

        Debug.Log($"[Fachadas] ✅ {edificios} edificios actualizados — " +
                  $"{balcones} balcones, {pintadas} pintadas.");
        EditorUtility.DisplayDialog("Fachadas vascas aplicadas",
            $"✅ {edificios} edificios con paleta real de Alsasua.\n" +
            $"  · {balcones} balcones de madera oscura\n" +
            $"  · {pintadas} pintadas políticas en euskera", "OK");
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  BALCONES
    // ─────────────────────────────────────────────────────────────────────────

    static void AnadirBalcones(Transform edificio, Shader shader, System.Random rnd)
    {
        // Eliminar balcones previos
        var prev = edificio.Find("Balcones");
        if (prev != null) Object.DestroyImmediate(prev.gameObject);

        // Buscar el MeshFilter de paredes para conocer la geometría
        var paredMF = (edificio.Find("LOD0/Paredes") ?? edificio.Find("Paredes"))
                      ?.GetComponent<MeshFilter>();
        if (paredMF == null) return;

        var bounds = paredMF.sharedMesh?.bounds ?? new Bounds(Vector3.zero, Vector3.one * 5f);
        float alto  = bounds.size.y;
        float largo = Mathf.Max(bounds.size.x, bounds.size.z);

        if (alto < 5f || largo < 3f) return; // edificio demasiado pequeño

        var contenedor = new GameObject("Balcones");
        contenedor.transform.SetParent(edificio, false);
        contenedor.isStatic = true;

        var matMadera = new Material(shader) { name = "Mat_Madera" };
        SetColor(matMadera, "_BaseColor", COLOR_MADERA_OSCURA);
        SetFloat(matMadera, "_Smoothness", 0.25f);

        int numPlantas = Mathf.FloorToInt((alto - 1f) / 3f);
        float anchoBalcon = Mathf.Clamp(largo * 0.6f, 2f, 5f);

        for (int p = 1; p < numPlantas && p < 4; p++) // máx 3 balcones por fachada
        {
            if (rnd.NextDouble() < 0.3f) continue; // algunos pisos sin balcón

            float y = p * 3f + 0.8f; // altura del balcón

            // Losa del balcón (cubo plano)
            var losa = GameObject.CreatePrimitive(PrimitiveType.Cube);
            losa.name = $"Losa_P{p}";
            losa.transform.SetParent(contenedor.transform, false);
            losa.transform.localPosition = new Vector3(bounds.center.x, y, bounds.max.z + 0.25f);
            losa.transform.localScale    = new Vector3(anchoBalcon, 0.12f, 0.6f);
            losa.GetComponent<Renderer>().sharedMaterial = matMadera;
            Object.DestroyImmediate(losa.GetComponent<Collider>());
            losa.isStatic = true;

            // Barandilla (barra horizontal)
            var barandilla = GameObject.CreatePrimitive(PrimitiveType.Cube);
            barandilla.name = $"Barandilla_P{p}";
            barandilla.transform.SetParent(contenedor.transform, false);
            barandilla.transform.localPosition = new Vector3(
                bounds.center.x, y + 0.9f, bounds.max.z + 0.50f);
            barandilla.transform.localScale = new Vector3(anchoBalcon, 0.06f, 0.06f);
            barandilla.GetComponent<Renderer>().sharedMaterial = matMadera;
            Object.DestroyImmediate(barandilla.GetComponent<Collider>());
            barandilla.isStatic = true;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  PINTADAS (texto 3D en muro)
    // ─────────────────────────────────────────────────────────────────────────

    static void AnadirPintada(Transform edificio, System.Random rnd)
    {
        var prev = edificio.Find("Pintada"); if (prev != null) Object.DestroyImmediate(prev.gameObject);

        var paredMF = (edificio.Find("LOD0/Paredes") ?? edificio.Find("Paredes"))
                      ?.GetComponent<MeshFilter>();
        if (paredMF == null) return;
        var bounds = paredMF.sharedMesh?.bounds ?? new Bounds(Vector3.zero, Vector3.one * 4f);
        if (bounds.size.z < 3f) return;

        string texto  = PINTADAS[rnd.Next(PINTADAS.Length)];
        Color  color  = COLORES_PINTADA[rnd.Next(COLORES_PINTADA.Length)];
        float  altura = 1.2f + (float)rnd.NextDouble() * 1.5f;
        float  escala = Mathf.Clamp(bounds.size.z * 0.35f, 0.5f, 2.5f);

        var go  = new GameObject("Pintada");
        go.transform.SetParent(edificio, false);
        go.isStatic = true;

        // Posición: en la pared sur (mayor Z) a altura aleatoria
        go.transform.localPosition = new Vector3(
            bounds.center.x,
            altura,
            bounds.max.z + 0.02f);  // ligeramente delante de la pared

        var tm = go.AddComponent<TextMesh>();
        tm.text       = texto;
        tm.fontSize   = 24;
        tm.characterSize = escala * 0.05f;
        tm.color      = color;
        tm.anchor     = TextAnchor.MiddleCenter;
        tm.alignment  = TextAlignment.Center;
        tm.fontStyle  = FontStyle.Bold;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  HELPERS
    // ─────────────────────────────────────────────────────────────────────────

    enum TipoFachada { Casco, Apartamentos, Industrial, Iglesia, Generico }

    static TipoFachada DetectarTipo(string nombre)
    {
        string n = nombre.ToLower();
        if (n.Contains("eliza") || n.Contains("iglesia") || n.Contains("chapel")) return TipoFachada.Iglesia;
        if (n.Contains("house") || n.Contains("terrace") || n.Contains("detached")) return TipoFachada.Casco;
        if (n.Contains("apart") || n.Contains("block")) return TipoFachada.Apartamentos;
        if (n.Contains("industrial") || n.Contains("garage") || n.Contains("farm")) return TipoFachada.Industrial;
        // Edificios cerca del centro → estilo casco
        return TipoFachada.Casco;
    }

    static Color ElegirColor(TipoFachada tipo, System.Random rnd)
    {
        Color[] paleta = tipo switch {
            TipoFachada.Casco         => PALETA_CASCO,
            TipoFachada.Apartamentos  => PALETA_APARTAMENTOS,
            TipoFachada.Industrial    => PALETA_INDUSTRIAL,
            TipoFachada.Iglesia       => new[]{ new Color(0.86f,0.84f,0.78f) },
            _                         => PALETA_CASCO
        };
        var c = paleta[rnd.Next(paleta.Length)];
        // Variación sutil individual (±3%)
        float v = (float)(rnd.NextDouble() - 0.5) * 0.06f;
        return new Color(Mathf.Clamp01(c.r+v), Mathf.Clamp01(c.g+v), Mathf.Clamp01(c.b+v));
    }

    static void SetColor(Material m, string p, Color c)
    { if (m.HasProperty(p)) m.SetColor(p, c); else m.color = c; }
    static void SetFloat(Material m, string p, float v)
    { if (m.HasProperty(p)) m.SetFloat(p, v); }
}
