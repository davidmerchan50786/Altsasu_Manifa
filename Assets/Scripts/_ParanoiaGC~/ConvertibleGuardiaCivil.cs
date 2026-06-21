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
    Behaviour _cerebroPolicia;   // añadido/activado al convertir
    bool _cacheado;

    void OnEnable()  => SistemaParanoiaGuardiaCivil.Registrar(this);
    void OnDisable() { if (Convertido) Revertir(); SistemaParanoiaGuardiaCivil.Desregistrar(this); }

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

    public void Convertir(ParanoiaGCConfig cfg)
    {
        if (Convertido || cfg == null) return;
        Cachear(cfg);

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

        // 3) Cerebro: civil OFF, policía ON
        if (cerebroCivil) cerebroCivil.enabled = false;
        if (cfg.swapCerebroPolicia) ActivarCerebroPolicia();

        // 4) Facción/tag para wanted y detección  //★ ajustar a tu sistema
        gameObject.tag = "Untagged"; // p.ej. "Policia" si tienes ese tag definido

        Convertido = true;
    }

    public void Revertir()
    {
        if (!Convertido) return;

        if (_rends != null)
            for (int i = 0; i < _rends.Length; i++)
                if (_rends[i]) _rends[i].sharedMaterials = _matsOrig[i];

        if (_tricornio) _tricornio.gameObject.SetActive(false);
        if (_rotativo)  _rotativo.gameObject.SetActive(false);

        if (_cerebroPolicia) _cerebroPolicia.enabled = false;
        if (cerebroCivil) cerebroCivil.enabled = true;

        Convertido = false;
    }

    /// <summary>True si algún renderer está siendo dibujado por la cámara (para no morfear en pantalla).</summary>
    public bool VisibleEnCamara()
    {
        if (_rends == null) return false;
        foreach (var r in _rends) if (r && r.isVisible) return true;
        return false;
    }

    void ActivarCerebroPolicia()
    {
        // ★ Si tienes CerebroGOAPPolicia, actívalo o añádelo. Aquí, defensivo:
        var t = System.Type.GetType("CerebroGOAPPolicia");
        if (t == null) return;
        _cerebroPolicia = GetComponent(t) as Behaviour;
        if (_cerebroPolicia == null) _cerebroPolicia = gameObject.AddComponent(t) as Behaviour;
        if (_cerebroPolicia) _cerebroPolicia.enabled = true;
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
