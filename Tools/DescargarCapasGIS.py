#!/usr/bin/env python3
"""
Tools/DescargarCapasGIS.py
Descarga TODAS las capas GIS de referencia para Alsasua y las convierte a un
formato plano que Unity puede leer con JsonUtility.

Fuentes:
  · IDENA Navarra WFS  — parcelas, caminos, patrimonio, aparcamientos, ferrocarril…
  · OSM Overpass API   — calles, edificios, amenities, usos de suelo, agua…

Salida: Assets/AlsasuaData/gis_layers/*.json
Formato de cada archivo:
  { "source":"IDENA|OSM", "typename":"…", "title":"…", "n":<int>,
    "features": [
      { "cat":"categoria", "nom":"label", "geom":"Point|LineString|Polygon",
        "coords": [E1,N1, E2,N2, …]  ← UTM30N EPSG:25830, metros }
    ]
  }
"""

import json, math, os, sys, time, urllib.request, urllib.parse
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT    = Path(__file__).parent.parent
OUT_DIR = ROOT / "Assets" / "AlsasuaData" / "gis_layers"
OUT_DIR.mkdir(parents=True, exist_ok=True)

# ── Extensión del mundo (±7300 m desde Herriko Plaza, un margen sobre ±7200) ──
E0, N0   = 567951.0, 4749902.0
HALF     = 7300.0
E_MIN, E_MAX = E0 - HALF, E0 + HALF   # 560 651 … 575 251
N_MIN, N_MAX = N0 - HALF, N0 + HALF   # 4 742 602 … 4 757 202
BBOX_UTM = f"{E_MIN:.0f},{N_MIN:.0f},{E_MAX:.0f},{N_MAX:.0f}"

# ── WGS84 → UTM30N ──────────────────────────────────────────────────────────
_REF_LON, _REF_LAT = -2.1853, 42.9005
_COS_LAT = math.cos(math.radians(_REF_LAT))
_M_LON   = 111320.0 * _COS_LAT   # ≈ 81 548 m / grado
_M_LAT   = 111320.0               # ≈ 111 320 m / grado

try:
    from pyproj import Transformer as _T
    _proj = _T.from_crs("EPSG:4326", "EPSG:25830", always_xy=True)
    def wgs84_utm(lon, lat): return _proj.transform(lon, lat)
    print("  [pyproj] conversión WGS84→UTM precisa activa")
except ImportError:
    # Aproximación lineal: error máximo ~30 m en los bordes del mundo.
    # Suficiente para un overlay de construcción de referencia.
    def wgs84_utm(lon, lat):
        return (E0 + (lon - _REF_LON) * _M_LON,
                N0 + (lat - _REF_LAT) * _M_LAT)

def en_bbox(E, N):
    return E_MIN <= E <= E_MAX and N_MIN <= N <= N_MAX

# ── BBox en WGS84 para Overpass ─────────────────────────────────────────────
LAT_MIN = _REF_LAT - HALF / _M_LAT
LAT_MAX = _REF_LAT + HALF / _M_LAT
LON_MIN = _REF_LON - HALF / _M_LON
LON_MAX = _REF_LON + HALF / _M_LON
BBOX_OSM = f"{LAT_MIN:.5f},{LON_MIN:.5f},{LAT_MAX:.5f},{LON_MAX:.5f}"

# ── HTTP ─────────────────────────────────────────────────────────────────────
def get_url(url, timeout=120, post_data=None, reintentos=3):
    for i in range(reintentos):
        try:
            req = urllib.request.Request(
                url, data=post_data,
                headers={"User-Agent": "DescargarCapasGIS/1.0 altsasu-manifa"})
            with urllib.request.urlopen(req, timeout=timeout) as r:
                return r.read()
        except Exception as e:
            print(f"    [intento {i+1}/{reintentos}] {e}")
            if i < reintentos - 1:
                time.sleep(3 * (i + 1))
    return None

# ── Guardar capa ─────────────────────────────────────────────────────────────
def guardar(nombre, source, typename, title, features):
    if not features:
        print(f"    0 features → omitida")
        return 0
    data = {"source": source, "typename": typename, "title": title,
            "n": len(features), "features": features}
    path = OUT_DIR / f"{nombre}.json"
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, separators=(",", ":"))
    kb = path.stat().st_size // 1024
    print(f"    ✅ {len(features)} features → {path.name} ({kb} KB)")
    return len(features)

# ── Convertir GeoJSON (EPSG:25830) → features planos ────────────────────────
def geojson_a_features(geojson_bytes, cat_fn):
    """cat_fn(properties) → string categoría"""
    try:
        gj = json.loads(geojson_bytes)
    except Exception as e:
        print(f"    parse error: {e}")
        return []

    out = []
    for feat in gj.get("features", []):
        geom  = feat.get("geometry") or {}
        props = feat.get("properties") or {}
        cat   = cat_fn(props)
        nom   = _label_gis(props)
        tipo  = geom.get("type", "")
        raw   = geom.get("coordinates", [])

        def pts_plano(ring):
            flat = []
            for pt in ring:
                flat += [round(float(pt[0]), 2), round(float(pt[1]), 2)]
            return flat

        if tipo == "Point":
            E, N = float(raw[0]), float(raw[1])
            if en_bbox(E, N):
                out.append({"cat": cat, "nom": nom, "geom": "Point",
                            "coords": [round(E,2), round(N,2)]})
        elif tipo == "MultiPoint":
            for pt in raw:
                E, N = float(pt[0]), float(pt[1])
                if en_bbox(E, N):
                    out.append({"cat": cat, "nom": nom, "geom": "Point",
                                "coords": [round(E,2), round(N,2)]})
        elif tipo == "LineString":
            flat = pts_plano(raw)
            if flat: out.append({"cat": cat, "nom": nom, "geom": "LineString", "coords": flat})
        elif tipo == "MultiLineString":
            for line in raw:
                flat = pts_plano(line)
                if flat: out.append({"cat": cat, "nom": nom, "geom": "LineString", "coords": flat})
        elif tipo == "Polygon":
            if raw:
                flat = pts_plano(raw[0])   # solo anillo exterior
                if flat: out.append({"cat": cat, "nom": nom, "geom": "Polygon", "coords": flat})
        elif tipo == "MultiPolygon":
            for poly in raw:
                if poly:
                    flat = pts_plano(poly[0])
                    if flat: out.append({"cat": cat, "nom": nom, "geom": "Polygon", "coords": flat})
    return out

def _label_gis(props):
    for k in ("NOMBRE","nombre","NAME","name","LABEL","label",
              "REFCAT","refcat","CODBICE","codbice","ref","id"):
        v = props.get(k)
        if v: return str(v)[:64]
    return ""

# ════════════════════════════════════════════════════════════════════════════
# IDENA WFS
# ════════════════════════════════════════════════════════════════════════════
IDENA = "https://idena.navarra.es/ogc/wfs"

_KW_OK  = ["camino","camin","viario","carret","ctra","senda","pecuari","ferroca",
           "parcel","catast","aparcam","ocup","suelo","patrim","cementer","cultur",
           "monumen","edifici","nucleo","peatonal","hidro","canaliz","rio","limite",
           "munici","pobl","hospital","sanidad","educaci","escuela","dotaci",
           "jardín","jardin","parque","verde","cubierta"]
_KW_NO  = ["raster","imagen","foto","rgb","nir","tif","orto","esrigrids","wcs"]

def _cat_idena(typename):
    t = typename.lower()
    if "catast" in t and "parcela" in t:
        if "urba" in t: return "parcela_urb"
        if "rusti" in t or "rura" in t: return "parcela_rur"
        return "parcela_mix"
    if "patrim" in t and "bic" in t: return "patrimonio_bic"
    if "aparcam" in t: return "aparcamiento"
    if ("carret" in t or "ctra" in t) and "lin" in t: return "carretera"
    if "senda" in t: return "sendero"
    if "pecuari" in t: return "pecuaria"
    if "camino" in t or "camin" in t: return "camino"
    if "ferroca" in t: return "ferrocarril"
    if "cementer" in t: return "cementerio"
    if "edifici" in t or "cubierta" in t: return "edificio"
    if "parque" in t or "verde" in t: return "parque"
    if "rio" in t or "hidro" in t or "canaliz" in t: return "agua"
    if "limite" in t or "munici" in t: return "limite_admin"
    if "nucleo" in t or "pobl" in t: return "nucleo_urbano"
    if "suelo" in t or "ocup" in t: return "uso_suelo"
    if "hospital" in t or "sanidad" in t: return "sanidad"
    if "educaci" in t or "escuela" in t: return "educacion"
    return "idena_otro"

def descargar_idena():
    print("\n══ IDENA WFS ══")
    caps_data = get_url(
        f"{IDENA}?SERVICE=WFS&VERSION=2.0.0&REQUEST=GetCapabilities", timeout=60)
    if not caps_data:
        print("  ERROR obteniendo GetCapabilities"); return

    try:
        root = ET.fromstring(caps_data)
    except ET.ParseError as e:
        print(f"  ERROR parseando XML: {e}"); return

    ftypes = []
    for ft in root.iter():
        # Compatible con WFS 1.x y 2.x
        if ft.tag.endswith("}FeatureType") or ft.tag == "FeatureType":
            name_el  = next((c for c in ft if c.tag.endswith("}Name")  or c.tag=="Name"),  None)
            title_el = next((c for c in ft if c.tag.endswith("}Title") or c.tag=="Title"), None)
            if name_el is not None and name_el.text:
                ftypes.append((name_el.text.strip(),
                               (title_el.text or name_el.text).strip()))

    print(f"  {len(ftypes)} feature types en IDENA")

    relevantes = [
        (n, t) for n, t in ftypes
        if any(k in (n+t).lower() for k in _KW_OK)
        and not any(k in (n+t).lower() for k in _KW_NO)
    ]
    print(f"  {len(relevantes)} capas relevantes")

    ok = 0
    for typename, title in relevantes:
        cat  = _cat_idena(typename)
        safe = typename.replace(":", "_").replace("/", "_")
        print(f"  {typename[:55]:<55}", end=" ", flush=True)

        url = (f"{IDENA}?SERVICE=WFS&VERSION=2.0.0&REQUEST=GetFeature"
               f"&TypeName={urllib.parse.quote(typename)}"
               f"&BBOX={BBOX_UTM},urn:ogc:def:crs:EPSG::25830"
               f"&SRSNAME=EPSG:25830"
               f"&OUTPUTFORMAT=application%2Fjson"
               f"&COUNT=100000")

        data = get_url(url, timeout=150)
        if not data:
            print("ERROR red"); continue
        if data[:1] != b'{':
            print("no JSON → omitida"); continue

        features = geojson_a_features(data, lambda p, c=cat: c)
        if guardar(f"idena_{safe}", "IDENA", typename, title, features):
            ok += 1

        time.sleep(0.4)

    print(f"\n  → {ok} capas IDENA guardadas")

# ════════════════════════════════════════════════════════════════════════════
# OSM Overpass
# ════════════════════════════════════════════════════════════════════════════
OVERPASS = "https://overpass-api.de/api/interpreter"

_OSM_QUERIES = {
    "calles": f"""
[out:json][timeout:90][bbox:{BBOX_OSM}];
(way[highway~"^(motorway|trunk|primary|secondary|tertiary|unclassified|residential|service|living_street|road)$"];
)->._;out body geom qt;""",

    "peatonal": f"""
[out:json][timeout:90][bbox:{BBOX_OSM}];
(way[highway~"^(pedestrian|footway|steps|path|track|bridleway|cycleway)$"];
 way[place=square];)->._;out body geom qt;""",

    "ferrocarril": f"""
[out:json][timeout:90][bbox:{BBOX_OSM}];
(way[railway];node[railway~"^(station|halt|stop|tram_stop)$"];
)->._;out body geom qt;""",

    "edificios": f"""
[out:json][timeout:90][bbox:{BBOX_OSM}];
(way[building];)->._;out body geom qt;""",

    "amenities": f"""
[out:json][timeout:90][bbox:{BBOX_OSM}];
(node[amenity~"^(parking|grave_yard|place_of_worship|school|university|kindergarten|college|hospital|clinic|pharmacy|doctors|bar|restaurant|cafe|pub|fast_food|bank|post_office|library|community_centre|police|fire_station|townhall|fuel|atm)$"];
 way[amenity~"^(parking|grave_yard|school|university|hospital)$"];
 node[shop];
 node[tourism~"^(attraction|monument|museum|artwork|hotel|hostel|information)$"];
 node[historic~"^(monument|memorial|castle|ruins|wayside_shrine|wayside_cross|chapel|church)$"];
 node[leisure~"^(park|playground|sports_centre|pitch|swimming_pool)$"];
 way[leisure~"^(park|playground|sports_centre|pitch)$"];
)->._;out body geom qt;""",

    "uso_suelo": f"""
[out:json][timeout:90][bbox:{BBOX_OSM}];
(way[landuse~"^(farmland|farmyard|meadow|orchard|vineyard|grass|greenfield|recreation_ground|allotments|forest|cemetery|industrial|commercial|retail|residential|military|quarry)$"];
 way[natural~"^(wood|scrub|heath|grassland|water|wetland|cliff|scree)$"];
)->._;out body geom qt;""",

    "agua": f"""
[out:json][timeout:90][bbox:{BBOX_OSM}];
(way[waterway~"^(river|stream|canal|drain|ditch)$"];
 way[natural=water];
 node[natural=spring];
)->._;out body geom qt;""",
}

def _cat_osm(tags):
    hw  = tags.get("highway","")
    rw  = tags.get("railway","")
    am  = tags.get("amenity","")
    sh  = tags.get("shop","")
    lu  = tags.get("landuse","")
    nat = tags.get("natural","")
    lei = tags.get("leisure","")
    hi  = tags.get("historic","")
    pl  = tags.get("place","")
    bu  = tags.get("building","")
    ww  = tags.get("waterway","")
    to  = tags.get("tourism","")

    if rw: return "ferrocarril"
    if hw in ("motorway","trunk","primary","secondary"): return "carretera_principal"
    if hw in ("tertiary","unclassified","residential","service","living_street"): return "carretera"
    if hw in ("pedestrian","footway"): return "peatonal"
    if hw in ("path","track","bridleway","steps"): return "camino"
    if hw == "cycleway": return "ciclovia"
    if pl == "square": return "plaza"
    if am == "parking" or lu == "garages": return "aparcamiento"
    if am in ("grave_yard",) or lu == "cemetery": return "cementerio"
    if am == "place_of_worship" or hi in ("chapel","wayside_shrine","wayside_cross"): return "religioso"
    if am in ("bar","pub","restaurant","cafe","fast_food"): return "hosteleria"
    if sh: return "comercio"
    if am in ("school","university","kindergarten","college"): return "educacion"
    if am in ("hospital","clinic","pharmacy","doctors"): return "sanidad"
    if am in ("townhall","police","fire_station","post_office","library"): return "equipamiento"
    if lei in ("park","garden"): return "parque"
    if lei in ("pitch","sports_centre","playground","stadium"): return "deporte"
    if to in ("attraction","monument","museum","artwork"): return "patrimonio"
    if hi: return "patrimonio"
    if bu: return "edificio"
    if ww or nat == "water": return "agua"
    if lu in ("forest",) or nat in ("wood","scrub"): return "bosque"
    if lu in ("farmland","farmyard","meadow","orchard","vineyard"): return "agricola"
    if lu in ("grass","greenfield","recreation_ground"): return "verde"
    return "osm_otro"

def osm_a_features(osm_json):
    features = []
    for el in osm_json.get("elements", []):
        t    = el.get("type")
        tags = el.get("tags") or {}
        cat  = _cat_osm(tags)
        nom  = _label_gis(tags)

        if t == "node":
            if "lat" not in el: continue
            E, N = wgs84_utm(el["lon"], el["lat"])
            if en_bbox(E, N):
                features.append({"cat": cat, "nom": nom, "geom": "Point",
                                  "coords": [round(E,2), round(N,2)]})

        elif t == "way":
            pts = el.get("geometry", [])
            if not pts: continue
            flat = []
            for pt in pts:
                E, N = wgs84_utm(pt["lon"], pt["lat"])
                flat += [round(E,2), round(N,2)]
            if not flat: continue
            cerrado = (len(pts) >= 4 and
                       abs(pts[0]["lat"]-pts[-1]["lat"]) < 1e-7 and
                       abs(pts[0]["lon"]-pts[-1]["lon"]) < 1e-7)
            geom_t = "Polygon" if cerrado else "LineString"
            features.append({"cat": cat, "nom": nom, "geom": geom_t, "coords": flat})

        elif t == "relation":
            # multipolygon: anillos outer solamente
            for member in el.get("members", []):
                if member.get("role","") == "outer":
                    pts = member.get("geometry", [])
                    if not pts: continue
                    flat = []
                    for pt in pts:
                        E, N = wgs84_utm(pt["lon"], pt["lat"])
                        flat += [round(E,2), round(N,2)]
                    if flat:
                        features.append({"cat": cat, "nom": nom, "geom": "Polygon", "coords": flat})

    return features

def descargar_osm():
    print("\n══ OSM Overpass ══")
    ok = 0
    for nombre, query in _OSM_QUERIES.items():
        print(f"  osm_{nombre}...", end=" ", flush=True)
        q_bytes = urllib.parse.urlencode({"data": query.strip()}).encode()
        data = get_url(OVERPASS, post_data=q_bytes, timeout=150)
        if not data:
            print("ERROR"); continue
        try:
            osm = json.loads(data)
        except Exception as e:
            print(f"parse error: {e}"); continue
        features = osm_a_features(osm)
        if guardar(f"osm_{nombre}", "OSM", f"osm_{nombre}", nombre.capitalize(), features):
            ok += 1
        time.sleep(1.5)   # cortesía con Overpass
    print(f"\n  → {ok} capas OSM guardadas")

# ════════════════════════════════════════════════════════════════════════════
if __name__ == "__main__":
    print("=" * 60)
    print("DescargarCapasGIS.py — Alsasua Manifa")
    print(f"Bbox UTM30N : {BBOX_UTM}")
    print(f"Bbox WGS84  : {BBOX_OSM}")
    print(f"Salida      : {OUT_DIR}")
    print("=" * 60)

    descargar_idena()
    descargar_osm()

    archivos = sorted(OUT_DIR.glob("*.json"))
    total_kb = sum(f.stat().st_size for f in archivos) // 1024
    print(f"\n{'='*60}")
    print(f"LISTO: {len(archivos)} archivos, {total_kb} KB total")
    for f in archivos:
        kb = f.stat().st_size // 1024
        print(f"  {f.name:<55} {kb:>6} KB")
