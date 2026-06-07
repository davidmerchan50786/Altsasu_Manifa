// Assets/Scripts/Editor/ImportadorPaquetes.cs
// ═══════════════════════════════════════════════════════════════════════════
//  IMPORTADOR DE PAQUETES — Menú: Tools → Alsasua → 📦 Importar Paquetes
//
//  Detecta automáticamente los .unitypackage en PackagesToImport/ y
//  permite importarlos en orden de prioridad con un clic por grupo.
//
//  Total detectado: 19 paquetes — 4.1 GB
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Linq;

public class ImportadorPaquetes : EditorWindow
{
    // ── Datos de cada paquete ─────────────────────────────────────────────

    private class InfoPaquete
    {
        public string   Nombre;
        public string   RutaCompleta;
        public float    TamanoMB;
        public int      Prioridad;     // 1=crítico, 2=importante, 3=opcional
        public string   Categoria;
        public string   UsoEnAlsasua;
        public bool     Seleccionado = true;
        public bool     YaImportado  = false;
    }

    private static readonly string CARPETA_PAQUETES =
        Path.Combine(Application.dataPath, "..", "PackagesToImport");

    private List<InfoPaquete> _paquetes = new();
    private Vector2           _scroll;
    private bool              _soloNoImportados = false;
    private bool              _autoConvertirHDRP = true;
    private bool              _interactivo = false;

    // ─────────────────────────────────────────────────────────────────────────

    // [DESACTIVADO] [MenuItem("Tools/Alsasua/📦 Importar Paquetes", priority = 5)]
    public static void Abrir()
    {
        var ventana = GetWindow<ImportadorPaquetes>("📦 Importar Paquetes");
        ventana.minSize = new Vector2(600, 500);
        ventana.EscanearPaquetes();
    }

    private void OnEnable() => EscanearPaquetes();

    // ─────────────────────────────────────────────────────────────────────────
    //  ESCANEO
    // ─────────────────────────────────────────────────────────────────────────

    private void EscanearPaquetes()
    {
        _paquetes.Clear();
        string carpeta = Path.GetFullPath(CARPETA_PAQUETES);
        if (!Directory.Exists(carpeta))
        {
            Debug.LogWarning($"[ImportadorPaquetes] Carpeta no encontrada: {carpeta}");
            return;
        }

        var archivos = Directory.GetFiles(carpeta, "*.unitypackage", SearchOption.TopDirectoryOnly);

        // Catálogo con prioridad y descripción para Alsasua
        var catalogo = new Dictionary<string, (int prio, string cat, string uso)>
        {
            // ── PRIORIDAD 1: Críticos para el Vertical Slice ─────────────────
            ["GTA Unity"]                   = (1, "🎮 Mecánicas GTA", "Base de mecánicas GTA: wanted, coches, cámara"),
            ["Fast Dynamic Sky"]            = (1, "🌤 Cielo/Atmósfera",  "Cielo dinámico HDRP — nubes, lluvia, día/noche"),
            ["simple_street_props"]         = (1, "🏙 Props Urbanos",    "Bancos, papeleras, semáforos, señales de Alsasua"),
            ["House Pack"]                  = (1, "🏠 Edificios",        "Casas del casco histórico de Alsasua"),
            ["VILLAGE HOUSES"]              = (1, "🏠 Edificios",        "Pack de casas de pueblo vasco-navarro"),
            ["Free Low Poly Nature Forest"] = (1, "🌲 Vegetación",       "Árboles del bosque — Urbasa, Aralar, pinar norte"),
            ["Quirky Series"]               = (1, "🦌 Fauna",            "Animales — ciervos, conejos, zorros (Sakana)"),

            // ── PRIORIDAD 2: Importantes para el entorno ─────────────────────
            ["Nature Starter Kit 2"]        = (2, "🌿 Naturaleza",       "Hierba, arbustos, terreno para los bosques"),
            ["Yughues Free Bushes"]         = (2, "🌿 Vegetación",       "Arbustos y maleza — borde de carreteras"),
            ["Realistic Fences Pack"]       = (2, "🚧 Props Rurales",    "Vallas metálicas para polígonos industriales"),
            ["mountain_terrain_rock"]       = (2, "⛰ Terreno",          "Rocas y árboles para las sierras de Urbasa/Aralar"),
            ["hill_rock_mountain"]          = (2, "⛰ Terreno",          "Terreno de colinas — ladera del Monte Artia"),
            ["Street Lamps 2"]              = (2, "💡 Iluminación",      "Farolas (complementa SpaceZeta_StreetLamps2)"),
            ["Idyllic Fantasy Nature"]      = (2, "🌸 Naturaleza",       "Vegetación decorativa — parques y plazas"),

            // ── PRIORIDAD 3: Audio y texturas (archivos grandes) ─────────────
            ["Free General Ambience"]       = (3, "🔊 Audio Ambiental",  "Sonidos ambientales del pueblo y la naturaleza (1.5 GB)"),
            ["terrain_textures_free"]       = (3, "🎨 Texturas Terreno", "Texturas PBR de suelo/hierba/roca (705 MB)"),
            ["freerealisticoutdoormaterials"]= (3,"🎨 Materiales PBR",  "Materiales exteriores realistas (481 MB)"),
            ["Free Sound Effects Pack"]     = (3, "🔊 SFX",             "Efectos de sonido generales (285 MB)"),
            ["Free Water Stream Sounds"]    = (3, "🔊 Agua",            "Sonidos río Altzania/Burunda (184 MB)"),
        };

        foreach (var archivo in archivos.OrderBy(f => f))
        {
            var info     = new FileInfo(archivo);
            string nombre = Path.GetFileNameWithoutExtension(archivo);

            // Buscar en catálogo (comparación flexible)
            var entrada = catalogo.FirstOrDefault(kv =>
                nombre.IndexOf(kv.Key, System.StringComparison.OrdinalIgnoreCase) >= 0);

            int    prio = entrada.Value.prio == 0 ? 2 : entrada.Value.prio;
            string cat  = string.IsNullOrEmpty(entrada.Value.cat) ? "📦 Otros" : entrada.Value.cat;
            string uso  = string.IsNullOrEmpty(entrada.Value.uso) ? nombre : entrada.Value.uso;

            // Detectar si ya hay assets del paquete importados (heurística por nombre)
            bool yaImportado = AssetDatabase.IsValidFolder($"Assets/{nombre}")
                            || AssetDatabase.IsValidFolder($"Assets/{nombre.Replace(" ", "_")}");

            _paquetes.Add(new InfoPaquete
            {
                Nombre       = nombre,
                RutaCompleta = archivo,
                TamanoMB     = info.Length / 1024f / 1024f,
                Prioridad    = prio,
                Categoria    = cat,
                UsoEnAlsasua = uso,
                Seleccionado = prio == 1,   // pre-seleccionar solo los críticos
                YaImportado  = yaImportado,
            });
        }

        // Ordenar: prioridad asc, tamaño asc (primero los pequeños del mismo grupo)
        _paquetes = _paquetes
            .OrderBy(p => p.Prioridad)
            .ThenBy(p => p.TamanoMB)
            .ToList();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  GUI
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly Color[] ColorPrioridad =
    {
        Color.clear,
        new Color(0.2f, 0.8f, 0.3f, 0.15f),  // P1 verde
        new Color(1.0f, 0.7f, 0.1f, 0.12f),  // P2 amarillo
        new Color(0.7f, 0.7f, 0.7f, 0.10f),  // P3 gris
    };

    private void OnGUI()
    {
        if (_paquetes.Count == 0) EscanearPaquetes();

        // ── Cabecera ──────────────────────────────────────────────────────
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("📦  IMPORTADOR DE PAQUETES — ALSASUA",
            EditorStyles.boldLabel);

        float totalSelMB = _paquetes.Where(p => p.Seleccionado).Sum(p => p.TamanoMB);
        float totalMB    = _paquetes.Sum(p => p.TamanoMB);
        EditorGUILayout.LabelField(
            $"Total: {_paquetes.Count} paquetes · {totalMB / 1024f:F1} GB  " +
            $"| Seleccionados: {_paquetes.Count(p=>p.Seleccionado)} · {totalSelMB / 1024f:F2} GB",
            EditorStyles.miniLabel);

        EditorGUILayout.Space(2);

        // ── Opciones ──────────────────────────────────────────────────────
        EditorGUILayout.BeginHorizontal();
        _soloNoImportados = EditorGUILayout.ToggleLeft("Solo no importados", _soloNoImportados, GUILayout.Width(150));
        _autoConvertirHDRP = EditorGUILayout.ToggleLeft("Auto-convertir → HDRP después", _autoConvertirHDRP, GUILayout.Width(220));
        _interactivo = EditorGUILayout.ToggleLeft("Modo interactivo (diálogo por paquete)", _interactivo, GUILayout.Width(250));
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(2);

        // ── Botones de selección rápida ───────────────────────────────────
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("✅ Seleccionar P1 Críticos", GUILayout.Height(22)))
            foreach (var p in _paquetes) p.Seleccionado = (p.Prioridad == 1);
        if (GUILayout.Button("✅✅ Seleccionar P1+P2", GUILayout.Height(22)))
            foreach (var p in _paquetes) p.Seleccionado = (p.Prioridad <= 2);
        if (GUILayout.Button("☑ Todos", GUILayout.Height(22)))
            foreach (var p in _paquetes) p.Seleccionado = true;
        if (GUILayout.Button("☐ Ninguno", GUILayout.Height(22)))
            foreach (var p in _paquetes) p.Seleccionado = false;
        if (GUILayout.Button("🔄 Reescanear", GUILayout.Height(22)))
            EscanearPaquetes();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);

        // ── Leyenda ───────────────────────────────────────────────────────
        EditorGUILayout.BeginHorizontal();
        MostrarChip("🟢 P1 Crítico", ColorPrioridad[1]);
        MostrarChip("🟡 P2 Importante", ColorPrioridad[2]);
        MostrarChip("⚪ P3 Opcional (pesado)", ColorPrioridad[3]);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(2);

        // ── Lista de paquetes ─────────────────────────────────────────────
        _scroll = EditorGUILayout.BeginScrollView(_scroll,
            GUILayout.ExpandHeight(true));

        int priActual = 0;
        foreach (var p in _paquetes)
        {
            if (_soloNoImportados && p.YaImportado) continue;

            if (p.Prioridad != priActual)
            {
                priActual = p.Prioridad;
                EditorGUILayout.Space(4);
                string label = priActual == 1 ? "🟢  PRIORIDAD 1 — CRÍTICOS (importar primero)"
                             : priActual == 2 ? "🟡  PRIORIDAD 2 — IMPORTANTES"
                                              : "⚪  PRIORIDAD 3 — OPCIONALES (archivos grandes)";
                EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            }

            // Fila del paquete
            var colorFondo = p.YaImportado
                ? new Color(0.3f, 0.3f, 0.3f, 0.05f)
                : ColorPrioridad[Mathf.Clamp(p.Prioridad, 0, 3)];

            var rectFila = EditorGUILayout.BeginHorizontal();
            EditorGUI.DrawRect(rectFila, colorFondo);

            GUI.enabled = !p.YaImportado;
            p.Seleccionado = EditorGUILayout.Toggle(p.Seleccionado, GUILayout.Width(20));
            GUI.enabled = true;

            // Nombre + categoría
            EditorGUILayout.LabelField(p.YaImportado ? $"✅ {p.Nombre}" : p.Nombre,
                GUILayout.Width(250));
            EditorGUILayout.LabelField(p.Categoria, EditorStyles.miniLabel, GUILayout.Width(130));

            // Tamaño
            string tamStr = p.TamanoMB >= 1000f
                ? $"{p.TamanoMB / 1024f:F1} GB"
                : $"{p.TamanoMB:F0} MB";
            EditorGUILayout.LabelField(tamStr, EditorStyles.miniLabel, GUILayout.Width(60));

            // Descripción
            EditorGUILayout.LabelField(p.UsoEnAlsasua, EditorStyles.miniLabel);

            // Botón importar individual
            if (!p.YaImportado && GUILayout.Button("⬇", GUILayout.Width(24), GUILayout.Height(18)))
                ImportarPaquete(p);

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(6);

        // ── Botón principal ───────────────────────────────────────────────
        int numSel = _paquetes.Count(p => p.Seleccionado && !p.YaImportado);
        float mbSel = _paquetes.Where(p => p.Seleccionado && !p.YaImportado).Sum(p => p.TamanoMB);
        string mbLabel = mbSel >= 1000f ? $"{mbSel/1024f:F1} GB" : $"{mbSel:F0} MB";

        GUI.enabled = numSel > 0;
        GUI.backgroundColor = new Color(0.3f, 0.8f, 0.4f);
        if (GUILayout.Button($"⬇  IMPORTAR {numSel} PAQUETES SELECCIONADOS ({mbLabel})",
            GUILayout.Height(36)))
        {
            ImportarSeleccionados();
        }
        GUI.backgroundColor = Color.white;
        GUI.enabled = true;

        if (numSel == 0)
            EditorGUILayout.HelpBox("Selecciona al menos un paquete para importar.", MessageType.Info);

        EditorGUILayout.Space(4);
    }

    private static void MostrarChip(string texto, Color color)
    {
        var rect = GUILayoutUtility.GetRect(new GUIContent(texto), EditorStyles.miniLabel,
            GUILayout.Width(180), GUILayout.Height(16));
        EditorGUI.DrawRect(rect, color);
        EditorGUI.LabelField(rect, texto, EditorStyles.miniLabel);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  IMPORTACIÓN
    // ─────────────────────────────────────────────────────────────────────────

    private void ImportarSeleccionados()
    {
        var seleccionados = _paquetes
            .Where(p => p.Seleccionado && !p.YaImportado)
            .OrderBy(p => p.Prioridad)
            .ThenBy(p => p.TamanoMB)
            .ToList();

        if (seleccionados.Count == 0) return;

        float totalMB = seleccionados.Sum(p => p.TamanoMB);
        string mbStr  = totalMB >= 1000 ? $"{totalMB/1024f:F1} GB" : $"{totalMB:F0} MB";

        bool confirmar = EditorUtility.DisplayDialog(
            "Importar paquetes",
            $"Se van a importar {seleccionados.Count} paquetes ({mbStr}).\n\n" +
            "Orden de importación:\n" +
            string.Join("\n", seleccionados.Select(p => $"  · {p.Nombre} ({p.TamanoMB:F0} MB)")) +
            "\n\n⚠️ Los paquetes grandes pueden tardar varios minutos.\n" +
            "Unity puede parecer bloqueado durante la importación — es normal.",
            "Importar", "Cancelar");

        if (!confirmar) return;

        int completados = 0;
        foreach (var p in seleccionados)
        {
            EditorUtility.DisplayProgressBar(
                "Importando paquetes...",
                $"({completados + 1}/{seleccionados.Count}) {p.Nombre}",
                (float)completados / seleccionados.Count);

            ImportarPaquete(p);
            completados++;
        }

        EditorUtility.ClearProgressBar();

        // Auto-convertir materiales a HDRP si está activado
        if (_autoConvertirHDRP)
        {
            EditorUtility.DisplayProgressBar("Post-proceso", "Convirtiendo materiales a HDRP...", 0.9f);
            AplicadorTexturasAAA.ConvertirMaterialesAHDRP();
            EditorUtility.ClearProgressBar();
        }

        // Reescanear para actualizar estado
        EscanearPaquetes();
        AssetDatabase.Refresh();

        Debug.Log($"[ImportadorPaquetes] ✅ {completados} paquetes importados correctamente.");
        EditorUtility.DisplayDialog("Importación completada",
            $"✅ {completados} paquetes importados.\n\n" +
            "Revisa la Consola para ver si hubo errores.\n\n" +
            "Próximo paso: Tools → Alsasua → 🔌 Integrar Assets Nuevos\n" +
            "para conectar los prefabs a los sistemas del juego.",
            "OK");
    }

    private void ImportarPaquete(InfoPaquete p)
    {
        if (!File.Exists(p.RutaCompleta))
        {
            Debug.LogError($"[ImportadorPaquetes] Archivo no encontrado: {p.RutaCompleta}");
            return;
        }

        try
        {
            // interactive=false → importa todo sin diálogo
            // interactive=true  → muestra el diálogo de selección de Unity
            AssetDatabase.ImportPackage(p.RutaCompleta, _interactivo);
            Debug.Log($"[ImportadorPaquetes] ✅ Importado: {p.Nombre} ({p.TamanoMB:F0} MB)");
            p.YaImportado = true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[ImportadorPaquetes] ❌ Error importando '{p.Nombre}': {e.Message}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  ACCESO RÁPIDO POR GRUPO (MenuItem para importar sin abrir ventana)
    // ─────────────────────────────────────────────────────────────────────────

    // [DESACTIVADO] [MenuItem("Tools/Alsasua/Paquetes/⚡ Importar solo CRÍTICOS (P1 · rápido)", priority = 6)]
    static void ImportarSoloCriticos()
    {
        string[] criticos = {
            "GTA Unity 1.0.unitypackage",
            "Fast Dynamic Sky.unitypackage",
            "simple_street_props.unitypackage",
            "House Pack.unitypackage",
            "VILLAGE HOUSES PACK.unitypackage",
            "Free Low Poly Nature Forest.unitypackage",
            "Quirky Series - FREE Animals Pack.unitypackage",
        };
        ImportarLista(criticos, "Paquetes Críticos P1");
    }

    // [DESACTIVADO] [MenuItem("Tools/Alsasua/Paquetes/🌿 Importar solo Naturaleza (vegetación)", priority = 7)]
    static void ImportarNaturaleza()
    {
        string[] naturaleza = {
            "Free Low Poly Nature Forest.unitypackage",
            "Nature Starter Kit 2.unitypackage",
            "Yughues Free Bushes.unitypackage",
            "mountain_terrain_rock_and_tree.unitypackage",
            "hill_rock_mountain_terrain.unitypackage",
            "Idyllic Fantasy Nature.unitypackage",
            "terrain_textures_free.unitypackage",
        };
        ImportarLista(naturaleza, "Pack Naturaleza");
    }

    // [DESACTIVADO] [MenuItem("Tools/Alsasua/Paquetes/🔊 Importar solo Audio", priority = 8)]
    static void ImportarSoloAudio()
    {
        string[] audio = {
            "Free General Ambience Sounds.unitypackage",
            "Free Sound Effects Pack.unitypackage",
            "Free Water Stream Sounds.unitypackage",
        };
        ImportarLista(audio, "Pack Audio");
    }

    private static void ImportarLista(string[] nombres, string etiqueta)
    {
        string carpeta = Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", "PackagesToImport"));

        int ok = 0;
        foreach (var nombre in nombres)
        {
            string ruta = Path.Combine(carpeta, nombre);
            if (!File.Exists(ruta))
            {
                Debug.LogWarning($"[ImportadorPaquetes] No encontrado: {nombre}");
                continue;
            }
            try
            {
                AssetDatabase.ImportPackage(ruta, false);
                Debug.Log($"[ImportadorPaquetes] ✅ {nombre}");
                ok++;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ImportadorPaquetes] ❌ {nombre}: {e.Message}");
            }
        }

        AssetDatabase.Refresh();
        Debug.Log($"[ImportadorPaquetes] '{etiqueta}' completado: {ok}/{nombres.Length} importados.");

        // Auto-convertir materiales
        AplicadorTexturasAAA.ConvertirMaterialesAHDRP();
    }
}
