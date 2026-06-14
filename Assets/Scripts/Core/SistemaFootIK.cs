// Assets/Scripts/SistemaFootIK.cs
// ═══════════════════════════════════════════════════════════════════════════
//  FOOT IK — los pies se plantan en el terreno y se adaptan a su pendiente
//  (Blueprint AAA+++, Pilar Game Feel §5.2 — Fase 3)
//
//  Elimina el deslizamiento y el "flotar" en rampas/escaleras: por cada pie, el
//  suelo BASE se toma de TerrenoGlobal (matemático, capa Core — funciona SIN
//  TerrainCollider, p.ej. con el Mosaico V3 GPU-driven) y un raycast OPCIONAL lo
//  refina con props/escalones/parches JIT; el goal de IK se planta ahí y se alinea
//  con la normal del suelo (diferencias finitas del muestreo de alturas).
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

        Vector3 ppie = pie.position;

        // ── Suelo BASE: matemático (TerrenoGlobal, capa Core). Siempre disponible y
        //    tile-aware; funciona aunque NO exista TerrainCollider (Mosaico V3 GPU). ──
        float   ySuelo = TerrenoGlobal.AlturaMundo(ppie.x, ppie.z);
        Vector3 nSuelo = NormalTerreno(ppie.x, ppie.z);

        // ── Detalle FINO: props, bordillos, escalones, parches JIT — por raycast.
        //    Ya no necesita golpear el terreno; solo refina si hay algo por encima. ──
        Vector3 origen = ppie + Vector3.up * 0.5f;
        if (Physics.Raycast(origen, Vector3.down, out var hit,
                            0.5f + distRaycast, capaSuelo, QueryTriggerInteraction.Ignore)
            && hit.point.y > ySuelo)
        {
            ySuelo = hit.point.y;
            nSuelo = hit.normal;
        }

        // Pie en el aire (lejos del suelo) → soltar IK, deja la animación original.
        if (ppie.y - ySuelo > distRaycast)
        {
            _anim.SetIKPositionWeight(goal, 0f);
            _anim.SetIKRotationWeight(goal, 0f);
            return;
        }

        _anim.SetIKPositionWeight(goal, pesoPosicion);
        _anim.SetIKPosition(goal, new Vector3(ppie.x, ySuelo + offsetSuelo, ppie.z));

        Quaternion rot = Quaternion.FromToRotation(Vector3.up, nSuelo) * _anim.GetIKRotation(goal);
        _anim.SetIKRotationWeight(goal, pesoRotacion);
        _anim.SetIKRotation(goal, rot);
    }

    // Normal del terreno por diferencias finitas del muestreo de alturas (sin collider).
    static Vector3 NormalTerreno(float x, float z)
    {
        const float e = 1f;   // m
        float hL = TerrenoGlobal.AlturaMundo(x - e, z);
        float hR = TerrenoGlobal.AlturaMundo(x + e, z);
        float hD = TerrenoGlobal.AlturaMundo(x, z - e);
        float hU = TerrenoGlobal.AlturaMundo(x, z + e);
        return new Vector3(hL - hR, 2f * e, hD - hU).normalized;
    }
}
