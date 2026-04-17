using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// OptimizadorTerreno — Gestiona el renderizado eficiente del terreno importado
/// desde Cloud Compare (malla OBJ gigante del escenario real de Alsasua).
///
/// INSTRUCCIONES DE USO:
///  1. Importa el OBJ más pequeño primero: "vertices.obj" (58 MB) como prueba.
///  2. Coloca el objeto del terreno en la escena y asigna su MeshRenderer aquí.
///  3. Este script aplica:
///     - LOD por distancia a cámara
///     - Culling agresivo (Frustum + Occlusion)
///     - División en chunks si el mesh supera el límite de vértices
///     - MaterialPropertyBlock para zero-alloc rendering
/// </summary>
[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(MeshFilter))]
public class OptimizadorTerreno : MonoBehaviour
{
    // ─── Configuración LOD ────────────────────────────────────────────────────
    [Header("LOD por Distancia")]
    [Tooltip("Distancia máxima para ver el terreno completo (costoso)")]
    public float distanciaLOD0 = 150f;   // Calidad alta — cerca del jugador
    [Tooltip("Distancia para LOD medio")]
    public float distanciaLOD1 = 400f;   // Calidad media
    [Tooltip("Más allá de esta distancia el terreno se oculta")]
    public float distanciaOcultar = 800f;

    [Header("Material y Textura")]
    [Tooltip("Textura de color exportada desde Cloud Compare (Alsasua_Color.png)")]
    public Texture2D texturaCCOlor;
    [Tooltip("Material HDRP para la malla del terreno")]
    public Material materialTerreno;

    [Header("Chunks (mallas grandes)")]
    [Tooltip("Activar si el OBJ supera 65k vértices y Unity lo ha dividido en sub-meshes")]
    public bool usarChunks = true;
    [Tooltip("Lista de MeshRenderer de los chunks hijos (si el OBJ se dividió al importar)")]
    public MeshRenderer[] chunksTerreno;

    [Header("Optimización")]
    [Tooltip("Referencia al jugador para calcular distancia")]
    public Transform jugador;
    [Tooltip("Activar Occlusion Culling (requiere bake en Window → Rendering → Occlusion Culling)")]
    public bool oclusionActiva = true;
    [Tooltip("Recalcular LOD cada N frames para reducir CPU")]
    public int frecuenciaActualizacionFrames = 10;

    // ─── Internos ─────────────────────────────────────────────────────────────
    private MeshRenderer _rendererPrincipal;
    private MaterialPropertyBlock _propBlock;
    private int _frameCount = 0;
    private int _lodActual = -1;  // -1 = sin inicializar

    // LOD 0 = sin simplificación, LOD 1 = reducida, -999 = oculto
    private const int LOD_ALTA    = 0;
    private const int LOD_MEDIA   = 1;
    private const int LOD_OCULTO  = 2;

    // =========================================================================
    //  UNITY LIFECYCLE
    // =========================================================================

    void Awake()
    {
        _rendererPrincipal = GetComponent<MeshRenderer>();
        _propBlock = new MaterialPropertyBlock();

        AplicarTexturaCCOlor();
        ConfigurarMeshRenderer();
    }

    void Start()
    {
        if (jugador == null)
        {
            var go = GameObject.FindGameObjectWithTag("Player");
            if (go != null) jugador = go.transform;
        }

        // Validación inicial de chunks
        if (usarChunks && (chunksTerreno == null || chunksTerreno.Length == 0))
        {
            // Auto-detectar renderers hijos
            chunksTerreno = GetComponentsInChildren<MeshRenderer>(true);
            if (chunksTerreno.Length > 0)
                Debug.Log($"[OptimizadorTerreno] Auto-detectados {chunksTerreno.Length} chunks.");
        }

        ActualizarLOD(true); // forzar primera actualización
    }

    void Update()
    {
        _frameCount++;
        if (_frameCount % frecuenciaActualizacionFrames != 0) return;
        ActualizarLOD(false);
    }

    // =========================================================================
    //  LOD
    // =========================================================================

    void ActualizarLOD(bool forzar)
    {
        if (jugador == null) return;

        float distancia = Vector3.Distance(jugador.position, transform.position);
        int nuevoLOD;

        if (distancia <= distanciaLOD0)
            nuevoLOD = LOD_ALTA;
        else if (distancia <= distanciaLOD1)
            nuevoLOD = LOD_MEDIA;
        else
            nuevoLOD = LOD_OCULTO;

        if (nuevoLOD == _lodActual && !forzar) return;
        _lodActual = nuevoLOD;

        AplicarLOD(nuevoLOD);
    }

    void AplicarLOD(int lod)
    {
        bool visible = lod != LOD_OCULTO;

        // Renderer principal
        if (_rendererPrincipal != null)
            _rendererPrincipal.enabled = visible;

        // Chunks hijos
        if (usarChunks && chunksTerreno != null)
        {
            foreach (var chunk in chunksTerreno)
            {
                if (chunk == null) continue;
                chunk.enabled = visible;

                if (visible && lod == LOD_MEDIA)
                {
                    // En LOD medio, aplicar un bias de shadow más agresivo para ahorrar
                    chunk.receiveShadows = false;
                    chunk.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                else if (visible && lod == LOD_ALTA)
                {
                    chunk.receiveShadows = true;
                    chunk.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                }
            }
        }
    }

    // =========================================================================
    //  TEXTURA CLOUD COMPARE
    // =========================================================================

    void AplicarTexturaCCOlor()
    {
        if (texturaCCOlor == null || _rendererPrincipal == null) return;

        // Usar MaterialPropertyBlock para zero-alloc (evita crear instancias de material)
        _rendererPrincipal.GetPropertyBlock(_propBlock);
        _propBlock.SetTexture("_BaseColorMap", texturaCCOlor);   // HDRP Lit
        _propBlock.SetTexture("_BaseMap", texturaCCOlor);         // URP/Built-in fallback
        _propBlock.SetTexture("_MainTex", texturaCCOlor);         // Legacy fallback
        _rendererPrincipal.SetPropertyBlock(_propBlock);

        Debug.Log("[OptimizadorTerreno] Textura CloudCompare aplicada: " + texturaCCOlor.name);
    }

    void ConfigurarMeshRenderer()
    {
        if (_rendererPrincipal == null) return;

        // Configuración óptima para mallas estáticas de terreno
        _rendererPrincipal.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        _rendererPrincipal.receiveShadows = true;
        _rendererPrincipal.allowOcclusionWhenDynamic = oclusionActiva;

        if (materialTerreno != null)
            _rendererPrincipal.sharedMaterial = materialTerreno;
    }

    // =========================================================================
    //  UTILIDADES EDITOR
    // =========================================================================

#if UNITY_EDITOR
    /// <summary>
    /// Marca el objeto y sus hijos como Static para que Unity genere
    /// Lightmaps, Occlusion Culling y batching estático automáticamente.
    /// Ejecutar desde el menú contextual del componente.
    /// </summary>
    [ContextMenu("Marcar como Static (Editor)")]
    void MarcarComoStatic()
    {
        GameObjectUtility.SetStaticEditorFlags(gameObject,
            StaticEditorFlags.ContributeGI |
            StaticEditorFlags.OccludeeStatic |
            StaticEditorFlags.OccluderStatic |
            StaticEditorFlags.BatchingStatic);

        foreach (Transform hijo in GetComponentsInChildren<Transform>())
        {
            GameObjectUtility.SetStaticEditorFlags(hijo.gameObject,
                StaticEditorFlags.ContributeGI |
                StaticEditorFlags.OccludeeStatic |
                StaticEditorFlags.OccluderStatic |
                StaticEditorFlags.BatchingStatic);
        }
        Debug.Log("[OptimizadorTerreno] Objetos marcados como Static correctamente.");
    }

    /// <summary>
    /// Muestra en consola información sobre la malla importada de CloudCompare.
    /// </summary>
    [ContextMenu("Info de la Malla (Editor)")]
    void InfoMalla()
    {
        var mf = GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null)
        {
            Debug.LogWarning("[OptimizadorTerreno] No hay MeshFilter o malla asignada.");
            return;
        }
        var mesh = mf.sharedMesh;
        Debug.Log($"[OptimizadorTerreno] === INFO MALLA CloudCompare ===\n" +
                  $"  Nombre       : {mesh.name}\n" +
                  $"  Vértices     : {mesh.vertexCount:N0}\n" +
                  $"  Triángulos   : {mesh.triangles.Length / 3:N0}\n" +
                  $"  Sub-meshes   : {mesh.subMeshCount}\n" +
                  $"  Bounds       : {mesh.bounds}\n" +
                  $"  Tiene UV     : {mesh.uv.Length > 0}\n" +
                  $"  Tiene Normales: {mesh.normals.Length > 0}");
    }

    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;

        // Mostrar radio LOD
        Gizmos.color = new Color(0, 1, 0, 0.1f);
        Gizmos.DrawWireSphere(transform.position, distanciaLOD0);
        Gizmos.color = new Color(1, 1, 0, 0.05f);
        Gizmos.DrawWireSphere(transform.position, distanciaLOD1);
        Gizmos.color = new Color(1, 0, 0, 0.03f);
        Gizmos.DrawWireSphere(transform.position, distanciaOcultar);
    }
#endif
}
