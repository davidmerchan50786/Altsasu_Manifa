# Tools/RemuestrearColoresTejado.py
# ═══════════════════════════════════════════════════════════════════════════
#  RE-MUESTREO DE COLORES DE TEJADO EN FOOTPRINTS CANÓNICOS
#
#  Deuda técnica tras ReproyectarEdificiosCanonico.py: los vértices de
#  buildings_final.json YA están en el espacio canónico, pero roof_r/g/b_real
#  (y roof_tipo_real) se muestrearon de la ortofoto en las posiciones VIEJAS
#  (~82 m O / ~211 m S) ⇒ el color era el del tejado/calle equivocados.
#
#  Este script re-muestrea el color del tejado DENTRO del polígono canónico de
#  cada edificio (mediana robusta sobre puntos interiores) usando las 72 teselas
#  de orto_tiles_meta.json (bbox en coords UNITY: ux_min/uz_min/ux_max/uz_max,
#  0.25 m/px) y re-clasifica roof_tipo_real con el vocabulario que espera el
#  runtime (GestorMaterialesAlsasua.GetTejado: pizarra_gris/negra,
#  cemento_gris_claro, teja_marron, cubierta_vegetal, desconocido).
#
#  Además actualiza z_base/z_top de catastro_edificios.json desde el
#  lidar_buildings.json ya regenerado (lidar_z_base canónico).
#
#  Salida: buildings_final.json (roof_*_real reescritos) + catastro_edificios.json
#          (z_base/z_top). Backup en _backup_pre_reproyeccion~/ si no existe ya.
# ═══════════════════════════════════════════════════════════════════════════

import json
import shutil
import sys
from pathlib import Path

import numpy as np
from PIL import Image

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

ROOT = Path(__file__).resolve().parent.parent
DATA = ROOT / "Assets" / "AlsasuaData"
TILES = DATA / "tiles" / "orto"
BACKUP = DATA / "_backup_pre_reproyeccion~"
OX, OZ = 1918.0, 8570.0


# ── clasificación → vocabulario del runtime (GestorMaterialesAlsasua) ───────
# Calibrado al carácter vasco: pizarra gris dominante; cemento solo para gris
# MUY claro (zinc/hormigón nuevo); teja para cálidos claros. Umbrales acordes a
# la distribución que documenta el runtime (~pizarra 311 / cemento 343 / 1030).
def clasificar(r, g, b):
    # Cortes derivados de la distribución real de los 1030 tejados re-muestreados
    # (mx mediana 185, crom mediana 24, r-b mediana 23 ⇒ ortofoto PNOA "lava" los
    # tonos): teja solo si cálido MARCADO; cemento solo gris muy claro neutro;
    # el resto pizarra (dominante, coherente con el fallback vasco del runtime).
    mx, mn = max(r, g, b), min(r, g, b)
    crom = mx - mn
    if g > r + 12 and g > b + 12 and g > 70:
        return "cubierta_vegetal"
    if (r - b) >= 40 and r > 130:        # terracota/marrón real
        return "teja_marron"
    if mx < 70:
        return "pizarra_negra"
    if mx >= 188 and crom < 22:          # zinc/hormigón nuevo (gris muy claro)
        return "cemento_gris_claro"
    return "pizarra_gris"                 # dominante (~83%)


def cargar_tiles_meta():
    metas = json.load(open(DATA / "orto_tiles_meta.json", encoding="utf-8"))
    # ordenar por área para que el primero que contiene un punto sea estable
    for m in metas:
        m["_img"] = None
    return metas


def tile_de(metas, cx, cz):
    for m in metas:
        if m["ux_min"] <= cx <= m["ux_max"] and m["uz_min"] <= cz <= m["uz_max"]:
            return m
    return None


def pixel_de(m, cx, cz):
    px = (cx - m["ux_min"]) / (m["ux_max"] - m["ux_min"]) * (m["width_px"] - 1)
    # Unity Z crece al norte; fila 0 de la imagen = norte = uz_max
    py = (m["uz_max"] - cz) / (m["uz_max"] - m["uz_min"]) * (m["height_px"] - 1)
    return int(round(px)), int(round(py))


def img_de(m):
    if m["_img"] is None:
        p = TILES / m["file"]
        m["_img"] = np.asarray(Image.open(p).convert("RGB")) if p.exists() else np.zeros((1, 1, 3), np.uint8)
    return m["_img"]


def puntos_interiores(verts, paso=1.5, maxn=120):
    """Grid de puntos Unity dentro del polígono (ray-casting), incl. centroide."""
    xs = np.array([v["x"] + OX for v in verts])
    zs = np.array([v["z"] + OZ for v in verts])
    cx, cz = xs.mean(), zs.mean()
    pts = [(cx, cz)]
    x0, x1, z0, z1 = xs.min(), xs.max(), zs.min(), zs.max()
    nx = max(1, int((x1 - x0) / paso)); nz = max(1, int((z1 - z0) / paso))
    nx = min(nx, 40); nz = min(nz, 40)
    for iz in range(nz + 1):
        for ix in range(nx + 1):
            x = x0 + (x1 - x0) * ix / max(1, nx)
            z = z0 + (z1 - z0) * iz / max(1, nz)
            # point in polygon
            dentro = False
            j = len(xs) - 1
            for i in range(len(xs)):
                if ((zs[i] > z) != (zs[j] > z)) and \
                   (x < (xs[j] - xs[i]) * (z - zs[i]) / (zs[j] - zs[i] + 1e-12) + xs[i]):
                    dentro = not dentro
                j = i
            if dentro:
                pts.append((x, z))
    if len(pts) > maxn:
        idx = np.linspace(0, len(pts) - 1, maxn).astype(int)
        pts = [pts[i] for i in idx]
    return pts


def main():
    final = json.load(open(DATA / "buildings_final.json", encoding="utf-8"))
    metas = cargar_tiles_meta()

    BACKUP.mkdir(exist_ok=True)
    if not (BACKUP / "buildings_final.colores.json").exists():
        shutil.copy2(DATA / "buildings_final.json", BACKUP / "buildings_final.colores.json")
        print(f"  backup: buildings_final.colores.json")

    # ── re-muestreo de color de tejado ─────────────────────────────────────
    cambios, sin_tile, tipos = 0, 0, {}
    dist_color = []
    for ed in final:
        v = ed.get("vertices")
        if not v:
            continue
        muestras = []
        for (x, z) in puntos_interiores(v):
            m = tile_de(metas, x, z)
            if m is None:
                continue
            img = img_de(m)
            if img.shape[0] < 2:
                continue
            px, py = pixel_de(m, x, z)
            px = min(max(px, 1), img.shape[1] - 2)
            py = min(max(py, 1), img.shape[0] - 2)
            muestras.append(img[py - 1:py + 2, px - 1:px + 2].reshape(-1, 3))
        if not muestras:
            sin_tile += 1
            continue
        rgb = np.median(np.concatenate(muestras, 0), 0)
        r, g, b = (int(round(c)) for c in rgb)

        # distancia al color viejo (para reporte de cuánto cambia)
        r0 = ed.get("roof_r_real", -1)
        if r0 >= 0:
            dist_color.append(abs(r - r0) + abs(g - ed.get("roof_g_real", 0))
                              + abs(b - ed.get("roof_b_real", 0)))
        tipo = clasificar(r, g, b)
        ed["roof_r_real"], ed["roof_g_real"], ed["roof_b_real"] = r, g, b
        ed["roof_tipo_real"] = tipo
        tipos[tipo] = tipos.get(tipo, 0) + 1
        cambios += 1

    print(f"\nColor de tejado re-muestreado: {cambios} edificios, {sin_tile} sin cobertura ortofoto")
    if dist_color:
        dc = np.array(dist_color)
        print(f"  cambio de color (|ΔR|+|ΔG|+|ΔB|): mediana={np.median(dc):.0f}, "
              f"p90={np.percentile(dc,90):.0f}  (mide cuánto corrige el desplazamiento viejo)")
    print("  distribución de tipos:")
    for t, n in sorted(tipos.items(), key=lambda x: -x[1]):
        print(f"    {t:18s}: {n}")

    # iglesia como sanity check
    igl = next((b for b in final if b["id"] == 91927762), None)
    if igl:
        print(f"  iglesia: roof=({igl['roof_r_real']},{igl['roof_g_real']},"
              f"{igl['roof_b_real']}) tipo={igl['roof_tipo_real']}")

    json.dump(final, open(DATA / "buildings_final.json", "w", encoding="utf-8"),
              ensure_ascii=False, separators=(",", ":"))
    print("  buildings_final.json escrito")

    # ── z_base/z_top del catastro desde el LIDAR ya regenerado ─────────────
    cat_path = DATA / "catastro_edificios.json"
    lid_path = DATA / "lidar_buildings.json"
    if cat_path.exists() and lid_path.exists():
        cat = json.load(open(cat_path, encoding="utf-8"))
        lid = {b["id"]: b for b in json.load(open(lid_path, encoding="utf-8"))}
        if not (BACKUP / "catastro_edificios.json").exists():
            shutil.copy2(cat_path, BACKUP / "catastro_edificios.json")
        act = 0
        for c in cat:
            ld = lid.get(c.get("osm_id"))
            if ld and "lidar_z_base" in ld:
                c["z_base"] = ld["lidar_z_base"]
                c["z_top"] = ld.get("lidar_z_top", c.get("z_top"))
                act += 1
        json.dump(cat, open(cat_path, "w", encoding="utf-8"),
                  ensure_ascii=False, separators=(",", ":"))
        print(f"\nz_base/z_top catastro actualizados desde LIDAR canónico: {act}/{len(cat)}")

    print("\n✅ Re-muestreo de colores y cotas completado.")
    print("   PENDIENTE (regenerar en Unity, derivados): buildings_fusion_final.json "
          "→ FusionadorEdificiosUltra ▸ ContextMenu 'Exportar buildings_fusion_final.json'.")


if __name__ == "__main__":
    main()
