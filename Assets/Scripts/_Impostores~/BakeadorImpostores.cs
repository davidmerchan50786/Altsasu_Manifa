// Assets/Scripts/_Impostores~/BakeadorImpostores.cs  (STAGING/DRAFT — Unity no compila carpetas con ~)
// ─────────────────────────────────────────────────────────────────────────────
//  Fase 1 del sistema de impostores: hornea un atlas de albedo con N vistas yaw
//  por edificio seleccionado. Crea un ImpostorAtlasSO con las UVs y el tamaño
//  de cada quad. La fase 2 (ImpostorBillboard + shader octaédrico + hook en
//  StreamerMundoEstatico) consume este SO.
//
//  Uso (tras ACTIVAR — ver LEEME_impostores.md):
//    1. Selecciona en la jerarquía los edificios a hornear (piloto: 5-10).
//    2. Tools ▸ Alsasua ▸ Impostores ▸ 🔆 Bake atlas (selección).
//
//  NOTA HDRP: el clear transparente de cámara en HDRP puede requerir un setup de
//  HDCamera dedicado; si el alfa sale opaco, hornear sobre un fondo croma y
//  recortarlo, o usar una ShaderGraph Unlit para el preview. Marcado DRAFT.
// ─────────────────────────────────────────────────────────────────────────────
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class BakeadorImpostores
{
    const int   DIST = 5000;     // staging lejos del mapa (que va de ±7 km)
    const float MARGEN = 1.08f;

    [MenuItem("Tools/Alsasua/Impostores/🔆 Bake atlas (selección)")]
    public static void Bake()
    {
        var objetivos = new List<GameObject>();
        foreach (var go in Selection.gameObjects)
            if (go.GetComponentInChildren<Renderer>() != null) objetivos.Add(go);
        if (objetivos.Count == 0) { EditorUtility.DisplayDialog("Impostores", "Selecciona edificios con Renderer.", "Ok"); return; }

        int vistas = 8, celda = 256, atlasPx = 4096;
        int tiraPx = vistas * celda;            // ancho de la tira de un edificio
        int porFila = Mathf.Max(1, atlasPx / tiraPx);
        int filas = Mathf.CeilToInt(objetivos.Count / (float)porFila);
        if (filas * celda > atlasPx)
            Debug.LogWarning($"[Impostores] {objetivos.Count} edificios no caben en {atlasPx}px; sube atlasPx o haz varios atlas.");

        var atlas = new Texture2D(atlasPx, atlasPx, TextureFormat.RGBA32, false, false);
        var vacio = new Color[atlasPx * atlasPx];
        atlas.SetPixels(vacio); atlas.Apply();

        var rt = new RenderTexture(celda, celda, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
        var camGo = new GameObject("~ImpostorCam") { hideFlags = HideFlags.HideAndDontSave };
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true; cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0, 0, 0, 0); cam.allowHDR = false; cam.targetTexture = rt;
        cam.nearClipPlane = 1f; cam.farClipPlane = DIST * 4f;

        var so = ScriptableObject.CreateInstance<ImpostorAtlasSO>();
        so.vistasYaw = vistas; so.celdaPx = celda; so.atlasPx = atlasPx;

        var celTex = new Texture2D(celda, celda, TextureFormat.RGBA32, false);
        try
        {
            for (int i = 0; i < objetivos.Count; i++)
            {
                var src = objetivos[i];
                EditorUtility.DisplayProgressBar("Bake impostores", src.name, i / (float)objetivos.Count);

                Bounds b = CalcularBounds(src);
                float r = Mathf.Max(b.extents.y, Mathf.Max(b.extents.x, b.extents.z)) * MARGEN;
                cam.orthographicSize = r;

                // clon aislado lejos del mapa
                var clon = Object.Instantiate(src);
                clon.hideFlags = HideFlags.HideAndDontSave;
                Vector3 stage = new Vector3(DIST, DIST, DIST);
                clon.transform.position += (stage - b.center);
                Vector3 centro = b.center + (stage - b.center);

                int col = i % porFila, fila = i / porFila;
                int ox = col * tiraPx, oy = fila * celda;

                for (int v = 0; v < vistas; v++)
                {
                    float ang = v / (float)vistas * 360f;
                    Quaternion rot = Quaternion.Euler(20f, ang, 0f); // ligera elevación
                    cam.transform.position = centro + rot * (Vector3.back * (r * 2f + 10f));
                    cam.transform.LookAt(centro, Vector3.up);

                    cam.Render();
                    RenderTexture.active = rt;
                    celTex.ReadPixels(new Rect(0, 0, celda, celda), 0, 0);
                    celTex.Apply();
                    RenderTexture.active = null;
                    atlas.SetPixels(ox + v * celda, oy, celda, celda, celTex.GetPixels());
                }
                Object.DestroyImmediate(clon);

                long id = ExtraerId(src.name);
                so.entradas.Add(new ImpostorAtlasSO.Entrada
                {
                    id = id,
                    uvTira = new Rect(ox / (float)atlasPx, oy / (float)atlasPx, tiraPx / (float)atlasPx, celda / (float)atlasPx),
                    anchoMundo = r * 2f, altoMundo = r * 2f,
                    pivotMundo = new Vector3(b.center.x, b.center.y - b.extents.y, b.center.z),
                });
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            cam.targetTexture = null; Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(rt); Object.DestroyImmediate(celTex);
        }

        atlas.Apply();
        Directory.CreateDirectory("Assets/AlsasuaData/impostores_v1");
        string pngPath = "Assets/AlsasuaData/impostores_v1/atlas_albedo.png";
        File.WriteAllBytes(pngPath, atlas.EncodeToPNG());
        Object.DestroyImmediate(atlas);
        AssetDatabase.ImportAsset(pngPath, ImportAssetOptions.ForceUpdate);
        if (AssetImporter.GetAtPath(pngPath) is TextureImporter ti)
        { ti.alphaIsTransparency = true; ti.mipmapEnabled = true; ti.SaveAndReimport(); }

        so.albedoAtlas = AssetDatabase.LoadAssetAtPath<Texture2D>(pngPath);
        AssetDatabase.CreateAsset(so, "Assets/AlsasuaData/impostores_v1/ImpostorAtlas.asset");
        AssetDatabase.SaveAssets();
        Debug.Log($"[Impostores] Atlas horneado: {so.entradas.Count} edificios, {vistas} vistas → {pngPath}");
        EditorUtility.DisplayDialog("Impostores",
            $"✅ Atlas horneado: {so.entradas.Count} edificios × {vistas} vistas.\n\n{pngPath}\n" +
            "Revisa el alfa (ver nota HDRP en la cabecera del script).", "Ok");
        Selection.activeObject = so;
    }

    static Bounds CalcularBounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        var b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return b;
    }

    static long ExtraerId(string nombre)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in nombre) if (char.IsDigit(c)) sb.Append(c);
        return sb.Length > 0 && long.TryParse(sb.ToString(), out var id) ? id : 0;
    }
}
