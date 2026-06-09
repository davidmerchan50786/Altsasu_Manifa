// Assets/Scripts/MisionesAltsasua.cs
// ═══════════════════════════════════════════════════════════════════════════
//  MISIONES M03 – M12 — Alsasua Simulator
//
//  Cada misión implementa la clase base Mision definida en SistemaMisiones.cs
//  y encadena a la siguiente via SiguienteMision.
//
//  Cadena completa:
//    M01 RobarCoche → M02 HuirPolicia → M03 PintadaPlaza →
//    M04 Manifestacion → M05 CorteN1 → M06 RadioAskatasuna →
//    M07 ConcentracionNocturna → M08 InterceptorArdiendo →
//    M09 PresoakEtxera → M10 LaRepresalia →
//    M11 Amnistia → M12 ManifaFinal (clímax)
//
//  Todas las condiciones son evaluadas por SistemaMisiones.Update()
//  via System.Func<bool> — sin polling manual.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using UnityEngine;

// ── Acceso desacoplado a GameManager ──────────────────────────────────────────
// Las misiones no deben depender de GameManagerAltsasua directamente.
// Estos helpers estáticos en el archivo encapsulan el acceso a ServiceLocator
// para que los cambios de implementación no requieran tocar cada misión.
//
// NOTA: GameManagerAltsasua.Instance se mantiene como fallback de seguridad solo
// para el caso extremo de que ServiceLocator no esté registrado en el frame de boot.
internal static class MisionHelper
{
    static IWantedSystem   Wanted   => ServiceLocator.Get<IWantedSystem>()   ?? (IWantedSystem)GameManagerAltsasua.Instance;
    static IEconomyService Economy  => ServiceLocator.Get<IEconomyService>() ?? (IEconomyService)GameManagerAltsasua.Instance;

    public static int  NivelBusqueda               => Wanted?.NivelBusqueda ?? 0;
    public static void AumentarBusqueda(int n)     => Wanted?.AumentarBusqueda(n);
    public static void GanarDinero(int n)          => Economy?.GanarDinero(n);
    public static bool SinWanted()                 => NivelBusqueda == 0;
    public static bool WantedMinimo(int min)       => NivelBusqueda >= min;
}

// ═════════════════════════════════════════════════════════════════════════════
//  COORDENADAS DE ALSASUA (sistema terreno, origen = esquina SW del DEM)
//  Herriko Plaza ≈ (1918, y, 8570) — referencia central
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Wrapper de compatibilidad — todos los miembros delegan a GeoDataAlsasua,
/// que es la única fuente de verdad para coordenadas del pueblo.
/// </summary>
public static class PuntosAlsasua
{
    public static Vector3 HerrikoPlaza    => GeoDataAlsasua.HerrikoPlaza;
    public static Vector3 EstacionTren   => GeoDataAlsasua.EstacionTren;
    public static Vector3 CuartelGC      => GeoDataAlsasua.CuartelGC;
    public static Vector3 CarreteraN1Norte => GeoDataAlsasua.CarreteraN1Norte;
    public static Vector3 CarreteraN1Sur  => GeoDataAlsasua.CarreteraN1Sur;
    public static Vector3 MonteAralar    => GeoDataAlsasua.MonteAralar;
    public static Vector3 BarrioNorte    => GeoDataAlsasua.BarrioNorte;
    public static Vector3 PoligonoIsasia => GeoDataAlsasua.PoligonoIsasia;

    public static float   Dist2D(Vector3 a, Vector3 b) => GeoDataAlsasua.Dist2D(a, b);
    public static Vector3 JugadorPos()                 => GeoDataAlsasua.JugadorPos();
}

// ═════════════════════════════════════════════════════════════════════════════
//  M03 — LA PINTADA DE LA PLAZA
//  Pinta 5 superficies en Herriko Plaza y escapa antes de 90 segundos.
// ═════════════════════════════════════════════════════════════════════════════

public class Mision_PintadaPlaza : Mision
{
    public override string Nombre => "La Pintada de Herriko Plaza";

    private int   _pintadas      = 0;
    private float _timerEscape   = 0f;
    private bool  _escapando     = false;
    private const int   META_PINTADAS = 5;
    private const float RADIO_PLAZA   = 200f;
    private const float TIEMPO_MAX    = 90f;

    public override System.Action AlIniciar => () =>
    {
        _pintadas  = 0;
        _escapando = false;
        SistemaGrafitis.OnPintadaRealizada += ContarPintada;
        AlsasuaLogger.Info("M03", $"Pinta {META_PINTADAS} superficies en la plaza — pulsa G");
    };

    private void ContarPintada()
    {
        if (_escapando) return;
        _pintadas++;
        AlsasuaLogger.Info("M03", $"Pintada {_pintadas}/{META_PINTADAS}");
    }

    public override List<Objetivo> Objetivos => new List<Objetivo>
    {
        new Objetivo
        {
            Descripcion = $"Pinta {META_PINTADAS} superficies en Herriko Plaza (pulsa G)",
            Condicion   = () => _pintadas >= META_PINTADAS,
            AlCompletar = () =>
            {
                _escapando = true;
                SistemaGrafitis.OnPintadaRealizada -= ContarPintada;
                MisionHelper.AumentarBusqueda(1);
                SistemaApoyoPopular.Instance?.SumarApoyo(80f, "Pintada colectiva en la plaza");
                AlsasuaLogger.Info("M03", "¡Pintadas listas! Escapa de la zona en 90 segundos");
            }
        },
        new Objetivo
        {
            Descripcion = "Escapa de la plaza — aléjate 200m antes de que llegue la patrulla",
            Condicion   = () =>
            {
                _timerEscape += Time.deltaTime;
                bool fueraPlaza = PuntosAlsasua.Dist2D(
                    PuntosAlsasua.JugadorPos(), PuntosAlsasua.HerrikoPlaza) > RADIO_PLAZA;
                if (_timerEscape >= TIEMPO_MAX)
                {
                    // Tiempo agotado — penalización
                    MisionHelper.AumentarBusqueda(2);
                    return true; // avanzar misión de todas formas
                }
                return fueraPlaza;
            },
            AlCompletar = () =>
            {
                bool enTiempo = _timerEscape < TIEMPO_MAX;
                int dinero = enTiempo ? 400 : 100;
                MisionHelper.GanarDinero(dinero);
                // Honor: solo se gana si escapaste limpio; huir tarde manda el mensaje equivocado
                if (enTiempo)
                    SistemaApoyoPopular.Instance?.SumarHonor(15f, "Pintada y escape sin violencia");
                else
                    SistemaApoyoPopular.Instance?.RestarHonor(5f, "Capturado en la pintada");
                string msg = enTiempo ? "Escape limpio — +400€" : "Llegaron tarde pero te pillaron — +100€";
                AlsasuaLogger.Info("M03", msg);
            }
        }
    };

    public override Mision SiguienteMision => new Mision_Manifestacion();
}

// ═════════════════════════════════════════════════════════════════════════════
//  M04 — LA MANIFESTACIÓN
//  Inicia la manifestación en la plaza y mantenla 2 minutos sin disolución.
// ═════════════════════════════════════════════════════════════════════════════

public class Mision_Manifestacion : Mision
{
    public override string Nombre => "Gora Euskal Herria — La Manifestación";

    private float _timerManifa    = 0f;
    private bool  _manifaIniciada = false;
    private const float DURACION_META = 120f; // 2 minutos

    public override System.Action AlIniciar => () =>
    {
        _timerManifa    = 0f;
        _manifaIniciada = false;
        // Bloquear el toggle manual mientras M04 controla la manifestación
        var manifa = AltsasuCore.I?.manifestacionSystem;
        if (manifa != null) manifa.ControladaPorMision = true;
        AlsasuaLogger.Info("M04", "Llega a Herriko Plaza sin wanted y pulsa M para manifestarte");
    };

    public override List<Objetivo> Objetivos => new List<Objetivo>
    {
        new Objetivo
        {
            Descripcion = "Llega a Herriko Plaza sin nivel de búsqueda activo",
            Condicion   = () =>
            {
                bool sinWanted = MisionHelper.SinWanted();
                bool enPlaza   = PuntosAlsasua.Dist2D(
                    PuntosAlsasua.JugadorPos(), PuntosAlsasua.HerrikoPlaza) < 80f;
                return sinWanted && enPlaza;
            },
            AlCompletar = () =>
                AlsasuaLogger.Info("M04", "¡Estás en la plaza! Pulsa M para iniciar la manifestación")
        },
        new Objetivo
        {
            Descripcion = "Inicia la manifestación (pulsa M)",
            Condicion   = () =>
            {
                var manifa = AltsasuCore.I?.manifestacionSystem;
                if (manifa != null && manifa.EnCurso && !_manifaIniciada)
                {
                    _manifaIniciada = true;
                    AlsasuaLogger.Info("M04", "¡Manifestación en marcha! Protégela 2 minutos");
                }
                return _manifaIniciada;
            },
            AlCompletar = () =>
                SistemaApoyoPopular.Instance?.SumarApoyo(60f, "Manifestación iniciada")
        },
        new Objetivo
        {
            Descripcion = $"Mantén la manifestación {DURACION_META}s — no dejes que la disuelvan",
            Condicion   = () =>
            {
                var manifa = AltsasuCore.I?.manifestacionSystem;
                if (manifa != null && manifa.EnCurso)
                    _timerManifa += Time.deltaTime;
                else if (_manifaIniciada)
                    _timerManifa -= Time.deltaTime * 2f; // penalización si se disuelve
                _timerManifa = Mathf.Max(0f, _timerManifa);
                return _timerManifa >= DURACION_META;
            },
            AlCompletar = () =>
            {
                MisionHelper.GanarDinero(800);
                SistemaApoyoPopular.Instance?.SumarApoyo(200f, "Manifestación sostenida 2 minutos");
                SistemaApoyoPopular.Instance?.SumarHonor(25f, "Organización pacífica exitosa");
                // La manifestación reduce el nivel de búsqueda si el apoyo es alto
                var apoyo = SistemaApoyoPopular.Instance;
                if (apoyo != null && apoyo.apoyo > 60f)
                    MisionHelper.AumentarBusqueda(-1);
                // Liberar el control de la manifestación — el jugador puede gestionarla libremente
                var manifa = AltsasuCore.I?.manifestacionSystem;
                if (manifa != null) manifa.ControladaPorMision = false;
            }
        }
    };

    public override Mision SiguienteMision => new Mision_CorteN1();
}

// ═════════════════════════════════════════════════════════════════════════════
//  M05 — EL CORTE DE LA N-1
//  Llega a la carretera N-1 y bloquéala durante 90 segundos.
// ═════════════════════════════════════════════════════════════════════════════

public class Mision_CorteN1 : Mision
{
    public override string Nombre => "Corte de la N-1 — Nafarroa Errepidea";

    private float _timerCorte = 0f;
    private bool  _enN1       = false;
    private const float RADIO_N1    = 60f;
    private const float DURACION    = 90f;

    public override System.Action AlIniciar => () =>
    {
        _timerCorte = 0f;
        _enN1 = false;
        AlsasuaLogger.Info("M05", "Ve a la N-1 al sur del pueblo y bloquea el tráfico");
    };

    public override List<Objetivo> Objetivos => new List<Objetivo>
    {
        new Objetivo
        {
            Descripcion = "Llega a la carretera N-1 (sur del pueblo)",
            Condicion   = () =>
            {
                bool cerca = PuntosAlsasua.Dist2D(
                    PuntosAlsasua.JugadorPos(), PuntosAlsasua.CarreteraN1Norte) < RADIO_N1
                    || PuntosAlsasua.Dist2D(
                    PuntosAlsasua.JugadorPos(), PuntosAlsasua.CarreteraN1Sur) < RADIO_N1;
                if (cerca) _enN1 = true;
                return _enN1;
            },
            AlCompletar = () =>
            {
                MisionHelper.AumentarBusqueda(1);
                AlsasuaLogger.Info("M05", "¡Estás en la N-1! Mantén el corte 90 segundos");
            }
        },
        new Objetivo
        {
            Descripcion = $"Mantén el corte de la N-1 durante {DURACION}s",
            Condicion   = () =>
            {
                bool enZona = PuntosAlsasua.Dist2D(
                    PuntosAlsasua.JugadorPos(), PuntosAlsasua.CarreteraN1Norte) < RADIO_N1 * 1.5f
                    || PuntosAlsasua.Dist2D(
                    PuntosAlsasua.JugadorPos(), PuntosAlsasua.CarreteraN1Sur) < RADIO_N1 * 1.5f;
                if (enZona) _timerCorte += Time.deltaTime;
                return _timerCorte >= DURACION;
            },
            AlCompletar = () =>
            {
                MisionHelper.GanarDinero(600);
                SistemaApoyoPopular.Instance?.SumarApoyo(120f, "Corte de la N-1 durante 90 segundos");
                AlsasuaLogger.Info("M05", "¡Corte exitoso! El tráfico lleva 90 segundos parado");
            }
        },
        new Objetivo
        {
            Descripcion = "Escapa — aléjate 300m antes de que lleguen los refuerzos",
            Condicion   = () =>
            {
                bool fueraN1A = PuntosAlsasua.Dist2D(
                    PuntosAlsasua.JugadorPos(), PuntosAlsasua.CarreteraN1Norte) > 300f;
                bool fueraN1B = PuntosAlsasua.Dist2D(
                    PuntosAlsasua.JugadorPos(), PuntosAlsasua.CarreteraN1Sur) > 300f;
                return fueraN1A && fueraN1B;
            },
            AlCompletar = () =>
            {
                MisionHelper.GanarDinero(300);
                MisionHelper.AumentarBusqueda(-1);
                SistemaApoyoPopular.Instance?.SumarHonor(20f, "Acción directa no violenta completada");
            }
        }
    };

    public override Mision SiguienteMision => new Mision_RadioAskatasuna();
}

// ═════════════════════════════════════════════════════════════════════════════
//  M06 — RADIO ASKATASUNA
//  Llega a pie a la antena en el monte Aralar y emite durante 60 segundos.
// ═════════════════════════════════════════════════════════════════════════════

public class Mision_RadioAskatasuna : Mision
{
    public override string Nombre => "Radio Askatasuna — La Voz del Monte";

    private float _timerEmision  = 0f;
    private bool  _enAntena      = false;
    private const float RADIO_ANTENA   = 50f;
    private const float DURACION_EMISION = 60f;

    public override System.Action AlIniciar => () =>
    {
        _timerEmision = 0f;
        _enAntena = false;
        AlsasuaLogger.Info("M06", "Ve ANDANDO hasta el monte Aralar — no puedes usar vehículo");
    };

    public override List<Objetivo> Objetivos => new List<Objetivo>
    {
        new Objetivo
        {
            Descripcion = "Llega a la antena de radio en el monte Aralar (sin vehículo)",
            Condicion   = () =>
            {
                // Si el jugador va en coche, no cuenta
                var coche = Object.FindFirstObjectByType<ControladorVehiculoJugador>();
                if (coche != null && coche.JugadorDentro) return false;
                bool cercaAntena = PuntosAlsasua.Dist2D(
                    PuntosAlsasua.JugadorPos(), PuntosAlsasua.MonteAralar) < RADIO_ANTENA;
                if (cercaAntena) _enAntena = true;
                return _enAntena;
            },
            AlCompletar = () =>
            {
                SistemaApoyoPopular.Instance?.SumarApoyo(50f, "Llegada al monte Aralar");
                AlsasuaLogger.Info("M06", "¡Antena encontrada! Emite durante 60 segundos");
            }
        },
        new Objetivo
        {
            Descripcion = $"Emite desde la antena durante {DURACION_EMISION}s (permanece en la zona)",
            Condicion   = () =>
            {
                bool enZona = PuntosAlsasua.Dist2D(
                    PuntosAlsasua.JugadorPos(), PuntosAlsasua.MonteAralar) < RADIO_ANTENA * 1.5f;
                if (enZona) _timerEmision += Time.deltaTime;
                else _timerEmision -= Time.deltaTime; // si te alejas, la señal se pierde
                _timerEmision = Mathf.Max(0f, _timerEmision);
                return _timerEmision >= DURACION_EMISION;
            },
            AlCompletar = () =>
            {
                MisionHelper.GanarDinero(1200);
                SistemaApoyoPopular.Instance?.SumarApoyo(300f, "Emisión Radio Askatasuna completada");
                // La emisión reduce la paranoia — la gente confía más
                SistemaApoyoPopular.Instance?.RestarParanoia(15f);
                AlsasuaLogger.Info("M06", "La emisión llega a toda la comarca — apoyo popular máximo");
            }
        }
    };

    public override Mision SiguienteMision => new Mision_ConcentracionNocturna();
}

// ═════════════════════════════════════════════════════════════════════════════
//  M07 — LA CONCENTRACIÓN NOCTURNA
//  Espera a que sea de noche (22:00h) y reúnete en la plaza.
// ═════════════════════════════════════════════════════════════════════════════

public class Mision_ConcentracionNocturna : Mision
{
    public override string Nombre => "Kontzentrazioa — La Concentración Nocturna";

    private float _timerConc    = 0f;
    private bool  _enPlaza      = false;
    private const float HORA_NOCHE   = 21.5f; // 21:30h
    private const float DURACION     = 180f;  // 3 minutos

    public override System.Action AlIniciar => () =>
    {
        _timerConc = 0f;
        _enPlaza = false;
        AlsasuaLogger.Info("M07", "Espera a las 21:30h y concentraos en Herriko Plaza");
    };

    private float HoraActual()
    {
        var atm = AltsasuCore.I?.atmosferaSystem;
        return atm != null ? atm.HoraDelDia : 12f;
    }

    public override List<Objetivo> Objetivos => new List<Objetivo>
    {
        new Objetivo
        {
            Descripcion = "Espera a que sea de noche — hora actual: (ver HUD)",
            Condicion   = () =>
            {
                float hora = HoraActual();
                return hora >= HORA_NOCHE || hora < 4f; // noche o madrugada
            },
            AlCompletar = () =>
                AlsasuaLogger.Info("M07", "¡Ha caído la noche! Ve a Herriko Plaza")
        },
        new Objetivo
        {
            Descripcion = "Llega a Herriko Plaza de noche",
            Condicion   = () =>
            {
                bool enPlaza = PuntosAlsasua.Dist2D(
                    PuntosAlsasua.JugadorPos(), PuntosAlsasua.HerrikoPlaza) < 100f;
                if (enPlaza) _enPlaza = true;
                return _enPlaza;
            },
            AlCompletar = () =>
            {
                SistemaApoyoPopular.Instance?.SumarApoyo(40f, "Concentración nocturna convocada");
                AlsasuaLogger.Info("M07", "Mantén la concentración 3 minutos sin que os disuelvan");
            }
        },
        new Objetivo
        {
            Descripcion = $"Mantén la concentración nocturna {DURACION}s",
            Condicion   = () =>
            {
                bool enZona = PuntosAlsasua.Dist2D(
                    PuntosAlsasua.JugadorPos(), PuntosAlsasua.HerrikoPlaza) < 150f;
                bool esNoche = HoraActual() >= HORA_NOCHE || HoraActual() < 4f;
                if (enZona && esNoche) _timerConc += Time.deltaTime;
                return _timerConc >= DURACION;
            },
            AlCompletar = () =>
            {
                MisionHelper.GanarDinero(700);
                SistemaApoyoPopular.Instance?.SumarApoyo(150f, "Concentración nocturna completada");
            }
        }
    };

    public override Mision SiguienteMision => new Mision_InterceptorArdiendo();
}

// ═════════════════════════════════════════════════════════════════════════════
//  M08 — EL INTERCEPTOR ARDIENDO
//  Destruye 2 coches de policía usando bombas.
// ═════════════════════════════════════════════════════════════════════════════

public class Mision_InterceptorArdiendo : Mision
{
    public override string Nombre => "Bi Kotxe — El Interceptor Ardiendo";

    private int   _cochesDestruidos = 0;
    private float _timerFuga        = 0f;
#pragma warning disable CS0414
    private bool  _escapando        = false;
#pragma warning restore CS0414
    private const int   META_COCHES  = 2;
    private const float TIEMPO_FUGA  = 120f;

    public override System.Action AlIniciar => () =>
    {
        _cochesDestruidos = 0;
        _escapando = false;
        _timerFuga = 0f;
        VehiculoNPC.OnVehiculoDestruido += ContarCoche;
        AlsasuaLogger.Info("M08", "Destruye 2 coches de policía con bombas (B=bomba, G=detonar)");
    };

    private void ContarCoche(VehiculoNPC v)
    {
        if (v == null) return;
        // Solo contar si es un vehículo de policía
        if (v.gameObject.name.ToLower().Contains("policia") ||
            v.gameObject.name.ToLower().Contains("interceptor") ||
            v.gameObject.name.ToLower().Contains("foral"))
        {
            _cochesDestruidos++;
            AlsasuaLogger.Info("M08", $"Coche destruido {_cochesDestruidos}/{META_COCHES}");
        }
    }

    public override List<Objetivo> Objetivos => new List<Objetivo>
    {
        new Objetivo
        {
            Descripcion = $"Destruye {META_COCHES} coches de la Policía Foral (usa bombas — tecla B)",
            Condicion   = () => _cochesDestruidos >= META_COCHES,
            AlCompletar = () =>
            {
                _escapando = true;
                VehiculoNPC.OnVehiculoDestruido -= ContarCoche;
                MisionHelper.AumentarBusqueda(3);
                SistemaApoyoPopular.Instance?.SumarApoyo(100f, "Coches policiales destruidos");
                AlsasuaLogger.Info("M08", "¡Alerta máxima! Escapa en 2 minutos");
            }
        },
        new Objetivo
        {
            Descripcion = "Escapa de la zona de búsqueda en 2 minutos",
            Condicion   = () =>
            {
                _timerFuga += Time.deltaTime;
                // Escapado si wanted baja a 0 O si pasan los 2 minutos (con penalización)
                bool sinWanted = MisionHelper.SinWanted();
                if (_timerFuga >= TIEMPO_FUGA)
                {
                    // Penalización por no escapar
                    MisionHelper.AumentarBusqueda(1);
                    return true;
                }
                return sinWanted;
            },
            AlCompletar = () =>
            {
                bool enTiempo = _timerFuga < TIEMPO_FUGA;
                MisionHelper.GanarDinero(enTiempo ? 1500 : 500);
            }
        }
    };

    public override Mision SiguienteMision => new Mision_PresoakEtxera();
}

// ═════════════════════════════════════════════════════════════════════════════
//  M09 — PRESOAK ETXERA
//  Campaña política: pinta "PRESOAK ETXERA" en 6 zonas del pueblo.
// ═════════════════════════════════════════════════════════════════════════════

public class Mision_PresoakEtxera : Mision
{
    public override string Nombre => "Presoak Etxera — Presos a Casa";

    private int   _pintadas = 0;
    private bool  _activo   = false;
    private const int META = 6;

    public override System.Action AlIniciar => () =>
    {
        _pintadas = 0;
        _activo = true;
        SistemaGrafitis.OnPintadaRealizada += Contar;
        SistemaGrafitis.OnPegatinaPuesta   += ContarPegatina;
        AlsasuaLogger.Info("M09",
            $"Pinta '{META}' mensajes de 'Presoak Etxera' por todo el pueblo — pulsa G");
    };

    private void Contar()      { if (_activo && _pintadas < META) { _pintadas++; } }
    private void ContarPegatina() => Contar();

    public override List<Objetivo> Objetivos => new List<Objetivo>
    {
        new Objetivo
        {
            Descripcion = $"Pinta {META} mensajes en distintas zonas del pueblo (G = modo spray)",
            Condicion   = () => _pintadas >= META,
            AlCompletar = () =>
            {
                _activo = false;
                SistemaGrafitis.OnPintadaRealizada -= Contar;
                SistemaGrafitis.OnPegatinaPuesta   -= ContarPegatina;
                MisionHelper.GanarDinero(900);
                SistemaApoyoPopular.Instance?.SumarApoyo(250f, "Campaña Presoak Etxera completada");
                SistemaApoyoPopular.Instance?.RestarParanoia(10f);
                AlsasuaLogger.Info("M09", "¡Campaña completa! El pueblo entero lleva el mensaje");
            }
        }
    };

    public override Mision SiguienteMision => new Mision_LaRepresalia();
}

// ═════════════════════════════════════════════════════════════════════════════
//  M10 — LA REPRESALIA
//  Llega a 3 estrellas de búsqueda y aguanta vivo durante 90 segundos.
// ═════════════════════════════════════════════════════════════════════════════

public class Mision_LaRepresalia : Mision
{
    public override string Nombre => "La Represalia — Errepresalia";

#pragma warning disable CS0414
    private float _timerBajo3    = 0f;
#pragma warning restore CS0414
    private float _timerAguante  = 0f;
    private bool  _en3Estrellas  = false;
    private const float DURACION_AGUANTE = 90f;

    public override System.Action AlIniciar => () =>
    {
        _timerAguante = 0f;
        _en3Estrellas = false;
        AlsasuaLogger.Info("M10", "Llega a 3 estrellas de búsqueda y sobrevive 90 segundos");
    };

    public override List<Objetivo> Objetivos => new List<Objetivo>
    {
        new Objetivo
        {
            Descripcion = "Alcanza nivel de búsqueda 3★ (haz acciones radicales)",
            Condicion   = () =>
            {
                if (MisionHelper.WantedMinimo(3)) _en3Estrellas = true;
                return _en3Estrellas;
            },
            AlCompletar = () =>
                AlsasuaLogger.Info("M10", "¡3 Estrellas! Ahora aguanta 90 segundos vivo")
        },
        new Objetivo
        {
            Descripcion = $"Sobrevive bajo 3★ de búsqueda durante {DURACION_AGUANTE}s",
            Condicion   = () =>
            {
                var jCtrl = AltsasuCore.Jugador?.GetComponent<ControladorJugador>();
                if (jCtrl == null) return false;
                bool vivo = jCtrl.Vida > 0;
                bool con3 = MisionHelper.WantedMinimo(3);
                if (vivo && con3)
                    _timerAguante += Time.deltaTime;
                return _timerAguante >= DURACION_AGUANTE;
            },
            AlCompletar = () =>
            {
                MisionHelper.GanarDinero(2000);
                SistemaApoyoPopular.Instance?.SumarApoyo(400f, "Sobreviviste a la represalia");
                SistemaApoyoPopular.Instance?.SumarHonor(30f, "Resistencia bajo represión");
                AlsasuaLogger.Info("M10", "Supervivencia demostrada — la resistencia no cede");
            }
        }
    };

    public override Mision SiguienteMision => new Mision_Amnistia();
}

// ═════════════════════════════════════════════════════════════════════════════
//  M11 — AMNISTIA
//  Consigue que el apoyo popular supere 80% estando sin wanted.
// ═════════════════════════════════════════════════════════════════════════════

public class Mision_Amnistia : Mision
{
    public override string Nombre => "Amnistia — La Victoria Política";

    private float _timerAltapoyo = 0f;
    private const float META_APOYO   = 80f;
    private const float DURACION_SOT = 60f; // 60s con apoyo > 80 y sin wanted

    public override System.Action AlIniciar => () =>
    {
        _timerAltapoyo = 0f;
        AlsasuaLogger.Info("M11",
            "Consigue apoyo popular > 80% y mantente sin wanted durante 60 segundos");
    };

    public override List<Objetivo> Objetivos => new List<Objetivo>
    {
        new Objetivo
        {
            Descripcion = "Pinta graffitis, organiza manifestaciones y reduce el wanted a 0",
            Condicion   = () =>
            {
                var apoyo = SistemaApoyoPopular.Instance;
                if (apoyo == null) return false;
                return MisionHelper.SinWanted() && apoyo.apoyo >= META_APOYO;
            },
            AlCompletar = () =>
                AlsasuaLogger.Info("M11",
                    "¡Apoyo máximo y sin búsqueda! Mantenlo 60 segundos para la victoria")
        },
        new Objetivo
        {
            Descripcion = $"Mantén apoyo > {META_APOYO}% y wanted = 0 durante {DURACION_SOT}s",
            Condicion   = () =>
            {
                var apoyo = SistemaApoyoPopular.Instance;
                if (apoyo == null) return false;
                bool condicion = MisionHelper.SinWanted() && apoyo.apoyo >= META_APOYO;
                if (condicion) _timerAltapoyo += Time.deltaTime;
                else           _timerAltapoyo  = Mathf.Max(0f, _timerAltapoyo - Time.deltaTime);
                return _timerAltapoyo >= DURACION_SOT;
            },
            AlCompletar = () =>
            {
                MisionHelper.GanarDinero(3000);
                SistemaApoyoPopular.Instance?.SumarApoyo(500f, "Victoria política — Amnistia");
                SistemaApoyoPopular.Instance?.SumarHonor(40f, "Victoria política sin violencia");
                AlsasuaLogger.Info("M11", "¡AMNISTIA! El pueblo ha ganado. Una última batalla...");
            }
        }
    };

    public override Mision SiguienteMision => new Mision_ManifaFinal();
}

// ═════════════════════════════════════════════════════════════════════════════
//  M12 — LA MANIFA FINAL (CLÍMAX)
//  Inicia la manifestación masiva con apoyo > 70%, llega al Ayuntamiento
//  y sostén la plaza durante 3 minutos. FIN DEL JUEGO.
// ═════════════════════════════════════════════════════════════════════════════

public class Mision_ManifaFinal : Mision
{
    public override string Nombre => "ASKATASUNA — La Manifa Final";

    private float _timerFinal    = 0f;
    private bool  _manifaActiva  = false;
    private bool  _enAyuntamiento = false;
    // El Ayuntamiento está en el centro de Herriko Plaza
    private static readonly Vector3 AYUNTAMIENTO = new(GeoDataAlsasua.OX, 0f, GeoDataAlsasua.OZ + 10f);
    private const float RADIO_AYUNT  = 40f;
    private const float DURACION     = 180f; // 3 minutos

    public override System.Action AlIniciar => () =>
    {
        _timerFinal = 0f;
        _manifaActiva = false;
        _enAyuntamiento = false;
        AlsasuaLogger.Info("M12",
            "LA MISIÓN FINAL — Manifestación masiva en Herriko Plaza. Llega al Ayuntamiento.");
    };

    public override List<Objetivo> Objetivos => new List<Objetivo>
    {
        new Objetivo
        {
            Descripcion = "Inicia la manifestación masiva con apoyo > 70% (pulsa M en la plaza)",
            Condicion   = () =>
            {
                var apoyo  = SistemaApoyoPopular.Instance;
                var manifa = AltsasuCore.I?.manifestacionSystem;
                bool apoyoOK  = apoyo != null && apoyo.apoyo >= 70f;
                bool enPlaza  = PuntosAlsasua.Dist2D(
                    PuntosAlsasua.JugadorPos(), PuntosAlsasua.HerrikoPlaza) < 100f;
                bool manifaOK = manifa != null && manifa.EnCurso;
                if (apoyoOK && enPlaza && manifaOK && !_manifaActiva)
                {
                    _manifaActiva = true;
                    AlsasuaLogger.Info("M12", "¡MANIFESTACIÓN MASIVA! Miles de personas en las calles");
                }
                return _manifaActiva;
            },
            AlCompletar = () =>
            {
                SistemaApoyoPopular.Instance?.SumarApoyo(200f, "Manifestación masiva convocada");
                MisionHelper.AumentarBusqueda(-2); // la fuerza del pueblo frena la represión
            }
        },
        new Objetivo
        {
            Descripcion = "Lidera la marcha hasta el Ayuntamiento de Alsasua",
            Condicion   = () =>
            {
                bool cerca = PuntosAlsasua.Dist2D(
                    PuntosAlsasua.JugadorPos(), AYUNTAMIENTO) < RADIO_AYUNT;
                if (cerca) _enAyuntamiento = true;
                return _enAyuntamiento;
            },
            AlCompletar = () =>
            {
                AlsasuaLogger.Info("M12",
                    "¡AYUNTAMIENTO! Sostén la plaza 3 minutos para la victoria definitiva");
                SistemaApoyoPopular.Instance?.SumarApoyo(100f, "Llegada al Ayuntamiento");
            }
        },
        new Objetivo
        {
 