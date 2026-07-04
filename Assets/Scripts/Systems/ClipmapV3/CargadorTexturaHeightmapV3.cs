// Assets/Scripts/_ClipmapV3~/CargadorTexturaHeightmapV3.cs  (STAGING — fuera del build)
// ─────────────────────────────────────────────────────────────────────────────
//  Sube heightmap_unificado.r16 a la GPU como Texture2D R16 (lineal, clamp,
//  bilineal) y cablea el material del clipmap con las constantes del meta.json.
//  Es la pieza GPU gemela de MuestreadorHeightmapV3 (CPU): misma fuente, misma
//  decodificación → la malla desplazada en GPU y AlturaMundo() en CPU coinciden.
//
//  Nombres de referencia que DEBE exponer el Shader Graph (ver LEEME, receta):
//     _Height (Texture2D)  _ClipmapOrigen (Vector2)  _Half _OX _OZ _Base _ZMin _Res (Float)
//  El holder (ClipmapTerrenoV3) actualiza _ClipmapOrigen cada frame al recolocar.
// ─────────────────────────────────────────────────────────────────────────────
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

[RequireComponent(typeof(MeshRenderer))]
public sealed class CargadorTexturaHeightmapV3 : MonoBehaviour
{
    public Texture2D HeightTex { get; private set; }
    public bool Listo { get; private set; }

    /// <summary>Carga el R16 + meta y fija las constantes en 'material'. Idempotente.</summary>
    public bool Configurar(Material material, string carpeta = null)
    {
        if (material == null) return false;
        carpeta ??= Path.Combine(Application.dataPath, "AlsasuaData", "terrain_clipmap_v3");
        string fr16  = Path.Combine(carpeta, "heightmap_unificado.r16");
        string fmeta = Path.Combine(carpeta, "meta.json");
        if (!File.Exists(fr16) || !File.Exists(fmeta))
        {
            Debug.LogError("[ClipmapV3] falta heightmap_unificado.r16 / meta.json");
            return false;
        }

        JObject m = JObject.Parse(File.ReadAllText(fmeta));
        int   res  = (int)m["res"];
        float half = (float)m["halfExtent_m"];
        float baseM = (float)m["datumYBase"];
        float zmin = (float)m["Z_MIN"];
        float ox   = (float)m["origenUnity"]["OX"];
        float oz   = (float)m["origenUnity"]["OZ"];

        byte[] bytes = File.ReadAllBytes(fr16);
        if (bytes.Length != res * res * 2)
        {
            Debug.LogError($"[ClipmapV3] tamaño r16 inesperado: {bytes.Length} != {res * res * 2}");
            return false;
        }

        if (HeightTex == null)
        {
            // R16 = 1 canal 16-bit unorm; sampler.r = q/65535. Lineal (no sRGB).
            // NOTA: el upload de 33 MB requiere D3D12 upload heap ≥ 64 MB.
            // Se configura en Assets/StreamingAssets/boot.config:
            //   gfx-D3D12UploadHeapSize=134217728   (128 MB)
            // Sin esa entrada Unity crashea con DXGI_ERROR_DEVICE_RESET (887a0007).
            HeightTex = new Texture2D(res, res, TextureFormat.R16, mipChain: false, linear: true)
            {
                name = "ClipmapV3_Height",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 0,
            };
            HeightTex.LoadRawTextureData(bytes);
            HeightTex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
        }

        material.SetTexture("_Height", HeightTex);
        material.SetFloat("_Half", half);
        material.SetFloat("_OX", ox);
        material.SetFloat("_OZ", oz);
        material.SetFloat("_Base", baseM);
        material.SetFloat("_ZMin", zmin);
        material.SetFloat("_Res", res);
        Listo = true;
        return true;
    }

    void OnDestroy()
    {
        if (Application.isPlaying && HeightTex != null) Destroy(HeightTex);
    }
}
