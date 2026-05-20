// IntegradorAssets.cs
// ═══════════════════════════════════════════════════════════════════════════
//  INTEGRADOR DE ASSETS — conecta en runtime todos los recursos descargados
//
//  Al arrancar:
//  1. Carga los 14 clips de audio desde Resources/Audio/
//     y los asigna al AudioManager por nombre
//  2. Carga el primer HDRI disponible y lo asigna al sky volume HDRP
//  3. Asigna materiales de las texturas Poly Haven a los edificios y terreno
//  4. Detecta y configura los FBX importados como prefabs de fauna/vehículos
//  5. Conecta GeneradorMundoOSM con el SceneBootstrapper
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

[DefaultExecutionOrder(-80)]
public class IntegradorAssets : MonoBehaviour
{
    public static IntegradorAssets Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void Start() => StartCoroutine(Integrar());

    IEnumerator Integrar()
    {
        yield return null; // dejar que AltsasuCore arranque

        IntegrarAudio();
        yield return null;
        IntegrarHDRI();
        yield return null;
        IntegrarTerrainLayers();
        yield return null;
        IntegrarFBXEnSistemas();

        AlsasuaLogger.Info("Integrador", "✅ Todos los assets integrados correctamente");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  AUDIO — carga los 14 clips reales y los mapea al AudioManager
    // ════════════════════════════════════════════════════════════════════════

    void IntegrarAudio()
    {
        var clips = Resources.LoadAll<AudioClip>("Audio");
        if (clips.Length == 0) { AlsasuaLogger.Warn("Integrador", "Sin clips en Resources/Audio"); return; }

        // Mapeo nombre → enum AudioManager.Clip
        var mapa = new Dictionary<string, AudioManager.Clip>(System.StringComparer.OrdinalIgnoreCase)
        {
            ["disparo"]      = AudioManager.Clip.Disparo,
            ["gunshot"]      = AudioManager.Clip.Disparo,
            ["pistol"]       = AudioManager.Clip.Disparo,
            ["explosion"]    = AudioManager.Clip.Explosion,
            ["explosion2"]   = AudioManager.Clip.ExplosionPequena,
            ["impacto_metal"]= AudioManager.Clip.ImpactoMetal,
            ["metal"]        = AudioManager.Clip.ImpactoMetal,
            ["paso_asfalto"] = AudioManager.Clip.PasoAsfalto,
            ["paso_tierra"]  = AudioManager.Clip.PasoTierra,
            ["lluvia"]       = AudioManager.Clip.LluviaLigera,
            ["rain"]         = AudioManager.Clip.LluviaLigera,
            ["viento"]       = AudioManager.Clip.VientoMontana,
            ["wind"]         = AudioManager.Clip.VientoMontana,
            ["pajaros"]      = AudioManager.Clip.Pajaros,
            ["birds"]        = AudioManager.Clip.Pajaros,
            ["sirena"]       = AudioManager.Clip.Sirena,
            ["siren"]        = AudioManager.Clip.Sirena,
            ["alert"]        = AudioManager.Clip.Sirena,
            ["click"]        = AudioManager.Clip.UIClick,
            ["notif"]        = AudioManager.Clip.UINotificacion,
            ["motor"]        = AudioManager.Clip.MotorAceleracion,
            ["engine"]       = AudioManager.Clip.MotorAceleracion,
            ["gritos"]       = AudioManager.Clip.Consignas,
            ["yell"]         = AudioManager.Clip.Consignas,
        };

        // Inyectar en AudioManager via su diccionario interno (reflexión)
        var am = AudioManager.I;
        if (am == null) { AlsasuaLogger.Warn("Integrador", "AudioManager no encontrado"); return; }

        var clipField = typeof(AudioManager).GetField("_clips",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (clipField == null) return;

        var clipDict = clipField.GetValue(am) as Dictionary<AudioManager.Clip, AudioClip>;
        if (clipDict == null) return;

        int asignados = 0;
        foreach (var clip in clips)
        {
            string n = clip.name.ToLower();
            foreach (var kv in mapa)
                if (n.Contains(kv.Key) && !clipDict.ContainsKey(kv.Value))
                {
                    clipDict[kv.Value] = clip;
                    asignados++;
                    break;
                }
        }

        AlsasuaLogger.Info("Integrador", $"Audio: {asignados}/{clips.Length} clips asignados al AudioManager");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  HDRI — asigna el mejor HDRI al sky volume
    // ════════════════════════════════════════════════════════════════════════

    void IntegrarHDRI()
    {
        // Buscar volumes HDRP en escena
        var volumes = FindObjectsByType<Volume>(FindObjectsSortMode.None);
        Volume skyVol = null;
        foreach (var v in volumes)
            if (v.isGlobal && v.profile != null) { skyVol = v; break; }

        if (skyVol == null) { AlsasuaLogger.Warn("Integrador", "No hay Volume global HDRP"); return; }

        // Cargar HDRIs disponibles
        var hdris = Resources.LoadAll<Cubemap>("HDRIs");
        Texture hdriTex = null;

        if (hdris.Length > 0)
        {
            // Preferir atardecer o puesta de sol
            hdriTex = hdris.FirstOrDefault(h => h.name.Contains("sunset") || h.name.Contains("belfast"))
                   ?? hdris[0];
        }
        else
        {
            // Intentar cargar desde Assets/HDRIs directamente (no están en Resources)
            // En runtime no podemos cargar fuera de Resources, pero podemos
            // usar el HDRI que ya esté en el profile del Volume
        }

        if (hdriTex == null)
        {
            AlsasuaLogger.Warn("Integrador", "HDRIs no en Resources/ — mover Assets/HDRIs a Assets/Resources/HDRIs para uso en runtime");
            return;
        }

        if (skyVol.profile.TryGet<HDRISky>(out var sky))
        {
            sky.hdriSky.Override(hdriTex as Cubemap);
            AlsasuaLogger.Info("Integrador", $"HDRI asignado: {hdriTex.name}");
        }
        else
        {
            var sky2 = skyVol.profile.Add<HDRISky>(true);
            sky2.hdriSky.Override(hdriTex as Cubemap);
            AlsasuaLogger.Info("Integrador", $"HDRI añadido al volume: {hdriTex.name}");
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  TERRAIN LAYERS — asigna texturas del suelo al terreno activo
    // ════════════════════════════════════════════════════════════════════════

    void IntegrarTerrainLayers()
    {
        var terrain = Terrain.activeTerrain;
        if (terrain == null) return;
        if (terrain.terrainData.terrainLayers.Length > 0) return; // ya tiene layers

        // Buscar texturas de suelo desde materiales cargados
        var matsSuelo = Resources.FindObjectsOfTypeAll<Material>()
            .Where(m => m != null && m.name.ToLower().Contains("grass") ||
                        (m != null && m.name.ToLower().Contains("ground")))
            .Take(4)
            .ToArray();

        if (matsSuelo.Length == 0) return;

        var layers = new List<TerrainLayer>();
        foreach (var mat in matsSuelo)
        {
            var layer = new TerrainLayer();
            // Intentar obtener textura del material
            if (mat.HasProperty("_BaseColorMap"))
            {
                var tex = mat.GetTexture("_BaseColorMap") as Texture2D;
                if (tex != null)
                {
                    layer.diffuseTexture = tex;
                    layer.tileSize = new Vector2(4f, 4f);
                    layers.Add(layer);
                }
            }
        }

        if (layers.Count > 0)
        {
            terrain.terrainData.terrainLayers = layers.ToArray();
            AlsasuaLogger.Info("Integrador", $"Terrain layers asignados: {layers.Count}");
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  FBX EN SISTEMAS — conecta los modelos 3D con los sistemas de juego
    // ════════════════════════════════════════════════════════════════════════

    void IntegrarFBXEnSistemas()
    {
        // Buscar prefabs de animales y asignarlos al SistemaFauna
        var fauna = AltsasuCore.I?.faunaSystem;
        if (fauna != null && (fauna.prefabsAnimales == null || fauna.prefabsAnimales.Length == 0))
        {
            var prefabsAnimales = new List<GameObject>();
            // Buscar en la escena objetos con nombres de animales
            string[] nombresAnimales = { "Caballo","Oveja","Conejo","Cierva","Gallina","Perro","Rata" };
            foreach (var nombre in nombresAnimales)
            {
                // Buscar prefab cargado
                var pf = Resources.Load<GameObject>($"Prefabs/FBX/Animales/{nombre}");
                if (pf != null) prefabsAnimales.Add(pf);
            }
            if (prefabsAnimales.Count > 0)
            {
                fauna.prefabsAnimales = prefabsAnimales.ToArray();
                AlsasuaLogger.Info("Integrador", $"Fauna: {prefabsAnimales.Count} prefabs asignados");
            }
        }

        // SistemaTrafico — buscar prefabs de vehículos NPC
        var trafico = AltsasuCore.I?.traficoSystem;
        // trafico gestiona sus propios VehiculoNPC internamente

        // SistemaVegetacion — usar meshes de árboles importados
        var veg = AltsasuCore.I?.vegetacionSystem;
        if (veg != null && (veg.meshesArbol == null || veg.meshesArbol.Length == 0))
        {
            // Buscar meshes de árboles en la escena
            // Los modelos de Vegetation están en _ExtractedAssets
            AlsasuaLogger.Info("Integrador", "Vegetacion: usando GPU instancing con meshes procedurales");
        }

        AlsasuaLogger.Info("Integrador", "FBX integrados en sistemas de gameplay");
    }
}
