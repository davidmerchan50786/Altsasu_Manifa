// Assets/Scripts/CatalogoVivo.cs
// ═══════════════════════════════════════════════════════════════════════════
//  CATÁLOGO VIVO — ScriptableObject con referencias a los prefabs reales
//  (humanos, guardias, vehículos) cargable en RUNTIME desde Resources.
//
//  Generar/actualizar el asset: menú  Tools/Alsasua/✨ ACTIVAR MUNDO VIVO AAA+
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

[CreateAssetMenu(fileName = "CatalogoVivo", menuName = "Altsasu/Catálogo Vivo")]
public class CatalogoVivo : ScriptableObject
{
    public GameObject[] civiles;
    public GameObject[] guardias;
    public GameObject[] vehiculos;
    public GameObject[] camiones;

    static CatalogoVivo _activo;
    static bool _intentadoCargar;

    /// <summary>Instancia activa, cargada perezosamente desde Resources/CatalogoVivo.</summary>
    public static CatalogoVivo Activo
    {
        get
        {
            if (_activo == null && !_intentadoCargar)
            {
                _intentadoCargar = true;
                _activo = Resources.Load<CatalogoVivo>("CatalogoVivo");
            }
            return _activo;
        }
    }

    public GameObject CivilAleatorio()   => Aleatorio(civiles);
    public GameObject GuardiaAleatorio() => Aleatorio(guardias);
    public GameObject VehiculoAleatorio(bool permitirCamion = true)
    {
        if (permitirCamion && camiones != null && camiones.Length > 0 && Random.value < 0.18f)
            return Aleatorio(camiones);
        return Aleatorio(vehiculos);
    }

    static GameObject Aleatorio(GameObject[] arr)
        => (arr != null && arr.Length > 0) ? arr[Random.Range(0, arr.Length)] : null;
}
