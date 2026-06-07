#!/usr/bin/env python3
"""Procesa las 257 fotos reales de Street View de Alsasua.
Rectificación de perspectiva, de-iluminación, normalización de color.
"""
import os, json, shutil
from pathlib import Path

try:
    from PIL import Image, ImageEnhance, ImageFilter
    import numpy as np
    import cv2
    HAS_CV2 = True
except ImportError:
    HAS_CV2 = False

# Rutas relativas al directorio del proyecto
BASE_DIR   = Path(__file__).parent.parent
INPUT_DIR  = BASE_DIR / "Assets/AlsasuaData/FacadeTextures/StreetView"
OUTPUT_DIR = BASE_DIR / "Assets/AlsasuaData/FacadeTextures/Processed"
OUTPUT_DIR.mkdir(parents=True, exist_ok=True)

ZONAS = [
    "casco_viejo",
    "plaza_fueros",
    "ferial",
    "gaztetxe",
    "iglesia",
    "ayto",
    "plaza_zubeztia",
    "garcia_jimenez",
]


def procesar_foto(img_path: str, output_path: str, zona: str) -> bool:
    img = Image.open(img_path).convert("RGB")
    w, h = img.size

    # --- 1. Guardar minimap (esquina inferior derecha ~20% ancho, 25% alto) ---
    minimap_path = str(output_path).replace(".png", "_minimap.png")
    minimap = img.crop((int(w * 0.8), int(h * 0.75), w, h))
    minimap.save(minimap_path)

    # --- 2. Recortar zona de fachada (quitar minimap del frame) ---
    fachada = img.crop((0, 0, int(w * 0.82), h))

    if HAS_CV2:
        # --- 3. Corrección de perspectiva con Hough ---
        arr = np.array(fachada)
        gray = cv2.cvtColor(arr, cv2.COLOR_RGB2GRAY)
        edges = cv2.Canny(gray, 50, 150, apertureSize=3)
        lines = cv2.HoughLines(edges, 1, np.pi / 180, 100)
        if lines is not None:
            angles = []
            for rho, theta in lines[:, 0]:
                angle = (theta - np.pi / 2) * 180 / np.pi
                if abs(angle) < 15:
                    angles.append(angle)
            if angles:
                skew = float(np.median(angles))
                if abs(skew) > 0.5:
                    fh, fw = arr.shape[:2]
                    M = cv2.getRotationMatrix2D((fw // 2, fh // 2), skew, 1.0)
                    corrected = cv2.warpAffine(arr, M, (fw, fh),
                                               flags=cv2.INTER_LANCZOS4,
                                               borderMode=cv2.BORDER_REFLECT)
                    fachada = Image.fromarray(corrected)

        # --- 4. De-iluminación (división por iluminación global) ---
        arr_f = np.array(fachada).astype(np.float32)
        # Blur grande captura la iluminación ambiental
        blur = cv2.GaussianBlur(arr_f, (201, 201), 0)
        delit = arr_f / (blur + 1e-3)
        delit = np.clip(delit, 0, 2)
        # Renormalizar a 0-255 con percentil para evitar clipping
        p2, p98 = np.percentile(delit, 2), np.percentile(delit, 98)
        if p98 > p2:
            delit = (delit - p2) / (p98 - p2)
        delit = np.clip(delit * 255, 0, 255).astype(np.uint8)
        fachada = Image.fromarray(delit)

    # --- 5. Ajustes de color para arquitectura vasca ---
    fachada = ImageEnhance.Color(fachada).enhance(0.85)      # -15% saturación
    fachada = ImageEnhance.Contrast(fachada).enhance(1.10)   # +10% contraste
    fachada = ImageEnhance.Sharpness(fachada).enhance(1.30)  # unsharp suave

    # --- 6. Resize a 2048px ancho máximo ---
    if fachada.width > 2048:
        ratio = 2048 / fachada.width
        new_h = int(fachada.height * ratio)
        fachada = fachada.resize((2048, new_h), Image.LANCZOS)

    fachada.save(output_path, "PNG", optimize=True)
    return True


def main():
    # Recoger fotos (excluir minimaps y README)
    fotos = sorted(INPUT_DIR.glob("*.png"))
    fotos += sorted(INPUT_DIR.glob("*.jpg"))
    fotos += sorted(INPUT_DIR.glob("*.jpeg"))
    fotos = [
        f for f in fotos
        if "minimap" not in f.name.lower() and "readme" not in f.name.lower()
    ]
    fotos.sort()  # orden cronológico por nombre

    n = len(fotos)
    print(f"Encontradas {n} fotos de Street View.")
    print(f"cv2 disponible: {HAS_CV2}")

    chunk = max(1, n // 8)
    procesadas = []
    errores = []

    for i, foto in enumerate(fotos):
        zona_idx = min(i // chunk, 7)
        zona = ZONAS[zona_idx]
        idx_en_zona = (i % chunk) + 1
        out_name = f"{zona}_{idx_en_zona:03d}.png"
        out_path = str(OUTPUT_DIR / out_name)

        try:
            ok = procesar_foto(str(foto), out_path, zona)
            if ok:
                procesadas.append({
                    "original": str(foto),
                    "procesada": out_path,
                    "zona": zona,
                    "idx": idx_en_zona,
                })
                print(f"[{i+1:3d}/{n}] {zona}/{out_name}")
        except Exception as e:
            print(f"[ERROR] {foto.name}: {e}")
            errores.append({"archivo": str(foto), "error": str(e)})

    # Informe final
    report = {
        "total_fotos": n,
        "procesadas": len(procesadas),
        "errores": len(errores),
        "por_zona": {z: sum(1 for p in procesadas if p["zona"] == z) for z in ZONAS},
        "lista_procesadas": procesadas,
        "lista_errores": errores,
    }
    report_path = BASE_DIR / "Assets/AlsasuaData/FacadeTextures/processing_report.json"
    with open(report_path, "w", encoding="utf-8") as f:
        json.dump(report, f, indent=2, ensure_ascii=False)

    print(f"\nCompletado: {len(procesadas)}/{n} fotos procesadas.")
    print(f"Errores:    {len(errores)}")
    print(f"Informe:    {report_path}")
    print("Por zona:")
    for z, cnt in report["por_zona"].items():
        print(f"  {z:20s}: {cnt}")


if __name__ == "__main__":
    main()
