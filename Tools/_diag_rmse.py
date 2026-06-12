# diagnóstico temporal: percentiles del error mosaico vs lidar_ground.xyz (borrar tras F1)
import sys
import numpy as np
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

from ValidarMosaicoV2 import Mosaico, DATA, GIS, OX, OZ, E0, N0, SX

m = Mosaico()
pts = np.loadtxt(DATA / "lidar_ground.xyz")
xs, cotas, zs = pts[:, 0], pts[:, 1], pts[:, 2]

d = np.load(GIS / "lidar_dtm_05_v2.npz")
zv, e0, n0, cell = d["valid"], float(d["e0"]), float(d["n0"]), float(d["cell"])
E = (xs - OX) / SX + E0
N = (zs - OZ) + N0
i = ((E - e0) / cell - 0.5).round().astype(int)
j = ((N - n0) / cell - 0.5).round().astype(int)
dentro = (i >= 0) & (i < zv.shape[1]) & (j >= 0) & (j < zv.shape[0])
keep = dentro.copy()
keep[dentro] = zv[j[dentro], i[dentro]]
print(f"puntos: {len(xs):,}, en celda válida: {keep.sum():,}")

rng = np.random.default_rng(42)
sel = rng.choice(np.flatnonzero(keep), 40000, replace=False)
h = m.alturas_reales(xs[sel], zs[sel])
err = h - cotas[sel]
err = err[~np.isnan(err)]
print("err mosaico-vs-xyz (celdas válidas):")
for p in (5, 25, 50, 75, 90, 95, 99):
    print(f"  p{p}: {np.percentile(err, p):+.3f}")
print(f"  median|e|={np.median(np.abs(err)):.3f}  RMSE={np.sqrt(np.mean(err**2)):.3f}")
print(f"  pct |e|>0.5: {100*(np.abs(err)>0.5).mean():.1f}%  |e|>2: {100*(np.abs(err)>2).mean():.2f}%")
