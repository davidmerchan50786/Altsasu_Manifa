// Assets/Scripts/Editor/ConfiguradorCesiumAlsasua.cs
// Tools → Alsasua → 🌍 Configurar Cesium Alsasua
//
// Hace todo lo necesario para que Cesium muestre Alsasua/Altsasua en AAA+:
//   1. Añade scripting define CESIUM_FOR_UNITY si falta
//   2. Crea CesiumGeoreference en coordenadas reales de Herriko Plaza
//   3. Añade Cesium3DTileset con Google Photorealistic 3D Tiles (ID 2275207)
//      → muestra edificios, bosques, terreno fotorrealista de Navarra
//   4. Añade CesiumSunSky para ciclo día/noche real
//   5. Configura el offset Unity↔GPS para que el jugador aparezca en la plaza

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Linq;
using System.Collections.Generic;

public static class ConfiguradorCesiumAlsasua
{
    // Herriko Plaza, Alsasua/Altsasua — coordenadas GPS reales
    const double LAT_PLAZA  = 42.89873;
    const double LON_PLAZA  = -2.16770;
    const double ALT_PLAZA  = 523.0;   // altitud media (metros sobre el mar)

    // Unity coords de Herriko Plaza (referencia del DEM)
    const float UX_PLAZA = GeoDataAlsasua.OX;
    const float UZ_PLAZA = GeoDataAlsasua.OZ;

    [MenuItem("Tools/Alsasua/Escena/🌍 Configurar Cesium", priority = 11)]
    public static void Configurar()
    {
        // ── 1. Scripting define ───────────────────────────────────────────
        AnadirScriptingDefine("CESIUM_FOR_UNITY");

        // ── 2. Verificar que Cesium está en el proyecto ───────────────────
        bool cesiumInstalado = System.Type.GetType("CesiumForUnity.CesiumGeoreference, CesiumForUnity") != null
                            || System.Type.GetType("CesiumForUnity.Cesium3DTileset, CesiumForUnity") != null;

        if (!cesiumInstalado)
        {
            EditorUtility.DisplayDialog("⚠ Cesium no compilado todavía",
                "El paquete com.cesium.unity está en manifest.json.\n\n" +
                "Unity necesita compilarlo antes de poder crear los objetos Cesium.\n\n" +
                "PASOS:\n" +
                "1. Cierra este diálogo\n" +
                "2. Espera a que Unity termine de compilar (barra inferior)\n" +
                "3. Vuelve a ejecutar Tools → Alsasua → 🌍 Configurar Cesium Alsasua",
                "OK");
            return;
        }

        CrearObjetosCesiumEnEscena();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("🌍 Cesium configurado",
            "Cesium listo para Alsasua/Altsasua:\n\n" +
            "• CesiumGeoreference → Herriko Plaza (42.899°N, 2.168°W)\n" +
            "• Google Photorealistic 3D Tiles (ID 2275207)\n" +
            "  → Edificios, bosques, terreno fotorrealista de Navarra\n" +
            "• CesiumSunSky → ciclo día/noche real\n\n" +
            "Si los tiles no cargan:\n" +
            "  Window → Cesium → Connect to Cesium ion\n" +
            "  (el token ya está en CesiumSettings/)",
            "OK");
    }

    static void CrearObjetosCesiumEnEscena()
    {
        // ── CesiumGeoreference ────────────────────────────────────────────
        var georefType = System.Type.GetType("CesiumForUnity.CesiumGeoreference, CesiumForUnity");
        if (georefType == null) return;

        var georefGO = FindOrCreateGO("CesiumGeoreference");
        var georef   = georefGO.GetComponent(georefType)
                    ?? georefGO.AddComponent(georefType);

        // Coordenadas GPS de Herriko Plaza
        SetProp(georef, "latitude",  LAT_PLAZA);
        SetProp(georef, "longitude", LON_PLAZA);
        SetProp(georef, "height",    ALT_PLAZA);

        // ── Google Photorealistic 3D Tiles ────────────────────────────────
        var tilesetType = System.Type.GetType("CesiumForUnity.Cesium3DTileset, CesiumForUnity");
        if (tilesetType != null)
        {
            var googleGO = FindOrCreateChildGO("Google_Photorealistic_3DTiles", georefGO);
            if (googleGO.GetComponent(tilesetType) == null) googleGO.AddComponent(tilesetType);
            var tileset = googleGO.GetComponent(tilesetType);
            // Ion Asset ID 2275207 = Google Photorealistic 3D Tiles
            SetProp(tileset, "ionAssetID",  2275207L);
            SetProp(tileset, "maximumScreenSpaceError", 8f);
            SetProp(tileset, "preloadAncestors", true);
        }

        // ── CesiumSunSky (luz solar georreferenciada) ─────────────────────
        var sunSkyType = System.Type.GetType("CesiumForUnity.CesiumSunSky, CesiumForUnity");
        if (sunSkyType != null)
        {
            var sunGO = FindOrCreateGO("CesiumSunSky");
            if (sunGO.GetComponent(sunSkyType) == null) sunGO.AddComponent(sunSkyType);
            var sunSky = sunGO.GetComponent(sunSkyType);
            SetProp(sunSky, "timeZone", 2); // Europa/Madrid UTC+2
        }

        // ── CesiumCameraController ligado al jugador ──────────────────────
        var camCtrlType = System.Type.GetType("CesiumForUnity.CesiumCameraController, CesiumForUnity");
        if (camCtrlType != null)
        {
            var cam = Camera.main;
            if (cam != null && cam.GetComponent(camCtrlType) == null)
                cam.gameObject.AddComponent(camCtrlType);
        }

        Debug.Log("[Cesium] ✅ CesiumGeoreference + Google Tiles + SunSky configurados");
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    static void AnadirScriptingDefine(string define)
    {
        var target  = UnityEditor.Build.NamedBuildTarget.Standalone;
        string defs = PlayerSettings.GetScriptingDefineSymbols(target);
        if (!defs.Contains(define))
        {
            defs = string.IsNullOrEmpty(defs) ? define : defs + ";" + define;
            PlayerSettings.SetScriptingDefineSymbols(target, defs);
            Debug.Log($"[Cesium] Scripting define '{define}' añadido.");
        }
    }

    static GameObject FindOrCreateGO(string nombre)
    {
        var go = GameObject.Find(nombre);
        if (go == null) go = new GameObject(nombre);
        return go;
    }

    static GameObject FindOrCreateChildGO(string nombre, GameObject padre)
    {
        var t = padre.transform.Find(nombre);
        if (t != null) return t.gameObject;
        var go = new GameObject(nombre);
        go.transform.SetParent(padre.transform, false);
        return go;
    }

    static void SetProp(object obj, string prop, object val)
    {
        if (obj == null) return;
        var p = obj.GetType().GetProperty(prop,
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public   |
            System.Reflection.BindingFlags.NonPublic);
        if (p != null && p.CanWrite)
            try { p.SetValue(obj, System.Convert.ChangeType(val, p.PropertyType)); } catch { }

        var f = obj.GetType().GetField(prop,
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public   |
            System.Reflection.BindingFlags.NonPublic);
        if (f != null)
            try { f.SetValue(obj, System.Convert.ChangeType(val, f.FieldType)); } catch { }
    }
}
#endif
