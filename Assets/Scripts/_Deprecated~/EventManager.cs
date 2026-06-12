// Assets/Scripts/EventManager.cs
// ═══════════════════════════════════════════════════════════════════════════
//  EVENT MANAGER — índice centralizado de todos los eventos del juego
//
//  No reemplaza los eventos en sus clases originales (eso rompería las
//  suscripciones existentes). Actúa como:
//    1. Documentación viva: un único lugar donde ver todos los eventos.
//    2. Punto de dispatch: métodos estáticos que disparan los eventos
//       correctos sin que el emisor conozca al receptor.
//    3. Agregador: permite suscribirse a grupos de eventos con una sola llamada.
//
//  USO:
//    // Suscribirse a todos los eventos de misión de golpe:
//    EventManager.Misiones.SuscribirTodo(onIniciada, onObjetivo, onCompletada);
//
//    // O disparar un evento desde cualquier sistema sin acoplar:
//    EventManager.Mundo.MundoGenerado();
// ═══════════════════════════════════════════════════════════════════════════

using System;
using UnityEngine;

public static class EventManager
{
    // ════════════════════════════════════════════════════════════════════════
    //  MUNDO
    //  Generación y streaming del mundo OSM/terreno.
    // ════════════════════════════════════════════════════════════════════════
    public static class Mundo
    {
        /// <summary>El mundo OSM ha terminado de generarse. Suscriptores: SistemaEdificiosAAA, SistemaSueloAAA, SistemaNavMesh…</summary>
        public static event Action OnMundoGenerado
        {
            add    => GeneradorMundoOSM.OnMundoGenerado += value;
            remove => GeneradorMundoOSM.OnMundoGenerado -= value;
        }

        /// <summary>Una zona OSM ha sido cargada en memoria.</summary>
        public static event Action<Vector2Int> OnZonaCargada
        {
            add    => SistemaZonas.OnZonaCargada += value;
            remove => SistemaZonas.OnZonaCargada -= value;
        }

        /// <summary>Una zona OSM ha sido descargada.</summary>
        public static event Action<Vector2Int> OnZonaDescargada
        {
            add    => SistemaZonas.OnZonaDescargada += value;
            remove => SistemaZonas.OnZonaDescargada -= value;
        }

        /// <summary>El NavMesh ha terminado de hornearse — habilita agentes NPC.</summary>
        public static event Action OnNavMeshListo
        {
            add    => SistemaNavMesh.OnNavMeshListo += value;
            remove => SistemaNavMesh.OnNavMeshListo -= value;
        }

        /// <summary>El mundo completo está listo para gameplay (boot finalizado).</summary>
        public static event Action OnWorldReady
        {
            add    => AltsasuCore.OnWorldReady += value;
            remove => AltsasuCore.OnWorldReady -= value;
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  JUGADOR
    //  Vida, spawn, daño, vehículos.
    // ════════════════════════════════════════════════════════════════════════
    public static class Jugador
    {
        /// <summary>El jugador ha spawneado en escena por primera vez.</summary>
        public static event Action<Transform> OnSpawned
        {
            add    => AltsasuCore.OnJugadorSpawned += value;
            remove => AltsasuCore.OnJugadorSpawned -= value;
        }

        /// <summary>El jugador ha recibido daño. Parámetro: cantidad de daño.</summary>
        public static event Action<int> OnDano
        {
            add    => ControladorJugador.OnDanoRecibido += value;
            remove => ControladorJugador.OnDanoRecibido -= value;
        }

        /// <summary>El jugador ha entrado en un vehículo.</summary>
        public static event Action<ControladorVehiculoJugador> OnEntroVehiculo
        {
            add    => ControladorVehiculoJugador.OnJugadorEntro += value;
            remove => ControladorVehiculoJugador.OnJugadorEntro -= value;
        }

        /// <summary>El jugador ha salido de un vehículo.</summary>
        public static event Action<ControladorVehiculoJugador> OnSalioVehiculo
        {
            add    => ControladorVehiculoJugador.OnJugadorSalio += value;
            remove => ControladorVehiculoJugador.OnJugadorSalio -= value;
        }

        /// <summary>El jugador ha respawneado tras morir.</summary>
        public static event Action OnRespawn
        {
            add    => GameManagerAltsasua.OnRespawn += value;
            remove => GameManagerAltsasua.OnRespawn -= value;
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  BÚSQUEDA (WANTED)
    //  Nivel de alerta policial.
    // ════════════════════════════════════════════════════════════════════════
    public static class Busqueda
    {
        /// <summary>El nivel de búsqueda (1-5 estrellas) ha cambiado.</summary>
        public static event Action<int> OnNivelCambia
        {
            add    => GameManagerAltsasua.OnEstrellasCambia += value;
            remove => GameManagerAltsasua.OnEstrellasCambia -= value;
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  MISIONES
    //  Progresión de misiones principales.
    // ════════════════════════════════════════════════════════════════════════
    public static class Misiones
    {
        /// <summary>Una misión principal ha comenzado. Parámetro: nombre de la misión.</summary>
        public static event Action<string> OnIniciada
        {
            add    => SistemaMisiones.OnMisionIniciada += value;
            remove => SistemaMisiones.OnMisionIniciada -= value;
        }

        /// <summary>Un objetivo de misión ha sido completado. Parámetro: descripción del objetivo.</summary>
        public static event Action<string> OnObjetivoCompletado
        {
            add    => SistemaMisiones.OnObjetivoCompletado += value;
            remove => SistemaMisiones.OnObjetivoCompletado -= value;
        }

        /// <summary>Una misión principal ha sido completada. Parámetro: nombre de la misión.</summary>
        public static event Action<string> OnCompletada
        {
            add    => SistemaMisiones.OnMisionCompletada += value;
            remove => SistemaMisiones.OnMisionCompletada -= value;
        }

        /// <summary>Suscribe los tres callbacks de misión en una sola llamada.</summary>
        public static void SuscribirTodo(Action<string> iniciada, Action<string> objetivo, Action<string> completada)
        {
            if (iniciada   != null) SistemaMisiones.OnMisionIniciada      += iniciada;
            if (objetivo   != null) SistemaMisiones.OnObjetivoCompletado  += objetivo;
            if (completada != null) SistemaMisiones.OnMisionCompletada    += completada;
        }

        /// <summary>Desuscribe los tres callbacks de misión en una sola llamada.</summary>
        public static void DesuscribirTodo(Action<string> iniciada, Action<string> objetivo, Action<string> completada)
        {
            if (iniciada   != null) SistemaMisiones.OnMisionIniciada      -= iniciada;
            if (objetivo   != null) SistemaMisiones.OnObjetivoCompletado  -= objetivo;
            if (completada != null) SistemaMisiones.OnMisionCompletada    -= completada;
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  MANIFESTACIÓN
    //  Apoyo popular y paranoia ciudadana.
    // ════════════════════════════════════════════════════════════════════════
    public static class Manifestacion
    {
        /// <summary>El nivel de apoyo popular ha cambiado (0-100).</summary>
        public static event Action<float> OnApoyoCambia
        {
            add    => SistemaApoyoPopular.OnApoyoCambia += value;
            remove => SistemaApoyoPopular.OnApoyoCambia -= value;
        }

        /// <summary>El nivel de paranoia ciudadana ha cambiado.</summary>
        public static event Action<float> OnParanoiaCambia
        {
            add    => SistemaApoyoPopular.OnParanoiaCambia += value;
            remove => SistemaApoyoPopular.OnParanoiaCambia -= value;
        }

        /// <summary>La paranoia ha superado el umbral crítico — civiles huyen.</summary>
        public static event Action OnParanoiaCritica
        {
            add    => SistemaApoyoPopular.OnParanoiaCritica += value;
            remove => SistemaApoyoPopular.OnParanoiaCritica -= value;
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  ACCIÓN CALLEJERA
    //  Grafitis y pintadas.
    // ════════════════════════════════════════════════════════════════════════
    public static class AccionCallejera
    {
        /// <summary>El jugador ha realizado una pintada con spray.</summary>
        public static event Action OnPintadaRealizada
        {
            add    => SistemaGrafitis.OnPintadaRealizada += value;
            remove => SistemaGrafitis.OnPintadaRealizada -= value;
        }

        /// <summary>El jugador ha colocado una pegatina.</summary>
        public static event Action OnPegatinaPuesta
        {
            add    => SistemaGrafitis.OnPegatinaPuesta += value;
            remove => SistemaGrafitis.OnPegatinaPuesta -= value;
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  LOGROS
    // ════════════════════════════════════════════════════════════════════════
    public static class Logros
    {
        /// <summary>Un logro ha sido desbloqueado.</summary>
        public static event Action<SistemaLogros.Logro> OnDesbloqueado
        {
            add    => SistemaLogros.OnLogroDesbloqueado += value;
            remove => SistemaLogros.OnLogroDesbloqueado -= value;
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  VEHÍCULOS NPC
    // ════════════════════════════════════════════════════════════════════════
    public static class VehiculosNPC
    {
        /// <summary>Un vehículo NPC ha sido destruido (explosión, bomba…).</summary>
        public static event Action<VehiculoNPC> OnVehiculoDestruido
        {
            add    => VehiculoNPC.OnVehiculoDestruido += value;
            remove => VehiculoNPC.OnVehiculoDestruido -= value;
        }
    }
}
