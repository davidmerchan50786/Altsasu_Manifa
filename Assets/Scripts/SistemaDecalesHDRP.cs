// Assets/Scripts/SistemaDecalesHDRP.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SISTEMA DE DECALES HDRP — impactos persistentes AAA
//
//  Problema que resuelve:
//    En GTA-style AAA, los impactos de bala, manchas de sangre, pintadas
//    y marcas de neumáticos PERSISTEN en las superficies. Sin este sistema,
//    cada impacto desaparece al redibujar el frame → feedback pobre.
//
//  Implementación:
//    · Pool de 128 decal projectors (HDRP DecalProjector)
//    · 5 tipos: BalaMetalica, BalaConcreto, BalaAsfalto, Sangre, GrafitiSmall
//    · Cada decal tiene tiempo de vida configurable (0 = permanente)
//    · El pool recicla el decal más antiguo cuando está lleno
//    · Fade-in rápido (0.1s) + fade-out suave al expirar
//
//  Uso:
//    SistemaDecalesHDRP.SpawnDecal(DecalTipo.BalaConcreto, pos, normal);
//    SistemaDecalesHDRP.SpawnGrafiti(pos, normal, color);
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

[DefaultExecutionOrder(-55)]
public class SistemaDecalesHDRP : MonoBehaviour
{
    public static SistemaDecalesHDRP Instance { get; private set; }

    public enum DecalTipo { BalaConcreto, BalaMetalica, BalaAsfalto, Sangre, GrafitiSmall }

    [Header("Pool")]
    [Tooltip("Tamaño del pool de decales. Al llenarse, se recicla el más antiguo.")]
    public int tamanoPool = 128;
    [Tooltip("Tiempo de vida de cada decal en segundos. 0 = permanente.")]
    public float vidaDecal = 120f;

    // ── Pool ──────────────────────────────────────────────────────────────
    struct Decal
    {
        public DecalProjector projector;
        public float          tiempoNacimiento;
        public bool           enUso;
    }

    Decal[]       _pool;
    int           _cursor;   // índice circular para reciclar
    Material[]    _materiales;

    // IDs de propiedades HDRP
    static readonly int ID_BaseColor   = Shader.PropertyToID("_BaseColor");
    static readonly int ID_FadeAlpha   = Shader.PropertyToID("_Opacity");

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void Start()
    {
        CrearMateriales();
        CrearPool();
        StartCoroutine(CicloFade());
        AlsasuaLogger.Info("Decales", $"Pool de {tamanoPool} decales HDRP listo.");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  MATERIALES POR TIPO
    // ════════════════════════════════════════════════════════════════════════

    void CrearMateriales()
    {
        _materiales = new Material[(int)DecalTipo.GrafitiSmall + 1];
        var sh = Shader.Find("HDRP/Decal");
        if (sh == null)
        {
            AlsasuaLogger.Warn("Decales", "Shader HDRP/Decal no encontrado — sistema en modo degradado.");
            sh = Shader.Find("Standard");
        }

        // BalaConcreto — crater gris oscuro
        _materiales[(int)DecalTipo.BalaConcreto] = CrearMatDecal(sh, "Decal_BalaConcreto",
            new Color(0.28f, 0.26f, 0.24f, 0.90f), 0.06f, 0.06f);

        // BalaMetalica — rayadura plateada con quemado
        _materiales[(int)DecalTipo.BalaMetalica] = CrearMatDecal(sh, "Decal_BalaMetalica",
            new Color(0.18f, 0.16f, 0.14f, 0.85f), 0.08f, 0.08f);

        // BalaAsfalto — mancha oscura de asfalto pulverizado
        _materiales[(int)DecalTipo.BalaAsfalto] = CrearMatDecal(sh, "Decal_BalaAsfalto",
            new Color(0.12f, 0.11f, 0.10f, 0.80f), 0.07f, 0.07f);

        // Sangre — rojo oscuro orgánico
        _materiales[(int)DecalTipo.Sangre] = CrearMatDecal(sh, "Decal_Sangre",
            new Color(0.55f, 0.05f, 0.04f, 0.88f), 0.10f, 0.10f);

        // GrafitiSmall — cuadrado genérico rojo; el color se sobreescribe desde SpawnGrafiti
        _materiales[(int)DecalTipo.GrafitiSmall] = CrearMatDecal(sh, "Decal_Grafiti",
            new Color(0.85f, 0.10f, 0.08f, 0.95f), 0.25f, 0.25f);
    }

    static Material CrearMatDecal(Shader sh, string nombre, Color color, float w, float h)
    {
        var m = new Material(sh) { name = nombre };
        if (m.HasProperty(ID_BaseColor))  m.SetColor(ID_BaseColor, color);
        else m.color = color;
        // En HDRP/Decal la opacidad inicial es 1 — se anima en CicloFade
        return m;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  POOL DE DECALPROJECTORES
    // ════════════════════════════════════════════════════════════════════════

    void CrearPool()
    {
        _pool = new Decal[tamanoPool];
        var parent = new GameObject("DecalPool").transform;
        parent.SetParent(transform, false);

        for (int i = 0; i < tamanoPool; i++)
        {
            var go = new GameObject($"Decal_{i}");
            go.transform.SetParent(parent, false);
            var proj = go.AddComponent<DecalProjector>();
            proj.enabled       = false;
            proj.size          = new Vector3(0.2f, 0.2f, 0.5f);
            proj.fadeFactor    = 0f;
            proj.decalLayerMask = (DecalLayerEnum)0xFF; // todas las capas
            proj.pivot         = new Vector3(0f, 0f, -0.5f);
            _pool[i] = new Decal { projector = proj, enUso = false };
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  API PÚBLICA
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Crea un decal del tipo dado en la posición world con la normal de la superficie.
    /// </summary>
    public static void SpawnDecal(DecalTipo tipo, Vector3 posicion, Vector3 normal)
    {
        if (Instance == null) return;
        Instance.SpawnInterno(tipo, posicion, normal,
            Instance._materiales[(int)tipo],
            TamanoPorTipo(tipo), null);
    }

    /// <summary>
    /// Grafiti con color personalizado (llamado desde SistemaGrafitis).
    /// </summary>
    public static void SpawnGrafiti(Vector3 posicion, Vector3 normal, Color color,
                                     Vector2 tamano)
    {
        if (Instance == null) return;
        // Instanciar una copia del material para este grafiti con el color dado
        var mat = new Material(Instance._materiales[(int)DecalTipo.GrafitiSmall]);
        if (mat.HasProperty(ID_BaseColor)) mat.SetColor(ID_BaseColor, color);
        else mat.color = color;
        Instance.SpawnInterno(DecalTipo.GrafitiSmall, posicion, normal, mat, tamano, mat);
    }

    void SpawnInterno(DecalTipo tipo, Vector3 pos, Vector3 normal,
                      Material mat, Vector2 tamano, Material matInstancia)
    {
        // Encontrar slot libre o reciclar el más antiguo
        int slot = -1;
        float mayorTiempo = -1f;
        for (int i = 0; i < _pool.Length; i++)
        {
            if (!_pool[i].enUso) { slot = i; break; }
            if (_pool[i].tiempoNacimiento > mayorTiempo)
            { mayorTiempo = _pool[i].tiempoNacimiento; slot = i; }
        }
        if (slot < 0) slot = _cursor % tamanoPool;
        _cursor++;

        var d    = _pool[slot];
        var proj = d.projector;
        if (proj == null) return;

        // Orientar el proyector: Z = dirección de proyección (contra la normal)
        proj.transform.position = pos + normal * 0.02f;
        proj.transform.rotation = Quaternion.LookRotation(-normal, Vector3.up);

        // Tamaño y material
        proj.size    = new Vector3(tamano.x, tamano.y, 0.5f);
        proj.material = mat;
        proj.fadeFactor = 1f;
        proj.enabled = true;

        _pool[slot] = new Decal
        {
            projector       = proj,
            tiempoNacimiento = Time.time,
            enUso           = true,
        };
    }

    static Vector2 TamanoPorTipo(DecalTipo tipo) => tipo switch
    {
        DecalTipo.BalaConcreto  => new Vector2(0.06f, 0.06f),
        DecalTipo.BalaMetalica  => new Vector2(0.05f, 0.05f),
        DecalTipo.BalaAsfalto   => new Vector2(0.07f, 0.07f),
        DecalTipo.Sangre        => new Vector2(0.12f, 0.10f),
        _                       => new Vector2(0.25f, 0.25f),
    };

    // ════════════════════════════════════════════════════════════════════════
    //  CICLO DE FADE Y EXPIRACIÓN
    // ════════════════════════════════════════════════════════════════════════

    IEnumerator CicloFade()
    {
        while (true)
        {
            yield return new WaitForSeconds(2f);

            if (vidaDecal <= 0f) continue; // permanentes

            float ahora = Time.time;
            for (int i = 0; i < _pool.Length; i++)
            {
                if (!_pool[i].enUso) continue;
                var proj = _pool[i].projector;
                if (proj == null) continue;

                float edad = ahora - _pool[i].tiempoNacimiento;
                if (edad >= vidaDecal)
                {
                    // Fade-out: reducir fadeFactor gradualmente
                    proj.fadeFactor -= 0.15f;
                    if (proj.fadeFactor <= 0f)
                    {
                        proj.enabled = false;
                        proj.fadeFactor = 0f;
                        var tmp = _pool[i];
                        tmp.enUso = false;
                        _pool[i] = tmp;
                    }
                }
                else if (edad > vidaDecal * 0.8f)
                {
                    // Empieza fade-out suave en el último 20% de la vida
                    float ratio = 1f - Mathf.InverseLerp(vidaDecal * 0.8f, vidaDecal, edad);
                    proj.fadeFactor = ratio;
                }
            }
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
