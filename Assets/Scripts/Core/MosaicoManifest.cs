// Assets/Scripts/Core/MosaicoManifest.cs
// ═══════════════════════════════════════════════════════════════════════════
//  MOSAICO MANIFEST — POCOs del schema terrain_tiles_v2/manifest_v2.json
//
//  Generado por Tools/GenerarMosaicoTerrenoV2.py y validado por
//  Tools/ValidarMosaicoV2.py (gate Python). Aquí solo se LEE.
//
//  Codificación vertical "lattice 1/64":
//    · cuantoVertical = 1/64 m, altoGlobal = 65535/64 = 1023.984375 m
//    · cada tile guarda q (uint16, RAW little-endian, fila 0 = sur)
//    · alturaMundo(Unity) = tile.y + q/64    (datum: cota real − datumYBase)
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Newtonsoft.Json;

[Serializable]
public class MosaicoManifest
{
    public int version;
    public string descripcion;
    public string fecha;
    public string generadoPor;
    public float datumYBase;          // Z_MIN (511.33): cotaReal = alturaUnity + datum
    public float cuantoVertical;      // 1/64 m
    public float altoGlobal;          // 1023.984375 — size.y de TODOS los tiles
    public ConvencionHorizontal convencionHorizontal;
    public string ordenFilas;
    public List<AnilloDef> anillos;
    public List<string> fuentes;
    public CotasReferencia cotasReferencia;
    public List<TileDef> tiles;

    [Serializable]
    public class ConvencionHorizontal
    {
        public double E0, N0, OX, OZ, escalaX;
        public string formula;
    }

    [Serializable]
    public class AnilloDef
    {
        public int id;
        public float halfExtent;      // m desde la plaza
        public float tileM;           // lado del tile
        public int res;               // resolución heightmap (2049 / 1025)
    }

    [Serializable]
    public class CotaRef
    {
        public float x, z, cota, cotaEsperada;
    }

    [Serializable]
    public class CotasReferencia
    {
        public CotaRef plaza;
        public CotaRef estacion;
    }

    [Serializable]
    public class TileDef
    {
        public string file;
        public int anillo;
        public float x, z;            // esquina suroeste en Unity
        public long y64;              // posY en cuantos (entero, puede ser negativo)
        public float y;               // posY mundo = y64/64 (múltiplo exacto de 1/64)
        public float ancho;           // lado en m
        public int res;
        public float hMinReal, hMaxReal; // cotas reales (m s.n.m.) del contenido
        public string sha256;
    }

    // ── Carga ────────────────────────────────────────────────────────────────

    /// <summary>Lee y deserializa el manifest. Lanza si el archivo no existe o
    /// el schema no es v2.</summary>
    public static MosaicoManifest Cargar(string rutaJson)
    {
        var m = JsonConvert.DeserializeObject<MosaicoManifest>(File.ReadAllText(rutaJson));
        if (m == null || m.version != 2 || m.tiles == null || m.tiles.Count == 0)
            throw new InvalidDataException($"manifest_v2 inválido: {rutaJson}");
        return m;
    }

    /// <summary>SHA256 de un RAW vs manifest (opcional: detecta corrupción al
    /// importar). Devuelve true si coincide.</summary>
    public static bool VerificarSha256(TileDef tile, byte[] raw)
    {
        using var sha = SHA256.Create();
        var hex = BitConverter.ToString(sha.ComputeHash(raw)).Replace("-", "").ToLowerInvariant();
        return hex == tile.sha256;
    }
}
