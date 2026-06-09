// Assets/Scripts/SistemaHuellasAsfalto.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SISTEMA DE HUELLAS DE ASFALTO — marcas de neumáticos persistentes
//
//  Genera DecalProjectors oscuros que simulan:
//    · Marcas de frenada (negro quemado, largo)
//    · Marcas de giro (media luna, más suaves)
//    · Manchas de aceite (oval, iridiscente)
//    · Polvo al frenar en tierra (beige, se desvanece rápido)
//
//  Se activa desde ControladorVehiculoJugador cuando:
//    · Freno fuerte (abs_brake > 0.6 + velocidad > 30 km/h)
//    · Giro brusco con aceleración (derrape)
//    · Impacto de colisión
//
//  Usa el pool de SistemaDecalesHDRP internamente para evitar duplicar código.
//  Si SistemaDecalesHDRP no está disponible, crea sus propios projectors.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

[DefaultExecutionOrder(-45)]
public class SistemaHuellasAsfalto : MonoBehaviour
{
    public static SistemaHuellasAsfalto Instance { get; private set; }

    public enum TipoHuella { Frenada, Giro, AceiteManchas, PolvoTierra }

    [Header("Pool")]
    public int tamanoPool = 64;
    [Tooltip("Tiempo de vida de las huellas (s). 0 = permanentes.")]
    public float vidaHuella = 180f;

    [Header("Umbrales de activación")]
    [Range(0f, 1f)] public float umbralFreno   = 0.55f;   // intensidad freno para marcar
    [Range(0f, 1f)] public float umbralDerrape  = 0.45f;  // slip ratio lateral
    public float velocidadMinimaKmh = 25f;

    // ── Pool propio (fallback si no hay SistemaDecalesHDRP) ───────────────
    struct HuellaSlot
    {
        public DecalProjector proj;
        public float          tiempoNac;
        public bool           enUso;
    }
    HuellaSlot[] _pool;
    int          _cursor;
    bool         _usaPoolPropio;

    Material _matFrenada, _matGiro, _matAceite, _matPolvo;
    static readonly int ID_BaseColor = Shader.PropertyToID("_BaseColor");

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void Start()
    {
        CrearMateriales();
        _usaPoolPropio = SistemaDecalesHDRP.Instance == null;
        if (_usaPoolPropio) CrearPoolPropio();
        StartCoroutine(CicloFade());
        AlsasuaLogger.Info("HuellasAsfalto", $"Pool={tamanoPool}, propio={_usaPoolPropio}");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  API PÚBLICA
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Llama desde ControladorVehiculoJugador.Update() cuando detecta frenada.
    /// </summary>
    public static void RegistrarFrenada(Vector3 posRuedaIzq, Vector3 posRuedaDer,
                                         Quaternion orientacion, float intensidad)
    {
        if (Instance == null) return;
        float largo = Mathf.Lerp(0.3f, 2.5f, intensidad);
        Instance.SpawnHuella(TipoHuella.Frenada, posRuedaIzq, orientacion, new Vector2(0.22f, largo));
        Instance.SpawnHuella(TipoHuella.Frenada, posRuedaDer, orientacion, new Vector2(0.22f, largo));
    }

    /// <summary>Derrape — arco de media luna.</summary>
    public static void RegistrarDerrape(Vector3 posicion, Quaternion orientacion,
                                         float intensidad, bool izquierda)
    {
        if (Instance == null) return;
        var rot = orientacion * Quaternion.Euler(0f, izquierda ? -20f : 20f, 0f);
        Instance.SpawnHuella(TipoHuella.Giro, posicion, rot,
            new Vector2(0.25f, Mathf.Lerp(0.4f, 1.8f, intensidad)));
    }

    /// <summary>Mancha de aceite (colisión con motor dañado).</summary>
    public static void RegistrarAceite(Vector3 posicion)
    {
        if (Instance == null) return;
        Instance.SpawnHuella(TipoHuella.AceiteManchas, posicion,
            Quaternion.Euler(90f, Random.Range(0f, 360f), 0f), new Vector2(0.6f, 0.8f));
    }

    // ════════════════════════════════════════════════════════════════════════
    //  SPAWN INTERNO
    // ════════════════════════════════════════════════════════════════════════

    void SpawnHuella(TipoHuella tipo, Vector3 posicion, Quaternion orientacion, Vector2 tamano)
    {
        // Raycast para pegar el decal al suelo exactamente
        if (Physics.Raycast(posicion + Vector3.up * 0.5f, Vector3.down, out var hit, 2f))
            posicion = hit.point + hit.normal * 0.015f;

        if (!_usaPoolPropio)
        {
            // Usar SistemaDecalesHDRP
            var decalTipo = tipo == TipoHuella.Frenada   ? SistemaDecalesHDRP.DecalTipo.BalaAsfalto
                          : tipo == TipoHuella.Giro      ? SistemaDecalesHDRP.DecalTipo.BalaAsfalto
                          : SistemaDecalesHDRP.DecalTipo.BalaConcreto;
            SistemaDecalesHDRP.SpawnDecal(decalTipo, posicion, Vector3.up);
            return;
        }

        // Pool propio
        int slot = BuscarSlotLibre();
        var d    = _pool[slot];
        if (d.proj == null) return;

        d.proj.transform.position = posicion + Vector3.up * 0.02f;
        d.proj.transform.rotation = Quaternion.LookRotation(Vector3.down,
            orientacion * Vector3.forward);
        d.proj.size     = new Vector3(tamano.x, tamano.y, 0.3f);
        d.proj.material = MaterialPorTipo(tipo);
        d.proj.fadeFactor = 1f;
        d.proj.enabled  = true;
        _pool[slot]     = new HuellaSlot { proj = d.proj, tiempoNac = Time.time, enUso = true };
    }

    int BuscarSlotLibre()
    {
        for (int i = 0; i < _pool.Length; i++)
            if (!_pool[i].enUso) return i;
        // Pool lleno: reciclar el más antiguo
        int mas = 0;
        for (int i = 1; i < _pool.Length; i++)
            if (_pool[i].tiempoNac < _pool[mas].tiempoNac) mas = i;
        return mas;
    }

    Material MaterialPorTipo(TipoHuella t) => t switch
    {
        TipoHuella.Giro         => _matGiro,
        TipoHuella.AceiteManchas => _matAceite,
        TipoHuella.PolvoTierra  => _matPolvo,
        _                        => _matFrenada,
    };

    // ════════════════════════════════════════════════════════════════════════
    //  POOL Y MATERIALES
    // ════════════════════════════════════════════════════════════════════════

    void CrearPoolPropio()
    {
        _pool = new HuellaSlot[tamanoPool];
        var parent = new GameObject("HuellasPool").transform;
        parent.SetParent(transform, false);
        for (int i = 0; i < tamanoPool; i++)
        {
            var go   = new GameObject($"Huella_{i}");
            go.transform.SetParent(parent, false);
            var proj = go.AddComponent<DecalProjector>();
            proj.enabled = false; proj.fadeFactor = 0f;
            proj.decalLayerMask = (DecalLayerEnum)0xFF;
            _pool[i] = new HuellaSlot { proj = proj };
        }
    }

    void CrearMateriales()
    {
        var sh = Shader.Find("HDRP/Decal") ?? Shader.Find("Standard");
        _matFrenada = CrearMat(sh, "Huella_Frenada", new Color(0.07f, 0.07f, 0.07f, 0.88f));
        _matGiro    = CrearMat(sh, "Huella_Giro",    new Color(0.10f, 0.09f, 0.08f, 0.72f));
        _matAceite  = CrearMat(sh, "Huella_Aceite",  new Color(0.05f, 0.08f, 0.07f, 0.80f));
        _matPolvo   = CrearMat(sh, "Huella_Polvo",   new Color(0.62f, 0.52f, 0.38f, 0.55f));
    }

    static Material CrearMat(Shader sh, string n, Color c)
    {
        var m = new Material(sh) { name = n };
        if (m.HasProperty(ID_BaseColor)) m.SetColor(ID_BaseColor, c); else m.color = c;
        return m;
    }

    IEnumerator CicloFade()
    {
        while (true)
        {
            yield return new WaitForSeconds(5f);
            if (!_usaPoolPropio || vidaHuella <= 0f || _pool == null) continue;
            float ahora = Time.time;
            for (int i = 0; i < _pool.Length; i++)
            {
                if (!_pool[i].enUso) continue;
                float edad = ahora - _pool[i].tiempoNac;
                if (edad >= vidaHuella)
                {
                    _pool[i].proj.fadeFactor -= 0.2f;
                    if (_pool[i].proj.fadeFactor <= 0f)
                    {
                        _pool[i].proj.enabled = false;
                        var tmp = _pool[i]; tmp.enUso = false; _pool[i] = tmp;
                    }
                }
                else if (edad > vidaHuella * 0.8f)
                    _pool[i].proj.fadeFactor = 1f - Mathf.InverseLerp(vidaHuella * 0.8f, vidaHuella, edad);
            }
        }
    }

    void OnDestroy()
    {
        // FIX FUGA: los 4 materiales de decal se crean con new Material() y Unity
        // no los libera solo. Destruirlos explícitamente al morir el sistema.
        foreach (var m in new[] { _matFrenada, _matGiro, _matAceite, _matPolvo })
            if (m != null) { if (Application.isPlaying) Destroy(m); else DestroyImmediate(m); }

        if (Instance == this) Instance = null;
    }
}
