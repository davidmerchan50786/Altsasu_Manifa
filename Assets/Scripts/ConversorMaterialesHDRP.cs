// Assets/Scripts/ConversorMaterialesHDRP.cs
// ═══════════════════════════════════════════════════════════════════════════
//  INGENIERO GRÁFICO — Conversión de materiales legacy a HDRP en runtime
//
//  Problema: LiteFireEffect y HQ Explosions Pack usan shaders Built-in
//  (Standard, Particles/Additive, Mobile/Particles/*) que resultan en
//  materiales magenta en HDRP.
//
//  Solución: detectar en Start() todos los ParticleSystemRenderer con
//  shaders no-HDRP y migrarlos a HDRP/Particles/Lit o HDRP/Particles/Unlit
//  según si usaban transparencia aditiva o blending estándar.
//
//  También:
//  • Asigna Textures_AAA a materiales de edificios y carreteras via HDRP/Lit
//  • Integra Wolf (HDRP prefab) en el pool de animales de SistemaSueloAAA
//  • Integra RSG_DogsPack GermanShepherd HDRP en los NPCs de calle
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DefaultExecutionOrder(100)] // después de que el mundo esté generado
public class ConversorMaterialesHDRP : MonoBehaviour
{
    public static ConversorMaterialesHDRP Instance { get; private set; }

    // ── Carpetas de assets a convertir ────────────────────────────────────
    static readonly string[] CARPETAS_FIRE = {
        "Assets/LiteFireEffect",
        "Assets/HQ Explosions Pack FREE/Materials",
    };

    // ── Shaders Built-in que deben migrarse ───────────────────────────────
    static readonly string[] SHADERS_LEGACY = {
        "Standard",
        "Legacy Shaders/Particles/Additive",
        "Legacy Shaders/Particles/Alpha Blended",
        "Mobile/Particles/Additive",
        "Particles/Additive",
        "Particles/Alpha Blended",
        "Particles/Standard Unlit",
        "Particles/Standard Surface",
        "Unlit/Transparent",
        "Unlit/Color",
    };

    // ── Cache para no procesar dos veces el mismo material ─────────────────
    readonly HashSet<int> _procesados = new();

    // ── Materiales de fuego y explosión convertidos ────────────────────────
    readonly Dictionary<Material, Material> _matConvertidos = new();

    // ── Shaders HDRP cargados una vez ─────────────────────────────────────
    Shader _hdrpParticlesLit;
    Shader _hdrpParticlesUnlit;
    Shader _hdrpLit;

    // ════════════════════════════════════════════════════════════════════════
    //  BOOT
    // ════════════════════════════════════════════════════════════════════════

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void Start()
    {
        _hdrpParticlesLit   = Shader.Find("HDRP/Particles/Lit")
                           ?? Shader.Find("HDRP/Particles/Unlit");
        _hdrpParticlesUnlit = Shader.Find("HDRP/Particles/Unlit")
                           ?? Shader.Find("HDRP/Unlit");
        _hdrpLit            = Shader.Find("HDRP/Lit")
                           ?? Shader.Find("Standard");

        StartCoroutine(ConvertirTodosLosAssets());
        StartCoroutine(IntegrarAnimalesHDRP());
        StartCoroutine(AsignarTexturesAAA());
    }

    // ════════════════════════════════════════════════════════════════════════
    //  A. CONVERSIÓN DE PARTÍCULAS LEGACY → HDRP
    // ════════════════════════════════════════════════════════════════════════

    IEnumerator ConvertirTodosLosAssets()
    {
        yield return new WaitForSeconds(1f);

        int convertidos = 0;

        // Recorrer todos los ParticleSystemRenderer activos en escena
        var renderers = FindObjectsByType<ParticleSystemRenderer>(FindObjectsSortMode.None);
        foreach (var psr in renderers)
        {
            if (psr == null) continue;
            convertidos += ConvertirRendererParticulas(psr);
            if (convertidos % 10 == 0) yield return null;
        }

        // Recorrer también MeshRenderers de efectos (HQ Explosions usa props con mesh)
        var meshRenderers = FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
        foreach (var mr in meshRenderers)
        {
            if (mr == null) continue;
            bool esEfecto = mr.gameObject.name.ToLower().Contains("explosion")
                         || mr.gameObject.name.ToLower().Contains("fire")
                         || mr.gameObject.name.ToLower().Contains("smoke");
            if (esEfecto) convertidos += ConvertirRendererMesh(mr);
        }

#if UNITY_EDITOR
        // En editor: también escanear prefabs de las carpetas de efectos
        yield return StartCoroutine(ConvertirPrefabsEnEditor());
#endif

        AlsasuaLogger.Info("ConversorHDRP", $"Materiales convertidos a HDRP: {convertidos}");
    }

    int ConvertirRendererParticulas(ParticleSystemRenderer psr)
    {
        int n = 0;
        var mats = psr.sharedMaterials;
        bool cambiado = false;

        for (int i = 0; i < mats.Length; i++)
        {
            if (mats[i] == null) continue;
            int id = mats[i].GetInstanceID();
            if (_procesados.Contains(id)) continue;

            if (EsShaderLegacy(mats[i].shader))
            {
                mats[i] = ConvertirMaterialParticula(mats[i]);
                _procesados.Add(id);
                cambiado = true;
                n++;
            }
        }

        if (cambiado) psr.sharedMaterials = mats;
        return n;
    }

    int ConvertirRendererMesh(MeshRenderer mr)
    {
        int n = 0;
        var mats = mr.sharedMaterials;
        bool cambiado = false;

        for (int i = 0; i < mats.Length; i++)
        {
            if (mats[i] == null) continue;
            int id = mats[i].GetInstanceID();
            if (_procesados.Contains(id)) continue;

            if (EsShaderLegacy(mats[i].shader))
            {
                mats[i] = ConvertirMaterialMesh(mats[i]);
                _procesados.Add(id);
                cambiado = true;
                n++;
            }
        }

        if (cambiado) mr.sharedMaterials = mats;
        return n;
    }

    bool EsShaderLegacy(Shader s)
    {
        if (s == null) return false;
        string nombre = s.name;
        foreach (var leg in SHADERS_LEGACY)
            if (nombre.StartsWith(leg)) return true;
        // También si el shader empieza por Legacy o Mobile
        return nombre.StartsWith("Legacy") || nombre.StartsWith("Mobile/");
    }

    Material ConvertirMaterialParticula(Material original)
    {
        if (_matConvertidos.TryGetValue(original, out var cached)) return cached;

        // Determinar si era aditivo (fuego/brillo) o alpha blended (humo)
        bool esAditivo = original.shader.name.Contains("Additive");
        var targetShader = esAditivo ? _hdrpParticlesUnlit : _hdrpParticlesLit;

        if (targetShader == null) return original;

        var nuevo = new Material(targetShader) { name = original.name + "_HDRP" };

        // Copiar textura principal
        var mainTex = original.mainTexture;
        if (mainTex != null)
        {
            if (nuevo.HasProperty("_BaseColorMap"))  nuevo.SetTexture("_BaseColorMap", mainTex);
            if (nuevo.HasProperty("_MainTex"))       nuevo.SetTexture("_MainTex", mainTex);
        }

        // Copiar color
        Color col = original.HasProperty("_Color") ? original.GetColor("_Color") :
                    original.HasProperty("_TintColor") ? original.GetColor("_TintColor") : Color.white;
        if (nuevo.HasProperty("_BaseColor")) nuevo.SetColor("_BaseColor", col);
        else nuevo.color = col;

        // Blend mode
        if (esAditivo)
        {
            nuevo.SetFloat("_BlendMode", 4f);    // Additive en HDRP/Particles/Unlit
            nuevo.SetFloat("_SrcBlend",  1f);    // One
            nuevo.SetFloat("_DstBlend",  1f);    // One
        }
        else
        {
            nuevo.SetFloat("_BlendMode", 1f);    // Alpha en HDRP/Particles/Lit
            nuevo.SetFloat("_SrcBlend",  5f);    // SrcAlpha
            nuevo.SetFloat("_DstBlend",  10f);   // OneMinusSrcAlpha
        }

        // Superficie transparente
        nuevo.SetFloat("_SurfaceType", 1f);  // Transparent
        nuevo.renderQueue = original.renderQueue > 0 ? original.renderQueue : 3000;
        nuevo.EnableKeyword("_ALPHAPREMULTIPLY_ON");

        _matConvertidos[original] = nuevo;
        return nuevo;
    }

    Material ConvertirMaterialMesh(Material original)
    {
        if (_matConvertidos.TryGetValue(original, out var cached)) return cached;

        var nuevo = new Material(_hdrpLit) { name = original.name + "_HDRP" };
        CopiarPropiedadesBasicas(original, nuevo);
        _matConvertidos[original] = nuevo;
        return nuevo;
    }

    void CopiarPropiedadesBasicas(Material src, Material dst)
    {
        // Albedo/Color
        var tex = src.mainTexture;
        if (tex != null)
        {
            if (dst.HasProperty("_BaseColorMap")) dst.SetTexture("_BaseColorMap", tex);
        }
        Color col = src.HasProperty("_Color") ? src.GetColor("_Color") : Color.white;
        if (dst.HasProperty("_BaseColor")) dst.SetColor("_BaseColor", col);
        else dst.color = col;

        // Metallic/Smoothness
        float metallic   = src.HasProperty("_Metallic")   ? src.GetFloat("_Metallic")   : 0f;
        float smoothness = src.HasProperty("_Smoothness")  ? src.GetFloat("_Smoothness") :
                           src.HasProperty("_Glossiness")  ? src.GetFloat("_Glossiness") : 0.5f;
        if (dst.HasProperty("_Metallic"))   dst.SetFloat("_Metallic",   metallic);
        if (dst.HasProperty("_Smoothness")) dst.SetFloat("_Smoothness", smoothness);

        // Normal map
        var normal = src.HasProperty("_BumpMap") ? src.GetTexture("_BumpMap") : null;
        if (normal != null && dst.HasProperty("_NormalMap"))
            dst.SetTexture("_NormalMap", normal);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  B. CONVERSIÓN EN EDITOR (prefabs) — solo en UNITY_EDITOR
    // ════════════════════════════════════════════════════════════════════════

#if UNITY_EDITOR
    IEnumerator ConvertirPrefabsEnEditor()
    {
        yield return null;
        foreach (var carpeta in CARPETAS_FIRE)
        {
            var guids = UnityEditor.AssetDatabase.FindAssets("t:Material", new[] { carpeta });
            foreach (var g in guids)
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
                var mat  = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null || !EsShaderLegacy(mat.shader)) continue;

                int id = mat.GetInstanceID();
                if (_procesados.Contains(id)) continue;
                _procesados.Add(id);

                // En editor, reemplazamos el shader directamente en el asset
                bool esParticula = path.Contains("Particle") || path.Contains("Fire")
                                || path.Contains("Smoke") || path.Contains("Explosion");
                if (esParticula)
                {
                    bool esAditivo = mat.shader.name.Contains("Additive");
                    var sh = esAditivo ? _hdrpParticlesUnlit : _hdrpParticlesLit;
                    if (sh != null)
                    {
                        mat.shader = sh;
                        mat.SetFloat("_SurfaceType", 1f);
                        if (esAditivo) mat.SetFloat("_BlendMode", 4f);
                        UnityEditor.EditorUtility.SetDirty(mat);
                    }
                }
                else
                {
                    if (_hdrpLit != null)
                    {
                        mat.shader = _hdrpLit;
                        UnityEditor.EditorUtility.SetDirty(mat);
                    }
                }
            }
        }
        UnityEditor.AssetDatabase.SaveAssets();
        AlsasuaLogger.Info("ConversorHDRP", "Prefabs de efectos convertidos a HDRP en editor");
    }
#else
    IEnumerator ConvertirPrefabsEnEditor() { yield break; }
#endif

    // ════════════════════════════════════════════════════════════════════════
    //  C. INTEGRACIÓN WOLF + DOGS EN ANIMALES NPC
    // ════════════════════════════════════════════════════════════════════════

    IEnumerator IntegrarAnimalesHDRP()
    {
        yield return new WaitForSeconds(3f);

        // Cargar prefab Wolf HDRP
        GameObject wolfPrefab = null;
        GameObject dogPrefab  = null;

#if UNITY_EDITOR
        wolfPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Wolf/HDRP/Wolf/Prefab/Wolf_HDRP.prefab");
        dogPrefab  = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/RSG_DogsPack/HDRP/Prefabs/P_GermanShepherd.prefab");
#endif
        wolfPrefab ??= Resources.Load<GameObject>("Wolf/Wolf_HDRP");
        dogPrefab  ??= Resources.Load<GameObject>("RSG_DogsPack/P_GermanShepherd");

        if (wolfPrefab == null && dogPrefab == null)
        {
            AlsasuaLogger.Info("ConversorHDRP", "Wolf/Dogs prefabs no encontrados en runtime — skip");
            yield break;
        }

        var terrain = Terrain.activeTerrain;
        var parentAnimales = GameObject.Find("Animales_Campo")?.transform
            ?? new GameObject("Animales_Campo").transform;

        // Spawnar manada de lobos en zona forestal norte
        if (wolfPrefab != null)
        {
            var zonaLobo = new Vector3(GeoDataAlsasua.OX - 600f, 0, GeoDataAlsasua.OZ + 450f);
            yield return StartCoroutine(SpawnManada(wolfPrefab, zonaLobo, 3, parentAnimales, terrain));
        }

        // Spawnar perros en zona periurbana
        if (dogPrefab != null)
        {
            var zonaDogs = new Vector3(GeoDataAlsasua.OX + 150f, 0, GeoDataAlsasua.OZ - 100f);
            yield return StartCoroutine(SpawnManada(dogPrefab, zonaDogs, 4, parentAnimales, terrain));
        }

        AlsasuaLogger.Info("ConversorHDRP", "Wolf y GermanShepherd HDRP integrados en escena");
    }

    IEnumerator SpawnManada(GameObject prefab, Vector3 centroZona, int n,
                            Transform parent, Terrain terrain)
    {
        for (int i = 0; i < n; i++)
        {
            float rx = centroZona.x + Random.Range(-80f, 80f);
            float rz = centroZona.z + Random.Range(-80f, 80f);
            float ry = terrain != null ? terrain.SampleHeight(new Vector3(rx, 0, rz)) : 240f;

            var animal = Object.Instantiate(prefab,
                new Vector3(rx, ry, rz),
                Quaternion.Euler(0, Random.Range(0f, 360f), 0),
                parent);

            // Añadir LOD si no lo tiene
            AgregarLODAnimal(animal);

            // Habilitar GPU Instancing en sus materiales
            HabilitarGPUInstancing(animal);

            yield return null;
        }
    }

    void AgregarLODAnimal(GameObject animal)
    {
        if (animal.GetComponent<LODGroup>() != null) return;

        var renderers = animal.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        var lod = animal.AddComponent<LODGroup>();
        // LOD0: completo < 40% screen, LOD1: solo body < 10%, culled
        var lodArr = new LOD[]
        {
            new LOD(0.04f, renderers),
            new LOD(0.008f, new Renderer[]{ renderers[0] }),
            new LOD(0f, new Renderer[0])
        };
        lod.SetLODs(lodArr);
        lod.RecalculateBounds();
    }

    void HabilitarGPUInstancing(GameObject go)
    {
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            foreach (var m in r.sharedMaterials)
            {
                if (m != null && !m.enableInstancing)
                    m.enableInstancing = true;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  D. ASIGNACIÓN TEXTURES_AAA A MATERIALES DE EDIFICIOS Y CARRETERAS
    // ════════════════════════════════════════════════════════════════════════

    IEnumerator AsignarTexturesAAA()
    {
        yield return new WaitForSeconds(4f);

#if UNITY_EDITOR
        int n = 0;

        // ── Edificios ─────────────────────────────────────────────────────
        var edificiosParent = GameObject.Find("Edificios_OSM");
        if (edificiosParent != null)
        {
            foreach (Transform edif in edificiosParent.transform)
            {
                var mr = edif?.GetComponent<MeshRenderer>();
                if (mr == null) continue;

                var mat = mr.sharedMaterial;
                if (mat == null || mat.shader.name.StartsWith("HDRP")) continue;
                // Si ya es HDRP/Lit y tiene textura, no tocar
                if (mat.HasProperty("_BaseColorMap") && mat.GetTexture("_BaseColorMap") != null) continue;

                // Asignar texturas desde Textures_AAA/Fachadas
                var nombre = mat.name.Replace("_HDRP","").Replace(" (Instance)","");
                AsignarTexturaAAA(mat, nombre, "Fachadas");
                n++;
                if (n % 30 == 0) yield return null;
            }
        }

        // ── Carreteras ────────────────────────────────────────────────────
        var callesParent = GameObject.Find("Calles_Precisas") ?? GameObject.Find("Calles_OSM");
        if (callesParent != null)
        {
            foreach (Transform calle in callesParent.transform)
            {
                var mr = calle?.GetComponent<MeshRenderer>();
                if (mr == null) continue;
                string nombre = calle.name.ToLower().Contains("pedestrian") ? "cobblestone_01" : "asphalt_01";
                AsignarTexturaAAA(mr.sharedMaterial, nombre, "Suelo");
                n++;
                if (n % 50 == 0) yield return null;
            }
        }

        AlsasuaLogger.Info("ConversorHDRP", $"Textures_AAA asignadas a {n} materiales");
#else
        yield break;
#endif
    }

#if UNITY_EDITOR
    void AsignarTexturaAAA(Material mat, string nombreBase, string subcarpeta)
    {
        if (mat == null) return;

        // Intentar cargar Albedo, Normal, ARM desde Textures_AAA
        string basePath = $"Assets/Textures_AAA/{subcarpeta}/{nombreBase}";

        // Albedo
        var albedo = CargarTextura(basePath + "_Albedo.png")
                  ?? CargarTextura(basePath + "_diffuse.png")
                  ?? CargarTextura(basePath + "_BaseColor.png");
        if (albedo != null && mat.HasProperty("_BaseColorMap"))
            mat.SetTexture("_BaseColorMap", albedo);

        // Normal
        var normal = CargarTextura(basePath + "_Normal.png");
        if (normal != null && mat.HasProperty("_NormalMap"))
        {
            mat.SetTexture("_NormalMap", normal);
            mat.SetFloat("_NormalScale", 1.0f);
        }

        // ARM (AmbientOcclusion/Roughness/Metallic combinado)
        var arm = CargarTextura(basePath + "_ARM.png");
        if (arm != null && mat.HasProperty("_MaskMap"))
            mat.SetTexture("_MaskMap", arm);

        // Roughness por separado si no hay ARM
        if (arm == null)
        {
            var roughness = CargarTextura(basePath + "_Roughness.png");
            if (roughness != null && mat.HasProperty("_SmoothnessRemapMax"))
                mat.SetFloat("_SmoothnessRemapMax", 1f);
        }

        if (albedo != null || normal != null || arm != null)
            UnityEditor.EditorUtility.SetDirty(mat);
    }

    Texture2D CargarTextura(string path)
        => UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(path);
#endif
}
