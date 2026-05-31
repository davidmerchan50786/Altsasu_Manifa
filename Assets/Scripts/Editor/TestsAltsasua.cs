// Assets/Scripts/Editor/TestsAltsasua.cs
// Suite de tests que verifican el estado del proyecto antes de hacer Play.
// Menú: Altsasu GTA → Tests → Ejecutar Todos los Tests

using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;

public static class TestsAltsasua
{
    struct TestResult
    {
        public string nombre;
        public bool   ok;
        public string detalle;
    }

    [MenuItem("Altsasu GTA/Tests/▶ Ejecutar Todos los Tests", false, 1)]
    public static void EjecutarTodos()
    {
        var resultados = new List<TestResult>();

        // ── BLOQUE 1: Archivos críticos ───────────────────────────────────
        resultados.Add(TestArchivo("DEM terreno",      "Assets/AlsasuaData/dem_unity_1025.raw",     2_100_000));
        resultados.Add(TestArchivo("Ortofoto PNOA",    "Assets/AlsasuaData/ortofoto_alsasua_REAL.png", 1_000_000));
        resultados.Add(TestArchivo("Buildings OSM",    "Assets/AlsasuaData/buildings_unity.json",   100_000));
        resultados.Add(TestArchivo("Roads OSM",        "Assets/AlsasuaData/roads_unity.json",       200_000));
        resultados.Add(TestArchivo("Railways OSM",     "Assets/AlsasuaData/railways_unity.json",    5_000));
        resultados.Add(TestArchivo("Waterways OSM",    "Assets/AlsasuaData/waterways_unity.json",   5_000));
        resultados.Add(TestArchivo("Trees OSM",        "Assets/AlsasuaData/trees_unity.json",       100_000));
        resultados.Add(TestArchivo("Lucia FBX",        "Assets/Models/Characters/Lucia/LuciaModel.FBX", 100_000));
        resultados.Add(TestArchivo("Guardia Civil FBX","Assets/Models/Characters/GuardiaCivil/Meshy_AI_Guardia_Civil_Officer_0501071058_texture.fbx", 1_000_000));
        resultados.Add(TestArchivo("Deer FBX",         "Assets/Models/Fauna/Deer/deer-female-mesh.fbx", 50_000));
        resultados.Add(TestArchivo("Oak OBJ",          "Assets/Models/Vegetation/Oak/oak.obj",      1_000));
        resultados.Add(TestArchivo("Highway FBX",      "Assets/Models/Roads/Highway/highway.fbx",   1_000));
        resultados.Add(TestArchivo("Bridge GLB",       "Assets/Models/Roads/Bridges/bridge_roads.glb",1_000));
        resultados.Add(TestArchivo("PoliceSiren WAV",  "Assets/Audio/Ambiente/PoliceSiren.WAV",     10_000));

        // ── BLOQUE 2: Scripts críticos compilados ─────────────────────────
        resultados.Add(TestTipoExiste("AltsasuCore"));
        resultados.Add(TestTipoExiste("SceneBootstrapper"));
        resultados.Add(TestTipoExiste("HUDAAA"));
        resultados.Add(TestTipoExiste("SistemaApoyoPopular"));
        resultados.Add(TestTipoExiste("SistemaDestruccion"));
        resultados.Add(TestTipoExiste("SistemaClima"));
        resultados.Add(TestTipoExiste("SistemaManifestacion"));
        resultados.Add(TestTipoExiste("SistemaParanoia"));
        resultados.Add(TestTipoExiste("SistemaArmasExtendido"));
        resultados.Add(TestTipoExiste("SistemaVegetacion"));
        resultados.Add(TestTipoExiste("SistemaFauna"));
        resultados.Add(TestTipoExiste("SistemaTrafico"));
        resultados.Add(TestTipoExiste("SistemaAtmosfera"));
        resultados.Add(TestTipoExiste("TrenEnMovimiento"));
        resultados.Add(TestTipoExiste("SistemaGrafitis"));
        resultados.Add(TestTipoExiste("Health"));
        resultados.Add(TestTipoExiste("GameManagerAltsasua"));

        // ── BLOQUE 3: Escena activa ───────────────────────────────────────
        resultados.Add(TestEscena());
        resultados.Add(TestGameManager());
        resultados.Add(TestTerreno());
        resultados.Add(TestJugadorOPrefab());
        resultados.Add(TestCamara());
        resultados.Add(TestBootstrapper());
        resultados.Add(TestSistemasSingleton());

        // ── BLOQUE 4: Coordenadas y geodatos ─────────────────────────────
        resultados.Add(TestCoordenadasDEM());
        resultados.Add(TestFormatoJSON());

        // ── Informe ───────────────────────────────────────────────────────
        int ok     = resultados.FindAll(r => r.ok).Count;
        int fallos = resultados.Count - ok;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"=== RESULTADOS: {ok}/{resultados.Count} PASSED ===\n");

        string[] bloques = { "ARCHIVOS", "SCRIPTS", "ESCENA", "GEODATOS" };
        int[] limites    = { 14, 17, 7, 2 };
        int offset = 0;
        for (int b = 0; b < bloques.Length; b++)
        {
            sb.AppendLine($"── {bloques[b]} ──");
            int hasta = Mathf.Min(offset + limites[b], resultados.Count);
            for (int i = offset; i < hasta; i++)
            {
                var r = resultados[i];
                sb.AppendLine($"  {(r.ok ? "✓" : "✗")} {r.nombre}{(r.ok ? "" : $"\n      → {r.detalle}")}");
            }
            sb.AppendLine();
            offset += limites[b];
        }

        if (fallos == 0)
            sb.AppendLine("✅ TODOS LOS TESTS PASADOS — Puedes hacer Play.");
        else
            sb.AppendLine($"⚠ {fallos} test(s) fallidos — Ver detalles arriba.");

        string informe = sb.ToString();
        Debug.Log(informe);
        EditorUtility.DisplayDialog(
            fallos == 0 ? "✅ Tests OK" : $"⚠ {fallos} fallos",
            informe.Length > 2000 ? informe.Substring(0, 2000) + "\n[ver Console para más]" : informe,
            "OK");
    }

    // ── Tests individuales ────────────────────────────────────────────────

    static TestResult TestArchivo(string nombre, string assetPath, long minBytes)
    {
        string abs = Path.Combine(Application.dataPath.Replace("Assets",""), assetPath);
        if (!File.Exists(abs))
            return Fail(nombre, $"No existe: {assetPath}");
        long size = new FileInfo(abs).Length;
        if (size < minBytes)
            return Fail(nombre, $"Demasiado pequeño: {size} bytes (mínimo {minBytes})");
        return Pass(nombre);
    }

    static TestResult TestTipoExiste(string className)
    {
        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            try { foreach (var t in asm.GetTypes()) if (t.Name == className) return Pass(className); }
            catch { }
        }
        return Fail(className, $"Clase '{className}' no encontrada — error de compilación");
    }

    static TestResult TestEscena()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        return scene.name.Length > 0 ? Pass("Escena activa válida")
                                     : Fail("Escena activa", "No hay escena abierta");
    }

    static TestResult TestGameManager()
    {
        var gm = Object.FindFirstObjectByType<GameManagerAltsasua>();
        if (gm == null) return Fail("GameManager en escena", "No hay GameManagerAltsasua — ejecuta MAESTRO → MONTAR");
        return Pass("GameManager en escena");
    }

    static TestResult TestTerreno()
    {
        var t = Object.FindFirstObjectByType<Terrain>();
        if (t == null) return Fail("Terrain en escena",
            "No hay Terrain — se creará en Play (SceneBootstrapper)\no ejecuta: Territorio Real → GENERAR TODO");

        var tc = t.GetComponent<TerrainCollider>();
        if (tc == null) return Fail("TerrainCollider", "El Terrain no tiene TerrainCollider — personaje caerá");

        if (t.terrainData == null) return Fail("TerrainData", "Terrain sin datos");

        float altCentro = t.SampleHeight(new Vector3(1918, 0, 8570));
        if (altCentro < 100f || altCentro > 800f)
            return Fail("Altura Herriko Plaza", $"Altura anómala: {altCentro}u (esperado 220-280u para ~545m snm)");

        return Pass($"Terrain OK — Herriko Plaza: {altCentro:F0}u Unity ({305+altCentro:F0}m snm)");
    }

    static TestResult TestJugadorOPrefab()
    {
        var jugador = GameObject.FindGameObjectWithTag("Player");
        if (jugador != null)
        {
            bool tieneRb  = jugador.GetComponent<Rigidbody>() != null;
            bool tieneCol = jugador.GetComponent<Collider>()  != null;
            if (!tieneRb)  return Fail("Jugador Rigidbody", "Player sin Rigidbody — caerá sin física");
            if (!tieneCol) return Fail("Jugador Collider",  "Player sin Collider — atravesará el suelo");
            return Pass($"Jugador en escena: {jugador.name} con Rb+Collider");
        }

        var bootstrap = Object.FindFirstObjectByType<SceneBootstrapper>();
        if (bootstrap != null)
            return Pass("SceneBootstrapper activo — creará jugador en Play");

        return Fail("Jugador/Bootstrapper",
            "Sin jugador y sin SceneBootstrapper.\nEjecuta: MAESTRO → Añadir Bootstrapper");
    }

    static TestResult TestCamara()
    {
        var cam = Camera.main;
        if (cam == null) return Fail("Cámara principal", "No hay Main Camera — no se verá nada");
        return Pass($"Cámara: {cam.gameObject.name}");
    }

    static TestResult TestBootstrapper()
    {
        var boot = Object.FindFirstObjectByType<SceneBootstrapper>();
        return boot != null
            ? Pass("SceneBootstrapper presente")
            : Fail("SceneBootstrapper", "Falta — ejecuta: MAESTRO → Añadir Bootstrapper a escena actual");
    }

    static TestResult TestSistemasSingleton()
    {
        var core  = Object.FindFirstObjectByType<AltsasuCore>();
        var apoyo = Object.FindFirstObjectByType<SistemaApoyoPopular>();
        var dest  = Object.FindFirstObjectByType<SistemaDestruccion>();

        if (core == null && apoyo == null && dest == null)
            return Fail("Sistemas de gameplay", "Ningún sistema activo en escena — se crearán en Play via AltsasuCore");
        if (core != null)
            return Pass($"AltsasuCore activo — inicializará todos los sistemas");
        return Pass("Algunos sistemas activos (AltsasuCore arrancará los demás en Play)");
    }

    static TestResult TestCoordenadasDEM()
    {
        string abs = Path.Combine(Application.dataPath.Replace("Assets",""), "Assets/AlsasuaData/dem_unity_1025.raw");
        if (!File.Exists(abs)) return Fail("DEM formato", "Archivo no existe");

        byte[] raw = File.ReadAllBytes(abs);
        int expected = 1025 * 1025 * 2;
        if (raw.Length != expected)
            return Fail("DEM tamaño", $"Tamaño incorrecto: {raw.Length} bytes (esperado {expected})");

        // Leer altura en Herriko Plaza (1918, 8570) → col=1918/(5000/1024)=393, row=8570/(18000/1024)=487
        int col = Mathf.Clamp((int)(1918f / (5000f/1024f)), 0, 1024);
        int row = Mathf.Clamp((int)(8570f / (18000f/1024f)), 0, 1024);
        int idx = (row * 1025 + col) * 2;
        ushort val = System.BitConverter.ToUInt16(raw, idx);
        float altUnity = val / 65535f * 900f;
        float altSnm   = 305f + altUnity;

        bool ok = altSnm > 450f && altSnm < 650f;
        return ok
            ? Pass($"DEM correcto: Herriko Plaza = {altSnm:F0}m snm ({altUnity:F0}u Unity)")
            : Fail("DEM altitud", $"Herriko Plaza fuera de rango: {altSnm:F0}m (esperado 450-650m snm)");
    }

    static TestResult TestFormatoJSON()
    {
        string abs = Path.Combine(Application.dataPath.Replace("Assets",""), "Assets/AlsasuaData/buildings_unity.json");
        if (!File.Exists(abs)) return Fail("buildings_unity.json", "No existe");

        string json = File.ReadAllText(abs);
        bool tieneX   = json.Contains("\"x\":");
        bool tieneZ   = json.Contains("\"z\":");
        bool tienePoly= json.Contains("\"poly\":");
        bool tieneH   = json.Contains("\"height\":");

        if (!tieneX || !tieneZ || !tienePoly || !tieneH)
            return Fail("JSON edificios", $"Formato inesperado (x:{tieneX} z:{tieneZ} poly:{tienePoly} height:{tieneH})");

        // Contar edificios (contar "\"x\":")
        int numEdificios = System.Text.RegularExpressions.Regex.Matches(json, "\"x\":").Count;
        return Pass($"buildings_unity.json: ~{numEdificios} edificios OSM");
    }

    static TestResult Pass(string nombre) => new TestResult { nombre = nombre, ok = true };
    static TestResult Fail(string nombre, string detalle) => new TestResult { nombre = nombre, ok = false, detalle = detalle };
}
