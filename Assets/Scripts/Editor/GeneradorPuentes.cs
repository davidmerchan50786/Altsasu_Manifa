#if UNITY_EDITOR
// Assets/Scripts/Editor/GeneradorPuentes.cs
// ═══════════════════════════════════════════════════════════════════════════
//  GENERADOR DE PUENTES Y PASOS ELEVADOS
//
//  Añade puentes sobre el río Arakil y un paso elevado de la autovía N-1.
//
//  MENÚ: Altsasu GTA → Territorio Real → ★ Generar Puentes y Pasos Elevados
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public static class GeneradorPuentes
{
    static Terrain _terrain;
    static Material _matPiedra, _matHormigon, _matAsfalto;

    // MENÚ-LEGACY: [MenuItem("Altsasu GTA/Territorio Real/★ Generar Puentes y Pasos Elevados", false, 12)]
    public static void Generar()
    {
        _terrain = Object.FindFirstObjectByType<Terrain>();
        if (_terrain == null) { EditorUtility.DisplayDialog("Sin terrain", "Crea terrain primero.", "OK"); return; }

        CargarMateriales();

        var antiguo = GameObject.Find("Puentes_Altsasu");
        if (antiguo != null) Undo.DestroyObjectImmediate(antiguo);

        var padre = new GameObject("Puentes_Altsasu");
        Undo.RegisterCreatedObjectUndo(padre, "Puentes");

        // 3 puentes sobre el río Arakil (Z ~8215)
        CrearPuentePiedra(new Vector3(1500f, 0, 8200f), 0f,   25f, 8f, padre.transform, "Puente_Oeste");
        CrearPuentePiedra(new Vector3(1918f, 0, 8215f), 0f,   30f, 10f, padre.transform, "Puente_Centro_HerrikoPlaza");
        CrearPuentePiedra(new Vector3(2400f, 0, 8270f), 5f,   28f, 9f, padre.transform, "Puente_Este");

        // Paso elevado de la autovía N-1 sobre la vía del tren
        CrearPasoElevado(new Vector3(2020f, 0, 8400f), 0f, 80f, 16f, padre.transform);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        EditorUtility.DisplayDialog("✅ Puentes generados",
            "Creados:\n\n" +
            "• 3 puentes de piedra sobre el río Arakil\n" +
            "• Paso elevado de la N-1 sobre las vías\n\n" +
            "Cada puente tiene tablero, pilares y barandillas.", "OK");
    }

    static void CargarMateriales()
    {
        string dir = "Assets/AlsasuaData/Materiales_AAA";
        _matPiedra   = AssetDatabase.LoadAssetAtPath<Material>($"{dir}/Mat_Piedra.mat");
        _matHormigon = AssetDatabase.LoadAssetAtPath<Material>($"{dir}/Mat_Hormigon.mat");
        _matAsfalto  = AssetDatabase.LoadAssetAtPath<Material>($"{dir}/Mat_Asfalto.mat");

        if (_matPiedra == null) _matPiedra = CrearMatBasico(new Color(0.78f, 0.74f, 0.66f));
        if (_matHormigon == null) _matHormigon = CrearMatBasico(new Color(0.78f, 0.78f, 0.76f));
        if (_matAsfalto == null) _matAsfalto = CrearMatBasico(new Color(0.20f, 0.20f, 0.20f));
    }

    static Material CrearMatBasico(Color c)
    {
        var m = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"));
        m.SetColor("_BaseColor", c); m.SetColor("_Color", c);
        m.SetFloat("_Smoothness", 0.1f);
        return m;
    }

    // =========================================================================
    //  PUENTE DE PIEDRA — tablero + 2 pilares + 2 barandillas
    // =========================================================================

    static void CrearPuentePiedra(Vector3 centro, float rotY, float longitud, float ancho,
                                    Transform padre, string nombre)
    {
        var root = new GameObject(nombre);
        root.transform.SetParent(padre);

        // Altura del terreno bajo el puente — usar la más alta de los extremos
        Vector3 dir = Quaternion.Euler(0, rotY, 0) * Vector3.forward;
        Vector3 perp = Quaternion.Euler(0, rotY, 0) * Vector3.right;

        Vector3 extA = centro + dir * (longitud * 0.5f);
        Vector3 extB = centro - dir * (longitud * 0.5f);
        float yA = _terrain.SampleHeight(extA);
        float yB = _terrain.SampleHeight(extB);
        float yPuente = Mathf.Max(yA, yB) + 2.5f; // 2.5m sobre la orilla más alta

        root.transform.position = new Vector3(centro.x, yPuente, centro.z);
        root.transform.rotation = Quaternion.Euler(0, rotY, 0);

        // Tablero
        var tablero = GameObject.CreatePrimitive(PrimitiveType.Cube);
        tablero.name = "Tablero";
        tablero.transform.SetParent(root.transform);
        tablero.transform.localPosition = Vector3.zero;
        tablero.transform.localScale    = new Vector3(ancho, 0.6f, longitud);
        tablero.GetComponent<Renderer>().sharedMaterial = _matPiedra;

        // 2 pilares hasta el agua
        for (int i = -1; i <= 1; i += 2)
        {
            float lateralOffset = longitud * 0.25f * i;
            Vector3 posPilarLocal = new Vector3(0, -3f, lateralOffset);
            var pilar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pilar.name = $"Pilar_{i}";
            pilar.transform.SetParent(root.transform);
            pilar.transform.localPosition = posPilarLocal;
            pilar.transform.localScale    = new Vector3(ancho - 1f, 6f, 2f);
            pilar.GetComponent<Renderer>().sharedMaterial = _matPiedra;
        }

        // 2 barandillas (laterales)
        for (int i = -1; i <= 1; i += 2)
        {
            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bar.name = $"Barandilla_{i}";
            bar.transform.SetParent(root.transform);
            bar.transform.localPosition = new Vector3((ancho * 0.5f + 0.15f) * i, 0.7f, 0);
            bar.transform.localScale    = new Vector3(0.3f, 1.2f, longitud);
            bar.GetComponent<Renderer>().sharedMaterial = _matPiedra;
        }

        // Arco bajo el puente (sólo visual, semicilindro)
        var arco = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        arco.name = "Arco";
        arco.transform.SetParent(root.transform);
        arco.transform.localPosition = new Vector3(0, -1.5f, 0);
        arco.transform.localRotation = Quaternion.Euler(0, 0, 90f);
        arco.transform.localScale    = new Vector3(2.5f, ancho * 0.5f, 2.5f);
        arco.GetComponent<Renderer>().sharedMaterial = _matPiedra;
        Object.DestroyImmediate(arco.GetComponent<Collider>());
    }

    // =========================================================================
    //  PASO ELEVADO DE LA AUTOVÍA
    // =========================================================================

    static void CrearPasoElevado(Vector3 centro, float rotY, float longitud, float ancho, Transform padre)
    {
        var root = new GameObject("PasoElevado_N1");
        root.transform.SetParent(padre);

        float yBase = _terrain.SampleHeight(centro);
        float yElevado = yBase + 6f; // 6m sobre el suelo

        root.transform.position = new Vector3(centro.x, yElevado, centro.z);
        root.transform.rotation = Quaternion.Euler(0, rotY, 0);

        // Tablero de la autovía
        var tablero = GameObject.CreatePrimitive(PrimitiveType.Cube);
        tablero.name = "Tablero_Autovia";
        tablero.transform.SetParent(root.transform);
        tablero.transform.localPosition = Vector3.zero;
        tablero.transform.localScale    = new Vector3(ancho, 1.2f, longitud);
        tablero.GetComponent<Renderer>().sharedMaterial = _matAsfalto;

        // 4 pilares de hormigón
        for (int p = 0; p < 4; p++)
        {
            float t = (p + 0.5f) / 4f;
            float zRel = (t - 0.5f) * longitud;
            var pilar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pilar.name = $"Pilar_{p}";
            pilar.transform.SetParent(root.transform);
            pilar.transform.localPosition = new Vector3(0, -3.5f, zRel);
            pilar.transform.localScale    = new Vector3(1.8f, 7f, 1.8f);
            pilar.GetComponent<Renderer>().sharedMaterial = _matHormigon;
        }

        // 2 muros laterales (quitamiedos)
        for (int i = -1; i <= 1; i += 2)
        {
            var muro = GameObject.CreatePrimitive(PrimitiveType.Cube);
            muro.name = $"Quitamiedos_{i}";
            muro.transform.SetParent(root.transform);
            muro.transform.localPosition = new Vector3((ancho * 0.5f + 0.2f) * i, 1f, 0);
            muro.transform.localScale    = new Vector3(0.4f, 1.4f, longitud);
            muro.GetComponent<Renderer>().sharedMaterial = _matHormigon;
        }
    }
}
#endif
