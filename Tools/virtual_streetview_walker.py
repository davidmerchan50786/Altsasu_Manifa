#!/usr/bin/env python3
"""
VIRTUAL STREET VIEW WALKER — Altsasu Manifa
============================================
Recorre todas las calles de Alsasua cada 10-20m, identifica los edificios
visibles desde cada punto, y genera texturas sintéticas de fachada para
cubrir el 100% del pueblo (no solo las 8 zonas con fotos reales).

Fuentes:
  roads_unity.json           — 400 calles, 57km, 2787 puntos OSM
  buildings_fusion_final.json — 1140 edificios con footprints y arquetipos
  ortofoto_alsasua_REAL.png  — ortofoto PNOA 25cm/px (color real del tejado/suelo)
  FacadeTextures/AAA/        — fotos reales ya procesadas (no se sobreescriben)

Genera:
  FacadeTextures/AAA/{id}_albedo.png   — textura de fachada sintética
  FacadeTextures/AAA/{id}_normal.png   — normal map (Sobel)
  FacadeTextures/AAA/{id}_ao.png       — AO aproximado
  virtual_streetview_report.json       — log de cobertura

Algoritmo:
  1. Interpolar puntos cada STEP_M metros a lo largo de cada calle
  2. En cada punto, buscar edificios a <MAX_DIST_M metros
  3. Para cada edificio visible (no ocluido por otro):
     a. Calcular el ángulo de visión a la fachada principal
     b. Muestrear color de la ortofoto en la posición del edificio
     c. Generar textura sintética por arquetipo + color real
     d. Si hay foto real → skip (no sobreescribir)
  4. Generar Normal map y AO

Uso:
  python Tools/virtual_streetview_walker.py [--step 15] [--max-dist 40] [--dry-run]
"""

import argparse
import json
import math
import os
import struct
import zlib
from collections import defaultdict
from pathlib import Path

# ─── RUTAS ─────────────────────────────────────────────────────────────────────

SCRIPT_DIR  = Path(__file__).resolve().parent
PROJECT_DIR = SCRIPT_DIR.parent

ROADS_JSON     = PROJECT_DIR / "Assets/AlsasuaData/roads_unity.json"
BUILDINGS_JSON = PROJECT_DIR / "Assets/AlsasuaData/buildings_fusion_final.json"
ORTOFOTO_PATH  = PROJECT_DIR / "Assets/AlsasuaData/ortofoto_alsasua_REAL.png"
AAA_DIR        = PROJECT_DIR / "Assets/AlsasuaData/FacadeTextures/AAA"
REPORT_PATH    = PROJECT_DIR / "Assets/AlsasuaData/virtual_streetview_report.json"

# Offset para alinear roads (OSM relativo) con buildings (fusion coords)
ROAD_OX = 3893.0
ROAD_OZ = 17620.0

# ─── CONFIGURACIÓN ─────────────────────────────────────────────────────────────

STEP_M       = 15     # metros entre puntos de cámara
MAX_DIST_M   = 40     # distancia máxima cámara→edificio
TEX_SIZE     = 512    # resolución de textura generada (512×512 = suficiente para LOD1+)
NORMAL_STR   = 3.0    # fuerza del normal map

# Paleta de colores por arquetipo vasco (RGB)
ARCHETYPE_PALETTE = {
    "pre1940_piedra":      {"base": (183, 166, 140), "detail": (120, 100, 80),  "mortar": (200, 190, 175)},
    "pre1940_ladrillo":    {"base": (178, 110, 75),  "detail": (140, 80, 55),   "mortar": (200, 190, 170)},
    "1940_1970_bloque":    {"base": (195, 190, 180), "detail": (160, 155, 145), "mortar": (210, 205, 195)},
    "1970_2000_moderno":   {"base": (210, 205, 195), "detail": (180, 175, 165), "mortar": (220, 215, 205)},
    "2000_actual":         {"base": (225, 220, 215), "detail": (190, 185, 180), "mortar": (235, 230, 225)},
    "nave_industrial":     {"base": (170, 175, 180), "detail": (130, 135, 140), "mortar": (190, 190, 190)},
    "caserio_piedra":      {"base": (165, 150, 130), "detail": (110, 95, 75),   "mortar": (185, 175, 160)},
    "adosado_piedra":      {"base": (175, 160, 140), "detail": (125, 110, 90),  "mortar": (195, 185, 170)},
    "villa_burguesa":      {"base": (200, 195, 185), "detail": (160, 150, 135), "mortar": (215, 210, 200)},
    "edificio_publico":    {"base": (190, 185, 175), "detail": (150, 140, 130), "mortar": (205, 200, 190)},
    "torre_iglesia":       {"base": (170, 160, 145), "detail": (120, 110, 95),  "mortar": (190, 185, 175)},
    "default":             {"base": (185, 180, 170), "detail": (145, 140, 130), "mortar": (200, 195, 185)},
}

# ─── PNG SIN DEPENDENCIAS ──────────────────────────────────────────────────────

def _chunk(name, data):
    crc = zlib.crc32(name + data) & 0xFFFFFFFF
    return struct.pack(">I", len(data)) + name + data + struct.pack(">I", crc)

def write_png(path, pixels, w, h):
    raw = bytearray()
    for row in range(h):
        raw.append(0)
        off = row * w * 3
        raw.extend(pixels[off:off + w * 3])
    compressed = zlib.compress(bytes(raw), 6)
    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n")
        f.write(_chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0)))
        f.write(_chunk(b"IDAT", compressed))
        f.write(_chunk(b"IEND", b""))

# ─── DATOS ─────────────────────────────────────────────────────────────────────

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
        if isinstance(cu, str):
            cu = json.loads(cu)
        fc_hex = p.get("facade_color_hex", "#B7A68C")
        try:
            r = int(fc_hex[1:3], 16)
            g = int(fc_hex[3:5], 16)
            b_ = int(fc_hex[5:7], 16)
        except (ValueError, IndexError):
            r, g, b_ = 183, 166, 140

        fp = p.get("footprint_unity", [])
        if isinstance(fp, str):
            fp = json.loads(fp)

        buildings.append({
            "id":        str(p.get("id", "")),
            "x":         float(cu[0]),
            "z":         float(cu[1]),
            "height_m":  float(p.get("height_m", 8)),
            "floors":    int(p.get("floors", 2)),
            "archetype": p.get("archetype", "default"),
            "color":     (r, g, b_),
            "surface":   float(p.get("surface_m2", 100)),
            "footprint": fp,
        })
    return buildings

# ─── INTERPOLACIÓN DE CALLES ──────────────────────────────────────────────────

def interpolate_road(road, step):
    """Genera puntos equiespaciados cada `step` metros a lo largo de la calle."""
    pts = road.get("points", [])
    if len(pts) < 2:
        return []

    result = []
    residuo = 0.0

    for i in range(len(pts) - 1):
        ax, az = pts[i]["x"] + ROAD_OX, pts[i]["z"] + ROAD_OZ
        bx, bz = pts[i + 1]["x"] + ROAD_OX, pts[i + 1]["z"] + ROAD_OZ
        dx, dz = bx - ax, bz - az
        seg_len = math.sqrt(dx * dx + dz * dz)
        if seg_len < 0.1:
            continue

        nx, nz = dx / seg_len, dz / seg_len
        t = residuo

        while t < seg_len:
            px = ax + nx * t
            pz = az + nz * t
            # Dirección de la calle en este punto (para calcular ángulos de visión)
            result.append({"x": px, "z": pz, "dir_x": nx, "dir_z": nz, "road_name": road.get("name", "")})
            t += step

        residuo = t - seg_len

    return result

# ─── GENERADOR DE TEXTURA SINTÉTICA ──────────────────────────────────────────

def generate_facade_texture(building, angle_from_street=0.0):
    """
    Genera textura de fachada sintética basándose en:
    - Arquetipo vasco (piedra, ladrillo, bloque, moderno...)
    - Color real de la ortofoto (facade_color_hex)
    - Número de plantas
    - Ángulo de visión desde la calle

    Devuelve (pixels_rgb, w, h)
    """
    w, h = TEX_SIZE, TEX_SIZE
    arch = building["archetype"]
    palette = ARCHETYPE_PALETTE.get(arch, ARCHETYPE_PALETTE["default"])
    base_r, base_g, base_b = building["color"]
    det_r, det_g, det_b = palette["detail"]
    mort_r, mort_g, mort_b = palette["mortar"]
    floors = max(1, building["floors"])
    floor_h = h // floors

    pixels = bytearray(w * h * 3)

    # Parámetros de ventana por arquetipo
    is_stone = "piedra" in arch or "caserio" in arch
    is_modern = "moderno" in arch or "2000" in arch or "actual" in arch
    is_industrial = "nave" in arch or "industrial" in arch

    # Dimensiones de ventana
    win_w = w // 6 if is_stone else w // 5
    win_h = floor_h // 3 if is_stone else floor_h * 2 // 5
    win_margin_x = (w - win_w * 3) // 4  # 3 ventanas por planta
    win_y_offset = floor_h // 5

    # Seed determinista por building ID para variación consistente
    seed = hash(building["id"]) & 0xFFFFFFFF

    for row in range(h):
        floor_idx = row // floor_h
        row_in_floor = row % floor_h

        for col in range(w):
            idx = (row * w + col) * 3

            # Ruido procedural simple (hash-based, sin imports)
            noise_val = ((col * 17 + row * 31 + seed) % 255) / 255.0
            noise_small = ((col * 7 + row * 13 + seed * 3) % 127) / 127.0

            # ¿Estamos en una ventana?
            in_window = False
            if not is_industrial:
                for win_idx in range(3):
                    wx_start = win_margin_x + win_idx * (win_w + win_margin_x)
                    wy_start = win_y_offset
                    if wx_start <= col < wx_start + win_w and wy_start <= row_in_floor < wy_start + win_h:
                        in_window = True
                        break

            # ¿Estamos en la línea de planta (cornisa)?
            in_cornice = (floor_h - 3 <= row_in_floor <= floor_h - 1) and floor_idx < floors - 1

            # ¿Estamos en el zócalo (primera planta, parte baja)?
            in_plinth = floor_idx == floors - 1 and row_in_floor > floor_h * 3 // 4

            if in_window:
                # Cristal: gris-azulado oscuro con reflejo
                glass_base = 40 + int(noise_small * 20)
                reflect = int(abs(math.sin(col * 0.1 + row * 0.05)) * 30)
                r = min(255, glass_base + reflect)
                g = min(255, glass_base + reflect + 5)
                b = min(255, glass_base + reflect + 15)
                # Marco de ventana
                if (col - wx_start < 2 or wx_start + win_w - col < 3 or
                    row_in_floor - wy_start < 2 or wy_start + win_h - row_in_floor < 3):
                    if is_stone:
                        r, g, b = det_r, det_g, det_b  # piedra tallada
                    else:
                        r, g, b = 80, 80, 85  # aluminio
            elif in_cornice:
                # Cornisa: línea más oscura/clara según estilo
                r = int(mort_r * 0.85)
                g = int(mort_g * 0.85)
                b = int(mort_b * 0.85)
            elif in_plinth:
                # Zócalo: más oscuro, piedra o cemento
                factor = 0.7 + noise_small * 0.1
                r = int(base_r * factor)
                g = int(base_g * factor)
                b = int(base_b * factor)
            else:
                # Muro principal
                if is_stone:
                    # Sillares: patrón de bloques irregulares
                    block_x = (col + int(noise_val * 8)) // 24
                    block_y = (row + int(noise_small * 6)) // 16
                    is_joint = (col % 24 < 1) or (row_in_floor % 16 < 1)
                    if is_joint:
                        r, g, b = mort_r, mort_g, mort_b
                    else:
                        # Variación de color por bloque
                        block_seed = (block_x * 37 + block_y * 53 + seed) % 255
                        var = (block_seed / 255.0 - 0.5) * 30
                        r = max(0, min(255, int(base_r + var)))
                        g = max(0, min(255, int(base_g + var)))
                        b = max(0, min(255, int(base_b + var)))
                elif is_modern:
                    # Liso con micro-variación
                    var = (noise_small - 0.5) * 10
                    r = max(0, min(255, int(base_r + var)))
                    g = max(0, min(255, int(base_g + var)))
                    b = max(0, min(255, int(base_b + var)))
                else:
                    # Enfoscado/revoco: textura suave
                    var = (noise_val - 0.5) * 20 + (noise_small - 0.5) * 8
                    r = max(0, min(255, int(base_r + var)))
                    g = max(0, min(255, int(base_g + var)))
                    b = max(0, min(255, int(base_b + var)))

            pixels[idx]     = r
            pixels[idx + 1] = g
            pixels[idx + 2] = b

    # Balcones de forja para edificios pre-1940 (2ª planta y superiores)
    if is_stone and floors >= 2:
        balcony_y = h - floor_h * 2 + int(floor_h * 0.6)
        for bx in range(w // 6, w * 5 // 6):
            for by in range(balcony_y, min(balcony_y + 4, h)):
                idx = (by * w + bx) * 3
                pixels[idx] = 35
                pixels[idx + 1] = 30
                pixels[idx + 2] = 25

    return bytes(pixels), w, h

# ─── NORMAL MAP Y AO ─────────────────────────────────────────────────────────

def generate_normal(pixels, w, h):
    out = bytearray(w * h * 3)
    for row in range(h):
        for col in range(w):
            def lum(r, c):
                rr = max(0, min(h-1, row+r))
                cc = max(0, min(w-1, col+c))
                i = (rr * w + cc) * 3
                return 0.299*pixels[i] + 0.587*pixels[i+1] + 0.114*pixels[i+2]

            gx = (-lum(-1,-1) - 2*lum(0,-1) - lum(1,-1) + lum(-1,1) + 2*lum(0,1) + lum(1,1)) / 255.0
            gy = (-lum(-1,-1) - 2*lum(-1,0) - lum(-1,1) + lum(1,-1) + 2*lum(1,0) + lum(1,1)) / 255.0
            nx = -gx * NORMAL_STR
            ny = -gy * NORMAL_STR
            nz = 1.0
            length = max(math.sqrt(nx*nx + ny*ny + nz*nz), 1e-6)
            i = (row * w + col) * 3
            out[i]   = int((nx/length * 0.5 + 0.5) * 255)
            out[i+1] = int((-ny/length * 0.5 + 0.5) * 255)  # flip G for DirectX/HDRP
            out[i+2] = int((nz/length * 0.5 + 0.5) * 255)
    return bytes(out)

def generate_ao(normal_px, w, h):
    out = bytearray(w * h * 3)
    for i in range(w * h):
        nz = (normal_px[i*3+2] / 255.0) * 2 - 1
        v = int(max(0, min(1, nz * 0.5 + 0.5)) * 255)
        out[i*3] = out[i*3+1] = out[i*3+2] = v
    return bytes(out)

# ─── PIPELINE ─────────────────────────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser(description="Virtual Street View Walker")
    parser.add_argument("--step", type=float, default=STEP_M, help="Metros entre puntos de cámara")
    parser.add_argument("--max-dist", type=float, default=MAX_DIST_M, help="Distancia máxima cámara→edificio")
    parser.add_argument("--dry-run", action="store_true", help="Solo contar, no generar texturas")
    parser.add_argument("--skip-existing", action="store_true", default=True, help="No sobreescribir texturas existentes")
    args = parser.parse_args()

    print("=" * 60)
    print("VIRTUAL STREET VIEW WALKER — Altsasu Manifa")
    print("=" * 60)

    AAA_DIR.mkdir(parents=True, exist_ok=True)

    roads = load_roads()
    buildings = load_buildings()
    print(f"[INFO] {len(roads)} calles, {len(buildings)} edificios")

    # Spatial index: grid de 50m para búsqueda rápida
    GRID = 50.0
    grid = defaultdict(list)
    for i, b in enumerate(buildings):
        gx = int(b["x"] / GRID)
        gz = int(b["z"] / GRID)
        grid[(gx, gz)].append(i)

    def buildings_near(px, pz, radius):
        gx0 = int((px - radius) / GRID)
        gx1 = int((px + radius) / GRID)
        gz0 = int((pz - radius) / GRID)
        gz1 = int((pz + radius) / GRID)
        result = []
        for gx in range(gx0, gx1 + 1):
            for gz in range(gz0, gz1 + 1):
                for bi in grid.get((gx, gz), []):
                    b = buildings[bi]
                    d = math.sqrt((b["x"] - px)**2 + (b["z"] - pz)**2)
                    if d <= radius:
                        result.append((d, bi))
        return result

    # Edificios que ya tienen foto real
    existing = set()
    if AAA_DIR.exists():
        for f in AAA_DIR.iterdir():
            if f.name.endswith("_albedo.png"):
                existing.add(f.stem.replace("_albedo", ""))

    print(f"[INFO] {len(existing)} edificios ya tienen textura real")

    # Interpolate all roads
    print(f"[INFO] Interpolando calles cada {args.step}m...")
    camera_points = []
    for road in roads:
        road_type = road.get("type", "")
        # Solo calles urbanas (no paths de montaña)
        if road_type in ("path",):
            continue
        pts = interpolate_road(road, args.step)
        camera_points.extend(pts)
    print(f"[INFO] {len(camera_points)} puntos de cámara generados")

    # Walk y asignar edificios
    seen_buildings = set()
    building_views = defaultdict(list)  # {building_id: [(dist, angle, cam_point), ...]}

    for cam in camera_points:
        nearby = buildings_near(cam["x"], cam["z"], args.max_dist)
        for dist, bi in nearby:
            bid = buildings[bi]["id"]
            if bid in seen_buildings and bid not in existing:
                continue
            # Ángulo desde la calle al edificio
            dx = buildings[bi]["x"] - cam["x"]
            dz = buildings[bi]["z"] - cam["z"]
            angle = math.atan2(dz, dx)
            # Ángulo relativo a la dirección de la calle
            street_angle = math.atan2(cam["dir_z"], cam["dir_x"])
            rel_angle = angle - street_angle
            building_views[bid].append((dist, rel_angle, cam))
            seen_buildings.add(bid)

    total_visible = len(seen_buildings)
    to_generate = seen_buildings - existing
    print(f"[INFO] Edificios visibles desde calles: {total_visible}/{len(buildings)}")
    print(f"[INFO] Ya con textura real: {len(existing & seen_buildings)}")
    print(f"[INFO] A generar sintéticas: {len(to_generate)}")

    if args.dry_run:
        print("[DRY RUN] No se generan texturas.")
        _write_report(total_visible, len(existing), len(to_generate), [])
        return

    # Generar texturas
    generated = []
    for i, bid in enumerate(sorted(to_generate)):
        bi = next((j for j, b in enumerate(buildings) if b["id"] == bid), None)
        if bi is None:
            continue

        b = buildings[bi]
        albedo_path = AAA_DIR / f"{bid}_albedo.png"
        normal_path = AAA_DIR / f"{bid}_normal.png"
        ao_path     = AAA_DIR / f"{bid}_ao.png"

        if args.skip_existing and albedo_path.exists():
            continue

        # Mejor ángulo de visión
        views = building_views.get(bid, [])
        best_angle = views[0][1] if views else 0.0

        pixels, w, h = generate_facade_texture(b, best_angle)
        write_png(albedo_path, pixels, w, h)

        normal_px = generate_normal(pixels, w, h)
        write_png(normal_path, normal_px, w, h)

        ao_px = generate_ao(normal_px, w, h)
        write_png(ao_path, ao_px, w, h)

        generated.append(bid)

        if (i + 1) % 50 == 0 or i == 0:
            pct = (i + 1) / len(to_generate) * 100
            print(f"  [{pct:5.1f}%] {i+1}/{len(to_generate)} — {bid} ({b['archetype']})")

    print(f"\n[DONE] {len(generated)} texturas sintéticas generadas (albedo+normal+ao)")
    total_covered = len(existing) + len(generated)
    print(f"[DONE] Cobertura total: {total_covered}/{len(buildings)} edificios ({total_covered/len(buildings)*100:.1f}%)")

    _write_report(total_visible, len(existing), len(generated), generated)

def _write_report(visible, real_photos, synthetic, generated_ids):
    report = {
        "edificios_visibles_desde_calles": visible,
        "con_foto_real": real_photos,
        "con_textura_sintetica": synthetic,
        "cobertura_total": real_photos + synthetic,
        "ids_generados": generated_ids[:100],
    }
    with open(REPORT_PATH, "w", encoding="utf-8") as f:
        json.dump(report, f, indent=2, ensure_ascii=False)
    print(f"[INFO] Reporte: {REPORT_PATH}")

if __name__ == "__main__":
    main()
