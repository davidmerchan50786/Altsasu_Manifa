#!/usr/bin/env python3
"""
MESHROOM PHOTOGRAMMETRY PIPELINE — Altsasu Manifa
Genera mallas 3D texturizadas desde fotos Street View reales.
Procesa cada zona por separado para obtener reconstrucciones precisas.

Uso:
    python Tools/meshroom_pipeline.py --zona iglesia
    python Tools/meshroom_pipeline.py --all
    python Tools/meshroom_pipeline.py --all --gpu

Rutas Windows esperadas (ejecutar desde E:\\DAM\\Altsasu_Manifa):
    Meshroom:  E:\\Meshroom\\Meshroom-2025.1.0\\meshroom_batch.exe
    Blender:   C:\\Program Files\\Blender Foundation\\Blender 5.1\\blender.exe
    Cache:     E:\\MeshroomCache\\
"""

import argparse
import json
import os
import platform
import shutil
import subprocess
import sys
import time
from collections import defaultdict
from datetime import datetime
from pathlib import Path

# ─────────────────────────── CONFIGURACIÓN ───────────────────────────────────

# Detectar si estamos en Windows o Linux (CI/dev en Linux, ejecución real en Windows)
IS_WINDOWS = platform.system() == "Windows"

# Rutas absolutas de herramientas (Windows)
MESHROOM_BATCH = Path(r"E:\Meshroom\Meshroom-2025.1.0\meshroom_batch.exe")
BLENDER_EXE    = Path(r"C:\Program Files\Blender Foundation\Blender 5.1\blender.exe")

# Rutas del proyecto (relativas al script → resolvemos desde __file__)
SCRIPT_DIR   = Path(__file__).resolve().parent          # Tools/
PROJECT_ROOT = SCRIPT_DIR.parent                         # Altsasu_Manifa/

PHOTO_MAPPING_JSON    = PROJECT_ROOT / "Assets" / "AlsasuaData" / "photo_building_mapping.json"
PROCESSED_PHOTOS_DIR  = PROJECT_ROOT / "Assets" / "AlsasuaData" / "FacadeTextures" / "Processed"
OUTPUT_FBX_DIR        = PROJECT_ROOT / "Assets" / "Models" / "Buildings_Photogrammetry"
OUTPUT_TEX_DIR        = PROJECT_ROOT / "Assets" / "AlsasuaData" / "FacadeTextures" / "Photogrammetry"
BLENDER_CLEANUP_SCRIPT = SCRIPT_DIR / "blender_photogrammetry_cleanup.py"

# Rutas de caché en E:\ (mucho espacio, fuera del repo)
CACHE_ROOT   = Path(r"E:\MeshroomCache")
INPUT_ROOT   = CACHE_ROOT / "input"
MESHROOM_CACHE = CACHE_ROOT / "cache"
PROGRESS_FILE  = CACHE_ROOT / "progress.json"

# Orden de prioridad de zonas (edificios más importantes primero)
ZONA_PRIORITY = ["iglesia", "ayto", "plaza_fueros", "casco_viejo",
                 "ferial", "gaztetxe", "plaza_zubeztia", "garcia_jimenez"]

# Parámetros Meshroom optimizados para fachadas urbanas
MESHROOM_OVERRIDES = {
    "FeatureExtraction": {
        "describerTypes": ["sift", "akaze"],
        "describerPreset": "high",
        "maxNbFeatures": 10000
    },
    "FeatureMatching": {
        "geometricEstimator": "acransac",
        "distanceRatio": 0.8
    },
    "StructureFromMotion": {
        "minAngleForLandmark": 1.0,
        "minNumberOfObservationsForTriangulation": 2
    },
    "Texturing": {
        "textureSide": 8192,
        "unwrapMethod": "ABF",
        "fillHoles": True,
        "padding": 8
    },
    "Meshing": {
        "maxInputPoints": 50000000,
        "maxPoints": 2000000,
        "angleFactor": 2.0,
        "simFactor": 0.0
    },
    "MeshFiltering": {
        "keepLargestMeshOnly": True,
        "smoothingSubset": "all",
        "smoothingIterations": 5
    }
}

# ─────────────────────────── UTILIDADES ──────────────────────────────────────

def log(msg: str, level: str = "INFO"):
    ts = datetime.now().strftime("%H:%M:%S")
    prefix = {"INFO": "[ ]", "OK": "[+]", "WARN": "[!]", "ERR": "[x]"}.get(level, "[ ]")
    print(f"  {ts}  {prefix}  {msg}", flush=True)


def load_progress() -> dict:
    if PROGRESS_FILE.exists():
        with open(PROGRESS_FILE, "r", encoding="utf-8") as f:
            return json.load(f)
    return {}


def save_progress(progress: dict):
    PROGRESS_FILE.parent.mkdir(parents=True, exist_ok=True)
    with open(PROGRESS_FILE, "w", encoding="utf-8") as f:
        json.dump(progress, f, indent=2, ensure_ascii=False)


def ensure_dirs():
    for d in [CACHE_ROOT, INPUT_ROOT, MESHROOM_CACHE, OUTPUT_FBX_DIR, OUTPUT_TEX_DIR]:
        d.mkdir(parents=True, exist_ok=True)


# ─────────────────────────── LECTURA DE MAPEO ────────────────────────────────

def load_building_photo_map() -> dict:
    """
    Lee photo_building_mapping.json y agrupa fotos por edificio_id.
    Retorna: {edificio_id: {"zona": str, "fotos": [Path, ...]}}
    """
    if not PHOTO_MAPPING_JSON.exists():
        log(f"No se encuentra {PHOTO_MAPPING_JSON}", "ERR")
        sys.exit(1)

    with open(PHOTO_MAPPING_JSON, "r", encoding="utf-8") as f:
        data = json.load(f)

    mappings = data.get("mappings", [])
    buildings: dict = defaultdict(lambda: {"zona": "", "fotos": []})

    for entry in mappings:
        eid   = entry.get("edificio_id", "unknown")
        zona  = entry.get("zona", "")
        foto  = Path(entry.get("foto_procesada", ""))

        # Ruta puede ser absoluta (Linux dev) o relativa — normalizamos
        if not foto.is_absolute():
            foto = PROJECT_ROOT / foto

        if foto.exists():
            buildings[eid]["zona"] = zona
            buildings[eid]["fotos"].append(foto)
        else:
            # Intentar buscar en Processed/ por nombre de archivo
            candidate = PROCESSED_PHOTOS_DIR / foto.name
            if candidate.exists():
                buildings[eid]["zona"] = zona
                buildings[eid]["fotos"].append(candidate)

    return dict(buildings)


def load_zone_photo_map() -> dict:
    """
    Alternativa: agrupa fotos de Processed/ directamente por zona (prefijo del nombre).
    Retorna: {zona: [Path, ...]}
    """
    zones: dict = defaultdict(list)
    if not PROCESSED_PHOTOS_DIR.exists():
        return {}
    for p in sorted(PROCESSED_PHOTOS_DIR.glob("*.png")):
        # Nombre: {zona}_{NNN}.png
        parts = p.stem.rsplit("_", 1)
        if len(parts) == 2:
            zona = parts[0]
            zones[zona].append(p)
    return dict(zones)


# ─────────────────────────── MESHROOM ────────────────────────────────────────

def run_meshroom(fotos: list, edificio_id: str, use_gpu: bool) -> Path | None:
    """
    Ejecuta meshroom_batch para un edificio.
    Retorna la ruta al .obj texturizado, o None si falla.
    """
    if not IS_WINDOWS:
        log(f"[SIMULADO] Meshroom no disponible en Linux. Edificio: {edificio_id}", "WARN")
        return None

    if not MESHROOM_BATCH.exists():
        log(f"meshroom_batch.exe no encontrado en {MESHROOM_BATCH}", "ERR")
        return None

    # Preparar directorio de entrada
    fotos_dir  = INPUT_ROOT / edificio_id / "images"
    output_dir = CACHE_ROOT / "output" / edificio_id
    fotos_dir.mkdir(parents=True, exist_ok=True)
    output_dir.mkdir(parents=True, exist_ok=True)

    # Copiar fotos al directorio temporal
    for foto in fotos:
        dest = fotos_dir / foto.name
        if not dest.exists():
            shutil.copy2(foto, dest)

    overrides = dict(MESHROOM_OVERRIDES)
    if not use_gpu:
        # Deshabilitar GPU en Meshing si no se solicita
        overrides.setdefault("Meshing", {})["useGPU"] = False

    cmd = [
        str(MESHROOM_BATCH),
        "--input",   str(fotos_dir),
        "--output",  str(output_dir),
        "--cache",   str(MESHROOM_CACHE),
        "--overrides", json.dumps(overrides),
    ]

    log(f"Ejecutando Meshroom para '{edificio_id}' ({len(fotos)} fotos)...")
    t0 = time.time()
    try:
        result = subprocess.run(
            cmd,
            capture_output=True,
            text=True,
            timeout=3600  # 1 hora máximo por edificio
        )
        elapsed = time.time() - t0
        if result.returncode != 0:
            log(f"Meshroom falló para '{edificio_id}': {result.stderr[-500:]}", "ERR")
            return None
        log(f"Meshroom completado en {elapsed:.0f}s para '{edificio_id}'", "OK")
    except subprocess.TimeoutExpired:
        log(f"Meshroom timeout para '{edificio_id}'", "ERR")
        return None
    except Exception as e:
        log(f"Error ejecutando Meshroom: {e}", "ERR")
        return None

    # Buscar .obj en output_dir/texturing/
    texturing_dir = output_dir / "texturing"
    obj_files = list(texturing_dir.glob("*.obj")) if texturing_dir.exists() else []
    if not obj_files:
        log(f"No se encontró .obj en {texturing_dir}", "ERR")
        return None

    return obj_files[0]


# ─────────────────────────── BLENDER POST-PROCESO ────────────────────────────

def run_blender_cleanup(obj_path: Path, edificio_id: str, use_gpu: bool) -> bool:
    """
    Llama a Blender --background para limpiar y optimizar la malla.
    Exporta FBX a Assets/Models/Buildings_Photogrammetry/{edificio_id}.fbx
    """
    if not IS_WINDOWS:
        log(f"[SIMULADO] Blender no disponible en Linux. Edificio: {edificio_id}", "WARN")
        return False

    if not BLENDER_EXE.exists():
        log(f"blender.exe no encontrado en {BLENDER_EXE}", "ERR")
        return False

    if not BLENDER_CLEANUP_SCRIPT.exists():
        log(f"Script Blender no encontrado: {BLENDER_CLEANUP_SCRIPT}", "ERR")
        return False

    fbx_out = OUTPUT_FBX_DIR / f"{edificio_id}.fbx"
    tex_out  = OUTPUT_TEX_DIR / f"{edificio_id}_albedo.png"

    cmd = [
        str(BLENDER_EXE),
        "--background",
        "--python", str(BLENDER_CLEANUP_SCRIPT),
        "--",
        "--obj",          str(obj_path),
        "--edificio-id",  edificio_id,
        "--fbx-out",      str(fbx_out),
        "--tex-out",      str(tex_out),
        "--gpu" if use_gpu else "--no-gpu",
    ]

    log(f"Ejecutando Blender cleanup para '{edificio_id}'...")
    try:
        result = subprocess.run(cmd, capture_output=True, text=True, timeout=1800)
        if result.returncode != 0:
            log(f"Blender falló para '{edificio_id}': {result.stderr[-500:]}", "ERR")
            return False
        log(f"Blender completado para '{edificio_id}'", "OK")
        return True
    except subprocess.TimeoutExpired:
        log(f"Blender timeout para '{edificio_id}'", "ERR")
        return False
    except Exception as e:
        log(f"Error ejecutando Blender: {e}", "ERR")
        return False


# ─────────────────────────── CALIDAD ─────────────────────────────────────────

def estimate_quality(fotos: list) -> dict:
    """
    Estima la calidad de la reconstrucción basándose en número de fotos.
    """
    n = len(fotos)
    if n >= 8:
        quality = "high"
    elif n >= 4:
        quality = "medium"
    else:
        quality = "low"

    # Cobertura angular aproximada (asumimos ~15° entre tomas consecutivas de Street View)
    angular_coverage = min(360, n * 15)

    return {
        "n_fotos": n,
        "quality": quality,
        "angular_coverage_deg": angular_coverage,
    }


# ─────────────────────────── PIPELINE PRINCIPAL ──────────────────────────────

def process_building(edificio_id: str, info: dict, progress: dict, use_gpu: bool) -> dict:
    """
    Procesa un único edificio: Meshroom → Blender → copy assets.
    Retorna entrada de progreso actualizada.
    """
    fotos = info["fotos"]
    zona  = info["zona"]
    quality_info = estimate_quality(fotos)

    if progress.get(edificio_id, {}).get("status") == "done":
        log(f"'{edificio_id}' ya procesado → skip", "OK")
        return progress.get(edificio_id, {})

    log(f"─── Procesando edificio: {edificio_id} ({zona}, {len(fotos)} fotos) ───")

    progress[edificio_id] = {
        "status": "processing",
        "zona": zona,
        "started_at": datetime.now().isoformat(),
        **quality_info
    }
    save_progress(progress)

    # Paso 1: Meshroom
    obj_path = run_meshroom(fotos, edificio_id, use_gpu)

    if obj_path is None and IS_WINDOWS:
        progress[edificio_id]["status"] = "failed"
        progress[edificio_id]["error"] = "Meshroom no generó .obj"
        save_progress(progress)
        return progress[edificio_id]

    # Paso 2: Blender cleanup
    blender_ok = False
    if obj_path is not None:
        blender_ok = run_blender_cleanup(obj_path, edificio_id, use_gpu)

    fbx_path = OUTPUT_FBX_DIR / f"{edificio_id}.fbx"
    tex_path  = OUTPUT_TEX_DIR / f"{edificio_id}_albedo.png"

    progress[edificio_id].update({
        "status": "done" if (not IS_WINDOWS or blender_ok) else "failed",
        "fbx": str(fbx_path) if fbx_path.exists() else None,
        "albedo_tex": str(tex_path) if tex_path.exists() else None,
        "completed_at": datetime.now().isoformat(),
        **quality_info
    })
    save_progress(progress)
    return progress[edificio_id]


def process_zona(zona: str, building_map: dict, zone_map: dict,
                 progress: dict, use_gpu: bool) -> list:
    """
    Procesa todos los edificios de una zona.
    Si no hay mapeo por edificio, agrupa todas las fotos de la zona como un único "edificio".
    Retorna lista de resultados.
    """
    results = []

    # Filtrar edificios de esta zona
    zona_buildings = {eid: info for eid, info in building_map.items()
                      if info["zona"] == zona and len(info["fotos"]) >= 3}

    if zona_buildings:
        for eid, info in zona_buildings.items():
            res = process_building(eid, info, progress, use_gpu)
            results.append({"edificio_id": eid, **res})
    else:
        # Fallback: usar todas las fotos de la zona como un grupo
        fotos = zone_map.get(zona, [])
        if len(fotos) < 3:
            log(f"Zona '{zona}': solo {len(fotos)} fotos — mínimo 3 requeridas, skip", "WARN")
            return []
        pseudo_id = f"zona_{zona}"
        info = {"zona": zona, "fotos": fotos}
        res = process_building(pseudo_id, info, progress, use_gpu)
        results.append({"edificio_id": pseudo_id, **res})

    return results


# ─────────────────────────── RESUMEN FINAL ───────────────────────────────────

def print_summary(all_results: list):
    print("\n" + "═" * 60)
    print("  RESUMEN PIPELINE FOTOGRAMETRÍA — Altsasu Manifa")
    print("═" * 60)

    total    = len(all_results)
    done     = sum(1 for r in all_results if r.get("status") == "done")
    failed   = sum(1 for r in all_results if r.get("status") == "failed")
    simulated = sum(1 for r in all_results if r.get("status") == "processing")

    print(f"  Edificios procesados : {total}")
    print(f"  Exitosos             : {done}")
    print(f"  Fallidos             : {failed}")
    if simulated:
        print(f"  Simulados (no-Win)   : {simulated}")
    print()

    for r in all_results:
        eid    = r.get("edificio_id", "?")
        status = r.get("status", "?")
        n_fotos = r.get("n_fotos", 0)
        quality = r.get("quality", "?")
        fbx     = r.get("fbx", "—")
        icon    = "+" if status == "done" else ("~" if status == "processing" else "x")
        print(f"  [{icon}] {eid:<25} | {n_fotos:>3} fotos | {quality:<6} | {fbx or '—'}")

    print("═" * 60 + "\n")


# ─────────────────────────── MAIN ────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser(
        description="Pipeline fotogrametría Meshroom para Altsasu Manifa"
    )
    parser.add_argument("--zona", default="", help="Procesar solo esta zona")
    parser.add_argument("--all",  action="store_true", help="Procesar todas las zonas")
    parser.add_argument("--gpu",  action="store_true", help="Usar GPU en Meshroom y Blender")
    parser.add_argument("--force", action="store_true", help="Reprocesar aunque esté done")
    args = parser.parse_args()

    if not args.zona and not args.all:
        parser.print_help()
        sys.exit(1)

    print("\n" + "═" * 60)
    print("  MESHROOM PIPELINE — Altsasu Manifa")
    print(f"  {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
    print(f"  Plataforma: {platform.system()}")
    print(f"  GPU: {'SI' if args.gpu else 'NO'}")
    print("═" * 60 + "\n")

    ensure_dirs()

    # Cargar datos
    log("Cargando mapeo de fotos por edificio...")
    building_map = load_building_photo_map()
    zone_map     = load_zone_photo_map()

    log(f"Edificios mapeados: {len(building_map)}")
    log(f"Zonas disponibles: {sorted(zone_map.keys())}")

    progress = {} if args.force else load_progress()
    all_results = []

    if args.all:
        # Procesar en orden de prioridad
        zonas_a_procesar = [z for z in ZONA_PRIORITY if z in zone_map]
        # Añadir zonas no priorizadas al final
        for z in sorted(zone_map.keys()):
            if z not in zonas_a_procesar:
                zonas_a_procesar.append(z)

        log(f"Procesando {len(zonas_a_procesar)} zonas en orden: {zonas_a_procesar}")
        for zona in zonas_a_procesar:
            log(f"\n{'─'*40}")
            log(f"ZONA: {zona.upper()}")
            results = process_zona(zona, building_map, zone_map, progress, args.gpu)
            all_results.extend(results)
    else:
        zona = args.zona.lower()
        if zona not in zone_map and not any(
            info["zona"] == zona for info in building_map.values()
        ):
            log(f"Zona '{zona}' no encontrada. Disponibles: {sorted(zone_map.keys())}", "ERR")
            sys.exit(1)
        results = process_zona(zona, building_map, zone_map, progress, args.gpu)
        all_results.extend(results)

    print_summary(all_results)

    # Guardar reporte en el proyecto
    report_path = PROJECT_ROOT / "Assets" / "AlsasuaData" / "photogrammetry_report.json"
    existing = {}
    if report_path.exists():
        with open(report_path, "r", encoding="utf-8") as f:
            existing = json.load(f)

    existing.update({
        "status": "done",
        "last_run": datetime.now().isoformat(),
        "edificios": all_results,
        "total": len(all_results),
        "done": sum(1 for r in all_results if r.get("status") == "done"),
    })
    with open(report_path, "w", encoding="utf-8") as f:
        json.dump(existing, f, indent=2, ensure_ascii=False)

    log(f"Reporte guardado en {report_path}", "OK")


if __name__ == "__main__":
    main()
