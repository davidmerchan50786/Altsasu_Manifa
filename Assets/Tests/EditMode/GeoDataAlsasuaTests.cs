// Assets/Tests/EditMode/GeoDataAlsasuaTests.cs
// ─────────────────────────────────────────────────────────────────────────────
//  Tests EditMode del núcleo de georreferenciación (GeoDataAlsasua).
//  Verifican la corrección a UTM real isótropo (2026-06): ESCALA_UTM_X = 1,
//  conversiones UTM↔Unity exactas e inversas, offsets OSM y utilidades.
//
//  Ejecutar: Window ▸ General ▸ Test Runner ▸ EditMode ▸ Run All.
//
//  Escenarios cubiertos (resumen):
//   Happy path : origen→Herriko Plaza, HerrikoPlaza en (OX,0,OZ), AlturaEdificio
//                con height, OSMaUnity, Dist2D plano.
//   Isotropía  : ESCALA_UTM_X==1 y 1 ud = 1 m igual en X y Z.
//   Edge cases : ida-vuelta a 1-3 km del centro, AlturaEdificio sin height,
//                AlturaEdificio con niveles 0/negativos, Dist2D ignorando Y.
//   Errores    : AlturaEdificio nunca por debajo de una planta (degradación
//                controlada, sin excepción) ante entradas inválidas.
//   Integración: AlturaTerreno delega en ITerrainService (fake inyectado) y, sin
//                servicio listo ni terreno, devuelve el fallback seguro (ALT_FALLBACK).
// ─────────────────────────────────────────────────────────────────────────────
using NUnit.Framework;
using UnityEngine;

[TestFixture]
public class GeoDataAlsasuaTests
{
    const float  TOL = 0.001f;   // tolerancia en metros para floats de Unity
    const double TOL_UTM = 0.01; // tolerancia en metros para el espacio UTM (double)

    // ── Happy path ────────────────────────────────────────────────────────────

    [Test]
    public void UTMaUnity_en_el_origen_devuelve_HerrikoPlaza()
    {
        Vector2 p = GeoDataAlsasua.UTMaUnity(GeoDataAlsasua.UTM_E_ORIGIN,
                                             GeoDataAlsasua.UTM_N_ORIGIN);
        Assert.AreEqual(GeoDataAlsasua.OX, p.x, TOL); // p.x = Unity X
        Assert.AreEqual(GeoDataAlsasua.OZ, p.y, TOL); // p.y = Unity Z
    }

    [Test]
    public void HerrikoPlaza_esta_en_OX_OZ_con_Y_cero()
    {
        Vector3 h = GeoDataAlsasua.HerrikoPlaza;
        Assert.AreEqual(GeoDataAlsasua.OX, h.x, TOL);
        Assert.AreEqual(0f, h.y, TOL);
        Assert.AreEqual(GeoDataAlsasua.OZ, h.z, TOL);
    }

    [Test]
    public void OSMaUnity_suma_el_offset_y_deja_Y_en_cero()
    {
        // Vértice relativo real de la iglesia (buildings_unity.json).
        Vector3 v = GeoDataAlsasua.OSMaUnity(-54.25f, -331.66f);
        Assert.AreEqual(GeoDataAlsasua.OX - 54.25f, v.x, TOL);
        Assert.AreEqual(0f, v.y, TOL);
        Assert.AreEqual(GeoDataAlsasua.OZ - 331.66f, v.z, TOL);
    }

    // ── Isotropía: la corrección de hoy (escalaX = 1, 1 ud = 1 m) ─────────────

    [Test]
    public void Escala_X_es_1_isotropa()
    {
        Assert.AreEqual(1f, GeoDataAlsasua.ESCALA_UTM_X, 1e-6f,
            "ESCALA_UTM_X debe ser 1 tras pasar a UTM real isótropo.");
    }

    [Test]
    public void Un_metro_real_es_una_unidad_igual_en_X_y_en_Z()
    {
        Vector2 este  = GeoDataAlsasua.UTMaUnity(GeoDataAlsasua.UTM_E_ORIGIN + 100.0,
                                                 GeoDataAlsasua.UTM_N_ORIGIN);
        Vector2 norte = GeoDataAlsasua.UTMaUnity(GeoDataAlsasua.UTM_E_ORIGIN,
                                                 GeoDataAlsasua.UTM_N_ORIGIN + 100.0);
        float dx = este.x  - GeoDataAlsasua.OX; // desplazamiento en X por 100 m al este
        float dz = norte.y - GeoDataAlsasua.OZ; // desplazamiento en Z por 100 m al norte
        Assert.AreEqual(100f, dx, TOL, "100 m al este deben ser 100 ud en X.");
        Assert.AreEqual(100f, dz, TOL, "100 m al norte deben ser 100 ud en Z.");
        Assert.AreEqual(dx, dz, TOL, "La escala debe ser idéntica en ambos ejes (isótropa).");
    }

    // ── Edge cases: ida-vuelta exacta a varias distancias del centro ──────────

    [TestCase(567951.0, 4749902.0)] // Herriko Plaza (origen)
    [TestCase(568951.0, 4750902.0)] // +1 km E, +1 km N
    [TestCase(566000.0, 4748000.0)] // suroeste lejano
    [TestCase(570000.0, 4752000.0)] // noreste lejano
    public void UnityAUTM_es_la_inversa_exacta_de_UTMaUnity(double e, double n)
    {
        Vector2 u = GeoDataAlsasua.UTMaUnity(e, n);
        GeoDataAlsasua.UnityAUTM(u.x, u.y, out double e2, out double n2);
        Assert.AreEqual(e, e2, TOL_UTM, "Easting ida-vuelta.");
        Assert.AreEqual(n, n2, TOL_UTM, "Northing ida-vuelta.");
    }

    // ── AlturaEdificio ────────────────────────────────────────────────────────

    [Test]
    public void AlturaEdificio_usa_la_altura_explicita_si_es_positiva()
    {
        Assert.AreEqual(6.4f, GeoDataAlsasua.AlturaEdificio(2, 6.4f), TOL);
    }

    [Test]
    public void AlturaEdificio_usa_los_niveles_cuando_no_hay_altura()
    {
        Assert.AreEqual(3 * GeoDataAlsasua.ALT_PLANTA,
                        GeoDataAlsasua.AlturaEdificio(3, 0f), TOL);
    }

    [Test]
    public void AlturaEdificio_nunca_devuelve_menos_de_una_planta()
    {
        // Entradas inválidas → degradación controlada (mínimo una planta), sin excepción.
        Assert.AreEqual(GeoDataAlsasua.ALT_PLANTA, GeoDataAlsasua.AlturaEdificio(0, 0f), TOL);
        Assert.That(GeoDataAlsasua.AlturaEdificio(-5, -1f),
                    Is.GreaterThanOrEqualTo(GeoDataAlsasua.ALT_PLANTA));
    }

    // ── Dist2D ────────────────────────────────────────────────────────────────

    [Test]
    public void Dist2D_calcula_distancia_plana_ignorando_la_altura()
    {
        Vector3 a = new Vector3(0f, 100f, 0f);
        Vector3 b = new Vector3(3f, -50f, 4f); // dx=3, dz=4 → 5 ; la Y se ignora
        Assert.AreEqual(5f, GeoDataAlsasua.Dist2D(a, b), TOL);
    }

    // ── Sanidad de constantes ─────────────────────────────────────────────────

    [Test]
    public void Constantes_de_origen_y_cota_son_las_esperadas()
    {
        Assert.AreEqual(567951.0,  GeoDataAlsasua.UTM_E_ORIGIN, 1e-6);
        Assert.AreEqual(4749902.0, GeoDataAlsasua.UTM_N_ORIGIN, 1e-6);
        Assert.AreEqual(1918f, GeoDataAlsasua.OX, 1e-6f);
        Assert.AreEqual(8570f, GeoDataAlsasua.OZ, 1e-6f);
        Assert.That(GeoDataAlsasua.COTA_PLAZA,
                    Is.InRange(GeoDataAlsasua.Z_MIN, GeoDataAlsasua.Z_MAX),
                    "La cota de Herriko Plaza debe caer dentro del rango LIDAR del terreno.");
    }

    // ── Integración: AlturaTerreno respeta el contrato ITerrainService ────────

    /// <summary>Terreno falso: AlturaMundo devuelve una cota fija conocida.</summary>
    class FakeTerrainService : ITerrainService
    {
        readonly float _altura;
        public FakeTerrainService(float altura, bool listo) { _altura = altura; EstaListo = listo; }
        public EstadoTerreno Estado => EstaListo ? EstadoTerreno.Listo : EstadoTerreno.Inicializando;
        public FuenteTerreno Fuente => FuenteTerreno.Plano;
        public Terrain Terreno => null;
        public bool EstaListo { get; }
        public float AlturaMundo(Vector3 posicionMundo) => _altura;
        public Terrain TerrainEn(Vector3 posicionMundo) => null;
        public System.Collections.Generic.IReadOnlyList<Terrain> Tiles => System.Array.Empty<Terrain>();
        public bool EsMosaico => false;
    }

    // ServiceLocator es estático y vive toda la sesión del editor: aislar cada test.
    [SetUp]
    public void Aislar() { ServiceLocator.Desregistrar<ITerrainService>(); GeoDataAlsasua.InvalidarCache(); }

    [TearDown]
    public void Limpiar() { ServiceLocator.Desregistrar<ITerrainService>(); GeoDataAlsasua.InvalidarCache(); }

    [Test]
    public void AlturaTerreno_delega_en_ITerrainService_cuando_esta_listo()
    {
        const float cota = 20.61f; // ≈ COTA_PLAZA - Z_MIN (altura Unity de Herriko Plaza)
        ServiceLocator.Registrar<ITerrainService>(new FakeTerrainService(cota, listo: true));
        GeoDataAlsasua.InvalidarCache();
        Assert.AreEqual(cota,
            GeoDataAlsasua.AlturaTerreno(GeoDataAlsasua.OX, GeoDataAlsasua.OZ), TOL);
    }

    [Test]
    public void AlturaTerreno_ignora_el_servicio_si_no_esta_listo()
    {
        ServiceLocator.Registrar<ITerrainService>(new FakeTerrainService(999f, listo: false));
        GeoDataAlsasua.InvalidarCache();
        // Servicio no listo, sin Terrain activo ni colisión → fallback seguro (sin excepción).
        Assert.AreEqual(GeoDataAlsasua.ALT_FALLBACK,
            GeoDataAlsasua.AlturaTerreno(GeoDataAlsasua.OX, GeoDataAlsasua.OZ), TOL);
    }

    [Test]
    public void AlturaTerreno_devuelve_fallback_seguro_sin_servicio_ni_terreno()
    {
        // Nada registrado: la altura es siempre consultable (degradación controlada).
        Assert.AreEqual(GeoDataAlsasua.ALT_FALLBACK,
            GeoDataAlsasua.AlturaTerreno(123f, 456f), TOL);
    }
}
