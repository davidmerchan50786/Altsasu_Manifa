// Assets/Scripts/Editor/ConfiguradorZonasEditor.cs
// ═══════════════════════════════════════════════════════════════════════════
//  CONFIGURADOR DE ZONAS — Herramienta de Editor
//
//  Menú: Tools → Alsasua → Configurar Zonas del Mapa
//
//  Qué hace en un clic:
//    1. Crea (o reutiliza) el GO "WorldManager" con GestorZonasAlsasua + SistemaChunks
//    2. Crea 10 GOs hijo vacíos, uno por zona, nombrados y posicionados correctamente
//    3. Los pinta con colores únicos (MaterialPropertyBlock)
//    4. Asigna todos los chunks al SistemaChunks automáticamente
//    5. Los desactiva para no sobrecargar la escena en Editor
//
//  WORKFLOW TÍPICO:
//    1. Ejecuta la herramienta (una vez por proyecto)
//    2. En la jerarquía aparecen: Zone_01_HerrikoPlaza, Zone_02_BarrioNorte, etc.
//    3. Para trabajar en una zona:
//         a) Actívala manualmente en la jerarquía (ojo del Inspector)
//         b) Añade tus GOs (edificios, props, luces) DENTRO del GO de la zona
//         c) Desactívala cuando termines
//    4. En Play, SistemaChunks activa/desactiva según la posición del jugador
//
//  Para reorganizar el mapa después:
//    "Herramientas → Alsasua → Solo mostrar zona..." permite activar una zona
//    y desactivar el resto con un solo clic.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Reflection;

public static class ConfiguradorZonasEditor
{
    // ─────────────────────────────────────────────────────────────────────────
    //  MENÚ PRINCIPAL — CONFIGURAR ZONAS
    // ─────────────────────────────────────────────────────────────────────────

    [MenuItem("Tools/Alsasua/📦 Configurar Zonas del Mapa", priority = 1)]
    public static void ConfigurarZonas()
    {
        // ── 1. WorldManager ─────────────────────────────────────────────────
        var worldMgr = GameObject.Find("WorldManager");
        if (worldMgr == null)
        {
            worldMgr = new GameObject("WorldManager");
            Undo.RegisterCreatedObjectUndo(worldMgr, "Crear WorldManager");
        }

        var gestor = worldMgr.GetComponent<GestorZonasAlsasua>()
                  ?? Undo.AddComponent<GestorZonasAlsasua>(worldMgr);
        var sistemaChunks = worldMgr.GetComponent<SistemaChunks>()
                         ?? Undo.AddComponent<SistemaChunks>(worldMgr);

        // ── 2. Crear GOs de zona ────────────────────────────────────────────
        var zonaDefs = GestorZonasAlsasua.Zonas;
        var chunkArray = new SistemaChunks.Chunk[zonaDefs.Length];

        for (int i = 0; i < zonaDefs.Length; i++)
        {
            var def = zonaDefs[i];
            string goName = $"Zone_{def.Id:D2}_{def.Nombre}";

            // Reutilizar GO existente o crear nuevo
            var zGO = GameObject.Find(goName);
            if (zGO == null)
            {
                zGO = new GameObject(goName);
                Undo.RegisterCreatedObjectUndo(zGO, $"Crear {goName}");
            }

            // Posición en el centro de la zona
            Undo.RecordObject(zGO.transform, $"Posicionar {goName}");
            zGO.transform.SetParent(worldMgr.transform, worldPositionStays: false);
            zGO.transform.localPosition = def.Centro;

            // Añadir marcador visual (esfera pequeña con color de zona)
            AnadirMarcadorVisual(zGO, def);

            // Desactivar en Editor (el streaming lo activa en Play)
            zGO.SetActive(false);

            // Configurar el chunk con los radios específicos de cada zona
            chunkArray[i] = new SistemaChunks.Chunk
            {
                go                   = zGO,
                nombre               = def.NombreDisplay,
                centro               = def.Centro,
                centroAuto           = false,            // ya tenemos el centro exacto de GeoData
                radioActivarPropio   = def.RadioActivar,
                radioDesactivarPropio= def.RadioDesactivar,
            };
        }

        // ── 3. Inyectar chunks en SistemaChunks via reflexión ───────────────
        // SistemaChunks.chunks es [SerializeField] private — accedemos via reflexión
        var campoChunks = typeof(SistemaChunks).GetField("chunks",
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (campoChunks != null)
        {
            Undo.RecordObject(sistemaChunks, "Asignar chunks a SistemaChunks");
            campoChunks.SetValue(sistemaChunks, chunkArray);

            // Ajustar radios por defecto del SistemaChunks a los de la zona más grande
            var campoRadioAct = typeof(SistemaChunks).GetField("radioActivacion",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var campoRadioDes = typeof(SistemaChunks).GetField("radioDesactivacion",
                BindingFlags.NonPublic | BindingFlags.Instance);
            // Los radios individuales se usan en el SistemaChunks extendido;
            // aquí dejamos el radio global en 300/400 como fallback.
            campoRadioAct?.SetValue(sistemaChunks, 300f);
            campoRadioDes?.SetValue(sistemaChunks, 400f);
        }
        else
        {
            Debug.LogWarning("[ConfiguradorZonas] No se pudo acceder al campo 'chunks' " +
                             "de SistemaChunks por reflexión. Asígnalo manualmente en el Inspector.");
        }

        // ── 4. Marcar escena como modificada ────────────────────────────────
        EditorUtility.SetDirty(worldMgr);
        EditorUtility.SetDirty(sistemaChunks);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        // ── 5. Seleccionar WorldManager en jerarquía ─────────────────────────
        Selection.activeGameObject = worldMgr;
        EditorGUIUtility.PingObject(worldMgr);

        Debug.Log($"[ConfiguradorZonas] ✓ {zonaDefs.Length} zonas configuradas en '{worldMgr.name}'.\n" +
                  "Activa/desactiva cada zona en la jerarquía para trabajar en ella.\n" +
                  "En Play, SistemaChunks las carga por proximidad.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  MENÚ: SOLO MOSTRAR ZONA X ─────────────────────────────────────────────
    // ─────────────────────────────────────────────────────────────────────────

    [MenuItem("Tools/Alsasua/👁 Zonas/01 · Herriko Plaza", priority = 20)]
    static void MostrarZona01() => AislarZona(1);

    [MenuItem("Tools/Alsasua/👁 Zonas/02 · Barrio Norte", priority = 21)]
    static void MostrarZona02() => AislarZona(2);

    [MenuItem("Tools/Alsasua/👁 Zonas/03 · Barrio Sur N-1", priority = 22)]
    static void MostrarZona03() => AislarZona(3);

    [MenuItem("Tools/Alsasua/👁 Zonas/04 · Cuartel GC", priority = 23)]
    static void MostrarZona04() => AislarZona(4);

    [MenuItem("Tools/Alsasua/👁 Zonas/05 · Estación Tren", priority = 24)]
    static void MostrarZona05() => AislarZona(5);

    [MenuItem("Tools/Alsasua/👁 Zonas/06 · Polígono Isasia", priority = 25)]
    static void MostrarZona06() => AislarZona(6);

    [MenuItem("Tools/Alsasua/👁 Zonas/07 · Polígono Ondarria", priority = 26)]
    static void MostrarZona07() => AislarZona(7);

    [MenuItem("Tools/Alsasua/👁 Zonas/08 · Ibarrea · Monte Artia", priority = 27)]
    static void MostrarZona08() => AislarZona(8);

    [MenuItem("Tools/Alsasua/👁 Zonas/09 · Entorno Natural Norte", priority = 28)]
    static void MostrarZona09() => AislarZona(9);

    [MenuItem("Tools/Alsasua/👁 Zonas/10 · Peña Blanca · Aralar", priority = 29)]
    static void MostrarZona10() => AislarZona(10);

    [MenuItem("Tools/Alsasua/👁 Zonas/── Mostrar TODAS", priority = 40)]
    static void MostrarTodasLasZonas()
    {
        foreach (var def in GestorZonasAlsasua.Zonas)
            SetZonaActiva(def.Id, true);
        Debug.Log("[ConfiguradorZonas] Todas las zonas activadas.");
    }

    [MenuItem("Tools/Alsasua/👁 Zonas/── Ocultar TODAS", priority = 41)]
    static void OcultarTodasLasZonas()
    {
        foreach (var def in GestorZonasAlsasua.Zonas)
            SetZonaActiva(def.Id, false);
        Debug.Log("[ConfiguradorZonas] Todas las zonas desactivadas.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  MENÚ: INFO DE ZONAS ────────────────────────────────────────────────────
    // ─────────────────────────────────────────────────────────────────────────

    [MenuItem("Tools/Alsasua/ℹ Info de zonas del mapa", priority = 60)]
    static void MostrarInfoZonas()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("═══ ZONAS DEL MAPA — ALSASUA ═══\n");
        foreach (var z in GestorZonasAlsasua.Zonas)
        {
            var go = GameObject.Find($"Zone_{z.Id:D2}_{z.Nombre}");
            string estado = go != null ? (go.activeSelf ? "✅ ACTIVA" : "⬜ inactiva") : "❌ no existe";
            sb.AppendLine($"  [{z.Id:D2}] {z.NombreDisplay}");
            sb.AppendLine($"       Centro: {z.Centro}  |  Radio: {z.RadioActivar}m");
            sb.AppendLine($"       {z.Descripcion}");
            sb.AppendLine($"       Estado en escena: {estado}");
            sb.AppendLine();
        }
        Debug.Log(sb.ToString());
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  HELPERS PRIVADOS
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Activa una zona y desactiva el resto para trabajar en aislamiento.</summary>
    private static void AislarZona(int id)
    {
        foreach (var def in GestorZonasAlsasua.Zonas)
            SetZonaActiva(def.Id, def.Id == id);

        var def2 = System.Array.Find(GestorZonasAlsasua.Zonas, z => z.Id == id);
        // Mover la cámara del Scene View a la zona
        MoverCamaraSceneView(def2.Centro);
        Debug.Log($"[ConfiguradorZonas] Zona {id:D2} aislada: {def2.NombreDisplay}\n" +
                  $"Contenido: {def2.Descripcion}");
    }

    private static void SetZonaActiva(int id, bool activa)
    {
        var def = System.Array.Find(GestorZonasAlsasua.Zonas, z => z.Id == id);
        var go  = GameObject.Find($"Zone_{id:D2}_{def.Nombre}");
        if (go == null)
        {
            Debug.LogWarning($"[ConfiguradorZonas] Zona {id:D2} no encontrada en escena. " +
                             "Ejecuta 'Configurar Zonas del Mapa' primero.");
            return;
        }
        Undo.RecordObject(go, activa ? $"Activar Zona {id}" : $"Desactivar Zona {id}");
        go.SetActive(activa);
        EditorUtility.SetDirty(go);
    }

    private static void MoverCamaraSceneView(Vector3 target)
    {
        var sv = SceneView.lastActiveSceneView;
        if (sv == null) return;
        sv.pivot    = target + Vector3.up * 50f;
        sv.rotation = Quaternion.Euler(45f, 0f, 0f);
        sv.size     = 200f;
        sv.Repaint();
    }

    /// <summary>
    /// Añade un marcador visual simple (cubo pequeño translúcido) para
    /// que la zona sea visible en el Scene View aunque esté vacía.
    /// </summary>
    private static void AnadirMarcadorVisual(GameObject zGO, GestorZonasAlsasua.DefinicionZona def)
    {
        // Buscar o crear el marcador hijo
        var existing = zGO.transform.Find("_Marcador");
        if (existing != null) return; // ya tiene marcador

        var marcador = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marcador.name = "_Marcador";
        Undo.RegisterCreatedObjectUndo(marcador, "Crear marcador de zona");
        marcador.transform.SetParent(zGO.transform, false);
        marcador.transform.localPosition = Vector3.up * 3f;
        marcador.transform.localScale    = Vector3.one * 4f;

        // Quitar colisionador — es solo visual
        var col = marcador.GetComponent<Collider>();
        if (col) Object.DestroyImmediate(col);

        // Color del material
        var renderer = marcador.GetComponent<Renderer>();
        if (renderer != null)
        {
            var mat = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"));
            if (mat != null)
            {
                mat.color = new Color(def.ColorGizmo.r, def.ColorGizmo.g, def.ColorGizmo.b, 0.6f);
                // HDRP: hacer transparente
                if (mat.HasProperty("_SurfaceType"))
                    mat.SetFloat("_SurfaceType", 1f); // transparent
                renderer.sharedMaterial = mat;
            }
        }

        // Etiqueta de texto con el número de zona (usando GizmoUtility)
        // Añadir un TextMesh para el nombre en Scene View
        var label = new GameObject("_Label");
        Undo.RegisterCreatedObjectUndo(label, "Crear label de zona");
        label.transform.SetParent(marcador.transform, false);
        label.transform.localPosition = Vector3.up * 2f;
        var tm = label.AddComponent<TextMesh>();
        tm.text      = $"[{def.Id:D2}] {def.NombreDisplay}";
        tm.fontSize  = 14;
        tm.color     = def.ColorGizmo;
        tm.anchor    = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
    }
}
