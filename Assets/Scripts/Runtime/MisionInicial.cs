// Assets/Scripts/MisionInicial.cs
// ═══════════════════════════════════════════════════════════════════════════
//  M00 — ESNATU, ALTSASU (misión inicial / tutorial)
//
//  Primera misión del juego. Enseña los controles básicos y lleva al
//  jugador hasta Herriko Plaza, donde le espera el grupo. Al completarse
//  encadena con Mision_RobarCoche (M01) — la cadena completa M00→M12.
//
//  Flujo:
//    1. El jugador despierta en su portal (Nafarroa Kalea, ~140m de la plaza)
//    2. Aprende a moverse (WASD/ratón) — completado al alejarse 12m
//    3. Camina hasta Herriko Plaza (marcador de misión)
//    4. Se reúne con el grupo: permanecer 6s en la plaza
//
//  Integración:
//    - SistemaTutorial.Mostrar() para las pistas de control
//    - SistemaApoyoPopular al unirse al grupo
//    - MisionHelper (economía) — recompensa simbólica
//    - La inicia SistemaMisiones.Start() (configurable con saltarIntro)
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using UnityEngine;

public class Mision_Inicial : Mision
{
    public override string Nombre => "Esnatu, Altsasu — El Despertar";

    // Portal de inicio: calle del casco viejo al este de la plaza
    static Vector3 PortalCasa
    {
        get
        {
            var p = GeoDataAlsasua.HerrikoPlaza + new Vector3(140f, 0f, -55f);
            p.y = GeoDataAlsasua.AlturaTerreno(p) + 1.2f;
            return p;
        }
    }

    Vector3 _spawn;
    float   _timerGrupo;

    const float RADIO_PLAZA   = 30f;   // distancia para "haber llegado"
    const float TIEMPO_GRUPO  = 6f;    // segundos junto al grupo

    public override System.Action AlIniciar => () =>
    {
        // Colocar al jugador en su portal (si el bootstrapper lo dejó en la plaza)
        var jugador = AltsasuCore.Jugador;
        if (jugador != null)
        {
            var cc = jugador.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;        // CharacterController bloquea teleports
            jugador.position = PortalCasa;
            if (cc != null) cc.enabled = true;
        }
        _spawn      = PortalCasa;
        _timerGrupo = 0f;

        // Pistas de control
        SistemaTutorial.Mostrar(SistemaTutorial.Pista.Movimiento);
        SistemaTutorial.Mostrar(SistemaTutorial.Pista.Camara);

        AlsasuaLogger.Info("M00", "Misión inicial: sal del portal y ve a Herriko Plaza");
    };

    public override List<Objetivo> Objetivos => new List<Objetivo>
    {
        new Objetivo
        {
            Descripcion = "Muévete por la calle — WASD y ratón para la cámara",
            Condicion   = () =>
                PuntosAlsasua.Dist2D(PuntosAlsasua.JugadorPos(), _spawn) > 12f,
            AlCompletar = () =>
            {
                SistemaTutorial.Mostrar(SistemaTutorial.Pista.Sprint);
                AlsasuaLogger.Info("M00", "Controles aprendidos — ahora a la plaza");
            }
        },
        new Objetivo
        {
            Descripcion = "Ve a Herriko Plaza — el grupo te espera junto a la fuente",
            Condicion   = () =>
                PuntosAlsasua.Dist2D(PuntosAlsasua.JugadorPos(),
                                     PuntosAlsasua.HerrikoPlaza) < RADIO_PLAZA,
            AlCompletar = () =>
            {
                MisionHelper.GanarDinero(50);
                AlsasuaLogger.Info("M00", "Has llegado a la plaza");
            }
        },
        new Objetivo
        {
            Descripcion = "Reúnete con el grupo — quédate en la plaza unos segundos",
            Condicion   = () =>
            {
                bool enPlaza = PuntosAlsasua.Dist2D(PuntosAlsasua.JugadorPos(),
                                   PuntosAlsasua.HerrikoPlaza) < RADIO_PLAZA + 10f;
                _timerGrupo = enPlaza ? _timerGrupo + Time.deltaTime : 0f;
                return _timerGrupo >= TIEMPO_GRUPO;
            },
            AlCompletar = () =>
            {
                SistemaApoyoPopular.Instance?.SumarApoyo(30f, "Te has unido al grupo de la plaza");
                MisionHelper.GanarDinero(100);
                SistemaTutorial.Mostrar(SistemaTutorial.Pista.EntrarVehiculo);
                AlsasuaLogger.Info("M00", "El grupo te cuenta el plan: necesitáis el coche patrulla…");
            }
        }
    };

    // Encadena con la cadena existente M01 → M12
    public override Mision SiguienteMision => new Mision_RobarCoche();
}
