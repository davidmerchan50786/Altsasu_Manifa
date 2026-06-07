#!/usr/bin/env python3
"""
Tools/generate_buildings_fusion.py
═══════════════════════════════════════════════════════════════════════════
Genera Assets/AlsasuaData/buildings_fusion_final.json fusionando:
  1. buildings_final.json / buildings_unity.json  — base
  2. lidar_buildings.json                          — alturas LIDAR (prioridad 1)
  3. catastro_navarra_inspire.geojson              — INSPIRE Navarra (prioridad 2)
  4. dsm_alsasua_5m.asc                            — DSM grid (prioridad 3)
  5. overture_buildings_2024.geojson               — Overture 2024 (prioridad 4)
  6. buildings_osm_rico.json                       — tags OSM (amenity, color…)
  7. mapillary_imagenes.json                       — fotos reales

Uso:
    cd /home/user/Altsasu_Manifa
    python3 Tools/generate_buildings_fusion.py

Salida:
    Assets/AlsasuaData/buildings_fusion_final.json
═══════════════════════════════════════════════════════════════════════════
"""

import json
import math
import os
import sys
from pathlib import Path
from typing import Optional

# ── Rutas ─────────────────────────────────────────────────────────────────
ROOT = Path(__file__).resolve().parent.parent
DATA = ROOT / "Assets" / "AlsasuaData"
OUT  = DATA / "buildings_fusion_final.json"

# ── Constantes de coordenadas (GeoDataAlsasua) ────────────────────────────
UTM_E_ORIGIN   = 567951.0
UTM_N_ORIGIN   = 4749902.0
UNITY_OX       = 1918.0
UNITY_OZ       = 8570.0
Z_MIN          = 511.33
ALT_PLANTA     = 3.2    # metros por planta OSM/Overture

# Aproximación lineal WGS84→UTM para el área de Alsasua (válida ~5km²)
LON_ORIGIN     = -2.192000
LAT_ORIGIN     = 42.819000
DEG_TO_UTM_E   = 77450.0   # m/grado longitud a esta latitud
DEG_TO_UTM_N   = 111320.0  # m/grado latitud


def lonlat_to_unity(lon: float, lat: float) -> tuple[float, float]:
    """Convierte lon/lat WGS84 → coordenadas Unity XZ."""
    dlon = lon - LON_ORIGIN
    dlat = lat - LAT_ORIGIN
    E = UTM_E_ORIGIN + dlon * DEG_TO_UTM_E
    N = UTM_N_ORIGIN + dlat * DEG_TO_UTM_N
    ux = (E - UTM_E_ORIGIN) + UNITY_OX
    uz = (N - UTM_N_ORIGIN) + UNITY_OZ
    return ux, uz


def polygon_area_m2(pts_unity: list[list[float]]) -> float:
    """Área de polígono en Unity (Shoelace / Gauss). 1 unidad = 1 metro."""
    if not pts_unity or len(pts_unity) < 3:
        return 0.0
    area = 0.0
    n = len(pts_unity)
    for i in range(n):
        j = (i + 1) % n
        area += pts_unity[i][0] * pts_unity[j][1]
        area -= pts_unity[j][0] * pts_unity[i][1]
    return abs(area) * 0.5


def geojson_first_ring(geometry: dict) -> Optional[list]:
    """Extrae el primer anillo exterior de una geometría Polygon o MultiPolygon."""
    if not geometry:
        return None
    gtype = geometry.get("type", "")
    coords = geometry.get("coordinates")
    if not coords:
        return None
    if gtype == "Polygon":
        return coords[0] if coords else None
    elif gtype == "MultiPolygon":
        return coords[0][0] if coords and coords[0] else None
    return None


def load_json(path: Path) -> Optional[dict | list]:
    if not path.exists():
        print(f"  [SKIP] No encontrado: {path.name}", file=sys.stderr)
        return None
    try:
        with open(path, encoding="utf-8") as f:
            return json.load(f)
    except Exception as e:
        print(f"  [ERR] {path.name}: {e}", file=sys.stderr)
        return None


# ── Jerarquía de arquetipo → string ───────────────────────────────────────
def arquetipo_string(tipo: str, amenity: str, shop: str, sport: str,
                     anio: int, niveles: int, uso: str) -> str:
    tipo  = (tipo or "").lower()
    uso   = (uso  or "").lower()

    if sport == "pelota" or tipo == "sports_hall":       return "fronton"
    if amenity == "place_of_worship" or tipo in ("chapel","church"): return "iglesia_piedra"
    if amenity in ("bar","pub","cafe","restaurant"):      return "bar_taberna"
    if shop or tipo in ("commercial","retail"):          return "comercio"
    if amenity in ("school","hospital","public_building","community_centre") \
       or tipo in ("school","public","train_station"):   return "equipamiento_publico"
    if tipo in ("industrial","warehouse","farm_auxiliary") \
       or "industrial" in uso:                           return "nave_industrial"
    if tipo in ("farm","barn","farmhouse"):              return "casero_baserri"
    if tipo in ("garage","garages","parking","roof"):    return "aparcamiento"
    if tipo in ("ruins","collapsed"):                    return "solar_ruina"

    # Residencial por época
    if anio and anio > 1975:  return "moderno_post1975"
    if anio and anio > 1940:  return "bloque_ladrillo"
    if anio and anio > 1800:  return "pre1940_piedra"
    if niveles and niveles > 5: return "moderno_post1975"
    if niveles and niveles > 3: return "bloque_ladrillo"
    return "pre1940_piedra"


def color_hex_por_arquetipo(arq: str) -> str:
    COLORES = {
        "iglesia_piedra":       "#5A5A5A",
        "nave_industrial":      "#8A8A8A",
        "casero_baserri":       "#E8D5B0",
        "bar_taberna":          "#C8956C",
        "comercio":             "#D4B896",
        "equipamiento_publico": "#B5A898",
        "fronton":              "#F0EDE8",
        "pre1940_piedra":       "#8B5E3C",
        "bloque_ladrillo":      "#C4A882",
        "moderno_post1975":     "#E8E0D8",
    }
    return COLORES.get(arq, "#C0B090")


# ─── MEJORA 3: Altura inteligente ─────────────────────────────────────────
def altura_inteligente(plantas: int, tipo: str, amenity: str, shop: str,
                       uso: str, footprint_unity: list) -> float:
    plantas = max(1, plantas or 2)
    tipo = (tipo or "").lower()
    uso  = (uso  or "").lower()

    # Industrial
    if tipo in ("industrial","warehouse") or "industrial" in uso:
        return max(6.0, plantas * 5.0)

    # Caserío
    if tipo in ("farm","barn","farmhouse"):
        sup = polygon_area_m2(footprint_unity) if footprint_unity else 0
        return max(7.5, plantas * 3.2) if sup > 300 else max(6.0, plantas * 3.2)

    # Comercial
    if tipo in ("commercial","retail","shop") or "comercial" in uso or shop:
        return 4.5 + max(0, plantas - 1) * 3.0

    # Residencial
    return plantas * 3.0 + 1.5


# ─── MEJORA 4: roof_type desde LIDAR ──────────────────────────────────────
def roof_type_from_lidar(lidar_forma: str, lidar_z_min: float, lidar_z_max: float,
                         puntos_tejado: list, anio: int) -> str:
    FORMA_MAP = {
        "flat":         "flat",
        "gabled":       "gabled",
        "hipped":       "hipped",
        "steep_gabled": "gabled",
        "shed":         "shed",
        "pyramidal":    "hipped",
    }
    if lidar_forma and lidar_forma != "unknown":
        return FORMA_MAP.get(lidar_forma.lower(), "gabled")

    # Análisis de nube de puntos
    if puntos_tejado and len(puntos_tejado) >= 4:
        ys = [p.get("y", p.get("z", 0)) for p in puntos_tejado if p]
        if ys:
            variacion = max(ys) - min(ys)
            if variacion < 0.8:
                return "flat"
            cx = sum(p.get("x", 0) for p in puntos_tejado) / len(puntos_tejado)
            cz = sum(p.get("z", 0) for p in puntos_tejado) / len(puntos_tejado)
            y_umbral = min(ys) + variacion * 0.7
            pts_centro = sum(
                1 for p in puntos_tejado
                if p and p.get("y", 0) >= y_umbral
                and math.sqrt((p.get("x",0)-cx)**2 + (p.get("z",0)-cz)**2) < 10*0.35
            )
            pts_periferia = sum(
                1 for p in puntos_tejado
                if p and p.get("y", 0) >= y_umbral
                and math.sqrt((p.get("x",0)-cx)**2 + (p.get("z",0)-cz)**2) >= 10*0.35
            )
            if pts_centro > pts_periferia and pts_centro > 2:
                return "hipped"
            if variacion > 2.5:
                return "gabled"
            return "shed"

    # Variación z_min / z_max
    if lidar_z_min and lidar_z_max and lidar_z_min > 0:
        variacion = lidar_z_max - lidar_z_min
        if variacion < 0.8:   return "flat"
        if variacion > 2.5:   return "gabled"
        return "shed"

    # Fallback por época
    if anio and anio > 1975:  return "flat"
    if anio and anio < 1940:  return "gabled"
    return "gabled"


# ══════════════════════════════════════════════════════════════════════════
#  CARGA DE FUENTES
# ══════════════════════════════════════════════════════════════════════════

def cargar_base() -> dict:
    """Carga buildings_final.json o buildings_unity.json. Devuelve dict id→ed."""
    eds = {}
    for fname in ("buildings_final.json", "buildings_unity.json"):
        data = load_json(DATA / fname)
        if data:
            for ed in (data if isinstance(data, list) else data.get("features", [])):
                bid = ed.get("id") or ed.get("properties", {}).get("id")
                if bid:
                    eds[str(bid)] = dict(ed)
            print(f"  Base: {len(eds)} edificios desde {fname}")
            break
    return eds


def enriquecer_lidar(eds: dict):
    data = load_json(DATA / "lidar_buildings.json")
    if not data:
        return
    count = 0
    for lidar in (data if isinstance(data, list) else []):
        bid = str(lidar.get("id", ""))
        if not bid:
            continue
        if bid not in eds:
            eds[bid] = {"id": bid}
        ed = eds[bid]
        for campo in ("lidar_altura","lidar_forma","lidar_pts","lidar_eje_x",
                      "lidar_eje_z","lidar_z_min","lidar_z_max","puntos_tejado"):
            if lidar.get(campo) is not None:
                ed[campo] = lidar[campo]
        count += 1
    print(f"  LIDAR: {count} edificios enriquecidos")


def enriquecer_inspire(eds: dict) -> dict:
    """
    Lee catastro_navarra_inspire.geojson.
    Campos INSPIRE: localId, heightAboveGround, numberOfFloorsAboveGround,
                    currentUse, lidar_forma, lidar_z_base, lidar_z_top, osm_id
    Devuelve dict osm_id→footprint_wgs84 para uso en export.
    """
    data = load_json(DATA / "catastro_navarra_inspire.geojson")
    footprints: dict[str, list] = {}
    if not data:
        return footprints

    count = 0
    for feat in data.get("features", []):
        props = feat.get("properties", {}) or {}
        geom  = feat.get("geometry") or {}

        # Obtener osm_id para cruzar
        osm_id = str(props.get("osm_id") or props.get("localId") or "")
        if not osm_id:
            continue

        if osm_id not in eds:
            eds[osm_id] = {"id": osm_id}
        ed = eds[osm_id]

        h = props.get("heightAboveGround", 0) or 0
        if h > 0:
            ed["inspire_altura"] = h

        plantas = props.get("numberOfFloorsAboveGround", 0) or 0
        if plantas > 0:
            ed["inspire_plantas"] = plantas

        uso = props.get("currentUse") or ""
        if uso:
            ed["inspire_uso"] = uso

        forma = props.get("lidar_forma") or ""
        if forma:
            ed["inspire_forma"] = forma

        for campo in ("lidar_z_base", "lidar_z_top"):
            v = props.get(campo)
            if v:
                ed[f"inspire_{campo.split('_',1)[1]}"] = v  # inspire_z_base / inspire_z_top

        nombre = props.get("name") or ""
        if nombre and not ed.get("name"):
            ed["name"] = nombre

        ed["inspire_local_id"] = props.get("localId") or props.get("gml_id") or osm_id

        # Footprint
        ring = geojson_first_ring(geom)
        if ring:
            footprints[osm_id] = ring

        count += 1

    print(f"  INSPIRE: {count} features procesadas, {len(footprints)} footprints")
    return footprints


def enriquecer_overture2024(eds: dict) -> dict:
    """
    Lee overture_buildings_2024.geojson.
    Si height > 0 → usar. Si num_floors > 0 y height == 0 → altura = num_floors * 3.2m.
    """
    data = load_json(DATA / "overture_buildings_2024.geojson")
    footprints: dict[str, list] = {}
    if not data:
        return footprints

    count = 0
    for feat in data.get("features", []):
        props = feat.get("properties", {}) or {}
        geom  = feat.get("geometry") or {}

        osm_id = str(props.get("osm_id") or "")
        if not osm_id:
            continue

        if osm_id not in eds:
            eds[osm_id] = {"id": osm_id}
        ed = eds[osm_id]

        ed["overture_id"] = props.get("id") or ""

        ovh = float(props.get("height") or 0)
        ovf = int(props.get("num_floors") or 0)

        if ovh > 0:
            ed["overture_height"] = ovh
        elif ovf > 0:
            ed["overture_height"] = ovf * ALT_PLANTA

        if ovf > 0:
            ed["overture_floors"] = ovf

        cls = props.get("class") or ""
        if cls:
            ed["overture_class"] = cls

        # names.primary → en GeoJSON real es un objeto; aquí viene como names_primary
        # o como objeto anidado dependiendo del archivo
        names = props.get("names") or {}
        primary = (names.get("primary") if isinstance(names, dict) else None) \
                  or props.get("names_primary") or ""
        if primary and not ed.get("overture_name"):
            ed["overture_name"] = primary

        ring = geojson_first_ring(geom)
        if ring:
            footprints[osm_id] = ring

        count += 1

    print(f"  Overture 2024: {count} features procesadas, {len(footprints)} footprints")
    return footprints


def enriquecer_osm_rico(eds: dict):
    data = load_json(DATA / "buildings_osm_rico.json")
    if not data:
        return
    count = 0
    for osm in (data if isinstance(data, list) else []):
        bid = str(osm.get("id") or "")
        if bid not in eds:
            continue
        ed = eds[bid]
        for campo in ("amenity","shop","sport","building_colour","roof_colour","roof_shape"):
            v = osm.get(campo)
            if v and not ed.get(campo):
                ed[campo] = v
        if osm.get("name") and not ed.get("name"):
            ed["name"] = osm["name"]
        count += 1
    print(f"  OSM rico: {count} edificios enriquecidos")


def cargar_mapillary_seqs() -> dict:
    """Devuelve dict building_ref → [sequence_ids]"""
    data = load_json(DATA / "mapillary_imagenes.json")
    seqs: dict[str, list] = {}
    if not data:
        return seqs
    for img in (data if isinstance(data, list) else []):
        ref = str(img.get("_building_ref") or "")
        seq = img.get("sequence_id") or ""
        if not ref:
            continue
        if ref not in seqs:
            seqs[ref] = []
        if seq and seq not in seqs[ref]:
            seqs[ref].append(seq)
    print(f"  Mapillary: {sum(len(v) for v in seqs.values())} secuencias para {len(seqs)} edificios")
    return seqs


# ══════════════════════════════════════════════════════════════════════════
#  CÁLCULO DE ALTURA CON JERARQUÍA COMPLETA
# ══════════════════════════════════════════════════════════════════════════

def get_altura_optima(ed: dict, footprint_unity: list) -> tuple[float, str]:
    """
    Devuelve (altura_m, fuente).
    Prioridad: lidar → inspire → dsm → overture → catastro → osm → floors → inteligente
    """
    lidar_pts  = int(ed.get("lidar_pts") or 0)
    lidar_alt  = float(ed.get("lidar_altura") or 0)
    if lidar_pts >= 5 and lidar_alt > 1.5:
        return lidar_alt, "lidar"

    inspire_alt = float(ed.get("inspire_altura") or 0)
    if inspire_alt > 1.5:
        return inspire_alt, "catastro"

    dsm_alt = float(ed.get("dsm_altura") or 0)
    if dsm_alt > 1.5:
        return dsm_alt, "dsm"

    overture_h = float(ed.get("overture_height") or 0)
    if overture_h > 1.5:
        return overture_h, "overture"

    catastro_alt = float(ed.get("catastro_altura") or 0)
    if catastro_alt > 1.5:
        return catastro_alt, "catastro"

    osm_h = float(ed.get("height") or 0)
    if osm_h > 1.5:
        return osm_h, "osm"

    levels = int(ed.get("levels") or ed.get("inspire_plantas") or
                 ed.get("overture_floors") or 0)
    if levels > 0:
        return levels * ALT_PLANTA, "floors"

    # Altura inteligente (Mejora 3)
    plantas = max(1, levels or 2)
    alt = altura_inteligente(
        plantas,
        ed.get("type") or ed.get("overture_class") or "",
        ed.get("amenity") or "",
        ed.get("shop") or "",
        ed.get("inspire_uso") or ed.get("catastro_uso") or "",
        footprint_unity
    )
    return alt, "floors"


# ══════════════════════════════════════════════════════════════════════════
#  EXPORT FINAL
# ══════════════════════════════════════════════════════════════════════════

def main():
    print("=== generate_buildings_fusion.py ===")
    print(f"  Data dir: {DATA}")

    # 1. Cargar base
    eds = cargar_base()
    if not eds:
        print("ERROR: No se encontró base de edificios.", file=sys.stderr)
        sys.exit(1)

    # 2. LIDAR (prioridad 1)
    enriquecer_lidar(eds)

    # 3. INSPIRE Catastro Navarra (prioridad 2)
    inspire_footprints = enriquecer_inspire(eds)

    # 4. OSM rico
    enriquecer_osm_rico(eds)

    # 5. Overture 2024 (prioridad 4)
    overture_footprints = enriquecer_overture2024(eds)

    # 6. Mapillary
    mapillary_seqs = cargar_mapillary_seqs()

    # 7. Generar registros fusion_final
    resultado = []
    for bid, ed in eds.items():
        # Footprint: INSPIRE > Overture
        ring_wgs84 = inspire_footprints.get(bid) or overture_footprints.get(bid)

        footprint_utm  = ring_wgs84  # lon/lat WGS84 (nombre histórico del campo)
        footprint_unity = []
        cx, cz = UNITY_OX, UNITY_OZ

        if ring_wgs84:
            footprint_unity = []
            sx, sz = 0.0, 0.0
            n_valid = 0
            for coord in ring_wgs84:
                if len(coord) >= 2:
                    ux, uz = lonlat_to_unity(coord[0], coord[1])
                    footprint_unity.append([round(ux, 4), round(uz, 4)])
                    sx += ux; sz += uz
                    n_valid += 1
            if n_valid > 0:
                cx = round(sx / n_valid, 2)
                cz = round(sz / n_valid, 2)

        # Superficie
        surface_m2 = round(polygon_area_m2(footprint_unity), 1) if footprint_unity else 0.0

        # Altura con jerarquía
        altura, height_source = get_altura_optima(ed, footprint_unity)
        altura = round(altura, 2)

        # Plantas
        floors = int(
            ed.get("inspire_plantas") or
            ed.get("overture_floors") or
            ed.get("levels") or
            max(1, round(altura / 3.2))
        )

        # Año
        anio = int(ed.get("anio_construccion") or 0)
        if anio < 1800:
            anio = 0

        # Tipo / uso
        tipo    = ed.get("type") or ed.get("overture_class") or ""
        amenity = ed.get("amenity") or ""
        shop    = ed.get("shop") or ""
        sport   = ed.get("sport") or ""
        uso     = ed.get("inspire_uso") or ed.get("catastro_uso") or ""

        # Arquetipo
        arq = arquetipo_string(tipo, amenity, shop, sport, anio, floors, uso)

        # Roof type (Mejora 4)
        roof_type = roof_type_from_lidar(
            ed.get("lidar_forma") or ed.get("inspire_forma"),
            float(ed.get("lidar_z_min") or 0),
            float(ed.get("lidar_z_max") or 0),
            ed.get("puntos_tejado"),
            anio
        )

        # Color fachada
        facade_color = ed.get("building_colour")
        if not facade_color:
            # Ortofoto sample
            r = ed.get("mat_r", 0)
            g = ed.get("mat_g", 0)
            b = ed.get("mat_b", 0)
            if r or g or b:
                scale = 1.0 / 255.0 if max(r, g, b) > 1.0 else 1.0
                facade_color = "#{:02X}{:02X}{:02X}".format(
                    int(r * scale * 255),
                    int(g * scale * 255),
                    int(b * scale * 255)
                )
        if not facade_color:
            facade_color = color_hex_por_arquetipo(arq)

        # Mapillary
        seqs = mapillary_seqs.get(bid) or []
        has_mapillary = len(seqs) > 0

        registro = {
            "id":                    bid,
            "footprint_utm":         footprint_utm or [],
            "footprint_unity":       footprint_unity or [],
            "center_unity":          [cx, cz],
            "height_m":              altura,
            "height_source":         height_source,
            "floors":                floors,
            "year_built":            anio,
            "archetype":             arq,
            "roof_type":             roof_type,
            "facade_color_hex":      facade_color,
            "has_mapillary_photos":  has_mapillary,
            "mapillary_sequence_ids": seqs,
            "surface_m2":            surface_m2,
        }
        resultado.append(registro)

    # Ordenar por id para reproducibilidad
    resultado.sort(key=lambda x: x["id"])

    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(resultado, f, ensure_ascii=False, indent=2)

    print(f"\n✅ Exportados {len(resultado)} edificios → {OUT}")

    # Estadísticas rápidas
    fuentes = {}
    for r in resultado:
        src = r["height_source"]
        fuentes[src] = fuentes.get(src, 0) + 1
    print("\nFuentes de altura:")
    for k, v in sorted(fuentes.items(), key=lambda x: -x[1]):
        print(f"  {k:12s}: {v:5d} ({100*v/len(resultado):.1f}%)")

    arquetipos = {}
    for r in resultado:
        a = r["archetype"]
        arquetipos[a] = arquetipos.get(a, 0) + 1
    print("\nArquetipos:")
    for k, v in sorted(arquetipos.items(), key=lambda x: -x[1]):
        print(f"  {k:30s}: {v:5d}")

    tejados = {}
    for r in resultado:
        t = r["roof_type"]
        tejados[t] = tejados.get(t, 0) + 1
    print("\nTipos de tejado:")
    for k, v in sorted(tejados.items(), key=lambda x: -x[1]):
        print(f"  {k:12s}: {v:5d}")


if __name__ == "__main__":
    main()
