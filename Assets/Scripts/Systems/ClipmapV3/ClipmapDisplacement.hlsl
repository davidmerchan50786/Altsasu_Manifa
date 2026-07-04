// Assets/Scripts/_ClipmapV3~/ClipmapDisplacement.hlsl  (STAGING — fuera del build)
// ─────────────────────────────────────────────────────────────────────────────
//  Custom Function (HDRP Shader Graph, etapa VERTEX) para el clipmap V3.
//  Muestrea heightmap_unificado.r16 (subido como Texture2D R16, lineal, clamp,
//  bilineal) por vertex-texture-fetch y desplaza el vértice a su cota real.
//  Además RECONSTRUYE la normal por diferencias centrales (4 taps) → sombreado
//  correcto sin bake de normalmap. Espacio OBJETO (el ClipmapTerrenoV3 no rota ni
//  escala y deja Y=0, así que objeto == mundo en XZ e Y).
//
//  Decodificación EXACTA, idéntica a MuestreadorHeightmapV3 (CPU) y al .py:
//     q (0..65535)  = muestra.r * 65535      // R16 unorm
//     altitudReal   = Base + q / 64.0
//     alturaMundo Y = altitudReal - ZMin
//
//  UV (fila 0 = sur):  u = (worldX - (OX-Half)) / (2*Half)
//                      v = (worldZ - (OZ-Half)) / (2*Half)
// ─────────────────────────────────────────────────────────────────────────────
#ifndef CLIPMAP_DISPLACEMENT_INCLUDED
#define CLIPMAP_DISPLACEMENT_INCLUDED

// Altura mundo (Y) en una coordenada de mundo (x,z). SampleLevel = válido en vertex.
float ClipmapSampleY(UnityTexture2D Height, UnitySamplerState SS,
                     float worldX, float worldZ,
                     float Half, float OX, float OZ, float Base, float ZMin)
{
    float lado = 2.0 * Half;
    float2 uv = float2((worldX - (OX - Half)) / lado,
                       (worldZ - (OZ - Half)) / lado);
    uv = saturate(uv);                                   // clamp al borde del heightmap
    float r = SAMPLE_TEXTURE2D_LOD(Height.tex, SS.samplerstate, uv, 0).r;
    float q = r * 65535.0;
    return (Base + q / 64.0) - ZMin;
}

// PosOS: posición objeto del vértice (Position node en Object).
// OrigenXZ: posición de mundo del GameObject (material._ClipmapOrigen.xz).
// OutPosOS/OutNormalOS: alimentan los puertos Position(Object) y Normal(Object) del master.
void ClipmapDisplace_float(
    float3 PosOS, float2 OrigenXZ,
    UnityTexture2D Height, UnitySamplerState SS,
    float Half, float OX, float OZ, float Base, float ZMin, float Res,
    out float3 OutPosOS, out float3 OutNormalOS)
{
    float wx = PosOS.x + OrigenXZ.x;
    float wz = PosOS.z + OrigenXZ.y;

    float y = ClipmapSampleY(Height, SS, wx, wz, Half, OX, OZ, Base, ZMin);
    OutPosOS = float3(PosOS.x, y, PosOS.z);

    // Normal por diferencias centrales (epsilon = 1 téxel de mundo).
    float e = (2.0 * Half) / max(Res - 1.0, 1.0);
    float hL = ClipmapSampleY(Height, SS, wx - e, wz,     Half, OX, OZ, Base, ZMin);
    float hR = ClipmapSampleY(Height, SS, wx + e, wz,     Half, OX, OZ, Base, ZMin);
    float hD = ClipmapSampleY(Height, SS, wx,     wz - e, Half, OX, OZ, Base, ZMin);
    float hU = ClipmapSampleY(Height, SS, wx,     wz + e, Half, OX, OZ, Base, ZMin);
    OutNormalOS = normalize(float3(hL - hR, 2.0 * e, hD - hU));
}

// Variante half (si el grafo pide precisión half). Misma matemática.
void ClipmapDisplace_half(
    float3 PosOS, float2 OrigenXZ,
    UnityTexture2D Height, UnitySamplerState SS,
    float Half, float OX, float OZ, float Base, float ZMin, float Res,
    out float3 OutPosOS, out float3 OutNormalOS)
{
    ClipmapDisplace_float(PosOS, OrigenXZ, Height, SS,
                          Half, OX, OZ, Base, ZMin, Res, OutPosOS, OutNormalOS);
}

#endif
