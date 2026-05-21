// Assets/Scripts/Editor/GeneradorEdificiosZona.cs — v2 AAA
// ═══════════════════════════════════════════════════════════════════════════
//  Menú: Tools → Alsasua → 🏠 Generar Edificios Zone_01 (AAA)
//
//  Genera edificios reales de Alsasua desde buildings_unity.json con:
//    · Footprint OSM exacto (polígonos reales de cada edificio)
//    · Tejados a dos aguas para casas / planos para industria / cúpula para iglesias
//    · Ventanas procedurales por planta y planta de fachada
//    · Cornisas en el remate superior de la pared
//    · 3 niveles de LOD (LOD0 < 60m, LOD1 < 150m, LOD2 < 400m)
//    · 4 variantes de material según tipo OSM
//    · Objetos estáticos con MeshCollider para NavMesh y física
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Linq;

public static class GeneradorEdificiosZona
{
    // ── Parámetros de zona ────────────────────────────────────────────────
    private const string JSON_PATH   = "Assets/AlsasuaData/buildings_unity.json";
    private const string ZONA_NOMBRE = "Zone_01_HerrikoPlaza_Centro";
    private const float  CENTRO_X    = 1918f;
    private const float  CENTRO_Z    = 8570f;
    private const float  RADIO_ZONA  = 350f;
    private const int    MAX_EDIFICIOS = 600;

    // ── Distancias de LOD ─────────────────────────────────────────────────
    private const float LOD0_DIST = 60f;
    private const float LOD1_DIST = 150f;
    private const float LOD2_DIST = 400f;

    // ── Proporciones arquitectónicas ──────────────────────────────────────
    private const float ALTURA_PLANTA    = 3.0f;  // metros por planta
    private const float CORNIS_ALTO      = 0.3f;  // altura de cornisa (m)
    private const float CORNIS_VUELO     = 0.15f; // vuelo de cornisa hacia fuera (m)
    private const float VENTANA_ANCHO    = 1.1f;  // ancho de ventana (m)
    private const float VENTANA_ALTO     = 1.4f;  // alto de ventana (m)
    private const float VENTANA_OFFSET_Z = 0.04f; // desplazamiento desde la pared

    // ── Materiales compartidos (creados una vez) ──────────────────────────
    private static Material _matLadrillo, _matPiedra, _matIndustrial,
                             _matIglesia,  _matVentana, _matTejado;

    // ─────────────────────────────────────────────────────────────────────────
    //  MENÚS
    // ─────────────────────────────────────────────────────────────────────────

    [MenuItem("Tools/Alsasua/Mundo/🏠 Generar Edificios Zone_01", priority = 30)]
    public static void GenerarEdificios() => GenerarEdificios(silencioso: false);
    public static void GenerarEdificios(bool silencioso)
    {
        string rutaAbs = Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", JSON_PATH));
        if (!File.Exists(rutaAbs))
        { EditorUtility.DisplayDialog("Error", $"No se encontró:\n{rutaAbs}", "OK"); return; }

        var zonaGO = GameObject.Find(ZONA_NOMBRE);
        if (zonaGO == null)
        { EditorUtility.DisplayDialog("Error",
            $"'{ZONA_NOMBRE}' no está en la escena.\nEjecuta primero 'Configurar Zonas del Mapa'.", "OK"); return; }

        bool estabaActiva = zonaGO.activeSelf;
        zonaGO.SetActive(true);
        LimpiarHijos(zonaGO);

        // Crear materiales compartidos
        CrearMateriales();

        // Parsear y filtrar edificios
        var todos = ParsearEdificios(File.ReadAllText(rutaAbs));
        var enZona = todos.Where(e => {
            float dx = e.cx - CENTRO_X, dz = e.cz - CENTRO_Z;
            return dx*dx + dz*dz <= RADIO_ZONA * RADIO_ZONA && e.poly.Count >= 3;
        }).Take(MAX_EDIFICIOS).ToList();

        int generados = 0;
        for (int i = 0; i < enZona.Count; i++)
        {
            EditorUtility.DisplayProgressBar("Generando edificios AAA",
                $"{i+1}/{enZona.Count}  {enZona[i].name}", (float)i / enZona.Count);
            try { GenerarEdificioAAA(enZona[i], zonaGO); generados++; }
            catch (System.Exception ex)
            { Debug.LogWarning($"[Edificios] Error en {enZona[i].name}: {ex.Message}"); }
        }

        EditorUtility.ClearProgressBar();
        EditorUtility.SetDirty(zonaGO);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
        zonaGO.SetActive(estabaActiva);

        Debug.Log($"[Edificios] ✅ {generados}/{enZona.Count} edificios AAA generados en Zone_01.");
        if (!silencioso)
            EditorUtility.DisplayDialog("Edificios generados",
                $"✅ {generados} edificios OSM con LODs, ventanas y tejados.\n\n" +
                "Siguiente: Tools → Alsasua → 👁 Zonas → 01 · Herriko Plaza", "OK");
    }

    [MenuItem("Tools/Alsasua/Mundo/🗑 Limpiar Edificios Zone_01", priority = 11)]
    public static void LimpiarEdificios()
    {
        var z = GameObject.Find(ZONA_NOMBRE); if (z == null) return;
        bool a = z.activeSelf; z.SetActive(true);
        int n = LimpiarHijos(z); z.SetActive(a);
        EditorUtility.SetDirty(z);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
        Debug.Log($"[Edificios] 🗑 {n} edificios eliminados.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  GENERACIÓN AAA DE UN EDIFICIO
    // ─────────────────────────────────────────────────────────────────────────

    private static void GenerarEdificioAAA(DatosEdificio e, GameObject zona)
    {
        TipoEdificio tipo = DetectarTipo(e);
        float y = ObtenerY(e.cx, e.cz, e.cy);

        // Raíz del edificio
        string nombre = string.IsNullOrEmpty(e.name) ? $"Ed_{e.osmId}" : $"{e.name}_{e.osmId}";
        var raiz = new GameObject(nombre);
        Undo.RegisterCreatedObjectUndo(raiz, "Crear edificio");
        raiz.transform.SetParent(zona.transform, false);
        raiz.transform.position = new Vector3(e.cx, y, e.cz);
        raiz.layer = 0;

        // ── LOD0 — calidad completa (ventanas, cornisas, tejado correcto) ──
        var lod0 = new GameObject("LOD0");
        lod0.transform.SetParent(raiz.transform, false);
        AnadirMeshRenderer(lod0, GenerarParedesConCornisa(e.poly, e.height), MatPared(tipo));
        AnadirMeshRenderer(new GameObject("Tejado") { transform = { parent = lod0.transform } },
            GenerarTejado(e.poly, e.height, tipo), _matTejado);
        GenerarVentanas(lod0, e.poly, e.height, tipo);
        lod0.isStatic = true;

        // ── LOD1 — paredes simples, sin ventanas, tejado simplificado ─────
        var lod1 = new GameObject("LOD1");
        lod1.transform.SetParent(raiz.transform, false);
        AnadirMeshRenderer(lod1, GenerarParedesSimples(e.poly, e.height), MatPared(tipo));
        AnadirMeshRenderer(new GameObject("Tejado") { transform = { parent = lod1.transform } },
            GenerarTejadoPlano(e.poly), _matTejado);
        lod1.isStatic = true;

        // ── LOD2 — caja simple (bounding box) ─────────────────────────────
        var lod2 = new GameObject("LOD2");
        lod2.transform.SetParent(raiz.transform, false);
        AnadirMeshRenderer(lod2, GenerarCajaBBox(e.poly, e.height), MatPared(tipo));
        lod2.isStatic = true;

        // ── LODGroup ───────────────────────────────────────────────────────
        var lodGroup = raiz.AddComponent<LODGroup>();
        lodGroup.SetLODs(new[] {
            new LOD(1f / LOD0_DIST * 4f, lod0.GetComponentsInChildren<Renderer>()),
            new LOD(1f / LOD1_DIST * 4f, lod1.GetComponentsInChildren<Renderer>()),
            new LOD(1f / LOD2_DIST * 4f, lod2.GetComponentsInChildren<Renderer>()),
        });
        lodGroup.RecalculateBounds();

        // ── MeshCollider sobre LOD1 (para NavMesh y física) ───────────────
        var col = raiz.AddComponent<MeshCollider>();
        col.sharedMesh = GenerarMeshColision(e.poly, e.height);
        col.convex = false;

        raiz.isStatic = true;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  GENERACIÓN DE MESHES
    // ─────────────────────────────────────────────────────────────────────────

    // LOD0 — Paredes con cornisa en el remate superior
    private static Mesh GenerarParedesConCornisa(List<Vector2> poly, float h)
    {
        int n = poly.Count;
        var verts = new List<Vector3>(); var tris = new List<int>(); var uvs = new List<Vector2>();

        float uAcum = 0f;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            Vector2 a = poly[i], b = poly[j];
            float L = Vector2.Distance(a, b);

            // Dirección perpendicular de la pared (hacia fuera)
            Vector2 dir = (b - a).normalized;
            Vector2 perp = new Vector2(-dir.y, dir.x); // normal exterior

            float hPared = h - CORNIS_ALTO;

            // ── Pared principal ─────────────────────────────────────────
            int v0 = verts.Count;
            verts.Add(new Vector3(a.x, 0f,    a.y));
            verts.Add(new Vector3(b.x, 0f,    b.y));
            verts.Add(new Vector3(b.x, hPared, b.y));
            verts.Add(new Vector3(a.x, hPared, a.y));

            float u0 = uAcum / 3f, u1 = (uAcum + L) / 3f;
            uvs.Add(new Vector2(u0, 0f)); uvs.Add(new Vector2(u1, 0f));
            uvs.Add(new Vector2(u1, hPared / 3f)); uvs.Add(new Vector2(u0, hPared / 3f));
            tris.AddRange(new[]{ v0,v0+3,v0+1, v0+1,v0+3,v0+2 });
            uAcum += L;

            // ── Cornisa ─────────────────────────────────────────────────
            // Geometría: banda horizontal que sobresale CORNIS_VUELO m hacia fuera
            float cx = perp.x * CORNIS_VUELO, cz = perp.y * CORNIS_VUELO;
            int c0 = verts.Count;
            // Cara frontal de cornisa
            verts.Add(new Vector3(a.x,         hPared,              a.y));
            verts.Add(new Vector3(b.x,         hPared,              b.y));
            verts.Add(new Vector3(b.x + cx,    h,                   b.y + cz));
            verts.Add(new Vector3(a.x + cx,    h,                   a.y + cz));
            uvs.Add(new Vector2(0f,0f)); uvs.Add(new Vector2(L/3f,0f));
            uvs.Add(new Vector2(L/3f,CORNIS_ALTO/3f)); uvs.Add(new Vector2(0f,CORNIS_ALTO/3f));
            tris.AddRange(new[]{ c0,c0+3,c0+1, c0+1,c0+3,c0+2 });
        }

        return BuildMesh("Paredes", verts, tris, uvs);
    }

    // LOD1 — Paredes simples sin cornisa
    private static Mesh GenerarParedesSimples(List<Vector2> poly, float h)
    {
        int n = poly.Count;
        var verts = new List<Vector3>(); var tris = new List<int>(); var uvs = new List<Vector2>();
        float uAcum = 0f;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            float L = Vector2.Distance(poly[i], poly[j]);
            int v0 = verts.Count;
            verts.Add(new Vector3(poly[i].x, 0f, poly[i].y));
            verts.Add(new Vector3(poly[j].x, 0f, poly[j].y));
            verts.Add(new Vector3(poly[j].x, h,  poly[j].y));
            verts.Add(new Vector3(poly[i].x, h,  poly[i].y));
            float u0 = uAcum/3f, u1 = (uAcum+L)/3f;
            uvs.Add(new Vector2(u0,0f)); uvs.Add(new Vector2(u1,0f));
            uvs.Add(new Vector2(u1,h/3f)); uvs.Add(new Vector2(u0,h/3f));
            tris.AddRange(new[]{ v0,v0+3,v0+1, v0+1,v0+3,v0+2 });
            uAcum += L;
        }
        return BuildMesh("Paredes_L1", verts, tris, uvs);
    }

    // Tejado según tipo de edificio
    private static Mesh GenerarTejado(List<Vector2> poly, float h, TipoEdificio tipo)
    {
        switch (tipo)
        {
            case TipoEdificio.Residencial:
            case TipoEdificio.Detached:
                return GenerarTejadoDosAguas(poly, h);
            case TipoEdificio.Iglesia:
                return GenerarTejadoCupula(poly, h);
            default:
                return GenerarTejadoPlano(poly);
        }
    }

    // Tejado a dos aguas — calcula el eje mayor y crea la cresta
    private static Mesh GenerarTejadoDosAguas(List<Vector2> poly, float h)
    {
        // Bounding box para determinar eje mayor
        float minX = poly.Min(p=>p.x), maxX = poly.Max(p=>p.x);
        float minZ = poly.Min(p=>p.y), maxZ = poly.Max(p=>p.y);
        float spanX = maxX - minX, spanZ = maxZ - minZ;
        float crestAltura = Mathf.Min(spanX, spanZ) * 0.35f; // inclinación ~35%
        Vector2 centro2D = new Vector2((minX+maxX)/2f, (minZ+maxZ)/2f);

        var verts = new List<Vector3>(); var tris = new List<int>(); var uvs = new List<Vector2>();
        int n = poly.Count;

        // Cresta: dos vértices en el eje mayor del bounding box
        Vector3 crestA, crestB;
        if (spanX >= spanZ)
        {
            crestA = new Vector3(minX, h + crestAltura, centro2D.y);
            crestB = new Vector3(maxX, h + crestAltura, centro2D.y);
        }
        else
        {
            crestA = new Vector3(centro2D.x, h + crestAltura, minZ);
            crestB = new Vector3(centro2D.x, h + crestAltura, maxZ);
        }

        // Añadir base del polígono y cresta
        int baseIdx = verts.Count;
        foreach (var p in poly) { verts.Add(new Vector3(p.x, h, p.y)); uvs.Add(new Vector2(p.x/6f, p.y/6f)); }
        int cAIdx = verts.Count; verts.Add(crestA); uvs.Add(new Vector2(0.5f, 0f));
        int cBIdx = verts.Count; verts.Add(crestB); uvs.Add(new Vector2(0.5f, 1f));

        // Triangular cada arista del polígono hacia la cresta más cercana
        for (int i = 0; i < n; i++)
        {
            int a = baseIdx + i, b = baseIdx + (i+1)%n;
            Vector3 mid = (verts[a] + verts[b]) * 0.5f;
            // Determinar qué extremo de la cresta está más cerca del punto medio de la arista
            bool haciaA = (mid - crestA).sqrMagnitude < (mid - crestB).sqrMagnitude;
            int c = haciaA ? cAIdx : cBIdx;
            tris.AddRange(new[]{ a, c, b, b, c, a });
        }
        // Cara central (si la cresta divide el techo)
        tris.AddRange(new[]{ cAIdx, baseIdx, cBIdx, cBIdx, baseIdx+n-1, cAIdx });

        return BuildMesh("Tejado_DosAguas", verts, tris, uvs);
    }

    // Tejado plano (piramidal simple) para apartamentos e industria
    private static Mesh GenerarTejadoPlano(List<Vector2> poly)
    {
        int n = poly.Count;
        var verts = new List<Vector3>(); var tris = new List<int>(); var uvs = new List<Vector2>();
        Vector2 cen = poly.Aggregate(Vector2.zero, (a,p) => a+p) / n;
        verts.Add(new Vector3(cen.x, 0, cen.y)); uvs.Add(new Vector2(0.5f, 0.5f));
        foreach (var p in poly) { verts.Add(new Vector3(p.x, 0, p.y)); uvs.Add(new Vector2(p.x/6f, p.y/6f)); }
        for (int i=0;i<n;i++) tris.AddRange(new[]{ 0, 1+(i+1)%n, 1+i, 0, 1+i, 1+(i+1)%n });
        return BuildMesh("Tejado_Plano", verts, tris, uvs);
    }

    // Tejado tipo cúpula escalonada (para iglesias / capillas)
    private static Mesh GenerarTejadoCupula(List<Vector2> poly, float h)
    {
        // Pirámide de 3 escalones
        int n = poly.Count; int pasos = 3;
        var verts = new List<Vector3>(); var tris = new List<int>(); var uvs = new List<Vector2>();
        Vector2 cen = poly.Aggregate(Vector2.zero,(a,p)=>a+p)/n;
        float heightStep = 1.5f;

        List<Vector2> ring = new List<Vector2>(poly);
        for (int step = 0; step < pasos; step++)
        {
            float t = (float)(step+1) / (pasos+1);
            List<Vector2> inner = ring.Select(p => Vector2.Lerp(p, cen, t * 0.7f)).ToList();
            float y0 = step * heightStep, y1 = y0 + heightStep;
            int ringBase = verts.Count;
            foreach (var p in ring)   { verts.Add(new Vector3(p.x, y0, p.y)); uvs.Add(new Vector2(p.x/4f,y0/4f)); }
            foreach (var p in inner)  { verts.Add(new Vector3(p.x, y1, p.y)); uvs.Add(new Vector2(p.x/4f,y1/4f)); }
            for (int i=0;i<n;i++)
            {
                int a=ringBase+i, b=ringBase+(i+1)%n, c=ringBase+n+i, d=ringBase+n+(i+1)%n;
                tris.AddRange(new[]{a,c,b, b,c,d});
            }
            ring = inner;
        }
        // Punta final
        int puntaBase = verts.Count; int puntaIdx = puntaBase + n;
        foreach (var p in ring) { verts.Add(new Vector3(p.x, pasos*heightStep, p.y)); uvs.Add(new Vector2(0.5f,0.5f)); }
        verts.Add(new Vector3(cen.x, pasos*heightStep + 2f, cen.y)); uvs.Add(new Vector2(0.5f,0.5f));
        for (int i=0;i<n;i++) tris.AddRange(new[]{puntaBase+i, puntaIdx, puntaBase+(i+1)%n});
        return BuildMesh("Tejado_Cupula", verts, tris, uvs);
    }

    // Caja simple para LOD2
    private static Mesh GenerarCajaBBox(List<Vector2> poly, float h)
    {
        float minX=poly.Min(p=>p.x), maxX=poly.Max(p=>p.x);
        float minZ=poly.Min(p=>p.y), maxZ=poly.Max(p=>p.y);
        var v = new Vector3[]{
            new(minX,0,minZ), new(maxX,0,minZ), new(maxX,0,maxZ), new(minX,0,maxZ),
            new(minX,h,minZ), new(maxX,h,minZ), new(maxX,h,maxZ), new(minX,h,maxZ)
        };
        var t = new[]{ 0,4,1,1,4,5, 1,5,2,2,5,6, 2,6,3,3,6,7, 3,7,0,0,7,4,
                       4,7,5,5,7,6, 0,1,3,1,2,3 };
        var uv = new[]{
            new Vector2(0,0),new Vector2(1,0),new Vector2(1,0),new Vector2(0,0),
            new Vector2(0,1),new Vector2(1,1),new Vector2(1,1),new Vector2(0,1)
        };
        var m = new Mesh{name="Caja_L2"};
        m.SetVertices(v); m.SetTriangles(t,0); m.SetUVs(0,uv);
        m.RecalculateNormals(); return m;
    }

    // Collider sólido (paredes sin cornisa)
    private static Mesh GenerarMeshColision(List<Vector2> poly, float h)
    {
        int n = poly.Count;
        var verts = new List<Vector3>(); var tris = new List<int>();
        for (int i=0;i<n;i++)
        {
            int j=(i+1)%n, v0=verts.Count;
            verts.Add(new Vector3(poly[i].x,0,poly[i].y)); verts.Add(new Vector3(poly[j].x,0,poly[j].y));
            verts.Add(new Vector3(poly[j].x,h,poly[j].y)); verts.Add(new Vector3(poly[i].x,h,poly[i].y));
            tris.AddRange(new[]{v0,v0+3,v0+1, v0+1,v0+3,v0+2, v0+1,v0+2,v0, v0+2,v0+3,v0});
        }
        Vector2 cen2 = poly.Aggregate(Vector2.zero,(a,p)=>a+p)/n;
        int ci=verts.Count; verts.Add(new Vector3(cen2.x,h,cen2.y));
        int pb=ci+1; foreach(var p in poly) verts.Add(new Vector3(p.x,h,p.y));
        for(int i=0;i<n;i++) tris.AddRange(new[]{ci,pb+(i+1)%n,pb+i, ci,pb+i,pb+(i+1)%n});
        var m=new Mesh{name="Col"}; m.SetVertices(verts); m.SetTriangles(tris,0);
        m.RecalculateNormals(); return m;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  VENTANAS PROCEDURALES (solo LOD0)
    // ─────────────────────────────────────────────────────────────────────────

    private static void GenerarVentanas(GameObject parent, List<Vector2> poly, float h, TipoEdificio tipo)
    {
        if (tipo == TipoEdificio.Industrial || tipo == TipoEdificio.Garaje) return;

        int nPlantasMax = Mathf.FloorToInt((h - 0.5f) / ALTURA_PLANTA);
        int n = poly.Count;
        int totalVentanas = 0;

        var allVerts = new List<Vector3>(); var allTris = new List<int>(); var allUVs = new List<Vector2>();

        for (int i = 0; i < n && totalVentanas < 60; i++)
        {
            int j = (i + 1) % n;
            Vector2 a = poly[i], b = poly[j];
            float L = Vector2.Distance(a, b);
            if (L < 2.5f) continue;  // pared demasiado corta para ventanas

            // Dirección perpendicular (normal exterior)
            Vector2 dir = (b - a).normalized;
            Vector2 perp = new Vector2(-dir.y, dir.x) * VENTANA_OFFSET_Z;

            int nVentH = Mathf.Max(1, Mathf.FloorToInt((L - 0.8f) / 2.5f));
            float espaciado = L / (nVentH + 1);

            for (int piso = 0; piso < nPlantasMax && totalVentanas < 60; piso++)
            {
                float yBase = piso * ALTURA_PLANTA + 0.8f;
                float yCima = yBase + VENTANA_ALTO;
                if (yCima > h - CORNIS_ALTO - 0.3f) break;

                for (int vx = 0; vx < nVentH && totalVentanas < 60; vx++)
                {
                    float t = (vx + 1f) / (nVentH + 1f);
                    Vector2 pos = Vector2.Lerp(a, b, t);

                    // Posición de la ventana ligeramente fuera de la pared
                    float wx = pos.x + perp.x - dir.x * VENTANA_ANCHO / 2f;
                    float wz = pos.y + perp.y - dir.y * VENTANA_ANCHO / 2f;

                    int v0 = allVerts.Count;
                    allVerts.Add(new Vector3(wx,              yBase, wz));
                    allVerts.Add(new Vector3(wx + dir.x*VENTANA_ANCHO, yBase, wz + dir.y*VENTANA_ANCHO));
                    allVerts.Add(new Vector3(wx + dir.x*VENTANA_ANCHO, yCima, wz + dir.y*VENTANA_ANCHO));
                    allVerts.Add(new Vector3(wx,              yCima, wz));

                    allUVs.Add(new Vector2(0,0)); allUVs.Add(new Vector2(1,0));
                    allUVs.Add(new Vector2(1,1)); allUVs.Add(new Vector2(0,1));
                    allTris.AddRange(new[]{ v0,v0+3,v0+1, v0+1,v0+3,v0+2 });
                    totalVentanas++;
                }
            }
        }

        if (allVerts.Count == 0) return;

        var goV = new GameObject("Ventanas"); goV.transform.SetParent(parent.transform, false);
        AnadirMeshRenderer(goV, BuildMesh("Ventanas", allVerts, allTris, allUVs), _matVentana);
        goV.isStatic = true;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  MATERIALES
    // ─────────────────────────────────────────────────────────────────────────

    private enum TipoEdificio { Residencial, Apartamentos, Industrial, Iglesia, Detached, Garaje, Publico, Generico }

    private static TipoEdificio DetectarTipo(DatosEdificio e)
    {
        string t = e.type.ToLower(), nm = e.name.ToLower();
        if (nm.Contains("eliza") || nm.Contains("iglesia") || nm.Contains("chapel") ||
            t == "chapel" || t == "church") return TipoEdificio.Iglesia;
        if (t == "house" || t == "terrace")   return TipoEdificio.Residencial;
        if (t == "detached")                  return TipoEdificio.Detached;
        if (t == "apartments")                return TipoEdificio.Apartamentos;
        if (t == "industrial" || t == "warehouse" || t == "farm_auxiliary")
                                              return TipoEdificio.Industrial;
        if (t == "garage")                    return TipoEdificio.Garaje;
        if (t == "school" || t == "college" || t == "public")
                                              return TipoEdificio.Publico;
        return TipoEdificio.Generico;
    }

    private static Material MatPared(TipoEdificio t)
    {
        return t switch {
            TipoEdificio.Iglesia    => _matIglesia,
            TipoEdificio.Industrial => _matIndustrial,
            TipoEdificio.Garaje     => _matIndustrial,
            TipoEdificio.Residencial=> _matLadrillo,
            TipoEdificio.Detached   => _matLadrillo,
            _                       => _matPiedra,
        };
    }

    private static void CrearMateriales()
    {
        var shader = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");

        // Cargar texturas del proyecto
        var texBrick  = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/wall_bricks_old_1024.png")
                     ?? AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_ExtractedAssets/Textures/wall_bricks_old_1024.png");
        var texStone  = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/wall_cobblestone_1024px.png")
                     ?? AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_ExtractedAssets/Textures/wall_cobblestone_1024px.png");
        var texCracks = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/wall_cobblestone_cracks_1024px.png");

        _matLadrillo  = MakeMat(shader, "Mat_Ladrillo",  texBrick,  new Color(0.75f, 0.60f, 0.48f), 0.80f, texCracks);
        _matPiedra    = MakeMat(shader, "Mat_Piedra",    texStone,  new Color(0.68f, 0.65f, 0.60f), 0.85f, texCracks);
        _matIndustrial= MakeMat(shader, "Mat_Industrial",null,       new Color(0.60f, 0.58f, 0.54f), 0.90f, null);
        _matIglesia   = MakeMat(shader, "Mat_Iglesia",  texStone,  new Color(0.82f, 0.80f, 0.75f), 0.88f, texCracks);

        // Tejado: teja árabe terracota
        _matTejado = MakeMat(shader, "Mat_Tejado", null, new Color(0.55f, 0.28f, 0.16f), 0.75f, null);

        // Ventana: cristal oscuro con ligera emisión
        _matVentana = new Material(shader) { name = "Mat_Ventana" };
        SetColor(_matVentana, "_BaseColor", new Color(0.10f, 0.14f, 0.20f, 0.85f));
        SetFloat(_matVentana, "_Smoothness", 0.92f);
        SetFloat(_matVentana, "_Metallic",   0.0f);
        // Ligera emisión para que las ventanas sean visibles en modo oscuro
        SetColor(_matVentana, "_EmissiveColor", new Color(0.03f, 0.05f, 0.08f));
        if (_matVentana.HasProperty("_SurfaceType")) _matVentana.SetFloat("_SurfaceType", 1f); // transparente
    }

    private static Material MakeMat(Shader sh, string name, Texture2D tex,
                                     Color col, float rough, Texture2D normalTex)
    {
        var m = new Material(sh) { name = name };
        if (tex != null)
        {
            if (m.HasProperty("_BaseColorMap")) m.SetTexture("_BaseColorMap", tex);
            else m.mainTexture = tex;
        }
        SetColor(m, "_BaseColor", col);
        SetFloat(m, "_Smoothness", 1f - rough);
        SetFloat(m, "_Metallic",   0f);
        if (normalTex != null && m.HasProperty("_NormalMap"))
        {
            m.SetTexture("_NormalMap", normalTex);
            m.SetFloat("_NormalScale", 0.6f);
        }
        return m;
    }

    private static void SetColor(Material m, string p, Color c) { if (m.HasProperty(p)) m.SetColor(p, c); else m.color = c; }
    private static void SetFloat(Material m, string p, float v) { if (m.HasProperty(p)) m.SetFloat(p, v); }

    // ─────────────────────────────────────────────────────────────────────────
    //  PARSEO JSON
    // ─────────────────────────────────────────────────────────────────────────

    private class DatosEdificio
    {
        public float cx, cz, cy, height;
        public List<Vector2> poly = new();
        public string name = "", type = "yes";
        public long osmId;
    }

    private static List<DatosEdificio> ParsearEdificios(string json)
    {
        var lista = new List<DatosEdificio>();
        int pos = 0;
        while (pos < json.Length)
        {
            int ini = json.IndexOf('{', pos); if (ini < 0) break;
            int depth = 0, fin = ini;
            for (int k = ini; k < json.Length; k++)
            {
                if (json[k]=='{' || json[k]=='[') depth++;
                else if (json[k]=='}'||json[k]==']') depth--;
                if (depth == 0) { fin = k; break; }
            }
            string obj = json.Substring(ini, fin - ini + 1);
            var e = new DatosEdificio
            {
                cx = LeerF(obj,"x"), cz = LeerF(obj,"z"), cy = LeerF(obj,"y"),
                height = Mathf.Max(3f, LeerF(obj,"height")),
                name   = LeerS(obj,"name"), type = LeerS(obj,"type"),
                osmId  = (long)LeerF(obj,"osm_id")
            };
            if (string.IsNullOrEmpty(e.type)) e.type = "yes";
            // Parsear poly
            int pi = obj.IndexOf("\"poly\":["); if (pi >= 0)
            {
                pi = obj.IndexOf('[', pi); int pf = obj.IndexOf(']', pi);
                var nums = obj.Substring(pi+1, pf-pi-1).Split(',');
                for (int k=0; k+1<nums.Length; k+=2)
                    if (float.TryParse(nums[k].Trim(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float px) &&
                        float.TryParse(nums[k+1].Trim(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float pz))
                        e.poly.Add(new Vector2(px, pz));
            }
            if (e.poly.Count >= 3) lista.Add(e);
            pos = fin + 1;
        }
        return lista;
    }

    private static float LeerF(string json, string key)
    {
        int ki = json.IndexOf($"\"{key}\":"); if (ki<0) return 0f;
        ki += key.Length+3; int fin=ki;
        while (fin<json.Length && (char.IsDigit(json[fin])||json[fin]=='.'||json[fin]=='-')) fin++;
        return float.TryParse(json.Substring(ki,fin-ki).Trim(),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : 0f;
    }

    private static string LeerS(string json, string key)
    {
        int ki = json.IndexOf($"\"{key}\":\""); if (ki<0) return "";
        ki += key.Length+4; int fin = json.IndexOf('"', ki);
        return fin < 0 ? "" : json.Substring(ki, fin-ki);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  HELPERS
    // ─────────────────────────────────────────────────────────────────────────

    private static void AnadirMeshRenderer(GameObject go, Mesh mesh, Material mat)
    {
        var mf = go.AddComponent<MeshFilter>(); mf.sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.On;
        mr.receiveShadows    = true;
        go.isStatic = true;
    }

    private static Mesh BuildMesh(string name, List<Vector3> v, List<int> t, List<Vector2> uv)
    {
        var m = new Mesh { name = name };
        m.SetVertices(v); m.SetTriangles(t, 0); m.SetUVs(0, uv);
        m.RecalculateNormals(); m.RecalculateBounds(); m.Optimize();
        return m;
    }

    private static float ObtenerY(float x, float z, float yJson)
    {
        if (Terrain.activeTerrain != null)
            return Terrain.activeTerrain.SampleHeight(new Vector3(x, 0, z));
        return yJson > 10f ? yJson : 240f;
    }

    private static int LimpiarHijos(GameObject zo)
    {
        var h = new List<GameObject>();
        for (int i = 0; i < zo.transform.childCount; i++)
        {
            var c = zo.transform.GetChild(i).gameObject;
            if (c.name != "_Marcador" && c.name != "_Label") h.Add(c);
        }
        foreach (var g in h) Undo.DestroyObjectImmediate(g);
        return h.Count;
    }
}
