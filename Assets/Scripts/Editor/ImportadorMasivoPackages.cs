// Assets/Scripts/Editor/ImportadorMasivoPackages.cs
// Ventana para importar los .unitypackage del caché del Asset Store
// uno a uno con checkbox, detección de ya importados y barra de progreso.
// Menú: Tools/Alsasua/Assets/📦 Importar Assets desde caché

using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class ImportadorMasivoPackages : EditorWindow
{
    // ── Rutas donde buscar .unitypackage ───────────────────────────────────
    static readonly string[] RUTAS_BUSQUEDA = {
        @"C:\Users\coperenea\AppData\Roaming\Unity\Asset Store-5.x",
        @"E:\assets",              // ← colección principal encontrada
        @"E:\Desk\DAM\Assets",     // por si se añaden aquí
        @"D:\",                    // se ignoran si no existen
        @"H:\",
        @"I:\",
        @"J:\",
    };

    struct Paquete
    {
        public string nombre;
        public string ruta;
        public float mb;
        public bool yaImportado;
        public bool seleccionado;
    }

    List<Paquete> _paquetes = new();
    Vector2 _scroll;
    bool _cargado;
    bool _soloNoImportados = true;
    string _filtro = "";

    [MenuItem("Tools/Alsasua/Assets/📦 Importar Assets desde caché", priority = 5)]
    static void Abrir() => GetWindow<ImportadorMasivoPackages>("Importar Assets").minSize = new Vector2(600, 500);

    void OnEnable() => Recargar();

    // ── Carga la lista de paquetes ─────────────────────────────────────────
    void Recargar()
    {
        _paquetes.Clear();
        var carpetasAssets = ObtenerCarpetasImportadas();

        foreach (var ruta in RUTAS_BUSQUEDA)
        {
            if (!Directory.Exists(ruta)) continue;
            foreach (var f in Directory.GetFiles(ruta, "*.unitypackage", SearchOption.AllDirectories))
            {
                var info = new FileInfo(f);
                if (info.Length == 0) continue; // paquete vacío
                string nombre = Path.GetFileNameWithoutExtension(f);
                bool ya = EstaImportado(nombre, carpetasAssets);
                _paquetes.Add(new Paquete
                {
                    nombre = nombre,
                    ruta = f,
                    mb = info.Length / 1024f / 1024f,
                    yaImportado = ya,
                    seleccionado = !ya
                });
            }
        }

        // Quitar duplicados por nombre (queda la primera aparición)
        _paquetes = _paquetes
            .GroupBy(p => p.nombre.ToLowerInvariant())
            .Select(g => g.First())
            .OrderBy(p => p.nombre)
            .ToList();

        _cargado = true;
    }

    // Carpetas de primer nivel en Assets/ (para detectar importados)
    static HashSet<string> ObtenerCarpetasImportadas()
    {
        var set = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        string assetsDir = Application.dataPath;
        if (!Directory.Exists(assetsDir)) return set;
        foreach (var d in Directory.GetDirectories(assetsDir))
            set.Add(Path.GetFileName(d).Replace(" ", "").Replace("_", "").ToLowerInvariant());
        return set;
    }

    static bool EstaImportado(string nombrePaquete, HashSet<string> carpetas)
    {
        string clave = nombrePaquete.Replace(" ", "").Replace("_", "").Replace("-", "").ToLowerInvariant();
        // Comprobación directa
        if (carpetas.Any(c => c.Replace("-","").Contains(clave) || clave.Contains(c.Replace("-","")))) return true;
        // Alias conocidos (paquete → carpeta real en Assets/)
        var alias = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
        {
            ["MapMagic2"]                        = "mapmagic",
            ["VegetationSpawnerFREE"]            = "vegetationspawner",
            ["FullscreenEditorPlayModeFREE"]      = "fullscreen",
            ["404GEN3DGenerator"]                = "404",
            ["GreenForest"]                      = "greenforest",
            ["HousePack"]                        = "housepack",
            ["EasyRoads3DFreev3"]                = "easyroads3d",
            ["NatureStarterKit2"]                = "naturesterterkit2",
            ["YughuesFreeBushes"]                = "yughuesfreebushes2018",
            ["YughuesFreeBushes2018"]            = "yughuesfreebushes2018",
            ["AdventureCharacter"]               = "adventure_character",
            ["BigPoplarTreeFREE"]                = "alp_assets",
            ["LowPolySoldiersDemo"]              = "lowpolysoldiers_demo",
            ["HQExplosionsPackFREE"]             = "hqexplosionspackfree",  // wait
            ["GrenadeExplosivePack"]             = "grenadepack",
            ["HatchbackandSedan"]                = "hatchbackandsedan",
            ["EpicGameHitsSFX"]                  = "epicgamehitssfx",
            ["Bodyguards"]                       = "bodyguards",
            ["Survivalistcharacter"]             = "survivalist",
            ["RacingCarsPack1"]                  = "racingcarspack1",
            ["UltraSkyboxFog"]                   = "ultraskyboxfog",
            ["FightingMotionsVol1"]              = "fightingmotionsvolume1",
            ["HotRod"]                           = "hotrod",
            ["PoliceCarHelicopter"]              = "policecarhelicopter",
        };
        if (alias.TryGetValue(clave, out var target))
            return carpetas.Any(c => c.Contains(target));
        return false;
    }

    // ── GUI ────────────────────────────────────────────────────────────────
    void OnGUI()
    {
        if (!_cargado) { Recargar(); return; }

        var filtrados = _paquetes
            .Where(p => !_soloNoImportados || !p.yaImportado)
            .Where(p => string.IsNullOrEmpty(_filtro) || p.nombre.ToLower().Contains(_filtro.ToLower()))
            .ToList();

        int selCount = filtrados.Count(p => p.seleccionado);
        float selMB  = filtrados.Where(p => p.seleccionado).Sum(p => p.mb);

        // Cabecera
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField(
            $"Total caché: {_paquetes.Count}  |  Mostrando: {filtrados.Count}  |  Seleccionados: {selCount} ({selMB:F0} MB)",
            EditorStyles.boldLabel);
        EditorGUILayout.Space(2);

        // Barra filtros
        EditorGUILayout.BeginHorizontal();
        _filtro = EditorGUILayout.TextField("Filtrar:", _filtro, GUILayout.ExpandWidth(true));
        _soloNoImportados = GUILayout.Toggle(_soloNoImportados, "Ocultar ya importados", GUILayout.Width(180));
        EditorGUILayout.EndHorizontal();

        // Botones selección
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("✔ Todos"))
            foreach (var p in filtrados.ToList()) SetSeleccionado(p.nombre, true);
        if (GUILayout.Button("✘ Ninguno"))
            foreach (var p in filtrados.ToList()) SetSeleccionado(p.nombre, false);
        if (GUILayout.Button("↺ Recargar"))
            Recargar();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);

        // Lista
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        for (int i = 0; i < filtrados.Count; i++)
        {
            var p = filtrados[i];
            int idx = _paquetes.FindIndex(x => x.nombre == p.nombre);

            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = p.yaImportado ? new Color(0.6f, 1f, 0.6f) : Color.white;

            bool nueva = EditorGUILayout.ToggleLeft(
                $"{p.nombre}  ({p.mb:F0} MB){(p.yaImportado ? "  ✓ ya importado" : "")}",
                p.seleccionado, GUILayout.ExpandWidth(true));

            if (nueva != p.seleccionado && idx >= 0)
            {
                var mod = _paquetes[idx]; mod.seleccionado = nueva; _paquetes[idx] = mod;
            }

            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();

        GUI.backgroundColor = Color.white;
        EditorGUILayout.Space(6);

        // Aviso HDRP
        EditorGUILayout.HelpBox(
            "⚠ Algunos paquetes usan shaders Built-in o URP → materiales rosas en HDRP. " +
            "Usa ConversorMaterialesHDRP después de importar.",
            MessageType.Warning);

        // Botón importar
        GUI.backgroundColor = selCount > 0 ? new Color(1f, 0.85f, 0.2f) : Color.gray;
        GUI.enabled = selCount > 0;
        if (GUILayout.Button($"  IMPORTAR {selCount} PAQUETES  ({selMB:F0} MB)  ", GUILayout.Height(42)))
            ImportarSeleccionados(filtrados.Where(p => p.seleccionado).ToList());
        GUI.enabled = true;
        GUI.backgroundColor = Color.white;
        EditorGUILayout.Space(4);
    }

    void SetSeleccionado(string nombre, bool valor)
    {
        int idx = _paquetes.FindIndex(p => p.nombre == nombre);
        if (idx < 0) return;
        var p = _paquetes[idx]; p.seleccionado = valor; _paquetes[idx] = p;
    }

    // ── Importación ────────────────────────────────────────────────────────
    void ImportarSeleccionados(List<Paquete> lista)
    {
        bool confirmar = EditorUtility.DisplayDialog(
            "Importar Assets",
            $"Se importarán {lista.Count} paquetes ({lista.Sum(p => p.mb):F0} MB).\n\n" +
            "• Modo silencioso: importa todo sin preguntar.\n" +
            "• Modo interactivo: muestra diálogo por paquete.\n\n" +
            "¿Continuar en modo silencioso?",
            "Sí, modo silencioso", "Cancelar");

        if (!confirmar) return;

        int ok = 0, err = 0;
        for (int i = 0; i < lista.Count; i++)
        {
            var p = lista[i];
            bool cancelar = EditorUtility.DisplayCancelableProgressBar(
                "Importando Assets",
                $"({i + 1}/{lista.Count}) {p.nombre}",
                (float)i / lista.Count);

            if (cancelar) break;

            try
            {
                AssetDatabase.ImportPackage(p.ruta, false);
                ok++;
                // Marcar como importado
                int idx = _paquetes.FindIndex(x => x.nombre == p.nombre);
                if (idx >= 0) { var mod = _paquetes[idx]; mod.yaImportado = true; mod.seleccionado = false; _paquetes[idx] = mod; }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[ImportadorMasivo] Error en '{p.nombre}': {e.Message}");
                err++;
            }
        }

        EditorUtility.ClearProgressBar();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Importación completa",
            $"✅ {ok} importados correctamente.\n" +
            (err > 0 ? $"⚠ {err} con error (ver Console).\n\n" : "\n") +
            "Si hay materiales rosas, usa:\nTools/Alsasua/Render → Convertir Materiales HDRP",
            "OK");

        Debug.Log($"[ImportadorMasivo] {ok} paquetes importados, {err} errores.");
    }
}
