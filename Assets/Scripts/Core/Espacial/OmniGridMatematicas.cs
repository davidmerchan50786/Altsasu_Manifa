// Assets/Scripts/Core/Espacial/OmniGridMatematicas.cs
// ═══════════════════════════════════════════════════════════════════════════
//  OMNI-GRID — MATEMÁTICA DEL ESPACIO (capa CORE, Burst-friendly, sin estado)
//
//  Quantización mundo→celda + código Morton 2D (Z-order curve) sobre XZ.
//  Vecinos espaciales → códigos Morton numéricamente cercanos → al ordenar el
//  array por este código, quedan contiguos en RAM (coherencia de caché).
//
//  El origen sólo necesita ser un punto FIJO: el particionado es idéntico para
//  cualquier origen estable. Se replican aquí las constantes del mundo (Herriko
//  Plaza) para mantener Core autónomo — GeoDataAlsasua vive en la capa Runtime y
//  Core no puede referenciarla (dirección de dependencias).
// ═══════════════════════════════════════════════════════════════════════════

using Unity.Mathematics;

public static class OmniGridMath
{
    // ── Mundo → celda ──
    public const float CELL_SIZE = 16f;            // m por celda. const por perf Burst (ver Docs §3.4)
    public const float INV_CELL  = 1f / CELL_SIZE;
    public const int   BIAS      = 1 << 15;        // 32768 → centra el rango de 16 bits (celda ≥ 0)

    // Origen fijo = Herriko Plaza (espejo de GeoDataAlsasua.OX/OZ; ver cabecera).
    public const float OX = 1918f;
    public const float OZ = 8570f;

    /// <summary>Posición mundo → coordenada de celda XZ, sesgada y recortada a [0, 65535].</summary>
    public static int2 Celda(float3 p, float ox, float oz)
    {
        int cx = (int)math.floor((p.x - ox) * INV_CELL) + BIAS;
        int cz = (int)math.floor((p.z - oz) * INV_CELL) + BIAS;
        return math.clamp(new int2(cx, cz), 0, 0xFFFF);
    }

    /// <summary>Sobrecarga con el origen por defecto del mundo.</summary>
    public static int2 Celda(float3 p) => Celda(p, OX, OZ);

    /// <summary>Código Morton 2D: entrelaza los 16 bits de cada eje → uint de 32 bits.</summary>
    public static uint Morton(int2 c) => Part1By1((uint)c.x) | (Part1By1((uint)c.y) << 1);

    /// <summary>Centro mundo (XZ) de una celda — para tests de cobertura (streaming).</summary>
    public static float2 CentroCelda(int2 c) => new float2(
        (c.x - BIAS) * CELL_SIZE + CELL_SIZE * 0.5f + OX,
        (c.y - BIAS) * CELL_SIZE + CELL_SIZE * 0.5f + OZ);

    // "Spread": separa los 16 bits bajos dejando un hueco entre cada bit.
    static uint Part1By1(uint x)
    {
        x &= 0x0000FFFF;
        x = (x | (x << 8)) & 0x00FF00FF;
        x = (x | (x << 4)) & 0x0F0F0F0F;
        x = (x | (x << 2)) & 0x33333333;
        x = (x | (x << 1)) & 0x55555555;
        return x;
    }

    // ── Gemelo HLSL (para culling en compute, fase 2) ──
    //   uint Part1By1(uint x){ x&=0xFFFF; x=(x|(x<<8))&0x00FF00FF; x=(x|(x<<4))&0x0F0F0F0F;
    //     x=(x|(x<<2))&0x33333333; x=(x|(x<<1))&0x55555555; return x; }
    //   uint Morton(uint2 c){ return Part1By1(c.x) | (Part1By1(c.y)<<1); }
}
