// Assets/Scripts/SistemaRagdoll.cs
// ═══════════════════════════════════════════════════════════════════════════
//  RAGDOLL PROCEDURAL — muerte física de NPCs y jugador
//
//  Si el modelo tiene rig Humanoid configurado, activa sus Rigidbodies/
//  Colliders al morir. Si no, crea un ragdoll de cajas procedural
//  (tronco + cabeza + 4 extremidades) que simula la caída.
//
//  Uso: SistemaRagdoll.Activar(transform, fuerzaImpacto, dirImpacto);
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using System.Collections.Generic;

public static class SistemaRagdoll
{
    // ── Nombres estándar de huesos Humanoid ───────────────────────────────
    static readonly string[] HUESOS_CUERPO = {
        "Hips","Spine","Chest","UpperChest","Neck","Head",
        "LeftUpperArm","LeftLowerArm","LeftHand",
        "RightUpperArm","RightLowerArm","RightHand",
        "LeftUpperLeg","LeftLowerLeg","LeftFoot",
        "RightUpperLeg","RightLowerLeg","RightFoot"
    };

    // ════════════════════════════════════════════════════════════════════════
    //  API PÚBLICA
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Activa ragdoll en el transform dado.
    /// Si tiene huesos Humanoid → activa sus Rigidbodies.
    /// Si no → crea ragdoll de cajas procedural.
    /// </summary>
    public static void Activar(Transform raiz, float fuerzaImpacto = 3f,
                               Vector3 dirImpacto = default)
    {
        if (raiz == null) return;

        // Desactivar scripts de IA y animación
        DesactivarControladores(raiz);

        // Intentar ragdoll real con huesos
        var huesosEncontrados = BuscarHuesos(raiz);
        if (huesosEncontrados.Count >= 4)
            ActivarRagdollReal(huesosEncontrados, fuerzaImpacto, dirImpacto);
        else
            CrearRagdollProcedural(raiz, fuerzaImpacto, dirImpacto);

        // Destruir tras 12 segundos
        if (Application.isPlaying)
            Object.Destroy(raiz.gameObject, 12f);
    }

    // ── Desactivar controladores ──────────────────────────────────────────

    static void DesactivarControladores(Transform raiz)
    {
        // Desactivar NavMeshAgent
        var nav = raiz.GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (nav != null) nav.enabled = false;

        // Desactivar scripts de IA
        foreach (var mb in raiz.GetComponents<MonoBehaviour>())
        {
            if (mb is SistemaRagdoll_Marker) continue; // no tocar el marcador
            var tipo = mb.GetType().Name;
            if (tipo.Contains("IA") || tipo.Contains("NPC") ||
                tipo.Contains("Policia") || tipo.Contains("Enemigo") ||
                tipo.Contains("Controlador")) mb.enabled = false;
        }

        // Desactivar Animator
        var anim = raiz.GetComponent<Animator>();
        if (anim != null) anim.enabled = false;

        // Desactivar CharacterController si existe
        var cc = raiz.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
    }

    // ── Ragdoll con huesos reales ─────────────────────────────────────────

    static List<Rigidbody> BuscarHuesos(Transform raiz)
    {
        var lista = new List<Rigidbody>();
        foreach (string nombre in HUESOS_CUERPO)
        {
            var hueso = raiz.Find(nombre);
            if (hueso == null)
            {
                // Búsqueda recursiva por nombre
                hueso = BuscarRecursivo(raiz, nombre);
            }
            if (hueso == null) continue;
            var rb = hueso.GetComponent<Rigidbody>() ?? hueso.gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = false; rb.mass = 5f;
            if (hueso.GetComponent<Collider>() == null)
            {
                var col = hueso.gameObject.AddComponent<CapsuleCollider>();
                col.radius = 0.08f; col.height = 0.2f;
            }
            lista.Add(rb);
        }
        return lista;
    }

    static Transform BuscarRecursivo(Transform padre, string nombre)
    {
        foreach (Transform hijo in padre)
        {
            if (hijo.name.Contains(nombre)) return hijo;
            var res = BuscarRecursivo(hijo, nombre);
            if (res != null) return res;
        }
        return null;
    }

    static void ActivarRagdollReal(List<Rigidbody> huesos, float fuerza, Vector3 dir)
    {
        if (dir == default) dir = Random.insideUnitSphere.normalized;
        dir.y = Mathf.Abs(dir.y) * 0.3f + 0.3f; // empuje hacia arriba

        foreach (var rb in huesos)
        {
            if (rb == null) continue;
            rb.isKinematic = false;
            rb.AddForce(dir * fuerza * Random.Range(0.7f, 1.3f), ForceMode.Impulse);
            rb.AddTorque(Random.insideUnitSphere * fuerza * 0.5f, ForceMode.Impulse);
        }
    }

    // ── Ragdoll procedural (sin huesos reales) ────────────────────────────

    static void CrearRagdollProcedural(Transform raiz, float fuerza, Vector3 dir)
    {
        // Desactivar renderer original
        foreach (var r in raiz.GetComponentsInChildren<Renderer>())
            r.enabled = false;

        // Crear muñeco de cajas
        var partes = new (Vector3 offset, Vector3 escala, float masa)[]
        {
            (new(0f,  0.9f, 0f), new(0.35f, 0.55f, 0.22f), 30f), // tronco
            (new(0f,  1.55f,0f), new(0.22f, 0.22f, 0.22f), 5f),  // cabeza
            (new(-0.3f,0.8f,0f), new(0.12f, 0.45f, 0.12f), 4f),  // brazo izq
            (new( 0.3f,0.8f,0f), new(0.12f, 0.45f, 0.12f), 4f),  // brazo der
            (new(-0.15f,0.3f,0f),new(0.15f, 0.5f, 0.15f), 8f),   // pierna izq
            (new( 0.15f,0.3f,0f),new(0.15f, 0.5f, 0.15f), 8f),   // pierna der
        };

        // Material del NPC para las cajas
        var matNPC = raiz.GetComponentInChildren<Renderer>()?.sharedMaterial;
        var matCaja = matNPC != null ? matNPC
            : new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"))
              { color = new Color(0.55f, 0.42f, 0.30f) };

        if (dir == default) dir = Random.insideUnitSphere.normalized;
        dir.y = 0.4f;

        foreach (var (offset, escala, masa) in partes)
        {
            var parte = GameObject.CreatePrimitive(PrimitiveType.Cube);
            parte.name = "RagdollParte";
            parte.transform.SetParent(raiz);
            parte.transform.localPosition = offset;
            parte.transform.localScale    = escala;
            parte.GetComponent<Renderer>().sharedMaterial = matCaja;

            var rb = parte.AddComponent<Rigidbody>();
            rb.mass = masa;
            rb.linearDamping = 0.5f; rb.angularDamping = 0.5f;
            rb.AddForce(dir * fuerza * Random.Range(0.8f, 1.2f), ForceMode.Impulse);
            rb.AddTorque(Random.insideUnitSphere * fuerza * 0.8f, ForceMode.Impulse);
        }

        // Marcar como ragdoll creado
        raiz.gameObject.AddComponent<SistemaRagdoll_Marker>();
    }
}

// Marcador para evitar doble activación
public class SistemaRagdoll_Marker : MonoBehaviour { }
