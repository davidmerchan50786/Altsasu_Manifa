using System.Collections;
using UnityEngine;

/// <summary>
/// Siembra prefabs de árboles/arbustos decorativos en puntos fijos del Inspector.
/// Separa la responsabilidad de vegetación manual de GameManagerAltsasua.
///
/// Setup: añadir este componente al mismo GO que GameManagerAltsasua (o a un
/// GO dedicado "VegetacionManager"). Asignar prefabsArboles y puntosArboles.
/// GameManagerAltsasua detecta este componente y no ejecuta su método legacy.
/// </summary>
[DefaultExecutionOrder(-70)]
public class SembradoVegetacionManual : MonoBehaviour
{
    [Header("Prefabs de vegetación")]
    [Tooltip("Pool de prefabs: roble, pino, chopo, etc. Se elige uno al azar por punto.")]
    public GameObject[] prefabsArboles;

    [Header("Puntos de siembra")]
    [Tooltip("Transforms donde se instancia un árbol al iniciar.")]
    public Transform[] puntosArboles;

    [Header("Opciones")]
    [Tooltip("Si true, ajusta la Y del árbol a la altura del terreno en ese punto.")]
    public bool ajustarATerreno = true;

    [Tooltip("Repartir la siembra a lo largo de N frames para no bloquear el primer frame.")]
    [Range(1, 60)]
    public int framesSiembra = 10;

    bool _sembrado;

    void Start()
    {
        if (prefabsArboles == null || prefabsArboles.Length == 0) return;
        if (puntosArboles  == null || puntosArboles.Length  == 0) return;
        StartCoroutine(SembrarPorFrames());
    }

    IEnumerator SembrarPorFrames()
    {
        if (_sembrado) yield break;
        _sembrado = true;

        int porFrame = Mathf.Max(1, puntosArboles.Length / framesSiembra);

        for (int i = 0; i < puntosArboles.Length; i++)
        {
            var punto = puntosArboles[i];
            if (punto == null) continue;

            var prefab = prefabsArboles[Random.Range(0, prefabsArboles.Length)];
            if (prefab == null) continue;

            Vector3 pos = punto.position;
            if (ajustarATerreno)
                pos.y = GeoDataAlsasua.AlturaTerreno(pos.x, pos.z);

            Instantiate(prefab, pos, Quaternion.Euler(0, Random.Range(0f, 360f), 0));

            if (i % porFrame == 0) yield return null;
        }
    }
}
