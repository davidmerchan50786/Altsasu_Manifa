# Tools/GenerarMosaicoTerrenoV2.py
# ═══════════════════════════════════════════════════════════════════════════
#  FASE F1b — Generador del mosaico de terrains V2 (14.4×14.4 km, 48 tiles)
#
#  Anillos (malla 1200 m anclada a Herriko Plaza, Unity (1918, 8570)):
#    0: plaza±1200 m, 4 tiles 1200 m @2049 (0.586 m/px)  ← LIDAR 0.5 m
#    1: ±3600 m,     32 tiles 1200 m @1025 (1.172 m/px)  ← IDENA 2 m / MDT05
#    2: ±7200 m,     12 tiles 3600 m @1025 (3.516 m/px)  ← MDT05 / MDT25
#
#  Garantía de costuras BIT-EXACTAS:
#    · Función maestra única H(E,N) — cascada determinista de fuentes.
#    · Retícula vertical entera global: Qg = round((H−Z_MIN)·64)  (cuanto 1/64 m).
#      El redondeo ocurre UNA vez en el espacio global; cada tile almacena
#      q = Qg − y64 (uint16) con y64 entero ⇒ dos tiles que comparten vértice
#      comparten Qg ⇒ igualdad de ENTEROS, tolerancia cero.
#    · Posiciones de vértice diádicas exactas (1200/2048, 1200/1024, 3600/1024
#      son binarios exactos) ⇒ mismas coordenadas float ⇒ mismo H.
#    · T-junction fix entre anillos: los vértices finos intermedios del borde
#      se reescriben como interpolación ENTERA de los vértices gruesos.
#
#  Decodificación en Unity:
#    alturaLocal01 = q/65535  (RAW uint16 nativo)
#    size.y = 65535/64 = 1023.984375 ; posY_tile = y64/64
#    alturaMundo = posY + q/64  (datum Z_MIN=511.33)
#
#  Entradas:  DatosGIS/*.npz (de DescargarMDT_Mosaico.py)
#  Salidas:   Assets/AlsasuaData/terrain_tiles_v2/*.raw + manifest_v2.json
# ═══════════════════════════════════════════════════════════════════════════

import hashlib
import json
import math
import sys
from datetime import datetime
from pathlib import Path

import numpy as np
from scipy.ndimage import distance_transform_edt, uniform_filter

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

# ── Constantes del mundo ────────────────────────────────────────────────────
E0, N0 = 567951.0, 4749902.0
OX, OZ = 1918.0, 8570.0
SX = 1.0                        # ISOTROPO — UTM real (1 ud = 1 m en X y Z).
# Antes 76400/81548 (=0.93687): comprimia el mundo 6.3% en X. Corregido 2026-06-19
# para que terreno, edificios, carreteras y jugador conserven la misma escala real.
Z_MIN = 511.33
COTA_PLAZA = 531.94

CUANTO = 1.0 / 64.0             # m por unidad de retícula vertical
ALTO_GLOBAL = 65535 / 64.0      # 1023.984375 m — size.y de TODOS los tiles
HEADROOM_64 = 256               # 4 m bajo el mínimo del tile (excavación ríos)

ROOT = Path(__file__).resolve().parent.parent
GIS = ROOT / "DatosGIS"
OUT = ROOT / "Assets" / "AlsasuaData" / "terrain_tiles_v2"
OUT.mkdir(parents=True, exist_ok=True)

# Anillos: (id, half_extent_unity, tile_m, res)
ANILLOS = [
    (0, 1200.0, 1200.0, 2049),
    (1, 3600.0, 1200.0, 1025),
    (2, 7200.0, 3600.0, 1025),
]


# ═══════════════════════════════════════════════════════════════════════════
#  FUENTES — carga + relleno NaN + rampa de peso por distancia al borde válido
# ═══════════════════════════════════════════════════════════════════════════

class Fuente:
    def __init__(self, nombre, rampa_m):
        d = np.load(GIS / f"{nombre}.npz")
        self.nombre = nombre
        self.z = d["z"].astype(np.float64)
        self.e0, self.n0, self.cell = float(d["e0"]), float(d["n0"]), float(d["cell"])
        self.nr, self.nc = self.z.shape

        # 'valid' explícito (celdas con dato REAL, no relleno) si el npz lo trae
        valid = d["valid"].astype(bool) if "valid" in d.files else ~np.isnan(self.z)
        # Distancia (m) al dato inválido O al borde del grid (pad con inválido)
        pad = np.zeros((self.nr + 2, self.nc + 2), bool)
        pad[1:-1, 1:-1] = valid
        dist = distance_transform_edt(pad)[1:-1, 1:-1] * self.cell
        t = np.clip(dist / rampa_m, 0.0, 1.0)
        self.w = (0.5 - 0.5 * np.cos(np.pi * t))  # rampa coseno 0→1

        # Rellenar NaN por difusión para que el muestreo bicúbico nunca vea NaN
        if not valid.all():
            filled = self.z.copy()
            vals = np.where(valid, self.z, 0.0)
            mask = valid.astype(np.float64)
            for _ in range(400):
                huecos = np.isnan(filled)
                if not huecos.any():
                    break
                s = uniform_filter(vals, 5)
                c = uniform_filter(mask, 5)
                nuevos = huecos & (c > 1e-9)
                filled[nuevos] = s[nuevos] / c[nuevos]
                vals = np.where(np.isnan(filled), 0.0, filled)
                mask = (~np.isnan(filled)).astype(np.float64)
            filled[np.isnan(filled)] = np.nanmean(self.z)
            self.z = filled
        print(f"  fuente {nombre}: {self.nc}×{self.nr} @ {self.cell} m, "
              f"válido {100*valid.mean():.1f} %, rampa {rampa_m} m")

    def _idx(self, E, N):
        """Coordenadas fraccionales de celda (centros de celda)."""
        fx = (E - self.e0) / self.cell - 0.5
        fy = (N - self.n0) / self.cell - 0.5
        return fx, fy

    def muestrear(self, E, N):
        """Bicúbico Catmull-Rom vectorizado, clamp en bordes."""
        fx, fy = self._idx(E, N)
        ix = np.floor(fx).astype(np.int64)
        iy = np.floor(fy).astype(np.int64)
        tx = fx - ix
        ty = fy - iy

        def cr_w(t):
            t2, t3 = t * t, t * t * t
            return (-0.5 * t3 + t2 - 0.5 * t,
                    1.5 * t3 - 2.5 * t2 + 1.0,
                    -1.5 * t3 + 2.0 * t2 + 0.5 * t,
                    0.5 * t3 - 0.5 * t2)

        wx = cr_w(tx)
        wy = cr_w(ty)
        out = np.zeros_like(E, dtype=np.float64)
        for j in range(4):
            row = np.zeros_like(out)
            yj = np.clip(iy + j - 1, 0, self.nr - 1)
            for i in range(4):
                xi = np.clip(ix + i - 1, 0, self.nc - 1)
                row += wx[i] * self.z[yj, xi]
            out += wy[j] * row
        return out

    def peso(self, E, N):
        """Peso bilineal (la rampa es suave; bilineal basta y es monótona)."""
        fx, fy = self._idx(E, N)
        ix = np.clip(np.floor(fx).astype(np.int64), 0, self.nc - 2)
        iy = np.clip(np.floor(fy).astype(np.int64), 0, self.nr - 2)
        tx = np.clip(fx - ix, 0.0, 1.0)
        ty = np.clip(fy - iy, 0.0, 1.0)
        w = self.w
        return ((w[iy, ix] * (1 - tx) + w[iy, ix + 1] * tx) * (1 - ty) +
                (w[iy + 1, ix] * (1 - tx) + w[iy + 1, ix + 1] * tx) * ty)


print("Cargando fuentes...")
FUENTES = []  # orden de cascada: de menor a mayor prioridad
_mdt25 = Fuente("ign_mdt25", rampa_m=300.0)
_mdt05 = Fuente("ign_mdt05", rampa_m=200.0)
FUENTES.append(_mdt25)
FUENTES.append(_mdt05)
_idena = Fuente("idena_mdt2", rampa_m=120.0) if (GIS / "idena_mdt2.npz").exists() else None
if _idena: FUENTES.append(_idena)
_lidar = Fuente("lidar_dtm_05_v2", rampa_m=80.0) if (GIS / "lidar_dtm_05_v2.npz").exists() else None
if _lidar: FUENTES.append(_lidar)


def H(E, N):
    """Función maestra de cota (m s.n.m.). Determinista: misma entrada → misma salida."""
    h = FUENTES[0].muestrear(E, N)          # base: MDT25 (cubre todo)
    for f in FUENTES[1:]:
        w = f.peso(E, N)
        m = w > 0.0
        if m.any():
            h = np.where(m, h * (1.0 - w) + f.muestrear(E, N) * w, h)
    return h


# ═══════════════════════════════════════════════════════════════════════════
#  TILES — definición de la malla
# ═══════════════════════════════════════════════════════════════════════════

def definir_tiles():
    """Lista de tiles: dict(anillo, x0, z0, ancho, res, jz, ix)."""
    tiles = []
    for anillo, half, tile_m, res in ANILLOS:
        n = int(round(2 * half / tile_m))
        x_base, z_base = OX - half, OZ - half
        half_int = ANILLOS[anillo - 1][1] if anillo > 0 else 0.0
        for jz in range(n):
            for ix in range(n):
                x0 = x_base + ix * tile_m
                z0 = z_base + jz * tile_m
                # saltar el agujero interior (cubierto por el anillo anterior)
                if anillo > 0:
                    cx, cz = x0 + tile_m / 2, z0 + tile_m / 2
                    if abs(cx - OX) < half_int and abs(cz - OZ) < half_int:
                        continue
                tiles.append(dict(anillo=anillo, x0=x0, z0=z0, ancho=tile_m,
                                  res=res, jz=jz, ix=ix))
    return tiles


def vertices_unity(t):
    """Coordenadas Unity exactas (diádicas) de los vértices del tile."""
    paso = t["ancho"] / (t["res"] - 1)   # binario exacto para las 3 combinaciones
    xs = t["x0"] + np.arange(t["res"], dtype=np.float64) * paso
    zs = t["z0"] + np.arange(t["res"], dtype=np.float64) * paso
    return xs, zs


def muestrear_tile_Qg(t):
    """Retícula entera global Qg del tile (int64), fila 0 = sur."""
    xs, zs = vertices_unity(t)
    res = t["res"]
    Qg = np.empty((res, res), np.int64)
    bloque = 256  # filas por lote para limitar memoria
    E_row = (xs - OX) / SX + E0
    for j0 in range(0, res, bloque):
        j1 = min(j0 + bloque, res)
        Zsub = zs[j0:j1]
        EE = np.broadcast_to(E_row, (j1 - j0, res)).copy()
        NN = np.broadcast_to((Zsub - OZ)[:, None] + N0, (j1 - j0, res)).copy()
        h = H(EE, NN)
        Qg[j0:j1, :] = np.round((h - Z_MIN) * 64.0).astype(np.int64)
    return Qg


# ═══════════════════════════════════════════════════════════════════════════
#  T-JUNCTION FIX — bordes entre anillos de distinta resolución
# ═══════════════════════════════════════════════════════════════════════════

def lerp_entera(a, b, num, den):
    """round(a + (b-a)*num/den) con aritmética entera (half-up determinista)."""
    return (a * (den - num) + b * num + den // 2) // den


def aplicar_tjunction(tiles, idx):
    """
    Reescribe los bordes EXTERIORES del anillo fino que tocan el anillo grueso:
      anillo 0 (perímetro ±1200) ← interpola vértices del anillo 1 (ratio 2)
      anillo 1 (perímetro ±3600) ← interpola vértices del anillo 2 (ratio 3)
    Los valores gruesos se muestrean con H (idéntico a lo que almacena el
    tile grueso) ⇒ coherencia bit-exacta con ambos lados.
    """
    def Qg_en(ux, uz):
        E = (np.asarray(ux, np.float64) - OX) / SX + E0
        N = np.asarray(uz, np.float64) - OZ + N0
        return np.round((H(E, N) - Z_MIN) * 64.0).astype(np.int64)

    for fino_id, half, ratio, paso_fino in ((0, 1200.0, 2, 1200.0 / 2048),
                                            (1, 3600.0, 3, 1200.0 / 1024)):
        # 4 lados del perímetro del bloque fino
        lo_x, hi_x = OX - half, OX + half
        lo_z, hi_z = OZ - half, OZ + half
        n_fino = int(round(2 * half / paso_fino))            # vértices-1 del lado
        ks = np.arange(n_fino + 1)
        # posiciones de los vértices gruesos del lado (cada `ratio` finos)
        ks_g = ks[::ratio]

        for lado, fija, es_x in (("S", lo_z, True), ("N", hi_z, True),
                                 ("W", lo_x, False), ("E", hi_x, False)):
            if es_x:
                pos = lo_x + ks * paso_fino       # coordenada variable (x)
                pos_g = lo_x + ks_g * paso_fino
                Qg_g = Qg_en(pos_g, np.full_like(pos_g, fija))
            else:
                pos = lo_z + ks * paso_fino       # (z)
                pos_g = lo_z + ks_g * paso_fino
                Qg_g = Qg_en(np.full_like(pos_g, fija), pos_g)

            # valor objetivo de TODOS los vértices finos del lado
            m = ks // ratio
            r = ks % ratio
            a = Qg_g[m]
            b = Qg_g[np.minimum(m + 1, len(Qg_g) - 1)]
            objetivo = np.where(r == 0, a, lerp_entera(a, b, r, ratio))

            # escribir en los tiles del anillo fino cuyo borde cae en este lado
            for t in tiles:
                if t["anillo"] != fino_id:
                    continue
                xs, zs = vertices_unity(t)
                Qg = idx[id(t)]
                if es_x and abs((t["z0"] if lado == "S" else t["z0"] + t["ancho"]) - fija) < 1e-6:
                    fila = 0 if lado == "S" else t["res"] - 1
                    k0 = int(round((t["x0"] - lo_x) / paso_fino))
                    Qg[fila, :] = objetivo[k0:k0 + t["res"]]
                elif not es_x and abs((t["x0"] if lado == "W" else t["x0"] + t["ancho"]) - fija) < 1e-6:
                    col = 0 if lado == "W" else t["res"] - 1
                    k0 = int(round((t["z0"] - lo_z) / paso_fino))
                    Qg[:, col] = objetivo[k0:k0 + t["res"]]


# ═══════════════════════════════════════════════════════════════════════════
#  AUTO-VERIFICACIÓN DE COSTURAS (antes de escribir nada)
# ═══════════════════════════════════════════════════════════════════════════

def verificar_costuras(tiles, idx):
    errores = 0
    # 1. intra-anillo: aristas compartidas → igualdad entera exacta
    por_clave = {(t["anillo"], t["x0"], t["z0"]): t for t in tiles}
    for t in tiles:
        Qg = idx[id(t)]
        # vecino al este
        v = por_clave.get((t["anillo"], t["x0"] + t["ancho"], t["z0"]))
        if v is not None:
            d = np.abs(Qg[:, -1] - idx[id(v)][:, 0]).max()
            if d != 0:
                print(f"  ✗ costura E a{t['anillo']} ({t['x0']},{t['z0']}): max|Δ|={d}")
                errores += 1
        # vecino al norte
        v = por_clave.get((t["anillo"], t["x0"], t["z0"] + t["ancho"]))
        if v is not None:
            d = np.abs(Qg[-1, :] - idx[id(v)][0, :]).max()
            if d != 0:
                print(f"  ✗ costura N a{t['anillo']} ({t['x0']},{t['z0']}): max|Δ|={d}")
                errores += 1

    # 2. cross-ring: los vértices gruesos coincidentes deben ser EXACTOS
    for fino_id, grueso_id, half, ratio in ((0, 1, 1200.0, 2), (1, 2, 3600.0, 3)):
        finos = [t for t in tiles if t["anillo"] == fino_id]
        gruesos = [t for t in tiles if t["anillo"] == grueso_id]
        for tf in finos:
            Qf = idx[id(tf)]
            xs_f, zs_f = vertices_unity(tf)
            for borde, coord in (("S", tf["z0"]), ("N", tf["z0"] + tf["ancho"])):
                if abs(abs(coord - OZ) - half) > 1e-6:
                    continue
                fila = 0 if borde == "S" else tf["res"] - 1
                for tg in gruesos:
                    xs_g, zs_g = vertices_unity(tg)
                    if not (abs(coord - tg["z0"]) < 1e-6 or
                            abs(coord - tg["z0"] - tg["ancho"]) < 1e-6):
                        continue
                    fila_g = 0 if abs(coord - tg["z0"]) < 1e-6 else tg["res"] - 1
                    Qgg = idx[id(tg)]
                    comunes_x = np.intersect1d(xs_f, xs_g)
                    if len(comunes_x) == 0:
                        continue
                    if_ = np.searchsorted(xs_f, comunes_x)
                    ig_ = np.searchsorted(xs_g, comunes_x)
                    d = np.abs(Qf[fila, if_] - Qgg[fila_g, ig_]).max()
                    if d != 0:
                        print(f"  ✗ cross-ring {fino_id}↔{grueso_id} z={coord}: max|Δ|={d}")
                        errores += 1
            for borde, coord in (("W", tf["x0"]), ("E", tf["x0"] + tf["ancho"])):
                if abs(abs(coord - OX) - half) > 1e-6:
                    continue
                col = 0 if borde == "W" else tf["res"] - 1
                for tg in gruesos:
                    xs_g, zs_g = vertices_unity(tg)
                    if not (abs(coord - tg["x0"]) < 1e-6 or
                            abs(coord - tg["x0"] - tg["ancho"]) < 1e-6):
                        continue
                    col_g = 0 if abs(coord - tg["x0"]) < 1e-6 else tg["res"] - 1
                    Qgg = idx[id(tg)]
                    comunes_z = np.intersect1d(zs_f, zs_g)
                    if len(comunes_z) == 0:
                        continue
                    if_ = np.searchsorted(zs_f, comunes_z)
                    ig_ = np.searchsorted(zs_g, comunes_z)
                    d = np.abs(Qf[if_, col] - Qgg[ig_, col_g]).max()
                    if d != 0:
                        print(f"  ✗ cross-ring {fino_id}↔{grueso_id} x={coord}: max|Δ|={d}")
                        errores += 1
    return errores


# ═══════════════════════════════════════════════════════════════════════════
#  MAIN
# ═══════════════════════════════════════════════════════════════════════════

def main():
    tiles = definir_tiles()
    n_por_anillo = {a: sum(1 for t in tiles if t["anillo"] == a) for a in (0, 1, 2)}
    print(f"\nTiles: {len(tiles)} (anillo 0: {n_por_anillo[0]}, "
          f"1: {n_por_anillo[1]}, 2: {n_por_anillo[2]})")

    idx = {}
    for k, t in enumerate(tiles):
        print(f"  [{k+1:2}/{len(tiles)}] a{t['anillo']} z{t['jz']}_x{t['ix']} "
              f"({t['x0']:.0f},{t['z0']:.0f}) {t['res']}px ...")
        idx[id(t)] = muestrear_tile_Qg(t)

    print("\nT-junction fix entre anillos...")
    aplicar_tjunction(tiles, idx)

    print("Verificando costuras (gate interno)...")
    errs = verificar_costuras(tiles, idx)
    if errs:
        print(f"✗ {errs} costuras con error — NO se escriben tiles")
        sys.exit(1)
    print("  ✅ todas las costuras exactas")

    # ── Escribir RAW + manifest ───────────────────────────────────────────
    manifest_tiles = []
    for t in tiles:
        Qg = idx[id(t)]
        y64 = int(Qg.min()) - HEADROOM_64
        q = Qg - y64
        if q.max() > 65535:
            print(f"✗ tile a{t['anillo']} ({t['x0']},{t['z0']}): rango {q.max()} > uint16")
            sys.exit(1)
        raw = q.astype("<u2").tobytes()
        nombre = f"tile_a{t['anillo']}_z{t['jz']}_x{t['ix']}.raw"
        (OUT / nombre).write_bytes(raw)
        manifest_tiles.append(dict(
            file=nombre, anillo=t["anillo"],
            x=t["x0"], z=t["z0"], y64=y64, y=y64 / 64.0,
            ancho=t["ancho"], res=t["res"],
            hMinReal=round(Qg.min() / 64.0 + Z_MIN, 3),
            hMaxReal=round(Qg.max() / 64.0 + Z_MIN, 3),
            sha256=hashlib.sha256(raw).hexdigest(),
        ))

    # cotas de referencia muestreadas de H (validación posterior)
    def cota_en(ux, uz):
        E = np.array([(ux - OX) / SX + E0]); N = np.array([uz - OZ + N0])
        return round(float(H(E, N)[0]), 2)

    manifest = dict(
        version=2,
        descripcion="Mosaico 3 anillos LIDAR0.5+IDENA2m+MDT05/25, costuras enteras exactas",
        fecha=datetime.now().isoformat(timespec="seconds"),
        generadoPor="GenerarMosaicoTerrenoV2.py",
        datumYBase=Z_MIN,
        cuantoVertical=CUANTO,
        altoGlobal=ALTO_GLOBAL,
        convencionHorizontal=dict(
            E0=E0, N0=N0, OX=OX, OZ=OZ, escalaX=SX,
            formula="UnityX=(E-E0)*escalaX+OX ; UnityZ=(N-N0)+OZ"),
        ordenFilas="fila 0 = sur (estilo SetHeights)",
        anillos=[dict(id=a, halfExtent=h, tileM=tm, res=r) for a, h, tm, r in ANILLOS],
        fuentes=[f.nombre for f in FUENTES],
        cotasReferencia=dict(
            plaza=dict(x=OX, z=OZ, cota=cota_en(OX, OZ), cotaEsperada=COTA_PLAZA),
            estacion=dict(x=1650.0, z=8200.0, cota=cota_en(1650.0, 8200.0)),
        ),
        tiles=manifest_tiles,
    )
    (OUT / "manifest_v2.json").write_text(json.dumps(manifest, indent=1))

    total_mb = sum((OUT / t["file"]).stat().st_size for t in manifest_tiles) / 1e6
    print(f"\n✅ {len(manifest_tiles)} tiles escritos ({total_mb:.0f} MB) en {OUT}")
    print(f"   plaza: {manifest['cotasReferencia']['plaza']['cota']} m "
          f"(esperado {COTA_PLAZA})")


if __name__ == "__main__":
    main()
