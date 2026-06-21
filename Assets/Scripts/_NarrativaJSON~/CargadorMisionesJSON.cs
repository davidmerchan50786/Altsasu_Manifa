// Assets/Scripts/_NarrativaJSON~/CargadorMisionesJSON.cs  (STAGING/DRAFT — carpeta ~ no se compila)
// ─────────────────────────────────────────────────────────────────────────────
//  Carga misiones_altsasu.json y las convierte en la cadena de `Mision` que
//  consume SistemaMisiones, construyendo las Condicion/AlCompletar con una
//  factoría por `tipo` (usa tus sistemas reales: SistemaGrafitis, apoyo, wanted).
//
//  Al ACTIVAR (ver LEEME_misiones_json.md):
//    - mover este archivo a Assets/Scripts/Runtime/
//    - poner misiones_altsasu.json en Assets/StreamingAssets/
//    - añadir el almacén FlagsNarrativos (3 líneas, ver LEEME)
//    - VERIFICAR la firma de la clase base `Mision` (Nombre/AlIniciar/Objetivos/
//      SiguienteMision) por si declara más miembros abstractos.
//
//  Es un esqueleto: los `tipo` no implementados devuelven una condición trivial
//  (log + true) para que la cadena avance mientras implementas cada mecánica.
// ─────────────────────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

// ── Clases de datos (deserializadas del JSON) ────────────────────────────────
[Serializable] public class ArchivoMisiones
{
    public List<MisionDatos> misiones;
    public List<MisionDatos> laterales;
    public Dictionary<string, FinalDatos> finales;
}
[Serializable] public class MisionDatos
{
    public string id, clase, nombre, localizacion, siguiente;
    public int acto, apoyoDelta, dineroDelta;
    public List<string> sistemas, flagsSet, flagsReq, desbloquea;
    public List<ObjetivoDatos> objetivos;
    public List<RamaDatos> ramas;
}
[Serializable] public class ObjetivoDatos
{
    public string id, descripcion, tipo;
    public Dictionary<string, object> parametros;
}
[Serializable] public class RamaDatos { public CondicionDatos condicion; public string resultado; }
[Serializable] public class CondicionDatos { public int apoyoMin; public string flag; }
[Serializable] public class FinalDatos { public string nombre, resumen, tono; }

// ── Mision data-driven ───────────────────────────────────────────────────────
public class MisionDataDriven : Mision
{
    readonly MisionDatos _d;
    List<Objetivo> _objetivos;
    public Mision Enlace;   // 'siguiente', enlazada por el cargador

    public MisionDataDriven(MisionDatos d) { _d = d; }

    public override string Nombre => _d.nombre;
    public override Action AlIniciar => () =>
        AlsasuaLogger.Info(_d.id, $"Inicio: {_d.nombre}");
    public override List<Objetivo> Objetivos => _objetivos ??= Construir();
    public override Mision SiguienteMision => Enlace;   // M12: el cargador resuelve ramas

    List<Objetivo> Construir()
    {
        var l = new List<Objetivo>();
        for (int i = 0; i < _d.objetivos.Count; i++)
        {
            bool ultimo = i == _d.objetivos.Count - 1;
            l.Add(FabricaObjetivos.Fabricar(_d.objetivos[i], _d, ultimo));
        }
        return l;
    }
}

// ── Factoría de objetivos por `tipo` ─────────────────────────────────────────
public static class FabricaObjetivos
{
    public static Objetivo Fabricar(ObjetivoDatos o, MisionDatos m, bool ultimo)
    {
        Func<bool> cond = CondicionPara(o, m);
        Action completar = () =>
        {
            // En el último objetivo aplica los deltas de la misión.
            if (ultimo) AplicarRecompensa(m);
            // (las decisiones aplican sus propios deltas dentro de su condición)
        };
        return new Objetivo { Descripcion = o.descripcion, Condicion = cond, AlCompletar = completar };
    }

    static Func<bool> CondicionPara(ObjetivoDatos o, MisionDatos m)
    {
        switch (o.tipo)
        {
            case "pintar":
            {
                int meta = ParamInt(o, "n", 1), contador = 0;
                Action h = null; h = () => contador++;
                SistemaGrafitis.OnPintadaRealizada += h;
                return () => { if (contador >= meta) { SistemaGrafitis.OnPintadaRealizada -= h; return true; } return false; };
            }
            case "llegar":
            case "llegar_sigilo":
            case "escapar_zona":
            {
                Vector3 p = PuntoDe(m.localizacion);
                float r = ParamFloat(o, o.tipo == "escapar_zona" ? "radio" : "radio", o.tipo == "escapar_zona" ? 150f : 12f);
                bool fuera = o.tipo == "escapar_zona";
                bool sigilo = o.tipo == "llegar_sigilo";
                return () =>
                {
                    float d = PuntosAlsasua.Dist2D(PuntosAlsasua.JugadorPos(), p);
                    bool ok = fuera ? d > r : d < r;
                    return ok && (!sigilo || MisionHelper.SinWanted());
                };
            }
            case "escapar_wanted":
                return () => MisionHelper.NivelBusqueda == 0;

            // TODO: minijuego, interactuar, recolectar, manifestacion/defender,
            // puzzle_*, decision*, persuadir, cruzar_multitud, etc. → implementar en editor.
            default:
                return () => { AlsasuaLogger.Info(m.id, $"[stub] tipo '{o.tipo}' no implementado → avanza"); return true; };
        }
    }

    static void AplicarRecompensa(MisionDatos m)
    {
        if (m.apoyoDelta > 0) SistemaApoyoPopular.Instance?.SumarApoyo(m.apoyoDelta, m.nombre);
        else if (m.apoyoDelta < 0) SistemaApoyoPopular.Instance?.RestarApoyo(-m.apoyoDelta, m.nombre);
        if (m.dineroDelta != 0) MisionHelper.GanarDinero(m.dineroDelta);
        if (m.flagsSet != null) foreach (var f in m.flagsSet) FlagsNarrativos.Set(f);
    }

    static Vector3 PuntoDe(string loc) => loc switch
    {
        "deposito" or "promotora" => PuntosAlsasua.PoligonoIsasia,
        "casa_quemada" => PuntosAlsasua.MonteAralar,
        "estacion" => PuntosAlsasua.EstacionTren,
        "rio" => PuntosAlsasua.CarreteraN1Sur,
        _ => PuntosAlsasua.HerrikoPlaza,
    };

    static int   ParamInt(ObjetivoDatos o, string k, int def)
        => o.parametros != null && o.parametros.TryGetValue(k, out var v) ? Convert.ToInt32(v) : def;
    static float ParamFloat(ObjetivoDatos o, string k, float def)
        => o.parametros != null && o.parametros.TryGetValue(k, out var v) ? Convert.ToSingle(v) : def;
}

// ── Cargador ─────────────────────────────────────────────────────────────────
public static class CargadorMisionesJSON
{
    public static Dictionary<string, Mision> CargarTodas(string ruta = null)
    {
        ruta ??= Path.Combine(Application.streamingAssetsPath, "misiones_altsasu.json");
        var arch = JsonConvert.DeserializeObject<ArchivoMisiones>(File.ReadAllText(ruta));

        var porId = new Dictionary<string, MisionDataDriven>();
        foreach (var d in arch.misiones) porId[d.id] = new MisionDataDriven(d);

        // Enlazar 'siguiente' lineal. (M12 sin 'siguiente': ramas → resolver en runtime.)
        foreach (var d in arch.misiones)
            if (!string.IsNullOrEmpty(d.siguiente) && porId.TryGetValue(d.siguiente, out var sig))
                porId[d.id].Enlace = sig;

        var salida = new Dictionary<string, Mision>();
        foreach (var kv in porId) salida[kv.Key] = kv.Value;
        return salida;
    }

    /// <summary>Resuelve el final de M12 según apoyo + flags (ver `ramas` en el JSON).</summary>
    public static string ResolverFinal(MisionDatos m12)
    {
        float apoyo = SistemaApoyoPopular.Instance != null ? SistemaApoyoPopular.Instance.apoyo : 0f;
        foreach (var r in m12.ramas)
        {
            bool okApoyo = r.condicion == null || apoyo >= r.condicion.apoyoMin;
            bool okFlag  = r.condicion == null || string.IsNullOrEmpty(r.condicion.flag) || FlagsNarrativos.Get(r.condicion.flag);
            if (okApoyo && okFlag) return r.resultado;
        }
        return "FINAL_B";
    }
}
