// Assets/Scripts/IntegradorMatematicas.cs
// ═══════════════════════════════════════════════════════════════════════════
//  INTEGRADOR MATEMÁTICAS — conecta los 5 algoritmos con los sistemas
//
//  Expone métodos estáticos que los sistemas llaman en su inicialización:
//
//  SistemaSueloAAA.Start()       → UsarPoissonDisk + UsarFBMNoise
//  SistemaManifestacion          → UsarBoids
//  GeneradorCarreteras           → UsarCatmullRom
//  SpawnerNPCsEditor / GameMgr   → UsarDensidadGaussiana
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

public static class IntegradorMatematicas
{
    // ════════════════════════════════════════════════════════════════════════
    //  1. POISSON DISK — distribución de árboles y props
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Genera posiciones de árboles con distribución de Poisson Disk.
    /// Reemplaza el Random.Range en SistemaSueloAAA y AlsasuaTreeStreamer.
    /// </summary>
    public static List<Vector3> GenerarPosicionesArbolesPoisson(
        Vector3 centro, float radio, float separacionMin = 8f, int max = 500, uint semilla = 0)
    {
        if (semilla == 0) semilla = (uint)UnityEngine.Random.Range(1, int.MaxValue);

        // Para bosques de Urbasa/Aralar: separación 8m, densidad natural
        // Para zona urbana: separación 15m (árboles de calle)
        var puntos = PoissonDiskSampler.Generar(centro, radio, separacionMin, max,
                                                 altura: 0f, semilla: semilla);

        // Ajustar altura al terreno
        var terrain = Terrain.activeTerrain;
        if (terrain != null)
            for (int i = 0; i < puntos.Count; i++)
            {
                float y = terrain.SampleHeight(puntos[i]);
                puntos[i] = new Vector3(puntos[i].x, y, puntos[i].z);
            }

        AlsasuaLogger.Info("MatPoisson", $"Poisson Disk: {puntos.Count} árboles en R={radio}m");
        return puntos;
    }

    /// <summary>
    /// Genera posiciones de props urbanos (farolas, bancos, papeleras)
    /// ponderadas por densidad gaussiana de Alsasua.
    /// </summary>
    public static List<Vector3> GenerarPosicionesPropsUrbanos(
        Vector3 centro, float radio, int cantidad)
    {
        var resultado = new List<Vector3>(cantidad);
        var rng = new Unity.Mathematics.Random((uint)System.DateTime.Now.Millisecond + 1);

        for (int i = 0; i < cantidad * 3 && resultado.Count < cantidad; i++)
        {
            var pos = DensidadAlsasua.SamplearPuntoConDensidad(centro, radio, ref rng);
            resultado.Add(pos);
        }

        AlsasuaLogger.Info("MatDensidad", $"Props urbanos: {resultado.Count} en R={radio}m");
        return resultado;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  2. FBM NOISE — pesos de capa de terreno
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Genera un mapa de pesos para las capas del Terrain de Unity usando fBm.
    /// Sustituye el Mathf.PerlinNoise de una sola frecuencia en SistemaSueloAAA.
    ///
    /// Devuelve array de pesos [hierba, roca, tierra, asfalto] por cada celda.
    /// </summary>
    public static float[,,] GenerarAlphamapFBM(
        Terrain terrain,
        int capas,
        float frecuenciaBase = 0.002f,
        int octavas = 6,
        float persistencia = 0.45f,
        float lacunaridad = 2.1f)
    {
        if (terrain == null || terrain.terrainData == null) return null;

        var td    = terrain.terrainData;
        int res   = td.alphamapResolution;
        var alpha = new float[res, res, capas];

        // Generar valores fBm en Burst
        var coords = new NativeArray<float2>(res * res, Allocator.TempJob);
        for (int z = 0; z < res; z++)
        for (int x = 0; x < res; x++)
        {
            float wx = terrain.transform.position.x + (float)x / res * td.size.x;
            float wz = terrain.transform.position.z + (float)z / res * td.size.z;
            coords[z * res + x] = new float2(wx, wz);
        }

        var noiseHierba = new NativeArray<float>(res * res, Allocator.TempJob);
        var noiseRoca   = new NativeArray<float>(res * res, Allocator.TempJob);
        var noiseTierra = new NativeArray<float>(res * res, Allocator.TempJob);

        // 3 capas de noise con offsets distintos para variedad
        var j1 = new JobFBMTerrain { coordenadas=coords, octavas=octavas,
            frecuenciaBase=frecuenciaBase, lacunaridad=lacunaridad, persistencia=persistencia,
            offset=new float2(0, 0), resultado=noiseHierba };
        var j2 = new JobFBMTerrain { coordenadas=coords, octavas=octavas,
            frecuenciaBase=frecuenciaBase*1.7f, lacunaridad=lacunaridad, persistencia=persistencia,
            offset=new float2(317.4f, 891.2f), resultado=noiseRoca };
        var j3 = new JobFBMTerrain { coordenadas=coords, octavas=octavas,
            frecuenciaBase=frecuenciaBase*0.8f, lacunaridad=lacunaridad, persistencia=persistencia,
            offset=new float2(-456.8f, 123.1f), resultado=noiseTierra };

        var dep1 = j1.Schedule(res*res, 64);
        var dep2 = j2.Schedule(res*res, 64);
        var dep3 = j3.Schedule(res*res, 64, dep1);

        JobHandle.CombineDependencies(dep1, dep2, dep3).Complete();

        // Mezclar capas usando los valores noise
        for (int z = 0; z < res; z++)
        for (int x = 0; x < res; x++)
        {
            int   idx     = z * res + x;
            float nH = (noiseHierba[idx] + 1f) * 0.5f; // [0,1]
            float nR = (noiseRoca  [idx] + 1f) * 0.5f;
            float nT = (noiseTierra[idx] + 1f) * 0.5f;

            // Peso de roca según pendiente + noise
            float pendiente = td.GetSteepness((float)x/res, (float)z/res) / 90f;
            float wRoca     = math.saturate(pendiente * 1.5f + nR * 0.3f);
            float wHierba   = math.saturate(nH * (1f - wRoca));
            float wTierra   = math.saturate(nT * 0.4f * (1f - wRoca));
            float wHierba2  = math.saturate((1f - nH) * 0.3f * (1f - wRoca));

            // Normalizar
            float total = wRoca + wHierba + wTierra + wHierba2;
            if (total < 0.001f) total = 1f;

            if (capas > 0) alpha[z, x, 0] = wHierba  / total;
            if (capas > 1) alpha[z, x, 1] = wRoca    / total;
            if (capas > 2) alpha[z, x, 2] = wTierra  / total;
            if (capas > 3) alpha[z, x, 3] = wHierba2 / total;
        }

        coords.Dispose(); noiseHierba.Dispose(); noiseRoca.Dispose(); noiseTierra.Dispose();

        AlsasuaLogger.Info("MatFBM", $"Alphamap fBm generado {res}x{res}, {octavas} octavas");
        return alpha;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  3. BOIDS — configuración para manifestantes y fauna
    // ════════════════════════════════════════════════════════════════════════

    public struct ConfigBoids
    {
        public float radioSep, radioAlin, radioCoh;
        public float pesoSep, pesoAlin, pesoCoh, pesoObj;
        public float velMax, fuerza;
    }

    public static readonly ConfigBoids BOIDS_MANIFESTANTE = new ConfigBoids
    {
        radioSep=1.5f, radioAlin=6f,  radioCoh=12f,
        pesoSep=2.5f,  pesoAlin=1.0f, pesoCoh=0.8f, pesoObj=0.6f,
        velMax=1.8f,   fuerza=3.0f
    };

    public static readonly ConfigBoids BOIDS_DISTURBIOS = new ConfigBoids
    {
        radioSep=1.2f, radioAlin=4f,  radioCoh=8f,
        pesoSep=2.0f,  pesoAlin=0.6f, pesoCoh=0.5f, pesoObj=1.2f,
        velMax=3.5f,   fuerza=5.0f
    };

    public static readonly ConfigBoids BOIDS_OVEJAS = new ConfigBoids
    {
        radioSep=2.0f, radioAlin=8f,  radioCoh=15f,
        pesoSep=3.0f,  pesoAlin=1.5f, pesoCoh=1.2f, pesoObj=0.3f,
        velMax=1.2f,   fuerza=2.0f
    };

    /// <summary>
    /// Ejecuta un tick de Boids para N agentes. Devuelve arrays con nuevas
    /// posiciones y velocidades (caller hace Dispose).
    /// </summary>
    public static (NativeArray<float3> pos, NativeArray<float3> vel)
        TickBoids(NativeArray<float3> posiciones, NativeArray<float3> velocidades,
                  Vector3 objetivo, ConfigBoids cfg, float dt)
    {
        int n   = posiciones.Length;
        var nPos = new NativeArray<float3>(n, Allocator.TempJob);
        var nVel = new NativeArray<float3>(n, Allocator.TempJob);

        var job = new JobBoidsUpdate
        {
            posiciones         = posiciones,
            velocidades        = velocidades,
            radioSeparacion    = cfg.radioSep,
            radioAlineacion    = cfg.radioAlin,
            radioCohesion      = cfg.radioCoh,
            pesoSeparacion     = cfg.pesoSep,
            pesoAlineacion     = cfg.pesoAlin,
            pesoCohesion       = cfg.pesoCoh,
            velocidadMax       = cfg.velMax,
            fuerza             = cfg.fuerza,
            deltaTime          = dt,
            objetivo           = new float3(objetivo.x, objetivo.y, objetivo.z),
            pesoObjetivo       = cfg.pesoObj,
            nuevasVelocidades  = nVel,
            nuevasPosiciones   = nPos,
        };

        job.Schedule(n, 8).Complete();
        return (nPos, nVel);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  4. CATMULL-ROM — suavizar carreteras
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Suaviza una lista de waypoints de carretera OSM con Catmull-Rom.
    /// Devuelve una lista con `divisiones` veces más puntos.
    /// </summary>
    public static List<Vector3> SuavizarCarretera(List<Vector3> waypoints, int divisiones = 8)
    {
        if (waypoints == null || waypoints.Count < 2) return waypoints;

        int n        = waypoints.Count;
        int maxPuntos= (n - 1) * divisiones + 1;

        var waypNative = new NativeArray<float3>(n, Allocator.TempJob);
        var resultNative= new NativeArray<float3>(maxPuntos, Allocator.TempJob);
        var conteo     = new NativeArray<int>(1, Allocator.TempJob);

        for (int i = 0; i < n; i++)
            waypNative[i] = new float3(waypoints[i].x, waypoints[i].y, waypoints[i].z);

        new JobCatmullRomSubdividir
        {
            waypoints   = waypNative,
            divisiones  = divisiones,
            resultado   = resultNative,
            conteo      = conteo,
        }.Schedule().Complete();

        var suavizados = new List<Vector3>(conteo[0]);
        for (int i = 0; i < conteo[0]; i++)
        {
            var p = resultNative[i];
            suavizados.Add(new Vector3(p.x, p.y, p.z));
        }

        waypNative.Dispose(); resultNative.Dispose(); conteo.Dispose();

        AlsasuaLogger.Info("MatCatmull", $"Carretera: {n} → {suavizados.Count} puntos (x{divisiones})");
        return suavizados;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  5. DENSIDAD GAUSSIANA — spawn de NPCs
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Genera N posiciones de spawn para NPCs ponderadas por densidad urbana de Alsasua.
    /// Los NPCs aparecen más frecuentemente cerca de Herriko Plaza.
    /// </summary>
    public static List<Vector3> GenerarSpawnsNPCPonderados(
        Vector3 centroMundo, float radio, int cantidad)
    {
        var resultado = new List<Vector3>(cantidad);
        var rng = new Unity.Mathematics.Random((uint)(Time.time * 1000 + 1));

        for (int i = 0; i < cantidad * 5 && resultado.Count < cantidad; i++)
        {
            var pos = DensidadAlsasua.SamplearPuntoConDensidad(centroMundo, radio, ref rng);
            var terrain = Terrain.activeTerrain;
            if (terrain != null)
            {
                float y = terrain.SampleHeight(pos);
                pos.y = y;
            }
            resultado.Add(pos);
        }

        AlsasuaLogger.Info("MatGaussiana", $"Spawn NPCs: {resultado.Count}/{cantidad} ponderados");
        return resultado;
    }
}
