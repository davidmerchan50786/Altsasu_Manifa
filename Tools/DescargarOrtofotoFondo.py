# Tools/DescargarOrtofotoFondo.py
# ═══════════════════════════════════════════════════════════════════════════
#  DESCARGA LA ORTOFOTO ANCHA (PNOA) DEL MUNDO JUGABLE COMPLETO (14.4 km)
#  Para drapear la foto aérea real sobre TODO el relieve del mosaico V2
#  (no solo el valle de 2.75 km). Estilo GTA: foto pegada al terreno, barato.
#
#  · Fuente: IGN PNOA máxima actualidad (WMS INSPIRE) — OI.OrthoimageCoverage,
#    EPSG:25830 (ETRS89/UTM30N). CC BY 4.0.
#  · Extensión: ±7200 m alrededor de Herriko Plaza = anillo 2 del mosaico V2.
#  · Se baja en cuadrícula NxN de 2048 px (evita el cap de tamaño del WMS) y se
#    cose con Pillow → Assets/AlsasuaData/ortofoto_fondo.jpg (def. 4096, 3.5 m/px).
#  · Sidecar ortofoto_fondo_meta.json con el bbox EN COORDS UNITY (X comprimida
#    por escalaX) → lo lee DrapeOrtofotoLejana para colocar la malla.
#
#  Uso:  python Tools/DescargarOrtofotoFondo.py [n_subtiles_por_lado]   (def. 2 -> 4096)
# ═══════════════════════════════════════════════════════════════════════════
import json, os, sys, io, time, urllib.request
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT  = os.path.join(ROOT, "Assets/AlsasuaData/ortofoto_fondo.jpg")
META = os.path.join(ROOT, "Assets/AlsasuaData/ortofoto_fondo_meta.json")

# Convención horizontal del mosaico V2 (manifest_v2.json)
E0, N0 = 567951.0, 4749902.0
OX, OZ = 1918.0, 8570.0
ESCALA_X = 1.0  # ISOTROPO UTM real (antes 0.93687; corregido 2026-06-19)
HALF = 7200.0                      # anillo 2: ±7200 m

WMS = "https://www.ign.es/wms-inspire/pnoa-ma"
LAYER = "OI.OrthoimageCoverage"
SUB_PX = 2048                      # px por sub-tesela (seguro para el WMS)

def getmap(e0, n0, e1, n1, w, h):
    q = (f"{WMS}?SERVICE=WMS&VERSION=1.3.0&REQUEST=GetMap&LAYERS={LAYER}"
         f"&CRS=EPSG:25830&BBOX={e0},{n0},{e1},{n1}&WIDTH={w}&HEIGHT={h}"
         f"&FORMAT=image/jpeg&STYLES=")
    for intento in range(4):
        try:
            with urllib.request.urlopen(q, timeout=60) as r:
                data = r.read()
            return Image.open(io.BytesIO(data)).convert("RGB")
        except Exception as ex:
            print(f"  reintento {intento+1}: {ex}")
            time.sleep(2)
    raise RuntimeError("WMS sin respuesta tras 4 intentos")

def main():
    n = int(sys.argv[1]) if len(sys.argv) > 1 else 2
    W = n * SUB_PX
    Emin, Emax = E0 - HALF, E0 + HALF
    Nmin, Nmax = N0 - HALF, N0 + HALF
    step = (2 * HALF) / n
    print(f"UTM bbox E[{Emin:.0f},{Emax:.0f}] N[{Nmin:.0f},{Nmax:.0f}]  -> {W}x{W}px ({2*HALF/W:.2f} m/px)")

    canvas = Image.new("RGB", (W, W), (70, 80, 64))
    for jy in range(n):                      # jy=0 = sur (N menor) → abajo
        for ix in range(n):
            se = Emin + ix * step
            sn = Nmin + jy * step
            sub = getmap(se, sn, se + step, sn + step, SUB_PX, SUB_PX)
            # norte arriba: fila 0 del canvas = N máx → jy alto va arriba
            px = ix * SUB_PX
            py = (n - 1 - jy) * SUB_PX
            canvas.paste(sub, (px, py))
            print(f"  ok sub ({ix},{jy})")
    canvas.save(OUT, "JPEG", quality=90)
    print(f"OK -> {OUT}  ({os.path.getsize(OUT)//1024} KB)")

    # bbox en coords Unity (X comprimida). Eje lineal por componente → corners directos.
    ux_min = (Emin - E0) * ESCALA_X + OX
    ux_max = (Emax - E0) * ESCALA_X + OX
    uz_min = (Nmin - N0) + OZ
    uz_max = (Nmax - N0) + OZ
    meta = {"ux_min": round(ux_min, 2), "uz_min": round(uz_min, 2),
            "ux_max": round(ux_max, 2), "uz_max": round(uz_max, 2),
            "fuente": "IGN PNOA-MA WMS OI.OrthoimageCoverage EPSG:25830", "px": W}
    json.dump(meta, open(META, "w"), indent=2)
    print(f"META -> {META}  {meta}")

if __name__ == "__main__":
    main()
