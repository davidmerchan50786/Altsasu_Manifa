#!/usr/bin/env python3
"""
Task 3 — Auditoría de cobertura Mapillary vs edificios catastro.
Un edificio tiene "cobertura suficiente" si ≥4 fotos a distancia ≤20m.
"""
import json
import math
import os

DATA_DIR = "/home/user/Altsasu_Manifa/Assets/AlsasuaData"
MAPILLARY_FILE = os.path.join(DATA_DIR, "mapillary_imagenes.json")
CATASTRO_FILE = os.path.join(DATA_DIR, "catastro_edificios.json")
OSM_FILE = os.path.join(DATA_DIR, "buildings_osm_rico.json")
OUT_FILE = os.path.join(DATA_DIR, "mapillary_coverage_report.json")

# Unity coord constants from CLAUDE.md
UTM_ORIGIN_E = 567951
UTM_ORIGIN_N = 4749902
UNITY_OX = 1918
UNITY_OZ = 8570

def unity_to_latlon_approx(ux, uz):
    """Approximate conversion: Unity → UTM → Lat/Lon (WGS84)"""
    # UTM coordinates
    utm_e = (ux - UNITY_OX) + UTM_ORIGIN_E
    utm_n = (uz - UNITY_OZ) + UTM_ORIGIN_N
    # Approximate UTM30N → WGS84 (valid for Navarra region)
    # Using simple formula for zone 30N
    lon = (utm_e - 500000) / (math.cos(math.radians(42.9)) * 6378137 * math.pi / 180)
    lat = utm_n / (6378137 * math.pi / 180)
    # More precise: use central meridian of zone 30 = -3 degrees
    lat_rad = math.atan(math.sinh(utm_n / 6378137))
    lat_deg = math.degrees(lat_rad)
    lon_deg = -3.0 + (utm_e - 500000) / (math.cos(math.radians(42.9)) * 111320)
    return lat_deg, lon_deg

def latlon_to_unity(lat, lon):
    """Approx: Lat/Lon → UTM → Unity coords"""
    # Simple approximation for Navarra
    utm_n = lat * 6378137 * math.pi / 180
    utm_e = 500000 + (lon - (-3.0)) * math.cos(math.radians(lat)) * 111320
    ux = (utm_e - UTM_ORIGIN_E) + UNITY_OX
    uz = (utm_n - UTM_ORIGIN_N) + UNITY_OZ
    return ux, uz

def haversine_m(lat1, lon1, lat2, lon2):
    """Distance in meters between two lat/lon points"""
    R = 6371000
    phi1, phi2 = math.radians(lat1), math.radians(lat2)
    dphi = math.radians(lat2 - lat1)
    dlambda = math.radians(lon2 - lon1)
    a = math.sin(dphi/2)**2 + math.cos(phi1)*math.cos(phi2)*math.sin(dlambda/2)**2
    return 2 * R * math.asin(math.sqrt(a))

def get_building_center_latlon(building):
    """Extract lat/lon center of a building from various formats"""
    # Try direct lat/lon
    if "lat" in building and "lon" in building:
        return float(building["lat"]), float(building["lon"])
    if "latitude" in building and "longitude" in building:
        return float(building["latitude"]), float(building["longitude"])

    # Try Unity coords
    if "unity_x" in building and "unity_z" in building:
        return unity_to_latlon_approx(building["unity_x"], building["unity_z"])
    if "x" in building and "z" in building:
        return unity_to_latlon_approx(building["x"], building["z"])

    # Try vertices (OSM format)
    if "vertices" in building and building["vertices"]:
        verts = building["vertices"]
        if isinstance(verts[0], (list, tuple)) and len(verts[0]) >= 2:
            # Check if they look like lat/lon or Unity coords
            avg_x = sum(v[0] for v in verts) / len(verts)
            avg_y = sum(v[1] for v in verts) / len(verts)
            if -180 <= avg_x <= 180 and -90 <= avg_y <= 90:
                return avg_y, avg_x  # lat, lon
            else:
                return unity_to_latlon_approx(avg_x, avg_y)
        elif isinstance(verts[0], dict):
            avg_x = sum(v.get("x", v.get("lon", 0)) for v in verts) / len(verts)
            avg_z = sum(v.get("z", v.get("lat", 0)) for v in verts) / len(verts)
            return unity_to_latlon_approx(avg_x, avg_z)

    # Try GeoJSON geometry
    if "geometry" in building:
        geom = building["geometry"]
        if geom and geom.get("type") == "Point":
            coords = geom["coordinates"]
            return coords[1], coords[0]
        elif geom and geom.get("type") in ("Polygon", "MultiPolygon"):
            coords = geom["coordinates"]
            if geom["type"] == "Polygon":
                ring = coords[0]
            else:
                ring = coords[0][0]
            avg_lon = sum(c[0] for c in ring) / len(ring)
            avg_lat = sum(c[1] for c in ring) / len(ring)
            return avg_lat, avg_lon

    return None, None

def get_photo_latlon(photo):
    """Extract lat/lon from a Mapillary photo record"""
    if "lat" in photo and "lon" in photo:
        return float(photo["lat"]), float(photo["lon"])
    if "latitude" in photo and "longitude" in photo:
        return float(photo["latitude"]), float(photo["longitude"])
    if "geometry" in photo:
        geom = photo["geometry"]
        if geom and "coordinates" in geom:
            c = geom["coordinates"]
            if len(c) >= 2:
                return float(c[1]), float(c[0])
    if "computed_geometry" in photo:
        geom = photo["computed_geometry"]
        if "coordinates" in geom:
            c = geom["coordinates"]
            return float(c[1]), float(c[0])
    return None, None

def load_buildings():
    """Load buildings from catastro or OSM as fallback"""
    # Try catastro first
    if os.path.exists(CATASTRO_FILE):
        try:
            with open(CATASTRO_FILE) as f:
                data = json.load(f)
            if isinstance(data, list) and len(data) > 0:
                print(f"Catastro: {len(data)} edificios cargados de {CATASTRO_FILE}")
                return data, "catastro"
            elif isinstance(data, dict) and "features" in data:
                buildings = data["features"]
                print(f"Catastro GeoJSON: {len(buildings)} edificios")
                return buildings, "catastro"
        except Exception as e:
            print(f"Error cargando catastro: {e}")

    # Fall back to OSM
    if os.path.exists(OSM_FILE):
        try:
            with open(OSM_FILE) as f:
                data = json.load(f)
            if isinstance(data, list):
                print(f"OSM: {len(data)} edificios cargados de {OSM_FILE}")
                return data, "osm"
        except Exception as e:
            print(f"Error cargando OSM: {e}")

    print("ERROR: No se encontraron datos de edificios")
    return [], "none"

def load_photos():
    """Load Mapillary photos"""
    if not os.path.exists(MAPILLARY_FILE):
        print(f"AVISO: No existe {MAPILLARY_FILE}")
        return []
    try:
        with open(MAPILLARY_FILE) as f:
            data = json.load(f)
        if isinstance(data, list):
            print(f"Mapillary: {len(data)} fotos cargadas")
            return data
        elif isinstance(data, dict):
            # Could be {"features": [...]} or {"images": [...]}
            photos = data.get("features") or data.get("images") or data.get("data") or []
            print(f"Mapillary dict: {len(photos)} fotos cargadas")
            return photos
    except Exception as e:
        print(f"Error cargando Mapillary: {e}")
    return []

if __name__ == "__main__":
    buildings, source = load_buildings()
    photos = load_photos()

    print(f"\nProcesando {len(buildings)} edificios y {len(photos)} fotos...")

    # Pre-extract photo locations
    photo_locations = []
    for ph in photos:
        lat, lon = get_photo_latlon(ph)
        if lat is not None and lon is not None:
            photo_locations.append((lat, lon))
    print(f"Fotos con coordenadas válidas: {len(photo_locations)}")

    # For each building, count photos within 20m
    edificios_con_cobertura = []
    edificios_sin_cobertura = []
    THRESHOLD_M = 20.0
    MIN_PHOTOS = 4

    for idx, building in enumerate(buildings):
        if idx % 100 == 0:
            print(f"  Procesando edificio {idx}/{len(buildings)}...")

        bid = (building.get("id") or building.get("ref") or
               building.get("properties", {}).get("id") if isinstance(building, dict) else str(idx))
        if bid is None:
            bid = str(idx)
        else:
            bid = str(bid)

        blat, blon = get_building_center_latlon(building)
        if blat is None:
            edificios_sin_cobertura.append(bid)
            continue

        # Count nearby photos
        nearby = 0
        for plat, plon in photo_locations:
            dist = haversine_m(blat, blon, plat, plon)
            if dist <= THRESHOLD_M:
                nearby += 1
                if nearby >= MIN_PHOTOS:
                    break

        if nearby >= MIN_PHOTOS:
            edificios_con_cobertura.append(bid)
        else:
            edificios_sin_cobertura.append(bid)

    total = len(buildings)
    con_cob = len(edificios_con_cobertura)
    pct = (con_cob / total * 100) if total > 0 else 0.0

    report = {
        "edificios_con_cobertura": edificios_con_cobertura,
        "edificios_sin_cobertura": edificios_sin_cobertura,
        "cobertura_porcentaje": round(pct, 2),
        "total_imagenes": len(photos),
        "total_imagenes_geolocalizadas": len(photo_locations),
        "total_edificios": total,
        "fuente_edificios": source,
        "umbral_distancia_m": THRESHOLD_M,
        "min_fotos_requeridas": MIN_PHOTOS
    }

    with open(OUT_FILE, "w") as f:
        json.dump(report, f, indent=2, ensure_ascii=False)

    print(f"\n=== RESULTADO ===")
    print(f"Total edificios: {total}")
    print(f"Con cobertura (≥{MIN_PHOTOS} fotos ≤{THRESHOLD_M}m): {con_cob}")
    print(f"Sin cobertura: {len(edificios_sin_cobertura)}")
    print(f"Porcentaje cobertura: {pct:.1f}%")
    print(f"Reporte guardado en: {OUT_FILE}")
