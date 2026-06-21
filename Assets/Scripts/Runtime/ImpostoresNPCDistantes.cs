// Assets/Scripts/Runtime/ImpostoresNPCDistantes.cs
// ═══════════════════════════════════════════════════════════════════════════
//  IMPOSTORES NPC — Billboard GPU-instanced para NPCs más allá del LOD2
//
//  Más allá de ~65m los NPCs activos están en LOD2 (proxy cápsula) o culled.
//  Este sistema los sustituye visualmente por quads GPU-instanced coloreados
//  por facción: 1 draw call por facción por frame independientemente de cuántos
//  haya (DrawMeshInstanced, max 1023 por lote, se hacen múltiples lotes).
//
//  Colores de facción:
//    Civilian     → gris azulado
//    Manifestante → rojo oscuro (keffiyeh)
//    Jarrai       → negro/verde (capucha)
//    GuardiaCivil → verde oscuro (uniforme)
//
//  El radio de activación de impostores lo lee de GobernadorRender.RadioImpostor.
//  Por dentro de ese radio los LODGroups manejan todo; por fuera este sistema pinta.
//
//  Registrar NPCs: llama a Registrar(go, faccion) desde SpawnManifestante/SpawnCivil.
//  O activa escaneado automático con escaneoAutomatico = true.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using UnityEngine;
using UnityEngine.Jobs;

[DefaultExecutionOrder(210)]
public class ImpostoresNPCDistantes : MonoBehaviour
{
    public static ImpostoresNPCDistantes Instance { get; private set; }

    public enum Faccion { Civilian, Manifestante, Jarrai, GuardiaCivil }

    // ── Config Inspector ──────────────────────────────────────────────────
    [Header("Radio de activación (0 = leer de GobernadorRender)")]
    [SerializeField] float radioImpostorManual = 0f;

    [Header("Tamaño del quad impostor (metros)")]
    [SerializeField] float anchoNPC  = 0.5f;
    [SerializeField] float altoNPC   = 1.8f;

    [Header("Colores por facción")]
    [SerializeField] Color colorCivil        = new(0.55f, 0.60f, 0.65f, 1f);
    [SerializeField] Color colorManifestante = new(0.65f, 0.12f, 0.10f, 1f);
    [SerializeField] Color colorJarrai       = new(0.08f, 0.15f, 0.08f, 1f);
    [SerializeField] Color colorGuardia      = new(0.10f, 0.22f, 0.10f, 1f);

    [Header("Escaneo auto de escena (alternativa a registro manual)")]
    [SerializeField] bool escaneoAutomatico = true;
    [SerializeField] float intervaloEscaneo = 3f;

    // ── Estado ────────────────────────────────────────────────────────────
    struct EntradaNPC { public Transform tf; public Faccion faccion; }
    readonly List<EntradaNPC> _npcs = new();

    Mesh _quadMesh;
    Material[] _mats;      // 1 material por facción, GPU-instanced
    Camera _cam;
    float _radioImpostor;
    float _tEscaneo;

    static readonly int PropColor = Shader.PropertyToID("_BaseColor");

    // ── Boot ──────────────────────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        _quadMesh = CrearQuad();
        _mats = new Material[(int)Faccion.GuardiaCivil + 1];
        Color[] cols = { colorCivil, colorManifestante, colorJarrai, colorGuardia };
        for (int i = 0; i < _mats.Length; i++)
        {
            _mats[i] = new Material(Shader.Find("HDRP/Unlit") ?? Shader.Find("Unlit/Color"));
            _mats[i].enableInstancing = true;
            if (_mats[i].HasProperty(PropColor)) _mats[i].SetColor(PropColor, cols[i]);
            else _mats[i].color = cols[i];
        }
    }

    void Start()
    {
        _cam = Camera.main;
        if (escaneoAutomatico) EscanearEscena();
    }

    // ── API pública ───────────────────────────────────────────────────────
    public void Registrar(GameObject go, Faccion faccion)
    {
        if (go == null) return;
        _npcs.Add(new EntradaNPC { tf = go.transform, faccion = faccion });
    }

    public void Registrar(GameObject go, string tag)
    {
        Faccion f = tag switch
        {
            "Manifestante" => Faccion.Manifestante,
            "GuardiaCivil" => Faccion.GuardiaCivil,
            "Jarrai"       => Faccion.Jarrai,
            _              => Faccion.Civilian,
        };
        Registrar(go, f);
    }

    public void Desregistrar(GameObject go)
    {
        if (go == null) return;
        _npcs.RemoveAll(e => e.tf == null || e.tf.gameObject == go);
    }

    // ── Update ────────────────────────────────────────────────────────────
    void Update()
    {
        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return;

        // Leer radio del gobernador si no hay override
        _radioImpostor = radioImpostorManual > 0f
            ? radioImpostorManual
            : (GobernadorRender.Instancia != null ? GobernadorRender.Instancia.RadioImpostor : 65f);

        // Escaneo automático periódico
        if (escaneoAutomatico)
        {
            _tEscaneo -= Time.deltaTime;
            if (_tEscaneo <= 0f) { EscanearEscena(); _tEscaneo = intervaloEscaneo; }
        }

        // Limpiar entradas nulas
        _npcs.RemoveAll(e => e.tf == null);

        // Separar por facción, solo los que están más allá del radio impostor
        var porFaccion = new List<Matrix4x4>[_mats.Length];
        for (int i = 0; i < porFaccion.Length; i++) porFaccion[i] = new List<Matrix4x4>(64);

        Vector3 camPos = _cam.transform.position;
        foreach (var e in _npcs)
        {
            float dist = Vector3.Distance(e.tf.position, camPos);
            if (dist < _radioImpostor) continue; // el LODGroup lo gestiona

            // Quad orientado a cámara, con offset de altura para centrar en el pecho
            Vector3 pos = e.tf.position + Vector3.up * (altoNPC * 0.5f);
            Quaternion rot = Quaternion.LookRotation(camPos - pos, Vector3.up);
            Matrix4x4 m = Matrix4x4.TRS(pos, rot, new Vector3(anchoNPC, altoNPC, 1f));
            porFaccion[(int)e.faccion].Add(m);
        }

        // Draw por facción en lotes de 1023 (límite DrawMeshInstanced)
        for (int f = 0; f < _mats.Length; f++)
        {
            var lista = porFaccion[f];
            if (lista.Count == 0) continue;
            int offset = 0;
            while (offset < lista.Count)
            {
                int count = Mathf.Min(1023, lista.Count - offset);
                var lote = lista.GetRange(offset, count).ToArray();
                Graphics.DrawMeshInstanced(_quadMesh, 0, _mats[f], lote, count);
                offset += count;
            }
        }
    }

    // ── Escaneo auto ──────────────────────────────────────────────────────
    void EscanearEscena()
    {
        _npcs.Clear();
        string[] tags = { "Civilian", "Manifestante", "GuardiaCivil", "Jarrai" };
        foreach (var tag in tags)
        {
            try
            {
                var gos = GameObject.FindGameObjectsWithTag(tag);
                foreach (var go in gos) Registrar(go, tag);
            }
            catch { } // tag no definido → ignorar
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────
    static Mesh CrearQuad()
    {
        var mesh = new Mesh { name = "ImpostorNPC_Quad" };
        mesh.vertices  = new[] { new Vector3(-0.5f,0,0), new Vector3(0.5f,0,0),
                                  new Vector3(0.5f,1,0), new Vector3(-0.5f,1,0) };
        mesh.uv        = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
        mesh.triangles = new[] { 0,2,1, 0,3,2 };
        mesh.normals   = new[] { -Vector3.forward,-Vector3.forward,-Vector3.forward,-Vector3.forward };
        mesh.RecalculateBounds();
        return mesh;
    }

    void OnDestroy()
    {
        foreach (var m in _mats) if (m != null) Destroy(m);
    }
}
