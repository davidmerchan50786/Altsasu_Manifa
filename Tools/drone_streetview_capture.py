#!/usr/bin/env python3
"""
DRON VIRTUAL STREET VIEW — Altsasu Manifa
==========================================
Recorre todas las calles de Alsasua como un dron virtual, descargando fotos
reales de fachadas desde Google Street View o Mapillary.

Genera ~2600 puntos de cámara cada 15m a lo largo de 344 calles (57km).
En cada punto apunta hacia los edificios más cercanos y descarga la imagen.

FUENTES DE IMÁGENES (en orden de prioridad):
  1. Google Street View Static API  — 640×640px, alta calidad, $7/1000 imgs
  2. Mapillary API v4               — resolución variable, GRATIS
  3. Fallback: textura sintética    — generada proceduralmente

SETUP:
  Google: obtener API key en https://console.cloud.google.com
          habilitar "Street View Static API"
  Mapillary: obtener token en https://www.mapillary.com/developer

USO:
  # Google Street View (mejor calidad)
  python Tools/drone_streetview_capture.py --google-key TU_API_KEY

  # Mapillary (gratis)
  python Tools/drone_streetview_capture.py --mapillary-token TU_TOKEN

  # Solo generar waypoints (sin descargar)
  python Tools/drone_streetview_capture.py --plan-only

  # Limitar zona
  python Tools/drone_streetview_capture.py --google-key KEY --zona iglesia

  # Paso de 10m para máxima cobertura
  python Tools/drone_streetview_capture.py --google-key KEY --step 10

SALIDA:
  Assets/AlsasuaData/FacadeTextures/StreetView_Auto/{building_id}_{angle}.jpg
  Assets/AlsasuaData/drone_waypoints.json           — plan de vuelo
  Assets/AlsasuaData/drone_capture_report.json       — resultado
"""

import argparse
import json
import math
import os
import sys
import time
from collections import defaultdict
from pathlib import Path

try:
    import urllib.request
    import urllib.error
    HAS_URLLIB = True
except ImportError:
    HAS_URLLIB = False

# ─── RUTAS ─────────────────────────────────────────────────────────────────────

SCRIPT_DIR  = Path(__file__).resolve().parent
PROJECT_DIR = SCRIPT_DIR.parent

ROADS_JSON      = PROJECT_DIR / "Assets/AlsasuaData/roads_unity.json"
BUILDINGS_JSON  = PROJECT_DIR / "Assets/AlsasuaData/buildings_fusion_final.json"
OUTPUT_DIR      = PROJECT_DIR / "Assets/AlsasuaData/FacadeTextures/StreetView_Auto"
WAYPOINTS_JSON  = PROJECT_DIR / "Assets/AlsasuaData/drone_waypoints.json"
REPORT_JSON     = PROJECT_DIR / "Assets/AlsasuaData/drone_capture_report.json"
AAA_DIR         = PROJECT_DIR / "Assets/AlsasuaData/FacadeTextures/AAA"

# ─── CONVERSIÓN COORDENADAS ────────────────────────────────────────────────────

# Mismos parámetros que generate_buildings_fusion.py
LON_ORIGIN   = -2.192000
LAT_ORIGIN   = 42.819000
DEG_TO_UTM_E = 77450.0
DEG_TO_UTM_N = 111320.0
UNITY_OX     = 1918.0
UNITY_OZ     = 8570.0

def unity_to_lonlat(ux, uz):
    """Convierte coordenadas Unity (del sistema fusion) → lon/lat WGS84."""
    dx = ux - UNITY_OX  # metros relativos al origen
    dz = uz - UNITY_OZ
    lon = LON_ORIGIN + dx / DEG_TO_UTM_E
    lat = LAT_ORIGIN + dz / DEG_TO_UTM_N
    return lon, lat

def road_to_lonlat(rx, rz):
    """Convierte punto de road (OSM relativo) → lon/lat."""
    # Road → unity fusion: +3893, +17620
    ux = rx + 3893.0
    uz = rz + 17620.0
    return unity_to_lonlat(ux, uz)

# ─── CENTROS DE ZONAS EN LON/LAT ──────────────────────────────────────────────

ZONA_CENTERS_LONLAT = {
    "casco_viejo":    (-2.1673, 42.9005),
    "plaza_fueros":   (-2.1660, 42.9010),
    "ferial":         (-2.1635, 42.9025),
    "gaztetxe":       (-2.1695, 42.8990),
    "iglesia":        (-2.1680, 42.8960),
    "ayto":           (-2.1670, 42.8995),
    "plaza_zubeztia": (-2.1710, 42.8985),
    "garcia_jimenez": (-2.1645, 42.8998),
}

# ─── CARGA DE DATOS ───────────────────────────────────────────────────────────

def load_roads():
    with open(ROADS_JSON, encoding="utf-8") as f:
        return json.load(f)

def load_buildings():
    with open(BUILDINGS_JSON, encoding="utf-8") as f:
        raw = json.load(f)
    blist = raw if isinstance(raw, list) else raw.get("buildings", raw.get("features", []))
    buildings = []
    for b in blist:
        p = b.get("properties", b)
        cu = p.get("center_unity", [0, 0])
        if isinstance(cu, str): cu = json.loads(cu)
        buildings.append({
            "id":   str(p.get("id", "")),
            "ux":   float(cu[0]),
            "uz":   float(cu[1]),
            "arch": p.get("archetype", ""),
            "h":    float(p.get("height_m", 8)),
        })
    return buildings

# ─── INTERPOLACIÓN DE RUTA ────────────────────────────────────────────────────

def interpolate_road(road, step):
    pts = road.get("points", [])
    if len(pts) < 2: return []
    result = []
    residuo = 0.0
    for i in range(len(pts) - 1):
        ax, az = pts[i]["x"], pts[i]["z"]
        bx, bz = pts[i+1]["x"], pts[i+1]["z"]
        dx, dz = bx - ax, bz - az
        seg = math.sqrt(dx*dx + dz*dz)
        if seg < 0.1: continue
        nx, nz = dx/seg, dz/seg
        t = residuo
        while t < seg:
            px, pz = ax + nx*t, az + nz*t
            result.append({"rx": px, "rz": pz, "dx": nx, "dz": nz})
            t += step
        residuo = t - seg
    return result

# ─── SPATIAL INDEX ────────────────────────────────────────────────────────────

def build_grid(buildings, cell=50.0):
    grid = defaultdict(list)
    for i, b in enumerate(buildings):
        gx = int(b["ux"] / cell)
        gz = int(b["uz"] / cell)
        grid[(gx, gz)].append(i)
    return grid

def query_grid(grid, buildings, ux, uz, radius, cell=50.0):
    gx0, gx1 = int((ux - radius)/cell), int((ux + radius)/cell)
    gz0, gz1 = int((uz - radius)/cell), int((uz + radius)/cell)
    result = []
    for gx in range(gx0, gx1+1):
        for gz in range(gz0, gz1+1):
            for bi in grid.get((gx, gz), []):
                b = buildings[bi]
                d = math.sqrt((b["ux"]-ux)**2 + (b["uz"]-uz)**2)
                if d <= radius:
                    result.append((d, bi))
    result.sort()
    return result

# ─── GENERADOR DE WAYPOINTS ──────────────────────────────────────────────────

def generate_waypoints(roads, buildings, grid, step=15, max_dist=40, filter_zona=None):
    """
    Genera el plan de vuelo del dron: puntos de cámara con heading hacia edificios.
    """
    waypoints = []
    building_coverage = defaultdict(int)  # cuántas veces se "ve" cada edificio
    seen = set()

    for road in roads:
        road_type = road.get("type", "")
        if road_type == "path": continue  # ignorar senderos de montaña

        cam_points = interpolate_road(road, step)

        for cam in cam_points:
            # Convertir a coordenadas fusion para buscar edificios
            cam_ux = cam["rx"] + 3893.0
            cam_uz = cam["rz"] + 17620.0
            cam_lon, cam_lat = road_to_lonlat(cam["rx"], cam["rz"])

            # Filtro por zona si se especifica
            if filter_zona:
                zlon, zlat = ZONA_CENTERS_LONLAT.get(filter_zona, (0, 0))
                if zlon == 0: continue
                d_zona = math.sqrt((cam_lon - zlon)**2 + (cam_lat - zlat)**2) * 111320
                if d_zona > 200: continue  # solo puntos dentro de 200m de la zona

            nearby = query_grid(grid, buildings, cam_ux, cam_uz, max_dist)

            for dist, bi in nearby[:4]:  # máximo 4 edificios por punto
                b = buildings[bi]
                bid = b["id"]

                # Heading: ángulo desde cámara al edificio (grados, 0=norte, clockwise)
                dx = b["ux"] - cam_ux
                dz = b["uz"] - cam_uz
                heading = (math.degrees(math.atan2(dx, dz))) % 360

                # Pitch: apuntar ligeramente hacia arriba según altura del edificio
                elev_angle = math.degrees(math.atan2(b["h"] * 0.6, max(dist, 1)))
                pitch = max(-10, min(30, elev_angle))

                # FOV: más ancho para edificios grandes cercanos
                fov = 90 if dist < 15 else 75

                wp = {
                    "lat": round(cam_lat, 7),
                    "lon": round(cam_lon, 7),
                    "heading": round(heading, 1),
                    "pitch": round(pitch, 1),
                    "fov": fov,
                    "building_id": bid,
                    "building_arch": b["arch"],
                    "distance_m": round(dist, 1),
                    "road_name": road.get("name", ""),
                    "road_type": road_type,
                }
                waypoints.append(wp)
                building_coverage[bid] += 1
                seen.add(bid)

    return waypoints, building_coverage, seen

# ─── DESCARGA GOOGLE STREET VIEW ─────────────────────────────────────────────

def download_google_sv(lat, lon, heading, pitch, fov, api_key, output_path, size="640x640"):
    """Descarga una imagen de Google Street View Static API."""
    url = (
        f"https://maps.googleapis.com/maps/api/streetview"
        f"?size={size}"
        f"&location={lat},{lon}"
        f"&heading={heading}"
        f"&pitch={pitch}"
        f"&fov={fov}"
        f"&source=outdoor"
        f"&key={api_key}"
    )
    try:
        req = urllib.request.Request(url, headers={"User-Agent": "AltsasuManifa/1.0"})
        with urllib.request.urlopen(req, timeout=15) as resp:
            data = resp.read()
            if len(data) < 5000:  # imagen negra/vacía = sin cobertura
                return False
            with open(output_path, "wb") as f:
                f.write(data)
            return True
    except Exception as e:
        return False

# ─── DESCARGA MAPILLARY ──────────────────────────────────────────────────────

def download_mapillary(lat, lon, heading, token, output_path, radius=30):
    """
    Busca la imagen Mapillary más cercana al punto y heading, y la descarga.
    """
    # Buscar imagen cercana
    bbox = f"{lon-0.0003},{lat-0.0003},{lon+0.0003},{lat+0.0003}"
    search_url = (
        f"https://graph.mapillary.com/images"
        f"?access_token={token}"
        f"&fields=id,thumb_1024_url,computed_compass_angle,geometry"
        f"&bbox={bbox}"
        f"&limit=5"
    )
    try:
        req = urllib.request.Request(search_url)
        with urllib.request.urlopen(req, timeout=10) as resp:
            data = json.loads(resp.read())

        if not data.get("data"):
            return False

        # Elegir la imagen con heading más parecido
        best = None
        best_diff = 360
        for img in data["data"]:
            img_heading = img.get("computed_compass_angle", 0)
            diff = abs(((heading - img_heading + 180) % 360) - 180)
            if diff < best_diff:
                best_diff = diff
                best = img

        if not best or "thumb_1024_url" not in best:
            return False

        # Descargar thumbnail 1024px
        img_url = best["thumb_1024_url"]
        req = urllib.request.Request(img_url)
        with urllib.request.urlopen(req, timeout=15) as resp:
            with open(output_path, "wb") as f:
                f.write(resp.read())
        return True
    except Exception:
        return False

# ─── PIPELINE PRINCIPAL ───────────────────────────────────────────────────────

def main():
    p = argparse.ArgumentParser(description="Dron Virtual Street View — Altsasu Manifa")
    p.add_argument("--google-key",      type=str, help="API key de Google Street View")
    p.add_argument("--mapillary-token", type=str, help="Access token de Mapillary")
    p.add_argument("--step",            type=float, default=15, help="Metros entre puntos (default: 15)")
    p.add_argument("--max-dist",        type=float, default=40, help="Radio de búsqueda edificios (default: 40m)")
    p.add_argument("--zona",            type=str, help="Solo cubrir esta zona")
    p.add_argument("--plan-only",       action="store_true", help="Solo generar waypoints, no descargar")
    p.add_argument("--limit",           type=int, default=0, help="Máximo de imágenes a descargar (0=todas)")
    p.add_argument("--skip-existing",   action="store_true", default=True, help="No reDescargar existentes")
    args = p.parse_args()

    print("=" * 60)
    print("  DRON VIRTUAL STREET VIEW — Altsasu Manifa")
    print("=" * 60)

    roads = load_roads()
    buildings = load_buildings()
    grid = build_grid(buildings)
    print(f"Calles: {len(roads)} | Edificios: {len(buildings)}")

    # Edificios que ya tienen textura
    existing_tex = set()
    if AAA_DIR.exists():
        for f in AAA_DIR.iterdir():
            if f.name.endswith("_albedo.png"):
                existing_tex.add(f.stem.replace("_albedo", ""))

    # Generar plan de vuelo
    print(f"\nGenerando plan de vuelo (paso={args.step}m, radio={args.max_dist}m)...")
    waypoints, coverage, seen = generate_waypoints(
        roads, buildings, grid,
        step=args.step, max_dist=args.max_dist, filter_zona=args.zona
    )

    # Deduplicar: 1 mejor foto por edificio (el más cercano)
    best_per_building = {}
    for wp in waypoints:
        bid = wp["building_id"]
        if bid not in best_per_building or wp["distance_m"] < best_per_building[bid]["distance_m"]:
            best_per_building[bid] = wp

    # Filtrar edificios que ya tienen textura
    to_capture = {bid: wp for bid, wp in best_per_building.items() if bid not in existing_tex}

    # Añadir ángulos extra para edificios grandes (2 fotos perpendiculares)
    extra_waypoints = []
    for bid, wp in list(to_capture.items()):
        b = next((b for b in buildings if b["id"] == bid), None)
        if b and b["h"] > 10:  # edificios de 3+ plantas: foto extra a 90°
            wp2 = dict(wp)
            wp2["heading"] = (wp["heading"] + 90) % 360
            wp2["_extra"] = True
            extra_waypoints.append((bid, wp2))

    print(f"\nPlan de vuelo:")
    print(f"  Puntos de cámara totales:     {len(waypoints)}")
    print(f"  Edificios visibles únicos:    {len(seen)}/{len(buildings)} ({len(seen)/len(buildings)*100:.0f}%)")
    print(f"  Ya con textura:               {len(existing_tex & seen)}")
    print(f"  Edificios a capturar:         {len(to_capture)}")
    print(f"  Fotos extra (90°):            {len(extra_waypoints)}")
    print(f"  Total descargas estimadas:    {len(to_capture) + len(extra_waypoints)}")

    # Guardar waypoints
    wp_export = sorted(best_per_building.values(), key=lambda w: (w["road_name"], w["heading"]))
    with open(WAYPOINTS_JSON, "w", encoding="utf-8") as f:
        json.dump({
            "total_waypoints": len(wp_export),
            "buildings_covered": len(best_per_building),
            "buildings_total": len(buildings),
            "coverage_pct": round(len(best_per_building) / len(buildings) * 100, 1),
            "step_m": args.step,
            "max_dist_m": args.max_dist,
            "zona_filter": args.zona,
            "waypoints": wp_export,
        }, f, indent=2, ensure_ascii=False)
    print(f"\nWaypoints guardados: {WAYPOINTS_JSON}")

    if args.plan_only:
        print("\n[PLAN ONLY] No se descargan imágenes.")
        _write_report(len(seen), len(existing_tex & seen), 0, len(to_capture))
        return

    if not args.google_key and not args.mapillary_token:
        print("\n⚠  Sin API key. Para descargar fotos reales necesitas:")
        print("   --google-key TU_KEY   (Google Street View, ~$7/1000 imgs)")
        print("   --mapillary-token TOK (Mapillary, gratis)")
        print("\n   El plan de vuelo está guardado. Puedes ejecutar después con la key.")
        _write_report(len(seen), len(existing_tex & seen), 0, len(to_capture))
        return

    # Descargar
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    source = "google" if args.google_key else "mapillary"
    print(f"\nDescargando desde {source.upper()}...")

    downloaded = 0
    failed = 0
    all_captures = list(to_capture.items()) + [(bid, wp) for bid, wp in extra_waypoints]

    if args.limit > 0:
        all_captures = all_captures[:args.limit]

    for i, (bid, wp) in enumerate(all_captures):
        suffix = "_90" if wp.get("_extra") else ""
        out_path = OUTPUT_DIR / f"{bid}{suffix}.jpg"

        if args.skip_existing and out_path.exists():
            downloaded += 1
            continue

        ok = False
        if args.google_key:
            ok = download_google_sv(
                wp["lat"], wp["lon"], wp["heading"], wp["pitch"], wp["fov"],
                args.google_key, str(out_path)
            )
        elif args.mapillary_token:
            ok = download_mapillary(
                wp["lat"], wp["lon"], wp["heading"],
                args.mapillary_token, str(out_path)
            )

        if ok:
            downloaded += 1
        else:
            failed += 1

        # Progreso
        if (i + 1) % 25 == 0 or i == 0:
            pct = (i + 1) / len(all_captures) * 100
            print(f"  [{pct:5.1f}%] {i+1}/{len(all_captures)} — OK:{downloaded} FAIL:{failed}")

        # Rate limiting (Google: 100/s free, Mapillary: 2000/min)
        if (i + 1) % 50 == 0:
            time.sleep(1)

    print(f"\n{'='*60}")
    total_with_tex = len(existing_tex) + downloaded
    print(f"Descargadas:  {downloaded}")
    print(f"Fallidas:     {failed} (sin cobertura Street View en ese punto)")
    print(f"Cobertura:    {total_with_tex}/{len(buildings)} ({total_with_tex/len(buildings)*100:.0f}%)")
    print(f"Imágenes en:  {OUTPUT_DIR}")

    _write_report(len(seen), len(existing_tex), downloaded, failed)

def _write_report(visible, existing, downloaded, failed):
    report = {
        "edificios_visibles_desde_calles": visible,
        "con_textura_previa": existing,
        "descargadas_nuevas": downloaded,
        "fallidas": failed,
    }
    with open(REPORT_JSON, "w", encoding="utf-8") as f:
        json.dump(report, f, indent=2, ensure_ascii=False)

if __name__ == "__main__":
    main()
