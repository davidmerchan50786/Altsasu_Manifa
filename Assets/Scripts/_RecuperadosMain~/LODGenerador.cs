// Assets/Scripts/Editor/LODGenerador.cs
// Añade LODGroup a todos los edificios y vegetación generados.
// Menú: Altsasu GTA → MAESTRO → Generar LOD Groups

using UnityEngine;
using UnityEditor;

public static class LODGenerador
{
    // MENÚ-LEGACY: [MenuItem("Altsasu GTA/MAESTRO/Generar LOD Groups (rendimiento AAA)", false, 28)]
    public static void GenerarLODs()
    {
        int procesados = 0;
        procesados += ProcesarContenedor("=== Edificios ===",   TipoLOD.Edificio);
        procesados += ProcesarContenedor("=== Árboles ===",     TipoLOD.Arbol);
        procesados += ProcesarContenedor("--- Vegetacion ---",  TipoLOD.Arbol);
        procesados += ProcesarContenedor("=== Carreteras ===",  TipoLOD.Carretera);
        procesados += ProcesarContenedor("=== Zonas Verdes ===",TipoLOD.ZonaVerde);

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        EditorUtility.DisplayDialog("LOD Groups", $"✓ {procesados} objetos con LODGroup.", "OK");
    }

    enum TipoLOD { Edificio, Arbol, Carretera, ZonaVerde }

    static int ProcesarContenedor(string nombre, TipoLOD tipo)
    {
        var go = GameObject.Find(nombre);
        if (go == null) return 0;
        int n = 0;
        foreach (Transform h in go.transform)
        {
            AñadirLOD(h.gameObject, tipo);
            n++;
        }
        Debug.Log($"[LOD] {nombre}: {n} LODGroups.");
        return n;
    }

    static void AñadirLOD(GameObject go, TipoLOD tipo)
    {
        if (go.GetComponent<LODGroup>() != null) return; // ya tiene

        var renderers = go.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return;

        var lodGroup = Undo.AddComponent<LODGroup>(go);

        // Configurar según tipo de objeto
        LOD[] lods;
        switch (tipo)
        {
            case TipoLOD.Edificio:
                lods = new LOD[] {
                    new LOD(0.06f, renderers),      // LOD0: visible hasta 94% pantalla vacía = ~800m
                    new LOD(0.015f, new Renderer[0]),// LOD1: billboards (sin geometría aquí — extender con Imposter)
                    new LOD(0.003f, new Renderer[0]) // Culled
                };
                break;
            case TipoLOD.Arbol:
                lods = new LOD[] {
                    new LOD(0.10f, renderers),        // LOD0: árbol completo hasta 400m
                    new LOD(0.025f, new Renderer[0]), // LOD1: billboard (simplificado)
                    new LOD(0.005f, new Renderer[0])  // Culled
                };
                break;
            case TipoLOD.Carretera:
                lods = new LOD[] {
                    new LOD(0.001f, renderers),       // Carreteras visibles siempre
                };
                break;
            case TipoLOD.ZonaVerde:
            default:
                lods = new LOD[] {
                    new LOD(0.02f, renderers),
                    new LOD(0.004f, new Renderer[0])
                };
                break;
        }

        lodGroup.SetLODs(lods);
        lodGroup.RecalculateBounds();

        // Configurar fade mode suave (crossfade entre LODs)
        lodGroup.fadeMode = LODFadeMode.CrossFade;
        lodGroup.animateCrossFading = true;
    }
}
