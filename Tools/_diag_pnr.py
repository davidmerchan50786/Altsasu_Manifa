# diagnóstico temporal: ¿clase 2 de PNR contaminada vs NAV? (borrar tras F1)
import laspy
import numpy as np

PARES = [
    (r"E:\567\PNOA_2024_NAV_568-4750_H30_NPC03.laz", r"E:\567\PNOA_2024_PNR_568-4750_NPC01.laz"),
    (r"E:\567\PNOA_2024_NAV_566-4750_H30_NPC03.laz", r"E:\567\PNOA_2024_PNR_566-4750_NPC01.laz"),
]

for nav_f, pnr_f in PARES:
    grids = []
    for f in (nav_f, pnr_f):
        with laspy.open(f) as fh:
            pts = fh.read()
        m = pts.classification == 2
        x = np.asarray(pts.x[m]); y = np.asarray(pts.y[m]); z = np.asarray(pts.z[m])
        e0, n0 = np.floor(x.min()), np.floor(y.min())
        # rejilla 10 m con MEDIANA por celda (robusta)
        ci = ((x - e0) / 10).astype(int); cj = ((y - n0) / 10).astype(int)
        nc, nr = ci.max() + 1, cj.max() + 1
        key = cj * nc + ci
        orden = np.argsort(key)
        key_s, z_s = key[orden], z[orden]
        bordes = np.searchsorted(key_s, np.arange(nr * nc))
        med = np.full(nr * nc, np.nan)
        for k in range(nr * nc):
            a, b = bordes[k], bordes[k + 1] if k + 1 < nr * nc else len(z_s)
            if b > a:
                med[k] = np.median(z_s[a:b])
        grids.append((med.reshape(nr, nc), e0, n0, len(z)))
    g1, e1, n1, np1 = grids[0]
    g2, e2, n2, np2 = grids[1]
    # alinear por offset entero de celdas
    h = min(g1.shape[0], g2.shape[0]); w = min(g1.shape[1], g2.shape[1])
    dif = g1[:h, :w] - g2[:h, :w]
    dif = dif[~np.isnan(dif)]
    print(f"{nav_f.split(chr(92))[-1]} vs PNR: nNAV={np1:,} nPNR={np2:,} "
          f"mediana={np.median(dif):.2f} p95abs={np.percentile(np.abs(dif),95):.2f} "
          f"pct>1m={100*(np.abs(dif)>1).mean():.1f}%")
