// Assets/Scripts/SistemaVidaNocturna.cs
// ═══════════════════════════════════════════════════════════════════════════
//  VIDA NOCTURNA — ventanas con emisión, farolas, ambiente nocturno
//
//  • Ventanas de edificios emiten luz cálida por la noche
//  • Farolas HDRP con lens flare y encendido/apagado según hora
//  • Coches con faros al anochecer
//  • Ciclo día/noche integrado con SistemaAtmosfera
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using System.Collections.Generic;

public class SistemaVidaNocturna : SingletonMono<SistemaVidaNocturna>
{


    // ── Estado ────────────────────────────────────────────────────────────
    bool  _esNoche;
    float _hora;
    float _transicion; // 0=día, 1=noche
    float _timer;

    // ── Ventanas ──────────────────────────────────────────────────────────
    readonly List<Renderer> _ventanas      = new();
    readonly List<Material> _matsVentana   = new();
    bool _ventanasRegistradas;

    // ── Farolas (registradas por SistemaFarolas) ──────────────────────────
    readonly List<Light>                 _farolas   = new();
    readonly List<HDAdditionalLightData> _hdFarolas = new();

    // ── Configuración ─────────────────────────────────────────────────────
    const float HORA_ANOCHECER = 19.5f;
    const float HORA_AMANECER  = 6.5f;
    static readonly Color COLOR_VENTANA_NOCHE  = new Color(1.0f, 0.92f, 0.68f); // amarillo cálido
    static readonly Color COLOR_VENTANA_DIA    = new Color(0.1f, 0.12f, 0.2f);  // azul oscuro (reflejo cielo)
    const float INTENSIDAD_FAROLA_NOCHE = 800f;  // lux HDRP
    const float INTENSIDAD_FAROLA_DIA   = 0f;

    // ════════════════════════════════════════════════════════════════════════

    void Start()
    {
        // Suscribir al ciclo día/noche
        var atm = AltsasuCore.I?.atmosferaSystem;
        if (atm != null) atm.OnCambioDia += OnCambioDia;
        RegistrarFarolas();
        InvokeRepeating(nameof(BuscarVentanas), 3f, 10f);
    }

    void Update()
    {
        _timer += Time.deltaTime;
        if (_timer < 0.5f) return; // actualizar cada 0.5s
        _timer = 0f;

        ActualizarHora();
        ActualizarFarolas();
        ActualizarVentanas();
        ActualizarVehiculosFaros();
    }

    // ── Hora del día ──────────────────────────────────────────────────────

    void ActualizarHora()
    {
        var atm = AltsasuCore.I?.atmosferaSystem;
        _hora = atm != null ? atm.HoraDelDia : 12f;
        bool noche = _hora < HORA_AMANECER || _hora > HORA_ANOCHECER;
        float target = noche ? 1f : 0f;
        _transicion = Mathf.MoveTowards(_transicion, target, 0.5f * 0.01f); // muy gradual
        _esNoche    = noche;
    }

    // ── Farolas ───────────────────────────────────────────────────────────

    void RegistrarFarolas()
    {
        var farolas = FindObjectsByType<Light>(FindObjectsSortMode.None);
        foreach (var l in farolas)
            if (l.gameObject.name.ToLower().Contains("farola") ||
                l.gameObject.name.ToLower().Contains("streetlight") ||
                l.gameObject.name.ToLower().Contains("lamppost"))
            {
                _farolas.Add(l);
                _hdFarolas.Add(l.GetComponent<HDAdditionalLightData>());
            }

        AlsasuaLogger.Info("VidaNocturna", $"Farolas registradas: {_farolas.Count}");
    }

    void ActualizarFarolas()
    {
        float target = Mathf.Lerp(INTENSIDAD_FAROLA_DIA, INTENSIDAD_FAROLA_NOCHE, _transicion);
        float parpadeo = _transicion > 0.05f && _transicion < 0.3f
            ? 1f + Mathf.Sin(Time.time * 18f) * 0.15f : 1f;
        float intensidad = target * parpadeo;
        for (int i = 0; i < _farolas.Count; i++)
        {
            var l = _farolas[i];
            if (l == null) continue;
            l.enabled = intensidad > 1f;
            var hd = _hdFarolas[i];
            if (hd != null) hd.intensity = intensidad;
            else l.intensity = intensidad;
        }
    }

    // ── Ventanas ──────────────────────────────────────────────────────────

    void BuscarVentanas()
    {
        if (_ventanasRegistradas && _ventanas.Count > 0) return;
        _ventanas.Clear();
        _matsVentana.Clear();

        var todos = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        foreach (var r in todos)
        {
            if (r == null) continue;
            string nombre = r.gameObject.name.ToLower();
            if (!nombre.Contains("ventana") && !nombre.Contains("window") &&
                !nombre.Contains("cristal") && !nombre.Contains("glass")) continue;

            // Crear material instancia para modificar emisión
            var mat = r.material; // crea instancia
            if (mat == null) continue;
            _ventanas.Add(r);
            _matsVentana.Add(mat);
        }

        if (_ventanas.Count > 0)
        {
            _ventanasRegistradas = true;
            AlsasuaLogger.Info("VidaNocturna", $"Ventanas detectadas: {_ventanas.Count}");
        }
    }

    void ActualizarVentanas()
    {
        Color colorActual = Color.Lerp(COLOR_VENTANA_DIA, COLOR_VENTANA_NOCHE, _transicion);
        float emision = Mathf.Lerp(0f, 2.5f, _transicion); // intensidad HDR

        for (int i = 0; i < _ventanas.Count; i++)
        {
            if (_ventanas[i] == null || _matsVentana[i] == null) continue;
            var mat = _matsVentana[i];

            // Algunas ventanas están apagadas (aleatoriedad basada en hash)
            float rand = Mathf.PerlinNoise(i * 0.137f, 0f);
            bool encendida = rand > 0.35f; // 65% de ventanas encendidas de noche

            Color emitColor = encendida ? colorActual * emision : Color.black;
            if (mat.HasProperty("_EmissiveColor"))
                mat.SetColor("_EmissiveColor", emitColor);      // HDRP
            else if (mat.HasProperty("_EmissionColor"))
                mat.SetColor("_EmissionColor", emitColor);      // URP / Standard
        }
    }

    // ── Coches con faros ──────────────────────────────────────────────────

    void ActualizarVehiculosFaros()
    {
        if (!_esNoche) return;
        var coches = FindObjectsByType<VehiculoNPC>(FindObjectsSortMode.None);
        foreach (var c in coches)
        {
            if (c == null) continue;
            // Activar luces del vehículo si existen, o crear luz simple
            var luces = c.GetComponentsInChildren<Light>();
            foreach (var l in luces)
                if (l.gameObject.name.ToLower().Contains("faro") ||
                    l.gameObject.name.ToLower().Contains("headlight"))
                    l.enabled = _esNoche;
        }
    }

    // ── Callbacks ─────────────────────────────────────────────────────────

    void OnCambioDia(bool esDia)
    {
        AlsasuaLogger.Info("VidaNocturna", esDia ? "Amanecer — apagando farolas" : "Anochecer — encendiendo farolas");
    }

    public void RegistrarFarola(Light l)
    {
        if (l == null || _farolas.Contains(l)) return;
        _farolas.Add(l);
        _hdFarolas.Add(l.GetComponent<HDAdditionalLightData>());
    }

    void OnDestroy()
    {
        var atm = AltsasuCore.I?.atmosferaSystem;
        if (atm != null) atm.OnCambioDia -= OnCambioDia;
    }
}
