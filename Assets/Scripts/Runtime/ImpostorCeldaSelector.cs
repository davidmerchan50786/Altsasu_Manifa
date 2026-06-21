// Assets/Scripts/Runtime/ImpostorCeldaSelector.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SELECTOR DE IMPOSTOR DE CELDA — LOD2 billboard octaédrico (8 ángulos)
//
//  Billboard real: quad que siempre mira a la cámara (solo eje Y) y muestra
//  la textura capturada desde el ángulo horizontal más cercano de 8.
//  Ángulos: N(0°) NE(45°) E(90°) SE(135°) S(180°) SW(225°) W(270°) NW(315°)
//
//  Coste: 4 floats + 1 modulo + 1 comparación por frame; solo activo cuando
//  el LODGroup activa este nivel (< 1.8% pantalla, ~400m+).
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

[DefaultExecutionOrder(100)]
public sealed class ImpostorCeldaSelector : MonoBehaviour
{
    // Asignado por CapturadorImpostores (8 ángulos en sentido horario desde Norte):
    // [0]=N [1]=NE [2]=E [3]=SE [4]=S [5]=SW [6]=W [7]=NW
    [HideInInspector] public Material[] materiales;

    const int   N_DIRS      = 8;
    const float PASO_GRADOS = 360f / N_DIRS;   // 45°

    MeshRenderer _mr;
    Camera       _cam;
    int          _idxActivo = -1;

    void Awake() => _mr = GetComponent<MeshRenderer>();

    void Start()
    {
        _cam = Camera.main;
        if (_cam == null) { enabled = false; return; }
        if (materiales == null || materiales.Length < N_DIRS)
        {
            Debug.LogWarning(
                $"[ImpostorCelda] {name}: necesita {N_DIRS} materiales (N/NE/E/SE/S/SW/W/NW). " +
                "Recaptura con 🎭 Capturar Impostores.", this);
            // Intentar fallback a 4 ángulos si existen (bake viejo)
            if (materiales != null && materiales.Length >= 4)
                Debug.Log($"[ImpostorCelda] {name}: usando fallback 4 ángulos.");
            else
                enabled = false;
        }
    }

    void LateUpdate()
    {
        if (_mr == null || materiales == null || materiales.Length == 0) return;
        if (_cam == null) { _cam = Camera.main; if (_cam == null) return; }

        var toCamera = _cam.transform.position - transform.position;
        toCamera.y = 0f;
        if (toCamera.sqrMagnitude < 0.01f) return;

        float angulo = Vector3.SignedAngle(Vector3.forward, toCamera.normalized, Vector3.up);
        if (angulo < 0f) angulo += 360f;

        // Snap al ángulo capturado más cercano (compatible con 4 u 8 materiales)
        int nDirs = materiales.Length;
        float paso = 360f / nDirs;
        int idx = Mathf.RoundToInt(angulo / paso) % nDirs;

        if (idx != _idxActivo)
        {
            _mr.sharedMaterial = materiales[idx];
            _idxActivo = idx;
        }

        transform.rotation = Quaternion.Euler(0f, angulo + 180f, 0f);
    }
}
