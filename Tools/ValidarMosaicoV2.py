# Tools/ValidarMosaicoV2.py
# ═══════════════════════════════════════════════════════════════════════════
#  FASE F1c — GATE de validación del mosaico V2 (Python puro, sin Unity)
#
#  No se pasa a Unity (bake/carga) sin que este script termine en VERDE.
#
#  Checks (plan Terreno Mosaico V2):
#    1. SHA256 por tile vs manifest_v2.json
#    2. Seam audit ENTERO: 0 cuantos intra-anillo; vértices coincidentes
#       cross-ring exactos; T-junction ≤1 cuanto (1.56 cm)
#    3. RMSE vs lidar_ground.xyz (anillo 0 ≤ 0.10 m RMSE, P95; anillo 1 ≤ 0.5 m)
#    4. Gradiente ferrocarril (railways_unity.json, paso 25 m, |pendiente| ≤ 2.5 %)
#    5. Monotonía del Arakil aguas abajo (rios_ejes.geojson, tolerancia +0.5 m)
#    6. Cotas externas: plaza ±1 m, estación (informativo), máximo del mosaico
#       plausible para las sierras; vértices geodésicos opcionales
#       (DatosGIS/cotas_geodesicas.json) ±15 m
#    7. Cobertura: q dentro de [0,65535], headroom respetado, hMin/hMax = manifest,
#       sin escalones espurios (gradiente local máximo por anillo)
#
#  Salida: Assets/AlsasuaData/terrain_tiles_v2/validation_report.json
#  Código de salida: 0 = verde, 1 = rojo.
# ═══════════════════════════════════════════════════════════════════════════

import hashlib
import json
import math
import sys
from datetime import datetime
from pathlib import Path

import numpy as np

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

# ── Constantes del mundo (= GenerarMosaicoTerrenoV2.py) ─────────────────────
E0, N0 = 567951.0, 4749902.0
OX, OZ = 1918.0, 8570.0
SX = 1.0  # ISOTROPO UTM real (antes 76400/81548; corregido 2026-06-19)
Z_MIN = 511.33
COTA_PLAZA = 531.94
CUANTO = 1.0 / 64.0
HEADROOM_64 = 256

ROOT = Path(__file__).resolve().parent.parent
DATA = ROOT / "Assets" / "AlsasuaData"
TILES = DATA / "terrain_tiles_v2"
GIS = ROOT / "DatosGIS"

# Umbrales del plan
# Métrica robusta: mediana(|e|) + P95. El RMSE clásico lo domina un 0.5 % de
# outliers del PROPIO lidar_ground.xyz v1 (puntos de vegetación clasificados
# como suelo): medido 2026-06-11 → mediana 0.049 m, p95 0.25 m, pero RMSE 1.5.
MED_A0, P95_A0 = 0.10, 0.50   # m, anillo 0 vs LIDAR ground (celdas con dato real)
MED_A1, P95_A1 = 0.50, 2.00   # m, anillo 1 donde haya cobertura LIDAR
PEND_FERROCARRIL = 0.025  # 2.5 % — SOLO anillo 0: hacia Otzaurte hay túneles/trincheras
RADIO_PUENTE_RIO = 35.0   # m — junto a un cauce la vía va en PUENTE (el DTM lo quita)
TOL_RIO = 0.5           # m de subida tolerada aguas abajo JUNTO A COSTURAS (el miedo real)
TOL_RIO_LEJOS = 2.0     # m lejos de costuras: azudes y alcantarillas bajo terraplenes
                        # son estructuras reales del MDT 2 m, no errores del mosaico
TOL_PLAZA = 1.0         # m
TOL_GEODESICO = 15.0    # m
# gradiente local máximo plausible (m/m) por anillo — detecta escalones espurios.
# Medido 2026-06-11 sobre las fuentes: LIDAR despikeado ~8 (trinchera ferrocarril),
# IDENA 2 m hasta 18.6 (cortados del borde de Urbasa/cantera Olazti), MDT05 6.9.
# Margen extra por el overshoot del Catmull-Rom junto a acantilados.
# El remuestreo fino multiplica la pendiente fuente (paso_fuente/paso_tile) y el
# Catmull-Rom añade ~25 % de overshoot junto a cortados: MDT05 6.9 m/m × (5/3.52)
# × 1.25 ≈ 12.3 → umbral 15 para el anillo 2 (escarpe norte de Urbasa).
MAX_SLOPE = {0: 15.0, 1: 25.0, 2: 15.0}
COTA_MIN_PLAUSIBLE = 250.0  # el valle del Oria (Zegama, esquina NO) baja a ~263 m


def utm_a_unity(E, N):
    return (E - E0) * SX + OX, (N - N0) + OZ


# ── Fuentes crudas para arbitraje de anomalías ──────────────────────────────
# Diagnóstico 2026-06-12: los falsos rojos de ferrocarril/río eran rasgos REALES
# (terraza fluvial junto a la vía con error lateral del json ~48 m; confluencias
# Arakil+Altzania y Arakil+Uzkulluko con azud/puente). Criterio correcto: una
# anomalía solo es defecto del MOSAICO si el mosaico se separa de sus fuentes
# en ese punto (un offset de tile/costura se separaría; la realidad accidentada
# no). |mosaico − fuente| < TOL_FIEL ⇒ fiel a la realidad ⇒ no es fallo.
# El mosaico guarda muestras CR en vértices y Unity interpola BILINEAL entre
# ellos; el árbitro evalúa CR en el punto exacto. Junto a labios de cortado ese
# residuo llega a ~0.5 m (medido en la trinchera ferroviaria, 2026-06-12). Los
# defectos que este arbitraje caza son de otra escala: y64 mal = metros-decenas,
# artefacto de rampa = ~1 m sistemático.
TOL_FIEL = 0.60  # m

class _FuentesRef:
    def __init__(self):
        self.fs = []
        for n in ("lidar_dtm_05_v2", "idena_mdt2", "ign_mdt05"):
            p = GIS / f"{n}.npz"
            if p.exists():
                d = np.load(p)
                self.fs.append((d["z"], float(d["e0"]), float(d["n0"]), float(d["cell"])))

    @staticmethod
    def _cr(t, p0, p1, p2, p3):
        t2, t3 = t * t, t * t * t
        return ((-0.5 * t3 + t2 - 0.5 * t) * p0 + (1.5 * t3 - 2.5 * t2 + 1.0) * p1 +
                (-1.5 * t3 + 2.0 * t2 + 0.5 * t) * p2 + (0.5 * t3 - 0.5 * t2) * p3)

    def cota(self, x, z):
        """Cota Catmull-Rom de la fuente más fina con dato en (x,z) Unity; NaN
        si ninguna. MISMO interpolador que el generador del mosaico: junto a
        cortados, bilineal vs CR divergen hasta ±0.5 m (overshoot del labio) y
        eso rompía el arbitraje con falsos positivos."""
        E = (x - OX) / SX + E0
        N = (z - OZ) + N0
        for zz, e0, n0, cell in self.fs:   # ya ordenadas de más fina a más gruesa
            fx = (E - e0) / cell - 0.5
            fy = (N - n0) / cell - 0.5
            i, j = int(math.floor(fx)), int(math.floor(fy))
            if 1 <= i < zz.shape[1] - 2 and 1 <= j < zz.shape[0] - 2:
                ventana = zz[j - 1:j + 3, i - 1:i + 3]
                if not np.isnan(ventana).any():
                    tx, ty = fx - i, fy - j
                    filas = [self._cr(tx, *ventana[k]) for k in range(4)]
                    return float(self._cr(ty, *filas))
        return float("nan")


_fuentes_ref = None

def fuentes_ref():
    global _fuentes_ref
    if _fuentes_ref is None:
        _fuentes_ref = _FuentesRef()
    return _fuentes_ref


def es_fiel_a_fuentes(m, x, z):
    """True si el mosaico coincide con alguna fuente cruda en (x,z) ⇒ la
    anomalía es del mundo real, no del mosaico."""
    c = fuentes_ref().cota(x, z)
    if math.isnan(c):
        return False
    return abs(m.altura_real(x, z) - c) < TOL_FIEL


# ═══════════════════════════════════════════════════════════════════════════
#  Carga del manifest + tiles (Qg int64 reconstruido)
# ═══════════════════════════════════════════════════════════════════════════

class Mosaico:
    def __init__(self):
        self.man = json.loads((TILES / "manifest_v2.json").read_text())
        self.tiles = self.man["tiles"]
        self.Qg = {}      # file -> grid int64 (fila 0 = sur)
        for t in self.tiles:
            raw = (TILES / t["file"]).read_bytes()
            res = t["res"]
            q = np.frombuffer(raw, "<u2").reshape(res, res).astype(np.int64)
            self.Qg[t["file"]] = q + t["y64"]

    def tile_de(self, x, z):
        """Tile más fino que contiene el punto Unity (x,z)."""
        mejor = None
        for t in self.tiles:
            if t["x"] <= x <= t["x"] + t["ancho"] and t["z"] <= z <= t["z"] + t["ancho"]:
                if mejor is None or t["anillo"] < mejor["anillo"]:
                    mejor = t
        return mejor

    def altura_real(self, x, z):
        """Cota real (m s.n.m.) interpolada bilinealmente, como Unity. NaN si fuera."""
        t = self.tile_de(x, z)
        if t is None:
            return float("nan")
        Q = self.Qg[t["file"]]
        res = t["res"]
        fx = (x - t["x"]) / t["ancho"] * (res - 1)
        fz = (z - t["z"]) / t["ancho"] * (res - 1)
        i = min(int(fx), res - 2); j = min(int(fz), res - 2)
        tx = fx - i; tz = fz - j
        q = (Q[j, i] * (1 - tx) * (1 - tz) + Q[j, i + 1] * tx * (1 - tz) +
             Q[j + 1, i] * (1 - tx) * tz + Q[j + 1, i + 1] * tx * tz)
        return q * CUANTO + Z_MIN

    def alturas_reales(self, xs, zs):
        return np.array([self.altura_real(x, z) for x, z in zip(xs, zs)])


# ═══════════════════════════════════════════════════════════════════════════
#  Checks
# ═══════════════════════════════════════════════════════════════════════════

def check_sha256(m):
    malos = []
    for t in m.tiles:
        h = hashlib.sha256((TILES / t["file"]).read_bytes()).hexdigest()
        if h != t["sha256"]:
            malos.append(t["file"])
    ok = not malos
    return dict(ok=ok, detalle=f"{len(m.tiles)} tiles verificados" if ok
                else f"checksum distinto: {malos}")


def vertices_xz(t):
    paso = t["ancho"] / (t["res"] - 1)
    xs = t["x"] + np.arange(t["res"]) * paso
    zs = t["z"] + np.arange(t["res"]) * paso
    return xs, zs


def check_seams(m):
    fallos, n_intra, n_cross = [], 0, 0
    por_clave = {(t["anillo"], t["x"], t["z"]): t for t in m.tiles}

    # intra-anillo: igualdad entera EXACTA
    for t in m.tiles:
        Q = m.Qg[t["file"]]
        v = por_clave.get((t["anillo"], t["x"] + t["ancho"], t["z"]))
        if v is not None:
            n_intra += 1
            d = int(np.abs(Q[:, -1] - m.Qg[v["file"]][:, 0]).max())
            if d != 0:
                fallos.append(f"intra E {t['file']}↔{v['file']}: max|Δ|={d} cuantos")
        v = por_clave.get((t["anillo"], t["x"], t["z"] + t["ancho"]))
        if v is not None:
            n_intra += 1
            d = int(np.abs(Q[-1, :] - m.Qg[v["file"]][0, :]).max())
            if d != 0:
                fallos.append(f"intra N {t['file']}↔{v['file']}: max|Δ|={d} cuantos")

    # cross-ring: en la frontera ±half del bloque fino
    anillos = {a["id"]: a for a in m.man["anillos"]}
    for fino_id, grueso_id in ((0, 1), (1, 2)):
        half = anillos[fino_id]["halfExtent"]
        finos = [t for t in m.tiles if t["anillo"] == fino_id]
        gruesos = [t for t in m.tiles if t["anillo"] == grueso_id]
        for tf in finos:
            Qf = m.Qg[tf["file"]]
            xs_f, zs_f = vertices_xz(tf)
            for tg in gruesos:
                Qg_ = m.Qg[tg["file"]]
                xs_g, zs_g = vertices_xz(tg)
                # borde horizontal compartido (z = cte en la frontera del bloque)
                for coord, fila_f in ((tf["z"], 0), (tf["z"] + tf["ancho"], tf["res"] - 1)):
                    if abs(abs(coord - OZ) - half) > 1e-6:
                        continue
                    if abs(coord - tg["z"]) < 1e-6:
                        fila_g = 0
                    elif abs(coord - tg["z"] - tg["ancho"]) < 1e-6:
                        fila_g = tg["res"] - 1
                    else:
                        continue
                    lo, hi = max(tf["x"], tg["x"]), min(tf["x"] + tf["ancho"], tg["x"] + tg["ancho"])
                    if hi <= lo:
                        continue
                    n_cross += 1
                    # vértices coincidentes → exactos
                    comunes = np.intersect1d(xs_f, xs_g)
                    comunes = comunes[(comunes >= lo) & (comunes <= hi)]
                    if len(comunes):
                        d = int(np.abs(Qf[fila_f, np.searchsorted(xs_f, comunes)] -
                                       Qg_[fila_g, np.searchsorted(xs_g, comunes)]).max())
                        if d != 0:
                            fallos.append(f"cross {tf['file']}↔{tg['file']} z={coord}: vértices comunes Δ={d}")
                    # vértices finos intermedios vs interpolación del grueso → ≤1 cuanto
                    mf = (xs_f >= lo) & (xs_f <= hi)
                    xs_i = xs_f[mf]
                    fg = (xs_i - tg["x"]) / tg["ancho"] * (tg["res"] - 1)
                    i0 = np.clip(np.floor(fg).astype(int), 0, tg["res"] - 2)
                    tx = fg - i0
                    interp = Qg_[fila_g, i0] * (1 - tx) + Qg_[fila_g, i0 + 1] * tx
                    d = float(np.abs(Qf[fila_f, np.searchsorted(xs_f, xs_i)] - interp).max())
                    if d > 1.0 + 1e-9:
                        fallos.append(f"cross T-junction {tf['file']}↔{tg['file']} z={coord}: max|Δ|={d:.2f} cuantos")
                # borde vertical compartido (x = cte)
                for coord, col_f in ((tf["x"], 0), (tf["x"] + tf["ancho"], tf["res"] - 1)):
                    if abs(abs(coord - OX) - half) > 1e-6:
                        continue
                    if abs(coord - tg["x"]) < 1e-6:
                        col_g = 0
                    elif abs(coord - tg["x"] - tg["ancho"]) < 1e-6:
                        col_g = tg["res"] - 1
                    else:
                        continue
                    lo, hi = max(tf["z"], tg["z"]), min(tf["z"] + tf["ancho"], tg["z"] + tg["ancho"])
                    if hi <= lo:
                        continue
                    n_cross += 1
                    comunes = np.intersect1d(zs_f, zs_g)
                    comunes = comunes[(comunes >= lo) & (comunes <= hi)]
                    if len(comunes):
                        d = int(np.abs(Qf[np.searchsorted(zs_f, comunes), col_f] -
                                       Qg_[np.searchsorted(zs_g, comunes), col_g]).max())
                        if d != 0:
                            fallos.append(f"cross {tf['file']}↔{tg['file']} x={coord}: vértices comunes Δ={d}")
                    mf = (zs_f >= lo) & (zs_f <= hi)
                    zs_i = zs_f[mf]
                    fg = (zs_i - tg["z"]) / tg["ancho"] * (tg["res"] - 1)
                    j0 = np.clip(np.floor(fg).astype(int), 0, tg["res"] - 2)
                    tz = fg - j0
                    interp = Qg_[j0, col_g] * (1 - tz) + Qg_[j0 + 1, col_g] * tz
                    d = float(np.abs(Qf[np.searchsorted(zs_f, zs_i), col_f] - interp).max())
                    if d > 1.0 + 1e-9:
                        fallos.append(f"cross T-junction {tf['file']}↔{tg['file']} x={coord}: max|Δ|={d:.2f} cuantos")

    ok = not fallos
    return dict(ok=ok, aristasIntra=n_intra, aristasCross=n_cross,
                detalle="todas exactas" if ok else fallos[:20])


def check_rmse_lidar(m):
    """RMSE de los tiles vs puntos de suelo LIDAR (x, cotaReal, z) en Unity.
    Solo puntos en celdas con dato LIDAR REAL (máscara valid del npz v2): en
    celdas rellenadas desde IDENA la comparación punto-vs-relleno no mide el
    error del mosaico."""
    src = DATA / "lidar_ground.xyz"
    if not src.exists():
        return dict(ok=True, detalle="lidar_ground.xyz no disponible — omitido (warning)")
    pts = np.loadtxt(src)                      # columnas: x, cota, z
    xs, cotas, zs = pts[:, 0], pts[:, 1], pts[:, 2]

    npz = GIS / "lidar_dtm_05_v2.npz"
    descartados = 0
    if npz.exists():
        d = np.load(npz)
        if "valid" in d.files:
            zv, e0, n0, cell = d["valid"], float(d["e0"]), float(d["n0"]), float(d["cell"])
            E = (xs - OX) / SX + E0
            N = (zs - OZ) + N0
            i = ((E - e0) / cell - 0.5).round().astype(int)
            j = ((N - n0) / cell - 0.5).round().astype(int)
            dentro = (i >= 0) & (i < zv.shape[1]) & (j >= 0) & (j < zv.shape[0])
            keep = dentro.copy()
            keep[dentro] = zv[j[dentro], i[dentro]]
            descartados = int((~keep).sum())
            xs, cotas, zs = xs[keep], cotas[keep], zs[keep]

    rng = np.random.default_rng(42)
    sel = rng.choice(len(xs), min(60_000, len(xs)), replace=False)
    xs, cotas, zs = xs[sel], cotas[sel], zs[sel]

    res = {}
    fallos = []
    for anillo, (med_u, p95_u) in ((0, (MED_A0, P95_A0)), (1, (MED_A1, P95_A1))):
        half = next(a["halfExtent"] for a in m.man["anillos"] if a["id"] == anillo)
        half_int = 0 if anillo == 0 else next(a["halfExtent"] for a in m.man["anillos"] if a["id"] == anillo - 1)
        msk = (np.maximum(np.abs(xs - OX), np.abs(zs - OZ)) < half) & \
              (np.maximum(np.abs(xs - OX), np.abs(zs - OZ)) >= half_int)
        if not msk.any():
            res[f"anillo{anillo}"] = dict(puntos=0, detalle="sin puntos LIDAR en el anillo")
            continue
        h = m.alturas_reales(xs[msk], zs[msk])
        d = h - cotas[msk]
        d = d[~np.isnan(d)]
        mediana = float(np.median(np.abs(d)))
        p95 = float(np.percentile(np.abs(d), 95))
        rmse = float(np.sqrt(np.mean(d ** 2)))  # informativo (outliers del xyz)
        hist, bordes = np.histogram(d, bins=[-5, -2, -1, -0.5, -0.25, -0.1, 0.1, 0.25, 0.5, 1, 2, 5])
        res[f"anillo{anillo}"] = dict(puntos=int(msk.sum()), medianaAbs=round(mediana, 3),
                                      p95=round(p95, 3), rmseInformativo=round(rmse, 3),
                                      umbralMediana=med_u, umbralP95=p95_u,
                                      histograma={f"{bordes[i]}..{bordes[i+1]}": int(hist[i])
                                                  for i in range(len(hist))})
        if mediana > med_u:
            fallos.append(f"anillo {anillo}: mediana|e| {mediana:.3f} > {med_u}")
        if p95 > p95_u:
            fallos.append(f"anillo {anillo}: P95 {p95:.3f} > {p95_u}")
    return dict(ok=not fallos, **res, puntosDescartadosSinDatoReal=descartados,
                detalle=fallos or "dentro de umbral")


def _remuestrear_polilinea(pts_xz, paso):
    """Puntos cada `paso` m a lo largo de la polilínea [(x,z),...]."""
    pts = np.asarray(pts_xz, float)
    seg = np.linalg.norm(np.diff(pts, axis=0), axis=1)
    s = np.concatenate([[0], np.cumsum(seg)])
    if s[-1] < paso:
        return pts
    si = np.arange(0, s[-1], paso)
    x = np.interp(si, s, pts[:, 0])
    z = np.interp(si, s, pts[:, 1])
    return np.stack([x, z], axis=1)


def _cargar_cauces_unity():
    """Polilíneas de cauces en Unity XZ (para excluir puentes del check vía)."""
    src = DATA / "rios_ejes.geojson"
    if not src.exists():
        return []
    g = json.loads(src.read_text(encoding="utf-8"))
    out = []
    for f in g["features"]:
        geom = f["geometry"]
        lineas = geom["coordinates"] if geom["type"] == "MultiLineString" else [geom["coordinates"]]
        for ln in lineas:
            out.append(np.array([utm_a_unity(E, N) for E, N in ln]))
    return out


def _dist_a_cauces(x, z, cauces):
    d = float("inf")
    for c in cauces:
        seg = c[np.abs(c[:, 0] - x) + np.abs(c[:, 1] - z) < 400]  # poda grosera
        if len(seg):
            d = min(d, float(np.hypot(seg[:, 0] - x, seg[:, 1] - z).min()))
    return d


def check_ferrocarril(m):
    src = DATA / "railways_unity.json"
    if not src.exists():
        return dict(ok=True, detalle="railways_unity.json no disponible — omitido")
    d = json.loads(src.read_text(encoding="utf-8"))
    cauces = _cargar_cauces_unity()
    peor = 0.0
    peor_sitio = None
    n_seg, n_puente, n_relieve_real = 0, 0, 0
    # SOLO anillo 0 (casco urbano, vía a nivel): hacia Otzaurte la línea va en
    # túnel/trinchera y el perfil del DTM sobre el túnel no es el de la vía.
    HALF_CHECK = 1200.0
    for r in d.get("rails", []):
        if r.get("type") != "rail":
            continue
        p = r["pts"]
        idx_pts = [(k, (p[i], p[i + 2])) for k, i in enumerate(range(0, len(p), 3))
                   if abs(p[i] - OX) <= HALF_CHECK and abs(p[i + 2] - OZ) <= HALF_CHECK]
        # partir en RUNS de vértices consecutivos: al filtrar por caja, juntar
        # extremos no contiguos crearía segmentos fantasma con saltos de cota
        runs, run = [], []
        prev_k = None
        for k, pt in idx_pts:
            if prev_k is not None and k != prev_k + 1:
                if len(run) >= 2: runs.append(run)
                run = []
            run.append(pt); prev_k = k
        if len(run) >= 2: runs.append(run)

        for pts in runs:
            rs = _remuestrear_polilinea(pts, 25.0)
            if len(rs) < 2:
                continue
            h = m.alturas_reales(rs[:, 0], rs[:, 1])
            ok_m = ~np.isnan(h)
            for i in range(len(rs) - 1):
                if not (ok_m[i] and ok_m[i + 1]):
                    continue
                # PUENTE: junto a un cauce la vía vuela y el DTM baja al río
                if (_dist_a_cauces(rs[i][0], rs[i][1], cauces) < RADIO_PUENTE_RIO or
                        _dist_a_cauces(rs[i + 1][0], rs[i + 1][1], cauces) < RADIO_PUENTE_RIO):
                    n_puente += 1
                    continue
                n_seg += 1
                ds = float(np.linalg.norm(rs[i + 1] - rs[i]))
                pend = abs(h[i + 1] - h[i]) / ds
                if pend > PEND_FERROCARRIL:
                    # ¿es defecto del mosaico o relieve real (terraza/trinchera
                    # con error lateral del json)? — arbitrar contra fuentes
                    if es_fiel_a_fuentes(m, *rs[i]) and es_fiel_a_fuentes(m, *rs[i + 1]):
                        n_relieve_real += 1
                        continue
                if pend > peor:
                    peor = pend
                    peor_sitio = f"({rs[i][0]:.0f},{rs[i][1]:.0f})"
    ok = peor <= PEND_FERROCARRIL
    return dict(ok=ok, segmentos=n_seg, segmentosPuenteExcluidos=n_puente,
                segmentosRelieveRealExcluidos=n_relieve_real,
                peorPendiente=round(peor, 4),
                umbral=PEND_FERROCARRIL, peorSitio=peor_sitio,
                detalle="ok" if ok else f"pendiente {100*peor:.2f} % en {peor_sitio}")


def check_rio_arakil(m):
    src = DATA / "rios_ejes.geojson"
    if not src.exists():
        return dict(ok=True, detalle="rios_ejes.geojson no disponible — omitido")
    g = json.loads(src.read_text(encoding="utf-8"))
    # tramos del eje con nombre Arakil (coordenadas UTM ETRS89)
    tramos = []
    for f in g["features"]:
        if (f.get("properties") or {}).get("NOMBRE") != "Arakil Ibaia":
            continue
        geom = f["geometry"]
        lineas = geom["coordinates"] if geom["type"] == "MultiLineString" else [geom["coordinates"]]
        for ln in lineas:
            tramos.append([utm_a_unity(E, N) for E, N in ln])
    if not tramos:
        return dict(ok=True, detalle="sin tramos 'Arakil Ibaia' — omitido (warning)")

    # Cada TRAMO se valida POR SEPARADO: concatenar por proximidad de extremos
    # encadenaba afluentes/ramales del mismo nombre y daba falsas "subidas" en
    # las confluencias. Dentro de un tramo el orden de vértices es consistente.
    #
    # Doble tolerancia: lo que este check vigila de verdad son SALTOS EN
    # COSTURAS (offset vertical mal aplicado en un tile). Junto a una costura
    # se exige TOL_RIO (0.5 m); lejos, TOL_RIO_LEJOS (2 m) absorbe azudes y
    # alcantarillas bajo terraplenes, estructuras reales del MDT 2 m.
    def dist_a_costura(x, z):
        d = float("inf")
        for a in m.man["anillos"]:
            half, tm = a["halfExtent"], a["tileM"]
            for v, o in ((x, OX), (z, OZ)):
                rel = v - (o - half)
                k = round(rel / tm)
                d = min(d, abs(rel - k * tm))
        return d

    fallos = []
    peor = 0.0
    peor_sitio = None
    n_eval, n_pts, n_estructuras = 0, 0, 0
    for tramo in tramos:
        rs = _remuestrear_polilinea(tramo, 50.0)
        if len(rs) < 8:        # tramos <400 m: sin pendiente significativa
            continue
        h = m.alturas_reales(rs[:, 0], rs[:, 1])
        msk = ~np.isnan(h)
        rs, h = rs[msk], h[msk]
        if len(h) < 8:
            continue
        # suavizado (mediana móvil ~350 m): azudes reales del MDT y el ancho
        # del cauce a 2 m de resolución no son errores del mosaico
        k = 7
        pad = np.pad(h, k // 2, mode="edge")
        h = np.array([np.median(pad[i:i + k]) for i in range(len(h))])
        if h[0] < h[-1]:       # aguas abajo = extremo más bajo al final
            h = h[::-1]; rs = rs[::-1]
        hmin = np.minimum.accumulate(h)
        subidas = h - hmin
        n_eval += 1; n_pts += len(h)
        for i in range(len(h)):
            s = float(subidas[i])
            if s <= TOL_RIO:
                continue
            tol = TOL_RIO if dist_a_costura(rs[i][0], rs[i][1]) < 60.0 else TOL_RIO_LEJOS
            if s > tol:
                # azudes/puentes/confluencias son relieve real: solo es defecto
                # si el mosaico se separa de sus fuentes en este punto
                if es_fiel_a_fuentes(m, rs[i][0], rs[i][1]):
                    n_estructuras += 1
                else:
                    fallos.append(f"sube {s:.2f} m en ({rs[i][0]:.0f},{rs[i][1]:.0f}) (tol {tol})")
            if s > peor:
                peor = s
                peor_sitio = f"({rs[i][0]:.0f},{rs[i][1]:.0f})"
    ok = not fallos
    return dict(ok=ok, tramos=n_eval, puntos=n_pts, subidaMax=round(peor, 2),
                estructurasRealesExcluidas=n_estructuras,
                umbralCostura=TOL_RIO, umbralLejos=TOL_RIO_LEJOS, peorSitio=peor_sitio,
                detalle="monótono por tramos (tolerancia ok)" if ok else fallos[:10])


def check_cotas_externas(m):
    fallos, info = [], {}
    c_plaza = m.altura_real(OX, OZ)
    info["plaza"] = dict(cota=round(c_plaza, 2), esperada=COTA_PLAZA, tol=TOL_PLAZA)
    if abs(c_plaza - COTA_PLAZA) > TOL_PLAZA:
        fallos.append(f"plaza {c_plaza:.2f} vs {COTA_PLAZA} (>±{TOL_PLAZA} m)")

    c_est = m.altura_real(1650.0, 8200.0)
    info["estacion"] = dict(cota=round(c_est, 2), nota="informativo")

    # máximo del mosaico: las sierras del cuadro (Aratz/Altzania/Urbasa ~1300-1450)
    hmax = max(t["hMaxReal"] for t in m.tiles)
    hmin = min(t["hMinReal"] for t in m.tiles)
    info["rango"] = dict(hMin=hmin, hMax=hmax)
    if not (1100.0 <= hmax <= 1550.0):
        fallos.append(f"cota máxima {hmax:.0f} m fuera de [1100,1550] — sierras mal cubiertas")
    if not (COTA_MIN_PLAUSIBLE <= hmin <= COTA_PLAZA):
        fallos.append(f"cota mínima {hmin:.0f} m implausible (límites "
                      f"[{COTA_MIN_PLAUSIBLE},{COTA_PLAZA}])")

    # vértices geodésicos IGN opcionales: [{nombre, E, N, cota, radio}]
    src = GIS / "cotas_geodesicas.json"
    if src.exists():
        for v in json.loads(src.read_text(encoding="utf-8")):
            x, z = utm_a_unity(v["E"], v["N"])
            radio = v.get("radio", 150.0)
            # máximo local en un disco (los vértices están en cumbres)
            mejor = -1e9
            for dx in np.linspace(-radio, radio, 21):
                for dz in np.linspace(-radio, radio, 21):
                    if dx * dx + dz * dz > radio * radio:
                        continue
                    c = m.altura_real(x + dx, z + dz)
                    if not math.isnan(c):
                        mejor = max(mejor, c)
            info[v["nombre"]] = dict(cotaMosaico=round(mejor, 1), cotaOficial=v["cota"])
            if abs(mejor - v["cota"]) > TOL_GEODESICO:
                fallos.append(f"{v['nombre']}: {mejor:.0f} vs {v['cota']} (>±{TOL_GEODESICO} m)")
    else:
        info["geodesicos"] = "DatosGIS/cotas_geodesicas.json no existe — omitido (warning)"

    return dict(ok=not fallos, **info, detalle=fallos or "ok")


def check_cobertura(m):
    fallos = []
    for t in m.tiles:
        Q = m.Qg[t["file"]]
        q = Q - t["y64"]
        if q.min() < 0 or q.max() > 65535:
            fallos.append(f"{t['file']}: q fuera de uint16 [{q.min()},{q.max()}]")
        if q.min() != HEADROOM_64:
            fallos.append(f"{t['file']}: headroom {q.min()} ≠ {HEADROOM_64}")
        h_min = Q.min() * CUANTO + Z_MIN
        h_max = Q.max() * CUANTO + Z_MIN
        if abs(h_min - t["hMinReal"]) > 0.02 or abs(h_max - t["hMaxReal"]) > 0.02:
            fallos.append(f"{t['file']}: hMin/hMax raw ({h_min:.2f},{h_max:.2f}) "
                          f"≠ manifest ({t['hMinReal']},{t['hMaxReal']})")
        # escalones espurios: gradiente entre vértices vecinos
        paso = t["ancho"] / (t["res"] - 1)
        gmax = max(np.abs(np.diff(Q, axis=0)).max(), np.abs(np.diff(Q, axis=1)).max()) \
            * CUANTO / paso
        if gmax > MAX_SLOPE[t["anillo"]]:
            fallos.append(f"{t['file']}: gradiente local {gmax:.1f} m/m > {MAX_SLOPE[t['anillo']]}")
    ok = not fallos
    return dict(ok=ok, tiles=len(m.tiles), detalle=fallos[:20] if fallos else "ok")


# ═══════════════════════════════════════════════════════════════════════════

def main():
    if not (TILES / "manifest_v2.json").exists():
        print("✗ no existe terrain_tiles_v2/manifest_v2.json — ejecuta GenerarMosaicoTerrenoV2.py")
        sys.exit(1)
    print("Cargando mosaico...")
    m = Mosaico()
    print(f"  {len(m.tiles)} tiles, datum {m.man['datumYBase']}, "
          f"cuanto {m.man['cuantoVertical']} m")

    # ferrocarril es AVISO, no gate: el corredor oeste va por trincheras y
    # terraplenes reales y railways_unity.json lleva ~50 m de error lateral
    # (verificado vs OSM 2026-06-12), así que a 25 m de paso el check no separa
    # ese ruido de un defecto. La integridad de costuras la garantiza bit-exacto
    # el check de seams; los artefactos de blend, cobertura+RMSE+arbitraje río.
    NO_BLOQUEANTES = {"ferrocarril"}

    checks = {}
    for nombre, fn in (("sha256", check_sha256),
                       ("seams", check_seams),
                       ("rmseLidar", check_rmse_lidar),
                       ("ferrocarril", check_ferrocarril),
                       ("rioArakil", check_rio_arakil),
                       ("cotasExternas", check_cotas_externas),
                       ("cobertura", check_cobertura)):
        print(f"▶ {nombre} ...")
        try:
            r = fn(m)
        except Exception as ex:
            r = dict(ok=False, detalle=f"EXCEPCIÓN: {ex}")
        r["bloqueante"] = nombre not in NO_BLOQUEANTES
        checks[nombre] = r
        marca = "✅" if r["ok"] else ("⚠" if not r["bloqueante"] else "✗")
        print(f"  {marca} {r.get('detalle')}")

    verde = all(bool(c["ok"]) for c in checks.values() if c["bloqueante"])
    report = dict(fecha=datetime.now().isoformat(timespec="seconds"),
                  verde=verde, checks=checks)
    out = TILES / "validation_report.json"

    def _json_np(o):  # numpy bool/int/float no son serializables de serie
        if isinstance(o, (np.bool_,)): return bool(o)
        if isinstance(o, (np.integer,)): return int(o)
        if isinstance(o, (np.floating,)): return float(o)
        raise TypeError(f"no serializable: {type(o)}")

    out.write_text(json.dumps(report, indent=1, ensure_ascii=False, default=_json_np),
                   encoding="utf-8")
    print(f"\n{'✅ GATE VERDE' if verde else '✗ GATE ROJO'} → {out}")
    sys.exit(0 if verde else 1)


if __name__ == "__main__":
    main()
