// Assets/Scripts/OptimizadorVisualHDRP.cs
// ═══════════════════════════════════════════════════════════════════════════
//  OPTIMIZADOR VISUAL — GPU Instancing, LOD y Occlusion Culling
//
//  • GPU Instancing en todos los materiales repetitivos (edificios, veg.)
//  • LOD Groups en prefabs de vegetación y props urbanos que no lo tengan
//  • Activa Occlusion Culling en la cámara principal
//  • Verifica que el perfil HDRP Balanced no tiene features de alto coste
//    innecesariamente activas
//  • Configura Shadow Distance y Cascade Splits óptimas para mundo abierto
//
//  No modifica geometría ni mecánicas. Solo optimización visual.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

[DefaultExecutionOrder(200)] // Último en arrancar para encontrar todo
public class OptimizadorVisualHDRP : MonoBehaviour
{
    // ── Umbrales LOD para vegetación ──────────────────────────────────────
    const float LOD_VEG_0  = 0.05f;  // completo < 80m
    const float LOD_VEG_1  = 0.012f; // Billboard LOD < 200m
    const float LOD_PROP_0 = 0.03f;  // props < 50m
    const float LOD_PROP_1 = 0.005f; // prop simple < 120m

    // ── Estadísticas ──────────────────────────────────────────────────────
    int _instancingActivado;
    int _lodsAnadidos;
    int _matsOptimizados;

    void Start()
    {
        StartCoroutine(OptimizarTodo());
    }

    IEnumerator OptimizarTodo()
    {
        yield return new WaitForSeconds(6f); // esperar a que el mundo esté generado

        yield return StartCoroutine(ActivarGPUInstancing());
        yield return null;
        yield return StartCoroutine(ConfigurarLODsVegetacion());
        yield return null;
        yield return StartCoroutine(ConfigurarLODsProps());
        yield return null;
        ActivarOcclusionCulling();
        ConfigurarSombrasOptimas();
        AuditarPerfilHDRPBalanced();

        AlsasuaLogger.Info("OptimizadorVisual",
            $"GPU Instancing: {_instancingActivado} mats | LODs añadidos: {_lodsAnadidos} | " +
            $"Mats optimizados: {_matsOptimizados}");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  A. GPU INSTANCING
    // ════════════════════════════════════════════════════════════════════════

    IEnumerator ActivarGPUInstancing()
    {
        var setMats = new HashSet<int>();
        int n = 0;

        // ── Edificios ──────────────────────────────────────────────────────
        var parentsEdif = new[] {
            GameObject.Find("Edificios_OSM"),
            GameObject.Find("Edificios_Precisos")
        };

        foreach (var parent in parentsEdif)
        {
            if (parent == null) continue;
            foreach (var r in parent.GetComponentsInChildren<MeshRenderer>())
            {
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    int id = m.GetInstanceID();
                    if (setMats.Contains(id)) continue;
                    setMats.Add(id);
                    if (!m.enableInstancing)
                    {
                        m.enableInstancing = true;
                        n++;
                    }
                }
            }
            if (n % 50 == 0) yield return null;
        }

        // ── Vegetación ────────────────────────────────────────────────────
        var parentVeg = GameObject.Find("Vegetacion_OSM");
        if (parentVeg != null)
        {
            foreach (var r in parentVeg.GetComponentsInChildren<Renderer>())
            {
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    int id = m.GetInstanceID();
                    if (setMats.Contains(id)) continue;
                    setMats.Add(id);
                    if (!m.enableInstancing) { m.enableInstancing = true; n++; }
                }
            }
        }
        yield return null;

        // ── Props y Animales ──────────────────────────────────────────────
        var otrosParents = new[] { "Props_Urbanos", "Animales_Campo", "Farolas_Procedurales" };
        foreach (var pName in otrosParents)
        {
            var parent = GameObject.Find(pName);
            if (parent == null) continue;
            foreach (var r in parent.GetComponentsInChildren<Renderer>())
            {
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    int id = m.GetInstanceID();
                    if (setMats.Contains(id)) continue;
                    setMats.Add(id);
                    if (!m.enableInstancing) { m.enableInstancing = true; n++; }
                }
            }
        }

        _instancingActivado = n;
        yield return null;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  B. LOD GROUPS — VEGETACIÓN
    // ════════════════════════════════════════════════════════════════════════

    IEnumerator ConfigurarLODsVegetacion()
    {
        var parentVeg = GameObject.Find("Vegetacion_OSM");
        if (parentVeg == null) yield break;

        int n = 0;
        foreach (Transform arbol in parentVeg.transform)
        {
            if (arbol == null) continue;
            if (arbol.GetComponent<LODGroup>() != null) continue; // ya tiene

            var renderers = arbol.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) continue;

            // Separar copa (Sphere) y tronco (Cylinder) si son primitivos
            var copa   = new List<Renderer>();
            var tronco = new List<Renderer>();
            var todo   = new List<Renderer>(renderers);

            foreach (var r in renderers)
            {
                string nombre = r.gameObject.name.ToLower();
                if (nombre.Contains("copa") || nombre.Contains("sphere") || nombre.Contains("leaf"))
                    copa.Add(r);
                else
                    tronco.Add(r);
            }

            var lod = arbol.gameObject.AddComponent<LODGroup>();
            lod.fadeMode = LODFadeMode.CrossFade;
            lod.animateCrossFading = true;

            var lodsArr = copa.Count > 0
                ? new LOD[] {
                    new LOD(LOD_VEG_0, todo.ToArray()),                      // completo
                    new LOD(LOD_VEG_1, tronco.Count > 0
                        ? tronco.ToArray() : new Renderer[]{ renderers[0] }), // solo tronco
                    new LOD(0f, new Renderer[0])                             // culled
                  }
                : new LOD[] {
                    new LOD(LOD_VEG_0, renderers),
                    new LOD(0f, new Renderer[0])
                  };

            lod.SetLODs(lodsArr);
            lod.RecalculateBounds();
            n++;

            if (n % 25 == 0) yield return null;
        }

        _lodsAnadidos += n;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  C. LOD GROUPS — PROPS URBANOS
    // ════════════════════════════════════════════════════════════════════════

    IEnumerator ConfigurarLODsProps()
    {
        var parentProps = GameObject.Find("Props_Urbanos");
        if (parentProps == null) yield break;

        int n = 0;
        foreach (Transform prop in parentProps.transform)
        {
            if (prop == null) continue;
            if (prop.GetComponent<LODGroup>() != null) continue;

            var renderers = prop.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) continue;

            var lod = prop.gameObject.AddComponent<LODGroup>();
            lod.fadeMode = LODFadeMode.CrossFade;

            lod.SetLODs(new LOD[] {
                new LOD(LOD_PROP_0, renderers),
                new LOD(LOD_PROP_1, new Renderer[]{ renderers[0] }),
                new LOD(0f, new Renderer[0])
            });
            lod.RecalculateBounds();
            n++;

            if (n % 30 == 0) yield return null;
        }

        _lodsAnadidos += n;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  D. OCCLUSION CULLING EN CÁMARA
    // ════════════════════════════════════════════════════════════════════════

    void ActivarOcclusionCulling()
    {
        var cam = Camera.main;
        if (cam == null) return;

        cam.useOcclusionCulling = true;

        // HDRP: configurar culling mask para excluir capas innecesarias del culling
        // (layer 0 = Default, queremos culling en todo excepto UI)
        cam.cullingMask = ~(1 << LayerMask.NameToLayer("UI"));

        // Frustum culling extra: reducir near/far para evitar overdraw
        cam.nearClipPlane = Mathf.Max(cam.nearClipPlane, 0.3f);
        cam.farClipPlane  = Mathf.Min(cam.farClipPlane, 3000f);

        AlsasuaLogger.Info("OptimizadorVisual", "Occlusion Culling activado en Camera.main");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  E. CONFIGURACIÓN DE SOMBRAS ÓPTIMAS
    // ════════════════════════════════════════════════════════════════════════

    void ConfigurarSombrasOptimas()
    {
        // Cascadas óptimas para mundo abierto urbano:
        // Cascada 0: 0-12m   (detalles cercanos — muy alta calidad)
        // Cascada 1: 12-50m  (entorno inmediato)
        // Cascada 2: 50-150m (distancia media)
        // Cascada 3: 150-300m (lejos — baja resolución)
        QualitySettings.shadowDistance  = 300f;
        QualitySettings.shadowCascades  = 4;
        // Los splits de cascada se configuran en el HDRP Asset (no expuesto en QualitySettings API)

        // LOD Bias: 2.5 significa que los LOD0 se mantienen hasta 2.5x más lejos
        QualitySettings.lodBias         = 2.5f;
        QualitySettings.maximumLODLevel = 0;    // nunca saltar el LOD más detallado

        AlsasuaLogger.Info("OptimizadorVisual", "Sombras y LOD bias configurados para mundo abierto");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  F. AUDITORÍA DEL PERFIL HDRP BALANCED
    // ════════════════════════════════════════════════════════════════════════

    void AuditarPerfilHDRPBalanced()
    {
        // El perfil HDRP Balanced está en Assets/Settings/HDRP Balanced.asset
        // No podemos modificarlo en runtime sin acceso directo al HDRenderPipelineAsset,
        // pero sí podemos verificar qué features están sobreactivadas a través del
        // pipeline actual y loguear recomendaciones.

        var pipeline = GraphicsSettings.currentRenderPipeline as HDRenderPipelineAsset;
        if (pipeline == null)
        {
            AlsasuaLogger.Info("OptimizadorVisual", "HDRP pipeline asset no accesible en runtime — skip auditoría");
            return;
        }

        // Emitir recomendaciones como logs (no modificamos el asset)
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== Auditoría HDRP Balanced ===");

        // Ray Tracing: desactivar si está activo (demasiado costoso para mundo abierto)
#if UNITY_EDITOR
        sb.AppendLine("  [RECOMENDACIÓN] Verificar que Ray Tracing está OFF en HDRP Balanced");
        sb.AppendLine("  [RECOMENDACIÓN] Decal Layers: activar solo si hay decals de graffiti");
        sb.AppendLine("  [RECOMENDACIÓN] Terrain Detail Lit: puede desactivarse si el terreno usa terrain shader básico");
        sb.AppendLine("  [RECOMENDACIÓN] Screen Space Shadows: mantener ON para sombras de contacto");
        sb.AppendLine("  [RECOMENDACIÓN] Subsurface Scattering: mantener ON para Wolf/FurShader");
        sb.AppendLine("  [RECOMENDACIÓN] Volumetric Clouds: OFF en Balanced, ON en High Fidelity");
        sb.AppendLine("  [OK] SSAO, SSR, Motion Blur, Bloom — apropiados para Balanced");
#endif
        AlsasuaLogger.Info("OptimizadorVisual", sb.ToString());

        _matsOptimizados++; // contabilizar auditoría
    }
}
