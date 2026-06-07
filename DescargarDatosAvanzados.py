# DescargarDatosAvanzados.py
# ═══════════════════════════════════════════════════════════════════════════
#  Descarga y enriquece los datos geoespaciales de Alsasua/Altsasua
#  con la máxima precisión posible desde todas las fuentes disponibles.
#
#  FUENTES:
#  1. OSM Overpass — tags completos de cada edificio (building:material,
#     roof:material, roof:colour, start_date, amenity, etc.)
#  2. Catastro España INSPIRE WFS — footprints catastrales exactos (cm)
#  3. IGN WCS capabilities — encuentra el nombre correcto del DSM
#  4. IGN PNOA DSM (si disponible) — superficie con tejados reales 5m
#  5. IDENA Navarra WCS — capas de suelo urbano / superficie de calles
#  6. OSM surface tags — tipo de pavimento en cada tramo de calle
#
#  RESULTADO:
#    Assets/AlsasuaData/buildings_completo.json  — edificios con todo
#    Assets/AlsasuaData/calles_superficie.json   — calles con surface tag
#    Assets/AlsasuaData/catastro_parcelas.json   — parcelas catastrales
#    Assets/AlsasuaData/dsm_alsasua_5m.asc       — DSM si se puede bajar
# ═══════════════════════════════════════════════════════════════════════════

import requests, json, math, time, os, sys, zipfile, io, re
from pathlib import Path

# ── Coordenadas Alsasua ────────────────────────────────────────────────────
LAT_MIN, LAT_MAX = 42.870, 42.930
LON_MIN, LON_MAX = -2.220, -2.130
BBOX_STR  = f"{LAT_MIN},{LON_MIN},{LAT_MAX},{LON_MAX}"   # Overpass S,W,N,E
BBOX_WGS  = f"{LON_MIN},{LAT_MIN},{LON_MAX},{LAT_MAX}"   # WFS W,S,E,N

LAT0, LON0 = 42.8987, -2.1677          # Herriko Plaza
M_LAT, M_LON = 111320.0, 76400.0       # metros por grado en lat/lon

OUT_DIR = Path("Assets/AlsasuaData")
OUT_DIR.mkdir(parents=True, exist_ok=True)

# ═══════════════════════════════════════════════════════════════════════════
#  UTILIDADES
# ═══════════════════════════════════════════════════════════════════════════

def geo_a_unity(lat, lon):
    x = (lon - LON0) * M_LON
    z = (lat - LAT0) * M_LAT
    return x, z

def descarga(url, params=None, timeout=90, label=""):
    try:
        r = requests.get(url, params=params, timeout=timeout)
        r.raise_for_status()
        print(f"  ✓ {label or url[:60]}  ({len(r.content)//1024}KB)")
        return r
    except Exception as e:
        print(f"  ✗ {label or url[:60]}: {e}")
        return None

# ═══════════════════════════════════════════════════════════════════════════
#  1. OSM OVERPASS — EDIFICIOS CON TODOS LOS TAGS
# ═══════════════════════════════════════════════════════════════════════════

def descargar_osm_edificios_completo():
    print("\n=== 1. OSM edificios con tags completos ===")

    query = f"""
[out:json][timeout:120];
(
  way["building"]({BBOX_STR});
  relation["building"]["type"="multipolygon"]({BBOX_STR});
);
out body geom;
"""
    r = descarga("https://overpass-api.de/api/interpreter",
                 params={"data": query}, timeout=120, label="OSM edificios completo")
    if not r:
        # Espejo alternativo
        r = descarga("https://overpass.kumi.systems/api/interpreter",
                     params={"data": query}, timeout=120, label="OSM (espejo kumi)")
    if not r:
        print("  Saltando OSM completo")
        return

    data = r.json()
    edificios = []

    for el in data.get("elements", []):
        if el.get("type") not in ("way", "relation"):
            continue
        tags = el.get("tags", {})
        if "building" not in tags:
            continue

        # Extraer geometría
        if el["type"] == "way" and "geometry" in el:
            geom = el["geometry"]
        elif el["type"] == "relation" and "members" in el:
            # Outer ring del multipolygon
            outer = next((m for m in el["members"] if m.get("role") == "outer"), None)
            if not outer or "geometry" not in outer:
                continue
            geom = outer["geometry"]
        else:
            continue

        # Convertir coordenadas a Unity XZ
        verts = []
        for node in geom:
            ux, uz = geo_a_unity(node["lat"], node["lon"])
            verts.append({"x": round(ux, 3), "z": round(uz, 3)})

        if len(verts) < 3:
            continue

        # Extraer todos los tags relevantes
        try:
            height = float(tags.get("height") or
                           (float(tags.get("building:levels", 2)) * 3.2))
        except:
            height = 6.4

        edif = {
            "id":               el["id"],
            "type":             tags.get("building", "yes"),
            "name":             tags.get("name") or tags.get("name:eu") or "",
            "levels":           int(float(tags.get("building:levels", 2))),
            "height":           round(height, 2),
            "material":         tags.get("building:material", ""),
            "material_detail":  tags.get("building:material:description", ""),
            "roof_material":    tags.get("roof:material", ""),
            "roof_colour":      tags.get("roof:colour", ""),
            "roof_shape":       tags.get("roof:shape", ""),
            "colour":           tags.get("building:colour", ""),
            "start_date":       tags.get("start_date", ""),
            "amenity":          tags.get("amenity", ""),
            "addr_street":      tags.get("addr:street", ""),
            "addr_housenumber": tags.get("addr:housenumber", ""),
            "vertices":         verts,
        }
        edificios.append(edif)

    out_path = OUT_DIR / "buildings_completo.json"
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(edificios, f, ensure_ascii=False, indent=2)

    # Estadísticas de tags
    con_mat  = sum(1 for e in edificios if e["material"])
    con_roof = sum(1 for e in edificios if e["roof_material"] or e["roof_colour"])
    tipos    = {}
    mats     = {}
    for e in edificios:
        tipos[e["type"]] = tipos.get(e["type"], 0) + 1
        if e["material"]:
            mats[e["material"]] = mats.get(e["material"], 0) + 1

    print(f"  Edificios: {len(edificios)}")
    print(f"  Con building:material: {con_mat}")
    print(f"  Con roof:material/colour: {con_roof}")
    print(f"  Tipos: {dict(sorted(tipos.items(), key=lambda x:-x[1])[:8])}")
    print(f"  Materiales: {mats}")
    print(f"  → {out_path}")

# ═══════════════════════════════════════════════════════════════════════════
#  2. OSM OVERPASS — CALLES CON SURFACE
# ═══════════════════════════════════════════════════════════════════════════

def descargar_osm_calles_completo():
    print("\n=== 2. OSM calles con surface y tags completos ===")

    query = f"""
[out:json][timeout:90];
(
  way["highway"]({BBOX_STR});
);
out body geom;
"""
    r = descarga("https://overpass-api.de/api/interpreter",
                 params={"data": query}, timeout=90, label="OSM calles completo")
    if not r:
        r = descarga("https://overpass.kumi.systems/api/interpreter",
                     params={"data": query}, timeout=90, label="OSM calles (kumi)")
    if not r:
        return

    data  = r.json()
    calles = []

    ANCHOS = {
        "motorway":4,"trunk":3.5,"primary":3,"secondary":3,
        "tertiary":2.5,"unclassified":2,"residential":1.6,
        "living_street":1.4,"service":1.4,"pedestrian":4,
        "footway":1.2,"path":0.8,"cycleway":1.2,"track":1.5,
    }

    for el in data.get("elements", []):
        if el.get("type") != "way" or "geometry" not in el:
            continue
        tags = el.get("tags", {})
        hw   = tags.get("highway", "")
        if not hw:
            continue

        pts = []
        for node in el["geometry"]:
            ux, uz = geo_a_unity(node["lat"], node["lon"])
            pts.append({"x": round(ux, 3), "z": round(uz, 3)})

        if len(pts) < 2:
            continue

        calle = {
            "id":      el["id"],
            "type":    hw,
            "name":    tags.get("name") or tags.get("name:eu") or "",
            "width":   float(tags.get("width") or ANCHOS.get(hw, 2.0)),
            "lanes":   int(tags.get("lanes") or (2 if hw in ("primary","secondary") else 1)),
            "surface": tags.get("surface", ""),
            "oneway":  tags.get("oneway", "no") == "yes",
            "maxspeed": tags.get("maxspeed", ""),
            "sidewalk": tags.get("sidewalk", ""),
            "points":  pts,
        }
        calles.append(calle)

    out_path = OUT_DIR / "calles_superficie.json"
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(calles, f, ensure_ascii=False, indent=2)

    superficies = {}
    for c in calles:
        s = c["surface"] or "unknown"
        superficies[s] = superficies.get(s, 0) + 1

    print(f"  Calles: {len(calles)}")
    print(f"  Superficies: {dict(sorted(superficies.items(), key=lambda x:-x[1]))}")
    print(f"  → {out_path}")

# ═══════════════════════════════════════════════════════════════════════════
#  3. CATASTRO ESPAÑA — FOOTPRINTS EXACTOS (INSPIRE WFS)
# ═══════════════════════════════════════════════════════════════════════════

def descargar_catastro():
    print("\n=== 3. Catastro España — parcelas y edificios ===")

    # INSPIRE Building WFS del Catastro
    url = "https://ovc.catastro.meh.es/OGCRS/WFS.aspx"
    params = {
        "SERVICE":    "WFS",
        "REQUEST":    "GetFeature",
        "VERSION":    "2.0.0",
        "TYPENAMES":  "bu:Building",
        "COUNT":      "5000",
        "BBOX":       f"{BBOX_WGS},EPSG:4326",
        "OUTPUTFORMAT": "application/json",
    }

    r = descarga(url, params=params, timeout=60, label="Catastro WFS edificios")
    if r and r.content and r.content[:1] == b"{":
        try:
            fc = r.json()
            features = fc.get("features", [])
            parcelas = []

            for f in features:
                geom  = f.get("geometry", {})
                props = f.get("properties", {})

                if geom.get("type") != "Polygon":
                    continue

                coords = geom["coordinates"][0]  # outer ring
                verts  = []
                for lon, lat in coords:
                    ux, uz = geo_a_unity(lat, lon)
                    verts.append({"x": round(ux, 2), "z": round(uz, 2)})

                if len(verts) < 3:
                    continue

                parcela = {
                    "id":           props.get("gml_id", ""),
                    "uso":          props.get("currentUse", ""),
                    "fecha":        props.get("conditionOfConstruction", ""),
                    "altura":       props.get("numberOfBuildingUnits", 0),
                    "vertices":     verts,
                }
                parcelas.append(parcela)

            out_path = OUT_DIR / "catastro_parcelas.json"
            with open(out_path, "w", encoding="utf-8") as f:
                json.dump(parcelas, f, ensure_ascii=False, indent=2)
            print(f"  Parcelas catastrales: {len(parcelas)}")
            print(f"  → {out_path}")
            return
        except Exception as e:
            print(f"  Catastro JSON parse: {e}")

    # Alternativa: INSPIRE Atom feed (ZIP con GML)
    atom_url = "https://www.catastro.meh.es/INSPIRE/buildings/31/31250_ALTSASUA.zip"
    r2 = descarga(atom_url, timeout=120, label="Catastro Atom ZIP (Altsasua 31250)")
    if r2:
        try:
            z = zipfile.ZipFile(io.BytesIO(r2.content))
            names = z.namelist()
            print(f"  ZIP contiene: {names}")
            # Guardar el GML/GeoJSON para procesamiento posterior
            for name in names:
                if name.endswith(".json") or name.endswith(".gml") or name.endswith(".gfs"):
                    with open(OUT_DIR / Path(name).name, "wb") as fw:
                        fw.write(z.read(name))
                    print(f"  Extraído: {name}")
        except Exception as e:
            print(f"  ZIP error: {e}")

# ═══════════════════════════════════════════════════════════════════════════
#  4. IGN WCS — ENCONTRAR NOMBRE CORRECTO DEL DSM + DESCARGAR
# ═══════════════════════════════════════════════════════════════════════════

def descargar_ign_dsm():
    print("\n=== 4. IGN WCS — DSM (superficie con edificios) ===")

    # Primero: GetCapabilities para ver coberturas disponibles
    r = descarga(
        "https://servicios.idee.es/wcs-inspire/mdt",
        params={"SERVICE": "WCS", "VERSION": "1.0.0", "REQUEST": "GetCapabilities"},
        timeout=30,
        label="IGN WCS GetCapabilities"
    )

    dsm_coverage = None
    if r:
        text = r.text
        # Buscar coberturas relacionadas con MDS/superficie
        for nombre in re.findall(r"<name>(.*?)</name>", text):
            n_low = nombre.lower()
            if any(k in n_low for k in ["mds", "superficie", "surface", "mdsnorm", "dsm"]):
                print(f"  Cobertura candidata DSM: {nombre}")
                if dsm_coverage is None:
                    dsm_coverage = nombre

        if dsm_coverage is None:
            # Mostrar todas las coberturas disponibles
            coberturas = re.findall(r"<name>(.*?)</name>", text)
            print(f"  Coberturas disponibles: {coberturas[:20]}")

    # Probar URLs conocidas del DSM
    DSM_CANDIDATOS = [
        dsm_coverage,
        "MDSNorm_CSN5",
        "Elevacion4258_MDSNorm",
        "Elevacion4258_MDS_5",
        "MDS_PNOA_5",
        "MDSNormInterpol_CSNA5_ETRS89",
    ]

    for cobertura in DSM_CANDIDATOS:
        if not cobertura:
            continue

        dsm_url = (
            "https://servicios.idee.es/wcs-inspire/mdt?SERVICE=WCS&VERSION=1.0.0"
            f"&REQUEST=GetCoverage&COVERAGE={cobertura}&CRS=EPSG:4326"
            f"&BBOX={LON_MIN},{LAT_MIN},{LON_MAX},{LAT_MAX}"
            f"&WIDTH=1400&HEIGHT=800&FORMAT=ArcGrid"
        )
        r = descarga(dsm_url, timeout=60, label=f"IGN DSM ({cobertura})")
        if r and r.content and not r.content.startswith(b"<?xml"):
            # Es un ASC de verdad
            out_path = OUT_DIR / "dsm_alsasua_5m.asc"
            with open(out_path, "wb") as f:
                f.write(r.content)
            print(f"  ✓ DSM descargado: {len(r.content)//1024}KB → {out_path}")
            return

        elif r and r.content.startswith(b"<?xml"):
            # XML de error — leer mensaje
            msg = re.search(r"ServiceException[^>]*>(.*?)</", r.text, re.DOTALL)
            if msg:
                print(f"    Error WCS: {msg.group(1).strip()[:100]}")
            continue

    # Alternativa: MDT05 completo (DTM ya lo tenemos, prueba hoja directa)
    print("  Probando CNIG FTP directo para MDS hoja 114...")
    hoja_url = "https://centrodedescargas.cnig.es/CentroDescargas/downloadFile.do?seq=MDS05-ASC-ETR89-114"
    r = descarga(hoja_url, timeout=120, label="CNIG MDS05 hoja 114")
    if r and r.content and len(r.content) > 10000:
        try:
            z = zipfile.ZipFile(io.BytesIO(r.content))
            for name in z.namelist():
                if name.lower().endswith(".asc"):
                    with open(OUT_DIR / "dsm_alsasua_5m.asc", "wb") as f:
                        f.write(z.read(name))
                    print(f"  ✓ DSM extraído del ZIP: {name}")
                    return
        except:
            with open(OUT_DIR / "dsm_alsasua_5m_raw.bin", "wb") as f:
                f.write(r.content)
            print(f"  Guardado como raw para inspección")

    print("  DSM no disponible via WCS/FTP automático.")
    print("  Descarga manual en: https://centrodedescargas.cnig.es/")
    print("  → Buscador → MDS05 → Hoja 0114")

# ═══════════════════════════════════════════════════════════════════════════
#  5. LIDAR PNOA NAVARRA — NUBE DE PUNTOS LAZ
# ═══════════════════════════════════════════════════════════════════════════

def descargar_lidar():
    print("\n=== 5. LIDAR PNOA Navarra — nube de puntos ===")

    # Tiles de LIDAR para Alsasua (UTM30N ETRS89):
    # Alsasua está aprox en UTM 574000-577000 E, 4750000-4754000 N
    # Las teselas PNOA son 2x2km nominadas como 0488_3 etc.
    candidatos = [
        # CNIG LIDAR PNOA descarga directa
        "https://centrodedescargas.cnig.es/CentroDescargas/downloadFile.do?seq=PNOA-LIDAR-NA-0488-3",
        "https://centrodedescargas.cnig.es/CentroDescargas/downloadFile.do?seq=PNOA-LIDAR-NA-0487-4",
        # IDENA Navarra WCS
        ("https://idena.navarra.es/ogc/wcs?SERVICE=WCS&VERSION=1.0.0&REQUEST=GetCoverage"
         "&COVERAGE=NUPNTS5&CRS=EPSG:25830"
         "&BBOX=571000,4749000,577000,4754000&FORMAT=PointCloudLAZ"),
    ]

    for url in candidatos:
        r = descarga(url, timeout=120, label=f"LIDAR {url[60:90]}")
        if r and r.content and len(r.content) > 50000:
            out_path = OUT_DIR / "lidar_alsasua.laz"
            # Si es ZIP
            if r.content[:2] == b"PK":
                try:
                    z = zipfile.ZipFile(io.BytesIO(r.content))
                    for name in z.namelist():
                        if name.lower().endswith(".laz") or name.lower().endswith(".las"):
                            with open(out_path, "wb") as f:
                                f.write(z.read(name))
                            print(f"  ✓ LAZ extraído: {name}")
                            return
                except:
                    pass
            else:
                with open(out_path, "wb") as f:
                    f.write(r.content)
                print(f"  ✓ LAZ guardado: {len(r.content)//1024}KB")
                return

    print("  LIDAR no disponible via URL directa.")
    print("  Descarga manual:")
    print("    https://centrodedescargas.cnig.es/ → PNOA LIDAR → Navarra → Alsasua")
    print("    https://idena.navarra.es/descargas/ → Nube de Puntos PNOA")

# ═══════════════════════════════════════════════════════════════════════════
#  6. FUSIONAR buildings_rico.json + buildings_completo.json
# ═══════════════════════════════════════════════════════════════════════════

def fusionar_datos():
    print("\n=== 6. Fusionando datos edificios ===")

    rico_path     = OUT_DIR / "buildings_rico.json"
    completo_path = OUT_DIR / "buildings_completo.json"
    unity_path    = OUT_DIR / "buildings_unity.json"

    # Cargar base
    base = []
    if completo_path.exists():
        with open(completo_path) as f:
            base = json.load(f)
        print(f"  Base: buildings_completo.json  ({len(base)} edificios)")
    elif unity_path.exists():
        with open(unity_path) as f:
            base = json.load(f)
        print(f"  Base: buildings_unity.json  ({len(base)} edificios)")
    else:
        print("  Sin datos base, saltando fusión")
        return

    # Cargar colores reales
    rico = {}
    if rico_path.exists():
        with open(rico_path) as f:
            for b in json.load(f):
                rico[b["id"]] = b

    # Fusionar
    fusionados = []
    for b in base:
        merged = dict(b)
        if b["id"] in rico:
            r = rico[b["id"]]
            merged["roof_r_real"] = r.get("roof_r_real", 0)
            merged["roof_g_real"] = r.get("roof_g_real", 0)
            merged["roof_b_real"] = r.get("roof_b_real", 0)
            merged["roof_tipo_real"] = r.get("roof_tipo_real", "")
            merged["mat_r"] = r.get("mat_r", 0)
            merged["mat_g"] = r.get("mat_g", 0)
            merged["mat_b"] = r.get("mat_b", 0)
        # Añadir material OSM si viene del completo
        if not merged.get("material"):
            merged["material"] = ""
        fusionados.append(merged)

    out_path = OUT_DIR / "buildings_final.json"
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(fusionados, f, ensure_ascii=False, indent=2)

    con_mat_osm  = sum(1 for b in fusionados if b.get("material"))
    con_color    = sum(1 for b in fusionados if b.get("roof_r_real", 0) > 0)
    print(f"  Edificios fusionados: {len(fusionados)}")
    print(f"  Con material OSM: {con_mat_osm}")
    print(f"  Con color real: {con_color}")
    print(f"  → {out_path}")

# ═══════════════════════════════════════════════════════════════════════════
#  7. ESTADÍSTICAS FINALES
# ═══════════════════════════════════════════════════════════════════════════

def mostrar_estado():
    print("\n=== ESTADO FINAL DE DATOS ===")
    archivos = {
        "buildings_final.json":    "Edificios fusionados (OSM + colores reales)",
        "buildings_completo.json": "Edificios OSM con todos los tags",
        "buildings_rico.json":     "Colores reales de tejados (ortofoto)",
        "buildings_unity.json":    "Edificios OSM base (Unity XZ)",
        "calles_superficie.json":  "Calles con tipo de superficie",
        "catastro_parcelas.json":  "Parcelas catastrales exactas",
        "dsm_alsasua_5m.asc":      "DSM IGN — superficie con tejados (5m)",
        "dtm_alsasua_5m.asc":      "DTM IGN — terreno puro (5m)",
        "lidar_alsasua.laz":       "LIDAR PNOA nube de puntos",
        "orto_tiles_meta.json":    "Metadatos 72 teselas ortofoto",
        "tiles/orto/":             "72 teselas JPEG ortofoto (25cm/px)",
    }
    for nombre, desc in archivos.items():
        path = OUT_DIR / nombre
        if path.is_dir():
            n = len(list(path.glob("*")))
            print(f"  {'✅' if n > 0 else '❌'}  {nombre:38s} {n} archivos — {desc}")
        elif path.exists():
            kb = path.stat().st_size // 1024
            print(f"  ✅  {nombre:38s} {kb:6d}KB — {desc}")
        else:
            print(f"  ❌  {nombre:38s}        — {desc}")

# ═══════════════════════════════════════════════════════════════════════════
#  MAIN
# ═══════════════════════════════════════════════════════════════════════════

if __name__ == "__main__":
    print("╔══════════════════════════════════════════════════════════╗")
    print("║  DESCARGADOR DATOS AVANZADOS — Alsasua/Altsasua          ║")
    print("║  Máxima precisión desde todas las fuentes disponibles    ║")
    print("╚══════════════════════════════════════════════════════════╝")

    descargar_osm_edificios_completo()
    time.sleep(2)   # respetar límites de rate Overpass
    descargar_osm_calles_completo()
    time.sleep(2)
    descargar_catastro()
    descargar_ign_dsm()
    descargar_lidar()
    fusionar_datos()
    mostrar_estado()

    print("\n=== PRÓXIMOS PASOS EN UNITY ===")
    print("1. En Unity: Tools → Alsasua → Ortofoto → 🗺 Aplicar Ortofoto")
    print("2. En Unity: Tools → Alsasua → 🏔 Procesar DSM/LIDAR")
    print("3. En Unity: Tools → Alsasua → Escena → 🎬 Crear Escena Principal")
    print("4. Ejecutar Play — edificios con colores y materiales reales")
