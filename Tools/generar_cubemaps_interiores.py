#!/usr/bin/env python3
"""
Tools/generar_cubemaps_interiores.py
Genera cubemaps fotorrealistas de 6 caras para cada arquetipo de interior
usando las texturas PBR reales ya descargadas + compositing con Pillow.
Salida: Assets/AlsasuaData/Interiores/Cubemaps/<arquetipo>/<cara>.png
"""
import os, sys, math, random
from pathlib import Path
from PIL import Image, ImageFilter, ImageEnhance, ImageDraw

RAIZ    = Path(__file__).resolve().parent.parent
TEX_DIR = RAIZ / "Assets/Textures_AAA/Interiores"
OUT_DIR = RAIZ / "Assets/AlsasuaData/Interiores/Cubemaps"
RES     = 512  # px por cara (512 = buen equilibrio calidad/tamaño)

# ── Paletas y config por arquetipo ─────────────────────────────────────────────
ARQUETIPOS = {
    "Bar": {
        "suelo":   "suelo_madera",
        "pared":   "pared_ladrillo",
        "techo":   None,
        "color_techo": (30, 20, 12),
        "color_ambiente": (180, 100, 40),   # luz cálida ámbar
        "brillo": 0.7,
        "muebles": [
            ("barra",   (0.05, 0.12, 0.85, 0.22), (90, 55, 30)),
            ("estante", (0.0,  0.55, 1.0,  0.65), (60, 35, 15)),
            ("botella", (0.08, 0.65, 0.12, 0.95), (40, 25, 10)),
            ("botella", (0.18, 0.65, 0.22, 0.95), (50, 30, 10)),
            ("botella", (0.28, 0.65, 0.32, 0.95), (35, 20, 8)),
            ("taburete",(0.30, 0.0,  0.38, 0.12), (20, 12, 5)),
            ("taburete",(0.50, 0.0,  0.58, 0.12), (20, 12, 5)),
            ("taburete",(0.70, 0.0,  0.78, 0.12), (20, 12, 5)),
            ("lampara", (0.80, 0.82, 0.92, 1.0),  (255, 200, 100)),
        ],
    },
    "Residencial": {
        "suelo":   "suelo_madera",
        "pared":   "pared_blanca",
        "techo":   None,
        "color_techo": (240, 235, 220),
        "color_ambiente": (220, 180, 120),
        "brillo": 0.9,
        "muebles": [
            ("sofa",    (0.08, 0.05, 0.55, 0.35), (160, 130, 100)),
            ("sofa_res",(0.08, 0.33, 0.55, 0.44), (140, 110, 85)),
            ("mesa",    (0.12, 0.0,  0.45, 0.06), (100, 70, 30)),
            ("cuadro",  (0.68, 0.55, 0.88, 0.82), (80, 100, 140)),
            ("lampara", (0.82, 0.68, 0.94, 1.0),  (255, 220, 140)),
            ("pie_lamp",(0.86, 0.0,  0.90, 0.7),  (120, 100, 80)),
        ],
    },
    "Comercio": {
        "suelo":   "suelo_azulejo",
        "pared":   "pared_blanca",
        "techo":   None,
        "color_techo": (250, 250, 255),
        "color_ambiente": (200, 210, 230),
        "brillo": 1.1,
        "muebles": [
            ("estante", (0.0,  0.15, 0.22, 0.90), (100, 100, 110)),
            ("estante", (0.25, 0.15, 0.47, 0.90), (100, 100, 110)),
            ("estante", (0.50, 0.15, 0.72, 0.90), (100, 100, 110)),
            ("producto",(0.02, 0.70, 0.10, 0.88), (220, 60,  60)),
            ("producto",(0.12, 0.70, 0.20, 0.88), (60,  120, 220)),
            ("producto",(0.22, 0.70, 0.30, 0.88), (60,  200, 80)),
            ("producto",(0.02, 0.40, 0.10, 0.58), (240, 180, 40)),
            ("producto",(0.12, 0.40, 0.20, 0.58), (180, 60,  200)),
            ("mostrador",(0.20, 0.0, 0.80, 0.15), (80, 80, 90)),
        ],
    },
    "Oficina": {
        "suelo":   "suelo_madera",
        "pared":   "pared_blanca",
        "techo":   None,
        "color_techo": (245, 248, 252),
        "color_ambiente": (190, 205, 225),
        "brillo": 1.0,
        "muebles": [
            ("mesa_of", (0.10, 0.08, 0.75, 0.22), (70, 80, 90)),
            ("monitor", (0.30, 0.20, 0.55, 0.50), (15, 20, 25)),
            ("monitor2",(0.58, 0.20, 0.75, 0.44), (15, 20, 25)),
            ("silla_of",(0.35, 0.0,  0.50, 0.10), (30, 35, 40)),
            ("pizarra", (0.05, 0.55, 0.50, 0.90), (240, 242, 245)),
            ("lampara", (0.85, 0.82, 0.95, 1.0),  (220, 230, 255)),
        ],
    },
    "Industrial": {
        "suelo":   "pared_hormigon",
        "pared":   "pared_ladrillo",
        "techo":   None,
        "color_techo": (40, 40, 42),
        "color_ambiente": (150, 160, 180),
        "brillo": 0.5,
        "muebles": [
            ("estante_ind",(0.0,  0.08, 0.18, 0.92), (50, 55, 60)),
            ("estante_ind",(0.20, 0.08, 0.38, 0.92), (50, 55, 60)),
            ("estante_ind",(0.62, 0.08, 0.80, 0.92), (50, 55, 60)),
            ("caja_ind",  (0.22, 0.0,  0.38, 0.08), (80, 70, 40)),
            ("caja_ind",  (0.40, 0.0,  0.56, 0.08), (70, 60, 35)),
            ("tubo",      (0.0,  0.88, 1.0,  0.92), (100, 110, 120)),
        ],
    },
    "Farmacia": {
        "suelo":   "suelo_azulejo",
        "pared":   "pared_blanca",
        "techo":   None,
        "color_techo": (255, 255, 255),
        "color_ambiente": (210, 225, 240),
        "brillo": 1.2,
        "muebles": [
            ("estante", (0.0, 0.15, 0.30, 0.90), (220, 225, 230)),
            ("estante", (0.35,0.15, 0.65, 0.90), (220, 225, 230)),
            ("mostrador",(0.15,0.0, 0.85, 0.18), (200, 205, 215)),
            ("cruz",    (0.44,0.70, 0.56, 0.90), (50, 200, 100)),
        ],
    },
    "Supermercado": {
        "suelo":   "suelo_azulejo",
        "pared":   "pared_blanca",
        "techo":   None,
        "color_techo": (255, 255, 255),
        "color_ambiente": (200, 210, 220),
        "brillo": 1.1,
        "muebles": [
            ("pasillo",  (0.0,  0.10, 0.30, 0.88), (90, 90, 100)),
            ("pasillo",  (0.35, 0.10, 0.65, 0.88), (90, 90, 100)),
            ("producto", (0.02, 0.70, 0.10, 0.85), (230, 50, 50)),
            ("producto", (0.12, 0.70, 0.20, 0.85), (50, 180, 80)),
            ("producto", (0.37, 0.70, 0.45, 0.85), (255, 200, 0)),
            ("caja_super",(0.70,0.0,  0.98, 0.20),(60, 65, 70)),
        ],
    },
    "Garaje": {
        "suelo":   "pared_hormigon",
        "pared":   "pared_hormigon",
        "techo":   None,
        "color_techo": (35, 35, 37),
        "color_ambiente": (100, 110, 130),
        "brillo": 0.35,
        "muebles": [
            ("coche",   (0.05, 0.0, 0.55, 0.30), (40, 50, 140)),
            ("pillar",  (0.60, 0.0, 0.68, 1.0),  (80, 80, 82)),
            ("pillar",  (0.0,  0.0, 0.08, 1.0),  (80, 80, 82)),
        ],
    },
}

# ── Cargar textura desde disco ──────────────────────────────────────────────────
_cache_tex: dict = {}
def cargar_tex(nombre: str, res: int) -> Image.Image:
    if nombre in _cache_tex: return _cache_tex[nombre]
    carpeta = TEX_DIR / nombre
    for ext in ("albedo.jpg", "albedo.png"):
        p = carpeta / ext
        if p.exists():
            img = Image.open(p).convert("RGB").resize((res, res), Image.LANCZOS)
            _cache_tex[nombre] = img
            return img
    # Fallback: color sólido neutro
    img = Image.new("RGB", (res, res), (200, 195, 185))
    _cache_tex[nombre] = img
    return img

# ── Cara de fondo (la más importante: la que se ve "al fondo" de la habitación) ─
def generar_cara_fondo(cfg: dict, res: int) -> Image.Image:
    # Base: textura de pared
    pared_tex = cfg.get("pared")
    base = cargar_tex(pared_tex, res) if pared_tex else Image.new("RGB", (res, res), (200, 195, 185))

    # Oscurecer para simular profundidad
    base = ImageEnhance.Brightness(base).enhance(cfg["brillo"] * 0.75)

    # Aplicar tinte de ambiente
    tinte = Image.new("RGB", (res, res), cfg["color_ambiente"])
    base  = Image.blend(base, tinte, 0.25)

    draw = ImageDraw.Draw(base)

    # Dibujar muebles como siluetas con gradiente
    for (tipo, rect, color) in cfg.get("muebles", []):
        x0 = int(rect[0] * res)
        y0 = int((1.0 - rect[3]) * res)  # Y invertido (0=suelo, 1=techo)
        x1 = int(rect[2] * res)
        y1 = int((1.0 - rect[1]) * res)
        if x1 <= x0 or y1 <= y0: continue

        # Silhoueta con gradiente lateral (más oscuro en bordes)
        r, g, b = color
        draw.rectangle([x0, y0, x1, y1], fill=(r, g, b))

        # Highlight superior (borde iluminado)
        hl = (min(255, r+60), min(255, g+60), min(255, b+60))
        draw.rectangle([x0, y0, x1, min(y0+max(2, (y1-y0)//8), y1)], fill=hl)

        # Sombra inferior
        sh = (max(0, r-40), max(0, g-40), max(0, b-40))
        draw.rectangle([x0, max(y0, y1-max(2,(y1-y0)//6)), x1, y1], fill=sh)

    # Luz de techo en la parte superior (bloom artificial)
    luz_y = int(res * 0.82)
    lc = cfg["color_ambiente"]
    for y in range(luz_y, res):
        t = (y - luz_y) / (res - luz_y)
        for x in range(0, res):
            px = base.getpixel((x, y))
            br = (
                int(px[0] + (lc[0] - px[0]) * t * 0.4),
                int(px[1] + (lc[1] - px[1]) * t * 0.4),
                int(px[2] + (lc[2] - px[2]) * t * 0.4),
            )
            draw.point((x, y), fill=br)

    # Blur suave (simula profundidad de campo en el interior)
    base = base.filter(ImageFilter.GaussianBlur(radius=1.2))
    return base

def generar_cara_suelo(cfg: dict, res: int) -> Image.Image:
    nombre = cfg.get("suelo")
    base   = cargar_tex(nombre, res) if nombre else Image.new("RGB", (res, res), (80, 65, 50))
    base   = ImageEnhance.Brightness(base).enhance(cfg["brillo"] * 0.65)
    # Oscurecer esquinas (oclusión ambiental)
    ao = Image.new("RGB", (res, res), (0, 0, 0))
    base = Image.blend(base, ao, 0.15)
    return base

def generar_cara_techo(cfg: dict, res: int) -> Image.Image:
    c = cfg.get("color_techo", (240, 235, 220))
    img = Image.new("RGB", (res, res), c)
    draw = ImageDraw.Draw(img)
    lc = cfg["color_ambiente"]
    # Luminaria central
    cx, cy = res // 2, res // 2
    for r in range(min(res//4, 80), 0, -1):
        t = 1.0 - r / (res // 4)
        col = (
            int(c[0] + (255 - c[0]) * t * 0.9),
            int(c[1] + (255 - c[1]) * t * 0.9),
            int(c[2] + (255 - c[2]) * t * 0.5),
        )
        draw.ellipse([cx-r, cy-r, cx+r, cy+r], fill=col)
    return img

def generar_cara_pared(cfg: dict, res: int, lado: str) -> Image.Image:
    pared_tex = cfg.get("pared")
    base = cargar_tex(pared_tex, res) if pared_tex else Image.new("RGB", (res, res), (200, 195, 185))
    base = ImageEnhance.Brightness(base).enhance(cfg["brillo"] * 0.6)
    tinte = Image.new("RGB", (res, res), cfg["color_ambiente"])
    base  = Image.blend(base, tinte, 0.20)
    # Oscurecer el lado alejado de la ventana (oclusión de esquina)
    fade = Image.new("RGB", (res, res), (0, 0, 0))
    base = Image.blend(base, fade, 0.20)
    return base

# ── Generar las 6 caras de un arquetipo ────────────────────────────────────────
CARAS_UNITY = ["+X", "-X", "+Y", "-Y", "+Z", "-Z"]

def generar_cubemap(nombre_arq: str, cfg: dict):
    salida = OUT_DIR / nombre_arq.lower()
    salida.mkdir(parents=True, exist_ok=True)

    # Verificar si ya están todas generadas
    if all((salida / f"{c}.png").exists() for c in CARAS_UNITY):
        print(f"  ✓ {nombre_arq} ya existe")
        return

    print(f"  ⚙  Generando {nombre_arq}...", end="", flush=True)
    res = RES

    caras = {
        "+X": generar_cara_pared(cfg, res, "der"),
        "-X": generar_cara_pared(cfg, res, "izq"),
        "+Y": generar_cara_techo(cfg, res),
        "-Y": generar_cara_suelo(cfg, res),
        "+Z": generar_cara_fondo(cfg, res),
        "-Z": generar_cara_fondo(cfg, res),  # cara trasera = mimos muebles (menos visible)
    }

    for nombre_cara, img in caras.items():
        ruta = salida / f"{nombre_cara}.png"
        img.save(str(ruta), "PNG", optimize=True)

    print(f" ✓  ({res}px × 6 = {sum((salida/f'{c}.png').stat().st_size for c in CARAS_UNITY)//1024}KB)")

# ── Main ───────────────────────────────────────────────────────────────────────
if __name__ == "__main__":
    print(f"\n═══ Generando cubemaps fotorrealistas para {len(ARQUETIPOS)} arquetipos ═══")
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    for nombre, cfg in ARQUETIPOS.items():
        generar_cubemap(nombre, cfg)
    total = sum(1 for _ in OUT_DIR.rglob("*.png"))
    print(f"\n  ✅  {total} imágenes PNG generadas en:\n  {OUT_DIR.relative_to(RAIZ)}")
    print("\n  EN UNITY: Altsasu → Interiores → Integrar todos los assets")
