# Tools/ValidarGeorrefDatos.py
# Gate de datos georreferenciados (sin Unity). Valida que el mundo sigue en UTM
# real isótropo y los datos en su sitio. Úsalo en pre-commit/CI.
#   python Tools/ValidarGeorrefDatos.py    (exit 0 = verde)
import json, sys
from pathlib import Path
import numpy as np

DATA = Path(__file__).resolve().parent.parent / "Assets" / "AlsasuaData"
OX, OZ = 1918.0, 8570.0
IGLESIA_ABS = (1892.65, 8235.53)   # OSM/Catastro real
fallos, oks = [], []
def chk(cond, ok, ko):
    (oks if cond else fallos).append(ok if cond else ko)

def load(fn):
    b = (DATA / fn).read_bytes()
    try: return json.loads(b.decode("utf-8"))
    except UnicodeDecodeError: return json.loads(b.decode("cp1252"))

# 1. terreno V2 isótropo + cota plaza
m2 = json.load(open(DATA / "terrain_tiles_v2" / "manifest_v2.json"))
chk(abs(m2["convencionHorizontal"]["escalaX"] - 1.0) < 1e-9,
    "manifest_v2 escalaX=1 (isótropo)", f"manifest_v2 escalaX={m2['convencionHorizontal']['escalaX']} (¡compresión!)")
cp = m2["cotasReferencia"]["plaza"]["cota"]
chk(abs(cp - 531.94) < 0.1, f"V2 cota plaza {cp} m", f"V2 cota plaza {cp} m (esperado ~531.94)")

# 2. edificios: iglesia en su sitio + censo
bu = load("buildings_unity.json")
ig = next((b for b in bu if b["id"] == 91927762), None)
if ig:
    xs = [v["x"] for v in ig["vertices"]]; zs = [v["z"] for v in ig["vertices"]]
    cx = sum(xs)/len(xs)+OX; cz = sum(zs)/len(zs)+OZ
    d = ((cx-IGLESIA_ABS[0])**2 + (cz-IGLESIA_ABS[1])**2)**0.5
    chk(d <= 1.0, f"iglesia a {d:.2f} m de OSM", f"iglesia a {d:.2f} m de OSM (>1)")
else: fallos.append("iglesia 91927762 no encontrada")
chk(len(bu) > 1000, f"censo edificios {len(bu)}", f"censo edificios {len(bu)} (<=1000)")

# 3. autovía / tren / río / huertas
roads = load("roads_unity.json")
av = sum(1 for r in roads if r.get("type") in ("motorway","trunk","motorway_link","trunk_link"))
chk(av > 0, f"autovía presente ({av} tramos)", "falta autovía (0 motorway/trunk)")
rail = load("railways_unity.json")["rails"]
chk(len(rail) > 0, f"vía tren ({len(rail)} rails)", "falta vía de tren")
ww = load("waterways_unity.json")
chk(any("Arakil" in (w.get("name") or "") for w in ww), "río Arakil presente", "falta río Arakil")
gs = load("greenspaces_unity.json")
hu = sum(1 for g in gs if g.get("type") in ("orchard","allotments","farmland","vineyard"))
chk(hu > 0, f"huertas/cultivos ({hu})", "faltan huertas")

# 4. V3 heightmap: isótropo, sin recortes, plaza
v3dir = DATA / "terrain_clipmap_v3"
if (v3dir / "meta.json").exists():
    m3 = json.load(open(v3dir / "meta.json")); res = m3["res"]; base = m3["datumYBase"]
    A = np.fromfile(v3dir / "heightmap_unificado.r16", dtype="<u2").reshape(res, res)
    plaza3 = base + int(A[res//2, res//2])/64.0
    chk(abs(m3["escalaX"]-1.0) < 1e-9, "V3 escalaX=1", "V3 escalaX != 1")
    chk(m3.get("recortes_pct", 99) == 0.0, "V3 sin recortes", f"V3 recortes {m3.get('recortes_pct')}%")
    chk(abs(plaza3 - 531.94) < 0.1, f"V3 cota plaza {plaza3:.2f} m", f"V3 plaza {plaza3:.2f} (esperado ~531.94)")
    # V3 vs V2 ring0
    half3 = m3["halfExtent_m"]; mpp3 = m3["metrosPorPixel"]
    def alt3(x, z):
        fx=(x-(OX-half3))/mpp3; fz=(z-(OZ-half3))/mpp3
        fx=np.clip(fx,0,res-1.0001); fz=np.clip(fz,0,res-1.0001)
        x0=fx.astype(int); z0=fz.astype(int); x1=np.minimum(x0+1,res-1); z1=np.minimum(z0+1,res-1); tx=fx-x0; tz=fz-z0
        q=((A[z0,x0]*(1-tx)+A[z0,x1]*tx)*(1-tz)+(A[z1,x0]*(1-tx)+A[z1,x1]*tx)*tz)
        return base+q/64.0
    ZMIN=m2["datumYBase"]; errs=[]
    for t in m2["tiles"]:
        if t["anillo"]!=0: continue
        r=t["res"]; q=np.fromfile(DATA/"terrain_tiles_v2"/t["file"],dtype="<u2").reshape(r,r).astype(np.float64)
        a2=ZMIN+(t["y64"]+q)/64.0; s=16; idx=np.arange(0,r,s)
        xs2=t["x"]+idx*(t["ancho"]/(r-1)); zs2=t["z"]+idx*(t["ancho"]/(r-1))
        XX,ZZ=np.meshgrid(xs2,zs2); errs.append(np.abs(alt3(XX,ZZ)-a2[np.ix_(idx,idx)]).ravel())
    e=np.concatenate(errs); mx=float(e.max()); med=float(np.median(e))
    chk(np.percentile(e,99) < 0.5, f"V3≈V2 p99 {np.percentile(e,99):.3f} m (med {med:.3f}, máx {mx:.3f})",
        f"V3 vs V2 p99 {np.percentile(e,99):.3f} m (>0.5)")
else:
    oks.append("V3 heightmap no generado (opcional)")

# Reporte
print("══ Validación de datos georreferenciados ══")
for s in oks: print("  ✓", s)
for f in fallos: print("  ✗", f)
rep = "\n".join(["✓ "+s for s in oks] + ["✗ "+f for f in fallos])
(Path(__file__).resolve().parent.parent / "Docs" / "validacion_georref_datos.txt").write_text(
    "Validación de datos georreferenciados (Tools/ValidarGeorrefDatos.py)\n\n" + rep + "\n")
print(f"\n{'✅ VERDE' if not fallos else '❌ '+str(len(fallos))+' FALLO(S)'}  ({len(oks)} checks OK)")
sys.exit(1 if fallos else 0)
