#!/usr/bin/env python3
"""
Task 4 — Reporte de fusión de fuentes de datos de edificios.
Compara catastro_edificios.json, buildings_osm_rico.json, overture_buildings_2024.geojson
Para cada edificio del catastro reporta: fuentes que lo tienen, altura Overture, fotos Mapillary.
"""
import json
import math
import os

DATA_DIR = "/home/user/Altsasu_Manifa/Assets/AlsasuaData"

FILES = {
    "catastro": os.path.join(DATA_DIR, "catastro_edificios.json"),
    "osm": os.path.join(DATA_DIR, "buildings_osm_rico.json"),
    "overture_2024": os.path.join(DATA_DIR, "overture_buildings_2024.geojson"),
    "overture_old": os.path.join(DATA_DIR, "overture_buildings.geojson"),
    "lidar": os.path.join(DATA_DIR, "lidar_buildings.json"),
    "mapillary": os.path.join(DATA_DIR, "mapillary_imagenes.json"),
    "coverage_report": os.path.join(DATA_DIR, "mapillary_coverage_report.json"),
}

OUT_FILE = os.path.join(DATA_DIR, "data_sources_report.json")

UTM_ORIGIN_E = 567951
UTM_ORIGIN_N = 4749902
UNITY_OX = 1918
UNITY_OZ = 8570

def haversine_m(lat1, lon1, lat2, lon2):
    R = 6371000
    phi1, phi2 = math.radians(lat1), math.radians(lat2)
    dphi = math.radians(lat2 - lat1)
    dlambda = math.radians(lon2 - lon1)
    a = math.sin(dphi/2)**2 + math.cos(phi1)*math.cos(phi2)*math.sin(dlambda/2)**2
    return 2 * R * math.asin(math.sqrt(a))

LAT_ORIGIN = 42.898703
LON_ORIGIN = -2.167699

def osm_rel_to_latlon(vx, vz):
    """OSM-relative meter offset from Herriko Plaza → (lat, lon)."""
    lat = LAT_ORIGIN + vz / 111320.0
    lon = LON_ORIGIN + vx / (111320.0 * math.cos(math.radians(LAT_ORIGIN)))
    return lat, lon

def unity_to_latlon_approx(ux, uz):
    return osm_rel_to_latlon(ux, uz)

def get_center_latlon(item):
    """Universal center extractor for various building formats"""
    if isinstance(item, dict):
        # Direct lat/lon
        if "lat" in item and "lon" in item:
            return float(item["lat"]), float(item["lon"])
        # GeoJSON feature
        geom = item.get("geometry") or item.get("geom")
        props = item.get("properties", item)
        if geom:
            t = geom.get("type", "")
            c = geom.get("coordinates", [])
            if t == "Point" and len(c) >= 2:
                return float(c[1]), float(c[0])
            elif t == "Polygon" and c:
                ring = c[0]
                return (sum(p[1] for p in ring)/len(ring),
                        sum(p[0] for p in ring)/len(ring))
            elif t == "MultiPolygon" and c:
                ring = c[0][0]
                return (sum(p[1] for p in ring)/len(ring),
                        sum(p[0] for p in ring)/len(ring))
        # Unity coords
        ux = item.get("unity_x") or item.get("x")
        uz = item.get("unity_z") or item.get("z")
        if ux is not None and uz is not None:
            return unity_to_latlon_approx(float(ux), float(uz))
        # Vertices array (OSM format: list of {x, z} in OSM-relative meters)
        verts = item.get("vertices", [])
        if verts:
            if isinstance(verts[0], dict) and "x" in verts[0] and "z" in verts[0]:
                avg_x = sum(v["x"] for v in verts) / len(verts)
                avg_z = sum(v["z"] for v in verts) / len(verts)
                return osm_rel_to_latlon(avg_x, avg_z)
            elif isinstance(verts[0], (list, tuple)):
                avg_x = sum(v[0] for v in verts) / len(verts)
                avg_y = sum(v[1] for v in verts) / len(verts)
                if abs(avg_x) <= 180 and abs(avg_y) <= 90:
                    return avg_y, avg_x
                return osm_rel_to_latlon(avg_x, avg_y)
    return None, None

def load_json_file(path, label):
    if not os.path.exists(path):
        print(f"  FALTA: {path}")
        return None
    try:
        with open(path) as f:
            data = json.load(f)
        if isinstance(data, list):
            print(f"  {label}: {len(data)} items (lista)")
            return data
        elif isinstance(data, dict):
            features = data.get("features", [])
            if features is not None:
                print(f"  {label}: {len(features)} features (FeatureCollection)")
                return features
            print(f"  {label}: dict con keys {list(data.keys())[:5]}")
            return data
    except Exception as e:
        print(f"  Error cargando {label}: {e}")
        return None

def build_spatial_index(items, label):
    """Build list of (lat, lon, item) for spatial lookup"""
    index = []
    missing = 0
    for item in (items or []):
        lat, lon = get_center_latlon(item)
        if lat is not None:
            index.append((lat, lon, item))
        else:
            missing += 1
    print(f"  {label}: {len(index)} con coords, {missing} sin coords")
    return index

def find_nearby(lat, lon, index, threshold_m=50):
    """Find items within threshold_m meters"""
    nearby = []
    for ilat, ilon, item in index:
        d = haversine_m(lat, lon, ilat, ilon)
        if d <= threshold_m:
            nearby.append((d, item))
    return sorted(nearby, key=lambda x: x[0])

def get_building_id(b, source):
    """Extract a stable ID from a building"""
    if isinstance(b, dict):
        for key in ["id", "ref", "osm_id", "catref", "localId"]:
            val = b.get(key)
            if val:
                return str(val)
        props = b.get("properties", {})
        if props:
            for key in ["id", "ref", "localId", "REFCAT"]:
                val = props.get(key)
                if val:
                    return str(val)
    return None

def get_height(b, source):
    """Extract height from building in various formats"""
    if isinstance(b, dict):
        for key in ["height", "building:height", "lidar_altura", "altura"]:
            v = b.get(key)
            if v is not None:
                return v
        props = b.get("properties", {})
        if props:
            for key in ["height", "building:height", "num_floors", "levels"]:
                v = props.get(key)
                if v is not None:
                    return v
    return None

if __name__ == "__main__":
    print("Cargando fuentes de datos...")

    catastro = load_json_file(FILES["catastro"], "catastro")
    osm = load_json_file(FILES["osm"], "osm")
    overture_2024 = load_json_file(FILES["overture_2024"], "overture_2024")
    overture_old = load_json_file(FILES["overture_old"], "overture_old")
    lidar = load_json_file(FILES["lidar"], "lidar")
    mapillary_raw = load_json_file(FILES["mapillary"], "mapillary")
    if os.path.exists(FILES["coverage_report"]):
        with open(FILES["coverage_report"]) as _f:
            coverage = json.load(_f)
        print(f"  coverage_report: dict con {len(coverage.get('edificios_con_cobertura',[]))} edificios con cobertura")
    else:
        coverage = None

    # Use OSM as catastro fallback if needed
    primary_buildings = catastro
    primary_label = "catastro"
    if not primary_buildings or len(primary_buildings) == 0:
        primary_buildings = osm
        primary_label = "osm"
        print("  Usando OSM como fuente primaria (catastro vacío)")

    # Build ID-based indices for sources sharing OSM IDs
    print("\nConstruyendo índices...")
    osm_by_id = {str(b.get("id", b.get("osm_id", ""))): b for b in (osm or []) if b.get("id") or b.get("osm_id")}
    lidar_by_id = {str(b.get("id", "")): b for b in (lidar or []) if b.get("id")}
    overture_by_osm_id = {}
    for feat in (overture_2024 or []):
        props = feat.get("properties", {})
        osm_id = str(props.get("osm_id", ""))
        if osm_id:
            overture_by_osm_id[osm_id] = feat
    print(f"  osm_by_id: {len(osm_by_id)}")
    print(f"  lidar_by_id: {len(lidar_by_id)}")
    print(f"  overture_by_osm_id: {len(overture_by_osm_id)}")

    # Also build spatial index for overture (for catastro matching via lat/lon)
    idx_overture_2024 = build_spatial_index(overture_2024, "overture_2024") if overture_2024 else []
    idx_overture_old = []

    # Mapillary coverage lookup
    edificios_con_cobertura = set()
    if coverage and isinstance(coverage, dict):
        edificios_con_cobertura = set(coverage.get("edificios_con_cobertura", []))

    # Load mapillary photos for photo counting
    photos = []
    if mapillary_raw:
        raw = mapillary_raw
        if isinstance(raw, list):
            photos = raw
        elif isinstance(raw, dict):
            photos = raw.get("features") or raw.get("images") or []

    photo_locs = []
    for ph in photos:
        if isinstance(ph, dict):
            lat = ph.get("lat") or ph.get("latitude")
            lon = ph.get("lon") or ph.get("longitude")
            if lat is None and "geometry" in ph:
                geom = ph["geometry"]
                if geom and "coordinates" in geom:
                    c = geom["coordinates"]
                    lat, lon = c[1], c[0]
            if lat is not None:
                photo_locs.append((float(lat), float(lon)))

    print(f"\nProcesando {len(primary_buildings or [])} edificios primarios...")

    # Stats
    source_stats = {
        "catastro": len(catastro or []),
        "osm": len(osm or []),
        "overture_2024": len(overture_2024 or []),
        "overture_old": len(overture_old or []),
        "lidar": len(lidar or []),
        "mapillary_fotos": len(photos),
    }

    buildings_report = []
    match_threshold = 30  # metros

    count_with_osm = 0
    count_with_overture_2024 = 0
    count_with_lidar = 0
    count_with_height_overture = 0
    count_with_mapillary = 0

    for idx_b, building in enumerate(primary_buildings or []):
        if idx_b % 200 == 0:
            print(f"  {idx_b}/{len(primary_buildings)}...")

        bid = get_building_id(building, primary_label) or str(idx_b)
        blat, blon = get_center_latlon(building)

        entry = {
            "id": bid,
            "lat": blat,
            "lon": blon,
            "fuente_primaria": primary_label,
            "en_osm": False,
            "en_overture_2024": False,
            "en_overture_old": False,
            "en_lidar": False,
            "altura_overture_2024": None,
            "altura_lidar": None,
            "altura_osm": None,
            "fotos_mapillary_cercanas": 0,
            "cobertura_mapillary": False,
        }

        if blat is None:
            buildings_report.append(entry)
            continue

        # Check OSM (same IDs as catastro/overture)
        osm_match = osm_by_id.get(bid)
        if osm_match:
            entry["en_osm"] = True
            entry["altura_osm"] = get_height(osm_match, "osm")
            count_with_osm += 1

        # Check Overture 2024 (match by osm_id first, then spatial)
        ov_match = overture_by_osm_id.get(bid)
        if ov_match:
            entry["en_overture_2024"] = True
            h = get_height(ov_match, "overture_2024")
            entry["altura_overture_2024"] = h
            if h is not None:
                count_with_height_overture += 1
            count_with_overture_2024 += 1
        elif blat is not None:
            ov24_near = find_nearby(blat, blon, idx_overture_2024, match_threshold)
            if ov24_near:
                entry["en_overture_2024"] = True
                h = get_height(ov24_near[0][1], "overture_2024")
                entry["altura_overture_2024"] = h
                if h is not None:
                    count_with_height_overture += 1
                count_with_overture_2024 += 1

        # Check LIDAR (match by ID, same OSM IDs)
        lidar_match = lidar_by_id.get(bid)
        if not lidar_match and bid.isdigit():
            lidar_match = lidar_by_id.get(bid)
        if lidar_match:
            entry["en_lidar"] = True
            entry["altura_lidar"] = lidar_match.get("lidar_altura")
            count_with_lidar += 1

        # Mapillary coverage
        if bid in edificios_con_cobertura:
            entry["cobertura_mapillary"] = True
            count_with_mapillary += 1
        else:
            # Count nearby photos directly
            nearby_photos = sum(
                1 for plat, plon in photo_locs
                if haversine_m(blat, blon, plat, plon) <= 20
            )
            entry["fotos_mapillary_cercanas"] = nearby_photos
            if nearby_photos >= 4:
                entry["cobertura_mapillary"] = True
                count_with_mapillary += 1

        buildings_report.append(entry)

    total = len(buildings_report)
    report = {
        "resumen": {
            "total_edificios_catastro": source_stats["catastro"],
            "total_edificios_osm": source_stats["osm"],
            "total_edificios_overture_2024": source_stats["overture_2024"],
            "total_edificios_overture_old": source_stats["overture_old"],
            "total_edificios_lidar": source_stats["lidar"],
            "total_fotos_mapillary": source_stats["mapillary_fotos"],
            "fuente_primaria_usada": primary_label,
            "total_procesados": total,
            "con_match_osm": count_with_osm,
            "con_match_overture_2024": count_with_overture_2024,
            "con_match_lidar": count_with_lidar,
            "con_altura_overture_2024": count_with_height_overture,
            "con_cobertura_mapillary": count_with_mapillary,
            "pct_cobertura_osm": round(count_with_osm / total * 100, 1) if total > 0 else 0,
            "pct_cobertura_overture": round(count_with_overture_2024 / total * 100, 1) if total > 0 else 0,
            "pct_cobertura_lidar": round(count_with_lidar / total * 100, 1) if total > 0 else 0,
            "pct_cobertura_mapillary": round(count_with_mapillary / total * 100, 1) if total > 0 else 0,
        },
        "edificios": buildings_report
    }

    with open(OUT_FILE, "w") as f:
        json.dump(report, f, indent=2, ensure_ascii=False)

    print(f"\n=== RESUMEN FUSIÓN ===")
    for k, v in report["resumen"].items():
        print(f"  {k}: {v}")
    print(f"\nReporte guardado en: {OUT_FILE}")
