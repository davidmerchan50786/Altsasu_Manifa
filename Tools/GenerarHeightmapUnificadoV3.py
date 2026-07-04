# Tools/GenerarHeightmapUnificadoV3.py
# ═══════════════════════════════════════════════════════════════════════════
#  MOSAICO V3 — heightmap UNIFICADO para clipmap GPU
#
#  Produce un único heightmap R16 (true-UTM isótropo, SX=1) que el clipmap GPU
#  muestrea por vertex-texture-fetch, sustituyendo los 48 Terrain por 1-2 draw
#  calls. Reutiliza el muestreador maestro H(E,N) de GenerarMosaicoTerrenoV2
#  (ya verificado contra MDT05/LIDAR), as que es correcto por construcción.
#
#  Codificación (datum DINÁMICO, sin recortes en valles bajos):
#    BASE = floor(min altitud del área) - 2
#    q    = round((H - BASE) * 64)   (uint16, cuanto 1/64 m)
#    altitudReal = BASE + q/64 ; alturaUnity = altitudReal - 511.33 (Z_MIN)
#    size.y del clipmap = 65535/64 = 1023.984375 m
#
#  Salida: Assets/AlsasuaData/terrain_clipmap_v3/
#            heightmap_unificado.r16   (RES x RES uint16 little-endian, fila 0 = sur)
#            meta.json
#
#  Uso:  python Tools/GenerarHeightmapUnificadoV3.py [half_m] [res]
#        defaults: half_m=3600 (+-3.6 km), res=4097 (~1.76 m/px)
# ═══════════════════════════════════════════════════════════════════════════
import json, sys, time
from pathlib import Path
import numpy as np

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "Tools"))
import GenerarMosaicoTerrenoV2 as G  # carga fuentes (.npz) y define H(), constantes

OUT = ROOT / "Assets" / "AlsasuaData" / "terrain_clipmap_v3"
OUT.mkdir(parents=True, exist_ok=True)

HALF = float(sys.argv[1]) if len(sys.argv) > 1 else 3600.0
RES  = int(sys.argv[2])   if len(sys.argv) > 2 else 4097

assert abs(G.SX - 1.0) < 1e-9, "GenerarMosaicoTerrenoV2.SX debe ser 1 (UTM real isotropo)."

x0, x1 = G.OX - HALF, G.OX + HALF
z0, z1 = G.OZ - HALF, G.OZ + HALF
xs = np.linspace(x0, x1, RES)
zs = np.linspace(z0, z1, RES)
E_row = (xs - G.OX) / G.SX + G.E0

t0 = time.time()

# Datum dinamico: base 2 m por debajo del minimo real (pre-pase coarse barato).
Ec = E_row[::32]
Nc = (zs[::32] - G.OZ) + G.N0
EEc, NNc = np.meshgrid(Ec, Nc)
BASE = float(np.floor(float(G.H(EEc, NNc).min())) - 2.0)
print("datum dinamico BASE =", BASE, "m")

qmin, qmax = 1 << 30, -(1 << 30)
raw = np.empty((RES, RES), np.uint16)
BLOQUE = 64
for j0 in range(0, RES, BLOQUE):
    j1 = min(j0 + BLOQUE, RES)
    Zsub = zs[j0:j1]
    EE = np.broadcast_to(E_row, (j1 - j0, RES)).copy()
    NN = np.broadcast_to((Zsub - G.OZ)[:, None] + G.N0, (j1 - j0, RES)).copy()
    H = G.H(EE, NN)
    Qg = np.round((H - BASE) * 64.0).astype(np.int64)
    qmin = min(qmin, int(Qg.min())); qmax = max(qmax, int(Qg.max()))
    np.clip(Qg, 0, 65535, out=Qg)
    raw[j0:j1, :] = Qg.astype(np.uint16)
    print("  filas %d/%d (%d%%)" % (j1, RES, 100 * j1 // RES), flush=True)

(OUT / "heightmap_unificado.r16").write_bytes(raw.astype("<u2").tobytes())
meta = dict(
    descripcion="Heightmap unificado true-UTM para clipmap GPU V3",
    crs="ETRS89/UTM30N (EPSG:25830) via Unity XZ", escalaX=G.SX,
    origenUnity=dict(OX=G.OX, OZ=G.OZ), E0=G.E0, N0=G.N0,
    halfExtent_m=HALF, lado_m=2 * HALF, res=RES,
    metrosPorPixel=round(2 * HALF / (RES - 1), 4),
    datumYBase=BASE, cuantoVertical=G.CUANTO, altoGlobal=G.ALTO_GLOBAL,
    Z_MIN=G.Z_MIN,
    formula="altitudReal = datumYBase + q/64 ; alturaUnity = altitudReal - Z_MIN",
    hMinReal=round(qmin / 64.0 + BASE, 3), hMaxReal=round(qmax / 64.0 + BASE, 3),
    recortes_pct=round(100.0 * float((raw == 0).mean()), 3),
    ordenFilas="fila 0 = sur",
)
(OUT / "meta.json").write_text(json.dumps(meta, indent=1))
print("\nOK heightmap_unificado.r16  %dx%d  (%.0f m, %.4f m/px)  en %.0fs"
      % (RES, RES, 2 * HALF, meta["metrosPorPixel"], time.time() - t0))
print("   altitud", meta["hMinReal"], meta["hMaxReal"], "m | recortes", meta["recortes_pct"], "%")
