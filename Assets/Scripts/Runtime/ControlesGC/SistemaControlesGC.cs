// Assets/Scripts/_ControlesGC~/SistemaControlesGC.cs  (STAGING/DRAFT — carpeta ~ no compila)
// ─────────────────────────────────────────────────────────────────────────────
//  Manager de los controles GC. Mantiene un nº de controles activos proporcional
//  a la paranoia (0 por debajo del umbral, hasta maxControles a paranoia 100), y
//  los enciende/apaga DE UNO EN UNO y SIEMPRE fuera de cámara, para que aparezcan
//  y desaparezcan sin que el jugador vea el "pop". Los controles se colocan a mano
//  en chokepoints (calle San Juan, puentes del Arakil, salidas de la N-1…).
// ─────────────────────────────────────────────────────────────────────────────
using System.Collections.Generic;
using UnityEngine;

public class SistemaControlesGC : MonoBehaviour
{
    public static SistemaControlesGC Instance { get; private set; }

    [Tooltip("Controles colocados en la escena (chokepoints). Si está vacío, se autodetectan.")]
    public List<ControlGuardiaCivil> controles = new();
    [Tooltip("Paranoia por debajo de la cual no hay ningún control.")]
    public float umbralActivacion = 70f;
    public int   maxControles = 4;
    public float intervaloChequeo = 2f;
    [Tooltip("Panel único de tuning. Si se asigna, manda sobre umbral/maxControles.")]
    public SintoniaAltsasu sintonia;

    float _t;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        if (controles.Count == 0)
            controles.AddRange(FindObjectsByType<ControlGuardiaCivil>(FindObjectsSortMode.None));
    }

    void Update()
    {
        _t += Time.deltaTime;
        if (_t < intervaloChequeo) return;
        _t = 0f;

        float paranoia = SistemaApoyoPopular.Instance != null ? SistemaApoyoPopular.Instance.paranoia : 0f;
        int objetivo = sintonia != null
            ? sintonia.ControlesObjetivo(paranoia)
            : (paranoia < umbralActivacion
                ? 0
                : Mathf.Clamp(Mathf.CeilToInt(Mathf.InverseLerp(umbralActivacion, 100f, paranoia) * maxControles), 0, maxControles));

        int activos = ContarActivos();
        if      (objetivo > activos) Cambiar(true);    // montar uno (off-screen)
        else if (objetivo < activos) Cambiar(false);   // retirar uno (off-screen)
    }

    int ContarActivos()
    {
        int n = 0;
        for (int i = 0; i < controles.Count; i++)
            if (controles[i] != null && controles[i].Activo) n++;
        return n;
    }

    // Cambia el estado de UN control que no esté en cámara (gradual, sin pop).
    void Cambiar(bool activar)
    {
        for (int i = 0; i < controles.Count; i++)
        {
            var c = controles[i];
            if (c == null || c.Activo == activar) continue;
            if (c.VisibleEnCamara()) continue;          // no montar/desmontar delante del jugador
            if (activar) c.Activar(); else c.Desactivar();
            return;
        }
        // si todos los candidatos están en cámara, se reintenta al siguiente tick
    }
}
