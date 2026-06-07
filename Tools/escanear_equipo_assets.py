#!/usr/bin/env python3
"""
Tools/escanear_equipo_assets.py
═══════════════════════════════════════════════════════════════════════════════
 ESCÁNER DE ASSETS DE TODO TU EQUIPO — Altsasu Manifa

 Rastrea las carpetas que le indiques (Descargas, Asset Store, proyectos viejos,
 discos externos...), detecta assets ÚTILES, los clasifica por tipo y los copia
 al proyecto en la carpeta correcta dentro de Assets/_Incoming/ para que el
 auto-importador de Unity los procese.

 IMPORTANTE: este script se ejecuta EN TU PC (no en la nube), porque solo tu
 equipo tiene acceso a tu disco completo.

 Uso:
   # Escaneo de las carpetas típicas de Windows automáticamente:
   python Tools/escanear_equipo_assets.py

   # Carpetas concretas:
   python Tools/escanear_equipo_assets.py "C:/Users/tu/Downloads" "D:/AssetStore"

   # Solo listar sin copiar (ver qué encontraría):
   python Tools/escanear_equipo_assets.py --dry-run

   # Filtrar por tipo:
   python Tools/escanear_equipo_assets.py --solo hdri,muebles
"""
import os, sys, shutil, hashlib, argparse, json
from pathlib import Path

RAIZ     = Path(__file__).resolve().parent.parent
INCOMING = RAIZ / "Assets/_Incoming"

# ── Clasificación por extensión + palabras clave del nombre/ruta ────────────────
# Cada categoría: (extensiones, palabras_clave_que_suman, palabras_que_excluyen)
CATEGORIAS = {
    "hdri": {
        "ext": {".hdr", ".exr"},
        "clave": ["indoor", "interior", "room", "studio", "office", "bar",
                  "shop", "garage", "warehouse", "hall", "kitchen", "house"],
        "excluye": [],
        "destino": "HDRIs_Interior",
    },
    "hdri_exterior": {
        "ext": {".hdr", ".exr"},
        "clave": ["sky", "outdoor", "field", "sunset", "forest", "street",
                  "city", "mountain", "cloud", "dawn", "dusk"],
        "excluye": [],
        "destino": "HDRIs_Exterior",
    },
    "muebles": {
        "ext": {".fbx", ".obj", ".glb", ".gltf", ".blend"},
        "clave": ["chair", "table", "sofa", "bed", "desk", "shelf", "lamp",
                  "furniture", "cabinet", "couch", "stool", "silla", "mesa",
                  "sofa", "cama", "armario", "estanteria", "mueble", "kitchen",
                  "fridge", "tv", "bookshelf", "drawer", "wardrobe"],
        "excluye": ["car", "vehicle", "weapon", "gun", "tree", "rock"],
        "destino": "Muebles",
    },
    "props_urbanos": {
        "ext": {".fbx", ".obj", ".glb", ".gltf"},
        "clave": ["bench", "lamp", "streetlight", "trash", "bin", "sign",
                  "fence", "barrier", "hydrant", "bollard", "kiosk", "phone",
                  "farola", "banco", "papelera", "valla", "semaforo", "poste",
                  "container", "dumpster", "pallet", "crate", "barrel"],
        "excluye": ["interior", "furniture"],
        "destino": "PropsUrbanos",
    },
    "vehiculos": {
        "ext": {".fbx", ".obj", ".glb", ".gltf"},
        "clave": ["car", "vehicle", "truck", "van", "bus", "coche", "camion",
                  "moto", "bike", "bicycle", "police", "ambulance", "taxi"],
        "excluye": [],
        "destino": "Vehiculos",
    },
    "personajes": {
        "ext": {".fbx", ".glb", ".gltf"},
        "clave": ["character", "human", "person", "man", "woman", "npc",
                  "people", "civilian", "police", "soldier", "personaje",
                  "ch_", "rigged", "mixamo", "avatar"],
        "excluye": ["weapon"],
        "destino": "Personajes",
    },
    "texturas_interior": {
        "ext": {".png", ".jpg", ".jpeg", ".tga", ".tif"},
        "clave": ["floor", "wall", "tile", "wood", "plaster", "marble",
                  "parquet", "carpet", "brick", "ceiling", "wallpaper",
                  "suelo", "pared", "madera", "azulejo", "marmol", "techo"],
        "excluye": ["terrain", "grass", "rock", "cliff", "sand", "icon",
                    "ui", "gui", "logo", "thumbnail", "preview"],
        "destino": "Texturas_Interior",
    },
    "texturas_fachada": {
        "ext": {".png", ".jpg", ".jpeg", ".tga"},
        "clave": ["facade", "building", "stone", "concrete", "stucco",
                  "fachada", "edificio", "piedra", "hormigon", "ladrillo",
                  "render", "sandstone", "masonry"],
        "excluye": ["icon", "ui", "logo", "interior"],
        "destino": "Texturas_Fachada",
    },
    "sonidos": {
        "ext": {".wav", ".ogg", ".mp3", ".aiff"},
        "clave": ["ambient", "city", "crowd", "street", "traffic", "rain",
                  "wind", "footstep", "door", "car", "siren", "protest",
                  "ciudad", "multitud", "lluvia", "viento", "paso", "sirena",
                  "manifest", "chant", "shout", "glass", "fire"],
        "excluye": ["music", "song", "soundtrack"],
        "destino": "Sonidos",
    },
    "animaciones": {
        "ext": {".fbx", ".anim"},
        "clave": ["anim", "walk", "run", "idle", "jump", "fight", "punch",
                  "throw", "death", "wave", "sit", "mixamo", "motion",
                  "andar", "correr", "saltar", "golpe", "lanzar"],
        "excluye": [],
        "destino": "Animaciones",
    },
}

# Carpetas que NUNCA escanear (rendimiento + ruido)
EXCLUIR_DIRS = {
    "node_modules", ".git", "Library", "Temp", "obj", "Build", "Builds",
    "Logs", ".vs", "AppData", "Windows", "Program Files", "Program Files (x86)",
    "$Recycle.Bin", "ProgramData", "Assets/_Incoming",  # no re-escanear lo copiado
}

TAM_MIN = {  # bytes mínimos para considerar (evita placeholders/iconos)
    "texturas_interior": 30_000, "texturas_fachada": 30_000,
    "muebles": 5_000, "props_urbanos": 5_000, "vehiculos": 10_000,
    "personajes": 50_000, "hdri": 100_000, "hdri_exterior": 100_000,
    "sonidos": 10_000, "animaciones": 3_000,
}

def carpetas_por_defecto():
    """Carpetas típicas de Windows/Mac/Linux donde suele haber assets."""
    home = Path.home()
    candidatas = [
        home / "Downloads", home / "Descargas",
        home / "Documents", home / "Documentos",
        home / "Desktop",   home / "Escritorio",
        home / "3D Objects",
        # Asset Store cache de Unity
        home / "AppData/Roaming/Unity/Asset Store-5.x",
        home / ".local/share/unity3d/Asset Store-5.x",
        Path("C:/Users/Public/Documents") if os.name == "nt" else None,
    ]
    return [c for c in candidatas if c and c.exists()]

def clasificar(ruta: Path):
    """Devuelve la categoría de un archivo o None si no es útil."""
    ext = ruta.suffix.lower()
    nombre_ruta = str(ruta).lower()
    mejor_cat, mejor_score = None, 0
    for cat, cfg in CATEGORIAS.items():
        if ext not in cfg["ext"]:
            continue
        if any(x in nombre_ruta for x in cfg["excluye"]):
            continue
        score = sum(1 for k in cfg["clave"] if k in nombre_ruta)
        # Un asset con extensión válida pero sin keyword: score bajo pero válido
        # solo para 3D (fbx/glb), no para texturas/sonidos (demasiado ruido)
        if score == 0 and ext in {".fbx", ".obj", ".glb", ".gltf", ".hdr", ".exr"}:
            score = 0.5
        if score > mejor_score:
            mejor_cat, mejor_score = cat, score
    return mejor_cat if mejor_score > 0 else None

def hash_archivo(ruta: Path, bloques=65536) -> str:
    h = hashlib.md5()
    try:
        with open(ruta, "rb") as f:
            buf = f.read(bloques)
            while buf:
                h.update(buf); buf = f.read(bloques)
    except Exception:
        return str(ruta)
    return h.hexdigest()

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("carpetas", nargs="*", help="Carpetas a escanear")
    ap.add_argument("--dry-run", action="store_true", help="Solo listar, no copiar")
    ap.add_argument("--solo", default="", help="Categorías separadas por coma")
    ap.add_argument("--max-mb", type=int, default=300, help="Tamaño máx por archivo (MB)")
    args = ap.parse_args()

    filtro = set(args.solo.split(",")) if args.solo else None
    carpetas = [Path(c) for c in args.carpetas] if args.carpetas else carpetas_por_defecto()

    if not carpetas:
        print("⚠ No se encontraron carpetas. Pasa rutas: python ... 'C:/Users/tu/Downloads'")
        return

    print("═══ Escaneando assets del equipo ═══")
    for c in carpetas: print(f"  📁 {c}")

    vistos_hash = set()
    encontrados = {cat: [] for cat in CATEGORIAS}
    total_escaneados = 0

    for base in carpetas:
        for root, dirs, files in os.walk(base):
            # Podar directorios excluidos
            dirs[:] = [d for d in dirs if d not in EXCLUIR_DIRS
                       and not d.startswith(".")]
            for f in files:
                total_escaneados += 1
                ruta = Path(root) / f
                cat = clasificar(ruta)
                if cat is None: continue
                if filtro and cat not in filtro: continue
                try:
                    size = ruta.stat().st_size
                except Exception:
                    continue
                if size < TAM_MIN.get(cat, 1000): continue
                if size > args.max_mb * 1024 * 1024: continue
                # Deduplicar por hash
                h = hash_archivo(ruta)
                if h in vistos_hash: continue
                vistos_hash.add(h)
                encontrados[cat].append((ruta, size))

    # ── Informe ──────────────────────────────────────────────────────────────
    print(f"\n  Archivos escaneados: {total_escaneados:,}")
    print("\n═══ Assets útiles encontrados ═══")
    total_util = 0
    for cat, lista in encontrados.items():
        if not lista: continue
        mb = sum(s for _, s in lista) / (1024*1024)
        print(f"  {cat:22s}: {len(lista):4d} archivos ({mb:.0f} MB)")
        total_util += len(lista)

    if total_util == 0:
        print("  (nada útil — prueba otras carpetas o revisa --solo)")
        return

    if args.dry_run:
        print("\n  [DRY-RUN] No se copió nada. Quita --dry-run para copiar al proyecto.")
        # Volcar lista detallada a un JSON para revisión
        rep = {cat: [str(r) for r, _ in l] for cat, l in encontrados.items() if l}
        (RAIZ / "Tools/assets_encontrados.json").write_text(
            json.dumps(rep, indent=2, ensure_ascii=False), encoding="utf-8")
        print(f"  Lista detallada en Tools/assets_encontrados.json")
        return

    # ── Copiar a _Incoming/<destino>/ ─────────────────────────────────────────
    print(f"\n═══ Copiando {total_util} assets a Assets/_Incoming/ ═══")
    copiados = 0
    for cat, lista in encontrados.items():
        if not lista: continue
        destino = INCOMING / CATEGORIAS[cat]["destino"]
        destino.mkdir(parents=True, exist_ok=True)
        for ruta, _ in lista:
            try:
                # Para FBX/OBJ copiar también texturas adyacentes (carpeta hermana)
                dst = destino / ruta.name
                if dst.exists():
                    dst = destino / f"{ruta.stem}_{hash_archivo(ruta)[:6]}{ruta.suffix}"
                shutil.copy2(ruta, dst)
                copiados += 1
            except Exception as e:
                print(f"  ✗ {ruta.name}: {e}")
    print(f"  ✓ {copiados} archivos copiados.")
    print("\n  SIGUIENTE PASO en Unity:")
    print("    Altsasu → Assets → Importar desde _Incoming")
    print("  (convierte a HDRP, genera prefabs y coloca cada asset en su sitio)")

if __name__ == "__main__":
    main()
