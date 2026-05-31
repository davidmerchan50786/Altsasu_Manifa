// Assets/Scripts/GestorZonasAlsasua.cs
// ═══════════════════════════════════════════════════════════════════════════
//  GESTOR DE ZONAS — Particionado geográfico de Alsasua
//
//  Define las 10 zonas geográficas del mapa basadas en GeoDataAlsasua.
//  Cada zona se convierte en un chunk independiente para SistemaChunks:
//  cuando el jugador se acerca, la zona se activa; al alejarse, se descarga.
//
//  Origen Unity (0,0,0) = Herriko Plaza. Escala: 1 unidad = 1 metro.
//
//  WORKFLOW DE DESARROLLO:
//    1. En Editor → Tools → Alsasua → Configurar Zonas del Mapa
//    2. Se crean 10 GOs en la jerarquía (Zone_01_HerrikoPlaza, etc.)
//    3. Cada zona empieza DESACTIVADA. Actívala manualmente para trabajar en ella.
//    4. En Play, SistemaChunks las activa según la proximidad del jugador.
//
//  Para añadir contenido a una zona:
//    · Arrastra tus GOs (edificios, props, NPCs) dentro del GO de la zona
//    · El streaming se encarga del resto
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

public class GestorZonasAlsasua : MonoBehaviour
{
    // ── Definición de las 10 zonas ────────────────────────────────────────────

    public static readonly DefinicionZona[] Zonas = new DefinicionZona[]
    {
        // ── ZONA 01: HERRIKO PLAZA / CENTRO HISTÓRICO ───────────────────────
        // El corazón del pueblo. Plaza de los Fueros, Ayuntamiento, Iglesia San Miguel.
        new DefinicionZona
        {
            Id             = 1,
            Nombre         = "HerrikoPlaza_Centro",
            NombreDisplay  = "Herriko Plaza · Centro Histórico",
            Centro         = new Vector3(0f, 0f, 0f),      // = GeoDataAlsasua.HerrikoPlaza
            RadioActivar   = 220f,
            RadioDesactivar= 300f,
            ColorGizmo     = new Color(1f, 0.85f, 0f),     // Amarillo → zona de máxima densidad
            Descripcion    = "Plaza de los Fueros, Ayuntamiento, Iglesia San Miguel, comercios"
        },

        // ── ZONA 02: BARRIO NORTE / COMISARÍA FORAL ────────────────────────
        // Zona residencial norte. Comisaría Policía Foral, calles Navarra norte.
        new DefinicionZona
        {
            Id             = 2,
            Nombre         = "BarrioNorte_PolForal",
            NombreDisplay  = "Barrio Norte · Comisaría Foral",
            Centro         = new Vector3(60f, 0f, 300f),   // ≈ GeoDataAlsasua.ComisariaPolForal
            RadioActivar   = 230f,
            RadioDesactivar= 310f,
            ColorGizmo     = new Color(0.3f, 0.8f, 1f),   // Azul → zona policial
            Descripcion    = "Comisaría Policía Foral, barrio residencial norte, salida hacia Arakil"
        },

        // ── ZONA 03: BARRIO SUR / GLORIETA N-1 ────────────────────────────
        // Entrada sur del pueblo. Glorieta N-1, Calle Navarra sur.
        new DefinicionZona
        {
            Id             = 3,
            Nombre         = "BarrioSur_GlorietaN1",
            NombreDisplay  = "Barrio Sur · Glorieta N-1",
            Centro         = new Vector3(50f, 0f, -280f),
            RadioActivar   = 230f,
            RadioDesactivar= 310f,
            ColorGizmo     = new Color(1f, 0.5f, 0.1f),   // Naranja → corredor de tráfico
            Descripcion    = "Glorieta sur N-1, Calle Navarra sur, entrada al casco"
        },

        // ── ZONA 04: CUARTEL GUARDIA CIVIL / ZONA SO CASCO ─────────────────
        // Barrio suroeste. Cuartel Guardia Civil en Calle Ameztia.
        new DefinicionZona
        {
            Id             = 4,
            Nombre         = "ZonaSO_CuartelGC",
            NombreDisplay  = "Zona SO · Cuartel Guardia Civil",
            Centro         = new Vector3(-220f, 0f, -270f), // ≈ GeoDataAlsasua.CuartelGuardiaCivil
            RadioActivar   = 230f,
            RadioDesactivar= 310f,
            ColorGizmo     = new Color(0f, 0.5f, 0f),      // Verde oscuro → zona militar
            Descripcion    = "Cuartel Guardia Civil (Calle Ameztia), barrio SO residencial"
        },

        // ── ZONA 05: ESTACIÓN DE TREN ──────────────────────────────────────
        // Estación Adif/Renfe. Nudos ferroviarios Madrid-Irún y Alsasua-Pamplona.
        new DefinicionZona
        {
            Id             = 5,
            Nombre         = "EstacionTren",
            NombreDisplay  = "Estación de Tren Alsasua",
            Centro         = new Vector3(-510f, 0f, -780f), // = GeoDataAlsasua.EstacionTren
            RadioActivar   = 350f,
            RadioDesactivar= 470f,
            ColorGizmo     = new Color(0.6f, 0.4f, 0.2f),  // Marrón → infraestructura ferroviaria
            Descripcion    = "Estación Adif/Renfe, andenes, vías, zona de maniobras"
        },

        // ── ZONA 06: POLÍGONO INDUSTRIAL ISASIA (SO) ───────────────────────
        // El mayor polígono. Zona de naves industriales al SO.
        new DefinicionZona
        {
            Id             = 6,
            Nombre         = "Poligono_Isasia",
            NombreDisplay  = "Polígono Industrial Isasia",
            Centro         = new Vector3(-1100f, 0f, -1445f), // = GeoDataAlsasua.PoligonoIsasia
            RadioActivar   = 450f,
            RadioDesactivar= 580f,
            ColorGizmo     = new Color(0.5f, 0.5f, 0.5f),   // Gris → industrial pesado
            Descripcion    = "Polígono Isasia, naves industriales, camiones de carga, grúas"
        },

        // ── ZONA 07: POLÍGONO INDUSTRIAL ONDARRIA (SE) ─────────────────────
        // Polígono sureste. Acceso por N-1 sur.
        new DefinicionZona
        {
            Id             = 7,
            Nombre         = "Poligono_Ondarria",
            NombreDisplay  = "Polígono Industrial Ondarria",
            Centro         = new Vector3(490f, 0f, -965f),  // = GeoDataAlsasua.PoligonoOndarria
            RadioActivar   = 380f,
            RadioDesactivar= 500f,
            ColorGizmo     = new Color(0.4f, 0.4f, 0.5f),  // Gris azulado → industrial
            Descripcion    = "Polígono Ondarria/Isustu, acceso N-1, furgonetas y tráfico pesado"
        },

        // ── ZONA 08: POLÍGONO IBARREA + MONTE ARTIA (O) ────────────────────
        // Zona industrial oeste + inicio del robledal del Monte Artia.
        new DefinicionZona
        {
            Id             = 8,
            Nombre         = "Ibarrea_MonteArtia",
            NombreDisplay  = "Polígono Ibarrea · Monte Artia",
            Centro         = new Vector3(-830f, 0f, 200f),  // ≈ GeoDataAlsasua.PoligonoIbarrea
            RadioActivar   = 380f,
            RadioDesactivar= 500f,
            ColorGizmo     = new Color(0.2f, 0.5f, 0.2f),  // Verde → transición urbano-natural
            Descripcion    = "Polígono Ibarrea, inicio del robledal Monte Artia, ribera Oyantzun"
        },

        // ── ZONA 09: ENTORNO NATURAL NORTE ─────────────────────────────────
        // Bosque y monte al norte del pueblo. Corredor del Bidasoa-Ebro.
        new DefinicionZona
        {
            Id             = 9,
            Nombre         = "EntornoNatural_Norte",
            NombreDisplay  = "Entorno Natural Norte · Pinar",
            Centro         = new Vector3(0f, 0f, 950f),    // = GeoDataAlsasua.Zonas[0].Centro
            RadioActivar   = 500f,
            RadioDesactivar= 650f,
            ColorGizmo     = new Color(0f, 0.8f, 0.3f),   // Verde brillante → naturaleza
            Descripcion    = "Pinar norte, corredor Bidasoa-Ebro, Collado Burunda, monte abierto"
        },

        // ── ZONA 10: PEÑA BLANCA / HAYEDO ARALAR (NE) ─────────────────────
        // Entorno natural noreste. Hayedo denso, piedemonte Aralar.
        new DefinicionZona
        {
            Id             = 10,
            Nombre         = "PenaBlanca_Aralar",
            NombreDisplay  = "Peña Blanca · Hayedo Aralar",
            Centro         = new Vector3(820f, 0f, 750f),  // ≈ GeoDataAlsasua.PenaBlanca
            RadioActivar   = 500f,
            RadioDesactivar= 660f,
            ColorGizmo     = new Color(0.1f, 0.6f, 0.2f), // Verde oscuro → hayedo denso
            Descripcion    = "Hayedo de Aralar, Peña Blanca/Askiz, pinar de repoblación este"
        },
    };

    // ─────────────────────────────────────────────────────────────────────────
    //  STRUCT DE DEFINICIÓN
    // ─────────────────────────────────────────────────────────────────────────

    [System.Serializable]
    public struct DefinicionZona
    {
        public int     Id;
        public string  Nombre;         // ID de código (sin espacios)
        public string  NombreDisplay;  // Nombre legible para el Inspector
        public Vector3 Centro;         // Coordenadas Unity (metros desde Herriko Plaza)
        public float   RadioActivar;   // Radio a partir del cual se activa el chunk
        public float   RadioDesactivar;// Radio a partir del cual se desactiva (histéresis)
        public Color   ColorGizmo;     // Color en Scene View para distinguir zonas
        public string  Descripcion;    // Qué contiene esta zona
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  RUNTIME — auto-registrar en SistemaChunks si no está configurado
    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        var chunks = GetComponent<SistemaChunks>();
        if (chunks == null)
        {
            AlsasuaLogger.Warn("ZonasAlsasua",
                "GestorZonasAlsasua necesita SistemaChunks en el mismo GO. " +
                "Usa Tools → Alsasua → Configurar Zonas del Mapa para el setup completo.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  GIZMOS — dibuja todas las zonas en el Scene View aunque no estén asignadas
    // ─────────────────────────────────────────────────────────────────────────

    private void OnDrawGizmos()
    {
        foreach (var z in Zonas)
        {
            // Disco de activación (translúcido)
            Gizmos.color = new Color(z.ColorGizmo.r, z.ColorGizmo.g, z.ColorGizmo.b, 0.08f);
            DibujarDiscoHorizontal(z.Centro, z.RadioActivar);

            // Disco de desactivación (más translúcido)
            Gizmos.color = new Color(z.ColorGizmo.r, z.ColorGizmo.g, z.ColorGizmo.b, 0.04f);
            DibujarDiscoHorizontal(z.Centro, z.RadioDesactivar);

            // Borde del radio de activación
            Gizmos.color = new Color(z.ColorGizmo.r, z.ColorGizmo.g, z.ColorGizmo.b, 0.6f);
            Gizmos.DrawWireSphere(z.Centro + Vector3.up * 0.5f, 6f); // marcador central

            // Etiqueta con el nombre (solo disponible con GizmoUtility en Editor)
        }
    }

    private static void DibujarDiscoHorizontal(Vector3 centro, float radio, int segmentos = 32)
    {
        float paso = 360f / segmentos;
        for (int i = 0; i < segmentos; i++)
        {
            float a1 = i * paso * Mathf.Deg2Rad;
            float a2 = (i + 1) * paso * Mathf.Deg2Rad;
            Vector3 p1 = centro + new Vector3(Mathf.Cos(a1) * radio, 0.3f, Mathf.Sin(a1) * radio);
            Vector3 p2 = centro + new Vector3(Mathf.Cos(a2) * radio, 0.3f, Mathf.Sin(a2) * radio);
            Gizmos.DrawLine(p1, p2);
        }
    }
}
