// Assets/Scripts/Core/Espacial/UnifiedSpatialGrid.cs
// ═══════════════════════════════════════════════════════════════════════════
//  OMNI-GRID — IMPLEMENTACIÓN (capa CORE) — Fase 1 (variante hashmap)
//
//  Clase plana (NO MonoBehaviour). Doble buffer con coherencia N-1:
//    · Submit (productores, frame N)        → _append
//    · ConstruirFrame (EarlyUpdate, N+1)    → vuelca _append a _datos, construye
//      el grid Morton y limpia _append. El grid de N+1 contiene los datos de N.
//    · Query* (consumidores, frame N+1)     → leen _datos/_grid estables
//
//  → cero hazards de orden de Update; 1 frame de latencia (invisible para
//    IA/culling/streaming). Disparo e instalación en PlayerLoop: OmniGridLoop.
//
//  Diseño: Docs/arquitectura_omnigrid.md
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

public sealed class UnifiedSpatialGrid : IUnifiedSpatialGrid
{
    NativeList<SpatialData> _append;   // acumula las submissions del frame en curso (→ próximo grid)
    NativeList<SpatialData> _datos;    // snapshot estable consultable este frame
    NativeParallelMultiHashMap<uint, int> _grid;   // Morton → índice en _datos
    bool _listo;
    int  _conteo;

    // Entidades retenidas (persisten entre frames hasta QuitarRetenido).
    readonly Dictionary<int, SpatialData> _retenidos = new(128);
    int _siguienteHandle = 1;

    public bool Listo  => _listo;
    public int  Conteo => _conteo;
    public NativeParallelMultiHashMap<uint, int> GridMorton => _grid;
    public NativeArray<SpatialData> Datos => _datos.AsArray();

    public UnifiedSpatialGrid(int capacidadInicial = 4096)
    {
        _append = new NativeList<SpatialData>(capacidadInicial, Allocator.Persistent);
        _datos  = new NativeList<SpatialData>(capacidadInicial, Allocator.Persistent);
        _grid   = new NativeParallelMultiHashMap<uint, int>(capacidadInicial, Allocator.Persistent);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  INYECCIÓN
    // ════════════════════════════════════════════════════════════════════════
    public void Submit(in SpatialData d) => _append.Add(d);

    public void SubmitBatch(NativeArray<SpatialData> lote)
    {
        if (lote.IsCreated && lote.Length > 0) _append.AddRange(lote);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  CONSTRUCCIÓN (EarlyUpdate; lo llama OmniGridLoop)
    // ════════════════════════════════════════════════════════════════════════
    public void ConstruirFrame()
    {
        int n = _append.Length;

        _datos.Clear();
        if (n > 0) _datos.AddRange(_append.AsArray());
        _append.Clear();

        // Entidades retenidas (ghosts, anclas de streaming, proxies estáticos):
        // se añaden al snapshot de este frame (persisten entre frames).
        if (_retenidos.Count > 0)
            foreach (var kv in _retenidos) _datos.Add(kv.Value);

        int total = _datos.Length;
        _conteo = total;

        _grid.Clear();
        if (total == 0) { _listo = true; return; }

        if (_grid.Capacity < total) _grid.Capacity = total;

        new InsertarJob
        {
            datos = _datos.AsArray(),
            ox = OmniGridMath.OX,
            oz = OmniGridMath.OZ,
            grid = _grid.AsParallelWriter()
        }.Schedule(total, 128).Complete();

        _listo = true;
    }

    // ── Entidades retenidas ──
    public int SubmitRetenido(in SpatialData d)
    {
        int h = _siguienteHandle++;
        _retenidos[h] = d;
        return h;
    }

    public void ActualizarRetenido(int handle, float3 pos)
    {
        if (_retenidos.TryGetValue(handle, out var d)) { d.pos = pos; _retenidos[handle] = d; }
    }

    public void QuitarRetenido(int handle) => _retenidos.Remove(handle);

    // ════════════════════════════════════════════════════════════════════════
    //  GOD QUERIES
    // ════════════════════════════════════════════════════════════════════════
    public void QueryRadio(float3 centro, float radio, TipoEspacial filtro, NativeList<int> resultado)
    {
        resultado.Clear();
        if (!_listo || _conteo == 0) return;

        var datos = _datos.AsArray();
        int2 c0 = OmniGridMath.Celda(centro);
        int anillos = (int)math.ceil(radio * OmniGridMath.INV_CELL);
        float r2 = radio * radio;

        for (int dz = -anillos; dz <= anillos; dz++)
        for (int dx = -anillos; dx <= anillos; dx++)
        {
            uint key = OmniGridMath.Morton(math.clamp(c0 + new int2(dx, dz), 0, 0xFFFF));
            if (!_grid.TryGetFirstValue(key, out int i, out var it)) continue;
            do
            {
                var d = datos[i];
                if ((d.tipo & filtro) == 0) continue;
                if (math.distancesq(d.pos, centro) <= r2) resultado.Add(d.id);
            }
            while (_grid.TryGetNextValue(out i, ref it));
        }
    }

    public void QueryRadioPos(float3 centro, float radio, TipoEspacial filtro, NativeList<float3> resultado)
    {
        resultado.Clear();
        if (!_listo || _conteo == 0) return;

        var datos = _datos.AsArray();
        int2 c0 = OmniGridMath.Celda(centro);
        int anillos = (int)math.ceil(radio * OmniGridMath.INV_CELL);
        float r2 = radio * radio;

        for (int dz = -anillos; dz <= anillos; dz++)
        for (int dx = -anillos; dx <= anillos; dx++)
        {
            uint key = OmniGridMath.Morton(math.clamp(c0 + new int2(dx, dz), 0, 0xFFFF));
            if (!_grid.TryGetFirstValue(key, out int i, out var it)) continue;
            do
            {
                var d = datos[i];
                if ((d.tipo & filtro) == 0) continue;
                if (math.distancesq(d.pos, centro) <= r2) resultado.Add(d.pos);
            }
            while (_grid.TryGetNextValue(out i, ref it));
        }
    }

    public int QueryRadioContar(float3 centro, float radio, TipoEspacial filtro)
    {
        if (!_listo || _conteo == 0) return 0;

        var datos = _datos.AsArray();
        int2 c0 = OmniGridMath.Celda(centro);
        int anillos = (int)math.ceil(radio * OmniGridMath.INV_CELL);
        float r2 = radio * radio;
        int total = 0;

        for (int dz = -anillos; dz <= anillos; dz++)
        for (int dx = -anillos; dx <= anillos; dx++)
        {
            uint key = OmniGridMath.Morton(math.clamp(c0 + new int2(dx, dz), 0, 0xFFFF));
            if (!_grid.TryGetFirstValue(key, out int i, out var it)) continue;
            do
            {
                var d = datos[i];
                if ((d.tipo & filtro) == 0) continue;
                if (math.distancesq(d.pos, centro) <= r2) total++;
            }
            while (_grid.TryGetNextValue(out i, ref it));
        }
        return total;
    }

    public void QueryFrustum(NativeArray<float4> planos6, float3 centroCam, float alcance,
                             TipoEspacial filtro, NativeList<int> resultado)
    {
        resultado.Clear();
        if (!_listo || _conteo == 0 || !planos6.IsCreated) return;

        var datos = _datos.AsArray();
        int2 c0 = OmniGridMath.Celda(centroCam);
        int anillos = (int)math.ceil(alcance * OmniGridMath.INV_CELL);

        for (int dz = -anillos; dz <= anillos; dz++)
        for (int dx = -anillos; dx <= anillos; dx++)
        {
            uint key = OmniGridMath.Morton(math.clamp(c0 + new int2(dx, dz), 0, 0xFFFF));
            if (!_grid.TryGetFirstValue(key, out int i, out var it)) continue;
            do
            {
                var d = datos[i];
                if ((d.tipo & filtro) == 0) continue;

                bool dentro = true;
                for (int p = 0; p < planos6.Length; p++)
                {
                    float4 pl = planos6[p];                       // (normal.xyz, distancia), inward
                    if (math.dot(pl.xyz, d.pos) + pl.w < -d.radio) { dentro = false; break; }
                }
                if (dentro) resultado.Add(d.id);
            }
            while (_grid.TryGetNextValue(out i, ref it));
        }
    }

    public void QueryCeldasNuevas(float3 antes, float3 ahora, float radio, NativeList<uint> celdasEntrantes)
    {
        celdasEntrantes.Clear();

        int2 ca = OmniGridMath.Celda(ahora);
        int anillos = (int)math.ceil(radio * OmniGridMath.INV_CELL);
        float r2 = radio * radio;
        float2 pa = new float2(antes.x, antes.z);
        float2 pn = new float2(ahora.x, ahora.z);

        for (int dz = -anillos; dz <= anillos; dz++)
        for (int dx = -anillos; dx <= anillos; dx++)
        {
            int2 c = math.clamp(ca + new int2(dx, dz), 0, 0xFFFF);
            float2 centro = OmniGridMath.CentroCelda(c);
            if (math.distancesq(centro, pn) > r2) continue;       // no está en el disco actual
            if (math.distancesq(centro, pa) > r2)                 // ...y no estaba en el anterior
                celdasEntrantes.Add(OmniGridMath.Morton(c));
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  CICLO DE VIDA
    // ════════════════════════════════════════════════════════════════════════
    public void Dispose()
    {
        if (_append.IsCreated) _append.Dispose();
        if (_datos.IsCreated)  _datos.Dispose();
        if (_grid.IsCreated)   _grid.Dispose();
        _listo = false;
        _conteo = 0;
    }
}
