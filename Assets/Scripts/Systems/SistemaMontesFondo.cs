// Assets/Scripts/SistemaMontesFondo.cs
// ═══════════════════════════════════════════════════════════════════════════
//  MONTES DE FONDO — anillo de cumbres en el horizonte de Alsasua
//
//  PROBLEMA que resuelve:
//    El terreno jugable cubre ~3 km de radio y la cámara tenía far clip 2 km,
//    así que el horizonte quedaba vacío (cielo HDRI), sin la silueta de las
//    sierras que enmarcan el valle (Urbasa/Andia al N, Aralar al S, Aizkorri/
//    Altzania al E). Este sistema coloca un anillo de mallas de montaña MÁS
//    ALLÁ del área jugable, sube el far clip y ajusta la niebla para que se
//    vean al fondo con perspectiva atmosférica, sin coste en el gameplay.
//
//  Las cumbres son decorativas: sin collider, estáticas, lejos del jugador.
//  Todo está parametrizado — ajusta escala/altura/radio en el Inspector
//  mirando el resultado en Unity (no se puede calcular el tamaño exacto de la
//  malla importada sin verla).
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[DefaultExecutionOrder(210)]   // tras terreno y cámara
public class SistemaMontesFondo : MonoBehaviour
{
    public static SistemaMontesFondo Instance { get; private set; }

    [Header("Anillo de montes")]
    [Tooltip("Radio del anillo desde el centro del pueblo (m). Debe superar el borde del terreno jugable (~3 km).")]
    [SerializeField] float radioAnillo      = 7000f;
    [Tooltip("Variación aleatoria del radio por cumbre (m).")]
    [SerializeField] float radioVariacion   = 1800f;
    [Tooltip("Número de cumbres alrededor de los 360°.")]
    [SerializeField] int   numMontes        = 30;

    [Header("Escala / altura (AJUSTAR EN UNITY mirando el resultado)")]
    [Tooltip("Escala base de la malla de montaña. La malla importada no tiene tamaño conocido — sube/baja esto hasta que las cumbres se vean grandes al fondo.")]
    [SerializeField] float escalaBase       = 900f;
    [Tooltip("Variación de escala (0.3 = ±30%).")]
    [SerializeField, Range(0f, 0.8f)] float escalaVariacion = 0.4f;
    [Tooltip("Altura Y de la BASE de las cumbres (mundo). Hundidas un poco para que solo asome la cima sobre el horizonte.")]
    [SerializeField] float alturaBaseY      = 120f;
    [Tooltip("Variación de altura Y por cumbre (m).")]
    [SerializeField] float alturaVariacion  = 120f;

    [Header("Cámara / niebla")]
    [Tooltip("Sube el far clip de la cámara para que el anillo se renderice (los montes están a varios km).")]
    [SerializeField] float farClip          = 16000f;
    [Tooltip("Si true, relaja la niebla HDRP para que las cumbres asomen con bruma (perspectiva atmosférica).")]
    [SerializeField] bool  ajustarNiebla    = true;
    [Tooltip("meanFreePath de la niebla (m). Mayor = se ve más lejos. ~6000 deja el valle con bruma pero muestra los montes.")]
    [SerializeField] float nieblaMeanFreePath = 6500f;

    [Header("Orientación a las sierras reales")]
    [Tooltip("Si true, sesga la altura por dirección imitando las sierras: más altas al N (Urbasa/Andia), S (Aralar) y E (Aizkorri/Altzania).")]
    [SerializeField] bool  sesgarPorSierra  = true;

    [Header("Material (la malla es de un pack 2016 — se reemplaza por HDRP/Lit en runtime)")]
    [Tooltip("Tinte sobre el albedo nevado para que lea como sierra vasca (verde-grisáceo). Blanco = nieve original.")]
    [SerializeField] Color tinteSierra      = new Color(0.45f, 0.52f, 0.40f);
    [SerializeField, Range(0f, 1f)] float smoothness = 0.05f;

    readonly List<GameObject> _montes = new();
    Transform _ring;
    Material  _matSierra;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    IEnumerator Start()
    {
        // Esperar a que exista terreno (para muestrear cota) y cámara.
        float t = 0f;
        while (Terrain.activeTerrain == null && t < 15f) { t += 0.5f; yield return new WaitForSeconds(0.5f); }

        var prefab = Resources.Load<GameObject>("Prefabs/Montes/mountain_Snow_000")
                  ?? CargarPrimerMonte();
        if (prefab == null)
        {
            AlsasuaLogger.Warn("MontesFondo", "No hay malla de montaña en Resources/Prefabs/Montes — sistema inactivo.");
            yield break;
        }

        SubirFarClip();
        if (ajustarNiebla) RelajarNiebla();
        _matSierra = CrearMaterialSierra();
        ConstruirAnillo(prefab);

        AlsasuaLogger.Info("MontesFondo",
            $"{_montes.Count} cumbres colocadas en anillo r={radioAnillo:F0}m · farClip={farClip:F0}m");
    }

    GameObject CargarPrimerMonte()
    {
        var todos = Resources.LoadAll<GameObject>("Prefabs/Montes");
        return todos.Length > 0 ? todos[0] : null;
    }

    void ConstruirAnillo(GameObject prefab)
    {
        var centro = new Vector3(GeoDataAlsasua.OX, 0f, GeoDataAlsasua.OZ);
        _ring = new GameObject("MontesFondo_Ring").transform;
        _ring.position = centro;

        for (int i = 0; i < numMontes; i++)
        {
            float ang = (i / (float)numMontes) * Mathf.PI * 2f
                      + Random.Range(-0.05f, 0.05f);          // leve irregularidad
            float r   = radioAnillo + Random.Range(-radioVariacion, radioVariacion);

            Vector3 pos = centro + new Vector3(Mathf.Sin(ang) * r, 0f, Mathf.Cos(ang) * r);

            // Altura: base hundida + variación, con sesgo por sierra real si procede.
            float sesgo = sesgarPorSierra ? SesgoSierra(ang) : 0f;
            pos.y = alturaBaseY + Random.Range(-alturaVariacion, alturaVariacion) + sesgo;

            var m = Instantiate(prefab, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), _ring);
            float esc = escalaBase * (1f + Random.Range(-escalaVariacion, escalaVariacion))
                      * (1f + sesgo / 800f);                  // las sierras altas, un poco más grandes
            m.transform.localScale = Vector3.one * esc;
            m.name = $"Monte_{i}";
            m.isStatic = true;

            DepurarParaFondo(m);
            _montes.Add(m);
        }
    }

    // Sesga la altura según la dirección, imitando las sierras que rodean Alsasua.
    // Ángulo 0 = +Z (Norte aprox en coords Unity del proyecto).
    //   N  (Urbasa/Andia)  → alto
    //   S  (Aralar)        → alto
    //   E  (Aizkorri)      → muy alto
    //   O                  → más bajo (corredor del valle)
    float SesgoSierra(float ang)
    {
        float norte = Mathf.Cos(ang);           // +1 al N, -1 al S
        float este  = Mathf.Sin(ang);           // +1 al E, -1 al O
        float altN  = Mathf.Max(0f,  norte) * 220f;   // Urbasa/Andia
        float altS  = Mathf.Max(0f, -norte) * 260f;   // Aralar
        float altE  = Mathf.Max(0f,  este ) * 340f;   // Aizkorri/Altzania (las más altas)
        float bajO  = Mathf.Max(0f, -este ) * -120f;  // corredor O más bajo
        return altN + altS + altE + bajO;
    }

    // Quita colliders (decorativo), baja el coste (sin sombras) y aplica material HDRP.
    void DepurarParaFondo(GameObject m)
    {
        foreach (var c in m.GetComponentsInChildren<Collider>(true)) Destroy(c);
        foreach (var r in m.GetComponentsInChildren<Renderer>(true))
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (_matSierra != null)
            {
                var mats = r.sharedMaterials;
                for (int k = 0; k < mats.Length; k++) mats[k] = _matSierra;
                r.sharedMaterials = mats;
            }
        }
    }

    // El FBX trae material Standard 2016 (magenta en HDRP). Construimos HDRP/Lit
    // con las texturas del pack y tinte de sierra vasca. Un solo material
    // compartido + GPU instancing = todo el anillo en pocos draw calls.
    Material CrearMaterialSierra()
    {
        var shader = Shader.Find("HDRP/Lit");
        if (shader == null)
        {
            AlsasuaLogger.Warn("MontesFondo", "Shader HDRP/Lit no encontrado — los montes usarán el material importado.");
            return null;
        }

        var mat = new Material(shader) { name = "M_SierraFondo (runtime)" };
        var albedo = Resources.Load<Texture2D>("Textures/Montes/mountain_Snow_000_Aldedo");
        var normal = Resources.Load<Texture2D>("Textures/Montes/mountain_Snow_000_Normal");

        if (albedo != null) mat.SetTexture("_BaseColorMap", albedo);
        if (normal != null)
        {
            mat.SetTexture("_NormalMap", normal);
            mat.SetFloat("_NormalScale", 1f);
            mat.EnableKeyword("_NORMALMAP");
            mat.EnableKeyword("_NORMALMAP_TANGENT_SPACE");
        }
        mat.SetColor("_BaseColor", tinteSierra);
        mat.SetFloat("_Smoothness", smoothness);
        mat.SetFloat("_Metallic", 0f);
        mat.enableInstancing = true;
        return mat;
    }

    void SubirFarClip()
    {
        var cam = Camera.main;
        if (cam != null && cam.farClipPlane < farClip)
            cam.farClipPlane = farClip;
    }

    // Relaja la niebla HDRP global para que el anillo asome con bruma.
    void RelajarNiebla()
    {
        foreach (var v in FindObjectsByType<UnityEngine.Rendering.Volume>(FindObjectsSortMode.None))
        {
            if (v.profile == null) continue;
            if (v.profile.TryGet(out UnityEngine.Rendering.HighDefinition.Fog fog))
            {
                fog.meanFreePath.overrideState = true;
                fog.meanFreePath.value = nieblaMeanFreePath;
            }
        }
    }
}
