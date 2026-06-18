// Assets/Scripts/Runtime/ImpostoresArbolesDistantes.cs
// ═══════════════════════════════════════════════════════════════════════════
//  IMPOSTORES DE ÁRBOLES A DISTANCIA — billboards GPU-instanced POR ESPECIE
//
//  El monte lejano salía pelado: AlsasuaTreeStreamer solo instancia árboles 3D
//  hasta ~800 m (máx ~300). Más allá, este sistema dibuja TODOS los árboles del
//  streamer (33k) que estén en [distanciaMin..distanciaMax] como quads orientados
//  a la cámara (cilíndricos), en muy pocas draw calls con RenderMeshInstanced.
//
//  PERFECTOS (jun 2026): cada árbol usa la SILUETA REAL de su especie (roble/pino/
//  ribera/genérico) — texturas billboard reales en Resources/Impostores/ (copiadas
//  por el menú Tools/Alsasua/Render ▸ 🌳 Preparar Impostores de Árboles). Antes era
//  un blob verde procedural único; ahora son árboles reales por bioma. Si falta una
//  textura, esa especie cae a la silueta procedural (nunca queda pelado).
//
//  · Buckets por especie (EspeciesArboles) → 1 material/draw-set por especie.
//  · Render en CHUNKS de 1023 (límite de RenderMeshInstanced) — el código viejo
//    pasaba 33k de golpe (solo se dibujaban 1023). Ahora se dibujan todos.
//  · Solo se reconstruyen las matrices de la banda visible (compactadas), a ~10 Hz
//    o al desplazarse la cámara. Tamaño de billboard por especie (pino alto/estrecho…).
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class ImpostoresArbolesDistantes : MonoBehaviour
{
    [Header("Banda de distancia (m)")]
    public float distanciaMin = 300f;
    public float distanciaMax = 6000f;

    [Tooltip("Cada cuánto se recalculan las matrices (s). Los impostores lejanos no necesitan 60 Hz.")]
    public float intervaloRebuild = 0.1f;

    const int NESP = 4;                       // 0 genérico · 1 roble · 2 pino · 3 ribera (= ESP_* del streamer)
    const int CHUNK = 1023;                   // máximo de instancias por RenderMeshInstanced
    static readonly string[] RES_TEX = { "Impostores/imp_generico", "Impostores/imp_roble", "Impostores/imp_pino", "Impostores/imp_ribera" };
    static readonly Vector2[] TAM    = { new(6f, 9f), new(9f, 11f), new(5f, 13f), new(5f, 14f) }; // (ancho, alto) por especie

    AlsasuaTreeStreamer _streamer;
    Camera      _cam;
    Mesh        _quad;
    Vector3[][] _base = new Vector3[NESP][];  // posiciones (Y terreno) por especie
    Matrix4x4[][] _vis = new Matrix4x4[NESP][]; // matrices compactas de la banda visible
    int[]       _nVis  = new int[NESP];
    Material[]  _mat   = new Material[NESP];
    RenderParams[] _rp = new RenderParams[NESP];
    bool        _listo;
    float       _tRebuild;
    Vector3     _ultimaCamPos = new Vector3(1e9f, 0, 0);
    static Texture2D _texProc;                // silueta procedural compartida (fallback)

    IEnumerator Start()
    {
        _streamer = GetComponent<AlsasuaTreeStreamer>() ?? FindFirstObjectByType<AlsasuaTreeStreamer>();
        if (_streamer == null) { enabled = false; yield break; }

        float tw = 0f;
        while (!_streamer.ListoImpostores && tw < 60f) { tw += 0.25f; yield return new WaitForSeconds(0.25f); }
        if (!_streamer.ListoImpostores) { enabled = false; yield break; }

        var pos = _streamer.PosicionesArboles;
        var esp = _streamer.EspeciesArboles;
        int n = pos != null ? pos.Count : 0;
        if (n == 0) { enabled = false; yield break; }

        // Conteo por especie → reservar arrays compactos.
        var cnt = new int[NESP];
        for (int i = 0; i < n; i++) cnt[Idx(esp, i)]++;
        for (int s = 0; s < NESP; s++) { _base[s] = new Vector3[cnt[s]]; _vis[s] = new Matrix4x4[cnt[s]]; }

        // Posición con Y real del terreno (tile-aware, amortizado), bucketeada por especie.
        var w = new int[NESP];
        for (int i = 0; i < n; i++)
        {
            var p = pos[i];
            int s = Idx(esp, i);
            _base[s][w[s]++] = new Vector3(p.x, TerrenoGlobal.AlturaMundo(p.x, p.z), p.z);
            if ((i & 2047) == 0) yield return null;
        }

        ConstruirQuad();
        var wb = new Bounds(new Vector3(GeoDataAlsasua.OX, 600f, GeoDataAlsasua.OZ), new Vector3(30000f, 5000f, 30000f));
        for (int s = 0; s < NESP; s++)
        {
            _mat[s] = ConstruirMaterial(s);
            _rp[s]  = new RenderParams(_mat[s]) { worldBounds = wb, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
        }

        _listo = true;
        AlsasuaLogger.Info("Impostores",
            $"Impostores por especie listos: gen {cnt[0]}, roble {cnt[1]}, pino {cnt[2]}, ribera {cnt[3]} (banda {distanciaMin:F0}-{distanciaMax:F0} m).");
    }

    static int Idx(System.Collections.Generic.IReadOnlyList<int> esp, int i)
    {
        if (esp == null || i >= esp.Count) return 0;
        int e = esp[i];
        return (e >= 0 && e < NESP) ? e : 0;
    }

    void LateUpdate()
    {
        if (!_listo) return;
        if (_cam == null) { _cam = Camera.main; if (_cam == null) return; }

        _tRebuild -= Time.deltaTime;
        Vector3 camPos = _cam.transform.position;
        bool movio = (camPos - _ultimaCamPos).sqrMagnitude > 25f;   // ~5 m
        if (_tRebuild <= 0f || movio) { RebuildTodas(camPos); _tRebuild = intervaloRebuild; _ultimaCamPos = camPos; }

        for (int s = 0; s < NESP; s++)
        {
            int nv = _nVis[s];
            if (nv == 0 || _mat[s] == null) continue;
            for (int off = 0; off < nv; off += CHUNK)
                Graphics.RenderMeshInstanced(_rp[s], _quad, 0, _vis[s], Mathf.Min(CHUNK, nv - off), off);
        }
    }

    void RebuildTodas(Vector3 camPos)
    {
        float minSq = distanciaMin * distanciaMin, maxSq = distanciaMax * distanciaMax;
        for (int s = 0; s < NESP; s++)
        {
            var b = _base[s]; var vis = _vis[s];
            var escala = new Vector3(TAM[s].x, TAM[s].y, 1f);
            int j = 0;
            for (int i = 0; i < b.Length; i++)
            {
                float dx = b[i].x - camPos.x, dz = b[i].z - camPos.z;
                float dSq = dx * dx + dz * dz;
                if (dSq < minSq || dSq > maxSq) continue;
                float yaw = Mathf.Atan2(camPos.x - b[i].x, camPos.z - b[i].z) * Mathf.Rad2Deg;
                vis[j++] = Matrix4x4.TRS(b[i], Quaternion.Euler(0f, yaw, 0f), escala);
            }
            _nVis[s] = j;
        }
    }

    // Quad con pivote en la BASE (y∈[0,1]) → se planta en el suelo al escalar por altura.
    void ConstruirQuad()
    {
        _quad = new Mesh { name = "ImpostorArbolQuad" };
        _quad.SetVertices(new[] { new Vector3(-0.5f,0,0), new Vector3(0.5f,0,0), new Vector3(0.5f,1,0), new Vector3(-0.5f,1,0) });
        _quad.SetUVs(0, new[] { new Vector2(0,0), new Vector2(1,0), new Vector2(1,1), new Vector2(0,1) });
        _quad.SetTriangles(new[] { 0,2,1, 0,3,2 }, 0);
        _quad.RecalculateNormals();
        _quad.RecalculateBounds();
    }

    Material ConstruirMaterial(int especie)
    {
        var sh = Shader.Find("HDRP/Unlit") ?? Shader.Find("Unlit/Transparent Cutout") ?? Shader.Find("Sprites/Default");
        var mat = new Material(sh) { name = $"Mat_Impostor_{especie}", enableInstancing = true };

        // Textura REAL de la especie (Resources) o silueta procedural si falta.
        Texture tex = Resources.Load<Texture2D>(RES_TEX[especie]);
        if (tex == null) tex = (_texProc ??= GenerarTexturaArbol());

        if (mat.HasProperty("_UnlitColorMap")) mat.SetTexture("_UnlitColorMap", tex);
        if (mat.HasProperty("_BaseColorMap"))  mat.SetTexture("_BaseColorMap", tex);
        if (mat.HasProperty("_MainTex"))        mat.SetTexture("_MainTex", tex);

        if (mat.HasProperty("_AlphaCutoffEnable")) mat.SetFloat("_AlphaCutoffEnable", 1f);
        if (mat.HasProperty("_AlphaCutoff"))       mat.SetFloat("_AlphaCutoff", 0.45f);
        if (mat.HasProperty("_Cutoff"))            mat.SetFloat("_Cutoff", 0.45f);
        mat.EnableKeyword("_ALPHATEST_ON");
        mat.renderQueue = (int)RenderQueue.AlphaTest;
        return mat;
    }

    // Silueta de árbol procedural (fallback) — copa elíptica + tronco con alpha.
    static Texture2D GenerarTexturaArbol()
    {
        const int W = 64, H = 128;
        var t = new Texture2D(W, H, TextureFormat.RGBA32, true);
        var px = new Color32[W * H];
        var trans = new Color32(0,0,0,0); var copaOsc = new Color32(38,78,32,255);
        var copaClr = new Color32(58,108,46,255); var tronco = new Color32(74,52,32,255);
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
        {
            int idx = y*W+x; px[idx] = trans;
            float fx = (x - W*0.5f)/(W*0.5f); float ny = y/(float)H;
            if (ny > 0.28f) { float ey = (ny-0.64f)/0.40f; float r = fx*fx + ey*ey; if (r < 1f) px[idx] = (r < 0.45f) ? copaClr : copaOsc; }
            if (ny < 0.34f && Mathf.Abs(fx) < 0.12f) px[idx] = tronco;
        }
        t.SetPixels32(px); t.Apply(true); t.wrapMode = TextureWrapMode.Clamp; t.filterMode = FilterMode.Bilinear;
        return t;
    }

    void OnDestroy()
    {
        for (int s = 0; s < NESP; s++) if (_mat[s] != null) Destroy(_mat[s]);
        if (_quad != null) Destroy(_quad);
    }
}
