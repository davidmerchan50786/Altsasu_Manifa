# diagnóstico temporal de calidad de fuentes (borrar tras F1)
import numpy as np, json
from pathlib import Path

GIS = Path(__file__).resolve().parent.parent / "DatosGIS"
ROOT = GIS.parent

print("=== fuentes ===")
for n in ("lidar_dtm_05_v2", "idena_mdt2", "ign_mdt05", "ign_mdt25"):
    d = np.load(GIS / f"{n}.npz")
    z = d["z"]
    print(f"{n}: nan%={100*np.isnan(z).mean():.2f} min={np.nanmin(z):.1f} max={np.nanmax(z):.1f}")

dl = np.load(GIS / "lidar_dtm_05_v2.npz"); di = np.load(GIS / "idena_mdt2.npz")
zl, el, nl, cl = dl["z"], float(dl["e0"]), float(dl["n0"]), float(dl["cell"])
zi, ei, ni, ci = di["z"], float(di["e0"]), float(di["n0"]), float(di["cell"])
rng = np.random.default_rng(1)
jj = rng.integers(0, zl.shape[0], 200000); ii = rng.integers(0, zl.shape[1], 200000)
E = el + (ii + 0.5) * cl; N = nl + (jj + 0.5) * cl
i2 = ((E - ei) / ci - 0.5).round().astype(int); j2 = ((N - ni) / ci - 0.5).round().astype(int)
ok = (i2 >= 0) & (i2 < zi.shape[1]) & (j2 >= 0) & (j2 < zi.shape[0])
dif = zl[jj[ok], ii[ok]] - zi[j2[ok], i2[ok]]
dif = dif[~np.isnan(dif)]
print(f"lidar_v2 - idena: mediana={np.median(dif):.2f} p95abs={np.percentile(np.abs(dif),95):.2f} "
      f"max={np.abs(dif).max():.1f} pct>1m={100*(np.abs(dif)>1).mean():.1f}%")

man = json.load(open(ROOT / "Assets/AlsasuaData/terrain_tiles_v2/manifest_v2.json"))
print("=== tiles hMin<480 o hMax>1500 ===")
for t in man["tiles"]:
    if t["hMinReal"] < 480 or t["hMaxReal"] > 1500:
        print(" ", t["file"], t["hMinReal"], t["hMaxReal"])

# ¿dónde está el mínimo global?
peor = min(man["tiles"], key=lambda t: t["hMinReal"])
print("tile con hMin global:", peor["file"], peor["hMinReal"], "x,z =", peor["x"], peor["z"])
