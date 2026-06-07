#!/usr/bin/env python3
"""
UNITY PHOTOGRAMMETRY IMPORTER — Altsasu Manifa  (v2 — métricas de calidad)
============================================================================
Actualiza el proyecto Unity para usar mallas fotogramétricas donde están disponibles.

Lee    : Assets/Models/Buildings_Photogrammetry/*.fbx  (generados por Meshroom+Blender)
Lee    : Assets/AlsasuaData/photo_building_mapping.json
Escribe: Assets/AlsasuaData/buildings_fusion_final.json  (añade campos photogrammetry_*)
Genera : Assets/AlsasuaData/photogrammetry_report.json  (estadísticas completas)

Los campos añadidos permiten que ImportadorEdificiosFBX.cs priorice mallas
fotogramétricas sobre arquetipos procedurales de SistemaEdificiosAAA.cs.

Campos añadidos por edificio:
  "photogrammetry_fbx":       "Assets/Models/Buildings_Photogrammetry/{id}.fbx"
  "photogrammetry_quality":   "high" | "medium" | "low"
  "photogrammetry_tris_lod0": N
  "photogrammetry_normal":    "Assets/AlsasuaData/FacadeTextures/Photogrammetry/{id}_normal.png"
  "photogrammetry_ao":        "Assets/AlsasuaData/FacadeTextures/Photogrammetry/{id}_ao.png"
  "photogrammetry_albedo":    "Assets/AlsasuaData/FacadeTextures/Photogrammetry/{id}_albedo.png"
  "photogrammetry_metrics":   {completeness, texel_density, angular_coverage,
                                color_consistency, overall_score}

Métricas de calidad v2:
  completeness     : % píxeles del UV atlas con textura válida (no negro)
  texel_density    : texels/m² de la textura resultante
  angular_coverage : variedad de ángulos cubiertos (más ángulos = mejor reconstrucción)
  color_consistency: 1 - varianza normalizada de exposición entre fotos (alta = problema)
  overall_score    : media ponderada de las 4 métricas anteriores

Uso:
    python Tools/unity_photogrammetry_importer.py
    python Tools/unity_photogrammetry_importer.py --dry-run
    python Tools/unity_photogrammetry_importer.py --report-only
    python Tools/unity_photogrammetry_importer.py --compute-metrics  # métricas de imagen
"""

import argparse
import json
import math
import os
import sys
from collections import defaultdict
from datetime import datetime
from pathlib import Path

# ─── RUTAS ────────────────────────────────────────────────────────────────────

SCRIPT_DIR   = Path(__file__).resolve().parent
PROJECT_ROOT = SCRIPT_DIR.parent

FBX_DIR     = PROJECT_ROOT / "Assets" / "Models" / "Buildings_Photogrammetry"
TEX_DIR     = PROJECT_ROOT / "Assets" / "AlsasuaData" / "FacadeTextures" / "Photogrammetry"
QUALITY_DIR = FBX_DIR      # los {id}_quality.json se guardan junto a los FBX

MAPPING_JSON      = PROJECT_ROOT / "Assets" / "AlsasuaData" / "photo_building_mapping.json"
FUSION_JSON       = PROJECT_ROOT / "Assets" / "AlsasuaData" / "buildings_fusion_final.json"
REPORT_JSON       = PROJECT_ROOT / "Assets" / "AlsasuaData" / "photogrammetry_report.json"
PROGRESS_JSON     = Path(r"E:\MeshroomCache\progress.json")  # solo en Windows

# Rutas Unity (relativas a Assets/, con / no \)
UNITY_FBX_PREFIX = "Assets/Models/Buildings_Photogrammetry"
UNITY_TEX_PREFIX = "Assets/AlsasuaData/FacadeTextures/Photogrammetry"


# ─── UTILIDADES ───────────────────────────────────────────────────────────────

def log(msg: str, level: str = "INFO"):
    icons = {"INFO": "·", "OK": "✓", "WARN": "!", "ERR": "✗"}
    print(f"  [{icons.get(level,'·')}] {msg}", flush=True)


def unity_path(absolute: Path) -> str:
    """Convierte ruta absoluta a ruta relativa Unity (Assets/...)."""
    try:
        rel = absolute.relative_to(PROJECT_ROOT)
        return str(rel).replace("\\", "/")
    except ValueError:
        return str(absolute).replace("\\", "/")


def fbx_to_unity(fbx_path: Path) -> str:
    return f"{UNITY_FBX_PREFIX}/{fbx_path.name}"


def tex_to_unity(tex_path: Path) -> str:
    return f"{UNITY_TEX_PREFIX}/{tex_path.name}"


# ─── MÉTRICAS DE CALIDAD FOTOGRAMÉTRICA ──────────────────────────────────────

def compute_texture_completeness(texture_path: Path) -> float:
    """
    Calcula la completitud del UV atlas: % de píxeles con textura válida (no negro).

    Un píxel se considera "válido" si su luminancia es > umbral mínimo.
    Un atlas con muchas zonas negras indica partes de la fachada sin fotografía.

    Devuelve float [0, 1]. Requiere Pillow o opencv-python.
    """
    if not texture_path or not texture_path.exists():
        return 0.0

    # Intentar con Pillow (más ligero)
    try:
        from PIL import Image
        import numpy as np
        img  = Image.open(str(texture_path)).convert("RGB")
        arr  = np.array(img, dtype=np.float32)
        # Luminancia perceptual
        lum  = 0.299 * arr[:, :, 0] + 0.587 * arr[:, :, 1] + 0.114 * arr[:, :, 2]
        # Umbral: píxel válido si lum > 5/255 ≈ 0.02 (casi negro)
        valid = (lum > 5).sum()
        total = lum.size
        return float(valid / max(total, 1))
    except ImportError:
        pass

    # Intentar con OpenCV
    try:
        import cv2
        import numpy as np
        img  = cv2.imread(str(texture_path))
        if img is None:
            return 0.0
        gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
        valid = (gray > 5).sum()
        return float(valid / max(gray.size, 1))
    except ImportError:
        pass

    # Sin dependencias de imagen: estimar por tamaño (heurística)
    # Si el archivo es > 100 KB para una 2K texture, probablemente tiene contenido
    size_kb = texture_path.stat().st_size // 1024
    if size_kb > 500:
        return 0.85   # estimado
    elif size_kb > 100:
        return 0.60
    else:
        return 0.30


def compute_texel_density(texture_path: Path, fbx_size_mb: float) -> int:
    """
    Estimación de texels/m² de la textura resultante.

    Fórmula simplificada: (resolución² × cobertura_uv) / área_fachada_estimada
    El área de fachada se estima desde el tamaño del FBX (proxy del área real).

    Devuelve texels/m² como entero.
    """
    if not texture_path or not texture_path.exists():
        return 0

    # Leer resolución de la textura
    tex_res = 2048   # default
    try:
        from PIL import Image
        with Image.open(str(texture_path)) as img:
            tex_res = img.size[0]   # ancho
    except Exception:
        pass

    # Estimar área de fachada: FBX de 1 MB ≈ 50 m² de fachada (heurística)
    area_m2 = max(1.0, fbx_size_mb * 50.0)

    # texels/m² = (tex_res × tex_res) / area_m2
    texels_per_m2 = int((tex_res * tex_res) / area_m2)
    return texels_per_m2


def compute_angular_coverage(photo_count_data: dict) -> float:
    """
    Estima la cobertura angular [0, 1] basada en el número de fotos y ángulos.

    - 1 ángulo / foto = 0.25 cobertura
    - 4 ángulos únicos = 0.75 cobertura
    - 8+ ángulos únicos = 1.0 cobertura

    Devuelve float [0, 1].
    """
    n_fotos   = photo_count_data.get("n_fotos",   0)
    n_angulos = photo_count_data.get("n_angulos", 1)

    if n_fotos == 0:
        return 0.0

    # Cobertura angular: normalizada respecto a 360° completo
    # Street View da ~15° entre fotos → 24 fotos = cobertura completa
    coverage_360 = min(1.0, n_fotos * 15.0 / 360.0)

    # Penalizar si hay pocos ángulos únicos (todas las fotos desde el mismo lado)
    angle_diversity = min(1.0, n_angulos / 8.0)

    return float(0.6 * coverage_360 + 0.4 * angle_diversity)


def compute_color_consistency_from_metadata(edificio_id: str) -> float:
    """
    Lee metadata del pre-procesado (preprocess_photos_advanced.py) para obtener
    la consistencia de color entre fotos del edificio.

    Si no hay metadata disponible, devuelve 0.7 (valor conservador neutral).

    Devuelve float [0, 1]:
      1.0 = todas las fotos tienen exposición idéntica (bueno)
      0.0 = exposición muy variable entre fotos (problema para Meshroom)
    """
    # Buscar metadata del pre-procesado
    enhanced_dir  = PROJECT_ROOT / "Assets" / "AlsasuaData" / "FacadeTextures" / "Processed_Enhanced"
    metadata_file = enhanced_dir / "preprocessing_metadata.json"

    if not metadata_file.exists():
        return 0.7   # sin datos: valor neutro

    try:
        with open(metadata_file, "r", encoding="utf-8") as f:
            meta = json.load(f)

        # Filtrar fotos de este edificio
        fotos_edificio = [
            m for m in meta.get("fotos", [])
            if edificio_id in m.get("foto_original", "")
        ]

        if len(fotos_edificio) < 2:
            return 0.7

        exposiciones = [
            m.get("calidad_exposicion", 0.7)
            for m in fotos_edificio
            if "calidad_exposicion" in m
        ]

        if len(exposiciones) < 2:
            return 0.7

        # Consistencia = 1 - (std / max_std_posible)
        import statistics
        std_exp   = statistics.stdev(exposiciones)
        max_std   = 0.5   # stdev máxima posible en [0,1]
        consistency = max(0.0, 1.0 - std_exp / max_std)
        return float(consistency)

    except Exception:
        return 0.7


def compute_photogrammetry_metrics(eid: str, fbx_entry: dict,
                                   photo_count_data: dict) -> dict:
    """
    Calcula las métricas de calidad fotogramétrica para un edificio.

    Métricas:
      completeness     : % píxeles UV atlas con textura válida
      texel_density    : texels/m² (más = mejor detalle)
      angular_coverage : variedad de ángulos de captura
      color_consistency: uniformidad de exposición entre fotos
      overall_score    : media ponderada

    Devuelve dict con todas las métricas y overall_score.
    """
    albedo_path  = fbx_entry.get("albedo_path")
    fbx_size_mb  = fbx_entry.get("fbx_size_mb", 0)

    # 1. Completitud UV atlas
    completeness = compute_texture_completeness(albedo_path)

    # 2. Densidad de texels
    texel_density = compute_texel_density(albedo_path, fbx_size_mb)

    # 3. Cobertura angular
    angular_coverage = compute_angular_coverage(photo_count_data)

    # 4. Consistencia de color
    color_consistency = compute_color_consistency_from_metadata(eid)

    # 5. Score global (media ponderada)
    # Pesos: completitud (30%) + cobertura angular (35%) + consistencia color (20%)
    # + texel_density normalizada (15%)
    texel_score = min(1.0, texel_density / 1024.0)   # normalizar: 1024 tx/m² = score 1.0

    overall = (0.30 * completeness +
               0.35 * angular_coverage +
               0.20 * color_consistency +
               0.15 * texel_score)
    overall = round(float(overall), 3)

    return {
        "completeness":       round(float(completeness), 3),
        "texel_density":      texel_density,
        "angular_coverage":   round(float(angular_coverage), 3),
        "color_consistency":  round(float(color_consistency), 3),
        "overall_score":      overall,
    }


# ─── ESCANEAR FBX GENERADOS ──────────────────────────────────────────────────

def scan_photogrammetry_fbx() -> dict:
    """
    Escanea Assets/Models/Buildings_Photogrammetry/ buscando FBX.
    Lee el {id}_quality.json asociado si existe.
    Devuelve: {edificio_id: {fbx_path, quality_data, tex_paths}}
    """
    results = {}

    if not FBX_DIR.exists():
        log(f"Directorio FBX no encontrado: {FBX_DIR}", "WARN")
        return results

    fbx_files = list(FBX_DIR.glob("*.fbx"))
    if not fbx_files:
        log(f"No se encontraron FBX en {FBX_DIR}", "WARN")
        return results

    log(f"FBX encontrados: {len(fbx_files)}")

    for fbx_path in sorted(fbx_files):
        eid = fbx_path.stem  # nombre sin extensión = edificio_id

        entry = {
            "edificio_id": eid,
            "fbx_path":    fbx_path,
            "fbx_size_mb": round(fbx_path.stat().st_size / 1_048_576, 2),
            "quality":     None,
            "tris_lod0":   None,
            "tris_lod1":   None,
            "tris_lod2":   None,
            "tris_lod3":   None,
            "uv_coverage": None,
            "delight":     None,
        }

        # Leer quality report si existe
        quality_json = QUALITY_DIR / f"{eid}_quality.json"
        if quality_json.exists():
            try:
                with open(quality_json, "r", encoding="utf-8") as f:
                    q = json.load(f)
                entry.update({
                    "tris_lod0":   q.get("tris_lod0"),
                    "tris_lod1":   q.get("tris_lod1"),
                    "tris_lod2":   q.get("tris_lod2"),
                    "tris_lod3":   q.get("tris_lod3"),
                    "uv_coverage": q.get("uv_coverage_pct"),
                    "delight":     q.get("delight_applied"),
                })
            except json.JSONDecodeError:
                pass

        # Buscar texturas asociadas
        albedo_path    = TEX_DIR / f"{eid}_albedo.png"
        albedo_4k_path = TEX_DIR / f"{eid}_albedo_4k.png"
        normal_path    = TEX_DIR / f"{eid}_normal.png"
        ao_path        = TEX_DIR / f"{eid}_ao.png"

        entry["albedo_exists"]    = albedo_path.exists()
        entry["albedo_4k_exists"] = albedo_4k_path.exists()
        entry["normal_exists"]    = normal_path.exists()
        entry["ao_exists"]        = ao_path.exists()
        # Usar 4K si está disponible (Real-ESRGAN upscale)
        entry["albedo_path"]   = albedo_4k_path if albedo_4k_path.exists() else (albedo_path if albedo_path.exists() else None)
        entry["albedo_2k_path"] = albedo_path if albedo_path.exists() else None
        entry["normal_path"]   = normal_path if normal_path.exists() else None
        entry["ao_path"]       = ao_path     if ao_path.exists()     else None

        results[eid] = entry

    return results


# ─── CALCULAR CALIDAD DESDE MAPPING ──────────────────────────────────────────

def load_photo_counts() -> dict:
    """
    Lee photo_building_mapping.json para obtener el conteo de fotos por edificio.
    Devuelve: {edificio_id: {"n_fotos": N, "zona": str, "n_angulos": N}}
    """
    if not MAPPING_JSON.exists():
        log(f"photo_building_mapping.json no encontrado: {MAPPING_JSON}", "WARN")
        return {}

    with open(MAPPING_JSON, "r", encoding="utf-8") as f:
        data = json.load(f)

    counts: dict = defaultdict(lambda: {"n_fotos": 0, "zona": "", "zonas": set()})
    for entry in data.get("mappings", []):
        eid  = entry.get("edificio_id", "")
        zona = entry.get("zona", "")
        if eid:
            counts[eid]["n_fotos"] += 1
            counts[eid]["zona"]     = zona
            counts[eid]["zonas"].add(zona)

    # Convertir sets a listas para serialización
    result = {}
    for eid, v in counts.items():
        n = v["n_fotos"]
        angulos = max(1, n // 4)
        if n >= 8 and angulos >= 3:
            quality = "high"
        elif n >= 4:
            quality = "medium"
        else:
            quality = "low"
        result[eid] = {
            "n_fotos":   n,
            "zona":      v["zona"],
            "n_angulos": angulos,
            "quality":   quality,
        }
    return result


# ─── CARGAR Y ACTUALIZAR buildings_fusion_final.json ─────────────────────────

def load_buildings_fusion() -> dict:
    """Lee buildings_fusion_final.json. Devuelve dict vacío si no existe."""
    if not FUSION_JSON.exists():
        log(f"buildings_fusion_final.json no encontrado: {FUSION_JSON}", "WARN")
        log("  Se creará photogrammetry_report.json igualmente.", "INFO")
        return {}

    try:
        with open(FUSION_JSON, "r", encoding="utf-8") as f:
            data = json.load(f)
        # Puede ser lista o dict con clave "buildings"
        if isinstance(data, list):
            return {str(b.get("id", b.get("osm_id", i))): b
                    for i, b in enumerate(data)}
        elif isinstance(data, dict) and "buildings" in data:
            return {str(b.get("id", b.get("osm_id", i))): b
                    for i, b in enumerate(data["buildings"])}
        elif isinstance(data, dict):
            return data
    except (json.JSONDecodeError, KeyError) as e:
        log(f"Error leyendo buildings_fusion_final.json: {e}", "WARN")
    return {}


def update_buildings_fusion(buildings_map: dict, fbx_data: dict,
                             photo_counts: dict, dry_run: bool,
                             compute_metrics: bool = False) -> int:
    """
    Añade/actualiza los campos photogrammetry_* en buildings_fusion_final.json.
    Devuelve el número de edificios actualizados.
    """
    updated = 0

    for eid, fbx_entry in fbx_data.items():
        if eid not in buildings_map:
            # Edificio con FBX pero sin entrada en fusion: crear entrada mínima
            buildings_map[eid] = {"id": eid}

        b = buildings_map[eid]

        # Determinar calidad (quality report > photo_counts)
        quality = fbx_entry.get("quality")
        if not quality:
            pc = photo_counts.get(eid, {})
            quality = pc.get("quality", "low")

        pc = photo_counts.get(eid, {})

        # Campos photogrammetry_*
        phot_fields = {
            "photogrammetry_fbx":         fbx_to_unity(fbx_entry["fbx_path"]),
            "photogrammetry_quality":      quality,
            "photogrammetry_tris_lod0":    fbx_entry.get("tris_lod0"),
            "photogrammetry_tris_lod1":    fbx_entry.get("tris_lod1"),
            "photogrammetry_tris_lod2":    fbx_entry.get("tris_lod2"),
            "photogrammetry_tris_lod3":    fbx_entry.get("tris_lod3"),
            "photogrammetry_uv_coverage":  fbx_entry.get("uv_coverage"),
            "photogrammetry_delit":        fbx_entry.get("delight"),
            "photogrammetry_upscale_4k":   fbx_entry.get("albedo_4k_exists", False),
            "photogrammetry_updated":      datetime.now().isoformat(),
        }

        if fbx_entry.get("albedo_path"):
            phot_fields["photogrammetry_albedo"] = tex_to_unity(fbx_entry["albedo_path"])
        # También guardar la versión 2K si hay 4K disponible
        if fbx_entry.get("albedo_2k_path") and fbx_entry.get("albedo_4k_exists"):
            phot_fields["photogrammetry_albedo_2k"] = tex_to_unity(fbx_entry["albedo_2k_path"])
        if fbx_entry.get("normal_path"):
            phot_fields["photogrammetry_normal"] = tex_to_unity(fbx_entry["normal_path"])
        if fbx_entry.get("ao_path"):
            phot_fields["photogrammetry_ao"] = tex_to_unity(fbx_entry["ao_path"])

        # Calcular métricas de calidad (requiere PIL/cv2 para completeness)
        if compute_metrics:
            try:
                metrics = compute_photogrammetry_metrics(eid, fbx_entry, pc)
                phot_fields["photogrammetry_metrics"] = metrics
            except Exception as e:
                log(f"  Métricas para '{eid}' fallaron: {e}", "WARN")
        else:
            # Calcular métricas que no requieren carga de imagen (siempre disponibles)
            try:
                angular_cov    = compute_angular_coverage(pc)
                color_consist  = compute_color_consistency_from_metadata(eid)
                phot_fields["photogrammetry_metrics"] = {
                    "completeness":      None,    # requiere --compute-metrics
                    "texel_density":     None,
                    "angular_coverage":  round(float(angular_cov), 3),
                    "color_consistency": round(float(color_consist), 3),
                    "overall_score":     None,
                }
            except Exception:
                pass

        b.update(phot_fields)
        updated += 1

    if not dry_run and updated > 0 and FUSION_JSON.exists():
        # Reescribir preservando formato original
        try:
            with open(FUSION_JSON, "r", encoding="utf-8") as f:
                raw = json.load(f)

            if isinstance(raw, list):
                # Actualizar edificios en lista por id
                id_to_idx = {}
                for i, b in enumerate(raw):
                    bid = str(b.get("id", b.get("osm_id", "")))
                    if bid:
                        id_to_idx[bid] = i
                for eid, b in buildings_map.items():
                    if eid in id_to_idx:
                        raw[id_to_idx[eid]].update(b)
                    else:
                        raw.append(b)
                with open(FUSION_JSON, "w", encoding="utf-8") as f:
                    json.dump(raw, f, indent=2, ensure_ascii=False)

            elif isinstance(raw, dict) and "buildings" in raw:
                id_to_idx = {}
                for i, b in enumerate(raw["buildings"]):
                    bid = str(b.get("id", b.get("osm_id", "")))
                    if bid:
                        id_to_idx[bid] = i
                for eid, b in buildings_map.items():
                    if eid in id_to_idx:
                        raw["buildings"][id_to_idx[eid]].update(b)
                    else:
                        raw["buildings"].append(b)
                raw["photogrammetry_updated"] = datetime.now().isoformat()
                with open(FUSION_JSON, "w", encoding="utf-8") as f:
                    json.dump(raw, f, indent=2, ensure_ascii=False)

            log(f"buildings_fusion_final.json actualizado ({updated} edificios)", "OK")
        except Exception as e:
            log(f"Error escribiendo buildings_fusion_final.json: {e}", "ERR")
    elif dry_run:
        log(f"[DRY-RUN] Se actualizarían {updated} edificios en buildings_fusion_final.json")

    return updated


# ─── GENERAR REPORTE ─────────────────────────────────────────────────────────

def generate_report(fbx_data: dict, photo_counts: dict,
                    updated_count: int, dry_run: bool) -> dict:
    """
    Genera photogrammetry_report.json con estadísticas completas.
    """
    by_quality = defaultdict(list)
    by_zona    = defaultdict(list)

    edificios = []
    for eid, entry in fbx_data.items():
        pc = photo_counts.get(eid, {})

        quality = entry.get("quality")
        if not quality:
            quality = pc.get("quality", "low")

        zona = pc.get("zona", "unknown")
        n_fotos = pc.get("n_fotos", 0)

        e_record = {
            "edificio_id":    eid,
            "zona":           zona,
            "quality":        quality,
            "n_fotos":        n_fotos,
            "n_angulos":      pc.get("n_angulos", 0),
            "fbx_size_mb":    entry.get("fbx_size_mb"),
            "tris_lod0":      entry.get("tris_lod0"),
            "tris_lod1":      entry.get("tris_lod1"),
            "tris_lod2":      entry.get("tris_lod2"),
            "tris_lod3":      entry.get("tris_lod3"),
            "uv_coverage":    entry.get("uv_coverage"),
            "albedo_ok":      entry.get("albedo_exists", False),
            "albedo_4k_ok":   entry.get("albedo_4k_exists", False),
            "normal_ok":      entry.get("normal_exists", False),
            "ao_ok":          entry.get("ao_exists", False),
            "delit_applied":  entry.get("delight", False),
            "unity_fbx":      fbx_to_unity(entry["fbx_path"]),
        }
        edificios.append(e_record)
        by_quality[quality].append(eid)
        by_zona[zona].append(eid)

    report = {
        "status":               "done" if edificios else "pending",
        "generated":            datetime.now().isoformat(),
        "total_fbx":            len(fbx_data),
        "total_with_albedo":    sum(1 for e in edificios if e["albedo_ok"]),
        "total_with_albedo_4k": sum(1 for e in edificios if e.get("albedo_4k_ok", False)),
        "total_with_normal":    sum(1 for e in edificios if e["normal_ok"]),
        "total_with_ao":        sum(1 for e in edificios if e["ao_ok"]),
        "buildings_updated_in_fusion": updated_count,
        "by_quality": {
            "high":   len(by_quality["high"]),
            "medium": len(by_quality["medium"]),
            "low":    len(by_quality["low"]),
        },
        "by_zona": {zona: len(ids) for zona, ids in sorted(by_zona.items())},
        "edificios": edificios,
        "paths": {
            "fbx_dir":          unity_path(FBX_DIR),
            "texture_dir":      unity_path(TEX_DIR),
            "buildings_fusion": unity_path(FUSION_JSON),
        },
        "unity_integration": {
            "script_to_use":    "ImportadorEdificiosFBX.cs",
            "priority_field":   "photogrammetry_fbx",
            "fallback":         "SistemaEdificiosAAA.cs arquetipos procedurales",
            "lod_convention":   "{id}_LOD0 ... {id}_LOD3",
            "normal_space":     "DirectX (Unity HDRP compatible)",
        },
    }

    if not dry_run:
        with open(REPORT_JSON, "w", encoding="utf-8") as f:
            json.dump(report, f, indent=2, ensure_ascii=False)
        log(f"photogrammetry_report.json guardado: {REPORT_JSON}", "OK")
    else:
        log("[DRY-RUN] photogrammetry_report.json NO guardado")

    return report


# ─── IMPRIMIR RESUMEN ────────────────────────────────────────────────────────

def print_summary(report: dict):
    print("\n" + "═" * 70)
    print("  UNITY PHOTOGRAMMETRY IMPORTER — Altsasu Manifa")
    print("═" * 70)
    print(f"  FBX procesados        : {report['total_fbx']}")
    print(f"  Con albedo texture    : {report['total_with_albedo']}")
    print(f"  Con albedo 4K (ESRGAN): {report.get('total_with_albedo_4k', 0)}")
    print(f"  Con normal map        : {report['total_with_normal']}")
    print(f"  Con AO map            : {report['total_with_ao']}")
    print(f"  Actualizados en fusion: {report['buildings_updated_in_fusion']}")
    print()
    print(f"  Calidad:")
    q = report["by_quality"]
    print(f"    high   : {q.get('high', 0)}")
    print(f"    medium : {q.get('medium', 0)}")
    print(f"    low    : {q.get('low', 0)}")
    print()
    print(f"  Por zona:")
    for zona, n in sorted(report["by_zona"].items()):
        print(f"    {zona:<22}: {n} edificios")
    print()

    if report["edificios"]:
        print("  EDIFICIOS CON FOTOGRAMETRÍA:")
        for e in report["edificios"]:
            tris = f"{e['tris_lod0']:,}" if e.get("tris_lod0") else "N/A"
            qual = e["quality"]
            maps = ("A" if e["albedo_ok"] else "·") + \
                   ("N" if e["normal_ok"] else "·") + \
                   ("O" if e["ao_ok"] else "·")
            print(f"    [{qual:6s}] [{maps}]  {e['edificio_id']:<20}  "
                  f"{e['n_fotos']:>3} fotos  LOD0={tris} tris")

    print()
    print(f"  Reporte : {REPORT_JSON}")
    print(f"  Fusion  : {FUSION_JSON}")
    print("═" * 70 + "\n")
    print("  INTEGRACIÓN UNITY:")
    print("  Añade campo 'photogrammetry_fbx' en buildings_fusion_final.json.")
    print("  ImportadorEdificiosFBX.cs debe leer este campo y priorizar el FBX")
    print("  fotogramétrico sobre el arquetipo procedural de SistemaEdificiosAAA.")
    print("  LODs: {id}_LOD0..LOD3 dentro del FBX (Unity LODGroup automático).")
    print("  Normal maps: espacio DirectX, flip G ya aplicado.\n")


# ─── MAIN ─────────────────────────────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser(
        description="Unity photogrammetry importer — Altsasu Manifa"
    )
    parser.add_argument("--dry-run",        action="store_true",
                        help="Muestra qué se haría sin modificar archivos")
    parser.add_argument("--report-only",   action="store_true",
                        help="Solo genera photogrammetry_report.json, sin tocar buildings_fusion_final.json")
    parser.add_argument("--compute-metrics", action="store_true",
                        help="Calcula métricas de calidad completas (requiere PIL o opencv-python)")
    args = parser.parse_args()

    print("\n" + "═" * 70)
    print("  UNITY PHOTOGRAMMETRY IMPORTER — Altsasu Manifa")
    print(f"  {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
    if args.dry_run:
        print("  MODO: DRY-RUN (no se modificarán archivos)")
    print("═" * 70 + "\n")

    # 1. Escanear FBX generados
    log("Escaneando FBX fotogramétricos...")
    fbx_data = scan_photogrammetry_fbx()
    if not fbx_data:
        log("No se encontraron FBX fotogramétricos. Ejecuta primero meshroom_pipeline.py", "WARN")
        # Igualmente generar reporte vacío
        report = {"status": "pending", "edificios": [], "total_fbx": 0}
        if not args.dry_run:
            with open(REPORT_JSON, "w", encoding="utf-8") as f:
                json.dump(report, f, indent=2)
        sys.exit(0)

    # 2. Cargar conteos de fotos para calcular calidad
    log("Cargando photo_building_mapping.json...")
    photo_counts = load_photo_counts()

    # 3. Actualizar buildings_fusion_final.json
    updated_count = 0
    if not args.report_only:
        log("Actualizando buildings_fusion_final.json...")
        if args.compute_metrics:
            log("  Calculando métricas de calidad completas (--compute-metrics)...", "INFO")
        buildings_map = load_buildings_fusion()
        updated_count = update_buildings_fusion(
            buildings_map, fbx_data, photo_counts,
            dry_run=args.dry_run,
            compute_metrics=args.compute_metrics,
        )

    # 4. Generar reporte completo
    log("Generando photogrammetry_report.json...")
    report = generate_report(fbx_data, photo_counts, updated_count, dry_run=args.dry_run)

    # 5. Resumen
    print_summary(report)


if __name__ == "__main__":
    main()
