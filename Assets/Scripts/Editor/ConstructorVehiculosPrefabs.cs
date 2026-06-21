// Assets/Scripts/Editor/ConstructorVehiculosPrefabs.cs
// Tools → Alsasua → 🚗 Construir Prefabs Vehículos
//
// Construye prefabs conducibles desde los FBX extraídos:
//   Trabant_Jugador, Ambulancia_Militar, Camion_Apisonadora,
//   Tractor_Rojo, Coche_Abandonado (prop estático)
// Cada prefab jugable: Rigidbody + WheelColliders + ControladorVehiculoJugador

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Reflection;
using System.Linq;

public static class ConstructorVehiculosPrefabs
{
    const string DIR = "Assets/Prefabs/Coches";

    struct CfgVehiculo
    {
        public string fbxPath, prefabNombre;
        public float masa, parMax, parFreno, parFrenoMano, velMax;
        public float semiLargo, semiAncho, alturaRueda, radioRueda;
        public bool conducible;
    }

    static readonly CfgVehiculo[] VEHICULOS =
    {
        new CfgVehiculo {
            fbxPath      = "Assets/_ExtractedAssets/Vehicles/Trabant_601/Trabant_601.fbx",
            prefabNombre = "Trabant_Jugador",
            masa=900f, parMax=140f, parFreno=600f, parFrenoMano=1200f, velMax=22f,
            semiLargo=1.85f, semiAncho=0.70f, alturaRueda=0.28f, radioRueda=0.28f,
            conducible=true
        },
        new CfgVehiculo {
            fbxPath      = "Assets/_ExtractedAssets/Vehicles/Ambulancia_Militar/ambul01a.fbx",
            prefabNombre = "Ambulancia_Militar",
            masa=2800f, parMax=420f, parFreno=1400f, parFrenoMano=2000f, velMax=26f,
            semiLargo=2.80f, semiAncho=0.95f, alturaRueda=0.42f, radioRueda=0.42f,
            conducible=true
        },
        new CfgVehiculo {
            fbxPath      = "Assets/_ExtractedAssets/Vehicles/Tractor_Rojo/Tractor_Rojo.fbx",
            prefabNombre = "Tractor_Rojo",
            masa=3500f, parMax=800f, parFreno=2000f, parFrenoMano=3000f, velMax=12f,
            semiLargo=2.20f, semiAncho=1.00f, alturaRueda=0.55f, radioRueda=0.55f,
            conducible=true
        },
        new CfgVehiculo {
            fbxPath      = "Assets/_ExtractedAssets/Vehicles/Camion_Apisonadora/Truck 2.fbx",
            prefabNombre = "Camion_Apisonadora",
            masa=6000f, parMax=1200f, parFreno=3000f, parFrenoMano=4000f, velMax=10f,
            semiLargo=3.50f, semiAncho=1.20f, alturaRueda=0.60f, radioRueda=0.60f,
            conducible=true
        },
        new CfgVehiculo {
            fbxPath      = "Assets/_ExtractedAssets/Vehicles/Coche_Abandonado/SM_JUNKCAR1_DEFORMED2.fbx",
            prefabNombre = "Coche_Abandonado_Prop",
            conducible=false,
            masa=900f, parMax=0, parFreno=0, parFrenoMano=0, velMax=0,
            semiLargo=2f, semiAncho=0.8f, alturaRueda=0.3f, radioRueda=0.3f
        },
    };

    [MenuItem("Tools/Alsasua/Assets/🚗 Prefabs Vehículos", priority = 60)]
    public static void Construir()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
            AssetDatabase.CreateFolder("Assets", "Prefabs");
        if (!AssetDatabase.IsValidFolder(DIR))
            AssetDatabase.CreateFolder("Assets/Prefabs", "Coches");

        int creados = 0;
        foreach (var cfg in VEHICULOS)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(cfg.fbxPath);
            if (src == null) { Debug.LogWarning($"[Coches] No encontrado: {cfg.fbxPath}"); continue; }

            var go = Object.Instantiate(src);
            go.name = cfg.prefabNombre;

            // Escalar si necesario (muchos FBX tienen escala incorrecta)
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                var bounds = renderers[0].bounds;
                foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                float lonActual = bounds.size.z;
                if (lonActual > 0.1f && (lonActual < 1f || lonActual > 12f))
                {
                    float escala = (cfg.semiLargo * 2f) / lonActual;
                    go.transform.localScale = Vector3.one * escala;
                }
            }

            // MeshCollider en el cuerpo
            var meshes = go.GetComponentsInChildren<MeshFilter>();
            if (meshes.Length > 0)
            {
                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = meshes[0].sharedMesh;
                mc.convex     = true;
            }
            else
            {
                var bc = go.AddComponent<BoxCollider>();
                bc.size   = new Vector3(cfg.semiAncho*2f, cfg.alturaRueda*2f, cfg.semiLargo*2f);
                bc.center = new Vector3(0, cfg.alturaRueda, 0);
            }

            if (cfg.conducible)
            {
                // Rigidbody
                var rb      = go.AddComponent<Rigidbody>();
                rb.mass     = cfg.masa;
                rb.linearDamping     = 0.05f;
                rb.angularDamping = 0.2f;
                rb.centerOfMass = new Vector3(0, -cfg.alturaRueda * 0.4f, 0);

                // 4 WheelColliders
                string[] nombres = { "WC_DI","WC_DD","WC_TI","WC_TD" };
                var posiciones   = new Vector3[]
                {
                    new Vector3(-cfg.semiAncho,  cfg.alturaRueda,  cfg.semiLargo),
                    new Vector3( cfg.semiAncho,  cfg.alturaRueda,  cfg.semiLargo),
                    new Vector3(-cfg.semiAncho,  cfg.alturaRueda, -cfg.semiLargo),
                    new Vector3( cfg.semiAncho,  cfg.alturaRueda, -cfg.semiLargo),
                };

                var wheelGOs = new GameObject[4];
                for (int i = 0; i < 4; i++)
                {
                    wheelGOs[i] = new GameObject(nombres[i]);
                    wheelGOs[i].transform.SetParent(go.transform, false);
                    wheelGOs[i].transform.localPosition = posiciones[i];
                    var wc = wheelGOs[i].AddComponent<WheelCollider>();
                    wc.radius            = cfg.radioRueda;
                    wc.suspensionDistance= 0.2f;
                    var spring  = wc.suspensionSpring;
                    spring.spring   = 35000f;
                    spring.damper   = 4500f;
                    spring.targetPosition = 0.5f;
                    wc.suspensionSpring = spring;
                    var fric = wc.forwardFriction;
                    fric.stiffness = 1.2f; wc.forwardFriction = fric;
                    fric = wc.sidewaysFriction;
                    fric.stiffness = 1.5f; wc.sidewaysFriction = fric;
                }

                // ControladorVehiculoJugador
                var ctrl = go.AddComponent<ControladorVehiculoJugador>();
                SetField(ctrl, "rDI",  wheelGOs[0].GetComponent<WheelCollider>());
                SetField(ctrl, "rDD",  wheelGOs[1].GetComponent<WheelCollider>());
                SetField(ctrl, "rTI",  wheelGOs[2].GetComponent<WheelCollider>());
                SetField(ctrl, "rTD",  wheelGOs[3].GetComponent<WheelCollider>());
                SetField(ctrl, "parMaximo",      cfg.parMax);
                SetField(ctrl, "parFreno",       cfg.parFreno);
                SetField(ctrl, "parFrenoMano",   cfg.parFrenoMano);
                SetField(ctrl, "velocidadMax",   cfg.velMax);

                // AsientoConductor
                var asiento = new GameObject("AsientoConductor");
                asiento.transform.SetParent(go.transform, false);
                asiento.transform.localPosition = new Vector3(-0.38f, cfg.alturaRueda + 0.55f, 0.1f);
                SetField(ctrl, "asientoConductor", asiento.transform);

                // IndicadorEntradaVehiculo
                go.AddComponent<IndicadorEntradaVehiculo>();
            }

            string ruta = $"{DIR}/{cfg.prefabNombre}.prefab";
            PrefabUtility.SaveAsPrefabAsset(go, ruta);
            Object.DestroyImmediate(go);
            creados++;
            Debug.Log($"[Coches] ✅ {cfg.prefabNombre}.prefab creado");
        }

        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("🚗 Vehículos creados",
            $"{creados} prefabs listos en {DIR}/\n\n" +
            "• Trabant, Ambulancia Militar, Tractor, Camión\n" +
            "• Todos conducibles con física Pacejka\n" +
            "• Coche Abandonado como prop estático",
            "OK");
    }

    static void SetField(object t, string name, object val)
    {
        var f = t.GetType().GetField(name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        f?.SetValue(t, val);
    }
}
#endif
