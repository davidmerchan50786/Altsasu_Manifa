#!/usr/bin/env python3
"""
map_photos_to_buildings.py — Altsasu Manifa B3
Mapea cada foto procesada al edificio catastral más probable.

Lee:
  Assets/AlsasuaData/FacadeTextures/processing_report.json
  Assets/AlsasuaData/catastro_edificios.json

Genera:
  Assets/AlsasuaData/photo_building_mapping.json

Uso: python3 Tools/map_photos_to_buildings.py
"""

import os
import sys
import json
import math

# ---------------------------------------------------------------------------
# Rutas
# ---------------------------------------------------------------------------
SCRIPT_DIR   = os.path.dirname(os.path.abspath(__file__))
PROJECT_ROOT = os.path.dirname(SCRIPT_DIR)

REPORT_PATH   = os.path.join(PROJECT_ROOT, "Assets", "AlsasuaData", "FacadeTextures", "processing_report.json")
CATASTRO_PATH = os.path.join(PROJECT_ROOT, "Assets", "AlsasuaData", "catastro_edificios.json")
OUTPUT_PATH   = os.path.join(PROJECT_ROOT, "Assets", "AlsasuaData", "photo_building_mapping.json")

# ---------------------------------------------------------------------------
# Centros geográficos de cada zona (coordenadas Unity: x, z)
# Derivadas de la geografía real de Alsasua/Altsasu
# Origen: Herriko Plaza  UTM E=567951, N=4749902  →  UnityX=1918, UnityZ=8570
# ---------------------------------------------------------------------------
ZONA_CENTERS = {
    # (unity_x, unity_z)
    "casco_viejo":          (1918.0,   8570.0),   # Herriko Plaza / casco antiguo
    "plaza_fueros":         (1950.0,   8610.0),   # Plaza de los Fueros ~50m E
    "ferial":               (2100.0,   8700.0),   # Zona ferial, NE del centro
    "gaztetxe":             (1860.0,   8520.0),   # Gaztetxe, SO del centro
    "iglesia":              (1865.0,   8238.0),   # Jasokundeko Andre Mariaren eliza  lat~42.8957
    "ayto":                 (1920.0,   8500.0),   # Ayuntamiento, cerca de la plaza
    "plaza_zubeztia":       (1780.0,   8450.0),   # Plaza Zubeztia, O del centro
    "calle_garcia_jimenez": (2050.0,   8550.0),   # Calle García Jiménez, E centro
    "sin_zona":             (1918.0,   8570.0),   # Fallback: centro de Alsasua
}

# Palabras clave en nombre/tipo de edificio que elevan la confianza por zona
ZONA_KEYWORDS = {
    "iglesia":  ["iglesia", "eliza", "church", "ermita", "capilla", "basílica", "religiös"],
    "ayto":     ["ayuntamiento", "udaletxea", "town hall", "municipal", "udal"],
    "ferial":   ["ferial", "pabellón", "frontoi", "pelota", "polideportivo", "frontón"],
    "gaztetxe": ["gaztetxe", "cultural", "juventud", "gaztelu"],
    "plaza_fueros":         ["plaza", "fueros"],
    "plaza_zubeztia":       ["zubeztia", "plaza"],
    "calle_garcia_jimenez": ["garcia jimenez", "garcía jiménez"],
}

# ---------------------------------------------------------------------------
# Funciones auxiliares
# ---------------------------------------------------------------------------

def dist2d(ax, az, bx, bz):
    return math.sqrt((ax - bx) ** 2 + (az - bz) ** 2)


def building_center(edificio: dict):
    """Devuelve (unity_x, unity_z) del centroide del edificio."""
    vertices = edificio.get("vertices", [])
    if vertices:
        cx = sum(v["x"] for v in vertices) / len(vertices)
        cz = sum(v["z"] for v in vertices) / len(vertices)
        return cx, cz
    # Fallback: convertir lat/lon a Unity
    lat = edificio.get("lat", 42.8957)
    lon = edificio.get("lon", -2.1680)
    # UTM 30N ETRS89 aproximado para la zona de Alsasua
    # E = (lon - lon_origin) * 111320 * cos(lat) + 567951
    # N = (lat - lat_origin) * 111320 + 4749902
    # UnityX = E - 567951 + 1918; UnityZ = N - 4749902 + 8570
    lat0 = 42.8957
    lon0 = -2.1680
    ux = (lon - lon0) * 111320.0 * math.cos(math.radians(lat0)) + 1918.0
    uz = (lat - lat0) * 111320.0 + 8570.0
    return ux, uz


def keyword_match(edificio: dict, zona: str) -> bool:
    """Devuelve True si el nombre o tipo del edificio coincide con keywords de la zona."""
    keywords = ZONA_KEYWORDS.get(zona, [])
    if not keywords:
        return False
    nombre = (edificio.get("nombre", "") or "").lower()
    tipo   = (edificio.get("tipo", "")   or "").lower()
    return any(kw in nombre or kw in tipo for kw in keywords)


def find_best_building(zona: str, edificios: list) -> tuple:
    """
    Devuelve (edificio_dict, confidence_str).
    Estrategia:
      1. Si hay keywords que coincidan, devolver el más cercano al centro de zona con confidence=high
      2. Si no, devolver el más cercano al centro de zona con confidence=medium
      3. Si no hay edificios, devolver None, "low"
    """
    if not edificios:
        return None, "low"

    zona_cx, zona_cz = ZONA_CENTERS.get(zona, ZONA_CENTERS["sin_zona"])

    # Calcular distancias
    with_dist = []
    for ed in edificios:
        bx, bz = building_center(ed)
        d = dist2d(zona_cx, zona_cz, bx, bz)
        with_dist.append((d, ed))
    with_dist.sort(key=lambda t: t[0])

    # Buscar primero coincidencia por keywords
    for d, ed in with_dist:
        if keyword_match(ed, zona):
            return ed, "high"

    # Sin keywords: el más cercano
    best_dist, best_ed = with_dist[0]
    confidence = "medium" if best_dist < 100.0 else "low"
    return best_ed, confidence


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

def main():
    print("=" * 60)
    print("map_photos_to_buildings.py — Altsasu Manifa")
    print("=" * 60)

    # --- Cargar processing report ---
    if not os.path.exists(REPORT_PATH):
        print(f"[WARN] No se encontró {REPORT_PATH}")
        print("       Ejecuta primero: python3 Tools/process_streetview_photos.py")
        _write_empty_mapping()
        return

    with open(REPORT_PATH, "r", encoding="utf-8") as f:
        report = json.load(f)

    lista_procesadas = report.get("lista_procesadas", [])
    if not lista_procesadas:
        print("[INFO] No hay fotos procesadas en el report — generando mapping vacío.")
        _write_empty_mapping()
        return

    # --- Cargar catastro edificios ---
    if not os.path.exists(CATASTRO_PATH):
        print(f"[WARN] No se encontró {CATASTRO_PATH}")
        _write_empty_mapping()
        return

    with open(CATASTRO_PATH, "r", encoding="utf-8") as f:
        catastro_raw = json.load(f)

    # Normalizar: puede ser lista o dict con clave "edificios"/"features"
    if isinstance(catastro_raw, list):
        edificios = catastro_raw
    elif isinstance(catastro_raw, dict):
        for key in ("edificios", "features", "buildings"):
            if key in catastro_raw:
                edificios = catastro_raw[key]
                break
        else:
            edificios = list(catastro_raw.values()) if catastro_raw else []
    else:
        edificios = []

    print(f"[INFO] {len(edificios)} edificios cargados del catastro.")
    print(f"[INFO] {len(lista_procesadas)} fotos a mapear.")

    mappings = []
    for entry in lista_procesadas:
        foto_path = entry.get("procesada", "")
        zona = entry.get("zona", "sin_zona")

        best_ed, confidence = find_best_building(zona, edificios)

        if best_ed is None:
            edificio_id = "UNKNOWN"
            confidence = "low"
        else:
            edificio_id = str(best_ed.get("id", best_ed.get("osm_id", "UNKNOWN")))

        mapping = {
            "foto_procesada": foto_path,
            "edificio_id":    edificio_id,
            "zona":           zona,
            "confidence":     confidence,
        }
        mappings.append(mapping)
        print(f"  {os.path.basename(foto_path)}  →  {edificio_id}  [{confidence}]")

    result = {"mappings": mappings}

    with open(OUTPUT_PATH, "w", encoding="utf-8") as f:
        json.dump(result, f, ensure_ascii=False, indent=2)

    print()
    print(f"[DONE] Mapping guardado en: {OUTPUT_PATH}")
    print(f"[DONE] {len(mappings)} mapeos generados.")
    high   = sum(1 for m in mappings if m["confidence"] == "high")
    medium = sum(1 for m in mappings if m["confidence"] == "medium")
    low    = sum(1 for m in mappings if m["confidence"] == "low")
    print(f"       high={high}  medium={medium}  low={low}")


def _write_empty_mapping():
    result = {"mappings": []}
    with open(OUTPUT_PATH, "w", encoding="utf-8") as f:
        json.dump(result, f, ensure_ascii=False, indent=2)
    print(f"[INFO] Mapping vacío escrito en: {OUTPUT_PATH}")


if __name__ == "__main__":
    main()
