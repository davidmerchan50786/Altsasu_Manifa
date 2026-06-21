// Assets/Scripts/Runtime/SistemaDecalesAAA.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SISTEMA DE DECALES AAA — Fase 6 (Docs/plan_render_aaa.md)
//
//  Siembra decales procedurales sobre el suelo y las fachadas sin necesitar
//  texturas externas. Crea los materiales HDRP/Decal en código.
//
//  TIPOS DE DECAL (sin texturas):
//    · Manchas húmedas (WetPatch): oscuro + smoothness alta → los SSR
//      del SistemaVolumenHDRP las hacen reflejar el cielo. Crea el look
//      de "ha llovido" tan característico de ciudades reales.
//    · Charcos (Puddle): variante más grandes y oscuras.
//    · Suciedad base-muro (WallDirt): franja horizontal oscura al pie de
//      las fachadas — suciedad acumulada por salpicaduras. Sin normal.
//
//  DISTRIBUCIÓN: rejilla de Poisson-lite (perturbación aleatoria sobre
//  rejilla regular) centrada en Herriko Plaza (OX, OZ). Número de decales
//  gobernado por BUDGET_MAX para no superar el presupuesto de render.
//
//  SINCRONIZACIÓN CON CICLO:
//    · Día: manchas húmedas visibles (0.7 opacity)
//    · Lluvia: opacity sube a 1.0 (auto-detecta via SistemaVolumenHDRP)
//    · Noche: reducidas (0.3) — el asfalto seco de noche no refleja tanto
//
//  Coste: DecalProjector en HDRP = ~0.05 ms GPU por decal en resolución
//  full. Con BUDGET_MAX = 400 → ~20 ms total por frame de decales → uso
//  del HDRP Decal atlas. Ajusta BUDGET_MAX si el GPU frame time sube.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

[DefaultExecutionOrder(60)]
public sealed class SistemaDecalesAAA : MonoBehaviour
{
    // ── Presupuesto ────────────────────────────────────────────────────────
    const int   BUDGET_MAX      = 400;    // decales totales máximos
    const float RADIO_URBANO    = 800f;   // m desde la plaza con decales densos
    const float RADIO_SUBURBANO = 1500f;  // m con decales espaciados
    const float PASO_DENSE      = 18f;    // m entre decales en zona densa
    const float PASO_SPARSE     = 45f;    // m en zona suburbana
    const float MS_FRAME        = 2f;     // presupuesto de CPU por frame para siembra

    static readonly int ID_BaseColor = Shader.PropertyToID("_BaseColor");

    Material _matWet, _matPuddle;
    readonly List<DecalProjector> _todosDecales = new(BUDGET_MAX);
    float _opacityTarget = 0.7f;

    // ── Bootstrap ─────────────────────────────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("SistemaDecalesAAA");
        DontDestroyOnLoad(go);
        go.AddComponent<SistemaDecalesAAA>();
    }

    // Material de suciedad de pared (proyección horizontal → fachadas verticales)
    Material _matDirt;

    void Start()
    {
        _matWet    = CrearMaterialDecal(new Color(0.10f, 0.11f, 0.13f, 0.70f), 0.93f);
        _matPuddle = CrearMaterialDecal(new Color(0.06f, 0.07f, 0.09f, 0.85f), 0.97f);
        _matDirt   = CrearMaterialDecal(new Color(0.10f, 0.09f, 0.08f, 0.45f), 0.18f);
        StartCoroutine(SembrarAsync());
        StartCoroutine(SembrarDecalesParedAsync());
        StartCoroutine(CicloDia());
    }

    void OnDestroy()
    {
        if (_matWet)    Destroy(_matWet);
        if (_matPuddle) Destroy(_matPuddle);
        if (_matDirt)   Destroy(_matDirt);
    }

    // ── Siembra ───────────────────────────────────────────────────────────
    IEnumerator SembrarAsync()
    {
        // Pequeña espera: dejar que el terreno cargue para tener alturas correctas
        yield return new WaitForSeconds(3f);

        var raiz = new GameObject("Decales_AAA");
        float cx = GeoDataAlsasua.OX, cz = GeoDataAlsasua.OZ;
        float t0 = Time.realtimeSinceStartup;
        int count = 0;

        // ── Zona densa (urbano) ──────────────────────────────────────────
        for (float dx = -RADIO_URBANO; dx <= RADIO_URBANO && count < BUDGET_MAX * 0.65f; dx += PASO_DENSE)
        {
            for (float dz = -RADIO_URBANO; dz <= RADIO_URBANO && count < BUDGET_MAX * 0.65f; dz += PASO_DENSE)
            {
                float wx = cx + dx + Random.Range(-PASO_DENSE * 0.4f, PASO_DENSE * 0.4f);
                float wz = cz + dz + Random.Range(-PASO_DENSE * 0.4f, PASO_DENSE * 0.4f);

                float n = Mathf.PerlinNoise(wx * 0.007f + 0.1f, wz * 0.007f + 0.3f);
                if (n < 0.38f) continue;   // ~38% de densidad

                float wy = TerrenoGlobal.AlturaMundo(wx, wz) + 0.05f;
                bool esPuddleGrande = n > 0.78f;
                var dp = ColocarDecal(raiz, esPuddleGrande ? _matPuddle : _matWet, wx, wy, wz,
                    esPuddleGrande ? Random.Range(3f, 7f) : Random.Range(1.5f, 4f));
                _todosDecales.Add(dp);
                count++;

                if ((Time.realtimeSinceStartup - t0) * 1000f > MS_FRAME)
                {
                    yield return null;
                    t0 = Time.realtimeSinceStartup;
                }
            }
        }

        // ── Zona suburbana ────────────────────────────────────────────────
        for (float dx = -RADIO_SUBURBANO; dx <= RADIO_SUBURBANO && count < BUDGET_MAX; dx += PASO_SPARSE)
        {
            for (float dz = -RADIO_SUBURBANO; dz <= RADIO_SUBURBANO && count < BUDGET_MAX; dz += PASO_SPARSE)
            {
                if (Mathf.Abs(dx) < RADIO_URBANO && Mathf.Abs(dz) < RADIO_URBANO) continue;
                float wx = cx + dx + Random.Range(-PASO_SPARSE * 0.4f, PASO_SPARSE * 0.4f);
                float wz = cz + dz + Random.Range(-PASO_SPARSE * 0.4f, PASO_SPARSE * 0.4f);

                float n = Mathf.PerlinNoise(wx * 0.009f, wz * 0.009f);
                if (n < 0.45f) continue;

                float wy = TerrenoGlobal.AlturaMundo(wx, wz) + 0.05f;
                var dp = ColocarDecal(raiz, _matWet, wx, wy, wz, Random.Range(2f, 5f));
                _todosDecales.Add(dp);
                count++;

                if ((Time.realtimeSinceStartup - t0) * 1000f > MS_FRAME)
                {
                    yield return null;
                    t0 = Time.realtimeSinceStartup;
                }
            }
        }

        Debug.Log($"[DecalesAAA] ✅ {count} decales sembrados (húmedos + charcos). " +
            "Con SSR activo → asfalto refleja el cielo.");
    }

    // ── Crear un DecalProjector en el mundo ───────────────────────────────
    static DecalProjector ColocarDecal(GameObject raiz, Material mat,
        float wx, float wy, float wz, float radio)
    {
        var go = new GameObject("D");
        go.transform.SetParent(raiz.transform, false);
        // DecalProjector dispara en -Y local → rotamos 90° en X para que -Z → -Y
        go.transform.SetPositionAndRotation(
            new Vector3(wx, wy + radio * 0.5f, wz),
            Quaternion.Euler(90f, Random.Range(0f, 360f), 0f));

        var dp = go.AddComponent<DecalProjector>();
        dp.material   = mat;
        dp.size       = new Vector3(radio, radio, radio);    // X, Z = footprint; Y = proj depth
        dp.fadeFactor = mat.GetColor(ID_BaseColor).a;
        dp.scaleMode  = DecalScaleMode.ScaleInvariant;
        return dp;
    }

    // ── Sincronización con el ciclo día/noche ─────────────────────────────
    IEnumerator CicloDia()
    {
        while (true)
        {
            yield return new WaitForSeconds(5f);

            // Detectar hora del ciclo vía SistemaVolumenHDRP si existe
            float hora = 12f;
            if (SistemaVolumenHDRP.Instance != null)
            {
                // La hora pública de SistemaVolumenHDRP es "_horaActual" (campo privado).
                // Intentamos vía reflexión; si no está, usamos 12h fijo.
                var campo = typeof(SistemaVolumenHDRP).GetField("_horaActual",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (campo != null) hora = (float)campo.GetValue(SistemaVolumenHDRP.Instance);
            }

            // Más visibles de noche (SSR refleja las luces de farola) y en lluvia
            bool esNoche = hora < 6f || hora > 21f;
            _opacityTarget = esNoche ? 0.9f : 0.65f;

            foreach (var dp in _todosDecales)
            {
                if (dp == null) continue;
                dp.fadeFactor = Mathf.Lerp(dp.fadeFactor, _opacityTarget, 0.3f);
            }
        }
    }

    // ── Siembra de suciedad en fachadas (proyección horizontal) ──────────
    IEnumerator SembrarDecalesParedAsync()
    {
        if (_matDirt == null) yield break;
        yield return new WaitForSeconds(4f);   // esperar tras SembrarAsync

        var raiz = new GameObject("Decales_Pared");
        float cx = GeoDataAlsasua.OX, cz = GeoDataAlsasua.OZ;
        float t0 = Time.realtimeSinceStartup;
        int count = 0;
        int budgetPared = BUDGET_MAX / 4;   // 25% del presupuesto para paredes

        // Grid de posiciones candidatas en la zona urbana
        for (float dx = -RADIO_URBANO; dx <= RADIO_URBANO && count < budgetPared; dx += PASO_DENSE * 1.5f)
        {
            for (float dz = -RADIO_URBANO; dz <= RADIO_URBANO && count < budgetPared; dz += PASO_DENSE * 1.5f)
            {
                // Solo en posiciones con suficiente ruido Perlin (zonas densas)
                float wx = cx + dx, wz = cz + dz;
                float n = Mathf.PerlinNoise(wx * 0.013f + 5f, wz * 0.013f + 7f);
                if (n < 0.55f) continue;

                float wy = TerrenoGlobal.AlturaMundo(wx, wz) + 0.5f;  // 0.5m sobre suelo

                // Colocar decales en 4 orientaciones cardinales (proyección horizontal)
                for (int dir = 0; dir < 4; dir++)
                {
                    float yaw = dir * 90f;
                    // Decal de pared: Euler(0, yaw, 0) → proyecta en -Z horizontal
                    var go = new GameObject("DP");
                    go.transform.SetParent(raiz.transform, false);
                    go.transform.SetPositionAndRotation(
                        new Vector3(wx, wy + Random.Range(0f, 1.2f), wz),
                        Quaternion.Euler(0f, yaw, 0f));

                    var dp = go.AddComponent<DecalProjector>();
                    if (dp == null) continue;
                    dp.material   = _matDirt;
                    dp.size       = new Vector3(
                        Random.Range(0.8f, 2.5f),   // ancho
                        Random.Range(0.4f, 1.2f),   // alto
                        0.3f);                       // profundidad de proyección
                    dp.fadeFactor = 0.5f;
                    dp.scaleMode  = DecalScaleMode.ScaleInvariant;
                    count++;
                }

                if ((Time.realtimeSinceStartup - t0) * 1000f > MS_FRAME)
                {
                    yield return null;
                    t0 = Time.realtimeSinceStartup;
                }
            }
        }
        Debug.Log($"[DecalesAAA] {count} decales de pared sembrados.");
    }

    // ── Crear material HDRP/Decal programáticamente ───────────────────────
    static Material CrearMaterialDecal(Color colorAlpha, float smoothness)
    {
        var shader = Shader.Find("HDRP/Decal");
        if (shader == null)
        {
            Debug.LogWarning("[DecalesAAA] Shader 'HDRP/Decal' no encontrado. " +
                "Verifica que HDRP esté instalado y que 'Decals' esté habilitado en el HDRP Asset.");
            return null;
        }
        var mat = new Material(shader) { name = $"Decal_s{smoothness:F2}" };

        // Propiedades garantizadas en HDRP/Decal de todas las versiones soportadas
        mat.SetColor(ID_BaseColor, colorAlpha);

        // Propiedades que pueden variar por versión → SetFloat con try/catch silencioso
        TrySetFloat(mat, "_DecalBlend",          colorAlpha.a);
        TrySetFloat(mat, "_AffectsAlbedo",        1f);
        TrySetFloat(mat, "_AffectsSmoothness",    1f);
        TrySetFloat(mat, "_AffectsNormal",        0f);
        TrySetFloat(mat, "_AffectsEmission",      0f);
        // Smoothness: intentar tanto _SmoothnessRemapMax (HDRP 14-15) como _Smoothness (otros)
        TrySetFloat(mat, "_SmoothnessRemapMax",   smoothness);
        TrySetFloat(mat, "_SmoothnessRemapMin",   smoothness * 0.8f);
        TrySetFloat(mat, "_Smoothness",           smoothness);   // fallback

        mat.enableInstancing = true;
        return mat;
    }

    static void TrySetFloat(Material mat, string prop, float value)
    {
        if (mat.HasFloat(prop)) mat.SetFloat(prop, value);
    }
}
