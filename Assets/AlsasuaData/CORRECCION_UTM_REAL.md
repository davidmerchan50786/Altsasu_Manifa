# Corrección a UTM real isótropo — Altsasu (2026-06-19)

## Qué estaba mal
El mundo estaba comprimido en X un factor `76400/81548 ≈ 0.93687` (bug del
importador OSM original, `M_LON_PROJ=76400`). Eso deformaba el pueblo **~6,3 % en
dirección Este-Oeste** (hasta ~25 m a 400 m del centro) y, además, los datos
vectoriales arrastraban un error de ~5 m respecto a OSM/Catastro. Faltaba la autovía.

## Qué se ha hecho (todo a <0,5 m de OSM/IGN/Catastro)
- **Escala isótropa**: `GeoDataAlsasua.ESCALA_UTM_X = 1` (antes 0.93687). Ahora
  terreno, edificios, carreteras y jugador comparten **1 unidad = 1 metro** real.
- **Edificios**: footprints reproyectados directamente desde OSM/Catastro
  (`EPSG:4326→25830`). Verificado: **mediana 0,00 m**, máx 0,00 m (1026/1030; 4
  edificios demolidos en OSM conservan su geometría previa).
- **Carreteras**: snap a OSM + **autovía A-1 (Iparraldeko autobia) y A-10
  (Sakanako autobia) añadidas** (89 tramos nuevos).
- **Tren, río Arakil, plazas**: snap a OSM en UTM real.
- **Sendas, cauces, zonas verdes y huertas**: regeneradas desde OSM actual
  (IDs viejos borrados de OSM) recortadas al pueblo. Incluye 100 huertas/cultivos.
- **Terreno**: mosaico V2 **regenerado nativo en UTM real** (`SX=1`) desde los DEM
  oficiales (LIDAR 0,5 m + IDENA 2 m + IGN MDT05/25). 48 tiles, costuras
  bit-exactas, cota Herriko Plaza 531,97 m (esperado 531,94 → 3 cm). El cargador y
  el manifest no cambian de estructura (mismos 48 tiles), solo las alturas.

## Pasos a ejecutar en Unity (ORDEN IMPORTANTE)
Abre la escena `Assets/#Scenes/Alsasua_Main.unity` y ejecuta, en este orden:

1. **Tools ▸ Alsasua ▸ Terreno ▸ 🧩 Construir Mosaico V2 (bake)**
   — recarga el terreno desde los nuevos RAW (ya en UTM real).
2. **Tools ▸ Alsasua ▸ Terreno ▸ 🗺 Aplicar Ortofoto TerrainLayer**
   — re-hornea la ortofoto (ahora con escalaX=1, queda alineada).
3. **Tools ▸ Alsasua ▸ Mundo ▸ ↩️ Limpiar Edificios** y luego
   **🏙️ Construir Edificios de Asset (footprints reales)**.
4. **Tools ▸ Alsasua ▸ Mundo ▸ ↩️ Limpiar Calles** y luego
   **🛣️ Construir Calles + Autovía (full, v2)**.

Tras esto todo queda en su sitio real con escala coherente. El jugador, al usar
las mismas coordenadas Unity (metros), también queda a escala.

## Verificación rápida
- Visor `outputs/verificacion_proyecto_UTMreal.html`: compáralo con un mapa real,
  deben coincidir en forma (sin estrechamiento E-O).
- En Unity, la cota del terreno en Herriko Plaza (1918, 8570) debe ser ≈ 531,94 m.

## Si algo va mal — revertir
- Datos vectoriales: `Assets/AlsasuaData/_backup_pre_utm_real~/`
- Terreno: `Assets/AlsasuaData/_backup_terreno_pre_utm~/` (48 RAW + manifest)
- Código: `ESCALA_UTM_X = ESCALA_UTM_X_LEGACY` y `SX = 76400/81548` en los .py.

## Blender (mejorar edificios)
Ver `blender_export/LEEME_blender.md`.
