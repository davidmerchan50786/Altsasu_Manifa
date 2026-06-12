// Assets/Scripts/SistemaAssets.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SISTEMA DE ASSETS — carga centralizada desde Resources en runtime
//
//  Carga en Awake todos los prefabs y clips bajo Resources/Prefabs/ y
//  Resources/Audio/, y los expone por categoría para que el resto de
//  sistemas los usen sin strings ni rutas dispersas.
//
//  Prefabs disponibles tras Awake:
//    CivilesAleatorios()    → NPC_Civil_* (8 variantes)
//    CochesAleatorios()     → Trabant, Hatchback, Sedan, Racing Cars (6)
//    AnimalAleatorio()      → wolf, dog, rabbit, deer, Oveja, Conejo (6)
//    PrefabNPC(nombre)      → por nombre exacto
//
//  Audio (complementa a AudioManager.GenerarClipsSinteticos):
//    ClipDisparo()          → TMM real si existe, sintético si no
//    ClipMotor()            → TMM real si existe, sintético si no
//    ClipsAmbiente()        → field recordings TMM
//
//  Uso:
//    var prefab = SistemaAssets.Instance.CivilesAleatorios();
//    Instantiate(prefab);
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using System.Collections.Generic;

[DefaultExecutionOrder(-200)]   // antes que cualquier sistema que necesite assets
public class SistemaAssets : MonoBehaviour
{
    public static SistemaAssets Instance { get; private set; }

    // ── Catálogos en memoria ──────────────────────────────────────────────
    readonly List<GameObject> _civiles      = new();
    readonly List<GameObject> _guardias     = new();
    readonly List<GameObject> _coches       = new();
    readonly List<GameObject> _animales     = new();
    readonly List<GameObject> _fuego         = new();
    readonly List<GameObject> _rocas         = new();
    readonly List<GameObject> _propsUrbanos  = new();
    readonly List<GameObject> _propsCalle    = new();
    readonly List<GameObject> _mobiliario   = new();
    readonly List<GameObject> _arboles       = new();
    readonly List<GameObject> _arbolesPais   = new();
    readonly List<GameObject> _arbustos      = new();
    readonly List<GameObject> _edificiosAlp  = new();
    readonly List<Texture>    _hdris         = new();

    AudioClip _clipDisparo;
    AudioClip _clipMotor;
    readonly List<AudioClip> _clipsAmbiente = new();
    readonly List<AudioClip> _clipsFuego    = new();

    // ════════════════════════════════════════════════════════════════════════

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        CargarTodo();
    }

    void CargarTodo()
    {
        // ── Personajes ────────────────────────────────────────────────────
        foreach (var p in Resources.LoadAll<GameObject>("Prefabs/NPCs"))
        {
            // Guardia Civil por nombre; TODO lo demás en NPCs/ se trata como civil.
            // Folder-based (no exige prefijo "NPC_Civil_"), así cualquier modelo
            // humano nuevo que se deje en NPCs/ (p.ej. civilian_girl) entra como civil.
            if (p.name.Contains("GuardiaCivil") || p.name.Contains("GC_")) _guardias.Add(p);
            else                                                            _civiles.Add(p);
        }

        // ── Vehículos ─────────────────────────────────────────────────────
        foreach (var p in Resources.LoadAll<GameObject>("Prefabs/Coches"))
            _coches.Add(p);

        // ── Fauna ─────────────────────────────────────────────────────────
        foreach (var p in Resources.LoadAll<GameObject>("Prefabs/Animales"))
            _animales.Add(p);

        // ── Efectos de fuego (Vefects Free Fire VFX) ─────────────────────
        foreach (var p in Resources.LoadAll<GameObject>("Prefabs/Fuego"))
            _fuego.Add(p);

        // ── Rocas HD ──────────────────────────────────────────────────────
        foreach (var p in Resources.LoadAll<GameObject>("Prefabs/Rocas"))
            _rocas.Add(p);

        // ── Props urbanos (vallas, contenedores) ──────────────────────────
        foreach (var p in Resources.LoadAll<GameObject>("Prefabs/Props/Urbano"))
            _propsUrbanos.Add(p);

        // ── Árboles, arbustos y árboles autóctonos del País Vasco ────────
        foreach (var p in Resources.LoadAll<GameObject>("Prefabs/Arboles"))
            _arboles.Add(p);
        foreach (var p in Resources.LoadAll<GameObject>("Prefabs/ArbolesPais"))
            _arbolesPais.Add(p);
        foreach (var p in Resources.LoadAll<GameObject>("Prefabs/Vegetacion"))
        {
            string n = p.name.ToLower();
            if (n.Contains("bush") || n.Contains("arbusto")) _arbustos.Add(p);
            else _arboles.Add(p);
        }

        // ── Props de calle (farolas, vallas) ──────────────────────────────
        foreach (var p in Resources.LoadAll<GameObject>("Prefabs/Props/Calle"))
            _propsCalle.Add(p);

        // ── Mobiliario urbano (Polygon City + PolyHaven) ──────────────────
        foreach (var p in Resources.LoadAll<GameObject>("Prefabs/Mobiliario"))
            _mobiliario.Add(p);

        // ── Props sueltos en Resources/Props (barriles, vallas, rocas, bancos,
        //    farola) que no estaban enrutados a ningún catálogo. Se reparten por
        //    tipo para dar variedad a rocas/barricadas/mobiliario/calle. ─────────
        foreach (var p in Resources.LoadAll<GameObject>("Props"))
        {
            string n = p.name.ToLower();
            if (n.Contains("rock") || n.Contains("roca"))       _rocas.Add(p);
            else if (n.Contains("fence") || n.Contains("valla")
                  || n.Contains("barrel") || n.Contains("barril")) _propsUrbanos.Add(p);
            else if (n.Contains("lamp") || n.Contains("post") || n.Contains("farola")) _propsCalle.Add(p);
            else if (n.Contains("bench") || n.Contains("banco"))  _mobiliario.Add(p);
            // (RPG_Rocket u otros proyectiles se ignoran: no son decoración)
        }

        // ── Edificios ALP (casas vascas) ──────────────────────────────────
        foreach (var p in Resources.LoadAll<GameObject>("Prefabs/Edificios"))
            _edificiosAlp.Add(p);

        // ── HDRIs para cielo reactivo ─────────────────────────────────────
        foreach (var t in Resources.LoadAll<Texture>("HDRIs"))
            _hdris.Add(t);

        // ── Audio ─────────────────────────────────────────────────────────
        _clipDisparo = Resources.Load<AudioClip>("Audio/SFX/disparo_real");
        _clipMotor   = Resources.Load<AudioClip>("Audio/SFX/motor_real");
        foreach (var c in Resources.LoadAll<AudioClip>("Audio/Ambiente")) _clipsAmbiente.Add(c);
        foreach (var c in Resources.LoadAll<AudioClip>("Audio/Fuego"))    _clipsFuego.Add(c);

        AlsasuaLogger.Info("SistemaAssets",
            $"Cargado: {_civiles.Count} civiles · {_coches.Count} coches · {_animales.Count} animales" +
            $" · {_fuego.Count} fuego · {_rocas.Count} rocas · {_propsUrbanos.Count} props" +
            $" · {_arboles.Count} árboles · {_clipsAmbiente.Count + _clipsFuego.Count} clips audio");

        // Auto-asignar a sistemas que tienen SerializeFields vacíos
        AutoAsignarSistemas();
    }

    /// <summary>
    /// Empuja los prefabs cargados a sistemas que los necesitan como SerializeField
    /// pero que probablemente no tienen nada asignado en el Inspector.
    /// </summary>
    void AutoAsignarSistemas()
    {
        // PoliciaForalIA — modelo visual real (NPC_GuardiaCivil)
        AutoAsignarPoliciaModelos();

        // AlsasuaTreeStreamer — árboles autóctonos vascos
        AutoAsignarTreeStreamer();

        // SistemaManifestacion — fuego en barricadas
        var manifa = SistemaManifestacion.Instance;
        if (manifa != null && _fuego.Count > 0)
        {
            var fuegoPeq = FuegoPequeno();
            if (fuegoPeq != null)
            {
                var field = typeof(SistemaManifestacion)
                    .GetField("prefabFuegoPequeño",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (field != null && field.GetValue(manifa) == null)
                    field.SetValue(manifa, fuegoPeq);
            }
        }

        // ConfiguradorAssetsAAA — prefabFuego + prefabsExplosion[] desde Resources
        var cfg = ConfiguradorAssetsAAA.Instance;
        if (cfg != null)
        {
            var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;

            // prefabFuego
            if (_fuego.Count > 0)
            {
                var f = typeof(ConfiguradorAssetsAAA).GetField("prefabFuego", flags);
                if (f != null && f.GetValue(cfg) == null) f.SetValue(cfg, FuegoGrande());
            }

            // prefabsExplosion[] — cargar desde Resources/Efectos
            var fExplo = typeof(ConfiguradorAssetsAAA).GetField("prefabsExplosion", flags);
            if (fExplo != null && (fExplo.GetValue(cfg) as GameObject[])?.Length == 0)
            {
                var exploders = Resources.LoadAll<GameObject>("Efectos");
                var lista = new System.Collections.Generic.List<GameObject>();
                foreach (var p in exploders)
                    if (p.name.StartsWith("Explosion")) lista.Add(p);
                if (lista.Count > 0) fExplo.SetValue(cfg, lista.ToArray());
                AlsasuaLogger.Info("SistemaAssets", $"prefabsExplosion auto-cargados: {lista.Count}");
            }

            // prefabExplosionAire
            var fAire = typeof(ConfiguradorAssetsAAA).GetField("prefabExplosionAire", flags);
            if (fAire != null && fAire.GetValue(cfg) == null)
            {
                var aire = Resources.Load<GameObject>("Efectos/Explosion_Air 1");
                if (aire != null) fAire.SetValue(cfg, aire);
            }
        }

        // SistemaDestruccion — fuego para Molotov, incendios de coches
        var destruccion = FindFirstObjectByType<SistemaDestruccion>();
        if (destruccion != null && _fuego.Count > 0)
        {
            var flags2 = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
            SetIfNull(destruccion, "prefabFuegoGrande", FuegoGrande(),  flags2);
            SetIfNull(destruccion, "prefabFuegoMedio",  FuegoMedio(),   flags2);
            SetIfNull(destruccion, "prefabFuegoChico",  FuegoPequeno(), flags2);
        }

        // SistemaArmasExtendido — prefabMolotov si no asignado
        var armas = FindFirstObjectByType<SistemaArmasExtendido>();
        if (armas != null && _fuego.Count > 0)
        {
            var flags3 = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
            SetIfNull(armas, "prefabMolotov", FuegoMedio(), flags3);
        }

        AlsasuaLogger.Info("SistemaAssets", "Auto-asignación de prefabs completada");
    }

    static void SetIfNull(object target, string fieldName, GameObject value,
        System.Reflection.BindingFlags flags)
    {
        if (value == null) return;
        var f = target.GetType().GetField(fieldName, flags);
        if (f != null && f.GetValue(target) == null) f.SetValue(target, value);
    }

    void AutoAsignarPoliciaModelos()
    {
        if (_guardias.Count == 0) return;
        var policia = FindObjectsByType<PoliciaForalIA>(FindObjectsSortMode.None);
        var fieldModelo = typeof(NPCBase)
            .GetField("prefabModelo", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (fieldModelo == null) return;

        int asignados = 0;
        foreach (var p in policia)
        {
            if (fieldModelo.GetValue(p) != null) continue;   // ya tiene modelo
            var gc = _guardias[asignados % _guardias.Count];
            fieldModelo.SetValue(p, gc);
            asignados++;
        }
        if (asignados > 0)
            AlsasuaLogger.Info("SistemaAssets", $"PoliciaForalIA: {asignados} modelos GC asignados");
    }

    void AutoAsignarTreeStreamer()
    {
        var streamer = FindFirstObjectByType<AlsasuaTreeStreamer>();
        if (streamer == null) return;

        var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
        var type  = typeof(AlsasuaTreeStreamer);

        // treePrefabs → árboles genéricos (Vegetacion + Arboles)
        var fieldGeneric = type.GetField("treePrefabs", flags);
        if (fieldGeneric != null && (fieldGeneric.GetValue(streamer) as GameObject[])?.Length == 0)
        {
            var genericos = new System.Collections.Generic.List<GameObject>(_arboles);
            genericos.AddRange(_arbustos);
            if (genericos.Count > 0) fieldGeneric.SetValue(streamer, genericos.ToArray());
        }

        // prefabsRoble → Haya_Europea, Roble_Ingles (laderas norte húmedas)
        var fieldRoble = type.GetField("prefabsRoble", flags);
        if (fieldRoble != null && (fieldRoble.GetValue(streamer) as GameObject[])?.Length == 0)
        {
            var robles = _arbolesPais.FindAll(p =>
                p.name.Contains("Haya") || p.name.Contains("Roble") || p.name.Contains("Avellano"));
            if (robles.Count > 0) fieldRoble.SetValue(streamer, robles.ToArray());
        }

        // prefabsPino → Pino_Baltico
        var fieldPino = type.GetField("prefabsPino", flags);
        if (fieldPino != null && (fieldPino.GetValue(streamer) as GameObject[])?.Length == 0)
        {
            var pinos = _arbolesPais.FindAll(p => p.name.Contains("Pino"));
            if (pinos.Count > 0) fieldPino.SetValue(streamer, pinos.ToArray());
        }

        // prefabsRibera → Aliso_Negro, Sauce_Caprio, Alamo_Templon (orillas del Arakil)
        var fieldRibera = type.GetField("prefabsRibera", flags);
        if (fieldRibera != null && (fieldRibera.GetValue(streamer) as GameObject[])?.Length == 0)
        {
            var ribera = _arbolesPais.FindAll(p =>
                p.name.Contains("Aliso") || p.name.Contains("Sauce") || p.name.Contains("Alamo"));
            if (ribera.Count > 0) fieldRibera.SetValue(streamer, ribera.ToArray());
        }

        AlsasuaLogger.Info("SistemaAssets",
            $"AlsasuaTreeStreamer: {_arbolesPais.Count} árboles vascos asignados por especie");
    }

    // ── API pública ───────────────────────────────────────────────────────

    public GameObject CivilAleatorio()
        => _civiles.Count > 0 ? _civiles[Random.Range(0, _civiles.Count)] : null;

    /// <summary>Modelo de Guardia Civil (NPC_GuardiaCivil / GC_*). Cae a civil si no hay.</summary>
    public GameObject GuardiaAleatorio()
        => _guardias.Count > 0 ? _guardias[Random.Range(0, _guardias.Count)] : CivilAleatorio();

    public int ContarGuardias() => _guardias.Count;

    public GameObject CocheAleatorio()
        => _coches.Count > 0 ? _coches[Random.Range(0, _coches.Count)] : null;

    public GameObject AnimalAleatorio()
        => _animales.Count > 0 ? _animales[Random.Range(0, _animales.Count)] : null;

    public GameObject AnimalPorNombre(string nombre)
        => _animales.Find(a => a.name.ToLower().Contains(nombre.ToLower()));

    public AudioClip ClipDisparoReal()   => _clipDisparo;
    public AudioClip ClipMotorReal()     => _clipMotor;
    public AudioClip ClipAmbienteAl()    => _clipsAmbiente.Count > 0
        ? _clipsAmbiente[Random.Range(0, _clipsAmbiente.Count)] : null;

    // ── Fuego ─────────────────────────────────────────────────────────────
    public GameObject FuegoGrande()  => Fuego("Big");
    public GameObject FuegoMedio()   => Fuego("Medium") ?? Fuego("01");
    public GameObject FuegoPequeno() => Fuego("Small") ?? Fuego("Simple");
    public AudioClip  ClipFuego()    => _clipsFuego.Count > 0
        ? _clipsFuego[Random.Range(0, _clipsFuego.Count)] : null;

    // ── Rocas HD ──────────────────────────────────────────────────────────
    public GameObject RocaAleatoria() => _rocas.Count > 0
        ? _rocas[Random.Range(0, _rocas.Count)] : null;

    // ── Props urbanos ─────────────────────────────────────────────────────
    public GameObject PropUrbanoAleatorio() => _propsUrbanos.Count > 0
        ? _propsUrbanos[Random.Range(0, _propsUrbanos.Count)] : null;

    // ── Árboles ───────────────────────────────────────────────────────────
    public GameObject ArbolAleatorio() => _arboles.Count > 0
        ? _arboles[Random.Range(0, _arboles.Count)] : null;
    public GameObject ArbustoAleatorio()    => _arbustos.Count > 0    ? _arbustos[Random.Range(0, _arbustos.Count)] : null;
    public GameObject ArbolPaisAleatorio()  => _arbolesPais.Count > 0 ? _arbolesPais[Random.Range(0, _arbolesPais.Count)] : null;
    public GameObject PropCalleAleatorio()  => _propsCalle.Count > 0  ? _propsCalle[Random.Range(0, _propsCalle.Count)] : null;
    public GameObject Farola()              => _propsCalle.Find(p => p.name.Contains("Lamp"))
                                              ?? _mobiliario.Find(p => p.name.ToLower().Contains("lamp") || p.name.ToLower().Contains("lantern"));
    public GameObject MobiliarioAleatorio() => _mobiliario.Count > 0 ? _mobiliario[Random.Range(0, _mobiliario.Count)] : null;
    public int ContarMobiliario()           => _mobiliario.Count;
    public GameObject EdicioAlpAleatorio()  => _edificiosAlp.Count > 0 ? _edificiosAlp[Random.Range(0, _edificiosAlp.Count)] : null;

    // HDRIs por clima
    public Texture HdriPorClima(SistemaClima.EstadoClima clima) => clima switch
    {
        SistemaClima.EstadoClima.Tormenta      => HdriBuscar("storm", "storm"),
        SistemaClima.EstadoClima.LluviaLigera  => HdriBuscar("rain", "cloudy", "overcast"),
        SistemaClima.EstadoClima.Niebla        => HdriBuscar("fog", "mist", "outskirts"),
        SistemaClima.EstadoClima.NieveLigera   => HdriBuscar("snow", "winter", "birch"),
        SistemaClima.EstadoClima.Sol           => HdriBuscar("sunset", "field", "autumn"),
        _                                      => HdriBuscar("field", "forest"),
    };

    public int ContarArbustos()      => _arbustos.Count;
    public int ContarArbolesPais()   => _arbolesPais.Count;
    public int ContarHdris()         => _hdris.Count;

    Texture HdriBuscar(params string[] keywords)
    {
        foreach (var kw in keywords)
            foreach (var h in _hdris)
                if (h.name.ToLower().Contains(kw)) return h;
        return _hdris.Count > 0 ? _hdris[0] : null;
    }

    public int ContarCiviles()      => _civiles.Count;
    public int ContarCoches()       => _coches.Count;
    public int ContarAnimales()     => _animales.Count;
    public int ContarFuego()        => _fuego.Count;
    public int ContarRocas()        => _rocas.Count;
    public int ContarPropsUrbanos() => _propsUrbanos.Count;

    GameObject Fuego(string keyword) =>
        _fuego.Find(p => p.name.Contains(keyword));
}
