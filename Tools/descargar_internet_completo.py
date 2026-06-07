#!/usr/bin/env python3
"""
Tools/descargar_internet_completo.py
═══════════════════════════════════════════════════════════════════════════════
 DESCARGADOR DE INTERNET COMPLETO — Altsasu Manifa

 Pensado para ejecutarse EN TU PC (internet sin restricciones). Descarga assets
 CC0/libres de las mejores fuentes y los deja en Assets/_Incoming/ para que el
 auto-importador de Unity los procese.

 Fuentes:
   • Poly Haven   — HDRIs de interior, texturas PBR, modelos (CC0)
   • ambientCG    — texturas PBR de interior/fachada (CC0)
   • Khronos glTF — muebles PBR (CC-BY)

 Uso:
   python Tools/descargar_internet_completo.py                 # todo
   python Tools/descargar_internet_completo.py --solo hdri
   python Tools/descargar_internet_completo.py --calidad 2k    # 1k/2k/4k
   python Tools/descargar_internet_completo.py --lista
"""
import os, sys, json, argparse, time, zipfile, io
from pathlib import Path
from urllib.request import urlopen, Request
from urllib.error import URLError, HTTPError

RAIZ     = Path(__file__).resolve().parent.parent
INCOMING = RAIZ / "Assets/_Incoming"
UA = {"User-Agent": "AltsasuManifa/1.0 (+game asset pipeline)"}

# ── Catálogos objetivo ──────────────────────────────────────────────────────────
PH_API = "https://api.polyhaven.com"
PH_DL  = "https://dl.polyhaven.org/file/ph-assets"

# HDRIs de interior por estancia (slugs Poly Haven verificados)
PH_HDRIS_INTERIOR = {
    "bar":          "brown_photostudio_02",
    "residencial":  "lebombo",
    "comercio":     "photography_studio",
    "oficina":      "office",
    "industrial":   "artist_workshop",
    "cocina":       "kitchen",
    "almacen":      "empty_warehouse_01",
    "salon":        "lythwood_room",
}

PH_TEXTURAS = [
    # (slug, tipo)
    ("wood_floor_deck",       "suelo_madera"),
    ("wood_floor_parquet_02", "suelo_parquet"),
    ("floor_tiles_06",        "suelo_azulejo"),
    ("marble_01",             "marmol"),
    ("plaster_wall",          "pared_yeso"),
    ("white_plaster_wall",    "pared_blanca"),
    ("brick_wall_001",        "pared_ladrillo"),
    ("concrete_wall_005",     "pared_hormigon"),
    ("painted_plaster_wall",  "pared_pintada"),
    # fachadas (mejoran exteriores también)
    ("castle_brick_02_red",   "fachada_ladrillo"),
    ("rough_plaster",         "fachada_revoco"),
    ("stone_brick_wall_001",  "fachada_piedra"),
]

# ambientCG (CC0) — texturas PBR, formato ZIP directo
AMBIENTCG = "https://ambientcg.com/get?file={asset}_{res}-JPG.zip"
AMBIENTCG_ASSETS = [
    "WoodFloor043", "Tiles101", "Plaster001", "Marble016",
    "Bricks075A", "Concrete034", "Wallpaper001",
]

# Khronos glTF muebles PBR (CC-BY)
GLTF = "https://raw.githubusercontent.com/KhronosGroup/glTF-Sample-Assets/main/Models"
GLTF_MUEBLES = {
    "Silla":   "SheenChair",
    "Sofa":    "GlamVelvetSofa",
    "Lampara": "Lantern",
    "Botella": "WaterBottle",
}

# ── HTTP helpers ────────────────────────────────────────────────────────────────
def get_json(url):
    with urlopen(Request(url, headers=UA), timeout=20) as r:
        return json.loads(r.read())

def get_bytes(url, timeout=120):
    with urlopen(Request(url, headers=UA), timeout=timeout) as r:
        return r.read()

def descargar(url, destino: Path, desc=""):
    if destino.exists() and destino.stat().st_size > 1000:
        print(f"  ✓ ya existe: {destino.name}"); return True
    destino.parent.mkdir(parents=True, exist_ok=True)
    print(f"  ↓ {desc or destino.name}...", end="", flush=True)
    try:
        data = get_bytes(url)
        destino.write_bytes(data)
        print(f" {len(data)//1024}KB ✓"); return True
    except (URLError, HTTPError) as e:
        print(f" ✗ {e}"); return False

# ── Poly Haven HDRIs ─────────────────────────────────────────────────────────────
def ph_hdris(res, listar):
    print("\n═══ Poly Haven — HDRIs de interior ═══")
    dest = INCOMING / "HDRIs_Interior"
    try:
        disponibles = set(get_json(f"{PH_API}/assets?t=hdris").keys())
    except Exception as e:
        print(f"  ⚠ API no accesible: {e}"); disponibles = set()
    for arq, slug in PH_HDRIS_INTERIOR.items():
        if disponibles and slug not in disponibles:
            print(f"  ⚠ {slug} no existe, saltando ({arq})"); continue
        url = f"{PH_DL}/HDRIs/hdr/{res}/{slug}_{res}.hdr"
        if listar: print(f"  [{arq}] {url}"); continue
        descargar(url, dest / f"{arq}_{slug}.hdr", f"{arq}/{slug}")
        time.sleep(0.3)

# ── Poly Haven texturas ──────────────────────────────────────────────────────────
def ph_texturas(res, listar):
    print("\n═══ Poly Haven — texturas PBR ═══")
    for slug, tipo in PH_TEXTURAS:
        carpeta = INCOMING / ("Texturas_Interior" if "fachada" not in tipo
                              else "Texturas_Fachada") / tipo
        base = f"{PH_DL}/Textures/jpg/{res}/{slug}"
        if listar:
            print(f"  [{tipo}] {base}/{slug}_diff_{res}.jpg"); continue
        descargar(f"{base}/{slug}_diff_{res}.jpg",   carpeta / "albedo.jpg", f"{tipo}/albedo")
        descargar(f"{base}/{slug}_nor_gl_{res}.jpg", carpeta / "normal.jpg", f"{tipo}/normal")
        descargar(f"{base}/{slug}_arm_{res}.jpg",    carpeta / "arm.jpg",    f"{tipo}/arm")
        time.sleep(0.2)

# ── ambientCG texturas ───────────────────────────────────────────────────────────
def ambientcg(res, listar):
    print("\n═══ ambientCG — texturas CC0 ═══")
    dest = INCOMING / "Texturas_Interior"
    res_acg = {"1k": "1K", "2k": "2K", "4k": "4K"}.get(res.lower(), "2K")
    for asset in AMBIENTCG_ASSETS:
        url = AMBIENTCG.format(asset=asset, res=res_acg)
        if listar: print(f"  {url}"); continue
        print(f"  ↓ {asset}...", end="", flush=True)
        try:
            data = get_bytes(url)
            carpeta = dest / asset.lower()
            carpeta.mkdir(parents=True, exist_ok=True)
            with zipfile.ZipFile(io.BytesIO(data)) as z:
                z.extractall(carpeta)
            print(f" {len(data)//1024}KB ✓")
        except Exception as e:
            print(f" ✗ {e}")
        time.sleep(0.3)

# ── Khronos muebles ──────────────────────────────────────────────────────────────
def gltf_muebles(listar):
    print("\n═══ Khronos glTF — muebles PBR ═══")
    dest = INCOMING / "Muebles"
    for nombre, modelo in GLTF_MUEBLES.items():
        url = f"{GLTF}/{modelo}/glTF-Binary/{modelo}.glb"
        if listar: print(f"  {nombre} ← {url}"); continue
        descargar(url, dest / f"{nombre}.glb", nombre)
        time.sleep(0.3)

# ── Main ─────────────────────────────────────────────────────────────────────────
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--solo", default="", help="hdri,texturas,ambientcg,muebles")
    ap.add_argument("--calidad", default="2k", choices=["1k", "2k", "4k"])
    ap.add_argument("--lista", action="store_true")
    args = ap.parse_args()

    solo = set(args.solo.split(",")) if args.solo else None
    def activo(x): return solo is None or x in solo

    print(f"Calidad: {args.calidad} | Destino: Assets/_Incoming/")
    if activo("hdri"):      ph_hdris(args.calidad, args.lista)
    if activo("texturas"):  ph_texturas(args.calidad, args.lista)
    if activo("ambientcg"): ambientcg(args.calidad, args.lista)
    if activo("muebles"):   gltf_muebles(args.lista)

    if not args.lista:
        print("\n  SIGUIENTE PASO en Unity:")
        print("    Altsasu → Assets → Importar desde _Incoming")

if __name__ == "__main__":
    main()
