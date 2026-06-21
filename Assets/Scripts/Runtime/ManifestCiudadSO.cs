// Assets/Scripts/Runtime/ManifestCiudadSO.cs
// ScriptableObject que HorneadorCiudad (editor) escribe en
// Assets/Resources/CiudadHorneada/ManifestCiudadSO.asset
// y CargadorCiudadHorneada (runtime) lee con Resources.Load.
// Las refs directas a prefabs evitan paths hardcodeados y funcionan
// en editor + builds sin necesidad de Addressables.

using UnityEngine;

[CreateAssetMenu(menuName = "Alsasua/Manifest Ciudad Horneada", fileName = "ManifestCiudadSO")]
public sealed class ManifestCiudadSO : ScriptableObject
{
    public float    celda;
    public int      totalCeldas;
    public int      drawCallsAprox;
    public GameObject[] prefabs;
}
