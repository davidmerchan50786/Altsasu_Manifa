// Assets/Scripts/SistemaIKProcedural.cs
// ═══════════════════════════════════════════════════════════════════════════
//  IK PROCEDURAL — pies, manos y cabeza con Animator IK
//
//  • Foot IK: los pies se adhieren al terreno mediante raycast, con
//    adaptación de altura del cuerpo y rotación del pie en pendientes.
//  • Hand IK: la mano derecha apunta hacia la mira del arma.
//  • Look At: la cabeza sigue al objetivo (enemigo más cercano o mira).
//  • Body Lean: el tronco se inclina en curvas del vehículo.
//
//  Requiere Animator con Humanoid Avatar configurado.
//  Si el Avatar no está listo, el sistema se silencia sin errores.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

[RequireComponent(typeof(Animator))]
public class SistemaIKProcedural : MonoBehaviour
{
    // ── Configuración ─────────────────────────────────────────────────────
    [Header("Foot IK")]
    public bool  footIKActivo    = true;
    public float alturaSuelo     = 0.08f;  // offset sobre el suelo
    public float pesoIK          = 1.0f;
    public float velocidadAdapt  = 8f;     // suavizado de altura del cuerpo
    public LayerMask mascaraSuelo = ~0;

    [Header("Look At")]
    public bool  lookAtActivo    = true;
    public float radioObjetivo   = 20f;
    public float pesoMirada      = 0.6f;

    [Header("Hand IK (arma)")]
    public Transform  puntoAgarre;  // asignar en Inspector: el grip del arma
    public bool       handIKActivo = true;

    // ── Estado ────────────────────────────────────────────────────────────
    Animator _anim;
    float    _alturaIzq, _alturaDer; // alturas IK pie
    float    _offsetCuerpo;
    Vector3  _objetivoMirada;
    bool     _tieneAvatar;

    // Datos de raycast por pie
    bool     _sueloIzq, _sueloDer;
    Vector3  _posIzq, _posDer;
    Vector3  _normalIzq, _normalDer;

    // ════════════════════════════════════════════════════════════════════════

    void Start()
    {
        _anim = GetComponent<Animator>();
        _tieneAvatar = _anim != null && _anim.isHuman;
        if (!_tieneAvatar)
            AlsasuaLogger.Info("IK", $"{name}: sin Avatar Humanoid — IK desactivado");
    }

    void Update()
    {
        if (!_tieneAvatar) return;
        ActualizarRaycastPies();
        ActualizarObjetivoMirada();
        AplicarOffsetCuerpo();
    }

    // ── Raycast por pie ───────────────────────────────────────────────────

    void ActualizarRaycastPies()
    {
        RaycastPie(AvatarIKGoal.LeftFoot,  ref _sueloIzq, ref _posIzq,  ref _normalIzq,  ref _alturaIzq);
        RaycastPie(AvatarIKGoal.RightFoot, ref _sueloDer, ref _posDer,  ref _normalDer, ref _alturaDer);
    }

    void RaycastPie(AvatarIKGoal pie, ref bool enSuelo,
                    ref Vector3 pos, ref Vector3 normal, ref float altura)
    {
        Vector3 origen = _anim.GetIKPosition(pie) + Vector3.up * 0.5f;
        if (Physics.Raycast(origen, Vector3.down, out var hit, 1.5f, mascaraSuelo))
        {
            enSuelo = true;
            pos     = hit.point;
            normal  = hit.normal;
            float meta = hit.point.y + alturaSuelo;
            altura = Mathf.Lerp(altura, meta, Time.deltaTime * velocidadAdapt);
        }
        else enSuelo = false;
    }

    // ── Offset del cuerpo ─────────────────────────────────────────────────

    void AplicarOffsetCuerpo()
    {
        if (!_sueloIzq && !_sueloDer) { _offsetCuerpo = Mathf.Lerp(_offsetCuerpo, 0f, Time.deltaTime * 5f); return; }
        float meta = Mathf.Min(
            _sueloIzq ? _alturaIzq : float.MaxValue,
            _sueloDer ? _alturaDer : float.MaxValue) - transform.position.y;
        meta = Mathf.Clamp(meta, -0.3f, 0.1f);
        _offsetCuerpo = Mathf.Lerp(_offsetCuerpo, meta, Time.deltaTime * velocidadAdapt);
    }

    // ── Objetivo de mirada ────────────────────────────────────────────────

    void ActualizarObjetivoMirada()
    {
        // Buscar enemigo más cercano
        Vector3 mejorPos = transform.position + transform.forward * 5f; // default: frente
        float   mejorDist = radioObjetivo;
        var policia = Physics.OverlapSphere(transform.position, radioObjetivo,
            LayerMask.GetMask("Default"));
        foreach (var c in policia)
        {
            if (c.GetComponent<PoliciaForalIA>() == null && c.GetComponent<EnemigoPatrulla>() == null) continue;
            float d = Vector3.Distance(transform.position, c.transform.position);
            if (d < mejorDist) { mejorDist = d; mejorPos = c.transform.position + Vector3.up * 1.6f; }
        }
        _objetivoMirada = Vector3.Lerp(_objetivoMirada, mejorPos, Time.deltaTime * 4f);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  ANIMATOR IK
    // ════════════════════════════════════════════════════════════════════════

    void OnAnimatorIK(int layerIndex)
    {
        if (!_tieneAvatar || _anim == null) return;

        // ── Foot IK ───────────────────────────────────────────────────────
        if (footIKActivo)
        {
            AplicarIKPie(AvatarIKGoal.LeftFoot,  _sueloIzq, _alturaIzq, _normalIzq);
            AplicarIKPie(AvatarIKGoal.RightFoot, _sueloDer, _alturaDer,  _normalDer);

            // Offset del cuerpo
            _anim.bodyPosition = _anim.bodyPosition + new Vector3(0, _offsetCuerpo, 0);
        }

        // ── Look At ───────────────────────────────────────────────────────
        if (lookAtActivo)
        {
            _anim.SetLookAtPosition(_objetivoMirada);
            _anim.SetLookAtWeight(pesoMirada, 0.1f, 0.8f, 0.6f, 0.5f);
        }

        // ── Hand IK (arma) ────────────────────────────────────────────────
        if (handIKActivo && puntoAgarre != null)
        {
            _anim.SetIKPositionWeight(AvatarIKGoal.RightHand, 0.85f);
            _anim.SetIKRotationWeight(AvatarIKGoal.RightHand, 0.85f);
            _anim.SetIKPosition(AvatarIKGoal.RightHand, puntoAgarre.position);
            _anim.SetIKRotation(AvatarIKGoal.RightHand, puntoAgarre.rotation);
        }
    }

    void AplicarIKPie(AvatarIKGoal pie, bool enSuelo, float altura, Vector3 normal)
    {
        float peso = enSuelo ? pesoIK : 0f;
        _anim.SetIKPositionWeight(pie, peso);
        _anim.SetIKRotationWeight(pie, peso);

        if (!enSuelo) return;

        // Posición: Y desde raycast
        Vector3 pos = _anim.GetIKPosition(pie);
        pos.y = altura;
        _anim.SetIKPosition(pie, pos);

        // Rotación: alinear con la normal del suelo
        Quaternion rot = Quaternion.FromToRotation(Vector3.up, normal)
                       * transform.rotation;
        _anim.SetIKRotation(pie, rot);
    }

    // ── Body lean en vehículo ─────────────────────────────────────────────
    // Llamado desde ControladorVehiculoJugador si quiere
    public void SetBodyLean(float lateral)
    {
        if (!_tieneAvatar || _anim == null) return;
        // Mover el cuerpo lateralmente (lean en curvas)
        var bodyPos = _anim.bodyPosition;
        bodyPos += transform.right * lateral * 0.08f;
        _anim.bodyPosition = bodyPos;
    }
}
