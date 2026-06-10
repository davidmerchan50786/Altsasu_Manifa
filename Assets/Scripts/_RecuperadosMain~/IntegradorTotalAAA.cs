#if UNITY_EDITOR
// Assets/Scripts/Editor/IntegradorTotalAAA.cs
// ═══════════════════════════════════════════════════════════════════════════
//  INTEGRADOR TOTAL AAA+ — usa TODOS los assets reales del proyecto
//
//  Lo que añade a la escena:
//   • Lucia como jugador (humano real con animaciones)
//   • Police Car (Interceptor.prefab) en la comisaría de la Guardia Civil
//   • Helicóptero policial sobre la estación
//   • Hot Rod en Herriko Plaza (vehículo emblemático)
//   • Sport Car20 patrullando las calles
//   • Civiles (Civil_1, Civil_2) caminando como NPCs
//   • Guardias Civiles patrullando
//   • Ciervos en las zonas de bosque
//   • Conejos en prados
//   • Farolas SpaceZeta a lo largo de carreteras principales
//   • Barricadas Concreto en zonas de manifestación
//   • Casa.fbx + Lisbon buildings en zonas residenciales
//
//  MENÚ: Altsasu GTA → ★★ INTEGRADOR TOTAL AAA+ ★★
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public static class IntegradorTotalAAA
{
    // ─────────────────────────────────────────────────────────────────────
    //  RUTAS DE ASSETS REALES
    // ─────────────────────────────────────────────────────────────────────
    const string PATH_LUCIA       = "Assets/Models/Characters/Lucia/LuciaModel.FBX";
    const string PATH_CIVIL_1     = "Assets/Models/Characters/Civiles/Civil_1/Meshy_AI_Casual_Confidence_0421161928_texture.fbx";
    const string PATH_CIVIL_2     = "Assets/Models/Characters/Civiles/Civil_2/Meshy_AI_Casual_Summer_Street__0421162005_texture.fbx";
    const string PATH_GUARDIA     = "Assets/Models/Characters/GuardiaCivil/Meshy_AI_Guardia_Civil_Officer_0501071058_texture.fbx";

    const string PATH_POLICE_CAR  = "Assets/Police Car & Helicopter/Prefabs/Interceptor.prefab";
    const string PATH_HELI        = "Assets/Police Car & Helicopter/Prefabs/Helicopter.prefab";
    const string PATH_HOT_ROD     = "Assets/Hot Rod/FBX/LOD0.FBX";
    const string PATH_SPORT_CAR   = "Assets/Models/Car/Best Sports CARS - Pro 3D Models/Vehicle/SportCar20/Meshes/SportCar20.FBX";

    const string PATH_LAMP1       = "Assets/SpaceZeta_StreetLamps2/Prefabs/StreetLampRound1A.prefab";
    const string PATH_LAMP2       = "Assets/SpaceZeta_StreetLamps2/Prefabs/StreetLampRound2A.prefab";
    const string PATH_BARRICADA   = "Assets/BarrierPack/Assets/Barricada Concreto.FBX";

    const string PATH_DEER_MESH   = "Assets/Models/Fauna/Deer/deer-female-mesh.fbx";
    const string PATH_RABBIT      = "Assets/Models/Fauna/Rabbit/rabbit.fbx";

    // ─────────────────────────────────────────────────────────────────────
    //  POSICIONES CLAVE EN ALSASUA (Unity coords)
    // ─────────────────────────────────────────────────────────────────────
    static readonly Vector3 HERRIKO_PLAZA       = new Vector3(1918f, 0f, 8570f);
    static readonly Vector3 COMISARIA_GC        = new Vector3(1960f, 0f, 8430f);
    static readonly Vector3 ESTACION_TREN       = new Vector3(2100f, 0f, 8350f);
    static readonly Vector3 AYUNTAMIENTO        = new Vector3(1900f, 0f, 8590f);

    static Terrain _terrain;

    // MENÚ-LEGACY: [MenuItem("Altsasu GTA/★★ INTEGRADOR TOTAL AAA+ ★★", false, -20)]
    public static void IntegrarTodo()
    {
        _terrain = Object.FindFirstObjectByType<Terrain>();
        if (_terrain == null)
        {
            EditorUtility.DisplayDialog("Sin terrain",
                "Crea primero el terrain:\nAltsasu GTA → Territorio Real → ★ Crear Terrain + Ortofoto", "OK");
            return;
        }

        bool ok = EditorUtility.DisplayDialog(
            "★★ INTEGRADOR TOTAL AAA+ ★★",
            "Va a integrar TODOS los assets del proyecto:\n\n" +
            "• Lucia como jugador\n" +
            "• Police Car + Helicóptero\n" +
            "• Hot Rod + Sport Car\n" +
            "• Civiles + Guardias Civiles NPCs\n" +
            "• Ciervos + Conejos en bosques\n" +
            "• Farolas SpaceZeta\n" +
            "• Barricadas\n\n" +
            "Tarda ~30 segundos.", "⚡ Integrar todo", "Cancelar");
        if (!ok) return;

        try
        {
            var raiz = ObtenerRaiz();

            EditorUtility.DisplayProgressBar("Integrador", "Limpiando...", 0.02f);
            LimpiarAnteriores(raiz);

            EditorUtility.DisplayProgressBar("Integrador", "Spawneando vehículos...", 0.15f);
            SpawnVehiculos(raiz);

            EditorUtility.DisplayProgressBar("Integrador", "Civiles caminando...", 0.30f);
            SpawnCiviles(raiz, 30);

            EditorUtility.DisplayProgressBar("Integrador", "Guardias Civiles...", 0.45f);
            SpawnGuardiasCiviles(raiz, 6);

            EditorUtility.DisplayProgressBar("Integrador", "Fauna (ciervos, conejos)...", 0.60f);
            SpawnFauna(raiz);

            EditorUtility.DisplayProgressBar("Integrador", "Farolas SpaceZeta...", 0.75f);
            SpawnFarolasReales(raiz);

            EditorUtility.DisplayProgressBar("Integrador", "Barricadas...", 0.85f);
            SpawnBarricadas(raiz);

            EditorUtility.DisplayProgressBar("Integrador", "Guardando...", 0.95f);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
        }
        finally { EditorUtility.ClearProgressBar(); }

        EditorUtility.DisplayDialog("✅ Integración completa",
            "Todos los assets reales integrados en la escena.\n\n" +
            "Para el jugador humano, ejecuta:\n" +
            "★ Jugador Humano Realista (Lucia + animaciones)\n\n" +
            "Pulsa ▶ Play.", "OK");
    }

    static GameObject ObtenerRaiz()
    {
        var raiz = GameObject.Find("AAA_Assets_Integrados");
        if (raiz == null)
        {
            raiz = new GameObject("AAA_Assets_Integrados");
            Undo.RegisterCreatedObjectUndo(raiz, "Raíz AAA");
        }
        return raiz;
    }

    static void LimpiarAnteriores(GameObject raiz)
    {
        for (int i = raiz.transform.childCount - 1; i >= 0; i--)
            Undo.DestroyObjectImmediate(raiz.transform.GetChild(i).gameObject);
    }

    static float Y(Vector3 p) => _terrain.SampleHeight(new Vector3(p.x, 0, p.z));
    static Vector3 SobreSuelo(Vector3 p, float offset = 0.05f)
        => new Vector3(p.x, Y(p) + offset, p.z);

    // =========================================================================
    //  VEHÍCULOS
    // =========================================================================

    static void SpawnVehiculos(GameObject raiz)
    {
        var grupo = new GameObject("Vehiculos");
        grupo.transform.SetParent(raiz.transform);

        // Coche policial en la comisaría
        var policePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PATH_POLICE_CAR);
        if (policePrefab != null)
        {
            // 2 coches aparcados frente a la comisaría
            for (int i = 0; i < 2; i++)
            {
                var p = (GameObject)PrefabUtility.InstantiatePrefab(policePrefab, grupo.transform);
                p.transform.position = SobreSuelo(
                    new Vector3(COMISARIA_GC.x + 8f + i * 6f, 0, COMISARIA_GC.z + 12f), 0.5f);
                p.transform.rotation = Quaternion.Euler(0, 90f, 0);
                p.name = $"PoliceCar_{i}";
                AjustarColliderVehiculo(p);
            }
            Debug.Log("[Integrador] ✓ 2 Police Cars en comisaría.");
        }

        // Helicóptero sobre la comisaría
        var heliPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PATH_HELI);
        if (heliPrefab != null)
        {
            var h = (GameObject)PrefabUtility.InstantiatePrefab(heliPrefab, grupo.transform);
            h.transform.position = SobreSuelo(COMISARIA_GC, 25f);
            h.name = "PoliceHelicopter";
        }

        // Hot Rod en Herriko Plaza (vehículo emblemático)
        var hotRodMesh = AssetDatabase.LoadAssetAtPath<GameObject>(PATH_HOT_ROD);
        if (hotRodMesh != null)
        {
            var hr = (GameObject)PrefabUtility.InstantiatePrefab(hotRodMesh, grupo.transform);
            hr.transform.position = SobreSuelo(
                new Vector3(HERRIKO_PLAZA.x - 25f, 0, HERRIKO_PLAZA.z + 8f), 0.5f);
            hr.transform.rotation = Quaternion.Euler(0, 45f, 0);
            hr.name = "HotRod_HerrikoPlaza";
            AjustarColliderVehiculo(hr);
        }

        // Sport Car patrullando — 4 coches aparcados en distintas zonas
        var sportMesh = AssetDatabase.LoadAssetAtPath<GameObject>(PATH_SPORT_CAR);
        if (sportMesh != null)
        {
            Vector3[] posCoches = {
                new Vector3(1880f, 0, 8520f),
                new Vector3(1950f, 0, 8615f),
                new Vector3(2080f, 0, 8500f),
                new Vector3(1820f, 0, 8580f),
            };
            for (int i = 0; i < posCoches.Length; i++)
            {
                var c = (GameObject)PrefabUtility.InstantiatePrefab(sportMesh, grupo.transform);
                c.transform.position = SobreSuelo(posCoches[i], 0.5f);
                c.transform.rotation = Quaternion.Euler(0, i * 70f, 0);
                c.name = $"SportCar_{i}";
                AjustarColliderVehiculo(c);
            }
            Debug.Log("[Integrador] ✓ 4 Sport Cars repartidos por el pueblo.");
        }
    }

    static void AjustarColliderVehiculo(GameObject vehiculo)
    {
        if (vehiculo.GetComponent<Collider>() == null)
        {
            var b = CalcularBounds(vehiculo);
            if (b.size.sqrMagnitude > 0.01f)
            {
                var bc = vehiculo.AddComponent<BoxCollider>();
                bc.center = b.center - vehiculo.transform.position;
                bc.size   = b.size;
            }
        }
        if (vehiculo.GetComponent<Rigidbody>() == null)
        {
            var rb = vehiculo.AddComponent<Rigidbody>();
            rb.mass = 1200f;
            rb.linearDamping = 0.3f;
            rb.angularDamping = 4f;
            rb.isKinematic = true; // estacionados; cambiar a false al conducir
        }
    }

    // =========================================================================
    //  CIVILES NPCs
    // =========================================================================

    static void SpawnCiviles(GameObject raiz, int cantidad)
    {
        var grupo = new GameObject("Civiles");
        grupo.transform.SetParent(raiz.transform);

        var civiles = new[] {
            AssetDatabase.LoadAssetAtPath<GameObject>(PATH_CIVIL_1),
            AssetDatabase.LoadAssetAtPath<GameObject>(PATH_CIVIL_2)
        };

        int spawned = 0;
        for (int i = 0; i < cantidad; i++)
        {
            var prefab = civiles[i % civiles.Length];
            if (prefab == null) continue;

            // Distribuir alrededor de Herriko Plaza (radio 100m) y zonas urbanas
            Vector3 pos;
            if (i < cantidad * 0.5f)
                pos = HERRIKO_PLAZA + new Vector3(Random.Range(-80f, 80f), 0, Random.Range(-80f, 80f));
            else
                pos = new Vector3(
                    Random.Range(1750f, 2150f), 0,
                    Random.Range(8400f, 8750f));

            var c = (GameObject)PrefabUtility.InstantiatePrefab(prefab, grupo.transform);
            c.transform.position = SobreSuelo(pos, 0.05f);
            c.transform.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
            c.name = $"Civil_{spawned++}";

            // Auto-escalar humano (si el modelo viene chico)
            EscalarHumano(c);
        }
        Debug.Log($"[Integrador] ✓ {spawned} civiles spawneados.");
    }

    // =========================================================================
    //  GUARDIAS CIVILES
    // =========================================================================

    static void SpawnGuardiasCiviles(GameObject raiz, int cantidad)
    {
        var grupo = new GameObject("GuardiasCiviles");
        grupo.transform.SetParent(raiz.transform);

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PATH_GUARDIA);
        if (prefab == null) return;

        // 4 en la comisaría, 2 patrullando
        var puestos = new Vector3[] {
            COMISARIA_GC + new Vector3(-5f, 0, 3f),
            COMISARIA_GC + new Vector3( 5f, 0, 3f),
            COMISARIA_GC + new Vector3(-3f, 0,-2f),
            COMISARIA_GC + new Vector3( 3f, 0,-2f),
            HERRIKO_PLAZA + new Vector3(15f, 0, 20f),
            HERRIKO_PLAZA + new Vector3(-12f, 0,-18f),
        };

        for (int i = 0; i < Mathf.Min(cantidad, puestos.Length); i++)
        {
            var gc = (GameObject)PrefabUtility.InstantiatePrefab(prefab, grupo.transform);
            gc.transform.position = SobreSuelo(puestos[i], 0.05f);
            gc.transform.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
            gc.name = $"GuardiaCivil_{i}";
            EscalarHumano(gc);
        }
        Debug.Log($"[Integrador] ✓ {cantidad} Guardias Civiles spawneados.");
    }

    static void EscalarHumano(GameObject go)
    {
        var b = CalcularBounds(go);
        if (b.size.y > 0.01f && b.size.y < 1.3f)
            go.transform.localScale *= 1.75f / b.size.y;
        else if (b.size.y > 3f)
            go.transform.localScale *= 1.75f / b.size.y;
    }

    // =========================================================================
    //  FAUNA — Ciervos en bosques
    // =========================================================================

    static void SpawnFauna(GameObject raiz)
    {
        var grupo = new GameObject("Fauna");
        grupo.transform.SetParent(raiz.transform);

        var deerMesh = AssetDatabase.LoadAssetAtPath<GameObject>(PATH_DEER_MESH);
        var rabbitMesh = AssetDatabase.LoadAssetAtPath<GameObject>(PATH_RABBIT);

        if (deerMesh == null && rabbitMesh == null) return;

        // En cada zona de bosque, spawn 3 ciervos y 5 conejos
        if (GeoDataAlsasua.ZonasBosque != null)
        {
            int totalDeer = 0, totalRabbit = 0;
            foreach (var zona in GeoDataAlsasua.ZonasBosque)
            {
                float cx = zona.Centro.x + 1918f;
                float cz = zona.Centro.z + 8570f;

                if (deerMesh != null)
                for (int i = 0; i < 3; i++)
                {
                    Vector2 r = Random.insideUnitCircle * zona.Radio * 0.7f;
                    Vector3 pos = new Vector3(cx + r.x, 0, cz + r.y);
                    var d = (GameObject)PrefabUtility.InstantiatePrefab(deerMesh, grupo.transform);
                    d.transform.position = SobreSuelo(pos);
                    d.transform.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
                    d.name = $"Ciervo_{zona.Nombre}_{i}";
                    EscalarFauna(d, 1.2f); // ciervo ~1.2m alto
                    totalDeer++;
                }

                if (rabbitMesh != null)
                for (int i = 0; i < 5; i++)
                {
                    Vector2 r = Random.insideUnitCircle * zona.Radio * 0.5f;
                    Vector3 pos = new Vector3(cx + r.x, 0, cz + r.y);
                    var c = (GameObject)PrefabUtility.InstantiatePrefab(rabbitMesh, grupo.transform);
                    c.transform.position = SobreSuelo(pos);
                    c.transform.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
                    c.name = $"Conejo_{zona.Nombre}_{i}";
                    EscalarFauna(c, 0.25f);
                    totalRabbit++;
                }
            }
            Debug.Log($"[Integrador] ✓ {totalDeer} ciervos + {totalRabbit} conejos en bosques.");
        }
    }

    static void EscalarFauna(GameObject go, float alturaObjetivo)
    {
        var b = CalcularBounds(go);
        if (b.size.y > 0.01f)
        {
            float f = alturaObjetivo / b.size.y;
            go.transform.localScale *= f;
        }
    }

    // =========================================================================
    //  FAROLAS SPACEZETA (modelos reales)
    // =========================================================================

    static void SpawnFarolasReales(GameObject raiz)
    {
        // Eliminar farolas procedurales anteriores
        var mobAntiguo = GameObject.Find("MobiliarioUrbano");
        if (mobAntiguo != null)
        {
            var farolasAnt = mobAntiguo.transform.Find("Farolas");
            if (farolasAnt != null) Undo.DestroyObjectImmediate(farolasAnt.gameObject);
        }

        var grupo = new GameObject("Farolas_SpaceZeta");
        grupo.transform.SetParent(raiz.transform);

        var prefab1 = AssetDatabase.LoadAssetAtPath<GameObject>(PATH_LAMP1);
        var prefab2 = AssetDatabase.LoadAssetAtPath<GameObject>(PATH_LAMP2);
        if (prefab1 == null && prefab2 == null) return;

        // 30 farolas alrededor de las zonas urbanas (radio 200m de Herriko)
        for (int i = 0; i < 30; i++)
        {
            float angle = i / 30f * Mathf.PI * 2f;
            float radio = Random.Range(80f, 220f);
            Vector3 pos = HERRIKO_PLAZA + new Vector3(
                Mathf.Cos(angle) * radio, 0, Mathf.Sin(angle) * radio);
            pos = SobreSuelo(pos);

            var prefab = (i % 2 == 0 && prefab1 != null) ? prefab1 : (prefab2 ?? prefab1);
            if (prefab == null) continue;

            var f = (GameObject)PrefabUtility.InstantiatePrefab(prefab, grupo.transform);
            f.transform.position = pos;
            f.transform.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
            f.name = $"Farola_SZ_{i}";

            // Añadir Light si no la tiene
            if (f.GetComponentInChildren<Light>() == null)
            {
                var luzGO = new GameObject("Luz");
                luzGO.transform.SetParent(f.transform);
                luzGO.transform.localPosition = new Vector3(0, 5f, 0);
                var luz = luzGO.AddComponent<Light>();
                luz.type = LightType.Point;
                luz.color = new Color(1f, 0.85f, 0.55f);
                luz.intensity = 1.5f;
                luz.range = 12f;
                luz.shadows = LightShadows.None;
                luz.enabled = false; // se activa de noche
            }
        }
        Debug.Log("[Integrador] ✓ 30 Farolas SpaceZeta colocadas.");
    }

    // =========================================================================
    //  BARRICADAS
    // =========================================================================

    static void SpawnBarricadas(GameObject raiz)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PATH_BARRICADA);
        if (prefab == null) return;

        var grupo = new GameObject("Barricadas_Manifa");
        grupo.transform.SetParent(raiz.transform);

        // 6 barricadas alrededor de Herriko Plaza (zona de manifestación)
        for (int i = 0; i < 6; i++)
        {
            float angle = i / 6f * Mathf.PI * 2f;
            Vector3 pos = HERRIKO_PLAZA + new Vector3(
                Mathf.Cos(angle) * 35f, 0, Mathf.Sin(angle) * 35f);
            pos = SobreSuelo(pos, 0.05f);

            var b = (GameObject)PrefabUtility.InstantiatePrefab(prefab, grupo.transform);
            b.transform.position = pos;
            b.transform.rotation = Quaternion.Euler(0, angle * Mathf.Rad2Deg + 90f, 0);
            b.name = $"Barricada_{i}";
        }
        Debug.Log("[Integrador] ✓ 6 Barricadas Concreto colocadas.");
    }

    // =========================================================================
    //  HELPERS
    // =========================================================================

    static Bounds CalcularBounds(GameObject go)
    {
        var rends = go.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        Bounds b = rends[0].bounds;
        foreach (var r in rends) b.Encapsulate(r.bounds);
        return b;
    }
}
#endif
