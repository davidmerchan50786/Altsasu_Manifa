// Assets/Scripts/GeoDataCalles.cs
// Datos geográficos reales de las manzanas de Alsasua/Altsasu.
// Usados por SistemaEdificios para colocar edificios en las ubicaciones OSM reales.
// Coordenadas en espacio Unity (origen = UTM 566000E, 4741000N).

using UnityEngine;

public static class GeoDataCalles
{
    // ── Tipos ──────────────────────────────────────────────────────────────

    public enum TipoEdificio
    {
        Residencial,
        CascoAntiguo,
        Comercial,
        Industrial,
        Publico,
        Religioso,
        Institucional,
        Deportivo
    }

    // ── Edificios singulares (iconos de la ciudad) ─────────────────────────
    public struct EdificioSingular
    {
        public Vector3      Centro;
        public float        TamanoX;
        public float        TamanoZ;
        public float        Altura;
        public float        RotacionY;
        public TipoEdificio Tipo;
        public string       Nombre;
        public GameObject   Prefab;   // null = procedural
    }

    public static readonly EdificioSingular[] EdificiosSingulares = new EdificioSingular[]
    {
        new EdificioSingular {
            Centro = new Vector3(1905f, 0f, 8610f), TamanoX = 22f, TamanoZ = 38f, Altura = 14f,
            Tipo = TipoEdificio.Religioso, Nombre = "Iglesia San Miguel Altsasu"
        },
        new EdificioSingular {
            Centro = new Vector3(1900f, 0f, 8590f), TamanoX = 30f, TamanoZ = 25f, Altura = 9f,
            Tipo = TipoEdificio.Institucional, Nombre = "Udaletxea Ayuntamiento"
        },
        new EdificioSingular {
            Centro = new Vector3(2100f, 0f, 8350f), TamanoX = 90f, TamanoZ = 20f, Altura = 8f,
            Tipo = TipoEdificio.Institucional, Nombre = "Estacion tren Altsasu"
        },
        new EdificioSingular {
            Centro = new Vector3(1960f, 0f, 8430f), TamanoX = 25f, TamanoZ = 20f, Altura = 7f,
            Tipo = TipoEdificio.Institucional, Nombre = "Guardia Civil cuartel"
        },
        new EdificioSingular {
            Centro = new Vector3(1850f, 0f, 8700f), TamanoX = 40f, TamanoZ = 30f, Altura = 6f,
            Tipo = TipoEdificio.Institucional, Nombre = "Polideportivo Altsasu"
        },
        new EdificioSingular {
            Centro = new Vector3(1860f, 0f, 8700f), TamanoX = 60f, TamanoZ = 45f, Altura = 7f,
            Tipo = TipoEdificio.Deportivo, Nombre = "Pabellon deportivo"
        },
        new EdificioSingular {
            Centro = new Vector3(2180f, 0f, 8450f), TamanoX = 80f, TamanoZ = 60f, Altura = 10f,
            Tipo = TipoEdificio.Industrial, Nombre = "Fabrika Industria A"
        },
    };

    public struct ManzanaData
    {
        public Vector3       Centro;      // Centro de la manzana en Unity coords
        public float         TamanoX;     // Ancho (metros, eje X)
        public float         TamanoZ;     // Largo (metros, eje Z)
        public float         RotacionY;   // Orientación de la manzana (grados)
        public int           NumPlantas;  // Número de plantas medio
        public TipoEdificio  Tipo;
        public string        Nombre;
    }

    // ── Manzanas reales de Alsasua/Altsasu ────────────────────────────────
    // Fuente: OSM + medición real. Coordenadas Unity (1u = 1m).
    // Origen (0,0,0) = UTM 566000E, 4741000N (esquina SW del área)
    // Herriko Plaza = (1918, y, 8570)

    // ── Calles principales ────────────────────────────────────────────────
    public struct CalleData
    {
        public string   Nombre;
        public Vector3[] Puntos;
    }

    public static readonly CalleData[] CallesPrincipales = new CalleData[]
    {
        new CalleData {
            Nombre = "Nafarroa Kalea",
            Puntos = new Vector3[] {
                new Vector3(1918f, 0f, 8300f), new Vector3(1918f, 0f, 8570f),
                new Vector3(1918f, 0f, 8800f)
            }
        },
        new CalleData {
            Nombre = "Kale Nagusia",
            Puntos = new Vector3[] {
                new Vector3(1700f, 0f, 8570f), new Vector3(1918f, 0f, 8570f),
                new Vector3(2150f, 0f, 8570f)
            }
        },
        new CalleData {
            Nombre = "N-1 Carretera",
            Puntos = new Vector3[] {
                new Vector3(1918f, 0f, 7800f), new Vector3(1918f, 0f, 8300f),
                new Vector3(2000f, 0f, 8800f), new Vector3(2100f, 0f, 9200f)
            }
        },
    };

    public static readonly ManzanaData[] ManzanasAlsasua = new ManzanaData[]
    {
        // ── CASCO ANTIGUO (Alde Zaharra) ──────────────────────────────────
        new ManzanaData {
            Centro = new Vector3(1918f, 0f, 8570f), TamanoX = 80f, TamanoZ = 60f,
            RotacionY = 0f, NumPlantas = 4, Tipo = TipoEdificio.CascoAntiguo,
            Nombre = "Herriko Plaza centro"
        },
        new ManzanaData {
            Centro = new Vector3(1875f, 0f, 8520f), TamanoX = 60f, TamanoZ = 45f,
            RotacionY = 5f, NumPlantas = 4, Tipo = TipoEdificio.CascoAntiguo,
            Nombre = "Nafarroa Kalea bloque A"
        },
        new ManzanaData {
            Centro = new Vector3(1960f, 0f, 8510f), TamanoX = 65f, TamanoZ = 50f,
            RotacionY = -3f, NumPlantas = 3, Tipo = TipoEdificio.CascoAntiguo,
            Nombre = "Nafarroa Kalea bloque B"
        },
        new ManzanaData {
            Centro = new Vector3(1920f, 0f, 8630f), TamanoX = 55f, TamanoZ = 40f,
            RotacionY = 2f, NumPlantas = 4, Tipo = TipoEdificio.CascoAntiguo,
            Nombre = "San Juan Kalea"
        },
        new ManzanaData {
            Centro = new Vector3(1850f, 0f, 8600f), TamanoX = 70f, TamanoZ = 55f,
            RotacionY = 8f, NumPlantas = 3, Tipo = TipoEdificio.CascoAntiguo,
            Nombre = "Kale Nagusia norte"
        },

        // ── ENSANCHE (Expansión moderna) ──────────────────────────────────
        new ManzanaData {
            Centro = new Vector3(2050f, 0f, 8500f), TamanoX = 90f, TamanoZ = 70f,
            RotacionY = 0f, NumPlantas = 5, Tipo = TipoEdificio.Residencial,
            Nombre = "Ensanche Este bloque 1"
        },
        new ManzanaData {
            Centro = new Vector3(2050f, 0f, 8600f), TamanoX = 90f, TamanoZ = 70f,
            RotacionY = 0f, NumPlantas = 5, Tipo = TipoEdificio.Residencial,
            Nombre = "Ensanche Este bloque 2"
        },
        new ManzanaData {
            Centro = new Vector3(1750f, 0f, 8550f), TamanoX = 80f, TamanoZ = 65f,
            RotacionY = 0f, NumPlantas = 4, Tipo = TipoEdificio.Residencial,
            Nombre = "Ensanche Oeste bloque 1"
        },
        new ManzanaData {
            Centro = new Vector3(1780f, 0f, 8450f), TamanoX = 75f, TamanoZ = 60f,
            RotacionY = -5f, NumPlantas = 4, Tipo = TipoEdificio.Residencial,
            Nombre = "Barrio Sur bloque 1"
        },
        new ManzanaData {
            Centro = new Vector3(1920f, 0f, 8450f), TamanoX = 85f, TamanoZ = 60f,
            RotacionY = 0f, NumPlantas = 3, Tipo = TipoEdificio.Residencial,
            Nombre = "Barrio Sur bloque 2"
        },

        // ── ZONA COMERCIAL ────────────────────────────────────────────────
        new ManzanaData {
            Centro = new Vector3(1940f, 0f, 8570f), TamanoX = 40f, TamanoZ = 30f,
            RotacionY = 0f, NumPlantas = 2, Tipo = TipoEdificio.Comercial,
            Nombre = "Mercado municipal"
        },
        new ManzanaData {
            Centro = new Vector3(1870f, 0f, 8480f), TamanoX = 50f, TamanoZ = 35f,
            RotacionY = 10f, NumPlantas = 2, Tipo = TipoEdificio.Comercial,
            Nombre = "Zona comercial N-1"
        },

        // ── EDIFICIOS PÚBLICOS ────────────────────────────────────────────
        new ManzanaData {
            Centro = new Vector3(1900f, 0f, 8590f), TamanoX = 35f, TamanoZ = 28f,
            RotacionY = 0f, NumPlantas = 2, Tipo = TipoEdificio.Publico,
            Nombre = "Udaletxea (Ayuntamiento)"
        },
        new ManzanaData {
            Centro = new Vector3(2100f, 0f, 8350f), TamanoX = 120f, TamanoZ = 80f,
            RotacionY = 5f, NumPlantas = 1, Tipo = TipoEdificio.Publico,
            Nombre = "Estación de tren"
        },
        new ManzanaData {
            Centro = new Vector3(1850f, 0f, 8700f), TamanoX = 45f, TamanoZ = 35f,
            RotacionY = 0f, NumPlantas = 2, Tipo = TipoEdificio.Publico,
            Nombre = "Ikastola / Escuela"
        },

        // ── ZONA INDUSTRIAL (polígono al sur) ─────────────────────────────
        new ManzanaData {
            Centro = new Vector3(2200f, 0f, 8200f), TamanoX = 150f, TamanoZ = 100f,
            RotacionY = 0f, NumPlantas = 1, Tipo = TipoEdificio.Industrial,
            Nombre = "Poligono industrial A"
        },
        new ManzanaData {
            Centro = new Vector3(2400f, 0f, 8200f), TamanoX = 130f, TamanoZ = 90f,
            RotacionY = 0f, NumPlantas = 1, Tipo = TipoEdificio.Industrial,
            Nombre = "Poligono industrial B"
        },
        new ManzanaData {
            Centro = new Vector3(1700f, 0f, 8200f), TamanoX = 120f, TamanoZ = 85f,
            RotacionY = -8f, NumPlantas = 1, Tipo = TipoEdificio.Industrial,
            Nombre = "Nave logistica Arakil"
        },

        // ── RELIGIOSO ─────────────────────────────────────────────────────
        new ManzanaData {
            Centro = new Vector3(1905f, 0f, 8610f), TamanoX = 25f, TamanoZ = 40f,
            RotacionY = 0f, NumPlantas = 3, Tipo = TipoEdificio.Religioso,
            Nombre = "Iglesia San Miguel"
        },
    };
}
