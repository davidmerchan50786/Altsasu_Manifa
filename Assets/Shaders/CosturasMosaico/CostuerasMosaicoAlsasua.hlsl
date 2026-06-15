// Assets/Shaders/CosturasMosaico/CostuerasMosaicoAlsasua.hlsl
// =====================================================================
//  COSTURAS DEL MOSAICO ALSASUA — HLSL reutilizable
//
//  Diseño completo: Docs/arquitectura_costuras_terreno.md
//  Tres pilares:
//    1. Edge-biased tessellation     → TessFactorBordeAlsasua
//    2. Cross-tile normal blending   → NormalBordeAlsasua
//    3. Depth bias suave en bordes   → DepthBiasBordeAlsasua
//
//  Diseñado como include + Custom Function de Shader Graph:
//    · Cada función tiene firma simple float→float / float2→float3 →
//      se puede expone como nodo "Custom Function" sin tocar el .hlsl.
//    · Usa SAMPLE_TEXTURE2D_LOD para ser válido tanto en vertex como en
//      fragment stage. Las texturas se pasan como parámetro (Shader Graph
//      las marca como TEXTURE2D_PARAM internamente al exponerlas).
//
//  Uniforms los publica InyectorVecinosTerreno.cs (Systems) vía
//  Terrain.SetSplatMaterialPropertyBlock(mpb). El shader es agnóstico a
//  la lógica de carga — solo lee.
//
//  Convención de orientación de los vecinos N/S/E/W:
//    Nuestro tile está en UV [0..1]; UV.y=0 = sur; UV.y=1 = norte.
//    Vecino N: limita con nuestro lado y=1 → su lado SUR (su v=0).
//    Vecino S: limita con nuestro lado y=0 → su lado NORTE (su v=1).
//    Vecino E: limita con nuestro lado x=1 → su lado OESTE (su u=0).
//    Vecino W: limita con nuestro lado x=0 → su lado ESTE (su u=1).
// =====================================================================

#ifndef ALSASUA_COSTURAS_MOSAICO_INCLUDED
#define ALSASUA_COSTURAS_MOSAICO_INCLUDED

// SAMPLER_LINEAR_CLAMP — definido por HDRP en core.hlsl; si no estuviera,
// declarar uno propio (linear, clamp).
#ifndef ALSASUA_SAMPLER_DECLARADO
SamplerState s_alsasua_linear_clamp_sampler
{
    Filter = MIN_MAG_MIP_LINEAR;
    AddressU = Clamp;
    AddressV = Clamp;
};
#define ALSASUA_SAMPLER s_alsasua_linear_clamp_sampler
#define ALSASUA_SAMPLER_DECLARADO 1
#endif

// =====================================================================
//  1. EDGE-BIASED TESSELLATION
// =====================================================================
//  Devuelve un factor de tess interpolado entre `tessEdge` (en la banda
//  de borde, ancho `bordeUV`) y `tessBase` (en el interior).
//
//  `bordeUV` = anchoBordeMetros / ladoTileMetros  (típicamente 0.001–0.003)
//  Pensado para usarse como entrada del nodo "Tessellation Factor" de
//  Shader Graph HDRP/Lit Tessellation.
// ---------------------------------------------------------------------
void TessFactorBordeAlsasua_float(
    float2 uv01,
    float  tessBase,
    float  tessEdge,
    float  bordeUV,
    out float Factor)
{
    float dx = min(uv01.x, 1.0 - uv01.x);
    float dy = min(uv01.y, 1.0 - uv01.y);
    float d  = min(dx, dy);
    float w  = saturate(d / max(bordeUV, 1e-6));
    Factor = lerp(tessEdge, tessBase, w);
}

// =====================================================================
//  HELPER — Normal desde heightmap por diferencias finitas
// =====================================================================
//  Reconstruye la NORMAL (mundo) desde un heightmap R16 dado un UV
//  (u, v) en [0..1] del vecino. `texel` = (1/res, 1/res). `alturaMundo`
//  es el rango vertical efectivo (size.y del Terrain del vecino).
//
//  No leemos la altura "real" porque solo queremos la dirección; basta
//  con las diferencias muestreadas (en unidades arbitrarias) y reescalar
//  por (anchoMundoX, anchoMundoZ, alturaMundo).
// ---------------------------------------------------------------------
float3 NormalDesdeHeightmap_(
    Texture2D h, SamplerState samp,
    float2 texel, float ladoMundo, float alturaMundo,
    float2 uv)
{
    // Muestreo central + 4 vecinos con clamp implícito (sampler clamp).
    float hC  = SAMPLE_TEXTURE2D_LOD(h, samp, uv,                              0).r;
    float hXp = SAMPLE_TEXTURE2D_LOD(h, samp, uv + float2(texel.x, 0),          0).r;
    float hXm = SAMPLE_TEXTURE2D_LOD(h, samp, uv - float2(texel.x, 0),          0).r;
    float hZp = SAMPLE_TEXTURE2D_LOD(h, samp, uv + float2(0, texel.y),          0).r;
    float hZm = SAMPLE_TEXTURE2D_LOD(h, samp, uv - float2(0, texel.y),          0).r;

    // Paso mundo entre samples: lado / (1/texel) = lado * texel.
    float dxM = max(ladoMundo * texel.x, 1e-3);
    float dzM = max(ladoMundo * texel.y, 1e-3);
    float dyX = (hXp - hXm) * alturaMundo * 0.5;
    float dyZ = (hZp - hZm) * alturaMundo * 0.5;

    // gradiente → normal. Y = up.
    float3 t1 = float3(2.0 * dxM, dyX, 0.0);
    float3 t2 = float3(0.0,      dyZ, 2.0 * dzM);
    return normalize(cross(t2, t1));
}

// =====================================================================
//  2. CROSS-TILE NORMAL BLENDING
// =====================================================================
//  Devuelve la normal en mundo, mezclando con la del vecino correspondiente
//  cuando el píxel cae en la banda de borde. Fuera de la banda, devuelve
//  `normalLocal` sin coste.
//
//  Cada vecino aporta peso = (1 - d/bordeUV) * pesoVecino, donde d es la
//  distancia al lado correspondiente y pesoVecino ∈ {0,1} indica si existe
//  (esquinas del mundo o agujero del anillo → 0).
//
//  PARÁMETROS del vecino (uno por dirección, prefijo Nbr*):
//    nbrTexel  = (1/res_vec, 1/res_vec)   — ya publicado por el inyector
//    nbrLado   = ladoMundo del vecino (m)
//    nbrAltura = size.y del Terrain del vecino (m)
//    peso      = 1 si existe, 0 si no
//
//  CONVERSIÓN UV nuestra → UV del vecino (en su borde adyacente):
//    Vecino N (norte, nuestro y≈1):
//        su u = uv01.x         (mismo eje X)
//        su v = bordeUV - (1 - uv01.y)  → solo se muestrea cerca de su v=0
//                                          (en la práctica leemos su primera fila)
//    (análogo para S/E/W)
//  Como en la banda buscamos la normal *en el borde* del vecino, leemos
//  exactamente la primera fila/columna (v=0, v=1, u=0 o u=1) y NormalDesdeHeightmap_
//  se encarga del muestreo central + diferencias finitas locales.
// ---------------------------------------------------------------------
void NormalBordeAlsasua_float(
    float2 uv01,
    float3 normalLocal,
    float  bordeUV,
    // Vecino N
    Texture2D nbrN, float2 nbrTexelN, float nbrLadoN, float nbrAlturaN, float pesoN,
    // Vecino S
    Texture2D nbrS, float2 nbrTexelS, float nbrLadoS, float nbrAlturaS, float pesoS,
    // Vecino E
    Texture2D nbrE, float2 nbrTexelE, float nbrLadoE, float nbrAlturaE, float pesoE,
    // Vecino W
    Texture2D nbrW, float2 nbrTexelW, float nbrLadoW, float nbrAlturaW, float pesoW,
    out float3 Normal)
{
    float3 acc = normalLocal;
    float  ws  = 1.0;

    // ── Norte (uv.y → 1) ───────────────────────────────────────────────
    float wN = saturate(1.0 - (1.0 - uv01.y) / max(bordeUV, 1e-6)) * pesoN;
    if (wN > 0.0)
    {
        // Su u = uv01.x; su v = pequeña distancia al sur del vecino (≈0 + texel)
        float2 uvVecN = float2(uv01.x, nbrTexelN.y);
        float3 nN = NormalDesdeHeightmap_(nbrN, ALSASUA_SAMPLER,
                                          nbrTexelN, nbrLadoN, nbrAlturaN, uvVecN);
        acc += nN * wN; ws += wN;
    }

    // ── Sur (uv.y → 0) ─────────────────────────────────────────────────
    float wS = saturate(1.0 - uv01.y / max(bordeUV, 1e-6)) * pesoS;
    if (wS > 0.0)
    {
        // Su u = uv01.x; su v ≈ 1 - texel (su lado norte)
        float2 uvVecS = float2(uv01.x, 1.0 - nbrTexelS.y);
        float3 nS = NormalDesdeHeightmap_(nbrS, ALSASUA_SAMPLER,
                                          nbrTexelS, nbrLadoS, nbrAlturaS, uvVecS);
        acc += nS * wS; ws += wS;
    }

    // ── Este (uv.x → 1) ────────────────────────────────────────────────
    float wE = saturate(1.0 - (1.0 - uv01.x) / max(bordeUV, 1e-6)) * pesoE;
    if (wE > 0.0)
    {
        // Su u ≈ 0 + texel (su lado oeste); su v = uv01.y
        float2 uvVecE = float2(nbrTexelE.x, uv01.y);
        float3 nE = NormalDesdeHeightmap_(nbrE, ALSASUA_SAMPLER,
                                          nbrTexelE, nbrLadoE, nbrAlturaE, uvVecE);
        acc += nE * wE; ws += wE;
    }

    // ── Oeste (uv.x → 0) ───────────────────────────────────────────────
    float wW = saturate(1.0 - uv01.x / max(bordeUV, 1e-6)) * pesoW;
    if (wW > 0.0)
    {
        // Su u ≈ 1 - texel; su v = uv01.y
        float2 uvVecW = float2(1.0 - nbrTexelW.x, uv01.y);
        float3 nW = NormalDesdeHeightmap_(nbrW, ALSASUA_SAMPLER,
                                          nbrTexelW, nbrLadoW, nbrAlturaW, uvVecW);
        acc += nW * wW; ws += wW;
    }

    Normal = normalize(acc / ws);
}

// =====================================================================
//  3. DEPTH BIAS SUAVE EN BORDES
// =====================================================================
//  Suma un pequeño desplazamiento (m) a posWS sobre la dirección vista
//  para evitar Z-fighting cuando hay overlap milimétrico en el borde.
//  El bias cae linealmente desde el borde hacia el interior.
//
//  USO: sumar el float devuelto al `Position` en el Vertex stage, o
//  empujar posWS a lo largo de (camPos - posWS) por esa cantidad.
// ---------------------------------------------------------------------
void DepthBiasBordeAlsasua_float(
    float2 uv01,
    float  bordeUV,
    float  biasM,
    out float Bias)
{
    float dx = min(uv01.x, 1.0 - uv01.x);
    float dy = min(uv01.y, 1.0 - uv01.y);
    float d  = min(dx, dy);
    float w  = 1.0 - saturate(d / max(bordeUV, 1e-6));
    Bias = -biasM * w;       // negativo = hacia la cámara (HDRP +Z = adelante en vista)
}

// =====================================================================
//  Helper conveniente — desplazar posWS hacia la cámara por `biasM`
// =====================================================================
void EmpujarHaciaCamaraAlsasua_float(
    float3 posWS, float3 camPos,
    float biasM,
    out float3 PosOut)
{
    float3 dir = camPos - posWS;
    float  len = max(length(dir), 1e-4);
    PosOut = posWS + dir / len * biasM;
}

#endif // ALSASUA_COSTURAS_MOSAICO_INCLUDED
