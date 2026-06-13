# Tools/ReproyectarEdificiosCanonico.py
# ═══════════════════════════════════════════════════════════════════════════
#  REPROYECCIÓN DE EDIFICIOS AL ESPACIO CANÓNICO — fix del "origen viejo"
#
#  Diagnóstico (2026-06-12, verificado contra la API de OSM, way 91927762):
#    · buildings_unity.json / buildings_final.json estaban en el espacio del
#      ORIGEN VIEJO: edificios renderizados ~82 m al O y ~211 m al S de su
#      posición real (la iglesia caía en (1808,8024) en vez de (1892,8236)).
#    · buildings_osm_rico.json y catastro_edificios.json SÍ están en el espacio
#      canónico (±2 m del OSM real).
#    · lidar_buildings.json extrajo los tejados DENTRO de los footprints
#      desplazados ⇒ sus atributos por id describían los edificios EQUIVOCADOS
#      (el "tejado de la iglesia" era el del edificio en UTM 567834,4749356).
#
#  Acciones:
#    1. Backup de los 3 archivos → Assets/AlsasuaData/_backup_pre_reproyeccion~/
#    2. Baseline (centroides y lidar_* viejos) → baseline_edificios_pre_reproyeccion.json
#    3. vertices de buildings_unity/buildings_final ← buildings_osm_rico (por id)
#    4. Re-extracción de tejados clase 6 en los footprints CANÓNICOS
#       (mismo algoritmo que PipelineLIDAR_Completo.procesar_edificios;
#        solo PNOA_2024_* — los las_cam_* son elipsoidales) → lidar_buildings.json
#    5. Refusión de lidar_* y height en buildings_final (regla del pipeline:
#       pts≥4 y altura>0.8) + lidar_z_min = z_base nuevo
#    6. Verificación: iglesia en (≈1892,8236), alero−suelo ∈ [0,15] m, reporte
#
#  Convención canónica (GeoDataAlsasua / mosaico V2):
#       UnityX = (E − 567951) × (76400/81548) + 1918 ;  UnityZ = (N − 4749902) + 8570
# ═══════════════════════════════════════════════════════════════════════════

import json
import shutil
import sys
from datetime import datetime
from pathlib import Path

import numpy as np
import laspy

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

ROOT = Path(__file__).resolve().parent.parent
DATA = ROOT / "Assets" / "AlsasuaData"
GIS = ROOT / "DatosGIS"
BACKUP = DATA / "_backup_pre_reproyeccion~"
LAZ_DIR = Path("E:/567")

E0, N0 = 567951.0, 4749902.0
OX, OZ = 1918.0, 8570.0
SX = 76400.0 / 81548.0

IGLESIA_ID = 91927762
IGLESIA_CANONICO = (1891.7, 8236.6)  # del way OSM real (API 2026-06-12)


def utm_to_unity(e, n):
    return (np.asarray(e) - E0) * SX + OX, (np.asarray(n) - N0) + OZ


def unity_to_utm(ux, uz):
    return (ux - OX) / SX + E0, (uz - OZ) + N0


def centroide(vertices):
    xs = [v["x"] for v in vertices]
    zs = [v["z"] for v in vertices]
    return sum(xs) / len(xs) + OX, sum(zs) / len(zs) + OZ


# ═══ 1-2. Backup + baseline ═══════════════════════════════════════════════

def backup_y_baseline(unity, final):
    BACKUP.mkdir(exist_ok=True)
    for f in ("buildings_unity.json", "buildings_final.json", "lidar_buildings.json"):
        src = DATA / f
        if src.exists() and not (BACKUP / f).exists():
            shutil.copy2(src, BACKUP / f)
            print(f"  backup: {f}")

    baseline = dict(
        fecha=datetime.now().isoformat(timespec="seconds"),
        descripcion="Estado PRE-reproyección: centroides Unity absolutos y lidar_* viejos",
        edificios={
            str(b["id"]): dict(
                centroide=[round(c, 2) for c in centroide(b["vertices"])],
                lidar_z_min=b.get("lidar_z_min"),
                lidar_altura=b.get("lidar_altura"),
                lidar_forma=b.get("lidar_forma"),
                height=b.get("height"),
            )
            for b in final if b.get("vertices")
        },
    )
    out = DATA / "baseline_edificios_pre_reproyeccion.json"
    out.write_text(json.dumps(baseline, ensure_ascii=False, indent=1))
    print(f"  baseline: {out.name} ({len(baseline['edificios'])} edificios)")


# ═══ 3. Reproyección de vértices ═══════════════════════════════════════════

def reproyectar(unity, final, rico):
    rico_por_id = {b["id"]: b for b in rico}
    deltas = []
    for coleccion, nombre in ((unity, "buildings_unity"), (final, "buildings_final")):
        n_ok = 0
        for b in coleccion:
            r = rico_por_id.get(b["id"])
            if r is None or not r.get("vertices"):
                print(f"  ⚠ id {b['id']} sin equivalente en rico — NO tocado")
                continue
            if nombre == "buildings_final" and b.get("vertices"):
                cx0, cz0 = centroide(b["vertices"])
                cx1, cz1 = centroide(r["vertices"])
                deltas.append((cx1 - cx0, cz1 - cz0))
            b["vertices"] = [dict(x=v["x"], z=v["z"]) for v in r["vertices"]]
            n_ok += 1
        print(f"  {nombre}: {n_ok}/{len(coleccion)} reproyectados")
    d = np.array(deltas)
    print(f"  desplazamiento aplicado: mediana dx={np.median(d[:,0]):+.1f} m "
          f"dz={np.median(d[:,1]):+.1f} m")


# ═══ 4. Re-extracción de tejados (= PipelineLIDAR procesar_edificios) ══════

def bbox_laz(path):
    import struct
    with open(path, "rb") as f:
        data = f.read(375)
    if data[:4] != b"LASF":
        return None
    v = struct.unpack("<6d", data[179:227])
    return v[1], v[3], v[0], v[2]  # e_min, n_min, e_max, n_max


def extraer_tejados(unity):
    fps = []
    for ed in unity:
        v = ed.get("vertices", [])
        if not v:
            continue
        xs = np.array([p["x"] + OX for p in v])
        zs = np.array([p["z"] + OZ for p in v])
        fps.append(dict(id=ed["id"],
                        xmin=xs.min() - 1.5, xmax=xs.max() + 1.5,
                        zmin=zs.min() - 1.5, zmax=zs.max() + 1.5,
                        cx=xs.mean(), cz=zs.mean()))
    # bbox global en UTM para podar tiles
    e_lo, n_lo = unity_to_utm(min(f["xmin"] for f in fps), min(f["zmin"] for f in fps))
    e_hi, n_hi = unity_to_utm(max(f["xmax"] for f in fps), max(f["zmax"] for f in fps))
    print(f"  bbox footprints UTM: E[{e_lo:.0f},{e_hi:.0f}] N[{n_lo:.0f},{n_hi:.0f}]")

    # SOLO PNOA_2024_* (ortométricos; las_cam_* son elipsoidales +50 m)
    vistos, tiles = set(), []
    for laz in sorted(LAZ_DIR.glob("PNOA_2024_*.laz")):
        base = laz.name.replace("(1)", "").strip()
        if base in vistos:
            continue
        bb = bbox_laz(laz)
        if bb is None or bb[0] > e_hi or bb[2] < e_lo or bb[1] > n_hi or bb[3] < n_lo:
            continue
        vistos.add(base)
        tiles.append(laz)
    print(f"  tiles LAZ a procesar: {len(tiles)}")

    bld_pts = {fp["id"]: [] for fp in fps}
    for path in tiles:
        print(f"    {path.name} ...", flush=True)
        las = laspy.read(str(path))
        m = np.array(las.classification, np.uint8) == 6
        if not m.any():
            continue
        x, y, z = np.array(las.x)[m], np.array(las.y)[m], np.array(las.z)[m]
        ux, uz = utm_to_unity(x, y)
        for fp in fps:
            mask = ((ux >= fp["xmin"]) & (ux <= fp["xmax"]) &
                    (uz >= fp["zmin"]) & (uz <= fp["zmax"]))
            if mask.any():
                bld_pts[fp["id"]].extend(np.stack([ux[mask], z[mask], uz[mask]], 1).tolist())

    resultados = []
    for fp in fps:
        pts = bld_pts[fp["id"]]
        if len(pts) < 4:
            continue
        arr = np.array(pts)
        z_vals = arr[:, 1]
        z_base = float(np.percentile(z_vals, 5))
        z_cumbr = float(np.percentile(z_vals, 97))
        altura = z_cumbr - z_base

        pts2d = arr[:, [0, 2]]
        centro = pts2d.mean(0)
        cov = np.cov((pts2d - centro).T)
        _, eigvecs = np.linalg.eigh(cov)
        eje = eigvecs[:, 1]

        hist, _ = np.histogram(z_vals, bins=20)
        peak_ratio = hist.max() / (hist.mean() + 0.5)
        if altura < 1.5:
            forma = "flat"
        elif peak_ratio > 3.5:
            forma = "gabled"
        elif altura > 0.5 * min(fp["xmax"] - fp["xmin"], fp["zmax"] - fp["zmin"]):
            forma = "steep_gabled"
        else:
            forma = "hipped"

        mask_techo = z_vals > (z_base + altura * 0.35)
        pts_techo = arr[mask_techo]
        if len(pts_techo) > 200:
            pts_techo = pts_techo[np.random.default_rng(fp["id"] % 2**32).choice(
                len(pts_techo), 200, replace=False)]
        puntos_rel = [dict(x=round(float(p[0] - fp["cx"]), 2),
                           y=round(float(p[1] - z_base), 2),
                           z=round(float(p[2] - fp["cz"]), 2)) for p in pts_techo]

        resultados.append(dict(
            id=fp["id"],
            lidar_z_base=round(z_base, 3), lidar_z_top=round(z_cumbr, 3),
            lidar_altura=round(altura, 3), lidar_forma=forma, lidar_pts=len(pts),
            lidar_eje_x=round(float(eje[0]), 4), lidar_eje_z=round(float(eje[1]), 4),
            puntos_tejado=puntos_rel))

    (DATA / "lidar_buildings.json").write_text(
        json.dumps(resultados, separators=(",", ":")))
    print(f"  lidar_buildings.json regenerado: {len(resultados)} edificios con nube")
    return {r["id"]: r for r in resultados}


# ═══ 5. Refusión en buildings_final ════════════════════════════════════════

def refusionar(final, lidar):
    act = 0
    for ed in final:
        ld = lidar.get(ed["id"])
        if ld is None:
            # sin nube en la posición nueva: invalidar atributos viejos (eran
            # de OTRO edificio); conservar height OSM si existía
            for k in ("lidar_altura", "lidar_forma", "lidar_pts",
                      "lidar_eje_x", "lidar_eje_z", "lidar_z_min"):
                ed.pop(k, None)
            continue
        ed["lidar_z_min"] = ld["lidar_z_base"]
        if ld["lidar_pts"] >= 4 and ld["lidar_altura"] > 0.8:
            ed["height"] = round(ld["lidar_altura"], 3)
            ed["lidar_altura"] = ld["lidar_altura"]
            ed["lidar_forma"] = ld["lidar_forma"]
            ed["lidar_pts"] = ld["lidar_pts"]
            ed["lidar_eje_x"] = ld["lidar_eje_x"]
            ed["lidar_eje_z"] = ld["lidar_eje_z"]
            ed["levels"] = max(1, round(ld["lidar_altura"] / 3.2))
            act += 1
    print(f"  refusión: {act}/{len(final)} edificios con LIDAR completo")


# ═══ 6. Verificación ═══════════════════════════════════════════════════════

def verificar(final, lidar):
    fallos = []

    # iglesia en su sitio
    igl = next(b for b in final if b["id"] == IGLESIA_ID)
    cx, cz = centroide(igl["vertices"])
    d = ((cx - IGLESIA_CANONICO[0]) ** 2 + (cz - IGLESIA_CANONICO[1]) ** 2) ** 0.5
    print(f"  iglesia: centroide ({cx:.1f},{cz:.1f}) — a {d:.1f} m del OSM real")
    if d > 10:
        fallos.append(f"iglesia a {d:.1f} m del OSM real (>10)")

    # alero − suelo plausible usando IDENA
    di = np.load(GIS / "idena_mdt2.npz")
    zi, ei, ni, ci = di["z"], float(di["e0"]), float(di["n0"]), float(di["cell"])

    def suelo(ux, uz):
        E, N = unity_to_utm(ux, uz)
        i = int(round((E - ei) / ci - 0.5))
        j = int(round((N - ni) / ci - 0.5))
        if 0 <= i < zi.shape[1] and 0 <= j < zi.shape[0]:
            return float(zi[j, i])
        return float("nan")

    aleros = []
    for b in final:
        zb = b.get("lidar_z_min")
        if zb is None or not b.get("vertices"):
            continue
        cx, cz = centroide(b["vertices"])
        s = suelo(cx, cz)
        if not np.isnan(s):
            aleros.append(zb - s)
    aleros = np.array(aleros)
    pct = 100 * ((aleros > -1.0) & (aleros < 15.0)).mean()
    print(f"  alero−suelo: mediana {np.median(aleros):+.2f} m, "
          f"{pct:.0f}% en [-1,15] m  (n={len(aleros)})")
    if pct < 90:
        fallos.append(f"solo {pct:.0f}% de aleros plausibles (<90%)")

    # comparación con baseline: el desplazamiento debe rondar (+82,+211)
    base = json.loads((DATA / "baseline_edificios_pre_reproyeccion.json").read_text())
    ds = []
    for b in final:
        e0_b = base["edificios"].get(str(b["id"]))
        if e0_b and b.get("vertices"):
            cx, cz = centroide(b["vertices"])
            ds.append((cx - e0_b["centroide"][0], cz - e0_b["centroide"][1]))
    ds = np.array(ds)
    print(f"  vs baseline: mediana dx={np.median(ds[:,0]):+.1f} dz={np.median(ds[:,1]):+.1f} "
          f"(esperado ≈ +92, +211)")

    return fallos


def main():
    unity = json.load(open(DATA / "buildings_unity.json", encoding="utf-8"))
    final = json.load(open(DATA / "buildings_final.json", encoding="utf-8"))
    rico = json.load(open(DATA / "buildings_osm_rico.json", encoding="utf-8"))

    print("1-2. Backup + baseline")
    backup_y_baseline(unity, final)
    print("3. Reproyección de vértices (rico → unity/final)")
    reproyectar(unity, final, rico)
    print("4. Re-extracción de tejados clase 6 en footprints canónicos")
    lidar = extraer_tejados(unity)
    print("5. Refusión lidar_* en buildings_final")
    refusionar(final, lidar)

    (DATA / "buildings_unity.json").write_text(
        json.dumps(unity, ensure_ascii=False, separators=(",", ":")))
    (DATA / "buildings_final.json").write_text(
        json.dumps(final, ensure_ascii=False, separators=(",", ":")))
    print("  buildings_unity.json y buildings_final.json escritos")

    print("6. Verificación")
    fallos = verificar(final, lidar)
    if fallos:
        print("✗ VERIFICACIÓN CON FALLOS:")
        for f in fallos:
            print("  -", f)
        print(f"  (restaurar desde {BACKUP})")
        sys.exit(1)
    print("✅ Reproyección completada y verificada")


if __name__ == "__main__":
    main()
