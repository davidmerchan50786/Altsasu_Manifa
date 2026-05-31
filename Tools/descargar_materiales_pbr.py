"""
descargar_materiales_pbr.py — Descarga materiales PBR hiperrealistas CC0 desde ambientCG.com

Uso:
    cd E:\\Desk\\DAM\\Altsasu_Manifa\\Tools
    pip install requests
    python descargar_materiales_pbr.py

Descarga 12 materiales 2K-JPG (~150 MB total) en Assets/AlsasuaData/Textures/PBR/
Después, en Unity: Altsasu GTA → Utilidades → ★ Crear Materiales PBR desde Texturas

Licencia: CC0 1.0 Universal — uso libre comercial sin atribución.
Fuente: https://ambientcg.com  (300+ materiales gratis del mismo autor que Poliigon)
"""

import os
import sys
import zipfile
import urllib.request
import shutil

# Carpeta de destino — relativa al proyecto Unity
DEST_DIR = os.path.normpath(
    os.path.join(os.path.dirname(__file__), "..", "Assets", "AlsasuaData", "Textures", "PBR")
)

# Catálogo curado para escenario Alsasua/euskal-herriko
# Cada entrada: (id_ambientcg, carpeta_local, descripción)
MATERIALES = [
    # ─── EDIFICIOS — más variedad para más realismo ────────────────
    ("Bricks051",   "Bricks_Rojo_Casco",     "Ladrillo rojo casco viejo"),
    ("Bricks074",   "Bricks_Naranja",        "Ladrillo naranja envejecido"),
    ("Bricks097",   "Bricks_Moderno",        "Ladrillo moderno limpio"),
    ("Bricks082",   "Bricks_Blanco",         "Ladrillo pintado blanco"),
    ("Plaster001",  "Plaster_Blanco",        "Yeso/estuco blanco"),
    ("PaintedPlaster003", "Plaster_Crema",   "Plaster pintado crema (típico vasco)"),
    ("PaintedPlaster017", "Plaster_Amarillo","Plaster amarillo desgastado"),
    ("Rocks023",    "Piedra_Sillar",         "Sillar de piedra (Iglesia, ayuntamiento)"),
    ("Rock035",     "Piedra_Caliza",         "Piedra caliza fachadas"),

    # ─── TEJADOS ────────────────────────────────────────────────────
    ("RoofingTiles013", "Tejado_Teja_Roja",  "Tejas curvas rojas tradicional"),
    ("RoofingTiles003", "Tejado_Pizarra",    "Pizarra (tejados modernos)"),

    # ─── CALLES Y SUELO ─────────────────────────────────────────────
    ("Asphalt023",  "Asfalto_Carretera",     "Asfalto carretera"),
    ("Asphalt010",  "Asfalto_Gastado",       "Asfalto desgastado con grietas"),
    ("Concrete036", "Hormigon_Acera",        "Hormigón aceras"),
    ("Concrete044", "Hormigon_Sucio",        "Hormigón sucio con manchas"),
    ("PavingStones092", "Adoquin_Plaza",     "Adoquín plaza (Herriko)"),
    ("PavingStones070", "Adoquin_Viejo",     "Adoquín viejo desnivelado"),
    ("Gravel023",   "Grava_Caminos",         "Grava caminos rurales"),

    # ─── DETALLES ARQUITECTÓNICOS ──────────────────────────────────
    ("Wood062",     "Madera_Puerta",         "Madera oscura puertas/postigos"),
    ("Wood048",     "Madera_Vieja",          "Madera muy desgastada"),
    ("Metal032",    "Metal_Forja",           "Hierro forjado rejas/balcones"),
    ("Metal004",    "Metal_Oxidado",         "Metal oxidado"),
    ("Tiles093",    "Azulejo_Decorativo",    "Azulejo decorativo entradas"),

    # ─── NATURALEZA — para terrain ──────────────────────────────────
    ("Grass001",    "Hierba",                "Hierba terrain detail"),
    ("Grass004",    "Hierba_Seca",           "Hierba seca verano"),
    ("Ground037",   "Tierra",                "Tierra zonas rurales"),
    ("Ground003",   "Tierra_Mojada",         "Tierra húmeda bosque"),
    ("Rock020",     "Roca_Montana",          "Roca montañosa Aralar"),
    ("Leaves013",   "Hojarasca",             "Hojarasca otoño"),
    ("Snow003",     "Nieve",                 "Nieve invierno cumbres"),
]

# HDRI Skyboxes — formato EXR equirectangular para HDRP
# Fuente: Poly Haven (https://polyhaven.com — CC0)
HDRI_SKIES = [
    ("kloofendal_43d_clear_puresky", "Kloofendal_Mediodia",
     "Cielo despejado mediodía (default)"),
    ("kloppenheim_06_puresky",       "Kloppenheim_Tarde",
     "Atardecer dorado"),
    ("dikhololo_night",              "DikhoIolo_Noche",
     "Cielo nocturno estrellado (vía láctea visible)"),
    ("kiara_late_afternoon",         "Kiara_Amanecer",
     "Amanecer dorado bajo"),
    ("approaching_storm",            "Tormenta_Aproximandose",
     "Cielo de tormenta dramático"),
]

URL_PATRON = "https://ambientcg.com/get?file={id}_2K-JPG.zip"

# Mapas que vamos a conservar (descartar el resto para ahorrar espacio)
MAPAS_UTILES = [
    "_Color.jpg",      # albedo
    "_NormalGL.jpg",   # normal map (OpenGL convention — Unity standard)
    "_Roughness.jpg",  # PBR roughness
    "_AmbientOcclusion.jpg",
    "_Displacement.jpg",
    "_Metalness.jpg",  # solo en metales — puede no existir
]


def descargar_y_extraer(mat_id, carpeta_local, descripcion):
    url = URL_PATRON.format(id=mat_id)
    destino_carpeta = os.path.join(DEST_DIR, carpeta_local)
    os.makedirs(destino_carpeta, exist_ok=True)

    # Saltar si ya descargado
    if any(os.path.exists(os.path.join(destino_carpeta, f"{mat_id}{m}"))
           for m in ["_Color.jpg", "_Color.png"]):
        print(f"  ✓ Ya existe: {carpeta_local}")
        return True

    zip_path = os.path.join(destino_carpeta, f"{mat_id}.zip")
    print(f"  ↓ {descripcion}  ({mat_id})")

    try:
        req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
        with urllib.request.urlopen(req, timeout=60) as resp, open(zip_path, "wb") as f:
            shutil.copyfileobj(resp, f)

        with zipfile.ZipFile(zip_path, "r") as zf:
            for nombre in zf.namelist():
                if any(nombre.endswith(m) for m in MAPAS_UTILES):
                    # Extraer sin la ruta interna
                    src = zf.open(nombre)
                    dst_path = os.path.join(destino_carpeta, os.path.basename(nombre))
                    with src, open(dst_path, "wb") as dst:
                        shutil.copyfileobj(src, dst)

        os.remove(zip_path)
        return True
    except Exception as e:
        print(f"  ✗ Error en {mat_id}: {e}")
        if os.path.exists(zip_path):
            os.remove(zip_path)
        return False


HDRI_URL_PATRON = "https://dl.polyhaven.org/file/ph-assets/HDRIs/exr/4k/{id}.exr"
HDRI_DEST_DIR = os.path.normpath(
    os.path.join(os.path.dirname(__file__), "..", "Assets", "AlsasuaData", "HDRI")
)


def descargar_hdri(hdri_id, nombre_local, descripcion):
    os.makedirs(HDRI_DEST_DIR, exist_ok=True)
    destino = os.path.join(HDRI_DEST_DIR, f"{nombre_local}.exr")
    if os.path.exists(destino):
        print(f"  ✓ Ya existe: {nombre_local}.exr")
        return True
    url = HDRI_URL_PATRON.format(id=hdri_id)
    print(f"  ↓ HDRI {descripcion}  ({hdri_id})")
    try:
        req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
        with urllib.request.urlopen(req, timeout=120) as resp, open(destino, "wb") as f:
            shutil.copyfileobj(resp, f)
        return True
    except Exception as e:
        print(f"  ✗ Error HDRI {hdri_id}: {e}")
        return False


def main():
    print(f"\n=== Descarga PBR + HDRI para Altsasu Manifa ===")
    print(f"Materiales destino: {DEST_DIR}")
    print(f"HDRI destino:       {HDRI_DEST_DIR}")
    print(f"Total materiales: {len(MATERIALES)} · HDRIs: {len(HDRI_SKIES)}\n")

    os.makedirs(DEST_DIR, exist_ok=True)
    correctos = 0
    fallidos = 0

    print("--- MATERIALES PBR ---")
    for i, (mat_id, carpeta, desc) in enumerate(MATERIALES, 1):
        print(f"[{i}/{len(MATERIALES)}]", end=" ")
        if descargar_y_extraer(mat_id, carpeta, desc):
            correctos += 1
        else:
            fallidos += 1

    print("\n--- HDRI SKIES ---")
    for i, (hdri_id, nombre, desc) in enumerate(HDRI_SKIES, 1):
        print(f"[{i}/{len(HDRI_SKIES)}]", end=" ")
        if descargar_hdri(hdri_id, nombre, desc):
            correctos += 1
        else:
            fallidos += 1

    print(f"\n=== Resumen ===")
    print(f"  ✓ OK: {correctos}")
    print(f"  ✗ Fallidos: {fallidos}")
    print(f"\nAhora en Unity:")
    print(f"  Altsasu GTA → Utilidades → ★ Crear Materiales PBR desde Texturas")
    print(f"  Altsasu GTA → ▶ Flujo AAA+ → 15 · Realismo AAA+ (usará HDRI automáticamente)")


if __name__ == "__main__":
    main()
