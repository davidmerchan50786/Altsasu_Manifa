# Tools/GenerarOrtofotoDrape.py
# ═══════════════════════════════════════════════════════════════════════════
#  GENERA EL "DRAPE" DE ORTOFOTO LEJANA
#  Combina las 72 teselas PNOA (con su bbox UTM/Unity exacto del meta) en UNA
#  sola textura, lista para drapearse sobre el relieve a distancia (1 draw call).
#
#  · Fuente fiable = las 72 teselas + orto_tiles_meta.json (ux/uz exactos).
#    (ortofoto_unity.png es un strip 1000x3600 no fiable; REAL.png es cuadrado
#     y no casa con el bbox 2750x2672 → no se usan.)
#  · Orientación: norte arriba (fila 0 = uz_max), igual que asume AplicadorOrtofoto.
#  · Salida: Assets/AlsasuaData/ortofoto_drape.png (~0.7 m/px), la lee en runtime
#    DrapeOrtofotoLejana vía File.ReadAllBytes + Texture2D.LoadImage.
#
#  Uso:  python Tools/GenerarOrtofotoDrape.py [ancho_px]   (def. 4096)
# ═══════════════════════════════════════════════════════════════════════════
import json, os, sys
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
META = os.path.join(ROOT, "Assets/AlsasuaData/orto_tiles_meta.json")
TILES = os.path.join(ROOT, "Assets/AlsasuaData/tiles/orto")
OUT  = os.path.join(ROOT, "Assets/AlsasuaData/ortofoto_drape.png")
W = int(sys.argv[1]) if len(sys.argv) > 1 else 4096

def main():
    metas = json.load(open(META, encoding="utf-8"))
    xs = [t["ux_min"] for t in metas] + [t["ux_max"] for t in metas]
    zs = [t["uz_min"] for t in metas] + [t["uz_max"] for t in metas]
    X0, X1, Z0, Z1 = min(xs), max(xs), min(zs), max(zs)
    Xw, Zh = X1 - X0, Z1 - Z0
    H = round(W * Zh / Xw)
    print(f"bbox X[{X0:.1f},{X1:.1f}] Z[{Z0:.1f},{Z1:.1f}]  ->  {W}x{H}px  ({Xw/W:.2f} m/px)")

    canvas = Image.new("RGB", (W, H), (90, 96, 80))  # verde-tierra de fondo (huecos)
    ok = 0
    for m in metas:
        p = os.path.join(TILES, m["file"])
        if not os.path.isfile(p):
            continue
        # rect destino (norte arriba: fila 0 = Z1)
        x0 = round((m["ux_min"] - X0) / Xw * W)
        x1 = round((m["ux_max"] - X0) / Xw * W)
        y0 = round((Z1 - m["uz_max"]) / Zh * H)   # borde norte (arriba)
        y1 = round((Z1 - m["uz_min"]) / Zh * H)   # borde sur (abajo)
        dw, dh = max(1, x1 - x0), max(1, y1 - y0)
        tile = Image.open(p).convert("RGB").resize((dw, dh), Image.LANCZOS)
        canvas.paste(tile, (x0, y0))
        ok += 1
    canvas.save(OUT, "PNG")
    print(f"OK  {ok}/{len(metas)} teselas -> {OUT}  ({os.path.getsize(OUT)//1024} KB)")

if __name__ == "__main__":
    main()
