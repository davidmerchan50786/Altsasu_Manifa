#!/usr/bin/env python3
"""
map_photos_to_buildings.py — Altsasu Manifa B3 v2
Mapea cada foto procesada al edificio más cercano en su zona.

Mejora v2: distribuye fotos a múltiples edificios por zona (no uno solo)
y usa las coordenadas Unity reales de buildings_fusion_final.json.

Lee:
  Assets/AlsasuaData/FacadeTextures/processing_report.json
  Assets/AlsasuaData/buildings_fusion_final.json

Genera:
  Assets/AlsasuaData/photo_building_mapping.json

Uso: python3 Tools/map_photos_to_buildings.py
"""

import json
import math
import os
from collections import defaultdict

SCRIPT_DIR   = os.path.dirname(os.path.abspath(__file__))
PROJECT_ROOT = os.path.dirname(SCRIPT_DIR)

REPORT_PATH    = os.path.join(PROJECT_ROOT, "Assets", "AlsasuaData", "FacadeTextures", "processing_report.json")
BUILDINGS_PATH = os.path.join(PROJECT_ROOT, "Assets", "AlsasuaData", "buildings_fusion_final.json")
OUTPUT_PATH    = os.path.join(PROJECT_ROOT, "Assets", "AlsasuaData", "photo_building_mapping.json")

# ── Conversión lon/lat → Unity (misma fórmula que generate_buildings_fusion.py) ──

LON_ORIGIN   = -2.192000
LAT_ORIGIN   = 42.819000
DEG_TO_UTM_E = 77450.0
DEG_TO_UTM_N = 111320.0
UTM_E_ORIGIN = 567951.0
UTM_N_ORIGIN = 4749902.0
UNITY_OX     = 1918.0
UNITY_OZ     = 8570.0

def lonlat_to_unity(lon, lat):
    E = UTM_E_ORIGIN + (lon - LON_ORIGIN) * DEG_TO_UTM_E
    N = UTM_N_ORIGIN + (lat - LAT_ORIGIN) * DEG_TO_UTM_N
    return (E - UTM_E_ORIGIN) + UNITY_OX, (N - UTM_N_ORIGIN) + UNITY_OZ

# Centros de zona en coordenadas reales (lon, lat) de Alsasua
ZONA_LONLAT = {
    "casco_viejo":    (-2.1673, 42.9005),
    "plaza_fueros":   (-2.1660, 42.9010),
    "ferial":         (-2.1635, 42.9025),
    "gaztetxe":       (-2.1695, 42.8990),
    "iglesia":        (-2.1680, 42.8960),
    "ayto":           (-2.1670, 42.8995),
    "plaza_zubeztia": (-2.1710, 42.8985),
    "garcia_jimenez": (-2.1645, 42.8998),
}

# Convertir a Unity
ZONA_CENTERS_UNITY = {z: lonlat_to_unity(lon, lat) for z, (lon, lat) in ZONA_LONLAT.items()}

ZONA_KEYWORDS = {
    "iglesia":  ["iglesia", "eliza", "church", "ermita", "capilla"],
    "ayto":     ["ayuntamiento", "udaletxea", "town_hall", "municipal"],
    "ferial":   ["ferial", "pabellón", "frontoi", "polideportivo"],
    "gaztetxe": ["gaztetxe", "cultural", "juventud"],
}

ZONA_RADIUS = 150.0  # metros: radio de búsqueda por zona

def dist2d(ax, az, bx, bz):
    return math.sqrt((ax - bx) ** 2 + (az - bz) ** 2)

def load_buildings():
    if not os.path.exists(BUILDINGS_PATH):
        print(f"[ERR] No se encontró {BUILDINGS_PATH}")
        return []
    with open(BUILDINGS_PATH, "r", encoding="utf-8") as f:
        raw = json.load(f)
    blist = raw if isinstance(raw, list) else raw.get("buildings", raw.get("features", []))
    buildings = []
    for b in blist:
        p = b.get("properties", b)
        cu = p.get("center_unity", [0, 0])
        if isinstance(cu, str):
            cu = json.loads(cu)
        buildings.append({
            "id": str(p.get("id", p.get("osm_id", "?"))),
            "x": float(cu[0]),
            "z": float(cu[1]),
            "archetype": p.get("archetype", ""),
            "height_m": float(p.get("height_m", 0)),
            "surface_m2": float(p.get("surface_m2", 0)),
        })
    return buildings

def find_zone_buildings(zona, buildings):
    """Encuentra todos los edificios dentro del radio de la zona, ordenados por cercanía."""
    cx, cz = ZONA_CENTERS_UNITY.get(zona, (0, 0))
    if cx == 0 and cz == 0:
        return []

    with_dist = []
    for b in buildings:
        d = dist2d(cx, cz, b["x"], b["z"])
        if d <= ZONA_RADIUS:
            with_dist.append((d, b))
    with_dist.sort(key=lambda t: t[0])

    # Priorizar edificios con keyword match
    keywords = ZONA_KEYWORDS.get(zona, [])
    if keywords:
        kw_match = [(d, b) for d, b in with_dist
                    if any(kw in b.get("archetype", "").lower() for kw in keywords)]
        if kw_match:
            rest = [(d, b) for d, b in with_dist if (d, b) not in kw_match]
            with_dist = kw_match + rest

    return with_dist

def main():
    print("=" * 60)
    print("map_photos_to_buildings.py v2 — distribuir fotos por edificio")
    print("=" * 60)

    if not os.path.exists(REPORT_PATH):
        print(f"[WARN] No se encontró {REPORT_PATH}")
        _write_empty(); return

    with open(REPORT_PATH, "r", encoding="utf-8") as f:
        report = json.load(f)
    lista = report.get("lista_procesadas", [])
    if not lista:
        print("[INFO] Sin fotos procesadas."); _write_empty(); return

    buildings = load_buildings()
    print(f"[INFO] {len(buildings)} edificios cargados.")
    print(f"[INFO] {len(lista)} fotos a mapear.")
    print()

    # Agrupar fotos por zona
    fotos_por_zona = defaultdict(list)
    for entry in lista:
        fotos_por_zona[entry.get("zona", "sin_zona")].append(entry)

    mappings = []
    stats = {"high": 0, "medium": 0, "low": 0}
    edificios_asignados = set()

    for zona, fotos in fotos_por_zona.items():
        zone_buildings = find_zone_buildings(zona, buildings)
        n_buildings = len(zone_buildings)

        cx, cz = ZONA_CENTERS_UNITY.get(zona, (0, 0))
        print(f"Zona '{zona}': {len(fotos)} fotos, centro=({cx:.0f},{cz:.0f}), {n_buildings} edificios <{ZONA_RADIUS}m")

        if n_buildings == 0:
            # Sin edificios cerca: asignar a zona genérica con low confidence
            for entry in fotos:
                mappings.append({
                    "foto_procesada": entry["procesada"],
                    "edificio_id": f"zona_{zona}",
                    "zona": zona,
                    "confidence": "low",
                })
                stats["low"] += 1
            continue

        # Distribuir fotos round-robin entre los edificios de la zona
        # Más fotos al edificio más cercano (ratio decreciente)
        for i, entry in enumerate(fotos):
            # Índice del edificio: las primeras fotos van al más cercano,
            # luego se reparten cíclicamente
            b_idx = i % n_buildings
            d, b = zone_buildings[b_idx]

            # Confianza según distancia
            if d < 30:
                conf = "high"
            elif d < 80:
                conf = "medium"
            else:
                conf = "low"

            mappings.append({
                "foto_procesada": entry["procesada"],
                "edificio_id": b["id"],
                "zona": zona,
                "confidence": conf,
                "distance_m": round(d, 1),
            })
            stats[conf] += 1
            edificios_asignados.add(b["id"])

        # Log primeros edificios
        for d, b in zone_buildings[:3]:
            print(f"    {d:6.1f}m → {b['id']} ({b['archetype']}, {b['height_m']:.1f}m, {b['surface_m2']:.0f}m²)")

    result = {"mappings": mappings}
    with open(OUTPUT_PATH, "w", encoding="utf-8") as f:
        json.dump(result, f, ensure_ascii=False, indent=2)

    print()
    print(f"[DONE] {len(mappings)} mapeos → {OUTPUT_PATH}")
    print(f"       Edificios únicos con foto: {len(edificios_asignados)}")
    print(f"       high={stats['high']}  medium={stats['medium']}  low={stats['low']}")

def _write_empty():
    with open(OUTPUT_PATH, "w", encoding="utf-8") as f:
        json.dump({"mappings": []}, f, ensure_ascii=False, indent=2)

if __name__ == "__main__":
    main()
