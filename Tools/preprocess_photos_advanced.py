#!/usr/bin/env python3
"""
PRE-PROCESADO AVANZADO DE FOTOS — Altsasu Manifa
=================================================
Pre-procesado avanzado de fotos para fotogrametría óptima.
Aplica técnicas que maximizan el número de features detectados por Meshroom.

Para cada foto en Processed/ aplica:
  1. Detección y recorte de ROI de fachada (Canny + RANSAC homografía)
  2. Equalización CLAHE en espacio LAB (mejora sombras: portales, balcones)
  3. Denoising fastNlMeansDenoisingColored antes de feature extraction
  4. Sharpening adaptativo (unsharp mask calibrado para ladrillo/piedra)
  5. Corrección de perspectiva robusta (RANSAC sobre líneas Hough)
  6. Normalización de exposición por edificio (histogram matching)
  7. Guarda en Processed_Enhanced/ sin sobreescribir originales
  8. Genera metadata JSON por foto (features_estimados, calidad_exposicion, angulo_estimado)

Uso:
    python Tools/preprocess_photos_advanced.py
    python Tools/preprocess_photos_advanced.py --edificio iglesia_001
    python Tools/preprocess_photos_advanced.py --skip-crop     # omitir ROI crop
    python Tools/preprocess_photos_advanced.py --skip-exposure # omitir normalización exposición
    python Tools/preprocess_photos_advanced.py --workers 4     # multiproceso

Requiere:
    pip install opencv-python numpy scikit-image
"""

import argparse
import json
import math
import os
import sys
import time
from collections import defaultdict
from concurrent.futures import ProcessPoolExecutor, as_completed
from datetime import datetime
from pathlib import Path

# ─── CONFIGURACIÓN ────────────────────────────────────────────────────────────

SCRIPT_DIR   = Path(__file__).resolve().parent
PROJECT_ROOT = SCRIPT_DIR.parent

PROCESSED_DIR  = PROJECT_ROOT / "Assets" / "AlsasuaData" / "FacadeTextures" / "Processed"
ENHANCED_DIR   = PROJECT_ROOT / "Assets" / "AlsasuaData" / "FacadeTextures" / "Processed_Enhanced"
MAPPING_JSON   = PROJECT_ROOT / "Assets" / "AlsasuaData" / "photo_building_mapping.json"
METADATA_JSON  = ENHANCED_DIR / "preprocessing_metadata.json"

# Parámetros CLAHE
CLAHE_CLIP_LIMIT   = 2.0
CLAHE_TILE_GRID    = (8, 8)

# Parámetros denoising
DENOISE_H            = 6
DENOISE_H_COLOR      = 6
DENOISE_TEMPLATE_WIN = 7
DENOISE_SEARCH_WIN   = 21

# Parámetros sharpening (calibrado para arenisca/ladrillo vasco)
UNSHARP_SIGMA    = 1.0
UNSHARP_STRENGTH = 0.7   # peso del detalle: 0.5–1.0 (piedra: 0.7)

# RANSAC perspectiva
RANSAC_REPROJ_THRESH = 3.0
MIN_HOUGH_LINES      = 4    # mínimo de líneas para intentar corrección


# ─── UTILIDADES ───────────────────────────────────────────────────────────────

def log(msg: str, level: str = "INFO"):
    ts = datetime.now().strftime("%H:%M:%S")
    icons = {"INFO": "·", "OK": "✓", "WARN": "!", "ERR": "✗"}
    print(f"  {ts}  [{icons.get(level,'·')}]  {msg}", flush=True)


def import_cv2():
    """Import cv2 con mensaje claro si no está instalado."""
    try:
        import cv2
        return cv2
    except ImportError:
        log("opencv-python no está instalado.", "ERR")
        log("  Instala: pip install opencv-python", "ERR")
        sys.exit(1)


def import_np():
    try:
        import numpy as np
        return np
    except ImportError:
        log("numpy no está instalado.", "ERR")
        log("  Instala: pip install numpy", "ERR")
        sys.exit(1)


# ─── ESTIMACIÓN DE FEATURES HARRIS ────────────────────────────────────────────

def estimate_harris_features(img_gray, cv2, np) -> int:
    """
    Cuenta corners Harris para estimar nº de features detectables por Meshroom.
    Es una aproximación: más corners = más puntos clave para SIFT/AKAZE.
    """
    # blockSize=2, ksize=3, k=0.04 (parámetros estándar Harris)
    harris = cv2.cornerHarris(img_gray.astype(np.float32), blockSize=2, ksize=3, k=0.04)
    # Umbral: 1% del máximo
    threshold = 0.01 * harris.max()
    return int((harris > threshold).sum())


# ─── DETECCIÓN ROI DE FACHADA ─────────────────────────────────────────────────

def detect_facade_roi(img, cv2, np):
    """
    Detecta el rectángulo dominante de fachada usando Canny + contornos.
    Devuelve (x, y, w, h) del crop o None si no se detecta claramente.

    Estrategia:
    - Escalar imagen a altura máx 800px para velocidad
    - Canny con umbrales adaptativos basados en la mediana
    - Encontrar contorno más grande con relación de aspecto de fachada (0.4–3.0)
    - Aplicar margen del 5% para no cortar bordes
    """
    h_orig, w_orig = img.shape[:2]
    scale = 1.0
    max_h = 800
    img_small = img

    if h_orig > max_h:
        scale = max_h / h_orig
        new_w  = int(w_orig * scale)
        img_small = cv2.resize(img, (new_w, max_h), interpolation=cv2.INTER_AREA)

    gray = cv2.cvtColor(img_small, cv2.COLOR_BGR2GRAY)

    # Umbral Canny adaptativo: basado en mediana de intensidad
    median_v = float(np.median(gray))
    sigma    = 0.33
    lo_thresh = int(max(0,   (1.0 - sigma) * median_v))
    hi_thresh = int(min(255, (1.0 + sigma) * median_v))
    edges = cv2.Canny(gray, lo_thresh, hi_thresh)

    # Dilatar bordes para cerrar gaps
    kernel = cv2.getStructuringElement(cv2.MORPH_RECT, (3, 3))
    edges = cv2.dilate(edges, kernel, iterations=2)

    contours, _ = cv2.findContours(edges, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    if not contours:
        return None

    # Filtrar contornos con área mínima (10% imagen) y relación aspecto válida
    min_area = img_small.shape[0] * img_small.shape[1] * 0.10
    facade_candidates = []

    for cnt in contours:
        area = cv2.contourArea(cnt)
        if area < min_area:
            continue
        x, y, w, h = cv2.boundingRect(cnt)
        aspect = w / max(h, 1)
        if 0.3 <= aspect <= 3.5:
            facade_candidates.append((area, x, y, w, h))

    if not facade_candidates:
        return None

    # El contorno de mayor área es la fachada principal
    facade_candidates.sort(reverse=True)
    _, x, y, w, h = facade_candidates[0]

    # Añadir margen 5%
    margin_x = int(w * 0.05)
    margin_y = int(h * 0.05)
    x = max(0, x - margin_x)
    y = max(0, y - margin_y)
    w = min(img_small.shape[1] - x, w + 2 * margin_x)
    h = min(img_small.shape[0] - y, h + 2 * margin_y)

    # Escalar de vuelta a resolución original
    x  = int(x  / scale)
    y  = int(y  / scale)
    w  = int(w  / scale)
    h  = int(h  / scale)

    # Validar que el crop no sea trivialmente todo el frame
    coverage = (w * h) / (w_orig * h_orig)
    if coverage > 0.90:
        return None   # No recortar si ya es casi todo

    return (x, y, w, h)


# ─── CLAHE ────────────────────────────────────────────────────────────────────

def apply_clahe(img, cv2, np):
    """
    Equalización CLAHE en canal L del espacio LAB.
    Mejora detección de features en zonas oscuras (portales, bajo balcones).
    """
    lab = cv2.cvtColor(img, cv2.COLOR_BGR2LAB)
    clahe = cv2.createCLAHE(clipLimit=CLAHE_CLIP_LIMIT, tileGridSize=CLAHE_TILE_GRID)
    lab[:, :, 0] = clahe.apply(lab[:, :, 0])
    return cv2.cvtColor(lab, cv2.COLOR_LAB2BGR)


# ─── DENOISING ────────────────────────────────────────────────────────────────

def apply_denoising(img, cv2):
    """
    Denoising antes de feature extraction.
    fastNlMeansDenoisingColored preserva bordes mejor que blur gaussiano.
    """
    return cv2.fastNlMeansDenoisingColored(
        img,
        h=DENOISE_H,
        hColor=DENOISE_H_COLOR,
        templateWindowSize=DENOISE_TEMPLATE_WIN,
        searchWindowSize=DENOISE_SEARCH_WIN,
    )


# ─── SHARPENING ADAPTATIVO ────────────────────────────────────────────────────

def apply_adaptive_sharpening(img, cv2, np):
    """
    Unsharp mask calibrado para textura de ladrillo/piedra vasca:
    - Sigma 1.0: preserva detalles de grano fino (arenisca rojiza)
    - Strength 0.7: agresivo en bordes, suave en planos
    No aplica sharpening en zonas muy saturadas (cielo, coches) para evitar halos.
    """
    # Blur gaussiano como estimación de baja frecuencia
    sigma_px = UNSHARP_SIGMA * max(1, img.shape[1] // 500)
    sigma_px = max(1, int(sigma_px) | 1)   # impar
    blurred  = cv2.GaussianBlur(img, (sigma_px, sigma_px), UNSHARP_SIGMA)

    # Máscara de alta frecuencia
    detail   = img.astype(np.float32) - blurred.astype(np.float32)

    # Máscara de bordes (Sobel magnitude) para sharpen adaptativo
    gray     = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
    grad_x   = cv2.Sobel(gray, cv2.CV_32F, 1, 0, ksize=3)
    grad_y   = cv2.Sobel(gray, cv2.CV_32F, 0, 1, ksize=3)
    mag      = np.sqrt(grad_x**2 + grad_y**2)
    # Normalizar máscara de bordes [0,1]
    mag_norm = np.clip(mag / (mag.max() + 1e-6), 0, 1)

    # Aplicar sharpening ponderado por bordes
    edge_mask = mag_norm[:, :, np.newaxis]
    sharpened = img.astype(np.float32) + detail * UNSHARP_STRENGTH * edge_mask

    return np.clip(sharpened, 0, 255).astype(np.uint8)


# ─── CORRECCIÓN DE PERSPECTIVA (RANSAC) ──────────────────────────────────────

def correct_perspective_ransac(img, cv2, np):
    """
    Corrección de distorsión de perspectiva usando RANSAC para ajuste robusto.

    En vez de solo Hough lines, agrupa líneas verticales y horizontales,
    filtra outliers con RANSAC implícito (regresión robusta), y calcula
    homografía para rectificar la fachada.

    Devuelve la imagen corregida (o la original si no hay suficientes líneas).
    """
    h, w = img.shape[:2]
    gray  = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)

    # Blur suave para reducir ruido antes de Hough
    blurred = cv2.GaussianBlur(gray, (5, 5), 1.0)
    edges   = cv2.Canny(blurred, 50, 150, apertureSize=3)

    # Hough probabilístico: más rápido, devuelve segmentos
    lines = cv2.HoughLinesP(
        edges,
        rho=1,
        theta=math.pi / 180,
        threshold=80,
        minLineLength=h * 0.15,   # mínimo 15% de la altura
        maxLineGap=20,
    )

    if lines is None or len(lines) < MIN_HOUGH_LINES:
        return img  # sin suficientes líneas

    # Separar líneas verticales (|ángulo| < 20°) y horizontales (|ángulo - 90°| < 20°)
    vert_lines  = []
    horiz_lines = []

    for line in lines:
        x1, y1, x2, y2 = line[0]
        dx = x2 - x1
        dy = y2 - y1
        angle_deg = math.degrees(math.atan2(dy, dx + 1e-6))

        if abs(angle_deg) < 20 or abs(angle_deg) > 160:
            # Casi horizontal
            horiz_lines.append((x1, y1, x2, y2))
        elif 70 < abs(angle_deg) < 110:
            # Casi vertical
            vert_lines.append((x1, y1, x2, y2))

    if len(vert_lines) < 2 or len(horiz_lines) < 2:
        return img

    # RANSAC simple para encontrar la línea vertical dominante (moda de x-intercept)
    def ransac_dominant_x(lines_list, n_iters=50):
        """Devuelve la x_intercept más consistente entre las líneas verticales."""
        best_x    = None
        best_inliers = 0
        for _ in range(n_iters):
            idx    = np.random.randint(0, len(lines_list))
            x1, y1, x2, y2 = lines_list[idx]
            # Ecuación de la recta: x = x1 + (x2-x1)/(y2-y1) * (y - y1)
            dy = y2 - y1
            if abs(dy) < 1:
                continue
            slope_x = (x2 - x1) / dy
            # Evaluar inliers: distancia perpendicular < thresh
            inliers = 0
            for lx1, ly1, lx2, ly2 in lines_list:
                ldy = ly2 - ly1
                if abs(ldy) < 1:
                    continue
                lslope = (lx2 - lx1) / ldy
                if abs(lslope - slope_x) < 0.1:   # mismo pendiente
                    inliers += 1
            if inliers > best_inliers:
                best_inliers = inliers
                best_x = slope_x
        return best_x

    # Usar pendiente dominante vertical para calcular inclinación de perspectiva
    dominant_slope = ransac_dominant_x(vert_lines)

    if dominant_slope is None or abs(dominant_slope) < 0.002:
        return img  # perspectiva ya es correcta

    # Calcular corrección: si las líneas verticales tienen pendiente sx,
    # la homografía de cizalla la compensa.
    # H = [[1, 0, 0], [-sx, 1, 0], [0, 0, 1]]  (skew en x en función de y)
    M = np.float32([
        [1,              -dominant_slope, 0],
        [0,              1,               0],
        [0,              0,               1],
    ])

    # Warp afín (aproximación válida para correcciones menores)
    corrected = cv2.warpPerspective(
        img, M, (w, h),
        flags=cv2.INTER_LINEAR,
        borderMode=cv2.BORDER_REPLICATE,
    )

    return corrected


# ─── NORMALIZACIÓN DE EXPOSICIÓN ENTRE FOTOS ─────────────────────────────────

def compute_exposure_score(img, np) -> float:
    """
    Calcula score de exposición [0,1].
    0 = subexpuesto/sobreexpuesto, 1 = exposición perfecta.
    Basado en distribución de histograma luminancia.
    """
    # Luminancia perceptual
    lum = (0.299 * img[:, :, 2].astype(float) +
           0.587 * img[:, :, 1].astype(float) +
           0.114 * img[:, :, 0].astype(float))

    mean_lum = lum.mean() / 255.0
    # Score máximo en mean ≈ 0.5 (exposición media), decrece hacia extremos
    score = 1.0 - abs(mean_lum - 0.5) * 2.0
    return float(np.clip(score, 0.0, 1.0))


def compute_reference_hist(imgs_list, cv2, np):
    """
    Calcula histograma de referencia (mediana de histogramas) para normalización.
    Usa el canal L (luminancia) en LAB.
    """
    hists = []
    for img in imgs_list:
        lab  = cv2.cvtColor(img, cv2.COLOR_BGR2LAB)
        hist = cv2.calcHist([lab], [0], None, [256], [0, 256])
        hist = hist / (hist.sum() + 1e-6)   # normalizar
        hists.append(hist.flatten())

    # Histograma de referencia = mediana por bin
    ref_hist = np.median(np.array(hists), axis=0)
    return ref_hist


def match_histogram(img, ref_hist, cv2, np):
    """
    Histogram matching de la luminancia (canal L en LAB) al ref_hist.
    Normaliza exposición entre fotos del mismo edificio.
    """
    lab = cv2.cvtColor(img, cv2.COLOR_BGR2LAB)
    l_channel = lab[:, :, 0]

    # CDF origen
    src_hist = cv2.calcHist([l_channel], [0], None, [256], [0, 256]).flatten()
    src_cdf  = np.cumsum(src_hist) / (src_hist.sum() + 1e-6)

    # CDF referencia
    ref_cdf  = np.cumsum(ref_hist) / (ref_hist.sum() + 1e-6)

    # Tabla de lookup: mapear nivel src a nivel ref con CDF matching
    lut = np.zeros(256, dtype=np.uint8)
    ref_j = 0
    for src_i in range(256):
        while ref_j < 255 and ref_cdf[ref_j] < src_cdf[src_i]:
            ref_j += 1
        lut[src_i] = ref_j

    # Aplicar LUT al canal L
    lab[:, :, 0] = lut[l_channel]
    return cv2.cvtColor(lab, cv2.COLOR_LAB2BGR)


# ─── PROCESAR UNA FOTO ────────────────────────────────────────────────────────

def process_single_photo(foto_path: Path, out_dir: Path,
                         skip_crop: bool = False,
                         ref_hist=None) -> dict:
    """
    Aplica todo el pipeline de pre-procesado a una foto.
    Devuelve metadata dict con métricas.
    """
    cv2 = import_cv2()
    np  = import_np()

    img = cv2.imread(str(foto_path))
    if img is None:
        return {"foto": str(foto_path), "error": "no_se_pudo_leer"}

    h_orig, w_orig = img.shape[:2]
    metadata = {
        "foto_original":    str(foto_path),
        "foto_enhanced":    str(out_dir / foto_path.name),
        "resolucion_orig":  f"{w_orig}x{h_orig}",
        "timestamp":        datetime.now().isoformat(),
    }

    # Calidad de exposición antes de procesar
    exp_score_before = compute_exposure_score(img, np)
    metadata["calidad_exposicion_original"] = round(exp_score_before, 3)

    # ── 1. Crop ROI de fachada ──────────────────────────────────────────────
    roi = None
    if not skip_crop:
        roi = detect_facade_roi(img, cv2, np)
    if roi is not None:
        x, y, w_r, h_r = roi
        img = img[y:y+h_r, x:x+w_r]
        metadata["roi_crop"] = {"x": x, "y": y, "w": w_r, "h": h_r}
    else:
        metadata["roi_crop"] = None

    # ── 2. CLAHE (mejora zonas oscuras) ────────────────────────────────────
    img = apply_clahe(img, cv2, np)

    # ── 3. Denoising ───────────────────────────────────────────────────────
    img = apply_denoising(img, cv2)

    # ── 4. Sharpening adaptativo ────────────────────────────────────────────
    img = apply_adaptive_sharpening(img, cv2, np)

    # ── 5. Corrección de perspectiva RANSAC ─────────────────────────────────
    img = correct_perspective_ransac(img, cv2, np)

    # ── 6. Normalización de exposición (histogram matching) ─────────────────
    if ref_hist is not None:
        img = match_histogram(img, ref_hist, cv2, np)

    # ── 7. Guardar en Processed_Enhanced/ ───────────────────────────────────
    out_path = out_dir / foto_path.name
    cv2.imwrite(str(out_path), img, [cv2.IMWRITE_PNG_COMPRESSION, 6])

    # ── 8. Calcular métricas post-procesado ─────────────────────────────────
    gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
    features_estimados  = estimate_harris_features(gray, cv2, np)
    exp_score_after     = compute_exposure_score(img, np)
    h_new, w_new        = img.shape[:2]

    # Ángulo estimado desde nombre de archivo (formato: {zona}_{NNN}.png)
    angulo_estimado = None
    try:
        parts = foto_path.stem.rsplit("_", 1)
        if len(parts) == 2 and parts[1].isdigit():
            n = int(parts[1])
            angulo_estimado = (n * 15) % 360   # heurística: 15° por foto Street View
    except Exception:
        pass

    metadata.update({
        "resolucion_enhanced":    f"{w_new}x{h_new}",
        "features_estimados":     features_estimados,
        "calidad_exposicion":     round(exp_score_after, 3),
        "angulo_estimado":        angulo_estimado,
    })

    return metadata


# ─── CARGAR MAPPING EDIFICIO → FOTOS ─────────────────────────────────────────

def load_building_groups() -> dict:
    """
    Agrupa fotos por edificio_id desde photo_building_mapping.json.
    Si no existe, agrupa por prefijo de nombre de archivo.
    Devuelve: {edificio_id: [Path, ...]}
    """
    if MAPPING_JSON.exists():
        try:
            with open(MAPPING_JSON, "r", encoding="utf-8") as f:
                data = json.load(f)
            groups: dict = defaultdict(list)
            for entry in data.get("mappings", []):
                eid  = entry.get("edificio_id", "")
                foto = Path(entry.get("foto_procesada", ""))
                if not eid:
                    continue
                if not foto.is_absolute():
                    foto = PROJECT_ROOT / foto
                if not foto.exists():
                    foto = PROCESSED_DIR / foto.name
                if foto.exists():
                    groups[eid].append(foto)
            if groups:
                return dict(groups)
        except Exception:
            pass

    # Fallback: agrupar por prefijo de nombre
    groups = defaultdict(list)
    if PROCESSED_DIR.exists():
        for p in sorted(PROCESSED_DIR.glob("*.png")):
            parts = p.stem.rsplit("_", 1)
            prefix = parts[0] if len(parts) == 2 and parts[1].isdigit() else p.stem
            groups[prefix].append(p)
    return dict(groups)


# ─── PIPELINE PRINCIPAL ───────────────────────────────────────────────────────

def preprocess_building(edificio_id: str, fotos: list,
                        out_dir: Path, skip_crop: bool,
                        skip_exposure: bool) -> list:
    """
    Pre-procesa todas las fotos de un edificio.
    Calcula histograma de referencia si skip_exposure=False.
    """
    cv2 = import_cv2()
    np  = import_np()

    # Calcular histograma de referencia para este edificio
    ref_hist = None
    if not skip_exposure and len(fotos) >= 2:
        imgs = []
        for fp in fotos:
            img = cv2.imread(str(fp))
            if img is not None:
                imgs.append(img)
        if len(imgs) >= 2:
            ref_hist = compute_reference_hist(imgs, cv2, np)

    results = []
    for foto in fotos:
        try:
            meta = process_single_photo(foto, out_dir, skip_crop, ref_hist)
            results.append(meta)
        except Exception as e:
            log(f"Error procesando {foto.name}: {e}", "WARN")
            results.append({"foto": str(foto), "error": str(e)})

    return results


def run_pipeline(args):
    """Ejecuta el pipeline de pre-procesado completo."""
    cv2 = import_cv2()
    np  = import_np()

    if not PROCESSED_DIR.exists():
        log(f"Directorio Processed/ no encontrado: {PROCESSED_DIR}", "ERR")
        log("  Ejecuta primero: python Tools/process_streetview_real.py", "ERR")
        sys.exit(1)

    ENHANCED_DIR.mkdir(parents=True, exist_ok=True)

    log("Cargando grupos de fotos por edificio...")
    building_groups = load_building_groups()

    if args.edificio:
        if args.edificio not in building_groups:
            log(f"Edificio '{args.edificio}' no encontrado. Disponibles: {sorted(building_groups.keys())[:10]}", "ERR")
            sys.exit(1)
        building_groups = {args.edificio: building_groups[args.edificio]}

    total_fotos    = sum(len(v) for v in building_groups.values())
    log(f"Edificios: {len(building_groups)}  |  Fotos totales: {total_fotos}")
    log(f"Salida: {ENHANCED_DIR}")
    log("")

    all_metadata  = []
    t_start       = time.time()
    n_done        = 0

    for edificio_id, fotos in building_groups.items():
        log(f"Procesando edificio: {edificio_id}  ({len(fotos)} fotos)")
        results = preprocess_building(
            edificio_id, fotos, ENHANCED_DIR,
            skip_crop=args.skip_crop,
            skip_exposure=args.skip_exposure,
        )
        all_metadata.extend(results)
        n_done += len(fotos)

        # Calcular métricas del edificio
        valid_results = [r for r in results if "error" not in r]
        if valid_results:
            avg_features = sum(r.get("features_estimados", 0) for r in valid_results) / len(valid_results)
            avg_exposure = sum(r.get("calidad_exposicion", 0) for r in valid_results) / len(valid_results)
            color_consistency = 1.0 - float(np.std([r.get("calidad_exposicion", 0) for r in valid_results]))
            color_consistency = max(0.0, min(1.0, color_consistency))
            log(f"  Features medios: {avg_features:.0f}  |  Exposición: {avg_exposure:.2f}  |  "
                f"Consistencia color: {color_consistency:.2f}", "OK")

        elapsed = time.time() - t_start
        pct     = n_done / max(total_fotos, 1) * 100
        log(f"  Progreso: {n_done}/{total_fotos} ({pct:.0f}%)  |  {elapsed:.0f}s")

    # Guardar metadata JSON
    metadata_out = {
        "generated":       datetime.now().isoformat(),
        "total_fotos":     len(all_metadata),
        "enhanced_dir":    str(ENHANCED_DIR),
        "processing_opts": {
            "skip_crop":     args.skip_crop,
            "skip_exposure": args.skip_exposure,
            "clahe_clip":    CLAHE_CLIP_LIMIT,
            "denoise_h":     DENOISE_H,
            "unsharp_sigma": UNSHARP_SIGMA,
            "unsharp_str":   UNSHARP_STRENGTH,
        },
        "fotos": all_metadata,
    }
    with open(METADATA_JSON, "w", encoding="utf-8") as f:
        json.dump(metadata_out, f, indent=2, ensure_ascii=False)

    elapsed_total = time.time() - t_start
    fotos_ok      = sum(1 for m in all_metadata if "error" not in m)
    fotos_err     = len(all_metadata) - fotos_ok

    print("\n" + "=" * 70)
    print("  PRE-PROCESADO COMPLETADO")
    print("=" * 70)
    print(f"  Fotos procesadas : {fotos_ok} / {len(all_metadata)}")
    if fotos_err:
        print(f"  Errores          : {fotos_err}")
    print(f"  Tiempo total     : {elapsed_total:.0f}s  ({elapsed_total/60:.1f} min)")
    print(f"  Salida           : {ENHANCED_DIR}")
    print(f"  Metadata         : {METADATA_JSON}")
    print("=" * 70)
    print()
    print("  SIGUIENTE PASO:")
    print("  python Tools/meshroom_pipeline.py --all --gpu --enhanced-input")
    print()


# ─── MAIN ─────────────────────────────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser(
        description="Pre-procesado avanzado de fotos para fotogrametría — Altsasu Manifa",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Ejemplos:
  python Tools/preprocess_photos_advanced.py
  python Tools/preprocess_photos_advanced.py --edificio iglesia_001
  python Tools/preprocess_photos_advanced.py --skip-crop
  python Tools/preprocess_photos_advanced.py --skip-exposure
        """,
    )
    parser.add_argument("--edificio",      metavar="ID",
                        help="Procesa solo este edificio_id")
    parser.add_argument("--skip-crop",     action="store_true",
                        help="Omite detección y recorte de ROI de fachada")
    parser.add_argument("--skip-exposure", action="store_true",
                        help="Omite normalización de exposición entre fotos")
    args = parser.parse_args()

    print("\n" + "=" * 70)
    print("  PRE-PROCESADO AVANZADO DE FOTOS — Altsasu Manifa")
    print(f"  {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
    print("=" * 70)
    print()
    print("  Pasos:")
    print("  1. Detección ROI fachada (Canny + RANSAC)")
    print("  2. CLAHE en LAB (sombras portales/balcones)")
    print("  3. Denoising fastNlMeans")
    print("  4. Sharpening adaptativo (calibrado arenisca vasca)")
    print("  5. Corrección perspectiva RANSAC")
    print("  6. Normalización exposición entre fotos del edificio")
    print()

    run_pipeline(args)


if __name__ == "__main__":
    main()
