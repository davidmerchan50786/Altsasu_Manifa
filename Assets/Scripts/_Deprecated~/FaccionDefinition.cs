// FaccionDefinition.cs — ScriptableObject de datos por facción
// ═══════════════════════════════════════════════════════════════════════════
//  Datos de presentación y gameplay de cada facción, editables sin tocar
//  código. Crear en: Assets/Data/Factions/ (clic derecho → Altsasu → Facción).
//
//  La matriz cruzada NUMÉRICA vive en SistemaFacciones (es lógica de balance
//  global, no de facción individual). Aquí va todo lo demás.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

[CreateAssetMenu(fileName = "Faccion_", menuName = "Altsasu/Facción", order = 10)]
public class FaccionDefinition : ScriptableObject
{
    [Header("Identidad")]
    public FaccionId id;
    public string nombreMostrado;
    public string lema;
    [TextArea(3, 8)] public string descripcionCodice;
    public Sprite emblema;
    public Color colorFaccion = Color.white;

    [Header("Sede en el mundo (coordenadas Unity — origen Herriko Plaza)")]
    public Vector3 posicionSede;
    public string nombreSede;

    [Header("Manifestaciones (consumido por SistemaManifestacion)")]
    [Tooltip("Multiplicador de velocidad de reclutamiento de esta facción")]
    [Range(0.1f, 3f)] public float multReclutamiento = 1f;
    [Tooltip("Multiplicador de moral/resistencia de sus manifestantes")]
    [Range(0.1f, 3f)] public float multMoral = 1f;
    [Tooltip("Proporción de su gente que va al grupo de disturbios (0 = pacíficos)")]
    [Range(0f, 1f)] public float ratioDisturbios = 0.1f;

    [Header("Recompensas de alianza")]
    [Tooltip("Reputación mínima para considerarse aliado")]
    [Range(50f, 100f)] public float umbralAlianza = 75f;
    [TextArea(2, 4)] public string ventajaAliado;
    [TextArea(2, 4)] public string castigoEnemigo;

    [Header("Diálogo")]
    [Tooltip("Líneas de saludo según reputación: 0=hostil, 1=neutral, 2=aliado")]
    [TextArea(1, 3)] public string[] saludos = new string[3];

    /// <summary>Saludo apropiado según reputación actual con la facción.</summary>
    public string SaludoSegunReputacion(float rep)
    {
        if (saludos == null || saludos.Length < 3) return string.Empty;
        if (rep >= umbralAlianza) return saludos[2];
        if (rep >= 35f)           return saludos[1];
        return saludos[0];
    }
}
