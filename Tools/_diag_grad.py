# diagnóstico temporal: ¿dónde están los escalones y qué dicen las fuentes? (borrar tras F1)
import json
import sys
import numpy as np
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

ROOT = Path(__file__).resolve().parent.parent
GIS = ROOT / "DatosGIS"
TILES = ROOT / "Assets/AlsasuaData/terrain_tiles_v2"
E0, N0, OX, OZ, SX, Z_MIN = 567951.0, 4749902.0, 1918.0, 8570.0, 76400.0 / 81548.0, 511.33

man = json.load(open(TILES / "manifest_v2.json"))

fuentes = {}
for n in ("lidar_dtm_05_v2", "idena_mdt2", "ign_mdt05"):
    d = np.load(GIS / f"{n}.npz")
    fuentes[n] = (d["z"], float(d["e0"]), float(d["n0"]), float(d["cell"]))

def fuente_en(nombre, E, N):
    z, e0, n0, cell = fuentes[nombre]
    i = int(round((E - e0) / cell - 0.5)); j = int(round((N - n0) / cell - 0.5))
    if 0 <= i < z.shape[1] and 0 <= j < z.shape[0]:
        return z[j, i]
    return float("nan")

# gradiente máximo de cada fuente (¿cuánto acantilado REAL hay?)
for n, (z, e0, n0, cell) in fuentes.items():
    g = max(np.abs(np.diff(z, axis=0)).max(), np.abs(np.diff(z, axis=1)).max()) / cell
    print(f"gradiente max fuente {n}: {g:.1f} m/m")

for nombre_tile in ("tile_a0_z1_x0.raw", "tile_a0_z0_x0.raw", "tile_a1_z1_x1.raw", "tile_a1_z2_x1.raw"):
    t = next(x for x in man["tiles"] if x["file"] == nombre_tile)
    res = t["res"]
    Q = np.frombuffer((TILES / t["file"]).read_bytes(), "<u2").reshape(res, res).astype(np.int64) + t["y64"]
    H = Q / 64.0 + Z_MIN
    paso = t["ancho"] / (res - 1)
    gz = np.abs(np.diff(H, axis=0)) / paso
    gx = np.abs(np.diff(H, axis=1)) / paso
    g = np.maximum(gz[:, :-1], gx[:-1, :])
    print(f"\n── {nombre_tile} (paso {paso:.3f} m) ──")
    idx = np.dstack(np.unravel_index(np.argsort(g.ravel())[-3:], g.shape))[0]
    for j, i in idx[::-1]:
        x = t["x"] + i * paso; z_u = t["z"] + j * paso
        E = (x - OX) / SX + E0; N = (z_u - OZ) + N0
        print(f"  grad {g[j,i]:6.1f} m/m en Unity({x:.0f},{z_u:.0f}) tile={H[j,i]:.1f} "
              f"lidar={fuente_en('lidar_dtm_05_v2',E,N):.1f} idena={fuente_en('idena_mdt2',E,N):.1f} "
              f"mdt05={fuente_en('ign_mdt05',E,N):.1f}")
