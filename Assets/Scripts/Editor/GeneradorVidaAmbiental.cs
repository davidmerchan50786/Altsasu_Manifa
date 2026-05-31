#if UNITY_EDITOR
// Assets/Scripts/Editor/GeneradorVidaAmbiental.cs
// ═══════════════════════════════════════════════════════════════════════════
//  VIDA AMBIENTAL — añade dinamismo natural a la escena.
//
//    · Bandadas de pájaros volando en torno al casco urbano (8 bandadas)
//    · Audio Sources 3D distribuidos: pájaros, río, viento, campanas iglesia,
//      tráfico distante, voces de plaza
//    · Faros nocturnos a todos los vehículos (Light Spot con cookies)
//    · SistemaDiaNocheReal en la escena con valores correctos
// ═══════════════════════════════════════════════════════════════════════════

using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public static class GeneradorVidaAmbiental
{
    public static void Generar()
    {
        int bandadas, audioSources, faros;
        try
        {
            EditorUtility.DisplayProgressBar("Vida ambiental", "Bandadas de pájaros...", 0.2f);
            bandadas = GenerarBandadasPajaros();

            EditorUtility.DisplayProgressBar("Vida ambiental", "Audio 3D...", 0.45f);
            audioSources = ColocarAudioFuentes();

            EditorUtility.DisplayProgressBar("Vida ambiental", "Faros a vehículos...", 0.7f);
            faros = AñadirFarosVehiculos();

            EditorUtility.DisplayProgressBar("Vida ambiental", "Sistema día/noche...", 0.9f);
            ConfigurarSistemaDiaNoche();
        }
        finally { EditorUtility.ClearProgressBar(); }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        EditorUtility.DisplayDialog("✅ Vida ambiental",
            $"• Bandadas de pájaros: {bandadas}\n" +
            $"• Audio sources 3D: {audioSources}\n" +
            $"• Faros añadidos a {faros} vehículos\n" +
            $"• SistemaDiaNocheReal en escena", "OK");
    }

    // =========================================================================
    //  BANDADAS DE PÁJAROS
    // =========================================================================

    static int GenerarBandadasPajaros()
    {
        var padre = GameObject.Find("Pajaros_Bandadas");
        if (padre != null) Object.DestroyImmediate(padre);
        padre = new GameObject("Pajaros_Bandadas");

        var t = Terrain.activeTerrain;
        if (t == null) return 0;

        const int N_BANDADAS = 8;
        const float CX = 1918f, CZ = 8570f;

        for (int i = 0; i < N_BANDADAS; i++)
        {
            // Distribuir alrededor del casco urbano
            float ang = (i / (float)N_BANDADAS) * Mathf.PI * 2f;
            float dist = Random.Range(200f, 700f);
            Vector3 pos = new Vector3(
                CX + Mathf.Cos(ang) * dist, 0,
                CZ + Mathf.Sin(ang) * dist);
            pos.y = t.SampleHeight(pos) + Random.Range(40f, 90f);

            CrearBandada(pos, padre.transform);
        }
        return N_BANDADAS;
    }

    static void CrearBandada(Vector3 pos, Transform padre)
    {
        var go = new GameObject("Bandada");
        go.transform.SetParent(padre);
        go.transform.position = pos;

        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop                = true;
        main.startLifetime       = 12f;
        main.startSpeed          = 3f;
        main.startSize           = 0.5f;
        main.startRotation       = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor          = new Color(0.1f, 0.1f, 0.1f);
        main.maxParticles        = 25;
        main.simulationSpace     = ParticleSystemSimulationSpace.World;

        var em = ps.emission;
        em.rateOverTime = 2.5f;

        // Shape: esfera pequeña
        var sh = ps.shape;
        sh.shapeType  = ParticleSystemShapeType.Sphere;
        sh.radius = 5f;

        // Velocidad orbital
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.x = new ParticleSystem.MinMaxCurve(-2f, 2f);
        vel.y = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
        vel.z = new ParticleSystem.MinMaxCurve(-2f, 2f);

        // Noise para movimiento natural
        var noise = ps.noise;
        noise.enabled         = true;
        noise.strength        = 1.5f;
        noise.frequency       = 0.3f;
        noise.scrollSpeed     = 0.5f;
        noise.damping         = true;

        // Material — sprite simple negro (mejor sería una textura de silueta pájaro)
        var psr = go.GetComponent<ParticleSystemRenderer>();
        var sh2 = Shader.Find("HDRP/Unlit") ?? Shader.Find("Sprites/Default");
        var mat = new Material(sh2);
        mat.SetColor("_UnlitColor", new Color(0.05f, 0.05f, 0.05f, 1f));
        psr.material = mat;
        psr.renderMode = ParticleSystemRenderMode.Billboard;
    }

    // =========================================================================
    //  AUDIO 3D AMBIENTAL
    // =========================================================================

    static int ColocarAudioFuentes()
    {
        var padre = GameObject.Find("Audio_Ambiental_3D");
        if (padre != null) Object.DestroyImmediate(padre);
        padre = new GameObject("Audio_Ambiental_3D");

        var t = Terrain.activeTerrain;
        if (t == null) return 0;

        const float CX = 1918f, CZ = 8570f;

        // (nombre, offsetX, offsetZ, alturaSobreSuelo, radio, vol, clipBuscar)
        var fuentes = new (string nombre, float ox, float oz, float oy, float radio, float vol, string clipNombre)[]
        {
            // Río Burunda — ruido de agua a lo largo
            ("Audio_Rio_1",      150f,  -50f, 0.5f, 60f,  0.6f, "agua_rio"),
            ("Audio_Rio_2",      550f,    0f, 0.5f, 60f,  0.6f, "agua_rio"),
            ("Audio_Rio_3",      950f,   30f, 0.5f, 60f,  0.6f, "agua_rio"),

            // Iglesia — campanas (periódico)
            ("Audio_Campanas",     0f,   80f, 25f,  300f, 0.8f, "campanas"),

            // Plaza — voces y ambient
            ("Audio_PlazaVoces",   0f,    0f, 2f,   40f,  0.4f, "voces_plaza"),

            // Tráfico distante
            ("Audio_Trafico_N",    0f,  500f, 8f,  150f,  0.3f, "trafico"),
            ("Audio_Trafico_S",    0f, -500f, 8f,  150f,  0.3f, "trafico"),

            // Pájaros zonas verdes
            ("Audio_Pajaros_E",  400f,  200f, 12f,  80f,  0.5f, "pajaros"),
            ("Audio_Pajaros_W", -400f,  200f, 12f,  80f,  0.5f, "pajaros"),

            // Viento en montañas
            ("Audio_Viento",       0f, 2000f, 50f, 500f,  0.3f, "viento"),
        };

        int count = 0;
        foreach (var f in fuentes)
        {
            var go = new GameObject(f.nombre);
            go.transform.SetParent(padre.transform);
            Vector3 pos = new Vector3(CX + f.ox, 0, CZ + f.oz);
            pos.y = t.SampleHeight(pos) + f.oy;
            go.transform.position = pos;

            var src = go.AddComponent<AudioSource>();
            src.spatialBlend = 1f;        // 3D
            src.rolloffMode  = AudioRolloffMode.Linear;
            src.minDistance  = f.radio * 0.2f;
            src.maxDistance  = f.radio;
            src.loop         = true;
            src.playOnAwake  = true;
            src.volume       = f.vol;

            // Buscar el clip en Resources o en Assets si está disponible
            var clip = BuscarAudioClip(f.clipNombre);
            if (clip != null) src.clip = clip;
            // Si no se encuentra, queda sin clip; el usuario puede arrastrarlo manualmente.
            count++;
        }
        return count;
    }

    static AudioClip BuscarAudioClip(string nombre)
    {
        // Busca en Assets/Audio/ y Assets/Resources/Audio/
        var guids = AssetDatabase.FindAssets($"{nombre} t:AudioClip");
        if (guids.Length > 0)
            return AssetDatabase.LoadAssetAtPath<AudioClip>(
                AssetDatabase.GUIDToAssetPath(guids[0]));
        return null;
    }

    // =========================================================================
    //  FAROS A VEHÍCULOS
    // =========================================================================

    static int AñadirFarosVehiculos()
    {
        int count = 0;
        // Buscar por nombre conocido + componente VehiculoNPC si existe
        var vehiculos = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)
            .Where(t => {
                string n = t.name.ToLower();
                return (n.Contains("coche") || n.Contains("vehiculo") || n.Contains("car") ||
                        n.Contains("polici") || n.Contains("furgon") || n.Contains("hotrod") ||
                        n.Contains("sportcar")) && t.parent == null;
            }).ToArray();

        foreach (var v in vehiculos)
        {
            // Estimar bounds para colocar los faros en el morro
            var rends = v.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) continue;

            Bounds b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);

            // Lado largo = forward
            Vector3 morro = b.center + v.forward * b.extents.z;

            // Faro izquierdo y derecho
            AñadirFaro(v, morro + v.right * b.extents.x * 0.6f
                                + Vector3.up * b.extents.y * 0.3f, "FaroI");
            AñadirFaro(v, morro - v.right * b.extents.x * 0.6f
                                + Vector3.up * b.extents.y * 0.3f, "FaroD");
            count++;
        }
        return count;
    }

    static void AñadirFaro(Transform padre, Vector3 worldPos, string nombre)
    {
        // No duplicar
        if (padre.Find(nombre) != null) return;

        var go = new GameObject(nombre);
        go.transform.SetParent(padre);
        go.transform.position = worldPos;
        go.transform.rotation = padre.rotation;

        var luz = go.AddComponent<Light>();
        luz.type      = LightType.Spot;
        luz.color     = new Color(1f, 0.95f, 0.85f);
        luz.intensity = 5f;
        luz.range     = 35f;
        luz.spotAngle = 45f;
        luz.innerSpotAngle = 25f;
        luz.shadows   = LightShadows.None; // shadows en spot son caros
        luz.enabled   = false; // se activa de noche por SistemaDiaNocheReal

        // Si HDRP, ajustes adicionales
        var hd = go.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>();
        if (hd != null) hd.affectsVolumetric = true;
    }

    // =========================================================================
    //  SISTEMA DÍA/NOCHE
    // =========================================================================

    static void ConfigurarSistemaDiaNoche()
    {
        var existente = Object.FindFirstObjectByType<SistemaDiaNocheReal>();
        if (existente != null) return;

        var go = new GameObject("_SistemaDiaNoche");
        var sis = go.AddComponent<SistemaDiaNocheReal>();
        sis.horaInicial          = 11f;
        sis.segundosPorHoraJuego = 120f; // 1h juego = 2 min real → día completo = 48 min
        sis.tiempoCorre          = true;
    }
}
#endif
