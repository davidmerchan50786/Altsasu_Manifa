// SistemaGrafitis.cs
// Graffitis políticos y pegatinas en fachadas de Alsasua.
// El jugador apunta con el spray y pinta en la superficie.
// Influye en el apoyo popular (SistemaApoyoPopular).

using System.Collections.Generic;
using UnityEngine;

public class SistemaGrafitis : SingletonMono<SistemaGrafitis>
{
    [Header("Texturas de pintadas (asignar en Inspector)")]
    public Texture2D[] texturasGraffiti;   // "Askatasuna", estrella ETA, "Gora Euskal Herria", etc.
    public Texture2D[] texturasEuskara;    // pintadas en euskara
    public Texture2D[] texturasSolidarias; // keffiyeh, solidaridad palestina
    public Texture2D[] texturasAntipolicia;// ACAB, "Policia" tachado, etc.
    public Texture2D[] texturasAdvertencia;// "Atencion" advertencias

    [Header("Pegatinas")]
    public Texture2D[] texturasPegatinas;  // pegatinas pequeñas en señales, farolas

    [Header("Parámetros de spray")]
    public float alcanceSpray = 4f;
    public float tamanoDecal  = 1.2f;
    [Range(0.05f, 0.5f)] public float opacidadBase = 0.9f;

    [Header("Apoyo al pintar")]
    public int apoyoPorPintada  = 3;
    public int apoyoPorPegatina = 1;

    // ── Estado ────────────────────────────────────────────────────────────
    readonly List<DecalData> _decals = new();
    bool _modoPintar;
    int  _tipoSeleccionado;
    SistemaApoyoPopular _apoyo;

    // ── Tipos de pintada ──────────────────────────────────────────────────
    public enum TipoPintada { Graffiti, Pegatina, Pancarta }
#pragma warning disable CS0414
    TipoPintada _tipoPintada = TipoPintada.Graffiti;
#pragma warning restore CS0414

    protected override void OnAwake() => _apoyo = SistemaApoyoPopular.Instance;

    void Update()
    {
        var kb2 = UnityEngine.InputSystem.Keyboard.current;
        var ms2 = UnityEngine.InputSystem.Mouse.current;
        // G = modo pintar (solo si no hay InputSystem bloqueando)
        if (kb2?.gKey.wasPressedThisFrame == true) ToggleModo();
        if (!_modoPintar) return;

        // Scroll = cambiar tipo de pintada
        if (ms2 != null)
        {
            float scroll = ms2.scroll.ReadValue().y;
            if (scroll != 0) CambiarTipo(scroll > 0 ? 1 : -1);
            if (ms2.leftButton.wasPressedThisFrame) IntentarPintar();
        }

        // Click derecho = pegatina
        if (ms2?.rightButton.wasPressedThisFrame == true) IntentarPegatina();
    }

    void ToggleModo()
    {
        _modoPintar = !_modoPintar;
        Debug.Log(_modoPintar ? "[Graffiti] Modo pintar ACTIVO — Click para pintar, Scroll para cambiar" : "[Graffiti] Modo pintar OFF");
    }

    void CambiarTipo(int dir)
    {
        if (texturasGraffiti == null || texturasGraffiti.Length == 0) return;
        _tipoSeleccionado = (_tipoSeleccionado + dir + texturasGraffiti.Length) % texturasGraffiti.Length;
    }

    void IntentarPintar()
    {
        var cam = Camera.main;
        if (cam == null) return;
        var ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
        if (!Physics.Raycast(ray, out var hit, alcanceSpray)) return;

        // Solo en superficies de edificios, muros, carreteras
        var go = hit.collider.gameObject;
        if (go.GetComponent<Terrain>() != null) return; // no en el terreno

        PegarDecal(hit, ElegirTexturaGraffiti(), tamanoDecal);

        // Apoyo popular
        _apoyo?.SumarApoyo(apoyoPorPintada, $"Pintada política en {go.name}");
        (ServiceLocator.Get<IEconomyService>() ?? (IEconomyService)GameManagerAltsasua.Instance)?.GanarDinero(10);
        OnPintadaRealizada?.Invoke();
    }

    void IntentarPegatina()
    {
        var cam = Camera.main;
        if (cam == null) return;
        var ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
        if (!Physics.Raycast(ray, out var hit, alcanceSpray * 0.7f)) return;

        if (texturasPegatinas != null && texturasPegatinas.Length > 0)
        {
            var tex = texturasPegatinas[Random.Range(0, texturasPegatinas.Length)];
            PegarDecal(hit, tex, tamanoDecal * 0.4f);
        }
        _apoyo?.SumarApoyo(apoyoPorPegatina, "Pegatina política");
        OnPegatinaPuesta?.Invoke();
    }

    void PegarDecal(RaycastHit hit, Texture2D tex, float size)
    {
        if (tex == null) return;

        // Crear quad decal en la superficie
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "Graffiti_Decal";
        Object.Destroy(go.GetComponent<MeshCollider>());

        // Posicionar ligeramente delante de la superficie
        go.transform.position = hit.point + hit.normal * 0.015f;
        go.transform.rotation = Quaternion.LookRotation(-hit.normal);
        go.transform.localScale = Vector3.one * size;

        // Material transparente con la textura del graffiti
        var mat = new Material(Shader.Find("Unlit/Transparent") ?? Shader.Find("Standard"));
        mat.mainTexture = tex;
        mat.color = new Color(1, 1, 1, opacidadBase);
        if (mat.HasProperty("_Mode")) { mat.SetFloat("_Mode", 2); mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); }
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // Parentar al objeto golpeado para que se mueva con él
        go.transform.SetParent(hit.collider.transform);

        _decals.Add(new DecalData { go = go, creado = Time.time });

        // Limpiar decals viejos (max 200)
        if (_decals.Count > 200)
        {
            Object.Destroy(_decals[0].go);
            _decals.RemoveAt(0);
        }
    }

    // Buffer reutilizable para evitar allocations en cada pintada
    readonly List<Texture2D> _bufferTexturas = new();

    Texture2D ElegirTexturaGraffiti()
    {
        _bufferTexturas.Clear();
        if (texturasGraffiti    != null) _bufferTexturas.AddRange(texturasGraffiti);
        if (texturasEuskara     != null) _bufferTexturas.AddRange(texturasEuskara);
        if (texturasSolidarias  != null) _bufferTexturas.AddRange(texturasSolidarias);
        if (texturasAntipolicia != null) _bufferTexturas.AddRange(texturasAntipolicia);
        if (_bufferTexturas.Count == 0)  return CreatePlaceholderTexture();
        return _bufferTexturas[Random.Range(0, _bufferTexturas.Count)];
    }

    Texture2D CreatePlaceholderTexture()
    {
        // Generar textura básica de graffiti proceduralmente
        var tex = new Texture2D(256, 256);
        // Fondo transparente
        for (int y = 0; y < 256; y++)
        for (int x = 0; x < 256; x++)
        {
            float dist = Vector2.Distance(new Vector2(x, y), new Vector2(128, 128));
            bool borde = dist > 80 && dist < 110;
            // Texto "ASKATASUNA" simplificado (líneas de color)
            bool esTexto = (x > 30 && x < 226 && y > 100 && y < 156 && (x % 20 < 14 || y % 12 < 8));
            Color c = borde ? new Color(0.9f, 0.1f, 0.1f, 0.9f)
                    : esTexto ? new Color(0.95f, 0.85f, 0.1f, 0.85f)
                    : Color.clear;
            tex.SetPixel(x, y, c);
        }
        tex.Apply();
        return tex;
    }

    struct DecalData { public GameObject go; public float creado; }

    // ── API pública ───────────────────────────────────────────────────────
    public bool EstaPintando  => _modoPintar;
    public int  TotalDecals   => _decals.Count;

    // Evento que las misiones pueden escuchar
    public static event System.Action OnPintadaRealizada;
    public static event System.Action OnPegatinaPuesta;
}
