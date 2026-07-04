// Assets/Scripts/_ParanoiaGC~/ConvertibleGuardiaCivil.cs  (STAGING/DRAFT — carpeta ~ no se compila)
// ─────────────────────────────────────────────────────────────────────────────
//  Va en cada NPC o coche que PUEDE convertirse en Guardia Civil / patrulla.
//  Cachea su estado original y hace Convertir()/Revertir() con swaps de material
//  + enable/disable de hijos y cerebro. Cero Instantiate/Destroy.
//
//  Integración (marcada con //★): asignar el cerebro civil a desactivar y, si
//  quieres comportamiento policial real, el tipo del cerebro de policía.
// ─────────────────────────────────────────────────────────────────────────────
using UnityEngine;

[DisallowMultipleComponent]
public class ConvertibleGuardiaCivil : MonoBehaviour
{
    [Tooltip("True si es un vehículo (usa librea/rotativo en vez de uniforme/tricornio).")]
    public bool esCoche = false;

    [Tooltip("★ Cerebro/IA civil a desactivar al convertir (NPC o tráfico). Si null, se intenta autodetectar.")]
    public MonoBehaviour cerebroCivil;

    public bool Convertido { get; private set; }

    Renderer[] _rends;
    Material[][] _matsOrig;
    Transform _tricornio, _rotativo;
    Behaviour _gcComp;           // CerebroGuardiaCivil (NPC) o PatrullaGuardiaCivil (coche)
    float _ultimoVisible = -999f;
    bool _cacheado;

    void OnEnable()  => SistemaParanoiaGuardiaCivil.Registrar(this);
    void OnDisable() { if (Convertido) Revertir(true); SistemaParanoiaGuardiaCivil.Desregistrar(this); }

    void Cachear(ParanoiaGCConfig cfg)
    {
        if (_cacheado) return;
        _rends = GetComponentsInChildren<Renderer>(true);
        _matsOrig = new Material[_rends.Length][];
        for (int i = 0; i < _rends.Length; i++) _matsOrig[i] = _rends[i].sharedMaterials;
        _tricornio = BuscarHijo(cfg.hijoTricornio);
        _rotativo  = BuscarHijo(cfg.hijoRotativo);
        if (cerebroCivil == null) cerebroCivil = AutodetectarCerebroCivil();
        _cacheado = true;
    }

    public bool Convertir(ParanoiaGCConfig cfg, bool forzar = false)
    {
        if (Convertido || cfg == null) return false;
        Cachear(cfg);
        // OFF-SCREEN: nunca morfear en pantalla salvo forzar explícito.
        if (!forzar && VisibleEnCamara()) return false;

        // 1) Skin
        Material skin = esCoche ? cfg.libreaPatrullaMaterial : cfg.uniformeMaterial;
        if (skin != null)
            foreach (var r in _rends)
            {
                var arr = r.sharedMaterials;
                for (int j = 0; j < arr.Length; j++) arr[j] = skin;
                r.sharedMaterials = arr;
            }

        // 2) Accesorios
        if (!esCoche && _tricornio) _tricornio.gameObject.SetActive(true);
        if (esCoche  && _rotativo)  _rotativo.gameObject.SetActive(true);

        // 3) Comportamiento: civil OFF, Guardia Civil ON
        if (cerebroCivil) cerebroCivil.enabled = false;
        if (esCoche) ActivarComportamiento<PatrullaGuardiaCivil>();
        else if (cfg.swapCerebroPolicia) ActivarComportamiento<CerebroGuardiaCivil>();

        // 4) Facción/tag para wanted y detección  //★ ajusta a tu sistema:
        // gameObject.tag = "Policia";

        Convertido = true;
        return true;
    }

    public bool Revertir(bool forzar = false)
    {
        if (!Convertido) return false;
        if (!forzar && VisibleEnCamara()) return false;   // tampoco des-morfear en pantalla

        if (_rends != null)
            for (int i = 0; i < _rends.Length; i++)
                if (_rends[i]) _rends[i].sharedMaterials = _matsOrig[i];

        if (_tricornio) _tricornio.gameObject.SetActive(false);
        if (_rotativo)  _rotativo.gameObject.SetActive(false);

        if (_gcComp) _gcComp.enabled = false;
        if (cerebroCivil) cerebroCivil.enabled = true;

        Convertido = false;
        return true;
    }

    /// <summary>True si algún renderer está siendo dibujado por la cámara (para no morfear en pantalla).</summary>
    /// <summary>True si está en pantalla (o lo estuvo hace <0.5s). Autocachea renderers.</summary>
    public bool VisibleEnCamara()
    {
        if (_rends == null) _rends = GetComponentsInChildren<Renderer>(true);
        foreach (var r in _rends) if (r && r.isVisible) { _ultimoVisible = Time.time; return true; }
        return Time.time - _ultimoVisible < 0.5f;
    }

    void ActivarComportamiento<T>() where T : Behaviour
    {
        _gcComp = GetComponent<T>();
        if (_gcComp == null) _gcComp = gameObject.AddComponent<T>();
        if (_gcComp) _gcComp.enabled = true;
    }

    MonoBehaviour AutodetectarCerebroCivil()
    {
        // Heurística: el primer MonoBehaviour que NO sea este ni un Renderer-helper.
        foreach (var mb in GetComponents<MonoBehaviour>())
            if (mb != this && mb.GetType().Name.Contains("NPC")) return mb;
        return null;
    }

    Transform BuscarHijo(string nombre)
    {
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (t.name == nombre) return t;
        return null;
    }
}
