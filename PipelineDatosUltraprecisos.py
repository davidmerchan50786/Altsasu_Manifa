# PipelineDatosUltraprecisos.py
# ═══════════════════════════════════════════════════════════════════════════
#  PIPELINE COMPLETO DE DATOS ULTRA-PRECISOS — Alsasua / Altsasua
#
#  Fuentes (10 orígenes independientes):
#
#  1. LIDAR PNOA 3ª Cobertura (CNIG/IGN, 2022-2024)
#     5+ pts/m², NPC03 clasificación avanzada
#     → DTM 0.5m, tejados exactos, árboles exactos
#
#  2. Catastro INSPIRE WFS (Catastro España)
#     Polígonos catastrales exactos + pisos, año construcción, uso
#     → footprints en cm, altura real, vintage del edificio
#
#  3. IDENA WFS (Gobierno de Navarra)
#     Edificaciones navarra: estado, uso, plantas, tipología
#     → datos locales más detallados que OSM
#
#  4. IDENA WCS DTM 2m (Gobierno de Navarra)
#     Modelo digital del terreno a 2m resolución para Navarra
#     → mejor que IGN 5m, submétrico en zona urbana
#
#  5. CartoCiudad WFS (IGN España)
#     Portales exactos en borde de parcela, callejero normalizado
#     → posición exacta de cada entrada de edificio
#
#  6. Overture Maps (Microsoft/Meta/AWS/TomTom, 2024)
#     Edificios con alturas ML, roads con atributos, POIs
#     → validación cruzada + alturas de edificios ML-derived
#
#  7. Microsoft GlobalML Building Footprints
#     1.4B footprints de Bing Maps + IA, ODbL
#     → footprints de alta precisión IA donde OSM/Catastro fallan
#
#  8. OSM Overpass extendido (ya en DescargarDatosAvanzados.py)
#     building:material, roof:shape, start_date, amenity, etc.
#
#  9. Mapillary API v4
#     Imágenes de calle + detecciones ML: señales, mobiliario, materiales
#     → posición de señales, bancos, farolas, paso de peatones
#
# 10. IGN WMTS PNOA-MA 2024 tiles (25cm)
#     Ortofoto máxima actualidad → mejores tiles que los descargados antes
#
#  RESULTADO (Assets/AlsasuaData/):
#    edificios_ultra.json     — edificios con datos de todas las fuentes
#    terreno_2m.asc           — DTM Navarra 2m (mejor que IGN 5m)
#    lidar_ground.xyz         — puntos suelo LIDAR (para terreno 0.5m)
#    lidar_buildings.json     — puntos edificio LIDAR por building_id
#    lidar_trees.json         — árboles detectados por LIDAR
#    portales_cartociudad.json — portales exactos
#    mapillary_objetos.json   — objetos detectados (señales, mobiliario)
#    overture_buildings.json  — footprints Overture con alturas ML
# ═══════════════════════════════════════════════════════════════════════════

import requests, json, math, time, os, sys, zipfile, io, re, struct
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor, as_completed

# ── Configuración ──────────────────────────────────────────────────────────
LAT_MIN, LAT_MAX = 42.870, 42.935
LON_MIN, LON_MAX = -2.225, -2.125
LAT0, LON0       = 42.8987, -2.1677   # Herriko Plaza (origen Unity)
M_LAT, M_LON     = 111320.0, 76400.0

# UTM 30N (ETRS89) — bbox Alsasua
UTM_E0, UTM_N0   = 571500, 4748500
UTM_E1, UTM_N1   = 578000, 4755000

OUT = Path("Assets/AlsasuaData")
OUT.mkdir(parents=True, exist_ok=True)
(OUT / "lidar_tiles").mkdir(exist_ok=True)

def geo2unity(lat, lon):
    return round((lon - LON0) * M_LON, 3), round((lat - LAT0) * M_LAT, 3)

def get(url, params=None, timeout=60, stream=False, label=""):
    try:
        r = requests.get(url, params=params, timeout=timeout, stream=stream)
        r.raise_for_status()
        kb = int(r.headers.get("content-length", 0)) // 1024
        print(f"  ✓ {label or url[:70]}  {kb}KB")
        return r
    except Exception as e:
        print(f"  ✗ {label or url[:70]}: {e}")
        return None

# ═══════════════════════════════════════════════════════════════════════════
#  1. LIDAR PNOA 3ª COBERTURA — tiles 1×1km para Alsasua
# ═══════════════════════════════════════════════════════════════════════════

def descargar_lidar_tiles():
    """
    Descarga los tiles LAZ del PNOA 3ª Cobertura que cubren Alsasua.
    Alsasua UTM 30N: 574000-576000 E, 4750000-4753000 N
    Tiles necesarios (1km×1km): 574_4750, 574_4751, 574_4752,
                                  575_4750, 575_4751, 575_4752
    Formato nombre CNIG: PNOA_2022_NAV_0574_4751_NPC03.laz
    """
    print("\n=== 1. LIDAR PNOA 3ª Cobertura (5+ pts/m²) ===")

    # Tiles que cubren Alsasua (en km, UTM 30N Navarra)
    tiles = [
        (574, 4750), (574, 4751), (574, 4752),
        (575, 4750), (575, 4751), (575, 4752),
    ]

    descargados = []

    for ex, ny in tiles:
        nombre = f"PNOA_2022_NAV_{ex:04d}_{ny:04d}_NPC03"
        out_path = OUT / "lidar_tiles" / f"{nombre}.laz"
        if out_path.exists():
            print(f"  ✓ {nombre}.laz ya descargado ({out_path.stat().st_size//1024}KB)")
            descargados.append(str(out_path))
            continue

        # Intentar múltiples URLs / patrones del CNIG
        urls_candidatas = [
            # Patrón directo 3ª cobertura
            f"https://centrodedescargas.cnig.es/CentroDescargas/downloadFile.do?seq=LIDA3-2022-31-{ex:04d}-{ny:04d}",
            f"https://centrodedescargas.cnig.es/CentroDescargas/downloadFile.do?seq=LIDA3-31-{ex:04d}-{ny:04d}-NPC03",
            # Patrón 2ª cobertura como fallback
            f"https://centrodedescargas.cnig.es/CentroDescargas/downloadFile.do?seq=LIDAR-NA-{ex:04d}-{ny:04d}",
            # FTP CNIG
            f"https://ftp.cnig.es/PNOA/LIDAR/por_Hoja_MTN50/LIDAR_3C/Navarra/{ex:04d}_{ny:04d}/{nombre}.laz",
            f"https://ftp.cnig.es/ATOM/LIDAR3/Navarra/PNOA_2022_NAV_{ex:04d}_{ny:04d}.laz",
        ]

        for url in urls_candidatas:
            r = get(url, timeout=300, label=f"LIDAR {nombre}")
            if r and r.content and len(r.content) > 100_000 and not r.content[:5] in (b"<?xml", b"<html", b"<HTML"):
                with open(out_path, "wb") as f:
                    f.write(r.content)
                print(f"  ✓ Guardado: {nombre}.laz ({len(r.content)//1024}KB)")
                descargados.append(str(out_path))
                break
        else:
            print(f"  ⚠ {nombre}: no disponible via URL directa")
            print(f"    → Manual: https://centrodedescargas.cnig.es/ → LIDAR 3ª Cobertura → Navarra → {ex},{ny}")

    if descargados:
        procesar_lidar(descargados)
    return descargados


def procesar_lidar(laz_paths):
    """
    Procesa los LAZ con laspy (si disponible).
    Extrae: suelo (class 2), edificios (class 6), vegetación (class 3,4,5).
    """
    try:
        import laspy
        import numpy as np
    except ImportError:
        print("  ⚠ laspy no instalado — ejecuta: pip install laspy lazrs-python numpy scipy")
        print("    Los tiles LAZ se guardan para procesamiento posterior.")
        generar_instrucciones_lidar(laz_paths)
        return

    print("\n  Procesando LIDAR con laspy...")

    all_ground   = []   # (x, y, z) UTM → se convierte a Unity
    all_building = []   # (x, y, z) edificios
    all_trees    = []   # (x, y, z) vegetación

    for laz_path in laz_paths:
        print(f"  Leyendo {Path(laz_path).name}...")
        try:
            las = laspy.read(laz_path)
            x, y, z = np.array(las.x), np.array(las.y), np.array(las.z)
            cls = np.array(las.classification)

            # UTM 30N → Unity XZ
            # Unity X = (UTM_E - origen_E) - pero necesitamos lat/lon
            # Aproximación: UTM_E 574000 ≈ lat 42.8987°N, lon -2.1677°W
            # UTM_E → lon: lon = LON0 + (UTM_E - E_origen) / M_LON
            # UTM_N → lat: lat = LAT0 + (UTM_N - N_origen) / M_LAT
            E_orig = 574900  # UTM_E en Herriko Plaza aprox
            N_orig = 4751600 # UTM_N en Herriko Plaza aprox

            ux = ((x - E_orig) / M_LON * M_LON).round(2)  # simplificado
            uz = ((y - N_orig) / M_LAT * M_LAT).round(2)

            # Ground (class 2)
            mask_g = cls == 2
            ground_pts = np.stack([ux[mask_g], z[mask_g], uz[mask_g]], axis=1)
            all_ground.extend(ground_pts.tolist())

            # Buildings (class 6) — puntos de edificio/tejado
            mask_b = cls == 6
            bld_pts = np.stack([ux[mask_b], z[mask_b], uz[mask_b]], axis=1)
            all_building.extend(bld_pts.tolist())

            # Vegetación alta (class 5 = High vegetation)
            mask_v = cls == 5
            veg_pts = np.stack([ux[mask_v], z[mask_v], uz[mask_v]], axis=1)
            all_trees.extend(veg_pts.tolist())

        except Exception as e:
            print(f"  ✗ Error procesando {laz_path}: {e}")
            continue

    # Guardar puntos suelo
    if all_ground:
        import numpy as np
        gnd = np.array(all_ground)
        # Guardar como XYZ texto (ligero)
        with open(OUT / "lidar_ground.xyz", "w") as f:
            for pt in gnd[::4]:  # cada 4 puntos (reducir tamaño)
                f.write(f"{pt[0]:.2f} {pt[1]:.3f} {pt[2]:.2f}\n")
        print(f"  ✓ lidar_ground.xyz: {len(gnd)//4} puntos suelo")

    # Guardar puntos edificio agrupados por footprint
    if all_building:
        guardar_building_pts_por_edificio(all_building)

    # Detectar árboles (cluster de puntos de vegetación alta)
    if all_trees:
        detectar_arboles_lidar(all_trees)


def guardar_building_pts_por_edificio(bld_pts):
    """
    Agrupa puntos de edificio LIDAR por footprint OSM.
    Asigna a cada edificio: z_min (alero), z_max (cumbrera), slope, forma.
    """
    import numpy as np, json
    bld = np.array(bld_pts)

    # Cargar footprints conocidos
    fp = OUT / "buildings_unity.json"
    if not fp.exists():
        return

    with open(fp) as f:
        edificios = json.load(f)

    resultados = []
    for ed in edificios:
        verts = ed.get("vertices", [])
        if len(verts) < 3:
            continue

        # Bounding box del edificio (con offset Unity)
        OX, OZ = 1918, 8570
        xs = [v["x"] + OX for v in verts]
        zs = [v["z"] + OZ for v in verts]
        xmin, xmax = min(xs) - 1, max(xs) + 1
        zmin, zmax = min(zs) - 1, max(zs) + 1

        # Puntos LIDAR dentro de la BB
        mask = (bld[:, 0] >= xmin) & (bld[:, 0] <= xmax) \
             & (bld[:, 2] >= zmin) & (bld[:, 2] <= zmax)
        pts_edif = bld[mask]

        if len(pts_edif) < 3:
            continue

        z_vals = pts_edif[:, 1]
        z_min  = float(np.percentile(z_vals, 5))    # alero (eaves)
        z_max  = float(np.percentile(z_vals, 95))   # cumbrera (ridge)
        altura = z_max - z_min

        # Detectar forma: si hay dos clusters Z claros → dos aguas
        hist, edges = np.histogram(z_vals, bins=20)
        # Si bimodal → gabled/hipped, si plano → flat
        pico_ratio = float(hist.max() / (hist.mean() + 0.01))
        forma = "flat" if altura < 1.5 else ("gabled" if pico_ratio > 2.5 else "hipped")

        resultados.append({
            "id":           ed["id"],
            "lidar_z_min":  round(z_min, 2),
            "lidar_z_max":  round(z_max, 2),
            "lidar_altura": round(altura, 2),
            "lidar_forma":  forma,
            "lidar_pts":    int(mask.sum()),
        })

    with open(OUT / "lidar_buildings.json", "w") as f:
        json.dump(resultados, f, indent=2)
    print(f"  ✓ lidar_buildings.json: {len(resultados)} edificios con datos LIDAR")


def detectar_arboles_lidar(veg_pts):
    """
    Clusteriza puntos de vegetación alta → posición y altura de cada árbol.
    """
    try:
        import numpy as np
        from scipy.ndimage import label
    except ImportError:
        return

    veg = np.array(veg_pts)
    # Grid 1m × 1m para clustering
    xoff = float(veg[:, 0].min())
    zoff = float(veg[:, 2].min())
    xr = ((veg[:, 0] - xoff) / 1.0).astype(int)
    zr = ((veg[:, 2] - zoff) / 1.0).astype(int)
    grid = np.zeros((xr.max()+2, zr.max()+2), dtype=bool)
    for xi, zi in zip(xr, zr):
        grid[xi, zi] = True

    labeled, n_clusters = label(grid)

    arboles = []
    for cid in range(1, n_clusters + 1):
        ys_idx, zs_idx = np.where(labeled == cid)
        if len(ys_idx) < 4:   # ignorar clusters muy pequeños
            continue
        cx = float(ys_idx.mean()) + xoff
        cz = float(zs_idx.mean()) + zoff
        # Altura máx del árbol
        mask_c = (xr == ys_idx.mean().astype(int)) | True  # approx
        mask_spatial = labeled[xr, zr] == cid
        z_max = float(veg[mask_spatial, 1].max()) if mask_spatial.any() else 0
        radio = math.sqrt(len(ys_idx) / math.pi)
        arboles.append({"x": round(cx, 1), "z": round(cz, 1),
                        "altura": round(z_max, 1), "radio": round(radio, 1)})

    with open(OUT / "lidar_trees.json", "w") as f:
        json.dump(arboles, f, indent=2)
    print(f"  ✓ lidar_trees.json: {len(arboles)} árboles detectados LIDAR")


def generar_instrucciones_lidar(laz_paths):
    """Instrucciones para procesar con herramientas externas si laspy no está."""
    instrucciones = {
        "descripcion": "Procesar tiles LIDAR PNOA para Alsasua",
        "tiles": laz_paths,
        "opcion_1_laspy": [
            "pip install laspy lazrs-python numpy scipy",
            "python PipelineDatosUltraprecisos.py",
        ],
        "opcion_2_pdal": [
            "# Instalar PDAL: https://pdal.io/",
            "pdal pipeline pdal_pipeline.json",
        ],
        "opcion_3_cloudcompare": [
            "# Abrir en CloudCompare: https://cloudcompare.org/",
            "# Filtrar por clase 2 (suelo), 6 (edificios), 5 (vegetación alta)",
            "# Exportar como XYZ o CSV",
        ],
    }
    with open(OUT / "lidar_instrucciones.json", "w") as f:
        json.dump(instrucciones, f, indent=2, ensure_ascii=False)


# ═══════════════════════════════════════════════════════════════════════════
#  2. CATASTRO INSPIRE WFS — edificios con año construcción y partes
# ═══════════════════════════════════════════════════════════════════════════

def descargar_catastro_inspire():
    print("\n=== 2. Catastro INSPIRE WFS — edificios exactos ===")

    # URL oficial INSPIRE Catastro
    URL = "https://ovc.catastro.meh.es/INSPIRE/wfsBU.aspx"
    BBOX = f"{LON_MIN},{LAT_MIN},{LON_MAX},{LAT_MAX}"

    edificios_cat = []

    for tipo in ["bu:Building", "bu:BuildingPart"]:
        params = {
            "SERVICE":    "WFS",
            "VERSION":    "2.0.0",
            "REQUEST":    "GetFeature",
            "TYPENAMES":  tipo,
            "COUNT":      "9999",
            "BBOX":       f"{BBOX},EPSG:4326",
            "OUTPUTFORMAT": "application/gml+xml; version=3.2",
        }
        r = get(URL, params=params, timeout=90, label=f"Catastro WFS {tipo}")
        if not r:
            continue

        # Parsear GML
        try:
            import xml.etree.ElementTree as ET
            ns = {
                "gml":  "http://www.opengis.net/gml/3.2",
                "bu":   "http://inspire.ec.europa.eu/schemas/bu-core2d/4.0",
                "bu3":  "http://inspire.ec.europa.eu/schemas/bu-core3d/4.0",
                "base": "http://inspire.ec.europa.eu/schemas/base/3.3",
                "base2":"http://inspire.ec.europa.eu/schemas/base2/2.0",
                "gn":   "http://inspire.ec.europa.eu/schemas/gn/4.0",
            }

            root = ET.fromstring(r.content)
            members = root.findall(".//{http://www.opengis.net/gml/3.2}featureMember") + \
                      root.findall(".//{http://www.opengis.net/gml/3.2}member")

            for member in members:
                # Extraer campos clave (namespace flexible)
                def tag(path):
                    for ns_uri in [
                        "http://inspire.ec.europa.eu/schemas/bu-core2d/4.0",
                        "http://inspire.ec.europa.eu/schemas/bu-core3d/4.0",
                    ]:
                        el = member.find(f".//{{{ns_uri}}}{path}")
                        if el is not None:
                            return el.text or (el.get("{http://www.opengis.net/gml/3.2}id") or "")
                    return ""

                # Geometría: lowerCorner/upperCorner o polygon
                polys = member.findall(".//{http://www.opengis.net/gml/3.2}posList")
                verts = []
                for pl in polys[:1]:  # primer polígono (outer ring)
                    coords = list(map(float, pl.text.split()))
                    for i in range(0, len(coords) - 1, 2):
                        lon_, lat_ = coords[i], coords[i+1]
                        ux, uz = geo2unity(lat_, lon_)
                        verts.append({"x": ux, "z": uz})
                    break

                if len(verts) < 3:
                    continue

                # Altura y año
                altura_str = tag("numberOfFloorsAboveGround") or tag("numberOfBuildingUnits") or "2"
                fecha_str  = tag("dateOfConstruction") or ""
                ref_id     = ""
                ref_el = member.find(".//{http://www.opengis.net/gml/3.2}identifier")
                if ref_el is not None:
                    ref_id = ref_el.text or ""

                try:
                    height = float(altura_str) * 3.2
                except:
                    height = 6.4

                anio = 0
                m = re.search(r"(\d{4})", fecha_str)
                if m:
                    anio = int(m.group(1))

                edificios_cat.append({
                    "ref_catastral": ref_id,
                    "height":        round(height, 2),
                    "anio":          anio,
                    "tipo":          tipo.split(":")[-1],
                    "vertices":      verts,
                })

        except Exception as e:
            print(f"  GML parse error ({tipo}): {e}")

    if edificios_cat:
        with open(OUT / "catastro_edificios.json", "w") as f:
            json.dump(edificios_cat, f, ensure_ascii=False, indent=2)
        print(f"  ✓ catastro_edificios.json: {len(edificios_cat)} partes catastrales")
    else:
        print("  ⚠ Sin datos del Catastro INSPIRE")

    # También intentar el ATOM feed de Altsasua (municipio 31250)
    _catastro_atom_altsasua()


def _catastro_atom_altsasua():
    """Descarga el ZIP del ATOM INSPIRE para Altsasua (municipio 31250 Navarra)."""
    # Patron: https://www.catastro.meh.es/INSPIRE/buildings/31/31250_ALTSASUA_CU.zip
    url = "https://www.catastro.meh.es/INSPIRE/buildings/31/31250-ALTSASUA.zip"
    urls = [
        url,
        "https://www.catastro.meh.es/INSPIRE/buildings/31/31250_ALTSASUA_CU.zip",
        "https://www.catastro.meh.es/INSPIRE/buildings/Navarra/31250_ALTSASUA_BU.zip",
    ]
    for u in urls:
        r = get(u, timeout=120, label="Catastro ATOM Altsasua ZIP")
        if r and r.content and len(r.content) > 5000 and r.content[:2] == b"PK":
            try:
                z = zipfile.ZipFile(io.BytesIO(r.content))
                for name in z.namelist():
                    ext = Path(name).suffix.lower()
                    if ext in (".gml", ".xml", ".json", ".gfs"):
                        data = z.read(name)
                        with open(OUT / f"catastro_{Path(name).name}", "wb") as f:
                            f.write(data)
                        print(f"  ✓ Extraído: {name} ({len(data)//1024}KB)")
                return
            except Exception as e:
                print(f"  ZIP error: {e}")


# ═══════════════════════════════════════════════════════════════════════════
#  3. IDENA WFS — edificaciones de Navarra con atributos detallados
# ═══════════════════════════════════════════════════════════════════════════

def descargar_idena_wfs():
    print("\n=== 3. IDENA WFS — Edificaciones Navarra ===")

    URL = "https://idena.navarra.es/ogc/wfs"
    BBOX_UTM = f"{UTM_E0},{UTM_N0},{UTM_E1},{UTM_N1}"

    # Capas clave de IDENA
    capas = [
        ("IDENA:EDIF_Pol_Edificaciones",    "edificaciones"),
        ("IDENA:CARTO_Pol_Manzanas",         "manzanas"),
        ("IDENA:CARTO_Lin_Red_Viaria",       "red_viaria"),
        ("IDENA:MOBILI_Sym_Arbolado",        "arbolado"),
        ("IDENA:IDEMUN_Sym_Alumbrado",       "alumbrado"),
        ("IDENA:PATRIM_Sym_BICarquite",      "patrimonio"),
    ]

    for typename, nombre in capas:
        params = {
            "SERVICE":    "WFS",
            "VERSION":    "2.0.0",
            "REQUEST":    "GetFeature",
            "TYPENAMES":  typename,
            "COUNT":      "5000",
            "BBOX":       f"{BBOX_UTM},EPSG:25830",
            "OUTPUTFORMAT": "application/json",
        }
        r = get(URL, params=params, timeout=60, label=f"IDENA {nombre}")
        if not r:
            continue

        try:
            if r.content[:1] == b"{":
                fc = r.json()
                features = fc.get("features", [])
                resultado = []

                for feat in features:
                    geom  = feat.get("geometry", {})
                    props = feat.get("properties", {})
                    verts = []

                    if geom.get("type") == "Polygon":
                        for lon, lat, *_ in geom["coordinates"][0]:
                            ux, uz = geo2unity(lat, lon)
                            verts.append({"x": ux, "z": uz})
                    elif geom.get("type") == "Point":
                        coords = geom["coordinates"]
                        if len(coords) >= 2:
                            ux, uz = geo2unity(coords[1], coords[0])
                            verts = [{"x": ux, "z": uz}]
                    elif geom.get("type") == "MultiPolygon":
                        for ring in geom["coordinates"][:1]:
                            for lon, lat, *_ in ring[0]:
                                ux, uz = geo2unity(lat, lon)
                                verts.append({"x": ux, "z": uz})

                    if not verts:
                        continue

                    resultado.append({
                        "id":    feat.get("id", ""),
                        "props": props,
                        "verts": verts,
                    })

                out_path = OUT / f"idena_{nombre}.json"
                with open(out_path, "w", encoding="utf-8") as f:
                    json.dump(resultado, f, ensure_ascii=False, indent=2)
                print(f"  ✓ idena_{nombre}.json: {len(resultado)} features")
            else:
                # Guardar raw si no es JSON
                with open(OUT / f"idena_{nombre}_raw.xml", "wb") as f:
                    f.write(r.content)
        except Exception as e:
            print(f"  Parse error {nombre}: {e}")


# ═══════════════════════════════════════════════════════════════════════════
#  4. IDENA WCS — DTM 2m Navarra (mejor que IGN 5m)
# ═══════════════════════════════════════════════════════════════════════════

def descargar_idena_dtm_2m():
    print("\n=== 4. IDENA WCS — DTM 2m Navarra ===")

    URL = "https://idena.navarra.es/ogc/wcs"

    # Primero GetCapabilities para ver coberturas disponibles
    r_caps = get(URL, params={"SERVICE":"WCS","VERSION":"1.0.0","REQUEST":"GetCapabilities"},
                 timeout=30, label="IDENA WCS Capabilities")
    if r_caps:
        coberturas = re.findall(r"<name>(.*?)</name>", r_caps.text)
        dtm_names = [c for c in coberturas if any(k in c.upper() for k in ["MDT","DTM","MDE","TERRENO","ALTIM","ELEV"])]
        print(f"  Coberturas DTM disponibles: {dtm_names[:10]}")

    # Nombres posibles del DTM 2m en IDENA
    candidatos = [
        "MDT_2M_etrs89_30n", "MDT2m", "MDT_2M", "MDT05_ETRS89",
        "MDT_05_ETRS89_30N", "Altimetria_2M", "MDE_2M",
        "MDT_2M_ETRS89_UTM30N", "NUPNTS5_MDT",
    ]

    BBOX_UTM = f"{UTM_E0},{UTM_N0},{UTM_E1},{UTM_N1}"
    W = int((UTM_E1 - UTM_E0) / 2)   # 1px = 2m → (6500/2)=3250 cols
    H = int((UTM_N1 - UTM_N0) / 2)

    for cob in candidatos:
        params = {
            "SERVICE": "WCS", "VERSION": "1.0.0", "REQUEST": "GetCoverage",
            "COVERAGE": cob, "CRS": "EPSG:25830",
            "BBOX": BBOX_UTM, "WIDTH": str(W), "HEIGHT": str(H),
            "FORMAT": "AAIGRID",  # ASCII Grid
        }
        r = get(URL, params=params, timeout=90, label=f"IDENA DTM 2m ({cob})")
        if r and r.content and not r.content.startswith(b"<?xml") and not r.content.startswith(b"<html"):
            # Verificar que es un ASCII Grid válido
            primera_linea = r.content[:50].decode("utf-8", errors="ignore")
            if "ncols" in primera_linea.lower() or primera_linea.strip().startswith("ncols"):
                out_path = OUT / "terreno_2m.asc"
                with open(out_path, "wb") as f:
                    f.write(r.content)
                print(f"  ✓ terreno_2m.asc: {len(r.content)//1024}KB (cobertura: {cob})")
                return
            else:
                print(f"    → No es ASC válido: {primera_linea[:60]}")

    print("  ⚠ DTM 2m no disponible via WCS automático")
    print("    → Manual: https://idena.navarra.es/navegar/ → Descargas → MDT")


# ═══════════════════════════════════════════════════════════════════════════
#  5. CARTOCIUDAD WFS — portales exactos en borde de parcela
# ═══════════════════════════════════════════════════════════════════════════

def descargar_cartociudad():
    print("\n=== 5. CartoCiudad WFS — portales y callejero ===")

    URL  = "https://www.cartociudad.es/wfs-inspire/direcciones"
    BBOX = f"{LON_MIN},{LAT_MIN},{LON_MAX},{LAT_MAX}"

    params = {
        "SERVICE":    "WFS",
        "VERSION":    "2.0.0",
        "REQUEST":    "GetFeature",
        "TYPENAMES":  "AD:Address",
        "COUNT":      "9999",
        "BBOX":       f"{BBOX},EPSG:4326",
        "OUTPUTFORMAT": "application/json",
    }

    r = get(URL, params=params, timeout=60, label="CartoCiudad Portales")
    if not r:
        return

    portales = []
    try:
        fc = r.json() if r.content[:1] == b"{" else None
        if fc:
            for feat in fc.get("features", []):
                geom  = feat.get("geometry", {})
                props = feat.get("properties", {})
                if geom.get("type") == "Point":
                    lon_, lat_ = geom["coordinates"][:2]
                    ux, uz = geo2unity(lat_, lon_)
                    portales.append({
                        "ux":    ux,
                        "uz":    uz,
                        "calle": props.get("thoroughfare_name", ""),
                        "num":   props.get("locator_designator", ""),
                        "cp":    props.get("post_code", ""),
                    })
    except Exception as e:
        print(f"  CartoCiudad parse error: {e}")

    if portales:
        with open(OUT / "portales_cartociudad.json", "w", encoding="utf-8") as f:
            json.dump(portales, f, ensure_ascii=False, indent=2)
        print(f"  ✓ portales_cartociudad.json: {len(portales)} portales")
    else:
        print("  ⚠ Sin portales — intentando URL alternativa")
        _cartociudad_rest()


def _cartociudad_rest():
    """Alternativa: servicio REST de geocodificación de CartoCiudad."""
    # Descarga callejero Navarra por provincia
    url = ("https://centrodedescargas.cnig.es/CentroDescargas/downloadFile.do"
           "?seq=CARTO_CIUDAD_PORTALES_31")
    r = get(url, timeout=120, label="CartoCiudad Portales Navarra ZIP")
    if r and r.content and r.content[:2] == b"PK":
        try:
            z = zipfile.ZipFile(io.BytesIO(r.content))
            for name in z.namelist():
                if Path(name).suffix.lower() in (".shp", ".dbf", ".prj", ".json", ".csv"):
                    with open(OUT / Path(name).name, "wb") as f:
                        f.write(z.read(name))
                    print(f"  ✓ Extraído: {name}")
        except Exception as e:
            print(f"  ZIP error: {e}")


# ═══════════════════════════════════════════════════════════════════════════
#  6. OVERTURE MAPS — edificios con alturas ML + POIs
# ═══════════════════════════════════════════════════════════════════════════

def descargar_overture():
    print("\n=== 6. Overture Maps (Microsoft/Meta/AWS/TomTom) ===")

    BBOX = f"{LON_MIN},{LAT_MIN},{LON_MAX},{LAT_MAX}"

    # Intentar CLI overturemaps
    import subprocess, shutil
    if shutil.which("overturemaps") or shutil.which("python"):
        tipos = [
            ("building",   "overture_buildings.geojson"),
            ("segment",    "overture_roads.geojson"),
            ("place",      "overture_places.geojson"),
        ]
        for tipo, fname in tipos:
            out_path = OUT / fname
            if out_path.exists():
                print(f"  ✓ {fname} ya existe")
                continue
            cmd = [
                sys.executable, "-m", "overturemaps", "download",
                f"--bbox={BBOX}",
                "-f", "geojson",
                f"--type={tipo}",
                "-o", str(out_path),
            ]
            print(f"  Ejecutando: overturemaps download {tipo}...")
            try:
                result = subprocess.run(cmd, capture_output=True, text=True, timeout=300)
                if result.returncode == 0 and out_path.exists():
                    size = out_path.stat().st_size // 1024
                    print(f"  ✓ {fname}: {size}KB")
                    if tipo == "building":
                        procesar_overture_buildings(out_path)
                else:
                    print(f"  ⚠ overturemaps {tipo}: {result.stderr[:200]}")
                    print(f"    → pip install overturemaps")
            except subprocess.TimeoutExpired:
                print(f"  ⚠ Timeout {tipo}")
            except Exception as e:
                print(f"  ⚠ {tipo}: {e}")
                print(f"    → pip install overturemaps")
                break
    else:
        print("  pip install overturemaps  para acceder a datos Microsoft/Meta/TomTom")


def procesar_overture_buildings(geojson_path):
    """Extrae alturas ML de Overture y las fusiona con los edificios OSM."""
    try:
        with open(geojson_path, encoding="utf-8") as f:
            fc = json.load(f)
        features = fc.get("features", [])
        alturas = []
        for feat in features:
            props = feat.get("properties", {})
            geom  = feat.get("geometry", {})
            h = props.get("height") or props.get("num_floors", 0)
            if not h:
                continue
            # Centroide del polígono
            if geom.get("type") == "Polygon":
                ring = geom["coordinates"][0]
                cx   = sum(c[0] for c in ring) / len(ring)
                cy   = sum(c[1] for c in ring) / len(ring)
                ux, uz = geo2unity(cy, cx)
                alturas.append({"ux": ux, "uz": uz, "height": float(h),
                                "name": props.get("names", {}).get("primary", "")})

        with open(OUT / "overture_alturas.json", "w") as f:
            json.dump(alturas, f, indent=2)
        print(f"  ✓ overture_alturas.json: {len(alturas)} edificios con altura ML")
    except Exception as e:
        print(f"  Overture process error: {e}")


# ═══════════════════════════════════════════════════════════════════════════
#  7. MICROSOFT GLOBALML BUILDING FOOTPRINTS — IA desde Bing Maps
# ═══════════════════════════════════════════════════════════════════════════

def descargar_microsoft_footprints():
    print("\n=== 7. Microsoft GlobalML Building Footprints (1.4B, ODbL) ===")

    # El manifest contiene las URLs de tiles CSV.GZ por cuadrante
    manifest_url = "https://minedbuildings.z5.web.core.windows.net/global-buildings/dataset-links.csv"
    r = get(manifest_url, timeout=30, label="Microsoft Footprints manifest")
    if not r:
        print("  → Acceder directamente: https://github.com/microsoft/GlobalMLBuildingFootprints")
        return

    # Encontrar tiles de Spain que cubran Alsasua
    # Alsasua: lat ~42.9, lon ~-2.17 → quadkey nivel 9
    qk = latlon_to_quadkey(42.8987, -2.1677, 9)
    print(f"  QuadKey nivel 9 para Alsasua: {qk}")

    lines = r.text.splitlines()
    header = lines[0].lower() if lines else ""
    spain_tiles = []

    for line in lines[1:]:
        if "Spain" in line or "spain" in line:
            parts = line.split(",")
            if len(parts) >= 2:
                country = parts[0].strip('"')
                link    = parts[-1].strip('"').strip()
                if "Spain" in country:
                    spain_tiles.append(link)

    print(f"  Tiles de Spain encontrados: {len(spain_tiles)}")

    # Filtrar tiles que cubran nuestra BBOX
    alsasua_tiles = []
    for url in spain_tiles[:500]:  # límite para no iterar miles
        if not url.startswith("http"):
            continue
        # Extraer quadkey del URL o nombre de archivo
        qk_match = re.search(r"[/_]([0-9]+)(?:\.csv\.gz|\.geojsonl\.gz)", url)
        if qk_match:
            tile_qk = qk_match.group(1)
            if qk.startswith(tile_qk) or tile_qk.startswith(qk[:6]):
                alsasua_tiles.append(url)

    if not alsasua_tiles:
        # Buscar por nombre de país directamente
        print("  Buscando tiles Spain con filtro bbox...")
        for url in spain_tiles[:20]:
            alsasua_tiles.append(url)  # descargar primeros tiles Spain

    print(f"  Tiles Alsasua candidatos: {len(alsasua_tiles)}")

    buildings_ms = []
    for url in alsasua_tiles[:3]:  # máximo 3 tiles
        r = get(url, timeout=120, label=f"MS Footprints {url[-40:]}")
        if not r:
            continue
        try:
            import gzip
            raw = gzip.decompress(r.content)
            for line in raw.decode("utf-8").splitlines():
                if not line.strip():
                    continue
                try:
                    feat = json.loads(line)
                    geom = feat.get("geometry", {})
                    if geom.get("type") != "Polygon":
                        continue
                    ring = geom["coordinates"][0]
                    # Filtrar por BBOX
                    lons = [c[0] for c in ring]
                    lats = [c[1] for c in ring]
                    if not (LON_MIN <= sum(lons)/len(lons) <= LON_MAX and
                            LAT_MIN <= sum(lats)/len(lats) <= LAT_MAX):
                        continue
                    verts = []
                    for lon_, lat_ in ring:
                        ux, uz = geo2unity(lat_, lon_)
                        verts.append({"x": ux, "z": uz})
                    buildings_ms.append({"vertices": verts, "source": "microsoft_ml"})
                except:
                    pass
        except Exception as e:
            print(f"  MS parse error: {e}")

    if buildings_ms:
        with open(OUT / "microsoft_footprints.json", "w") as f:
            json.dump(buildings_ms, f, indent=2)
        print(f"  ✓ microsoft_footprints.json: {len(buildings_ms)} footprints IA")


def latlon_to_quadkey(lat, lon, level):
    """Convierte coordenadas geográficas a QuadKey de Bing Maps."""
    lat  = max(-85.05112878, min(85.05112878, lat))
    lon  = max(-180.0, min(180.0, lon))
    x    = int((lon + 180) / 360 * (1 << level))
    sin  = math.sin(lat * math.pi / 180)
    y    = int((0.5 - math.log((1 + sin) / (1 - sin)) / (4 * math.pi)) * (1 << level))
    qk   = ""
    for i in range(level, 0, -1):
        digit = 0
        mask  = 1 << (i - 1)
        if x & mask: digit += 1
        if y & mask: digit += 2
        qk += str(digit)
    return qk


# ═══════════════════════════════════════════════════════════════════════════
#  8. MAPILLARY API v4 — fotos de calle + detecciones ML
# ═══════════════════════════════════════════════════════════════════════════

def descargar_mapillary(token=""):
    print("\n=== 8. Mapillary API v4 — fotos calle + objetos ML ===")

    if not token:
        print("  ⚠ Necesitas token gratuito: https://www.mapillary.com/app/settings/developers")
        print("    → Registrarse, crear App, copiar Client Token")
        print("    → Ejecutar: python PipelineDatosUltraprecisos.py --mapillary-token TU_TOKEN")
        # Guardar plantilla de datos vacía para que Unity no falle
        with open(OUT / "mapillary_objetos.json", "w") as f:
            json.dump([], f)
        return

    URL    = "https://graph.mapillary.com/images"
    BBOX   = f"{LON_MIN},{LAT_MIN},{LON_MAX},{LAT_MAX}"
    FIELDS = "id,geometry,thumb_256_url,compass_angle,sequence,captured_at"

    params = {
        "access_token": token,
        "bbox":         BBOX,
        "limit":        "2000",
        "fields":       FIELDS,
    }

    r = get(URL, params=params, timeout=60, label="Mapillary imágenes")
    if not r:
        return

    try:
        data   = r.json()
        images = data.get("data", [])
        print(f"  Imágenes: {len(images)}")

        # Posiciones con orientación de cámara
        posiciones = []
        for img in images:
            geom = img.get("geometry", {})
            if geom.get("type") == "Point":
                lon_, lat_ = geom["coordinates"][:2]
                ux, uz = geo2unity(lat_, lon_)
                posiciones.append({
                    "id":      img["id"],
                    "ux":      ux,
                    "uz":      uz,
                    "angulo":  img.get("compass_angle", 0),
                    "fecha":   img.get("captured_at", ""),
                    "thumb":   img.get("thumb_256_url", ""),
                })

        with open(OUT / "mapillary_imagenes.json", "w") as f:
            json.dump(posiciones, f, indent=2)
        print(f"  ✓ mapillary_imagenes.json: {len(posiciones)} fotos")

        # Detecciones ML de objetos
        _mapillary_detecciones(token, [img["id"] for img in images[:200]])

    except Exception as e:
        print(f"  Mapillary parse error: {e}")


def _mapillary_detecciones(token, image_ids):
    """Descarga detecciones ML (señales, farolas, bancos, etc.) de Mapillary."""
    tipos_objeto = [
        "regulatory--stop--g1",
        "information--street-name--g1",
        "complementary--street-name--g1",
        "object--street-light",
        "object--bench",
        "object--trash-can",
        "object--bike-rack",
    ]

    objetos = []
    for img_id in image_ids[:50]:  # limitar llamadas API
        url = f"https://graph.mapillary.com/{img_id}/detections"
        r = get(url, params={"access_token": token, "fields": "value,geometry,image"},
                timeout=10, label=f"Mapillary det {img_id[:8]}")
        if not r:
            continue
        try:
            for det in r.json().get("data", []):
                val = det.get("value", "")
                if any(t in val for t in tipos_objeto):
                    geom = det.get("geometry", {})
                    if geom:
                        objetos.append({"tipo": val, "imagen": img_id, "geom": geom})
        except:
            pass

    with open(OUT / "mapillary_objetos.json", "w") as f:
        json.dump(objetos, f, indent=2)
    print(f"  ✓ mapillary_objetos.json: {len(objetos)} objetos detectados")


# ═══════════════════════════════════════════════════════════════════════════
#  9. PNOA-MA 2024 WMTS — tiles orto máxima actualidad (25cm)
# ═══════════════════════════════════════════════════════════════════════════

def descargar_pnoa_2024():
    print("\n=== 9. PNOA-MA 2024 WMTS — tiles ortofoto máxima actualidad ===")

    # WMTS del IGN para PNOA Máxima Actualidad
    # https://www.ign.es/wmts/pnoa-ma
    WMTS_URL  = "https://www.ign.es/wmts/pnoa-ma"
    LAYER     = "OI.OrthoimageCoverage"
    TILESET   = "INSPIRE"
    FORMAT    = "image/jpeg"

    # Calcular tiles TMS para el área de Alsasua
    # Zoom level 18 ≈ 60cm/pixel, level 19 ≈ 30cm/pixel
    zoom = 18
    tiles = bbox_to_wmts_tiles(LAT_MIN, LAT_MAX, LON_MIN, LON_MAX, zoom)
    print(f"  Tiles zoom {zoom}: {len(tiles)}")

    descargados = 0
    (OUT / "tiles" / "pnoa2024").mkdir(parents=True, exist_ok=True)

    for tx, ty, tz in tiles[:144]:  # max 144 tiles (≈ grid 12×12)
        fname = f"pnoa_{tz}_{tx}_{ty}.jpg"
        out_p = OUT / "tiles" / "pnoa2024" / fname
        if out_p.exists():
            descargados += 1
            continue

        url = (f"{WMTS_URL}?SERVICE=WMTS&REQUEST=GetTile&VERSION=1.0.0"
               f"&LAYER={LAYER}&TILEMATRIXSET={TILESET}"
               f"&TILEMATRIX={TILESET}:{zoom:02d}"
               f"&TILEROW={ty}&TILECOL={tx}&FORMAT={FORMAT}")

        r = get(url, timeout=30, label=f"PNOA {zoom}/{tx}/{ty}")
        if r and r.content and len(r.content) > 1000:
            with open(out_p, "wb") as f:
                f.write(r.content)
            descargados += 1

    print(f"  ✓ {descargados} tiles PNOA-MA 2024 descargados")


def bbox_to_wmts_tiles(lat_min, lat_max, lon_min, lon_max, zoom):
    """Genera lista de tiles TMS para un bounding box."""
    def latlon_to_tile(lat, lon, z):
        n   = 1 << z
        tx  = int((lon + 180.0) / 360.0 * n)
        lat_r = math.radians(lat)
        ty  = int((1.0 - math.log(math.tan(lat_r) + 1.0/math.cos(lat_r)) / math.pi) / 2.0 * n)
        return tx, ty

    x0, y0 = latlon_to_tile(lat_max, lon_min, zoom)  # top-left
    x1, y1 = latlon_to_tile(lat_min, lon_max, zoom)  # bottom-right

    tiles = [(x, y, zoom) for x in range(x0, x1+1) for y in range(y0, y1+1)]
    return tiles


# ═══════════════════════════════════════════════════════════════════════════
#  10. FUSIÓN FINAL — combinar todas las fuentes en edificios_ultra.json
# ═══════════════════════════════════════════════════════════════════════════

def fusionar_todo():
    print("\n=== 10. Fusión final — edificios_ultra.json ===")

    # Base: buildings_final.json o buildings_unity.json
    base_path = OUT / "buildings_final.json"
    if not base_path.exists():
        base_path = OUT / "buildings_completo.json"
    if not base_path.exists():
        base_path = OUT / "buildings_unity.json"
    if not base_path.exists():
        print("  ⚠ Sin datos base")
        return

    with open(base_path, encoding="utf-8") as f:
        edificios = json.load(f)
    print(f"  Base: {len(edificios)} edificios ({base_path.name})")

    # Cargar datos LIDAR por edificio
    lidar_idx = {}
    lidar_p = OUT / "lidar_buildings.json"
    if lidar_p.exists():
        with open(lidar_p) as f:
            for b in json.load(f):
                lidar_idx[b["id"]] = b
        print(f"  LIDAR: {len(lidar_idx)} edificios")

    # Cargar alturas Overture
    overture_idx = {}  # índice espacial simple: (round(ux,0), round(uz,0))
    over_p = OUT / "overture_alturas.json"
    if over_p.exists():
        with open(over_p) as f:
            for b in json.load(f):
                k = (round(b["ux"]), round(b["uz"]))
                overture_idx[k] = b
        print(f"  Overture: {len(overture_idx)} alturas ML")

    # Cargar datos Catastro
    catastro_idx = {}  # id → datos
    cat_p = OUT / "catastro_edificios.json"
    if cat_p.exists():
        with open(cat_p) as f:
            for b in json.load(f):
                if b.get("vertices"):
                    v = b["vertices"][0]
                    k = (round(v["x"]), round(v["z"]))
                    catastro_idx[k] = b
        print(f"  Catastro: {len(catastro_idx)} referencias")

    # Fusionar
    con_lidar = con_overture = con_catastro = 0

    for ed in edificios:
        OX, OZ = 1918, 8570
        verts = ed.get("vertices", [])
        cx = (sum(v["x"] for v in verts) / len(verts) + OX) if verts else OX
        cz = (sum(v["z"] for v in verts) / len(verts) + OZ) if verts else OZ

        # 1. LIDAR (máxima precisión)
        if ed["id"] in lidar_idx:
            ld = lidar_idx[ed["id"]]
            ed["lidar_z_min"]   = ld["lidar_z_min"]
            ed["lidar_z_max"]   = ld["lidar_z_max"]
            ed["lidar_altura"]  = ld["lidar_altura"]
            ed["lidar_forma"]   = ld["lidar_forma"]
            # Actualizar altura con dato LIDAR si es más fiable
            if ld["lidar_pts"] >= 5 and ld["lidar_altura"] > 2:
                ed["height"] = round(ld["lidar_altura"], 2)
            con_lidar += 1

        # 2. Overture (altura ML)
        k_ov = (round(cx), round(cz))
        if k_ov in overture_idx:
            ed["overture_height"] = overture_idx[k_ov]["height"]
            if not ed.get("lidar_altura") and overture_idx[k_ov]["height"] > 0:
                ed["height"] = round(overture_idx[k_ov]["height"], 2)
            con_overture += 1

        # 3. Catastro (año construcción)
        k_cat = (round(cx), round(cz))
        if k_cat in catastro_idx:
            cat = catastro_idx[k_cat]
            ed["anio_construccion"] = cat.get("anio", 0)
            con_catastro += 1

    out_p = OUT / "edificios_ultra.json"
    with open(out_p, "w", encoding="utf-8") as f:
        json.dump(edificios, f, ensure_ascii=False, separators=(",",":"))

    print(f"\n  ✅ edificios_ultra.json: {len(edificios)} edificios")
    print(f"     LIDAR: {con_lidar} | Overture: {con_overture} | Catastro: {con_catastro}")
    print(f"     Tamaño: {out_p.stat().st_size//1024}KB")


# ═══════════════════════════════════════════════════════════════════════════
#  ESTADO FINAL
# ═══════════════════════════════════════════════════════════════════════════

def mostrar_estado():
    archivos = {
        "edificios_ultra.json":          "Fusión completa (LIDAR+Overture+Catastro+OSM)",
        "lidar_buildings.json":          "Tejados LIDAR: altura, forma por edificio",
        "lidar_ground.xyz":              "Puntos suelo LIDAR (terreno 0.5m)",
        "lidar_trees.json":              "Árboles detectados por LIDAR",
        "terreno_2m.asc":                "DTM Navarra 2m (mejor que IGN 5m)",
        "catastro_edificios.json":       "Edificios Catastro INSPIRE (año construcción)",
        "idena_edificaciones.json":      "Edificaciones IDENA Navarra",
        "idena_arbolado.json":           "Arbolado urbano inventario IDENA",
        "idena_alumbrado.json":          "Alumbrado público IDENA",
        "portales_cartociudad.json":     "Portales exactos CartoCiudad",
        "overture_buildings.geojson":    "Footprints Overture Maps (ML)",
        "overture_alturas.json":         "Alturas ML de Overture",
        "microsoft_footprints.json":     "Footprints Microsoft AI (Bing)",
        "mapillary_imagenes.json":       "Posiciones fotos Mapillary",
        "mapillary_objetos.json":        "Objetos ML detectados (señales, etc.)",
        "tiles/pnoa2024/":               "Tiles PNOA-MA 2024 (25cm)",
        "lidar_tiles/":                  "Tiles LIDAR LAZ descargados",
    }

    print("\n╔══════════════════════════════════════════════════════════════╗")
    print("║  ESTADO DATOS ULTRA-PRECISOS — Alsasua/Altsasua             ║")
    print("╚══════════════════════════════════════════════════════════════╝")
    for nombre, desc in archivos.items():
        path = OUT / nombre
        if path.is_dir():
            n = len(list(path.glob("*")))
            estado = f"✅ {n} archivos" if n > 0 else "❌ Vacío"
        elif path.exists():
            kb = path.stat().st_size // 1024
            estado = f"✅ {kb}KB"
        else:
            estado = "❌ Pendiente"
        print(f"  {estado:18s}  {nombre:38s} {desc}")


# ═══════════════════════════════════════════════════════════════════════════
#  MAIN
# ═══════════════════════════════════════════════════════════════════════════

if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument("--mapillary-token", default="", help="Token API Mapillary (gratis)")
    parser.add_argument("--solo", default="", help="Ejecutar solo: lidar|catastro|idena|dtm|portales|overture|microsoft|mapillary|pnoa|fusion")
    args = parser.parse_args()

    print("╔═══════════════════════════════════════════════════════════════╗")
    print("║  PIPELINE DATOS ULTRA-PRECISOS — Alsasua/Altsasua v2.0       ║")
    print("║  10 fuentes de datos independientes                           ║")
    print("╚═══════════════════════════════════════════════════════════════╝\n")

    funcs = {
        "lidar":      descargar_lidar_tiles,
        "catastro":   descargar_catastro_inspire,
        "idena":      descargar_idena_wfs,
        "dtm":        descargar_idena_dtm_2m,
        "portales":   descargar_cartociudad,
        "overture":   descargar_overture,
        "microsoft":  descargar_microsoft_footprints,
        "mapillary":  lambda: descargar_mapillary(args.mapillary_token),
        "pnoa":       descargar_pnoa_2024,
        "fusion":     fusionar_todo,
    }

    if args.solo and args.solo in funcs:
        funcs[args.solo]()
    else:
        for nombre, fn in funcs.items():
            try:
                fn()
                time.sleep(0.5)
            except KeyboardInterrupt:
                print("\n⚠ Interrumpido por usuario")
                break
            except Exception as e:
                print(f"\n✗ Error en {nombre}: {e}")
                continue

    mostrar_estado()

    print("\n=== PRÓXIMOS PASOS EN UNITY ===")
    print("1. Tools → Alsasua → 🏔 Procesar Datos Ultra-Precisos")
    print("   (carga edificios_ultra.json + lidar_buildings.json + terreno_2m.asc)")
    print("2. Tools → Alsasua → Ortofoto → 🗺 Aplicar Ortofoto")
    print("3. Play → SceneBootstrapper construye el mundo completo")
