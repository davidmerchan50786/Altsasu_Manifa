// Assets/Scripts/_ClipmapV3~/MuestreadorHeightmapV3.cs  (STAGING — carpeta ~ no se compila)
// ─────────────────────────────────────────────────────────────────────────────
//  Muestreador CPU del heightmap unificado V3 (heightmap_unificado.r16 + meta.json,
//  generados por Tools/GenerarHeightmapUnificadoV3.py). Bilineal, O(1).
//
//  Es la pieza que permite que ServicioTerreno exponga AlturaMundo() con el
//  clipmap, manteniendo el contrato ITerrainService → edificios / NavMesh /
//  árboles / Cesium NO cambian. Determinista (sin dependencias de render).
//
//  Convención: row 0 = sur (Z mínima). altitudReal = BASE + q/64.
//  Devuelve altura MUNDO Unity = altitudReal - Z_MIN (igual que el resto del juego).
// ─────────────────────────────────────────────────────────────────────────────
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

public sealed class MuestreadorHeightmapV3
{
    ushort[] _q;
    int _res;
    float _ox, _oz, _half, _mpp, _base, _zmin;
    public bool Listo { get; private set; }

    /// <summary>Carga desde Assets/AlsasuaData/terrain_clipmap_v3 (o ruta dada).</summary>
    public bool Cargar(string carpeta = null)
    {
        carpeta ??= Path.Combine(Application.dataPath, "AlsasuaData", "terrain_clipmap_v3");
        string fr16 = Path.Combine(carpeta, "heightmap_unificado.r16");
        string fmeta = Path.Combine(carpeta, "meta.json");
        if (!File.Exists(fr16) || !File.Exists(fmeta)) { Listo = false; return false; }

        JObject m = JObject.Parse(File.ReadAllText(fmeta));
        _res  = (int)m["res"];
        _half = (float)m["halfExtent_m"];
        _mpp  = (float)m["metrosPorPixel"];
        _base = (float)m["datumYBase"];
        _zmin = (float)m["Z_MIN"];
        _ox = (float)m["origenUnity"]["OX"];
        _oz = (float)m["origenUnity"]["OZ"];

        byte[] bytes = File.ReadAllBytes(fr16);
        if (bytes.Length != _res * _res * 2) { Debug.LogError("[V3] tamaño r16 inesperado"); Listo = false; return false; }
        _q = new ushort[_res * _res];
        System.Buffer.BlockCopy(bytes, 0, _q, 0, bytes.Length); // little-endian (igual que Unity)
        Listo = true;
        return true;
    }

    /// <summary>True si (x,z) cae dentro del área cubierta por el heightmap.</summary>
    public bool EnRango(float x, float z)
    {
        return x >= _ox - _half && x <= _ox + _half && z >= _oz - _half && z <= _oz + _half;
    }

    /// <summary>Altura MUNDO Unity (Y) bajo (x,z), bilineal. Fuera de rango → borde clamp.</summary>
    public float AlturaMundo(float x, float z)
    {
        if (!Listo) return GeoDataAlsasua.ALT_FALLBACK;

        float fx = (x - (_ox - _half)) / _mpp;   // columna fraccionaria
        float fz = (z - (_oz - _half)) / _mpp;   // fila fraccionaria (0 = sur)
        fx = Mathf.Clamp(fx, 0f, _res - 1.0001f);
        fz = Mathf.Clamp(fz, 0f, _res - 1.0001f);

        int x0 = (int)fx, z0 = (int)fz;
        int x1 = Mathf.Min(x0 + 1, _res - 1), z1 = Mathf.Min(z0 + 1, _res - 1);
        float tx = fx - x0, tz = fz - z0;

        float q00 = _q[z0 * _res + x0], q10 = _q[z0 * _res + x1];
        float q01 = _q[z1 * _res + x0], q11 = _q[z1 * _res + x1];
        float q = Mathf.Lerp(Mathf.Lerp(q00, q10, tx), Mathf.Lerp(q01, q11, tx), tz);

        float altitudReal = _base + q / 64f;
        return altitudReal - _zmin;   // altura mundo Unity (datum Z_MIN), igual que AlturaTerreno
    }
}
