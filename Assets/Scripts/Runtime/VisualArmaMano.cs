// Assets/Scripts/Runtime/VisualArmaMano.cs
// Cuelga el MODELO del arma activa del hueso RightHand del personaje humanoide
// (PlayerArmature) y lo intercambia cuando SistemaArmasExtendido cambia de arma.
//
// Antes no había NINGÚN modelo de arma en la mano: SistemaArmasExtendido solo
// llevaba el tipo/munición y SistemaDisparo lanzaba el raycast desde una posición
// calculada en el aire. El cuerpo procedural viejo tenía un rifle de primitivas;
// con PlayerArmature desapareció. Este componente lo recupera con modelos reales
// (Resources/Armas/*) y placeholders procedurales para las armas sin modelo.
//
// ControladorJugador lo añade automáticamente al jugador (junto a SistemaArmasExtendido).
// Construcción perezosa: espera a que exista el Animator humanoide (el modelo se
// instancia en ControladorJugador.Start, después de los Awake).

using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class VisualArmaMano : MonoBehaviour
{
    [System.Serializable]
    public class Config
    {
        public SistemaArmasExtendido.TipoArma tipo;
        [Tooltip("Ruta Resources del modelo. Vacío = placeholder procedural (ver kindProc).")]
        public string recurso = "";
        [Tooltip("Tipo de placeholder procedural si 'recurso' está vacío.")]
        public string kindProc = "";       // spray | tirachinas | escopeta | bandera
        [Tooltip("El lado mayor del modelo se escala hasta medir esto (m). 0 = no reescalar.")]
        public float largoObjetivo = 0.3f;
        [Tooltip("Offset local respecto al hueso de la mano (tras recentrar el modelo).")]
        public Vector3 posMano = Vector3.zero;
        [Tooltip("Rotación local en la mano (grados).")]
        public Vector3 eulerMano = Vector3.zero;
        [Tooltip("Pegar a la mano izquierda en vez de la derecha.")]
        public bool manoIzquierda = false;
    }

    [SerializeField] private Config[] configs;   // vacío → se rellena con DefaultsCfg()

    private SistemaArmasExtendido _armas;
    private Animator _anim;
    private Transform _manoDer, _manoIzq;
    private readonly Dictionary<SistemaArmasExtendido.TipoArma, GameObject> _modelos =
        new Dictionary<SistemaArmasExtendido.TipoArma, GameObject>();
    private readonly List<Material> _mats = new List<Material>();
    private SistemaArmasExtendido.TipoArma _visible = (SistemaArmasExtendido.TipoArma)(-1);
    private bool _construido;
    private float _reintentar;

    private void Awake()
    {
        _armas = GetComponent<SistemaArmasExtendido>();
        if (configs == null || configs.Length == 0) configs = DefaultsCfg();
    }

    private void Update()
    {
        if (!_construido)
        {
            _reintentar -= Time.deltaTime;
            if (_reintentar <= 0f) { _reintentar = 0.5f; TryConstruir(); }
            return;
        }
        if (_armas != null && _armas.armaActual != _visible)
            MostrarSolo(_armas.armaActual);
    }

    // ── Construcción perezosa ────────────────────────────────────────────────
    private void TryConstruir()
    {
        if (_anim == null || !_anim.isHuman)
        {
            // Usar el Animator del MODELO instanciado (_PersonajeMixamo), NO el de la raíz:
            // Jugador_Altsasua es variante del FBX del Guardia Civil y arrastra su propio
            // Animator/esqueleto humanoide OCULTO; colgar el arma de ahí la pondría en una
            // mano invisible que no coincide con PlayerArmature.
            var modeloRoot = transform.Find("_PersonajeMixamo");
            if (modeloRoot == null) return;                // modelo aún no instanciado
            _anim = modeloRoot.GetComponent<Animator>() ?? modeloRoot.GetComponentInChildren<Animator>();
            if (_anim == null || !_anim.isHuman) return;   // aún no hay modelo humanoide
        }
        _manoDer = _anim.GetBoneTransform(HumanBodyBones.RightHand);
        _manoIzq = _anim.GetBoneTransform(HumanBodyBones.LeftHand);
        if (_manoDer == null) return;

        foreach (var c in configs)
        {
            if (_modelos.ContainsKey(c.tipo)) continue;
            var modelo = CrearModelo(c);
            if (modelo == null) continue;

            var mano = (c.manoIzquierda && _manoIzq != null) ? _manoIzq : _manoDer;

            // Holder: desacopla el pivot del modelo del punto de agarre.
            var holder = new GameObject("Arma_" + c.tipo);
            holder.transform.SetParent(mano, false);
            modelo.transform.SetParent(holder.transform, false);
            modelo.transform.localPosition = Vector3.zero;
            modelo.transform.localRotation = Quaternion.identity;

            EscalarYRecentrar(holder.transform, modelo, c.largoObjetivo);

            holder.transform.localPosition = c.posMano;
            holder.transform.localEulerAngles = c.eulerMano;

            PrepararRender(holder);
            holder.SetActive(false);
            _modelos[c.tipo] = holder;
        }

        _construido = true;
        MostrarSolo(_armas != null ? _armas.armaActual : SistemaArmasExtendido.TipoArma.Puños);
        AlsasuaLogger.Info("ArmaMano", $"Armas en mano construidas: {_modelos.Count} modelos.");
    }

    // Escala el modelo para que su lado mayor mida 'largo' y lo recentra en el holder.
    private void EscalarYRecentrar(Transform holder, GameObject modelo, float largo)
    {
        var b = BoundsDe(modelo);
        if (largo > 0f && b.size != Vector3.zero)
        {
            float maxDim = Mathf.Max(b.size.x, b.size.y, b.size.z);
            if (maxDim > 1e-4f)
                modelo.transform.localScale *= largo / maxDim;
        }
        // Recentrar: el centro de los bounds (ya escalado) al origen del holder.
        b = BoundsDe(modelo);
        Vector3 centroLocal = holder.InverseTransformPoint(b.center);
        modelo.transform.localPosition -= centroLocal;
    }

    private static Bounds BoundsDe(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        var b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return b;
    }

    // Desactiva colisiones/físicas y mete el arma en Ignore Raycast (que el propio
    // raycast de disparo no impacte en el arma que el jugador sostiene).
    private void PrepararRender(GameObject root)
    {
        foreach (var col in root.GetComponentsInChildren<Collider>(true)) col.enabled = false;
        foreach (var rb in root.GetComponentsInChildren<Rigidbody>(true)) { rb.isKinematic = true; rb.detectCollisions = false; }
        int ignoreRaycast = 2;
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = ignoreRaycast;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            r.receiveShadows = true;
        }
    }

    private void MostrarSolo(SistemaArmasExtendido.TipoArma tipo)
    {
        foreach (var kv in _modelos)
            if (kv.Value != null) kv.Value.SetActive(kv.Key == tipo);
        _visible = tipo;
    }

    // ── Creación de modelos (real Resources o placeholder procedural) ────────
    private GameObject CrearModelo(Config c)
    {
        if (!string.IsNullOrEmpty(c.recurso))
        {
            var pf = Resources.Load<GameObject>(c.recurso);
            if (pf != null) return Instantiate(pf);
            AlsasuaLogger.Warn("ArmaMano", $"No se cargó Resources/{c.recurso} para {c.tipo} — uso placeholder.");
        }
        return CrearProcedural(c.kindProc, c.tipo);
    }

    private Material Mat(Color col)
    {
        var sh = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
        var m = new Material(sh) { color = col };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
        _mats.Add(m);
        return m;
    }

    private GameObject Prim(Transform padre, PrimitiveType tipo, Vector3 lp, Vector3 ls, Vector3 euler, Color col)
    {
        var go = GameObject.CreatePrimitive(tipo);
        var cc = go.GetComponent<Collider>(); if (cc != null) Destroy(cc);
        go.transform.SetParent(padre, false);
        go.transform.localPosition = lp;
        go.transform.localEulerAngles = euler;
        go.transform.localScale = ls;
        go.GetComponent<Renderer>().material = Mat(col);
        return go;
    }

    private GameObject CrearProcedural(string kind, SistemaArmasExtendido.TipoArma tipo)
    {
        var raiz = new GameObject("Proc_" + tipo);
        Color metal  = new Color(0.18f, 0.18f, 0.20f);
        Color madera = new Color(0.35f, 0.22f, 0.12f);
        switch (kind)
        {
            case "spray":   // bote de spray
                Prim(raiz.transform, PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.05f, 0.08f, 0.05f), Vector3.zero, new Color(0.2f,0.5f,0.8f));
                Prim(raiz.transform, PrimitiveType.Cylinder, new Vector3(0,0.09f,0), new Vector3(0.02f,0.02f,0.02f), Vector3.zero, Color.white);
                break;
            case "tirachinas":  // horquilla en Y
                Prim(raiz.transform, PrimitiveType.Cylinder, new Vector3(0,-0.05f,0), new Vector3(0.015f,0.06f,0.015f), Vector3.zero, madera);
                Prim(raiz.transform, PrimitiveType.Cylinder, new Vector3(-0.03f,0.05f,0), new Vector3(0.012f,0.06f,0.012f), new Vector3(0,0,25f), madera);
                Prim(raiz.transform, PrimitiveType.Cylinder, new Vector3( 0.03f,0.05f,0), new Vector3(0.012f,0.06f,0.012f), new Vector3(0,0,-25f), madera);
                break;
            case "escopeta":  // recortada simple
                Prim(raiz.transform, PrimitiveType.Cube, new Vector3(0,0,0.25f), new Vector3(0.05f,0.07f,0.5f), Vector3.zero, metal);
                Prim(raiz.transform, PrimitiveType.Cube, new Vector3(0,-0.06f,-0.05f), new Vector3(0.045f,0.12f,0.14f), new Vector3(12f,0,0), madera);
                Prim(raiz.transform, PrimitiveType.Cylinder, new Vector3(0,0.01f,0.55f), new Vector3(0.03f,0.18f,0.03f), new Vector3(90f,0,0), new Color(0.06f,0.06f,0.06f));
                break;
            case "bandera":  // ikurriña: asta + paño rojo con cruz blanca y aspa verde
                Prim(raiz.transform, PrimitiveType.Cylinder, new Vector3(0,0.5f,0), new Vector3(0.02f,0.6f,0.02f), Vector3.zero, madera);
                Prim(raiz.transform, PrimitiveType.Cube, new Vector3(0.32f,0.85f,0), new Vector3(0.6f,0.4f,0.01f), Vector3.zero, new Color(0.7f,0.05f,0.08f));   // paño rojo
                Prim(raiz.transform, PrimitiveType.Cube, new Vector3(0.32f,0.85f,-0.006f), new Vector3(0.6f,0.06f,0.012f), new Vector3(0,0,33f), new Color(0.05f,0.45f,0.12f)); // aspa verde
                Prim(raiz.transform, PrimitiveType.Cube, new Vector3(0.32f,0.85f,-0.006f), new Vector3(0.6f,0.06f,0.012f), new Vector3(0,0,-33f), new Color(0.05f,0.45f,0.12f));
                Prim(raiz.transform, PrimitiveType.Cube, new Vector3(0.32f,0.85f,-0.004f), new Vector3(0.6f,0.05f,0.011f), Vector3.zero, Color.white); // cruz blanca horiz
                Prim(raiz.transform, PrimitiveType.Cube, new Vector3(0.32f,0.85f,-0.004f), new Vector3(0.05f,0.4f,0.011f), Vector3.zero, Color.white);  // cruz blanca vert
                break;
            default:
                Destroy(raiz);
                return null;
        }
        return raiz;
    }

    private void OnDestroy()
    {
        for (int i = 0; i < _mats.Count; i++)
            if (_mats[i] != null) Destroy(_mats[i]);
        _mats.Clear();
    }

    // ── Configuración por defecto (en código → iterable sin tocar el Inspector) ──
    private static Config[] DefaultsCfg()
    {
        return new[]
        {
            // Modelos reales (Resources/Armas + Botella). Offsets/escala APROXIMADOS:
            // ajustar posMano/eulerMano/largoObjetivo tras ver el agarre en Play.
            new Config{ tipo=SistemaArmasExtendido.TipoArma.Fusil,    recurso="Armas/dragunov", largoObjetivo=0.95f, posMano=new Vector3(0,0,0.25f), eulerMano=new Vector3(0,90f,0) },
            new Config{ tipo=SistemaArmasExtendido.TipoArma.Pistola,  recurso="Armas/Pistol2",  largoObjetivo=0.22f, posMano=new Vector3(0,0,0.05f), eulerMano=new Vector3(0,90f,0) },
            new Config{ tipo=SistemaArmasExtendido.TipoArma.BombaLapa, recurso="Armas/Bomb",    largoObjetivo=0.16f, posMano=Vector3.zero,          eulerMano=Vector3.zero },
            new Config{ tipo=SistemaArmasExtendido.TipoArma.Molotov,  recurso="MueblesCiudad/Botella", largoObjetivo=0.26f, posMano=new Vector3(0,0.05f,0), eulerMano=new Vector3(10f,0,0) },
            // Placeholders procedurales:
            new Config{ tipo=SistemaArmasExtendido.TipoArma.Spray,        kindProc="spray",      largoObjetivo=0.16f, posMano=new Vector3(0,0,0.02f) },
            new Config{ tipo=SistemaArmasExtendido.TipoArma.Tirachinas,   kindProc="tirachinas", largoObjetivo=0.18f, posMano=new Vector3(0,0,0.03f) },
            new Config{ tipo=SistemaArmasExtendido.TipoArma.Escopeta,     kindProc="escopeta",   largoObjetivo=0.80f, posMano=new Vector3(0,0,0.2f), eulerMano=new Vector3(0,90f,0) },
            new Config{ tipo=SistemaArmasExtendido.TipoArma.BanderaEuskadi, kindProc="bandera",  largoObjetivo=1.10f, posMano=new Vector3(0,0,0.05f), eulerMano=new Vector3(0,0,-10f) },
            // Puños y CocheBomba: sin modelo en mano (manos vacías / se arma un coche).
        };
    }
}
