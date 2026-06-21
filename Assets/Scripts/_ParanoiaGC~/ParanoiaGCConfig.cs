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
    [Tooltip("Si true, intenta añadir/activar el cerebro de policía (CerebroGOAPPolicia) al convertir.")]
    public bool swapCerebroPolicia = true;

    /// <summary>Nº objetivo de convertidos para una paranoia dada.</summary>
    public int Objetivo(float paranoia, int max)
    {
        if (paranoia < umbralInicio) return 0;
        if (paranoia >= umbralCritico) return max;
        float t = (paranoia - umbralInicio) / Mathf.Max(0.01f, umbralCritico - umbralInicio);
        return Mathf.RoundToInt(Mathf.Lerp(0f, max * 0.5f, t));
    }
}
