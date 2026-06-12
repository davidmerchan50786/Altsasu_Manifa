# Tools/DescargarMDT_Mosaico.py
# ═══════════════════════════════════════════════════════════════════════════
#  FASE F1a del plan Terreno Mosaico V2 — descarga y preparación de fuentes
#
#  Fuentes (todas EPSG:25830, UTM 30N ETRS89):
#    1. IGN MDT05  — WCS servicios.idee.es Elevacion25830_5  (5 m, todo el cuadro)
#    2. IGN MDT25  — WCS servicios.idee.es Elevacion25830_25 (25 m, fallback)
#    3. IDENA MDT 2 m (2024) — WCS idena.navarra.es (anillos 0-1, solo Navarra)
#    4. IDENA MDS 2 m (2024) — superficie, para regenerar el DSM (anillo 0)
#    5. LIDAR PNOA local (E:\567, 97 LAZ) — DTM 0.5 m clase 2 ampliado a
#       plaza±1300 m Unity (el raw v1 solo cubría ±512 m)
#
#  CONVENCIÓN HORIZONTAL (verificada empíricamente 2026-06-11 contra MDT05,
#  mediana de error 0.19 m; ver memoria del proyecto):
#       UnityX = (E − 567951) × (76400/81548) + 1918
#       UnityZ = (N − 4749902) + 8570
#
#  Salida: DatosGIS/*.npz  — grids float32 con meta (e0, n0, cell, fila 0 = SUR,
#  valores en centros de celda). Reproducible: borrar DatosGIS y relanzar.
#
#  Uso:  python Tools/DescargarMDT_Mosaico.py [--solo ign|idena|lidar]
# ═══════════════════════════════════════════════════════════════════════════

import argparse
import io
import json
import math
import struct
import sys
import time
import urllib.request
import urllib.parse
from pathlib import Path

import numpy as np

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

# ── Constantes del mundo (única fuente: GeoDataAlsasua.cs / esta cabecera) ──
E0, N0 = 567951.0, 4749902.0       # UTM Herriko Plaza
OX, OZ = 1918.0, 8570.0            # Unity Herriko Plaza
SX = 76400.0 / 81548.0             # escala X UTM→Unity (≈0.93687)
Z_MIN = 511.33                     # datum vertical (AlturaUnity = cota − Z_MIN)
COTA_PLAZA = 531.94                # cota real de la plaza (validada)

# Extensión del mosaico en Unity: plaza ± 7200 m (14.4×14.4 km)
HALF_UNITY = 7200.0
MARGEN_M = 250.0                   # margen para blending y Catmull-Rom

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "DatosGIS"
OUT.mkdir(exist_ok=True)

LAZ_DIR = Path("E:/567")

WCS_IGN = "https://servicios.idee.es/wcs-inspire/mdt"
WCS_IDENA = "https://idena.navarra.es/ogc/wcs"
# Rejilla nativa IDENA (DescribeCoverage): envolvente desde 539160.375 / 4639999.625, paso 2 m
IDENA_GRID_E0 = 539160.375
IDENA_GRID_N0 = 4639999.625


def unity_a_utm(ux, uz):
    return (ux - OX) / SX + E0, (uz - OZ) + N0


def bbox_utm(half_unity_x, half_unity_z, margen=MARGEN_M):
    """BBox UTM que cubre un cuadrado Unity centrado en la plaza."""
    e_min, n_min = unity_a_utm(OX - half_unity_x, OZ - half_unity_z)
    e_max, n_max = unity_a_utm(OX + half_unity_x, OZ + half_unity_z)
    return e_min - margen, n_min - margen, e_max + margen, n_max + margen


def http_get(url, intentos=4, timeout=180):
    ultimo = None
    for i in range(intentos):
        try:
            req = urllib.request.Request(url, headers={"User-Agent": "AltsasuManifa-mosaico/1.0"})
            with urllib.request.urlopen(req, timeout=timeout) as r:
                return r.read()
        except Exception as ex:
            ultimo = ex
            espera = 3 * (i + 1)
            print(f"    reintento {i+1}/{intentos} en {espera}s ({ex})")
            time.sleep(espera)
    raise RuntimeError(f"GET falló tras {intentos} intentos: {url}\n{ultimo}")


def guardar_npz(nombre, z, e0, n0, cell, fuente, valid=None):
    """Grid float32, fila 0 = SUR, valores en centros de celda (e0+(i+0.5)cell...).
    valid: máscara bool opcional de celdas con dato REAL (no rellenado)."""
    path = OUT / f"{nombre}.npz"
    extra = {} if valid is None else {"valid": valid.astype(bool)}
    np.savez_compressed(path, z=z.astype(np.float32), e0=e0, n0=n0, cell=cell, **extra)
    meta = dict(e0=e0, n0=n0, cell=cell, ncols=int(z.shape[1]), nrows=int(z.shape[0]),
                z_min=float(np.nanmin(z)), z_max=float(np.nanmax(z)), fuente=fuente,
                orden_filas="fila 0 = sur", registro="centros de celda")
    (OUT / f"{nombre}.meta.json").write_text(json.dumps(meta, indent=1))
    print(f"  ✅ {path.name}: {z.shape[1]}×{z.shape[0]} @ {cell} m  "
          f"cota [{meta['z_min']:.1f}, {meta['z_max']:.1f}]")
    return path


# ═══════════════════════════════════════════════════════════════════════════
#  1-2. IGN — WCS application/asc (multipart MIME), troceado
# ═══════════════════════════════════════════════════════════════════════════

def parsear_asc_mime(data):
    txt = data.decode("ascii", errors="replace")
    i = txt.find("ncols")
    if i < 0:
        raise RuntimeError(f"Respuesta WCS sin ASC (¿excepción?): {txt[:300]}")
    lines = txt[i:].strip().splitlines()
    hdr, data_start = {}, 0
    for k, ln in enumerate(lines):
        p = ln.split()
        if len(p) == 2 and p[0].lower() in ("ncols", "nrows", "xllcorner", "yllcorner",
                                            "cellsize", "nodata_value"):
            hdr[p[0].lower()] = float(p[1]); data_start = k + 1
        else:
            break
    nc, nr = int(hdr["ncols"]), int(hdr["nrows"])
    grid = np.loadtxt(lines[data_start:data_start + nr]).reshape(nr, nc)
    nodata = hdr.get("nodata_value")
    if nodata is not None:
        grid[grid == nodata] = np.nan
    # ASC: fila 0 = norte → invertir a fila 0 = sur
    return np.flipud(grid).astype(np.float32), hdr["xllcorner"], hdr["yllcorner"], hdr["cellsize"]


def descargar_ign(coverage, cell, nombre, bbox, chunk_px=1600):
    e_min, n_min, e_max, n_max = bbox
    # alinear a la rejilla del MDT (múltiplos de cell) para chunks exactos
    e_min = math.floor(e_min / cell) * cell
    n_min = math.floor(n_min / cell) * cell
    e_max = math.ceil(e_max / cell) * cell
    n_max = math.ceil(n_max / cell) * cell
    nc_tot = round((e_max - e_min) / cell)
    nr_tot = round((n_max - n_min) / cell)
    print(f"▶ IGN {coverage}: {nc_tot}×{nr_tot} @ {cell} m "
          f"E[{e_min:.0f},{e_max:.0f}] N[{n_min:.0f},{n_max:.0f}]")
    if (OUT / f"{nombre}.npz").exists():
        print("  (cacheado, omitido — borra DatosGIS para regenerar)")
        return
    total = np.full((nr_tot, nc_tot), np.nan, dtype=np.float32)
    paso = chunk_px * cell
    for n_a in np.arange(n_min, n_max, paso):
        for e_a in np.arange(e_min, e_max, paso):
            e_b, n_b = min(e_a + paso, e_max), min(n_a + paso, n_max)
            url = (f"{WCS_IGN}?SERVICE=WCS&VERSION=2.0.1&REQUEST=GetCoverage"
                   f"&COVERAGEID={coverage}&SUBSET=x({e_a},{e_b})&SUBSET=y({n_a},{n_b})"
                   f"&FORMAT=application/asc")
            print(f"  chunk E[{e_a:.0f},{e_b:.0f}] N[{n_a:.0f},{n_b:.0f}] ...")
            grid, xll, yll, c = parsear_asc_mime(http_get(url))
            if abs(c - cell) > 1e-6:
                raise RuntimeError(f"cellsize inesperado {c} != {cell}")
            i0 = round((xll - e_min) / cell)
            j0 = round((yll - n_min) / cell)
            total[j0:j0 + grid.shape[0], i0:i0 + grid.shape[1]] = grid
    faltan = int(np.isnan(total).sum())
    if faltan:
        print(f"  ⚠ {faltan} celdas NaN ({100*faltan/total.size:.2f} %)")
    guardar_npz(nombre, total, e_min, n_min, cell, f"IGN WCS {coverage}")


# ═══════════════════════════════════════════════════════════════════════════
#  3-4. IDENA — WCS GeoTIFF, troceado, alineado a su rejilla nativa
# ═══════════════════════════════════════════════════════════════════════════

def descargar_idena(coverage, nombre, bbox, chunk_px=1000):
    # chunk_px=1000 (2000 m): GeoServer devuelve 500 con salidas >≈16 MB (2000×2000 px float32)
    import tifffile
    cell = 2.0
    e_min, n_min, e_max, n_max = bbox
    # alinear a la rejilla nativa de IDENA (bordes en GRID0 + k·2)
    e_min = IDENA_GRID_E0 + math.floor((e_min - IDENA_GRID_E0) / cell) * cell
    n_min = IDENA_GRID_N0 + math.floor((n_min - IDENA_GRID_N0) / cell) * cell
    e_max = IDENA_GRID_E0 + math.ceil((e_max - IDENA_GRID_E0) / cell) * cell
    n_max = IDENA_GRID_N0 + math.ceil((n_max - IDENA_GRID_N0) / cell) * cell
    nc_tot = round((e_max - e_min) / cell)
    nr_tot = round((n_max - n_min) / cell)
    print(f"▶ IDENA {coverage}: {nc_tot}×{nr_tot} @ 2 m "
          f"E[{e_min:.1f},{e_max:.1f}] N[{n_min:.1f},{n_max:.1f}]")
    if (OUT / f"{nombre}.npz").exists():
        print("  (cacheado, omitido)")
        return
    total = np.full((nr_tot, nc_tot), np.nan, dtype=np.float32)
    paso = chunk_px * cell
    for n_a in np.arange(n_min, n_max, paso):
        for e_a in np.arange(e_min, e_max, paso):
            e_b, n_b = min(e_a + paso, e_max), min(n_a + paso, n_max)
            url = (f"{WCS_IDENA}?SERVICE=WCS&VERSION=2.0.1&REQUEST=GetCoverage"
                   f"&COVERAGEID={coverage}&SUBSET=E({e_a},{e_b})&SUBSET=N({n_a},{n_b})"
                   f"&FORMAT=image/geotiff")  # GeoServer: 'image/tiff;application=geotiff' da HTTP 500
            print(f"  chunk E[{e_a:.0f},{e_b:.0f}] N[{n_a:.0f},{n_b:.0f}] ...")
            data = http_get(url)
            try:
                img = tifffile.imread(io.BytesIO(data))
            except Exception:
                raise RuntimeError(f"IDENA no devolvió TIFF: {data[:200]}")
            img = np.asarray(img, dtype=np.float32)
            img[img > 1e30] = np.nan          # nodata GDAL 3.4e38
            img[img <= -9999] = np.nan
            img = np.flipud(img)              # TIFF fila 0 = norte → fila 0 = sur
            nc = round((e_b - e_a) / cell); nr = round((n_b - n_a) / cell)
            if img.shape != (nr, nc):
                print(f"    ⚠ shape {img.shape} != esperado ({nr},{nc}) — recorto/relleno")
                tmp = np.full((nr, nc), np.nan, np.float32)
                tmp[:min(nr, img.shape[0]), :min(nc, img.shape[1])] = \
                    img[:min(nr, img.shape[0]), :min(nc, img.shape[1])]
                img = tmp
            i0 = round((e_a - e_min) / cell)
            j0 = round((n_a - n_min) / cell)
            total[j0:j0 + nr, i0:i0 + nc] = img
    nan_pct = 100 * np.isnan(total).mean()
    print(f"  NaN (fuera de Navarra / sin dato): {nan_pct:.1f} %")
    guardar_npz(nombre, total, e_min, n_min, cell, f"IDENA WCS {coverage} (LIDAR 2024)")


# ═══════════════════════════════════════════════════════════════════════════
#  5. LIDAR PNOA local — DTM 0.5 m ampliado (clase 2 = suelo)
# ═══════════════════════════════════════════════════════════════════════════

def bbox_laz(path):
    """Lee min/max XYZ de la cabecera LAS sin cargar el archivo."""
    with open(path, "rb") as f:
        data = f.read(375)
    if data[:4] != b"LASF":
        return None
    # offsets cabecera LAS 1.x: max/min X,Y,Z como float64 desde el byte 179
    vals = struct.unpack("<6d", data[179:227])
    return vals[1], vals[3], vals[0], vals[2]  # e_min, n_min, e_max, n_max


def generar_lidar_v2():
    import laspy
    cell = 0.5
    # anillo 0 = plaza ± 1200 m Unity; +100 m margen Unity → UTM
    e_min, n_min, e_max, n_max = bbox_utm(1300.0, 1300.0, margen=120.0)
    e_min = math.floor(e_min / cell) * cell
    n_min = math.floor(n_min / cell) * cell
    nc = math.ceil((e_max - e_min) / cell)
    nr = math.ceil((n_max - n_min) / cell)
    print(f"▶ LIDAR v2: {nc}×{nr} @ 0.5 m E[{e_min:.0f},{e_max:.0f}] N[{n_min:.0f},{n_max:.0f}]")
    if (OUT / "lidar_dtm_05_v2.npz").exists():
        print("  (cacheado, omitido)")
        return
    if not LAZ_DIR.exists():
        print(f"  ⚠ {LAZ_DIR} no existe — OMITIDO (el generador usará IDENA 2 m en el anillo 0)")
        return

    suma = np.zeros((nr, nc), np.float64)
    cnt = np.zeros((nr, nc), np.int32)

    # SOLO productos PNOA_2024_* (ortométricos). Los las_cam_*_EPSG25830.laz del
    # mismo directorio llevan alturas ELIPSOIDALES (+50.3 m de geoide): mezclarlos
    # desplazaba la plaza a 557 m (detectado 2026-06-11).
    candidatos = sorted(LAZ_DIR.glob("PNOA_2024_*.laz"))
    usados = 0
    vistos = set()
    for laz in candidatos:
        base = laz.name.replace("(1)", "").replace("( 1)", "").strip()
        if base in vistos:
            continue
        bb = bbox_laz(laz)
        if bb is None or bb[0] > e_max or bb[2] < e_min or bb[1] > n_max or bb[3] < n_min:
            continue
        vistos.add(base)
        usados += 1
        print(f"  {laz.name} ...")
        with laspy.open(laz) as fh:
            for chunk in fh.chunk_iterator(2_000_000):
                m = chunk.classification == 2
                if not m.any():
                    continue
                x = np.asarray(chunk.x[m]); y = np.asarray(chunk.y[m]); z = np.asarray(chunk.z[m])
                dentro = (x >= e_min) & (x < e_min + nc * cell) & \
                         (y >= n_min) & (y < n_min + nr * cell)
                if not dentro.any():
                    continue
                ci = ((x[dentro] - e_min) / cell).astype(np.int32)
                cj = ((y[dentro] - n_min) / cell).astype(np.int32)
                np.add.at(suma, (cj, ci), z[dentro])
                np.add.at(cnt, (cj, ci), 1)
    print(f"  tiles LAZ usados: {usados}, puntos suelo: {int(cnt.sum()):,}")
    if usados == 0:
        print("  ⚠ ningún LAZ intersecta — OMITIDO")
        return

    z = np.full((nr, nc), np.nan, np.float32)
    con = cnt > 0
    z[con] = (suma[con] / cnt[con]).astype(np.float32)
    pct_void = 100 * (~con).mean()
    print(f"  celdas vacías iniciales: {pct_void:.1f} %")

    # ── Referencia IDENA 2 m (DTM hidrológicamente tratado) ─────────────────
    # 1. DESPIKE: celdas LIDAR que se desvían >2.5 m de IDENA son ruido
    #    (vegetación/estructuras clasificadas como suelo, pozos de relleno):
    #    detectado 2026-06-11 — picos de ±8..13 m que metían paredes de
    #    24-50 m/m en el casco urbano.
    # 2. RELLENO: los huecos toman el valor de IDENA (no difusión: la difusión
    #    creaba mesetas con escalón en el borde del hueco).
    ref_p = OUT / "idena_mdt2.npz"
    if not ref_p.exists():
        raise RuntimeError("idena_mdt2.npz no existe — descarga IDENA antes que el LIDAR")
    d = np.load(ref_p)
    zi, ei, ni, ci = d["z"].astype(np.float32), float(d["e0"]), float(d["n0"]), float(d["cell"])
    # remuestreo bilineal de IDENA en los centros de celda del grid LIDAR
    Ec = e_min + (np.arange(nc) + 0.5) * cell
    Nc = n_min + (np.arange(nr) + 0.5) * cell
    fx = (Ec - ei) / ci - 0.5
    fy = (Nc - ni) / ci - 0.5
    ix = np.clip(np.floor(fx).astype(np.int64), 0, zi.shape[1] - 2)
    iy = np.clip(np.floor(fy).astype(np.int64), 0, zi.shape[0] - 2)
    tx = np.clip(fx - ix, 0, 1).astype(np.float32)[None, :]
    ty = np.clip(fy - iy, 0, 1).astype(np.float32)[:, None]
    ref = ((zi[iy][:, ix] * (1 - tx) + zi[iy][:, ix + 1] * tx) * (1 - ty) +
           (zi[iy + 1][:, ix] * (1 - tx) + zi[iy + 1][:, ix + 1] * tx) * ty)

    UMBRAL_DESPIKE = 2.5
    ruido = con & ~np.isnan(ref) & (np.abs(z - ref) > UMBRAL_DESPIKE)
    print(f"  despike vs IDENA: {int(ruido.sum()):,} celdas ruidosas "
          f"({100*ruido.mean():.2f} %) reemplazadas")
    valido = con & ~ruido
    filled = np.where(valido, z, ref).astype(np.float32)

    # huecos donde tampoco IDENA tiene dato (no debería pasar dentro de Navarra)
    resto = np.isnan(filled)
    if resto.any():
        from scipy.ndimage import uniform_filter
        vals = np.where(resto, 0.0, filled).astype(np.float32)
        mask = (~resto).astype(np.float32)
        for _ in range(200):
            resto = np.isnan(filled)
            if not resto.any():
                break
            s = uniform_filter(vals, 3)
            c = uniform_filter(mask, 3)
            nuevos = resto & (c > 1e-6)
            filled[nuevos] = s[nuevos] / c[nuevos]
            vals = np.where(np.isnan(filled), 0.0, filled)
            mask = (~np.isnan(filled)).astype(np.float32)
        print(f"  NaN tras relleno IDENA+difusión: {int(np.isnan(filled).sum())}")

    guardar_npz("lidar_dtm_05_v2", filled, e_min, n_min, cell,
                f"PNOA LIDAR local clase 2, {usados} LAZ, media por celda, "
                f"despike {UMBRAL_DESPIKE} m + relleno IDENA 2 m", valid=valido)


# ═══════════════════════════════════════════════════════════════════════════

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--solo", choices=["ign", "idena", "lidar"], default=None)
    args = ap.parse_args()

    bbox_total = bbox_utm(HALF_UNITY, HALF_UNITY)          # 14.4 km + margen
    bbox_valle = bbox_utm(3800.0, 3800.0)                  # anillos 0-1 + margen
    bbox_nucleo = bbox_utm(1500.0, 1500.0, margen=100.0)   # anillo 0 + margen (DSM)

    if args.solo in (None, "ign"):
        descargar_ign("Elevacion25830_5", 5.0, "ign_mdt05", bbox_total)
        descargar_ign("Elevacion25830_25", 25.0, "ign_mdt25", bbox_total)
    if args.solo in (None, "idena"):
        descargar_idena("IDENA.WCS__ELEVAC_Ras_MDT_2M", "idena_mdt2", bbox_valle)
        descargar_idena("IDENA.WCS__ELEVAC_Ras_MDS_2M", "idena_mds2", bbox_nucleo)
    if args.solo in (None, "lidar"):
        generar_lidar_v2()

    print("\n── Comprobación rápida: cota de la plaza por fuente ──")
    for nombre in ("lidar_dtm_05_v2", "idena_mdt2", "ign_mdt05", "ign_mdt25"):
        p = OUT / f"{nombre}.npz"
        if not p.exists():
            continue
        d = np.load(p)
        z, e0, n0, cell = d["z"], float(d["e0"]), float(d["n0"]), float(d["cell"])
        i = int((E0 - e0) / cell - 0.5)
        j = int((N0 - n0) / cell - 0.5)
        if 0 <= i < z.shape[1] and 0 <= j < z.shape[0]:
            v = z[j, i]
            marca = "✅" if abs(v - COTA_PLAZA) < 1.5 else "⚠"
            print(f"  {marca} {nombre}: plaza = {v:.2f} m (esperado {COTA_PLAZA})")


if __name__ == "__main__":
    main()
