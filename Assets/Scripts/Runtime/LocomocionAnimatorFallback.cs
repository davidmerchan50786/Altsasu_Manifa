// Assets/Scripts/Runtime/LocomocionAnimatorFallback.cs
// ═══════════════════════════════════════════════════════════════════════════
//  LOCOMOCIÓN — fallback sin Motion Matching (Locomoción Fase 1)
//
//  Diseño: Docs/arquitectura_locomocion.md §2.1, §7 (fase 3)
//
//  Implementa ILocomocionAvanzada derivando Fase/VelocidadActual de la
//  trayectoria deseada (IProveedorTrayectoria) con umbrales simples — sin
//  pose search ni root motion. Es el "detrás de ALSASUA_MOTIONMATCHING":
//  cuando ese define exista, un LocomocionMotionMatching con el mismo
//  contrato lo sustituirá sin que los consumidores cambien.
//
//  ControladorJugador.Awake() añade este componente automáticamente, lo conecta
//  a ProveedorTrayectoriaInput y lo expone vía ControladorJugador.Locomocion.
//  ADITIVO: no toca el Animator/CharacterController — ControladorJugador sigue
//  gestionando sus propias animaciones (ActualizarAnimaciones). Este componente
//  solo EXPONE Fase/VelocidadActual para que futuros consumidores (Foot IK
//  avanzado, audio, HUD, Motion Matching real) decidan sin duplicar la lógica
//  de movimiento.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

public sealed class LocomocionAnimatorFallback : MonoBehaviour, ILocomocionAvanzada
{
    [Tooltip("Velocidad (m/s) por debajo de la cual se considera Quieto.")]
    [SerializeField] float umbralQuieto = 0.15f;

    [Tooltip("Velocidad (m/s) a partir de la cual se considera Corriendo.")]
    [SerializeField] float umbralCorrer = 4.5f;

    [Tooltip("Deceleración (m/s²) que se interpreta como Frenando.")]
    [SerializeField] float umbralFrenada = 6f;

    // Opcional: si el GameObject tiene ControladorJugador, se usa para detectar
    // el estado de suelo (FaseLocomocion.EnAire). Si no existe, se asume en suelo.
    ControladorJugador _jugador;
    IProveedorTrayectoria _proveedor;
    float _velAnterior;

    public Transform Transformacion => transform;
    public FaseLocomocion Fase { get; private set; }
    public float VelocidadActual { get; private set; }

    void Awake() => _jugador = GetComponent<ControladorJugador>();

    public void Conectar(IProveedorTrayectoria proveedor) => _proveedor = proveedor;

    // Sin pose search que corregir: el fallback no usa la realimentación física,
    // pero la conserva para que un futuro LocomocionMotionMatching con el mismo
    // contrato pueda sustituir este componente sin tocar consumidores.
    public void RealimentarDesplazamientoReal(Vector3 desplazamientoMundo) { }

    void Update()
    {
        if (_proveedor == null) return;

        var tr = _proveedor.MuestrearAhora();
        float vObjetivo = tr.velocidadFinal;
        float dv = (vObjetivo - _velAnterior) / Mathf.Max(Time.deltaTime, 1e-4f);
        _velAnterior = vObjetivo;
        VelocidadActual = vObjetivo;

        bool enSuelo = _jugador == null || _jugador.EstaEnSueloP;

        Fase = !enSuelo                ? FaseLocomocion.EnAire
             : vObjetivo < umbralQuieto ? FaseLocomocion.Quieto
             : dv < -umbralFrenada      ? FaseLocomocion.Frenando
             : vObjetivo >= umbralCorrer ? FaseLocomocion.Corriendo
             : FaseLocomocion.Andando;
    }
}
