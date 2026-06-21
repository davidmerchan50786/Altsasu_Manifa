// Assets/Scripts/Editor/MigracionUTMRealAuto.cs
// ─────────────────────────────────────────────────────────────────────────────
//  Aplica de una sola vez la corrección a UTM real isótropo:
//   1) Reconstruye el mosaico de terreno V2 (RAW ya regenerados con SX=1)
//   2) Re-hornea la ortofoto (ahora escalaX=1)
//   3) Limpia y reconstruye edificios desde los footprints corregidos
//   4) Limpia y reconstruye calles + autovía desde los datos corregidos
//
//  Menú:  Tools ▸ Alsasua ▸ ▶▶ APLICAR TODO (UTM real)
//  Cada paso usa ExecuteMenuItem para no acoplarse a las clases concretas.
// ─────────────────────────────────────────────────────────────────────────────
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class MigracionUTMRealAuto
{
    static readonly string[] PASOS =
    {
        "Tools/Alsasua/Terreno/🧩 Construir Mosaico V2 (bake)",
        "Tools/Alsasua/Terreno/🗺 Aplicar Ortofoto TerrainLayer (legacy)",
        "Tools/Alsasua/Mundo/↩️ Limpiar Edificios de Asset",
        "Tools/Alsasua/Mundo/🏙️ Construir Edificios de Asset (footprints reales)",
        "Tools/Alsasua/Mundo/↩️ Limpiar Calles",
        "Tools/Alsasua/Mundo/🛣️ Construir Calles + Autovía (full, v2)",
    };

    [MenuItem("Tools/Alsasua/▶▶ APLICAR TODO (UTM real)", priority = 0)]
    public static void EjecutarTodo()
    {
        Debug.Log("════════ MigracionUTM: INICIO ════════");
        var fallos = new List<string>();
        for (int i = 0; i < PASOS.Length; i++)
        {
            string p = PASOS[i];
            Debug.Log($"[MigracionUTM] ({i + 1}/{PASOS.Length}) {p}");
            bool ok = false;
            try { ok = EditorApplication.ExecuteMenuItem(p); }
            catch (System.Exception e) { Debug.LogError($"[MigracionUTM] EXCEPCIÓN en '{p}': {e.Message}"); }
            if (!ok) { fallos.Add(p); Debug.LogWarning($"[MigracionUTM]   ⚠ no ejecutado: {p}"); }
            else      Debug.Log($"[MigracionUTM]   ✓ {p}");
            AssetDatabase.SaveAssets();
        }
        AssetDatabase.Refresh();
        if (fallos.Count == 0)
            Debug.Log("════════ MigracionUTM: ✅ COMPLETADO (todos los pasos) ════════");
        else
        {
            Debug.LogWarning($"════════ MigracionUTM: terminado con {fallos.Count} paso(s) sin ejecutar ════════");
            foreach (var f in fallos) Debug.LogWarning("   · " + f);
        }
    }
}
