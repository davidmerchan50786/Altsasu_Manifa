#if UNITY_EDITOR
// Assets/Scripts/Editor/MenuMaestroAAA.cs
// ═══════════════════════════════════════════════════════════════════════════
//  MENÚ MAESTRO REORGANIZADO — FLUJO NUMERADO EN ORDEN
//
//  Submenu organizado:
//   Altsasu GTA/
//     ▶ Flujo AAA+/         ← orden numerado 1..12
//     Utilidades/           ← arreglos puntuales
//     Tests/                ← verificaciones
//
//  Los menús viejos sueltos (Maestro, Visual, Integración GTA, Cesium...)
//  se reagrupan en este submenú "Legacy" deshabilitable.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEditor;
using UnityEngine;

public static class MenuMaestroAAA
{
    const string M = "Altsasu GTA/▶ Flujo AAA+/";

    // ──────────────────────────────────────────────────────────────────
    //  FLUJO COMPLETO EN ORDEN (cada paso llama al script existente)
    // ──────────────────────────────────────────────────────────────────

    [MenuItem(M + "0  · Asegurar Tags necesarias", false, 0)]
    static void P0() => CallStatic("FixTags", "AñadirTagsMenu", "AñadirTags");

    [MenuItem(M + "1  · (Opcional) Descargar datos IGN — info", false, 1)]
    static void P1() => EditorUtility.DisplayDialog("Descargar datos IGN",
        "Abre una terminal y ejecuta:\n\n" +
        "cd E:\\Desk\\DAM\\Altsasu_Manifa\\Tools\n" +
        "pip install requests shapely pyproj\n" +
        "python descargar_ign_navarra.py\n\n" +
        "Tarda 2-5 min. Después pulsa el paso 2.", "OK");

    [MenuItem(M + "2  · (Opcional) Importar Datos IGN Navarra", false, 2)]
    static void P2() => CallStatic("ImportadorDatosIGN", "Importar");

    [MenuItem(M + "3  · Generar Texturas Procedurales", false, 13)]
    static void P3() => CallStatic("GeneradorTexturasProcedural", "Generar");

    [MenuItem(M + "3b · (Opcional) Crear Materiales PBR hiperrealistas — info", false, 13)]
    static void P3b() => EditorUtility.DisplayDialog("Materiales PBR (CC0)",
        "Para texturas hiperrealistas reales (ambientCG.com, CC0):\n\n" +
        "1. Abre terminal:\n" +
        "   cd E:\\Desk\\DAM\\Altsasu_Manifa\\Tools\n" +
        "   pip install requests\n" +
        "   python descargar_materiales_pbr.py\n\n" +
        "2. Tarda 2-3 min (~150 MB)\n\n" +
        "3. Luego: Altsasu GTA → Utilidades →\n" +
        "   ★ Crear Materiales PBR desde Texturas\n\n" +
        "El Paso 8 (Materiales AAA) detectará automáticamente los PBR\n" +
        "y los usará en lugar de los procedurales.", "OK");

    [MenuItem(M + "4  · Crear Terrain + Ortofoto", false, 14)]
    static void P4() => CallStatic("CrearTerrainEditor", "CrearTodo");

    [MenuItem(M + "5  · Generar Edificios OSM Reales", false, 15)]
    static void P5() => CallStatic("GeneradorEdificiosOSM", "Generar");

    [MenuItem(M + "6  · Generar Infraestructura (calles, tren, río)", false, 16)]
    static void P6() => CallStatic("GeneradorInfraestructura", "GenerarTodo");

    [MenuItem(M + "7  · Generar Puentes y Pasos Elevados", false, 17)]
    static void P7() => CallStatic("GeneradorPuentes", "Generar");

    [MenuItem(M + "8  · Aplicar Materiales AAA a Todo", false, 18)]
    static void P8() => CallStatic("AsignadorMaterialesAAA", "AplicarTodo");

    [MenuItem(M + "9  · Vegetación Real (árboles, arbustos)", false, 19)]
    static void P9() => CallStatic("GeneradorVegetacionReal", "Generar");

    [MenuItem(M + "10 · Mobiliario Urbano (farolas, bancos)", false, 20)]
    static void P10() => CallStatic("GeneradorMobiliarioUrbano", "Generar");

    [MenuItem(M + "11 · Integrador Total (vehículos, NPCs, fauna)", false, 21)]
    static void P11() => CallStatic("IntegradorTotalAAA", "IntegrarTodo");

    [MenuItem(M + "12 · Jugador Humano (Lucia + animaciones)", false, 22)]
    static void P12() => CallStatic("CrearJugadorHumano", "CrearJugador");

    [MenuItem(M + "13 · Detalles edificios (bajantes, AC, antenas, humo)", false, 23)]
    static void P13() => CallStatic("GeneradorDetalleEdificios", "Generar");

    [MenuItem(M + "14 · Decales urbanos (alcantarillas, manchas, pasos)", false, 24)]
    static void P14() => CallStatic("GeneradorDecalesUrbanos", "Generar");

    [MenuItem(M + "15 · ★ Realismo AAA+ (HDRP completo, probes, sol real)", false, 25)]
    static void P15() => CallStatic("EnriquecedorRealismoAAA", "EnriquecerTodo");

    [MenuItem(M + "16 · Hierba en terrain (GPU instanced + viento)", false, 26)]
    static void P16() => CallStatic("GeneradorHierbaTerreno", "Generar");

    [MenuItem(M + "17 · Detalles vivos (ventanas, cables, banderas)", false, 27)]
    static void P17() => CallStatic("GeneradorDetallesVivos", "Generar");

    [MenuItem(M + "18 · ★★ Realismo EXTREMO (HDRI + RayTracing + LensFlare)", false, 28)]
    static void P18() => CallStatic("EnriquecedorRealismoExtremo", "Aplicar");

    [MenuItem(M + "19 · Río Burunda (HDRP Water)", false, 29)]
    static void P19() => CallStatic("GeneradorRioBurunda", "Generar");

    [MenuItem(M + "20 · Montañas Aralar/Urbasa de fondo", false, 30)]
    static void P20() => CallStatic("GeneradorMontanasFondo", "Generar");

    [MenuItem(M + "21 · Vida ambiental (pájaros, audio 3D, faros, día/noche)", false, 31)]
    static void P21() => CallStatic("GeneradorVidaAmbiental", "Generar");

    [MenuItem(M + "22 · FIX FINAL (SceneBootstrapper)", false, 32)]
    static void P22() => CallStatic("FixTodoAAA", "FixTodo");

    [MenuItem(M + "──────────────────────────", false, 30)]
    static void Sep() { }

    [MenuItem(M + "▶▶ EJECUTAR TODO EN ORDEN (un solo clic)", false, 31)]
    static void EjecutarTodo()
    {
        if (!EditorUtility.DisplayDialog("Ejecutar TODO el flujo AAA+",
            "Se van a ejecutar los pasos 0, 3-13 en orden.\n" +
            "Tarda 3-5 minutos. Asegúrate de tener guardado.\n\n" +
            "Los pasos 1 y 2 (datos IGN) son opcionales y se omiten.",
            "⚡ Ejecutar", "Cancelar")) return;

        try
        {
            EditorUtility.DisplayProgressBar("Flujo AAA+", "Paso 0: Tags", 0.05f);
            P0();
            EditorUtility.DisplayProgressBar("Flujo AAA+", "Paso 3: Texturas", 0.12f); P3();
            EditorUtility.DisplayProgressBar("Flujo AAA+", "Paso 4: Terrain", 0.22f); P4();
            EditorUtility.DisplayProgressBar("Flujo AAA+", "Paso 5: Edificios OSM", 0.35f); P5();
            EditorUtility.DisplayProgressBar("Flujo AAA+", "Paso 6: Infraestructura", 0.48f); P6();
            EditorUtility.DisplayProgressBar("Flujo AAA+", "Paso 7: Puentes", 0.58f); P7();
            EditorUtility.DisplayProgressBar("Flujo AAA+", "Paso 8: Materiales", 0.68f); P8();
            EditorUtility.DisplayProgressBar("Flujo AAA+", "Paso 9: Vegetación", 0.78f); P9();
            EditorUtility.DisplayProgressBar("Flujo AAA+", "Paso 10: Mobiliario", 0.85f); P10();
            EditorUtility.DisplayProgressBar("Flujo AAA+", "Paso 11: Integrador Total", 0.84f); P11();
            EditorUtility.DisplayProgressBar("Flujo AAA+", "Paso 12: Jugador", 0.87f); P12();
            EditorUtility.DisplayProgressBar("Flujo AAA+", "Paso 13: Detalles edificios", 0.84f); P13();
            EditorUtility.DisplayProgressBar("Flujo AAA+", "Paso 14: Decales", 0.87f); P14();
            EditorUtility.DisplayProgressBar("Flujo AAA+", "Paso 15: Realismo AAA+", 0.90f); P15();
            EditorUtility.DisplayProgressBar("Flujo AAA+", "Paso 16: Hierba terrain", 0.93f); P16();
            EditorUtility.DisplayProgressBar("Flujo AAA+", "Paso 17: Detalles vivos", 0.95f); P17();
            EditorUtility.DisplayProgressBar("Flujo AAA+", "Paso 18: Realismo Extremo (HDRI+RT)", 0.96f); P18();
            EditorUtility.DisplayProgressBar("Flujo AAA+", "Paso 19: Río Burunda", 0.97f); P19();
            EditorUtility.DisplayProgressBar("Flujo AAA+", "Paso 20: Montañas Aralar", 0.98f); P20();
            EditorUtility.DisplayProgressBar("Flujo AAA+", "Paso 21: Vida ambiental", 0.99f); P21();
            EditorUtility.DisplayProgressBar("Flujo AAA+", "Paso 22: Fix final", 0.99f); P22();
        }
        finally { EditorUtility.ClearProgressBar(); }

        EditorUtility.DisplayDialog("✅ Flujo AAA+ completo",
            "Todo generado. Pulsa ▶ Play.", "¡A jugar!");
    }

    // ──────────────────────────────────────────────────────────────────
    //  Helper — invoca un método estático por reflexión
    // ──────────────────────────────────────────────────────────────────

    static void CallStatic(string nombreClase, params string[] candidatos)
    {
        // GUARD: ningún paso del flujo puede ejecutarse durante Play Mode
        // (causa System.InvalidOperationException en MarkSceneDirty, SaveOpenScenes, etc.)
        if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Play Mode activo",
                "No se puede ejecutar el flujo durante Play Mode.\n\n" +
                "Detén Play primero (botón ▶) y vuelve a intentarlo.", "OK");
            return;
        }

        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            System.Type[] types;
            try { types = asm.GetTypes(); }
            catch (System.Reflection.ReflectionTypeLoadException rtle)
            {
                // Asamblea con tipos parcialmente cargados (compile error en otro script):
                // usar el array Types que sí contiene los tipos que SÍ compilaron
                types = System.Array.FindAll(rtle.Types, t => t != null);
            }
            catch { continue; }

            foreach (var t in types)
            {
                if (t == null || t.Name != nombreClase) continue;
                foreach (var m in candidatos)
                {
                    var mi = t.GetMethod(m,
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Static);
                    if (mi != null)
                    {
                        mi.Invoke(null, null);
                        return;
                    }
                }
            }
        }
        Debug.LogError($"[Menu] No se encontró {nombreClase}.{string.Join("/", candidatos)} — verifica que el script compile sin errores.");
    }
}
#endif
