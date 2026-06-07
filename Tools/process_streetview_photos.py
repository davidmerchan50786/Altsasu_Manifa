#!/usr/bin/env python3
"""
process_streetview_photos.py
Procesado automático de fotos Street View de Alsasua para extracción de texturas de fachada.
Uso: python3 Tools/process_streetview_photos.py
"""

import os
import sys
import json
import math
import numpy as np

# Intenta importar PIL y cv2; si no están disponibles, se escribe reporte vacío
_DEPS_OK = True
try:
    from PIL import Image, ImageEnhance, ImageFilter
    import cv2
except ImportError as e:
    print(f"[WARN] Dependencia no disponible — {e}")
    print("[WARN] Instala con: pip install pillow numpy opencv-python")
    _DEPS_OK = False

# ---------------------------------------------------------------------------
# Rutas
# ---------------------------------------------------------------------------
SCRIPT_DIR   = os.path.dirname(os.path.abspath(__file__))
PROJECT_ROOT = os.path.dirname(SCRIPT_DIR)

STREETVIEW_DIR = os.path.join(PROJECT_ROOT, "Assets", "AlsasuaData", "FacadeTextures", "StreetView")
PROCESSED_DIR  = os.path.join(PROJECT_ROOT, "Assets", "AlsasuaData", "FacadeTextures", "Processed")
REPORT_PATH    = os.path.join(PROJECT_ROOT, "Assets", "AlsasuaData", "FacadeTextures", "processing_report.json")

MAX_WIDTH = 2048

# Zonas conocidas y sus palabras clave en la ruta
ZONAS_CONOCIDAS = [
    "casco_viejo",
    "plaza_fueros",
    "ferial",
    "gaztetxe",
    "iglesia",
    "ayto",
    "plaza_zubeztia",
    "calle_garcia_jimenez",
]

README_CONTENT = """\
# FacadeTextures / StreetView

Coloca aquí las fotos reales de fachadas de Alsasua organizadas en subcarpetas por zona:

  casco_viejo/
  plaza_fueros/
  ferial/
  gaztetxe/
  iglesia/
  ayto/
  plaza_zubeztia/
  calle_garcia_jimenez/

Formatos soportados: .jpg  .jpeg  .png

Las fotos deben ser en orientación horizontal (landscape).
El script process_streetview_photos.py extraerá texturas procesadas en:
  Assets/AlsasuaData/FacadeTextures/Processed/

Para ejecutar el procesado:
  python3 Tools/process_streetview_photos.py
"""

# ---------------------------------------------------------------------------
# Utilidades
# ---------------------------------------------------------------------------

def detect_zone(path: str) -> str:
    """Detecta la zona a partir de la ruta del archivo."""
    path_lower = path.lower().replace("\\", "/")
    for zona in ZONAS_CONOCIDAS:
        if zona in path_lower:
            return zona
    # Si no coincide con ninguna zona conocida, usa el nombre de la carpeta padre
    return os.path.basename(os.path.dirname(path))


def find_photos(base_dir: str):
    """Devuelve lista de rutas absolutas de fotos encontradas recursivamente."""
    extensions = {".jpg", ".jpeg", ".png"}
    photos = []
    for root, _dirs, files in os.walk(base_dir):
        for fname in sorted(files):
            ext = os.path.splitext(fname)[1].lower()
            if ext in extensions:
                # Ignorar archivos ya procesados (minimap o processed)
                if "_minimap" in fname or "_facade" in fname:
                    continue
                photos.append(os.path.join(root, fname))
    return photos


def crop_minimap(img_np):
    """Recorta la esquina inferior derecha (~20% ancho, ~25% alto) como minimap."""
    h, w = img_np.shape[:2]
    x0 = int(w * 0.80)
    y0 = int(h * 0.75)
    return img_np[y0:h, x0:w]


def detect_skew_and_correct(img_np):
    """
    Detecta líneas verticales dominantes con Canny + HoughLines y corrige el skew.
    Devuelve la imagen corregida (sin recortar bordes negros para preservar información).
    """
    gray = cv2.cvtColor(img_np, cv2.COLOR_RGB2GRAY)

    # Canny adaptativo basado en la mediana de la imagen
    median_val = float(np.median(gray))
    sigma = 0.33
    lower = max(0, int((1.0 - sigma) * median_val))
    upper = min(255, int((1.0 + sigma) * median_val))
    edges = cv2.Canny(gray, lower, upper, apertureSize=3)

    # HoughLines estándar
    lines = cv2.HoughLines(edges, rho=1, theta=np.pi / 180, threshold=80)

    if lines is None or len(lines) == 0:
        return img_np  # Sin líneas detectadas — devolver original

    # Seleccionar solo líneas casi verticales (theta cercano a 0 o PI)
    vertical_angles = []
    for line in lines:
        rho, theta = line[0]
        # theta=0 o theta~PI son verticales; theta~PI/2 son horizontales
        angle_from_vertical = min(abs(theta), abs(theta - math.pi))
        if angle_from_vertical < math.radians(20):  # Dentro de 20° de vertical
            # Convertir theta a ángulo de inclinación en grados
            angle_deg = math.degrees(theta)
            if angle_deg > 90:
                angle_deg -= 180
            vertical_angles.append(angle_deg)

    if not vertical_angles:
        return img_np

    # Ángulo de skew = mediana de los ángulos de las líneas verticales
    skew_deg = float(np.median(vertical_angles))

    # Si el skew es menor de 0.5° no vale la pena corregir
    if abs(skew_deg) < 0.5:
        return img_np

    # Limitar corrección a ±15° para evitar distorsiones absurdas
    skew_deg = max(-15.0, min(15.0, skew_deg))

    h, w = img_np.shape[:2]
    cx, cy = w / 2.0, h / 2.0
    M = cv2.getRotationMatrix2D((cx, cy), skew_deg, 1.0)

    # Calcular nuevo tamaño para no recortar
    cos_a = abs(M[0, 0])
    sin_a = abs(M[0, 1])
    new_w = int(h * sin_a + w * cos_a)
    new_h = int(h * cos_a + w * sin_a)
    M[0, 2] += (new_w / 2.0) - cx
    M[1, 2] += (new_h / 2.0) - cy

    corrected = cv2.warpAffine(
        img_np, M, (new_w, new_h),
        flags=cv2.INTER_LANCZOS4,
        borderMode=cv2.BORDER_REFLECT_101,
    )
    return corrected


def de_illuminate(img_np, blur_radius: int = 200):
    """
    De-iluminación: estima la iluminación con un blur gaussiano de radio grande
    y divide la imagen por esa estimación para obtener reflectancia uniforme.
    """
    # Asegurar float32 para evitar overflow
    img_f = img_np.astype(np.float32)

    # blur_radius debe ser impar
    ksize = blur_radius if blur_radius % 2 == 1 else blur_radius + 1
    illumination = cv2.GaussianBlur(img_f, (ksize, ksize), sigmaX=blur_radius / 3.0)

    # Evitar división por cero
    illumination = np.clip(illumination, 1.0, None)

    # Normalizar: reflectance = img / illumination * mean(illumination)
    mean_illum = np.mean(illumination)
    result = (img_f / illumination) * mean_illum

    # Reescalar al rango [0, 255]
    result = np.clip(result, 0, 255).astype(np.uint8)
    return result


def adjust_for_basque_architecture(img_pil):
    """
    Ajuste de color para arquitectura vasca:
    - Reducir saturación -15%
    - Aumentar contraste ligeramente (+10%)
    """
    # Reducir saturación
    enhancer_color = ImageEnhance.Color(img_pil)
    img_pil = enhancer_color.enhance(0.85)  # 1.0 = original, 0.85 = -15%

    # Contraste ligeramente mayor
    enhancer_contrast = ImageEnhance.Contrast(img_pil)
    img_pil = enhancer_contrast.enhance(1.10)  # +10%

    return img_pil


def resize_to_max_width(img_pil, max_width: int = 2048):
    """Redimensiona la imagen para que el ancho no supere max_width, manteniendo proporción."""
    w, h = img_pil.size
    if w <= max_width:
        return img_pil
    ratio = max_width / w
    new_w = max_width
    new_h = int(h * ratio)
    return img_pil.resize((new_w, new_h), Image.LANCZOS)


# ---------------------------------------------------------------------------
# Procesado de una foto individual
# ---------------------------------------------------------------------------

def process_photo(src_path, zona):
    """
    Procesa una foto y guarda los resultados.
    Devuelve un dict con info, o None si falla.
    """
    try:
        img_pil = Image.open(src_path).convert("RGB")
    except Exception as e:
        print(f"  [ERROR] No se pudo abrir {src_path}: {e}")
        return None

    w, h = img_pil.size

    # Verificar que sea landscape (horizontal)
    if h > w:
        print(f"  [SKIP] {os.path.basename(src_path)} es portrait ({w}x{h}) — ignorando")
        return None

    img_np = np.array(img_pil)

    # --- Paso 1: Recortar minimap y guardarlo ---
    minimap_np = crop_minimap(img_np)
    base_name = os.path.splitext(os.path.basename(src_path))[0]
    minimap_dir = os.path.dirname(src_path)
    minimap_path = os.path.join(minimap_dir, f"{base_name}_minimap.png")
    try:
        Image.fromarray(minimap_np).save(minimap_path)
    except Exception as e:
        print(f"  [WARN] No se pudo guardar minimap: {e}")

    # --- Paso 2: Rectificación de perspectiva (corrección de skew) ---
    img_np = detect_skew_and_correct(img_np)

    # --- Paso 3: De-iluminación ---
    img_np = de_illuminate(img_np, blur_radius=200)

    # --- Paso 4: Ajuste de color para arquitectura vasca ---
    img_pil_processed = Image.fromarray(img_np)
    img_pil_processed = adjust_for_basque_architecture(img_pil_processed)

    # --- Paso 5: Redimensionar a 2048px máx ---
    img_pil_processed = resize_to_max_width(img_pil_processed, MAX_WIDTH)

    # --- Paso 6: Guardar textura procesada ---
    os.makedirs(PROCESSED_DIR, exist_ok=True)
    out_name = f"{zona}_{base_name}_facade.png"
    out_path = os.path.join(PROCESSED_DIR, out_name)
    try:
        img_pil_processed.save(out_path, format="PNG", optimize=False, compress_level=6)
    except Exception as e:
        print(f"  [ERROR] No se pudo guardar textura procesada: {e}")
        return None

    # Rutas relativas al proyecto para el JSON
    rel_original  = os.path.relpath(src_path, PROJECT_ROOT)
    rel_procesada = os.path.relpath(out_path, PROJECT_ROOT)

    print(f"  [OK] {rel_original}  →  {rel_procesada}")
    return {
        "original":  rel_original.replace("\\", "/"),
        "procesada": rel_procesada.replace("\\", "/"),
        "zona":      zona,
        "size_px":   list(img_pil_processed.size),
    }


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

def main():
    print("=" * 60)
    print("process_streetview_photos.py — Altsasu Manifa")
    print("=" * 60)

    os.makedirs(PROCESSED_DIR, exist_ok=True)

    if not _DEPS_OK:
        print("[INFO] Dependencias no disponibles — escribiendo reporte vacío y saliendo limpiamente.")
        os.makedirs(STREETVIEW_DIR, exist_ok=True)
        readme_path = os.path.join(STREETVIEW_DIR, "README.txt")
        if not os.path.exists(readme_path):
            with open(readme_path, "w", encoding="utf-8") as f:
                f.write(README_CONTENT)
        _write_empty_report()
        return

    # Verificar si el directorio StreetView existe y tiene fotos
    if not os.path.isdir(STREETVIEW_DIR):
        print(f"Directorio StreetView no encontrado: {STREETVIEW_DIR}")
        print("Creando directorio y README con instrucciones...")
        os.makedirs(STREETVIEW_DIR, exist_ok=True)
        readme_path = os.path.join(STREETVIEW_DIR, "README.txt")
        with open(readme_path, "w", encoding="utf-8") as f:
            f.write(README_CONTENT)
        print(f"README creado en: {readme_path}")
        _write_empty_report()
        return

    photos = find_photos(STREETVIEW_DIR)

    if not photos:
        print(f"No se encontraron fotos en: {STREETVIEW_DIR}")
        print("Creando README con instrucciones...")
        readme_path = os.path.join(STREETVIEW_DIR, "README.txt")
        with open(readme_path, "w", encoding="utf-8") as f:
            f.write(README_CONTENT)
        print(f"README creado en: {readme_path}")
        _write_empty_report()
        return

    print(f"Fotos encontradas: {len(photos)}")
    print()

    por_zona = {}
    lista_procesadas = []

    for photo_path in photos:
        zona = detect_zone(photo_path)
        print(f"Procesando [{zona}]: {os.path.basename(photo_path)}")
        result = process_photo(photo_path, zona)
        if result is not None:
            por_zona[zona] = por_zona.get(zona, 0) + 1
            lista_procesadas.append(result)

    # Generar report
    report = {
        "total_fotos":     len(photos),
        "procesadas":      len(lista_procesadas),
        "por_zona":        por_zona,
        "lista_procesadas": lista_procesadas,
    }
    os.makedirs(os.path.dirname(REPORT_PATH), exist_ok=True)
    with open(REPORT_PATH, "w", encoding="utf-8") as f:
        json.dump(report, f, ensure_ascii=False, indent=2)

    print()
    print(f"Report guardado en: {REPORT_PATH}")
    print(f"Total procesadas: {len(lista_procesadas)} / {len(photos)}")
    for z, n in sorted(por_zona.items()):
        print(f"  {z}: {n}")


def _write_empty_report():
    """Escribe un report vacío válido cuando no hay fotos."""
    report = {
        "total_fotos":      0,
        "procesadas":       0,
        "por_zona":         {},
        "lista_procesadas": [],
    }
    os.makedirs(os.path.dirname(REPORT_PATH), exist_ok=True)
    with open(REPORT_PATH, "w", encoding="utf-8") as f:
        json.dump(report, f, ensure_ascii=False, indent=2)
    print(f"Report vacío escrito en: {REPORT_PATH}")


if __name__ == "__main__":
    main()
