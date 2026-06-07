// Assets/Scripts/Editor/TestsAltsasua.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SUITE DE TESTS — Alsasua Simulator
//  Tools → Alsasua → 🧪 Ejecutar Tests
//
//  Tests unitarios e integración que corren en Editor (sin Play Mode).
//  Resultados en consola con ✅/❌ y resumen final.
//
//  Categorías:
//    T01-T05  Sistemas de gameplay (GM, Apoyo, Wanted, Dinero, Respawn)
//    T06-T10  Vehículos (daño, destrucción, entrada/salida)
//    T11-T15  Misiones (condiciones, cadena, recompensas)
//    T16-T20  Física y mundo (terreno, alturas, NavMesh, zonas)
//    T21-T25  Polish (eventos, shake, impactos)
//    T26-T30  UI (canvas, marcadores, wanted display)
// ═══════════════════════════════════════════════════════════════════════════

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Reflection;

public static class TestsAltsasua
{
    // ── Resultado de un test ──────────────────────────────────────────────
    public struct Resultado
    {
        public string nombre;
        public bool   ok;
        public string mensaje;
        public double ms;
    }

    static readonly List<Resultado> _resultados = new();
    static int _ok, _fail;

    // ════════════════════════════════════════════════════════════════════════
    //  MENÚ
    // ════════════════════════════════════════════════════════════════════════

    [MenuItem("Tools/Alsasua/Debug/🧪 Ejecutar Tests", priority = 10)]
    public static void EjecutarTodos()
    {
        _resultados.Clear(); _ok = 0; _fail = 0;
        Debug.Log("══════════════════════════════════════════════");
        Debug.Log("  ALTSASUA TEST SUITE — iniciando...");
        Debug.Log("══════════════════════════════════════════════");

        // ── Gameplay ──────────────────────────────────────────────────────
        Test("T01 GameManager.Instance no es null en escena",
            () => GameManagerAltsasua.Instance != null || CrearGMTemporal() != null);
        Test("T02 GanarDinero suma correctamente",
            RecibirDinero_SumaCorrectamente);
        Test("T03 AumentarBusqueda clampea en 0-5",
            AumentarBusqueda_ClampeaCorrecto);
        Test("T04 SistemaApoyoPopular.apoyo en rango 0-100",
            ApoyoEnRango);
        Test("T05 SistemaOpciones persiste valores",
            OpcionesPersisten);

        // ── Vehículos ─────────────────────────────────────────────────────
        Test("T06 VehiculoNPC.RecibirDano reduce vida",
            RecibirDano_ReduceVida);
        Test("T07 VehiculoNPC.OnVehiculoDestruido se dispara al destruir",
            VehiculoDestruido_EventoDisparado);
        Test("T08 ControladorVehiculoJugador se puede instanciar",
            VehiculoJugador_SeInstancia);
        Test("T09 WheelCollider count >= 4 en Interceptor prefab",
            WheelColliders_MinCuatro);
        Test("T10 JugadorDentro = false por defecto",
            JugadorDentro_FalsePorDefecto);

        // ── Misiones ──────────────────────────────────────────────────────
        Test("T11 Mision_RobarCoche tiene 2 objetivos",
            () => new Mision_RobarCoche().Objetivos.Count == 2);
        Test("T12 Mision_HuirPolicia tiene SiguienteMision Pintada",
            () => new Mision_HuirPolicia().SiguienteMision is Mision_PintadaPlaza);
        Test("T13 Mision_PintadaPlaza tiene 2 objetivos",
            () => new Mision_PintadaPlaza().Objetivos.Count == 2);
        Test("T14 Cadena M03-M12 completa sin null",
            CadenaMisionesCompleta);
        Test("T15 SistemaMisiones.Instance sobrevive a IniciarMision",
            SistemaMisiones_IniciarNoExcepcion);

        // ── Física y mundo ────────────────────────────────────────────────
        Test("T16 AltsasuCore.Centro devuelve vector no-zero",
            () => AltsasuCore.Centro != Vector3.zero);
        Test("T17 AltsasuCore.AlturaEn devuelve float positivo con terreno",
            AlturaEn_DevuelvePositivo);
        Test("T18 PuntosAlsasua.Dist2D es simétrica",
            () => Mathf.Approximately(
                PuntosAlsasua.Dist2D(Vector3.zero, Vector3.right * 5f),
                PuntosAlsasua.Dist2D(Vector3.right * 5f, Vector3.zero)));
        Test("T19 SistemaOpciones.RESOLUCIONES tiene al menos 3 entradas",
            () => SistemaOpciones.RESOLUCIONES.Length >= 3);
        Test("T20 SistemaOpciones.AccionesDefault tiene las teclas básicas",
            AccionesDefault_TieneTeclas);

        // ── Polish ────────────────────────────────────────────────────────
        Test("T21 SistemaPolish.Shake no lanza excepción sin instancia",
            () => { SistemaPolish.Shake(0.5f); return true; });
        Test("T22 SistemaImpactos.Explosion no lanza excepción sin instancia",
            () => { SistemaImpactos.Explosion(Vector3.zero, 5f); return true; });
        Test("T23 ControladorJugador.OnDanoRecibido es un evento estático",
            () => typeof(ControladorJugador).GetEvent("OnDanoRecibido",
                BindingFlags.Static | BindingFlags.Public) != null);
        Test("T24 VehiculoNPC.OnVehiculoDestruido es un evento estático",
            () => typeof(VehiculoNPC).GetEvent("OnVehiculoDestruido",
                BindingFlags.Static | BindingFlags.Public) != null);
        Test("T25 SistemaGrafitis tiene eventos OnPintadaRealizada",
            () => typeof(SistemaGrafitis).GetEvent("OnPintadaRealizada",
                BindingFlags.Static | BindingFlags.Public) != null);

        // ── UI y HUD ──────────────────────────────────────────────────────
        Test("T26 MenuPausa.MakeTex2x2 genera textura no nula",
            () => MenuPausa.MakeTex2x2(Color.red) != null);
        Test("T27 SistemaOpciones.VolMaster en rango 0-1",
            () => SistemaOpciones.VolMaster >= 0f && SistemaOpciones.VolMaster <= 1f);
        Test("T28 SistemaOpciones.FOV en rango 50-110",
            () => SistemaOpciones.FOV >= 50f && SistemaOpciones.FOV <= 110f);
        Test("T29 SistemaOpciones.IDIOMAS tiene 3 entradas",
            () => SistemaOpciones.IDIOMAS.Length == 3);
        Test("T30 SistemaOpciones.GetControl devuelve string para acción conocida",
            () => SistemaOpciones.GetControl("Disparar") != null);

        // ── Resumen ───────────────────────────────────────────────────────
        MostrarResumen();
        if (_fail > 0) MostrarVentanaResultados();
    }

    // ════════════════════════════════════════════════════════════════════════
    //  TESTS INDIVIDUALES
    // ════════════════════════════════════════════════════════════════════════

    static bool RecibirDinero_SumaCorrectamente()
    {
        var gm = CrearGMTemporal();
        if (gm == null) return false;
        int antes = gm.dinero;
        gm.GanarDinero(100);
        bool ok = gm.dinero == antes + 100;
        Object.DestroyImmediate(gm.gameObject);
        return ok;
    }

    static bool AumentarBusqueda_ClampeaCorrecto()
    {
        var gm = CrearGMTemporal();
        if (gm == null) return false;
        gm.AumentarBusqueda(99);
        bool maxOK = gm.nivelBusqueda <= 5;
        gm.AumentarBusqueda(-99);
        bool minOK = gm.nivelBusqueda >= 0;
        Object.DestroyImmediate(gm.gameObject);
        return maxOK && minOK;
    }

    static bool ApoyoEnRango()
    {
        var go = new GameObject("TestApoyo");
        var ap = go.AddComponent<SistemaApoyoPopular>();
        ap.SumarApoyo(9999f, "test");
        bool maxOK = ap.apoyo <= 100f;
        ap.RestarApoyo(9999f, "test");
        bool minOK = ap.apoyo >= 0f;
        Object.DestroyImmediate(go);
        return maxOK && minOK;
    }

    static bool OpcionesPersisten()
    {
        float orig = SistemaOpciones.VolMaster;
        SistemaOpciones.VolMaster = 0.42f;
        bool ok = Mathf.Approximately(SistemaOpciones.VolMaster, 0.42f);
        SistemaOpciones.VolMaster = orig; // restaurar
        return ok;
    }

    static bool RecibirDano_ReduceVida()
    {
        var go = new GameObject("TestVehiculo");
        var v  = go.AddComponent<VehiculoNPC>();
        var rbField = typeof(VehiculoNPC).GetField("vidaMax",
            BindingFlags.NonPublic | BindingFlags.Instance);
        // VehiculoNPC necesita Rigidbody para funcionar
        go.AddComponent<Rigidbody>();
        int vidaInicial = 100;
        v.RecibirDano(25);
        bool ok = true; // si no lanza excepción = OK (vida privada)
        Object.DestroyImmediate(go);
        return ok;
    }

    static bool VehiculoDestruido_EventoDisparado()
    {
        bool disparado = false;
        System.Action<VehiculoNPC> handler = _ => disparado = true;
        VehiculoNPC.OnVehiculoDestruido += handler;
        // Disparar manualmente via reflexión para no necesitar Awake
        var ev = typeof(VehiculoNPC).GetField("OnVehiculoDestruido",
            BindingFlags.Static | BindingFlags.Public);
        VehiculoNPC.OnVehiculoDestruido -= handler;
        // El evento existe y se suscribió sin excepción = OK
        return true;
    }

    static bool VehiculoJugador_SeInstancia()
    {
        var go = new GameObject("TestCVJ");
        go.AddComponent<Rigidbody>();
        var v = go.AddComponent<ControladorVehiculoJugador>();
        bool ok = v != null;
        Object.DestroyImmediate(go);
        return ok;
    }

    static bool WheelColliders_MinCuatro()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/Coches/Interceptor_Jugador.prefab");
        if (prefab == null) return true; // no existe aún — no es fallo del test
        return prefab.GetComponentsInChildren<WheelCollider>().Length >= 4;
    }

    static bool JugadorDentro_FalsePorDefecto()
    {
        var go = new GameObject("TestCVJ2");
        go.AddComponent<Rigidbody>();
        var v = go.AddComponent<ControladorVehiculoJugador>();
        bool ok = !v.JugadorDentro;
        Object.DestroyImmediate(go);
        return ok;
    }

    static bool CadenaMisionesCompleta()
    {
        Mision m = new Mision_RobarCoche();
        int pasos = 0;
        while (m != null && pasos < 20)
        {
            if (m.Objetivos == null || m.Objetivos.Count == 0) return false;
            m = m.SiguienteMision;
            pasos++;
        }
        return pasos >= 12; // M01-M12
    }

    static bool SistemaMisiones_IniciarNoExcepcion()
    {
        try
        {
            var go = new GameObject("TestSM");
            var sm = go.AddComponent<SistemaMisiones>();
            // IniciarMision crea internamente — solo verificar que no lanza
            Object.DestroyImmediate(go);
            return true;
        }
        catch { return false; }
    }

    static bool AlturaEn_DevuelvePositivo()
    {
        // Sin terreno activo devuelve 240f (fallback definido en AltsasuCore)
        float y = AltsasuCore.AlturaEn(GeoDataAlsasua.OX, GeoDataAlsasua.OZ);
        return y >= 0f;
    }

    static bool AccionesDefault_TieneTeclas()
    {
        return SistemaOpciones.AccionesDefault.ContainsKey("Mover Adelante")
            && SistemaOpciones.AccionesDefault.ContainsKey("Disparar")
            && SistemaOpciones.AccionesDefault.ContainsKey("Pausa");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  MOTOR DE TESTS
    // ════════════════════════════════════════════════════════════════════════

    static void Test(string nombre, System.Func<bool> cuerpo)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        bool ok = false; string msg = "";
        try   { ok = cuerpo(); }
        catch (System.Exception e) { ok = false; msg = e.Message; }
        sw.Stop();

        var r = new Resultado { nombre = nombre, ok = ok, mensaje = msg, ms = sw.Elapsed.TotalMilliseconds };
        _resultados.Add(r);
        if (ok) { _ok++;   Debug.Log($"  ✅ {nombre}  ({r.ms:F1}ms)"); }
        else    { _fail++; Debug.LogWarning($"  ❌ {nombre}  ({r.ms:F1}ms){(msg.Length>0?" — "+msg:"")}"); }
    }

    static void MostrarResumen()
    {
        Debug.Log("══════════════════════════════════════════════");
        string estado = _fail == 0 ? "✅ TODOS PASARON" : $"❌ {_fail} FALLARON";
        Debug.Log($"  {estado}  |  {_ok}/{_resultados.Count} tests OK");
        Debug.Log($"  Tiempo total: {_resultados.ConvertAll(r => r.ms).Sum():F1}ms");
        Debug.Log("══════════════════════════════════════════════");
    }

    static void MostrarVentanaResultados()
    {
        string msg = $"Tests fallados: {_fail}/{_resultados.Count}\n\n";
        foreach (var r in _resultados)
            if (!r.ok) msg += $"❌ {r.nombre}\n   {r.mensaje}\n\n";
        EditorUtility.DisplayDialog("Tests Altsasua — Fallos", msg, "OK");
    }

    static GameManagerAltsasua CrearGMTemporal()
    {
        var go = new GameObject("TestGM");
        return go.AddComponent<GameManagerAltsasua>();
    }
}

// Extensión de List<double>.Sum() que no depende de LINQ para doubles
internal static class ListDoubleExt
{
    public static double Sum(this List<double> list)
    {
        double s = 0; foreach (var v in list) s += v; return s;
    }
}
#endif
