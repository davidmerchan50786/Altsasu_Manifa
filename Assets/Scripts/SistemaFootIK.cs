// Assets/Scripts/SistemaFootIK.cs
// ═══════════════════════════════════════════════════════════════════════════
//  FOOT IK — los pies se plantan en el terreno y se adaptan a su pendiente
//  (Blueprint AAA+++, Pilar Game Feel §5.2 — Fase 3)
//
//  Elimina el deslizamiento y el "flotar" en rampas/escaleras: por cada pie,
//  raycast al suelo y se coloca el goal de IK en el punto de contacto, alineando
//  la rotación con la normal del terreno.
//
//  REQUISITOS (auto-seguro si no se cumplen):
//   · Animator HUMANOIDE (avatar Humanoid). Con cuerpo procedural o avatar
//     Generic, el componente se autodesactiva (la animación de caminar
//     procedural de ControladorJugador cubre ese caso).
//   · En el Animator Controller, marca "IK Pass" en la capa base
//     (Layers ▸ engranaje ▸ IK Pass). Sin eso, OnAnimatorIK no se invoca y el
//     componente simplemente no hace nada (no rompe nada).
//
//  ControladorJugador añade este componente automáticamente al personaje Mixamo
//  cuando detecta un Animator. También puedes añadirlo a mano a cualquier
//  GameObject que tenga un Animator humanoide.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

[RequireComponent(typeof(Animator))]
public class SistemaFootIK : MonoBehaviour
{
    [Header("Pesos de IK")]
    [Range(0f, 1f)] public float pesoPosicion = 1f;
    [Range(0f, 1f)] public float pesoRotacion = 1f;

    [Header("Raycast al suelo")]
    [Tooltip("Distancia de búsqueda del suelo bajo el pie (m).")]
    public float distRaycast = 0.7f;
    [Tooltip("Altura del tobillo sobre el punto de contacto (m).")]
    public float offsetSuelo = 0.06f;
    [Tooltip("Capas consideradas suelo.")]
    public LayerMask capaSuelo = ~0;

    Animator _anim;
    bool _humanoide;

    void Awake()
    {
        _anim = GetComponent<Animator>();
        _humanoide = _anim != null && _anim.isHuman;
        if (!_humanoide)
        {
            AlsasuaLogger.Info("FootIK", "Animator no humanoide — Foot IK desactivado (auto-no-op).");
            enabled = false;
        }
    }

    // Unity llama esto solo si la capa del Animator tiene "IK Pass" activado.
    void OnAnimatorIK(int layer)
    {
        if (!_humanoide) return;
        ResolverPie(AvatarIKGoal.LeftFoot,  HumanBodyBones.LeftFoot);
        ResolverPie(AvatarIKGoal.RightFoot, HumanBodyBones.RightFoot);
    }

    void ResolverPie(AvatarIKGoal goal, HumanBodyBones hueso)
    {
        var pie = _anim.GetBoneTransform(hueso);
        if (pie == null) return;

        Vector3 origen = pie.position + Vector3.up * 0.5f;
        if (Physics.Raycast(origen, Vector3.down, out var hit,
                            0.5f + distRaycast, capaSuelo, QueryTriggerInteraction.Ignore))
        {
            _anim.SetIKPositionWeight(goal, pesoPosicion);
            _anim.SetIKPosition(goal, hit.point + Vector3.up * offsetSuelo);

            Quaternion rot = Quaternion.FromToRotation(Vector3.up, hit.normal) * _anim.GetIKRotation(goal);
            _anim.SetIKRotationWeight(goal, pesoRotacion);
            _anim.SetIKRotation(goal, rot);
        }
        else
        {
            // Sin suelo cerca → soltar el IK (deja la animación original).
            _anim.SetIKPositionWeight(goal, 0f);
            _anim.SetIKRotationWeight(goal, 0f);
        }
    }
}
