#!/usr/bin/env python3
"""
Task 2b — Sin acceso a red, genera catastro_navarra_inspire.geojson y
catastro_edificios.json desde datos OSM+LIDAR locales en formato INSPIRE.
Documenta todos los intentos fallidos.
"""
import json
import os
import math

DATA_DIR = "/home/user/Altsasu_Manifa/Assets/AlsasuaData"
OSM_FILE = os.path.join(DATA_DIR, "buildings_osm_rico.json")
LIDAR_FILE = os.path.join(DATA_DIR, "lidar_buildings.json")
OUT_INSPIRE = os.path.join(DATA_DIR, "catastro_navarra_inspire.geojson")
OUT_CATASTRO = os.path.join(DATA_DIR, "catastro_edificios.json")

UTM_ORIGIN_E = 567951
UTM_ORIGIN_N = 4749902
UNITY_OX = 1918
UNITY_OZ = 8570

LAT_ORIGIN = 42.898703
LON_ORIGIN = -2.167699
UTM_E_ORIGIN = 567951
UTM_N_ORIGIN = 4749902

def unity_to_wgs84(vx, vz):
    """
    Building vertices are OSM-relative: Herriko Plaza = (0,0) in meters.
    Returns (lon, lat) for GeoJSON coordinates.
    """
    lat = LAT_ORIGIN + vz / 111320.0
    lon = LON_ORIGIN + vx / (111320.0 * math.cos(math.radians(LAT_ORIGIN)))
    return lon, lat

def unity_to_utm(ux, uz):
    utm_e = (ux - UNITY_OX) + UTM_ORIGIN_E
    utm_n = (uz - UNITY_OZ) + UTM_ORIGIN_N
    return utm_e, utm_n

def vertices_to_polygon_wgs84(vertices):
    coords = []
    for v in vertices:
        if isinstance(v, dict):
            lon, lat = unity_to_wgs84(v["x"], v["z"])
            coords.append([round(lon, 7), round(lat, 7)])
        elif isinstance(v, (list, tuple)):
            lon, lat = unity_to_wgs84(v[0], v[1])
            coords.append([round(lon, 7), round(lat, 7)])
    if len(coords) < 3:
        return None
    if coords[0] != coords[-1]:
        coords.append(coords[0])
    return {"type": "Polygon", "coordinates": [coords]}

def vertices_to_polygon_utm(vertices):
    """UTM 25830 polygon"""
    coords = []
    for v in vertices:
        if isinstance(v, dict):
            e, n = unity_to_utm(v["x"], v["z"])
            coords.append([round(e, 2), round(n, 2)])
        elif isinstance(v, (list, tuple)):
            e, n = unity_to_utm(v[0], v[1])
            coords.append([round(e, 2), round(n, 2)])
    if len(coords) < 3:
        return None
    if coords[0] != coords[-1]:
        coords.append(coords[0])
    return {"type": "Polygon", "coordinates": [coords]}

def get_centroid_wgs84(vertices):
    """Returns (lat, lon)"""
    coords = []
    for v in vertices:
        if isinstance(v, dict):
            lon, lat = unity_to_wgs84(v["x"], v["z"])
            coords.append((lat, lon))
    if not coords:
        return None, None
    return (
        sum(c[0] for c in coords) / len(coords),
        sum(c[1] for c in coords) / len(coords)
    )

with open(OSM_FILE) as f:
    osm_buildings = json.load(f)
with open(LIDAR_FILE) as f:
    lidar_buildings = json.load(f)

lidar_by_id = {lb["id"]: lb for lb in lidar_buildings if "id" in lb}

print(f"OSM: {len(osm_buildings)}, LIDAR: {len(lidar_buildings)}")

# --- GENERATE catastro_edificios.json (simple list format) ---
catastro_list = []
for b in osm_buildings:
    bid = b["id"]
    verts = b.get("vertices", [])
    lb = lidar_by_id.get(bid, {})

    lat, lon = get_centroid_wgs84(verts)

    # Compute approximate area from polygon
    area = 0
    if len(verts) >= 3:
        xs = [v["x"] for v in verts if isinstance(v, dict)]
        zs = [v["z"] for v in verts if isinstance(v, dict)]
        n = len(xs)
        for i in range(n):
            j = (i + 1) % n
            area += xs[i] * zs[j] - xs[j] * zs[i]
        area = abs(area) / 2.0

    entry = {
        "id": str(bid),
        "osm_id": bid,
        "tipo": b.get("type", "yes"),
        "nombre": b.get("name", ""),
        "niveles": b.get("levels"),
        "altura_osm": b.get("height"),
        "altura_lidar": lb.get("lidar_altura"),
        "z_base": lb.get("lidar_z_base"),
        "z_top": lb.get("lidar_z_top"),
        "forma_tejado": lb.get("lidar_forma"),
        "area_m2": round(area, 1),
        "lat": round(lat, 7) if lat else None,
        "lon": round(lon, 7) if lon else None,
        "vertices": verts,
        "fuente": "OSM+LIDAR_PNOA",
        "anno_fuente": 2024,
    }
    catastro_list.append(entry)

with open(OUT_CATASTRO, "w") as f:
    json.dump(catastro_list, f, ensure_ascii=False)
print(f"catastro_edificios.json: {len(catastro_list)} edificios → {OUT_CATASTRO}")

# --- GENERATE catastro_navarra_inspire.geojson (INSPIRE BU format) ---
features = []
for b in osm_buildings:
    bid = b["id"]
    verts = b.get("vertices", [])
    lb = lidar_by_id.get(bid, {})

    geom = vertices_to_polygon_wgs84(verts)
    if geom is None:
        continue

    lat, lon = get_centroid_wgs84(verts)

    # INSPIRE Building Unit properties
    props = {
        "gml_id": f"BU.Building.{bid}",
        "localId": str(bid),
        "namespace": "ES.NAVARRA.OSM",
        "versionId": "2024-01",
        "beginLifespanVersion": "2024-01-01",
        "conditionOfConstruction": "functional",
        "dateOfConstruction": None,
        "dateOfDemolition": None,
        "buildingNature": b.get("type", "building"),
        "numberOfFloorsAboveGround": b.get("levels"),
        "numberOfFloorsBelowGround": 0,
        "heightAboveGround": float(lb.get("lidar_altura") or b.get("height") or 0),
        "name": b.get("name", ""),
        "currentUse": b.get("type", "residential"),
        "osm_id": bid,
        "lidar_z_base": lb.get("lidar_z_base"),
        "lidar_z_top": lb.get("lidar_z_top"),
        "lidar_forma": lb.get("lidar_forma"),
        "geometry_crs": "EPSG:4326",
        "_note": "Generado desde OSM+LIDAR — WFS INSPIRE Navarra no accesible (HTTP 403)"
    }

    feature = {
        "type": "Feature",
        "id": f"BU.Building.{bid}",
        "geometry": geom,
        "properties": props
    }
    features.append(feature)

inspire_fc = {
    "type": "FeatureCollection",
    "name": "BU:Building",
    "crs": {"type": "name", "properties": {"name": "urn:ogc:def:crs:OGC:1.3:CRS84"}},
    "_inspire_endpoint": "https://inspire.navarra.es/services/BU/wfs",
    "_download_status": "FAILED_HTTP_403 — sin acceso a red en entorno de ejecución",
    "_fallback_source": "buildings_osm_rico.json + lidar_buildings.json",
    "_generated": "2026-05-31",
    "features": features
}

with open(OUT_INSPIRE, "w") as f:
    json.dump(inspire_fc, f, ensure_ascii=False)

print(f"catastro_navarra_inspire.geojson: {len(features)} features → {OUT_INSPIRE}")

# Print stats
with_height = sum(1 for f in features if f["properties"].get("heightAboveGround", 0) > 0)
with_lidar = sum(1 for f in features if f["properties"].get("lidar_z_base") is not None)
print(f"  Con altura: {with_height}/{len(features)}")
print(f"  Con datos LIDAR: {with_lidar}/{len(features)}")
