// Assets/#Tools/Editor/AutoGrass.cs
// ═══════════════════════════════════════════════════════════════════════════
//  AUTO GRASS — pinta hierba en el terreno de Alsasua
//  Menú: Tools → Alsasua → 🌿 AutoGrass
//
//  Cómo funciona:
//    1. Lee el splat map (alphamap) del terreno
//    2. Detecta qué capa de textura domina cada pixel
//    3. Pinta la capa de detail (hierba) con esa densidad
//    4. Botón "Setup" configura los DetailPrototypes automáticamente
//       usando las texturas de GreenForest/Textures/Foliage/
//
//  Reescrito para Unity 6 — APIs modernas (terrainLayers, DetailPrototype.prototype)
// ═══════════════════════════════════════════════════════════════════════════

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;

public class AutoGrass : EditorWindow
{
    // ── Estado ────────────────────────────────────────────────────────────────
    Terrain _ter;
    int     _layerTextura  = 0;    // índice de TerrainLayer (splat) que define dónde hay hierba
    int     _layerDetail   = 0;    // índice del DetailPrototype a pintar
    float   _densidad      = 0.7f;
    float   _randMin       = 0.6f;
    float   _randMax       = 1.0f;
    bool    _soloExterior  = true;  // evitar zona urbana (Herriko Plaza ±300m)

    // Zona urbana: X≈1918, Z≈8570, radio≈300m
    const float URBAN_CX = 1918f, URBAN_CZ = 8570f, URBAN_R = 300f;

    Vector2 _scroll;

    // ── Menú ──────────────────────────────────────────────────────────────────

    [MenuItem("Tools/Alsasua/🌿 AutoGrass")]
    public static void ShowWindow()
        => GetWindow<AutoGrass>("AutoGrass");

    // ── GUI ───────────────────────────────────────────────────────────────────

    void OnGUI()
    {
        EditorGUILayout.Space(4);
        GUILayout.Label("🌿 AutoGrass — Alsasua", EditorStyles.boldLabel);
        EditorGUILayout.Space(2);

        _ter = (Terrain)EditorGUILayout.ObjectField("Terreno", _ter, typeof(Terrain), true);

        if (_ter == null)
        {
            EditorGUILayout.HelpBox("Arrastra el Terrain activo aquí.", MessageType.Info);
            if (GUILayout.Button("Usar Terrain activo"))
                _ter = Terrain.activeTerrain;
            return;
        }

        var td = _ter.terrainData;

        EditorGUILayout.Space(6);
        GUILayout.Label("Configuración", EditorStyles.boldLabel);

        // ── Capa de textura (splat) ──
        int nLayers = td.terrainLayers?.Length ?? 0;
        if (nLayers == 0)
        {
            EditorGUILayout.HelpBox("El terreno no tiene TerrainLayers (splat maps). " +
                "Usa el Terrain Inspector → Paint Texture para añadir capas.", MessageType.Warning);
        }
        else
        {
            string[] layerNames = new string[nLayers];
            for (int i = 0; i < nLayers; i++)
                layerNames[i] = td.terrainLayers[i]?.name ?? $"Layer {i}";
            _layerTextura = EditorGUILayout.Popup("Textura base (dónde crece)", _layerTextura,
                Mathf.Clamp(_layerTextura, 0, nLayers - 1) == _layerTextura ? layerNames : layerNames);
            _layerTextura = Mathf.Clamp(_layerTextura, 0, nLayers - 1);
        }

        // ── Capa de detail (hierba) ──
        int nDetails = td.detailPrototypes?.Length ?? 0;
        if (nDetails == 0)
        {
            EditorGUILayout.HelpBox("No hay DetailPrototypes. Pulsa 'Setup Hierba GreenForest'.", MessageType.Warning);
        }
        else
        {
            string[] detailNames = new string[nDetails];
            for (int i = 0; i < nDetails; i++)
            {
                var dp = td.detailPrototypes[i];
                detailNames[i] = dp.prototype != null ? dp.prototype.name
                                : dp.prototypeTexture != null ? dp.prototypeTexture.name
                                : $"Detail {i}";
            }
            _layerDetail = EditorGUILayout.Popup("Detail a pintar", _layerDetail, detailNames);
            _layerDetail = Mathf.Clamp(_layerDetail, 0, nDetails - 1);
        }

        EditorGUILayout.Space(4);
        _densidad = EditorGUILayout.Slider("Densidad", _densidad, 0f, 1f);
        EditorGUILayout.MinMaxSlider("Variación aleatoria",
            ref _randMin, ref _randMax, 0f, 1f);
        EditorGUILayout.LabelField("", $"min {_randMin:F2}  max {_randMax:F2}",
            EditorStyles.miniLabel);

        _soloExterior = EditorGUILayout.Toggle("Solo fuera zona urbana (recomendado)", _soloExterior);

        EditorGUILayout.Space(8);

        // ── Botones ──
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("⚙ Setup Hierba GreenForest", GUILayout.Height(32)))
                SetupDetailPrototypes();

            if (GUILayout.Button("🗑 Borrar toda la hierba", GUILayout.Height(32)))
                BorrarHierba();
        }

        EditorGUILayout.Space(4);

        GUI.backgroundColor = new Color(0.4f, 0.9f, 0.4f);
        if (GUILayout.Button("🌿 PINTAR HIERBA", GUILayout.Height(40)))
        {
            if (td.detailPrototypes == null || td.detailPrototypes.Length == 0)
                EditorUtility.DisplayDialog("Sin prototypes",
                    "Pulsa 'Setup Hierba GreenForest' primero.", "OK");
            else
                PintarHierba();
        }
        GUI.backgroundColor = Color.white;

        // ── Info ──
        EditorGUILayout.Space(6);
        EditorGUILayout.HelpBox(
            $"Terreno: {td.size.x}×{td.size.z}m  " +
            $"DetailRes: {td.detailResolution}  " +
            $"AlphamapRes: {td.alphamapResolution}\n" +
            $"TerrainLayers: {nLayers}  DetailPrototypes: {nDetails}",
            MessageType.None);
    }

    // ── Setup: crea DetailPrototypes desde texturas GreenForest ──────────────

    void SetupDetailPrototypes()
    {
        if (_ter == null) return;

        // Buscar texturas de hierba en GreenForest
        string[] candidates = new[]
        {
            "Assets/GreenForest/Textures/Foliage/Grass.tif",
            "Assets/GreenForest/Textures/Foliage/Grass2.tif",
            "Assets/GreenForest/Textures/Foliage/GrassDry.TIF",
            "Assets/GreenForest/Textures/Foliage/GrassFlowerBlue.TIF",
            "Assets/GreenForest/Textures/Foliage/GrassFlowerYellow.tif",
            "Assets/GreenForest/Textures/Grass.tif",
        };

        var prototypes = new System.Collections.Generic.List<DetailPrototype>();
        foreach (var path in candidates)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null) continue;

            var dp = new DetailPrototype
            {
                renderMode       = DetailRenderMode.GrassBillboard,
                prototypeTexture = tex,
                minWidth         = 0.5f,
                maxWidth         = 1.2f,
                minHeight        = 0.3f,
                maxHeight        = 0.8f,
                noiseSpread      = 0.1f,
                healthyColor     = new Color(0.25f, 0.65f, 0.15f),
                dryColor         = new Color(0.70f, 0.65f, 0.25f),
            };
            prototypes.Add(dp);
        }

        if (prototypes.Count == 0)
        {
            EditorUtility.DisplayDialog("Sin texturas",
                "No se encontraron texturas de hierba en GreenForest/Textures/Foliage/.\n" +
                "Asegúrate de que el paquete GreenForest está importado.", "OK");
            return;
        }

        Undo.RecordObject(_ter.terrainData, "Setup Grass Prototypes");
        _ter.terrainData.detailPrototypes = prototypes.ToArray();
        EditorUtility.SetDirty(_ter.terrainData);
        AssetDatabase.SaveAssets();

        EditorUtility.DisplayDialog("✅ Hierba configurada",
            $"{prototypes.Count} tipos de hierba añadidos al terreno.\n\n" +
            "Ahora pulsa '🌿 PINTAR HIERBA'.", "OK");
    }

    // ── Pintar hierba ─────────────────────────────────────────────────────────

    void PintarHierba()
    {
        var td   = _ter.terrainData;
        int res  = td.detailResolution;
        int ares = td.alphamapResolution;

        if (res == 0 || ares == 0) { Debug.LogWarning("[AutoGrass] Resolution = 0"); return; }

        float scale   = (float)ares / res;
        float[,,] alpha = td.GetAlphamaps(0, 0, ares, ares);
        int nLayers     = alpha.GetLength(2);
        int layerIdx    = Mathf.Clamp(_layerTextura, 0, nLayers - 1);

        int[,] dens = new int[res, res];
        int painted = 0;

        Vector3 terPos  = _ter.transform.position;
        Vector3 terSize = td.size;

        for (int y = 0; y < res; y++)
        for (int x = 0; x < res; x++)
        {
            // Posición Unity de este píxel de detail
            float ux = terPos.x + (float)x / res * terSize.x;
            float uz = terPos.z + (float)y / res * terSize.z;

            // Excluir zona urbana
            if (_soloExterior)
            {
                float dx = ux - URBAN_CX, dz = uz - URBAN_CZ;
                if (dx * dx + dz * dz < URBAN_R * URBAN_R) continue;
            }

            int ay = Mathf.Clamp(Mathf.RoundToInt(y * scale), 0, ares - 1);
            int ax = Mathf.Clamp(Mathf.RoundToInt(x * scale), 0, ares - 1);

            float peso = alpha[ay, ax, layerIdx];
            if (peso < 0.1f) continue;

            float rnd = UnityEngine.Random.Range(_randMin, _randMax);
            dens[y, x] = Mathf.RoundToInt(peso * _densidad * rnd * 16f);
            if (dens[y, x] > 0) painted++;
        }

        Undo.RecordObject(td, "AutoGrass Paint");
        td.SetDetailLayer(0, 0, _layerDetail, dens);
        EditorUtility.SetDirty(td);

        Debug.Log($"[AutoGrass] ✅ {painted:N0} celdas pintadas en detail layer {_layerDetail}");
        EditorUtility.DisplayDialog("🌿 Hierba pintada",
            $"{painted:N0} celdas con hierba pintadas.\n\nDetail layer: {_layerDetail}", "OK");
    }

    // ── Borrar hierba ─────────────────────────────────────────────────────────

    void BorrarHierba()
    {
        if (!EditorUtility.DisplayDialog("Borrar hierba",
            "¿Borrar TODA la hierba del terreno (todos los detail layers)?", "Sí", "Cancelar"))
            return;

        var td  = _ter.terrainData;
        int res = td.detailResolution;
        var empty = new int[res, res];

        Undo.RecordObject(td, "AutoGrass Clear");
        for (int i = 0; i < td.detailPrototypes.Length; i++)
            td.SetDetailLayer(0, 0, i, empty);

        EditorUtility.SetDirty(td);
        Debug.Log("[AutoGrass] Hierba borrada.");
    }
}
#endif
