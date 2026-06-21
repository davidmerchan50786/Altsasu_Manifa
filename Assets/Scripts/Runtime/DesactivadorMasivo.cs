// Assets/Scripts/Runtime/DesactivadorMasivo.cs
// Helper interno: desactiva un conjunto de MeshRenderers a 50/frame para
// evitar el GPU stall de un SetActive masivo en un solo frame.
// Se autodestruye al terminar. Usado exclusivamente por CargadorCiudadHorneada.

using System;
using System.Collections;
using UnityEngine;

public sealed class DesactivadorMasivo : MonoBehaviour
{

    public void Iniciar(MeshRenderer[] mrs, GameObject raiz, Action onDone)
        => StartCoroutine(Desactivar(mrs, raiz, onDone));

    IEnumerator Desactivar(MeshRenderer[] mrs, GameObject raiz, Action onDone)
    {
        int n = 0;
        foreach (var mr in mrs)
        {
            if (mr == null) continue;
            if (mr.transform.IsChildOf(raiz.transform)) continue;
            if (EnDenylist(mr.transform)) continue;
            mr.gameObject.SetActive(false);
            if (++n % 50 == 0) yield return null;   // cede el frame cada 50 objetos
        }
        onDone?.Invoke();
        Destroy(gameObject);
    }

    static bool EnDenylist(Transform t) => DenylistUtility.EnDenylist(t);
}
