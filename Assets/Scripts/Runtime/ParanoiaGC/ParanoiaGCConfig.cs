// Assets/Scripts/_ParanoiaGC~/ParanoiaGCConfig.cs  (STAGING/DRAFT — carpeta ~ no se compila)
// Config del sistema "paranoia → Guardia Civil". Ver Docs/Narrativa/MECANICA_Paranoia_GuardiaCivil.md
using UnityEngine;

[CreateAssetMenu(menuName = "Alsasua/Paranoia GC Config", fileName = "ParanoiaGCConfig")]
public class ParanoiaGCConfig : ScriptableObject
{
    [Header("Skins de conversión")]
    [Tooltip("Material de uniforme verde GC para los NPC convertidos.")]
    public Material uniformeMaterial;
    [Tooltip("Material de librea de patrulla para los coches convertidos.")]
    public Material libreaPatrullaMaterial;

    [Header("Nombres de los hijos a activar al convertir (pre-creados y desactivados)")]
    public string hijoTricornio = "Tricornio";
    public string hijoRotativo  = "Rotativo";

    [Header("Curva de paranoia")]
    [Range(0, 100)] public float umbralInicio  = 70f;   // por debajo: 0 conversiones
    [Range(0, 100)] public float umbralCritico = 90f;   // por encima: MAX
    public int   maxNpc    = 12;
    public int   maxCoches = 5;
    [Tooltip("Conversiones (o reversiones) por segundo. Gradual = inmersión.")]
    public float ritmoPorSegundo = 1.5f;

    [Header("Comportamiento")]
    [Tooltip("Si true, activa CerebroGuardiaCivil al convertir un NPC.")]
    public bool swapCerebroPolicia = true;

    [Header("El apoyo popular frena la conversión")]
    [Range(0f, 1f)]
    [Tooltip("Cuánto reduce la conversión el apoyo máximo. 0.7 = a apoyo 100 solo se convierte el 30%.")]
    public float frenoApoyo = 0.7f;

    /// <summary>Factor [1 .. 1-frenoApoyo] según apoyo (0..100). Calle alta = menos guardias.</summary>
    public float FactorApoyo(float apoyo) => Mathf.Lerp(1f, 1f - frenoApoyo, Mathf.Clamp01(apoyo / 100f));

    /// <summary>Nº objetivo de convertidos para una paranoia dada.</summary>
    public int Objetivo(float paranoia, int max)
    {
        if (paranoia < umbralInicio) return 0;
        if (paranoia >= umbralCritico) return max;
        float t = (paranoia - umbralInicio) / Mathf.Max(0.01f, umbralCritico - umbralInicio);
        return Mathf.RoundToInt(Mathf.Lerp(0f, max * 0.5f, t));
    }
}
