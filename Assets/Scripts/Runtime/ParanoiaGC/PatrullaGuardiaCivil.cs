// Assets/Scripts/_ParanoiaGC~/PatrullaGuardiaCivil.cs  (STAGING/DRAFT — carpeta ~ no se compila)
// ─────────────────────────────────────────────────────────────────────────────
//  Comportamiento de coche patrulla GC: enciende rotativo + sirena y persigue al
//  jugador cuando hay búsqueda (wanted). Lo habilita ConvertibleGuardiaCivil al
//  convertir un coche. Movimiento básico (MoveTowards) como scaffold; engancha
//  tu VehiculoBase real en los puntos //★.
// ─────────────────────────────────────────────────────────────────────────────
using UnityEngine;

public class PatrullaGuardiaCivil : MonoBehaviour
{
    [Header("Persecución")]
    public float velPatrulla = 6f;
    public float velPersecucion = 13f;
    public float rangoPersecucion = 60f;

    [Header("Rotativo (hijo) y sirena")]
    [Tooltip("Transform del rotativo a girar (hijo). Si null, busca 'Rotativo'.")]
    public Transform rotativo;
    public Light luzAzul;            // si null, busca un Light en el rotativo
    public AudioSource sirena;       // si null, busca un AudioSource

    void OnEnable()
    {
        if (!rotativo) rotativo = BuscarHijo("Rotativo");
        if (rotativo)
        {
            rotativo.gameObject.SetActive(true);
            if (!luzAzul) luzAzul = rotativo.GetComponentInChildren<Light>(true);
        }
        if (!sirena) sirena = GetComponent<AudioSource>();
        if (sirena) { sirena.loop = true; if (!sirena.isPlaying) sirena.Play(); }
    }

    void OnDisable()
    {
        if (rotativo) rotativo.gameObject.SetActive(false);
        if (sirena && sirena.isPlaying) sirena.Stop();
    }

    void Update()
    {
        // Rotativo girando + parpadeo azul (luz de emergencia).
        if (rotativo) rotativo.Rotate(0f, 720f * Time.deltaTime, 0f, Space.Self);
        if (luzAzul)  luzAzul.intensity = 2f + 1.5f * Mathf.Abs(Mathf.Sin(Time.time * 8f));

        var wanted = ServiceLocator.Get<IWantedSystem>();
        int nivel = wanted?.NivelBusqueda ?? 0;

        Vector3 jug = GeoDataAlsasua.JugadorPos();
        if (jug == Vector3.zero) return;
        float d = GeoDataAlsasua.Dist2D(transform.position, jug);

        bool persigue = nivel > 0 && d < rangoPersecucion;
        float vel = persigue ? velPersecucion : velPatrulla;

        if (persigue)
        {
            // ★ Si tienes VehiculoBase/controlador, mándale el destino en vez de mover el transform.
            Vector3 obj = new Vector3(jug.x, transform.position.y, jug.z);
            transform.position = Vector3.MoveTowards(transform.position, obj, vel * Time.deltaTime);
            Vector3 mir = obj - transform.position; mir.y = 0;
            if (mir.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(mir), 4f * Time.deltaTime);
        }
        // sirena más aguda en persecución
        if (sirena) sirena.pitch = persigue ? 1.15f : 1.0f;
    }

    Transform BuscarHijo(string nombre)
    {
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (t.name == nombre) return t;
        return null;
    }
}
