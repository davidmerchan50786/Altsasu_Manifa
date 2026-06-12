# diagnóstico temporal: ¿qué convención usa lidar_ground.xyz? (borrar tras F1)
import numpy as np
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
GIS = ROOT / "DatosGIS"
E0, N0, OX, OZ = 567951.0, 4749902.0, 1918.0, 8570.0
SX = 76400.0 / 81548.0

print((ROOT / "Assets/AlsasuaData/lidar_dtm_meta.json").read_text()[:500])

pts = np.loadtxt(ROOT / "Assets/AlsasuaData/lidar_ground.xyz")
print("xyz:", pts.shape, "| col0", pts[:, 0].min(), pts[:, 0].max(),
      "| col1", pts[:, 1].min(), pts[:, 1].max(),
      "| col2", pts[:, 2].min(), pts[:, 2].max())

d = np.load(GIS / "lidar_dtm_05_v2.npz")
z, e0, n0, cell = d["z"], float(d["e0"]), float(d["n0"]), float(d["cell"])

rng = np.random.default_rng(7)
sel = rng.choice(len(pts), 30000, replace=False)
x_u, cota, z_u = pts[sel, 0], pts[sel, 1], pts[sel, 2]

def rmse_conv(escala_x):
    E = (x_u - OX) / escala_x + E0
    N = (z_u - OZ) + N0
    i = ((E - e0) / cell - 0.5).round().astype(int)
    j = ((N - n0) / cell - 0.5).round().astype(int)
    ok = (i >= 0) & (i < z.shape[1]) & (j >= 0) & (j < z.shape[0])
    dif = z[j[ok], i[ok]] - cota[ok]
    dif = dif[~np.isnan(dif)]
    return np.sqrt(np.mean(dif ** 2)), np.median(dif), len(dif)

for nombre, esc in (("con escala SX", SX), ("sin escala (1.0)", 1.0)):
    r, m, n = rmse_conv(esc)
    print(f"{nombre}: RMSE={r:.3f} mediana={m:.3f} n={n}")

# ¿y la col1 es quizá Unity-Y (real - Z_MIN)?
print("posible cota real si col1 fuera unityY: ", pts[:5, 1] + 511.33)
