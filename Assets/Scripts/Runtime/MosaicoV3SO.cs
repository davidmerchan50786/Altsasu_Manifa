// Assets/Scripts/Runtime/MosaicoV3SO.cs
// ScriptableObject puente entre el baker del editor y el sistema runtime.
// Guardado en Assets/Resources/MosaicoV3/ para que MosaicoV3Sistema lo
// cargue con Resources.Load sin necesitar Addressables.

using UnityEngine;

[CreateAssetMenu(menuName = "Alsasua/Mosaico V3 SO", fileName = "MosaicoV3SO")]
public sealed class MosaicoV3SO : ScriptableObject
{
    [Tooltip("Una malla por anillo (índice 0 = urbano, 1 = valle, 2 = sierras).")]
    public Mesh[] mallasPorAnillo;       // 3 elementos

    [Tooltip("Material HDRP/Lit compartido para los 3 anillos.")]
    public Material material;

    [Tooltip("halfExtent en metros de cada anillo (leído del manifest).")]
    public float[] halfExtents;          // 3 elementos

    [Tooltip("Centro del mosaico en Unity (OX, OZ).")]
    public float centroX, centroZ;

    [Tooltip("true (recomendado) = conservar los TerrainCollider originales para físicas y NavMesh.\n" +
             "false = desactivarlos (usar solo cuando las mallas V3 tengan sus propios MeshColliders).")]
    public bool preservarTerrainColliders = true;

    // Distancia de cull de LODGroup por anillo (fracción de pantalla).
    // Índice 0 = anillo urbano (cull tardío), 2 = sierras (cull muy tardío).
    // El LODGroup permite escalar la visibilidad de cada anillo independientemente
    // del QualitySettings.lodBias global que afecta a los edificios.
    public float[] cullScreenRatio = { 0.003f, 0.002f, 0.0005f };
}
