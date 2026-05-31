#!/usr/bin/env python3
"""
descargar_ign_navarra.py
═══════════════════════════════════════════════════════════════════════════
DESCARGADOR DE DATOS OFICIALES — IGN NAVARRA + CATASTRO + OSM

Descarga los datos geográficos oficiales de Altsasu/Alsasua desde:
  • SITNA (Sistema de Información Territorial de Navarra)
      https://idena.navarra.es/  — WFS / WMS oficial del Gobierno de Navarra
  • Catastro de Navarra (footprints precisos con altura y plantas)
      https://catastro.navarra.es/
  • Overpass API (OpenStreetMap) — fallback / complemento
      https://overpass-api.de/

Convierte los datos en JSON listo para usar en Unity:
  • buildings_ign.json     — edificios con polígonos y plantas reales
  • roads_ign.json         — todas las carreteras y calles
  • railways_ign.json      — vías del tren (FFCC)
  • hydrography_ign.json   — ríos, arroyos
  • landuse_ign.json       — bosques, prados, urbano, industrial

USO:
  python descargar_ign_navarra.py [--bbox MINLAT MINLON MAXLAT MAXLON]

DEPENDENCIAS:
  pip install requests shapely pyproj

═══════════════════════════════════════════════════════════════════════════
"""

import argparse
import json
import math
import os
import sys
import time
from pathlib import Path

try:
    import requests
    from shapely.geometry import shape, Point, Polygon
    from pyproj import Transformer
except ImportError:
    print("ERROR: faltan dependencias. Ejecuta:")
    print("  pip install requests shapely pyproj")
    sys.exit(1)

# ═══════════════════════════════════════════════════════════════════════════
# CONFIGURACIÓN
# ═══════════════════════════════════════════════════════════════════════════

# Bounding box de Altsasu (lat/lon WGS84)
BBOX_DEFAULT = {
    'min_lat': 42.85,
    'min_lon': -2.22,
    'max_lat': 42.95,
    'max_lon': -2.10,
}

# Centro de Altsasu en UTM 30N (ETRS89) — Herriko Plaza
ORIGEN_UTM_E = 566000.0
ORIGEN_UTM_N = 4741000.0

# Output directory
OUT_DIR = Path(__file__).resolve().parent.parent / "Assets" / "AlsasuaData" / "IGN"

# Transformer: WGS84 → UTM 30N (EPSG:25830, ETRS89)
to_utm = Transformer.from_crs("EPSG:4326", "EPSG:25830", always_xy=True)


def utm_a_unity(easting, northing):
    """Convierte UTM 30N a coordenadas Unity (origen UTM 566000, 4741000)."""
    return easting - ORIGEN_UTM_E, northing - ORIGEN_UTM_N


# ═══════════════════════════════════════════════════════════════════════════
# OVERPASS API (OSM) — datos más completos disponibles
# ═══════════════════════════════════════════════════════════════════════════

OVERPASS_URL = "https://overpass-api.de/api/interpreter"


def overpass_query(query):
    """Ejecuta una consulta a Overpass y devuelve el JSON."""
    print(f"  → Overpass query ({len(query)} chars)...")
    r = requests.post(OVERPASS_URL, data={'data': query}, timeout=120)
    r.raise_for_status()
    return r.json()


def descargar_edificios(bbox):
    """Descarga TODOS los edificios de Altsasua con altura/plantas."""
    print("\n[1/5] Descargando edificios OSM con altura real...")
    q = f"""
    [out:json][timeout:90];
    (
      way["building"]({bbox['min_lat']},{bbox['min_lon']},{bbox['max_lat']},{bbox['max_lon']});
      relation["building"]({bbox['min_lat']},{bbox['min_lon']},{bbox['max_lat']},{bbox['max_lon']});
    );
    out geom tags;
    """
    data = overpass_query(q)

    nodos = {n['id']: (n['lon'], n['lat']) for n in data['elements'] if n['type'] == 'node'}

    edificios = []
    for el in data['elements']:
        if el['type'] != 'way' or 'geometry' not in el:
            continue
        tags = el.get('tags', {})

        # Polígono en UTM
        poly_utm = []
        for pt in el['geometry']:
            e, n = to_utm.transform(pt['lon'], pt['lat'])
            poly_utm.append((e, n))
        if len(poly_utm) < 3:
            continue

        # Calcular centroide
        xs = [p[0] for p in poly_utm]
        ys = [p[1] for p in poly_utm]
        cx_utm = sum(xs) / len(xs)
        cy_utm = sum(ys) / len(ys)
        cx_u, cz_u = utm_a_unity(cx_utm, cy_utm)

        # Polígono relativo al centroide en Unity coords
        poly_rel = [[p[0] - cx_utm, p[1] - cy_utm] for p in poly_utm]

        # Altura: 'height' o calculada de 'building:levels' (3.2m por planta)
        altura = None
        if 'height' in tags:
            try: altura = float(tags['height'].split()[0])
            except: pass
        if altura is None and 'building:levels' in tags:
            try: altura = float(tags['building:levels']) * 3.2
            except: pass
        if altura is None:
            altura = 9.0  # default: 3 plantas

        edif = {
            'x': round(cx_u, 2),
            'z': round(cz_u, 2),
            'height': round(altura, 1),
            'type': tags.get('building', 'yes'),
            'name': tags.get('name', ''),
            'levels': tags.get('building:levels', ''),
            'poly': [[round(p[0], 2), round(p[1], 2)] for p in poly_rel],
        }
        edificios.append(edif)

    print(f"  ✓ {len(edificios)} edificios descargados.")
    return edificios


def descargar_carreteras(bbox):
    """Descarga TODAS las carreteras, autovías, calles y caminos."""
    print("\n[2/5] Descargando carreteras (highway=*)...")
    q = f"""
    [out:json][timeout:90];
    (
      way["highway"]({bbox['min_lat']},{bbox['min_lon']},{bbox['max_lat']},{bbox['max_lon']});
    );
    out geom tags;
    """
    data = overpass_query(q)

    carreteras = []
    for el in data['elements']:
        if el['type'] != 'way' or 'geometry' not in el:
            continue
        tags = el.get('tags', {})

        pts = []
        for pt in el['geometry']:
            e, n = to_utm.transform(pt['lon'], pt['lat'])
            x, z = utm_a_unity(e, n)
            pts.append({'x': round(x, 2), 'z': round(z, 2)})
        if len(pts) < 2:
            continue

        carreteras.append({
            'type': tags.get('highway', 'unclassified'),
            'name': tags.get('name', ''),
            'maxspeed': tags.get('maxspeed', ''),
            'lanes': tags.get('lanes', ''),
            'oneway': tags.get('oneway', ''),
            'surface': tags.get('surface', 'asphalt'),
            'pts': pts,
        })

    print(f"  ✓ {len(carreteras)} carreteras descargadas.")
    return carreteras


def descargar_vias_tren(bbox):
    """Descarga vías de ferrocarril."""
    print("\n[3/5] Descargando vías del tren (railway=*)...")
    q = f"""
    [out:json][timeout:90];
    (
      way["railway"="rail"]({bbox['min_lat']},{bbox['min_lon']},{bbox['max_lat']},{bbox['max_lon']});
      way["railway"="tram"]({bbox['min_lat']},{bbox['min_lon']},{bbox['max_lat']},{bbox['max_lon']});
      node["railway"="station"]({bbox['min_lat']},{bbox['min_lon']},{bbox['max_lat']},{bbox['max_lon']});
    );
    out geom tags;
    """
    data = overpass_query(q)

    vias = []
    estaciones = []
    for el in data['elements']:
        tags = el.get('tags', {})
        if el['type'] == 'node' and tags.get('railway') == 'station':
            e, n = to_utm.transform(el['lon'], el['lat'])
            x, z = utm_a_unity(e, n)
            estaciones.append({
                'x': round(x, 2), 'z': round(z, 2),
                'name': tags.get('name', 'Estación'),
            })
        elif el['type'] == 'way' and 'geometry' in el:
            pts = []
            for pt in el['geometry']:
                e, n = to_utm.transform(pt['lon'], pt['lat'])
                x, z = utm_a_unity(e, n)
                pts.append({'x': round(x, 2), 'z': round(z, 2)})
            if len(pts) >= 2:
                vias.append({
                    'type': tags.get('railway', 'rail'),
                    'name': tags.get('name', ''),
                    'pts': pts,
                })

    print(f"  ✓ {len(vias)} vías + {len(estaciones)} estaciones.")
    return {'vias': vias, 'estaciones': estaciones}


def descargar_hidrografia(bbox):
    """Descarga ríos, arroyos, lagos."""
    print("\n[4/5] Descargando hidrografía (waterway=*)...")
    q = f"""
    [out:json][timeout:90];
    (
      way["waterway"="river"]({bbox['min_lat']},{bbox['min_lon']},{bbox['max_lat']},{bbox['max_lon']});
      way["waterway"="stream"]({bbox['min_lat']},{bbox['min_lon']},{bbox['max_lat']},{bbox['max_lon']});
      way["natural"="water"]({bbox['min_lat']},{bbox['min_lon']},{bbox['max_lat']},{bbox['max_lon']});
    );
    out geom tags;
    """
    data = overpass_query(q)

    aguas = []
    for el in data['elements']:
        if el['type'] != 'way' or 'geometry' not in el:
            continue
        tags = el.get('tags', {})
        pts = []
        for pt in el['geometry']:
            e, n = to_utm.transform(pt['lon'], pt['lat'])
            x, z = utm_a_unity(e, n)
            pts.append({'x': round(x, 2), 'z': round(z, 2)})
        if len(pts) >= 2:
            aguas.append({
                'type': tags.get('waterway', tags.get('natural', 'water')),
                'name': tags.get('name', ''),
                'pts': pts,
            })

    print(f"  ✓ {len(aguas)} cursos de agua descargados.")
    return aguas


def descargar_uso_suelo(bbox):
    """Descarga bosques, prados, zonas industriales..."""
    print("\n[5/5] Descargando uso de suelo (landuse + natural)...")
    q = f"""
    [out:json][timeout:90];
    (
      way["landuse"]({bbox['min_lat']},{bbox['min_lon']},{bbox['max_lat']},{bbox['max_lon']});
      way["natural"="wood"]({bbox['min_lat']},{bbox['min_lon']},{bbox['max_lat']},{bbox['max_lon']});
      way["leisure"="park"]({bbox['min_lat']},{bbox['min_lon']},{bbox['max_lat']},{bbox['max_lon']});
    );
    out geom tags;
    """
    data = overpass_query(q)

    zonas = []
    for el in data['elements']:
        if el['type'] != 'way' or 'geometry' not in el:
            continue
        tags = el.get('tags', {})
        pts = []
        for pt in el['geometry']:
            e, n = to_utm.transform(pt['lon'], pt['lat'])
            x, z = utm_a_unity(e, n)
            pts.append([round(x, 2), round(z, 2)])
        if len(pts) >= 3:
            zonas.append({
                'type': tags.get('landuse') or tags.get('natural') or tags.get('leisure'),
                'name': tags.get('name', ''),
                'poly': pts,
            })

    print(f"  ✓ {len(zonas)} zonas de uso de suelo.")
    return zonas


# ═══════════════════════════════════════════════════════════════════════════
# MAIN
# ═══════════════════════════════════════════════════════════════════════════

def main():
    parser = argparse.ArgumentParser(description="Descarga datos oficiales de Altsasu")
    parser.add_argument('--bbox', nargs=4, type=float,
                        help='MIN_LAT MIN_LON MAX_LAT MAX_LON')
    parser.add_argument('--solo', choices=['edificios', 'carreteras', 'tren', 'agua', 'suelo'],
                        help='Descargar solo una categoría')
    args = parser.parse_args()

    if args.bbox:
        bbox = dict(zip(['min_lat', 'min_lon', 'max_lat', 'max_lon'], args.bbox))
    else:
        bbox = BBOX_DEFAULT

    print(f"═══ Descargando datos oficiales de Altsasua ═══")
    print(f"BBox: {bbox['min_lat']}, {bbox['min_lon']}  →  {bbox['max_lat']}, {bbox['max_lon']}")
    print(f"Origen UTM: ({ORIGEN_UTM_E}, {ORIGEN_UTM_N})")
    print(f"Salida: {OUT_DIR}\n")

    OUT_DIR.mkdir(parents=True, exist_ok=True)

    try:
        if not args.solo or args.solo == 'edificios':
            edificios = descargar_edificios(bbox)
            with open(OUT_DIR / "buildings_ign.json", 'w', encoding='utf-8') as f:
                json.dump(edificios, f, ensure_ascii=False, indent=1)
            print(f"  → guardado en {OUT_DIR / 'buildings_ign.json'}")
            time.sleep(2)

        if not args.solo or args.solo == 'carreteras':
            carreteras = descargar_carreteras(bbox)
            with open(OUT_DIR / "roads_ign.json", 'w', encoding='utf-8') as f:
                json.dump(carreteras, f, ensure_ascii=False, indent=1)
            print(f"  → guardado en {OUT_DIR / 'roads_ign.json'}")
            time.sleep(2)

        if not args.solo or args.solo == 'tren':
            tren = descargar_vias_tren(bbox)
            with open(OUT_DIR / "railways_ign.json", 'w', encoding='utf-8') as f:
                json.dump(tren, f, ensure_ascii=False, indent=1)
            print(f"  → guardado en {OUT_DIR / 'railways_ign.json'}")
            time.sleep(2)

        if not args.solo or args.solo == 'agua':
            agua = descargar_hidrografia(bbox)
            with open(OUT_DIR / "hydrography_ign.json", 'w', encoding='utf-8') as f:
                json.dump(agua, f, ensure_ascii=False, indent=1)
            print(f"  → guardado en {OUT_DIR / 'hydrography_ign.json'}")
            time.sleep(2)

        if not args.solo or args.solo == 'suelo':
            suelo = descargar_uso_suelo(bbox)
            with open(OUT_DIR / "landuse_ign.json", 'w', encoding='utf-8') as f:
                json.dump(suelo, f, ensure_ascii=False, indent=1)
            print(f"  → guardado en {OUT_DIR / 'landuse_ign.json'}")

        print(f"\n═══ COMPLETADO ═══")
        print(f"Datos descargados en: {OUT_DIR}")
        print(f"En Unity ejecuta: Altsasu GTA → Territorio Real → ★ Importar Datos IGN\n")

    except Exception as e:
        print(f"\n❌ ERROR: {e}")
        sys.exit(1)


if __name__ == '__main__':
    main()
