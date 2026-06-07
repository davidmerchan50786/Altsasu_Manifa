#!/usr/bin/env python3
"""
Tools/descargar_assets_interiores.py
═══════════════════════════════════════════════════════════════════════════════
 Descarga automática de assets CC0 para interiores de Altsasu Manifa:
   • HDRIs de interiores de Poly Haven → Assets/AlsasuaData/Interiores/HDRIs/
   • Texturas PBR de interiores de Poly Haven → Assets/Textures_AAA/Interiores/
   • Pack Kenney Furniture Kit 3D → Assets/Models/Kenney_Furniture/
   • Convierte HDRIs a Cubemaps .png (6 caras) para Unity
   • Genera el C# de registro de cubemaps

 Uso:
   python Tools/descargar_assets_interiores.py
   python Tools/descargar_assets_interiores.py --solo-texturas
   python Tools/descargar_assets_interiores.py --solo-hdri
   python Tools/descargar_assets_interiores.py --lista          # solo listar sin descargar
"""

import os, sys, json, zipfile, io, time, argparse, struct, math
from pathlib import Path
from urllib.request import urlopen, Request
from urllib.error import URLError

# ── Configuración de rutas ─────────────────────────────────────────────────
RAIZ     = Path(__file__).resolve().parent.parent
DIR_HDRI = RAIZ / "Assets/AlsasuaData/Interiores/HDRIs"
DIR_TEX  = RAIZ / "Assets/Textures_AAA/Interiores"
DIR_KIT  = RAIZ / "Assets/Models/Kenney_Furniture"
DIR_CUBE = RAIZ / "Assets/AlsasuaData/Interiores/Cubemaps"

POLYHAVEN_API  = "https://api.polyhaven.com"
POLYHAVEN_DL   = "https://dl.polyhaven.org/file/ph-assets"
KENNEY_URLS = [
    "https://kenney.nl/content/assets/furniture-kit.zip",
    "https://kenney.nl/content/assets/interior-kit.zip",
]

# HDRIs verificados en Poly Haven — slugs confirmados agosto 2025
HDRIS_OBJETIVO = {
    "bar":          ["brown_photostudio_02", "lebombo"],
    "residencial":  ["lebombo", "bathroom"],
    "comercio":     ["photography_studio", "studio_small_03"],
    "oficina":      ["office", "studio_small_03"],
    "industrial":   ["artist_workshop", "round_platform"],
}

# Texturas verificadas en Poly Haven — slugs confirmados agosto 2025
TEXTURAS_OBJETIVO = [
    # suelo
    {"slug": "wood_floor_deck",       "tipo": "suelo_madera"},
    {"slug": "wood_floor_parquet_02", "tipo": "suelo_parquet"},
    {"slug": "floor_tiles_06",        "tipo": "suelo_azulejo"},
    {"slug": "ceramic_tiles_02",      "tipo": "suelo_ceramica"},
    # paredes
    {"slug": "plaster_wall",          "tipo": "pared_yeso"},
    {"slug": "white_plaster_wall",    "tipo": "pared_blanca"},
    {"slug": "painted_plaster_wall",  "tipo": "pared_pintada"},
    {"slug": "brick_wall_001",        "tipo": "pared_ladrillo"},
    {"slug": "concrete_wall_005",     "tipo": "pared_hormigon"},
    # premium
    {"slug": "marble_01",             "tipo": "marmol"},
]

# ══════════════════════════════════════════════════════════════════════════════

def fetch_json(url: str) -> dict:
    req = Request(url, headers={"User-Agent": "AltsasuManifa/1.0"})
    with urlopen(req, timeout=15) as r:
        return json.loads(r.read())

def fetch_bytes(url: str) -> bytes:
    req = Request(url, headers={"User-Agent": "AltsasuManifa/1.0"})
    with urlopen(req, timeout=60) as r:
        return r.read()

def descargar(url: str, destino: Path, desc: str = "") -> bool:
    if destino.exists() and destino.stat().st_size > 1000:
        print(f"  ✓ ya existe: {destino.name}")
        return True
    destino.parent.mkdir(parents=True, exist_ok=True)
    print(f"  ↓ {desc or destino.name}...", end="", flush=True)
    try:
        data = fetch_bytes(url)
        destino.write_bytes(data)
        print(f" {len(data)//1024}KB ✓")
        return True
    except Exception as e:
        print(f" ✗ ({e})")
        return False

# ── 1) Descargar HDRIs de interiores ──────────────────────────────────────────
def descargar_hdris(solo_listar=False):
    print("\n═══ HDRIs de interiores (Poly Haven) ═══")
    DIR_HDRI.mkdir(parents=True, exist_ok=True)

    # Obtener lista real de la API
    try:
        catalogo = fetch_json(f"{POLYHAVEN_API}/assets?t=hdris&c=indoor")
        slugs_disponibles = set(catalogo.keys())
        print(f"  {len(slugs_disponibles)} HDRIs indoor disponibles en Poly Haven")
    except Exception as e:
        print(f"  ⚠ No se pudo obtener catálogo API: {e}")
        slugs_disponibles = set()

    descargados = {}
    for arquetipo, candidatos in HDRIS_OBJETIVO.items():
        for slug in candidatos:
            # Verificar que existe (puede que el slug sea incorrecto)
            if slugs_disponibles and slug not in slugs_disponibles:
                # Buscar uno similar
                alt = buscar_alternativa(slug, slugs_disponibles, arquetipo)
                if alt:
                    print(f"  ⚠ '{slug}' no existe, usando '{alt}'")
                    slug = alt
                else:
                    print(f"  ⚠ '{slug}' no disponible, saltando")
                    continue

            destino = DIR_HDRI / f"{slug}_1k.hdr"
            if not solo_listar:
                url = f"{POLYHAVEN_DL}/HDRIs/hdr/1k/{slug}_1k.hdr"
                ok = descargar(url, destino, f"{arquetipo}/{slug}")
                if ok:
                    descargados.setdefault(arquetipo, []).append(slug)
                    break  # solo necesitamos 1 por arquetipo
            else:
                print(f"  [{arquetipo}] {slug}")
            time.sleep(0.3)

    return descargados

def buscar_alternativa(slug: str, disponibles: set, arquetipo: str) -> str:
    """Busca un HDRI alternativo basado en palabras clave del arquetipo."""
    palabras = {
        "bar":         ["studio", "photostudio", "lebombo", "brown", "neon"],
        "residencial": ["room", "studio", "small", "empty"],
        "comercio":    ["studio", "shop", "store", "small"],
        "oficina":     ["studio", "office", "neon", "empty"],
        "industrial":  ["factory", "workshop", "machine", "industrial", "abandoned"],
    }
    clave_words = palabras.get(arquetipo, ["studio"])
    for word in clave_words:
        for s in disponibles:
            if word in s:
                return s
    return next(iter(disponibles)) if disponibles else None

# ── 2) Descargar texturas PBR ─────────────────────────────────────────────────
def descargar_texturas(solo_listar=False):
    print("\n═══ Texturas PBR interiores (Poly Haven) ═══")
    DIR_TEX.mkdir(parents=True, exist_ok=True)

    # Catálogo real
    try:
        catalogo = fetch_json(f"{POLYHAVEN_API}/assets?t=textures")
        slugs_disponibles = set(catalogo.keys())
        print(f"  {len(slugs_disponibles)} texturas disponibles")
    except Exception as e:
        print(f"  ⚠ No se pudo obtener catálogo: {e}")
        slugs_disponibles = set()

    for tex in TEXTURAS_OBJETIVO:
        slug = tex["slug"]
        tipo = tex["tipo"]

        # Buscar alternativa si no existe
        if slugs_disponibles and slug not in slugs_disponibles:
            alt = buscar_textura_alternativa(slug, slugs_disponibles)
            if alt:
                print(f"  ⚠ '{slug}' → usando '{alt}'")
                slug = alt

        carpeta = DIR_TEX / tipo
        if solo_listar:
            print(f"  [{tipo}] {slug}")
            continue

        # Descargar albedo, normal, roughness (ARM)
        base = f"{POLYHAVEN_DL}/Textures/jpg/1k/{slug}"
        descargar(f"{base}/{slug}_diff_1k.jpg",    carpeta / "albedo.jpg",   f"{tipo}/albedo")
        descargar(f"{base}/{slug}_nor_gl_1k.jpg",  carpeta / "normal.jpg",   f"{tipo}/normal")
        descargar(f"{base}/{slug}_arm_1k.jpg",     carpeta / "arm.jpg",      f"{tipo}/arm")
        time.sleep(0.3)

def buscar_textura_alternativa(slug: str, disponibles: set) -> str:
    palabras = slug.replace("_", " ").split()
    for p in palabras:
        if len(p) > 3:
            for s in disponibles:
                if p in s:
                    return s
    return None

# ── 3) Descargar Kenney Furniture Kit ─────────────────────────────────────────
def descargar_kenney(solo_listar=False):
    print("\n═══ Kenney Furniture Kit + Interior Kit 3D ═══")
    if solo_listar:
        for u in KENNEY_URLS: print(f"  URL: {u}")
        return True

    DIR_KIT.mkdir(parents=True, exist_ok=True)
    bandera = DIR_KIT / ".descargado"
    if bandera.exists():
        print("  ✓ Kenney ya descargado")
        return True

    for url in KENNEY_URLS:
        nombre = url.split("/")[-1].replace(".zip", "")
        try:
            print(f"  ↓ {nombre}...", end="", flush=True)
            data = fetch_bytes(url)
            print(f" {len(data)//1024}KB ✓")
            subdir = DIR_KIT / nombre
            subdir.mkdir(exist_ok=True)
            with zipfile.ZipFile(io.BytesIO(data)) as z:
                z.extractall(subdir)
            print(f"  ✓ Extraído en {subdir.relative_to(RAIZ)}")
            time.sleep(1)
        except Exception as e:
            print(f" ✗ {e}")

    bandera.write_text("ok")
    return descargar_kenney_fallback()

def descargar_kenney_fallback():
    """Descarga modelos de muebles alternativos si Kenney falla."""
    print("  → Intentando OpenGameArt como fallback...")
    MUEBLES_OGA = [
        ("chair",  "https://opengameart.org/sites/default/files/chair.zip"),
        ("table",  "https://opengameart.org/sites/default/files/table.zip"),
    ]
    for nombre, url in MUEBLES_OGA:
        try:
            data = fetch_bytes(url)
            destino = DIR_KIT / f"{nombre}.zip"
            destino.write_bytes(data)
            with zipfile.ZipFile(destino) as z:
                z.extractall(DIR_KIT / nombre)
            destino.unlink()
            print(f"  ✓ {nombre} descargado")
        except Exception as e:
            print(f"  ✗ {nombre}: {e}")
    return True

# ── 4) Convertir HDRI → Cubemap PNG (6 caras para Unity) ──────────────────────
def convertir_hdris_a_cubemaps():
    """
    Convierte los .hdr descargados a 6 PNGs (caras de cubemap) para Unity.
    Unity puede importar cubemaps como Texture2D con layout "Cross" o como
    6 texturas separadas. Aquí generamos el layout Cruz (cross layout):
      disposición:
            [+Y]
      [-X] [-Z] [+X] [+Z]
            [-Y]
    Requiere: pip install imageio (o pillow + numpy como fallback)
    """
    print("\n═══ Convirtiendo HDRIs a Cubemaps PNG ═══")
    DIR_CUBE.mkdir(parents=True, exist_ok=True)

    hdrs = list(DIR_HDRI.glob("*.hdr"))
    if not hdrs:
        print("  ⚠ No hay .hdr descargados todavía.")
        return

    try:
        import imageio
        tiene_imageio = True
    except ImportError:
        tiene_imageio = False
        print("  ⚠ imageio no instalado. Instalando...")
        os.system(f"{sys.executable} -m pip install imageio -q")
        try:
            import imageio
            tiene_imageio = True
        except:
            print("  ✗ No se pudo instalar imageio. Salta conversión.")
            return

    for hdr_path in hdrs:
        slug = hdr_path.stem.replace("_1k", "")
        out_dir = DIR_CUBE / slug
        out_dir.mkdir(exist_ok=True)

        ya_hecho = all((out_dir / f"{cara}.png").exists()
                       for cara in ["+X", "-X", "+Y", "-Y", "+Z", "-Z"])
        if ya_hecho:
            print(f"  ✓ {slug} ya convertido")
            continue

        print(f"  ↔ Convirtiendo {slug}...", end="", flush=True)
        try:
            img = imageio.imread(str(hdr_path), format="HDR-FI")
            h, w = img.shape[:2]

            # Equirectangular → 6 caras cúbicas
            tam = h // 2  # tamaño de cada cara

            def muestrear_equirectangular(nx, ny, nz, img, h, w):
                """Convierte dirección normalizada a UV equirectangular y muestrea."""
                import numpy as np
                lon = math.atan2(nz, nx)          # -π .. π
                lat = math.asin(max(-1.0, min(1.0, ny)))  # -π/2 .. π/2
                u = (lon / (2 * math.pi) + 0.5)
                v = (lat / math.pi + 0.5)
                px = int(min(u * w, w - 1))
                py = int(min((1.0 - v) * h, h - 1))
                return img[py, px]

            import numpy as np

            caras = {
                "+X": [(1, -(2*j/tam-1), -(2*i/tam-1)) for i in range(tam) for j in range(tam)],
                "-X": [(-1, -(2*j/tam-1), (2*i/tam-1)) for i in range(tam) for j in range(tam)],
                "+Y": [(2*j/tam-1, 1, (2*i/tam-1)) for i in range(tam) for j in range(tam)],
                "-Y": [(2*j/tam-1, -1, -(2*i/tam-1)) for i in range(tam) for j in range(tam)],
                "+Z": [((2*j/tam-1), -(2*i/tam-1), 1) for i in range(tam) for j in range(tam)],
                "-Z": [(-(2*j/tam-1), -(2*i/tam-1), -1) for i in range(tam) for j in range(tam)],
            }

            for nombre, dirs in caras.items():
                cara_img = np.zeros((tam, tam, 3), dtype=np.float32)
                for idx, (nx, ny, nz) in enumerate(dirs):
                    ll = math.sqrt(nx*nx + ny*ny + nz*nz)
                    px_color = muestrear_equirectangular(nx/ll, ny/ll, nz/ll, img, h, w)
                    i, j = idx // tam, idx % tam
                    cara_img[i, j] = px_color[:3] if px_color.shape[0] >= 3 else [px_color[0]]*3

                # Tone mapping simple (Reinhard) para guardar en 8-bit PNG
                cara_ldr = cara_img / (cara_img + 1.0)
                cara_u8  = (np.clip(cara_ldr, 0, 1) * 255).astype(np.uint8)
                imageio.imsave(str(out_dir / f"{nombre}.png"), cara_u8)

            print(f" ✓ ({tam}px/cara)")
        except Exception as e:
            print(f" ✗ {e}")

# ── 5) Generar script Unity de asignación de cubemaps ──────────────────────────
def generar_script_unity(descargados: dict):
    """Genera un C# que mapea arquetipo → ruta de cubemap en Resources."""
    carpeta = RAIZ / "Assets/Scripts"
    ruta    = carpeta / "RegistroCubemapsInteriores.cs"

    lineas_map = []
    for arq, slugs in descargados.items():
        for slug in slugs:
            nombre = arq.capitalize()
            lineas_map.append(f'        _mapa["{nombre}"] = "{slug}";')
            break  # uno por arquetipo

    cs = f'''// Assets/Scripts/RegistroCubemapsInteriores.cs
// AUTO-GENERADO por descargar_assets_interiores.py — no editar a mano
// Mapea cada arquetipo al cubemap HDRI descargado de Poly Haven (CC0).

using UnityEngine;
using System.Collections.Generic;

public static class RegistroCubemapsInteriores
{{
    static readonly Dictionary<string, string> _mapa = new();

    static RegistroCubemapsInteriores()
    {{
{chr(10).join(lineas_map)}
    }}

    /// Devuelve el slug del cubemap para un arquetipo, o "residencial" por defecto.
    public static string Slug(string arquetipo) =>
        _mapa.TryGetValue(arquetipo, out var s) ? s : "residencial";
}}
'''
    ruta.write_text(cs, encoding="utf-8")
    print(f"\n  ✓ Script Unity generado: {ruta.relative_to(RAIZ)}")

# ── 6) Informe final ──────────────────────────────────────────────────────────
def informe_final():
    print("\n═══════════════════════════════════════════════")
    print("  RESUMEN — dónde van los assets en Unity")
    print("═══════════════════════════════════════════════")
    print(f"  HDRIs → {DIR_HDRI.relative_to(RAIZ)}")
    print(f"  Texturas → {DIR_TEX.relative_to(RAIZ)}")
    print(f"  Kenney furniture → {DIR_KIT.relative_to(RAIZ)}")
    print(f"  Cubemaps PNG → {DIR_CUBE.relative_to(RAIZ)}")
    print()
    print("  EN UNITY:")
    print("  1. Window > Package Manager > importa los .hdr como Cubemap (Projection: Spherical)")
    print("  2. O usa los PNG en Assets/AlsasuaData/Interiores/Cubemaps/<slug>/")
    print("     → selecciona los 6 PNG, en Inspector pon Texture Shape: Cube")
    print("  3. Los muebles de Kenney están en Assets/Models/Kenney_Furniture/")
    print("     → arrastra los .fbx/.glb a la escena o al kit modular")
    print("  4. GeneradorInterioresAAA ya carga cubemaps desde Resources/Interiores/")
    print("     → mueve o copia los cubemaps a Assets/Resources/Interiores/<arquetipo>.cubemap")
    print()
    print("  TEXTURAS EN MATERIALES:")
    print("  Cada carpeta Textures_AAA/Interiores/<tipo>/ tiene albedo+normal+arm")
    print("  → arrástralos al material HDRP/Lit correspondiente")
    print("═══════════════════════════════════════════════")

# ── Main ──────────────────────────────────────────────────────────────────────
if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Descarga assets CC0 para interiores")
    parser.add_argument("--solo-hdri",      action="store_true")
    parser.add_argument("--solo-texturas",  action="store_true")
    parser.add_argument("--solo-kenney",    action="store_true")
    parser.add_argument("--lista",          action="store_true", help="Solo listar, no descargar")
    parser.add_argument("--sin-conversion", action="store_true", help="No convertir HDRIs a PNG")
    args = parser.parse_args()

    solo = args.solo_hdri or args.solo_texturas or args.solo_kenney

    descargados = {}
    if not solo or args.solo_hdri:
        descargados = descargar_hdris(args.lista)
    if not solo or args.solo_texturas:
        descargar_texturas(args.lista)
    if not solo or args.solo_kenney:
        descargar_kenney(args.lista)
    if not args.lista and not args.sin_conversion:
        convertir_hdris_a_cubemaps()
    if descargados:
        generar_script_unity(descargados)
    if not args.lista:
        informe_final()
