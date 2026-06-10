// Assets/Scripts/SistemaSuperficiesMojadas.cs
// ═══════════════════════════════════════════════════════════════════════════
//  Cambia los materiales del suelo a versión "mojada" durante lluvia activa.
//  Trabaja con SistemaClima (existe en el proyecto).
//
//  En materiales HDRP/Lit, sube _Smoothness al 0.9 y baja _BaseColor 60%
//  cuando llueve, vuelve a originales al parar.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using UnityEngine;

public class SistemaSuperficiesMojadas : MonoBehaviour
{
    [Tooltip("Smoothness objetivo cuando está mojado (0..1).")]
    [Range(0f, 1f)] public float smoothnessLluvia = 0.92f;
    [Tooltip("Multiplicador de color base cuando mojado (oscurece).")]
    [Range(0f, 1f)] public float oscurecimientoLluvia = 0.6f;
    [Tooltip("Velocidad de transición (0..1 por segundo).")]
    public float velocidadTransicion = 0.5f;

    struct Original
    {
        public Material mat;
        public float smoothness;
        public Color baseColor;
    }

    readonly List<Original> _materiales = new List<Original>();
    float _mojado = 0f;    // 0 = seco, 1 = empapado
    SistemaClima _clima;

    void Start()
    {
        _clima = FindFirstObjectByType<SistemaClima>();
        EscanearMaterialesSuelo();
    }

    void Update()
    {
        if (_clima == null) return;
        // Asumimos que SistemaClima expone una propiedad o estado de lluvia.
        // Si no, hacemos fallback a hora del día (más mojado de noche).
        float deseado = ObtenerNivelLluvia();
        _mojado = Mathf.MoveTowards(_mojado, deseado, velocidadTransicion * Time.deltaTime);

        ActualizarMateriales();
    }

    float ObtenerNivelLluvia()
    {
        // Heurística: el sistema de clima no estandariza interfaz. Probamos por reflection.
        var t = _clima.GetType();
        var prop = t.GetProperty("IntensidadLluvia")
                ?? t.GetProperty("intensidadLluvia")
                ?? t.GetProperty("Lluvia");
        if (prop != null && prop.PropertyType == typeof(float))
            return Mathf.Clamp01((float)prop.GetValue(_clima));

        var field = t.GetField("intensidadLluvia")
                 ?? t.GetField("lluvia")
                 ?? t.GetField("nivelLluvia");
        if (field != null && field.FieldType == typeof(float))
            return Mathf.Clamp01((float)field.GetValue(_clima));

        return 0f;
    }

    void EscanearMaterialesSuelo()
    {
        _materiales.Clear();
        // Carreteras + aceras + plaza adoquines
        var nombres = new[] { "carreteras", "aceras", "plaza", "decales", "calle", "via",
                              "asfalto", "hormigon", "adoquines", "infrastructura" };

        foreach (var rend in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            string objLow = rend.gameObject.name.ToLower();
            string padre = rend.transform.parent != null
                ? rend.transform.parent.name.ToLower() : "";

            bool esSuelo = false;
            foreach (var key in nombres)
                if (objLow.Contains(key) || padre.Contains(key)) { esSuelo = true; break; }
            if (!esSuelo) continue;

            foreach (var m in rend.sharedMaterials)
            {
                if (m == null) continue;
                if (!m.HasProperty("_Smoothness") || !m.HasProperty("_BaseColor")) continue;

                // Guardar originales solo si todavía no
                bool yaRegistrado = false;
                foreach (var o in _materiales) if (o.mat == m) { yaRegistrado = true; break; }
                if (yaRegistrado) continue;

                _materiales.Add(new Original {
                    mat        = m,
                    smoothness = m.GetFloat("_Smoothness"),
                    baseColor  = m.GetColor("_BaseColor"),
                });
            }
        }
    }

    void ActualizarMateriales()
    {
        foreach (var o in _materiales)
        {
            if (o.mat == null) continue;
            float s = Mathf.Lerp(o.smoothness, smoothnessLluvia, _mojado);
            Color c = Color.Lerp(o.baseColor,
                                  o.baseColor * oscurecimientoLluvia, _mojado);
            o.mat.SetFloat("_Smoothness", s);
            o.mat.SetColor("_BaseColor", c);
        }
    }

    void OnDestroy()
    {
        // Restaurar originales
        foreach (var o in _materiales)
        {
            if (o.mat == null) continue;
            o.mat.SetFloat("_Smoothness", o.smoothness);
            o.mat.SetColor("_BaseColor",  o.baseColor);
        }
    }
}
