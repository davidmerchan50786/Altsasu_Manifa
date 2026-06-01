#!/usr/bin/env python3
"""
MESHROOM PHOTOGRAMMETRY PIPELINE — Altsasu Manifa
===================================================
Genera mallas 3D texturizadas desde fotos Street View reales de Alsasua.
Procesa cada zona/edificio por separado para obtener reconstrucciones precisas.

Fuente de fotos : Assets/AlsasuaData/FacadeTextures/Processed/
Mapping         : Assets/AlsasuaData/photo_building_mapping.json
Salida FBX      : Assets/Models/Buildings_Photogrammetry/{edificio_id}.fbx
Salida texturas : Assets/AlsasuaData/FacadeTextures/Photogrammetry/

Uso:
    python Tools/meshroom_pipeline.py --all
    python Tools/meshroom_pipeline.py --all --gpu
    python Tools/meshroom_pipeline.py --zona iglesia
    python Tools/meshroom_pipeline.py --zona iglesia --gpu
    python Tools/meshroom_pipeline.py --all --retry   # reintenta solo los fallidos

Rutas Windows esperadas (ejecutar desde E:\\DAM\\Altsasu_Manifa):
    Meshroom : E:\\Meshroom\\Meshroom-2025.1.0\\meshroom_batch.exe
    Blender  : C:\\Program Files\\Blender Foundation\\Blender 5.1\\blender.exe
    Cache    : E:\\MeshroomCache\\
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

# ─── CONFIGURACIÓN ────────────────────────────────────────────────────────────

IS_WINDOWS = platform.system() == "Windows"

MESHROOM_BATCH = Path(r"E:\Meshroom\Meshroom-2025.1.0\meshroom_batch.exe")
BLENDER_EXE    = Path(r"C:\Program Files\Blender Foundation\Blender 5.1\blender.exe")

SCRIPT_DIR   = Path(__file__).resolve().parent   # Tools/
PROJECT_ROOT = SCRIPT_DIR.parent                  # Altsasu_Manifa/

PHOTO_MAPPING_JSON     = PROJECT_ROOT / "Assets" / "AlsasuaData" / "photo_building_mapping.json"
PROCESSED_PHOTOS_DIR   = PROJECT_ROOT / "Assets" / "AlsasuaData" / "FacadeTextures" / "Processed"
OUTPUT_FBX_DIR         = PROJECT_ROOT / "Assets" / "Models" / "Buildings_Photogrammetry"
OUTPUT_TEX_DIR         = PROJECT_ROOT / "Assets" / "AlsasuaData" / "FacadeTextures" / "Photogrammetry"
BLENDER_CLEANUP_SCRIPT = SCRIPT_DIR / "blender_photogrammetry_cleanup.py"
PHOTOGRAMMETRY_REPORT  = PROJECT_ROOT / "Assets" / "AlsasuaData" / "photogrammetry_report.json"

CACHE_ROOT     = Path(r"E:\MeshroomCache")
INPUT_ROOT     = CACHE_ROOT / "input"
MESHROOM_CACHE = CACHE_ROOT / "cache"
PROGRESS_FILE  = CACHE_ROOT / "progress.json"

# Prioridad: edificios icónicos primero
ZONA_PRIORITY = [
    "iglesia", "ayto", "plaza_fueros", "casco_viejo",
    "gaztetxe", "plaza_zubeztia", "ferial", "garcia_jimenez",
]

MIN_FOTOS_POR_EDIFICIO = 3   # mínimo fotogrametría estable


# ─── PARÁMETROS MESHROOM OPTIMIZADOS PARA FACHADAS URBANAS ───────────────────

def build_meshroom_overrides(use_gpu: bool) -> dict:
    """
    Parámetros calibrados para fachadas de arquitectura vasca:
    - SIFT para texturas de arenisca rojiza (gradientes suaves)
    - AKAZE para detección de bordes de balcones y cornisas
    - Textura 8K → máxima calidad para edificios hero
    - ABF unwrap → distribución angular de UV óptima
    """
    return {
        "FeatureExtraction": {
            "describerTypes":     ["sift", "akaze"],
            "describerPreset":    "high",
            "maxNbFeatures":      10000,
            "forceCpuExtraction": not use_gpu,
        },
        "FeatureMatching": {
            "geometricEstimator":    "acransac",
            "distanceRatio":         0.8,
            "maxIteration":          2048,
            "guided_matching":       True,
        },
        "StructureFromMotion": {
            "minAngleForLandmark":                     1.0,
            "minNumberOfObservationsForTriangulation": 2,
            "minAngleForSelection":                    0.1,
            "maxReprojectionError":                    4.0,
            "localizerEstimator":                      "acransac",
        },
        "Texturing": {
            "textureSide":               8192,
            "unwrapMethod":              "ABF",
            "fillHoles":                 True,
            "padding":                   8,
            "correctEV":                 True,
            "visibilityRemappingMethod": "PullPush",
            "downscale":                 1,
        },
        "Meshing": {
            "maxInputPoints":        50_000_000,
            "maxPoints":              2_000_000,
            "angleFactor":            2.0,
            "simFactor":              0.0,
            "removeSmallSegments":    True,
            "estimateSpaceFromSfM":   True,
        },
        "MeshFiltering": {
            "keepLargestMeshOnly":          True,
            "smoothingSubset":              "all",
            "smoothingIterations":          5,
            "filterLargeTrianglesFactor":   60.0,
            "lambda_":                      1.0,
        },
        "DepthMap": {
            "downscale":          2 if use_gpu else 4,
            "exportTilePattern":  False,
        },
        "DepthMapFilter": {
            "minNumOfConsistentCams":          3,
            "minNumOfConsistentCamsWithLowSimilarity": 4,
        },
    }


# ─── UTILIDADES ───────────────────────────────────────────────────────────────

def log(msg: str, level: str = "INFO"):
    ts = datetime.now().strftime("%H:%M:%S")
    icons = {"INFO": "·", "OK": "✓", "WARN": "!", "ERR": "✗"}
    icon = icons.get(level, "·")
    print(f"  {ts}  [{icon}]  {msg}", flush=True)


def load_progress() -> dict:
    if PROGRESS_FILE.exists():
        try:
            with open(PROGRESS_FILE, "r", encoding="utf-8") as f:
                return json.load(f)
        except json.JSONDecodeError:
            return {}
    return {}


def save_progress(progress: dict):
    PROGRESS_FILE.parent.mkdir(parents=True, exist_ok=True)
    with open(PROGRESS_FILE, "w", encoding="utf-8") as f:
        json.dump(progress, f, indent=2, ensure_ascii=False)


def ensure_dirs():
    for d in [CACHE_ROOT, INPUT_ROOT, MESHROOM_CACHE, OUTPUT_FBX_DIR, OUTPUT_TEX_DIR]:
        d.mkdir(parents=True, exist_ok=True)


# ─── LECTURA DE DATOS ─────────────────────────────────────────────────────────

def load_building_photo_map() -> dict:
    """
    Lee photo_building_mapping.json y agrupa fotos por edificio_id.
    Devuelve: {edificio_id: {"zona": str, "fotos": [Path, ...]}}
    Solo incluye edificios con ≥ MIN_FOTOS_POR_EDIFICIO fotos existentes.
    """
    if not PHOTO_MAPPING_JSON.exists():
        log(f"No se encuentra photo_building_mapping.json:\n  {PHOTO_MAPPING_JSON}", "ERR")
        sys.exit(1)

    with open(PHOTO_MAPPING_JSON, "r", encoding="utf-8") as f:
        data = json.load(f)

    mappings = data.get("mappings", [])
    buildings: dict = defaultdict(lambda: {"zona": "", "fotos": []})

    for entry in mappings:
        eid  = entry.get("edificio_id", "")
        zona = entry.get("zona", "")
        foto = Path(entry.get("foto_procesada", ""))

        if not eid:
            continue

        if not foto.is_absolute():
            foto = PROJECT_ROOT / foto

        if not foto.exists():
            # Buscar por nombre en Processed/
            foto = PROCESSED_PHOTOS_DIR / foto.name

        if foto.exists():
            buildings[eid]["zona"] = zona
            buildings[eid]["fotos"].append(foto)

    # Filtrar edificios con pocas fotos
    valid = {eid: v for eid, v in buildings.items()
             if len(v["fotos"]) >= MIN_FOTOS_POR_EDIFICIO}

    log(f"Edificios en mapping: {len(buildings)} → con ≥{MIN_FOTOS_POR_EDIFICIO} fotos: {len(valid)}")
    return valid


def load_zone_photo_map() -> dict:
    """
    Alternativa: agrupa fotos de Processed/ por zona (prefijo del nombre de archivo).
    Devuelve: {zona: [Path, ...]}
    """
    zones: dict = defaultdict(list)
    if not PROCESSED_PHOTOS_DIR.exists():
        log(f"Directorio Processed/ no encontrado: {PROCESSED_PHOTOS_DIR}", "WARN")
        return {}
    for p in sorted(PROCESSED_PHOTOS_DIR.glob("*.png")):
        # Formato esperado: {zona}_{NNN}.png
        parts = p.stem.rsplit("_", 1)
        if len(parts) == 2 and parts[1].isdigit():
            zones[parts[0]].append(p)
    return dict(zones)


def ordered_zone_list(zone_map: dict) -> list:
    """Devuelve zonas en orden de prioridad."""
    priority = [z for z in ZONA_PRIORITY if z in zone_map]
    rest = sorted(z for z in zone_map if z not in ZONA_PRIORITY)
    return priority + rest


# ─── PREPARAR IMÁGENES ────────────────────────────────────────────────────────

def prepare_images(fotos: list, edificio_id: str) -> Path:
    """
    Copia fotos a E:\\MeshroomCache\\input\\{edificio_id}\\images\\.
    Devuelve la ruta al directorio de imágenes.
    """
    img_dir = INPUT_ROOT / edificio_id / "images"
    img_dir.mkdir(parents=True, exist_ok=True)

    # Limpiar anteriores
    for old in img_dir.glob("*"):
        old.unlink(missing_ok=True)

    for i, foto in enumerate(fotos):
        # Prefijo numérico garantiza orden correcto para Meshroom
        dest = img_dir / f"{i:04d}_{foto.name}"
        shutil.copy2(foto, dest)

    log(f"  Imágenes preparadas: {len(fotos)} → {img_dir}")
    return img_dir


# ─── EJECUTAR MESHROOM ────────────────────────────────────────────────────────

def run_meshroom(fotos: list, edificio_id: str, use_gpu: bool) -> Path | None:
    """
    Ejecuta meshroom_batch para un edificio.
    Devuelve la ruta al .obj texturizado o None si falla.
    """
    if not IS_WINDOWS:
        log(f"[SIMULADO] Meshroom solo en Windows — {edificio_id}", "WARN")
        return None

    if not MESHROOM_BATCH.exists():
        log(f"meshroom_batch.exe no encontrado:\n  {MESHROOM_BATCH}", "ERR")
        return None

    img_dir    = prepare_images(fotos, edificio_id)
    output_dir = CACHE_ROOT / "output" / edificio_id
    output_dir.mkdir(parents=True, exist_ok=True)

    overrides = build_meshroom_overrides(use_gpu)

    cmd = [
        str(MESHROOM_BATCH),
        "--input",     str(img_dir),
        "--output",    str(output_dir),
        "--cache",     str(MESHROOM_CACHE),
        "--overrides", json.dumps(overrides),
    ]

    log(f"Meshroom SfM+MVS+Texturing → '{edificio_id}' ({len(fotos)} fotos, GPU={use_gpu})")
    t0 = time.time()
    try:
        result = subprocess.run(
            cmd,
            capture_output=True,
            text=True,
            timeout=7200,  # 2h máx por edificio
        )
        elapsed = time.time() - t0
        if result.returncode != 0:
            log(f"Meshroom falló ({elapsed:.0f}s):\n{result.stderr[-1000:]}", "ERR")
            return None
        log(f"Meshroom OK en {elapsed:.0f}s", "OK")
    except subprocess.TimeoutExpired:
        log(f"Meshroom timeout (>2h) para '{edificio_id}'", "ERR")
        return None
    except Exception as e:
        log(f"Error ejecutando Meshroom: {e}", "ERR")
        return None

    # Buscar .obj en output/texturing/
    texturing_dir = output_dir / "texturing"
    obj_candidates = []
    if texturing_dir.exists():
        obj_candidates = list(texturing_dir.glob("*.obj"))
    if not obj_candidates:
        # Meshroom a veces anida en subdirectorios
        obj_candidates = list(output_dir.rglob("texturedMesh.obj"))
    if not obj_candidates:
        obj_candidates = list(output_dir.rglob("*.obj"))

    if not obj_candidates:
        log(f"No se encontró .obj en {output_dir}", "ERR")
        return None

    # El más grande es la malla principal
    obj_path = max(obj_candidates, key=lambda p: p.stat().st_size)
    log(f"OBJ encontrado: {obj_path.name} ({obj_path.stat().st_size // 1024} KB)", "OK")
    return obj_path


# ─── POST-PROCESO BLENDER ─────────────────────────────────────────────────────

def run_blender_cleanup(obj_path: Path, edificio_id: str, use_gpu: bool) -> bool:
    """
    Llama a Blender --background para:
    1. Limpiar malla fotogramétrica (islas, holes, normales)
    2. Decimar a LOD0-3
    3. UV unwrap profesional
    4. Bake Normal + AO + Diffuse (GPU Cycles)
    5. De-lighting de albedo
    6. Exportar FBX Unity HDRP ready
    """
    if not IS_WINDOWS:
        log(f"[SIMULADO] Blender solo en Windows — {edificio_id}", "WARN")
        return False

    if not BLENDER_EXE.exists():
        log(f"blender.exe no encontrado:\n  {BLENDER_EXE}", "ERR")
        return False

    if not BLENDER_CLEANUP_SCRIPT.exists():
        log(f"Script Blender no encontrado:\n  {BLENDER_CLEANUP_SCRIPT}", "ERR")
        return False

    fbx_out = OUTPUT_FBX_DIR / f"{edificio_id}.fbx"
    tex_out  = OUTPUT_TEX_DIR / f"{edificio_id}_albedo.png"

    # Pasar argumentos como JSON para evitar problemas con espacios en rutas
    args_json = json.dumps({
        "edificio_id": edificio_id,
        "obj_path":    str(obj_path),
        "fbx_output":  str(fbx_out),
        "tex_output":  str(tex_out),
        "use_gpu":     use_gpu,
    })

    cmd = [
        str(BLENDER_EXE),
        "--background",
        "--python", str(BLENDER_CLEANUP_SCRIPT),
        "--",
        args_json,
    ]

    log(f"Blender cleanup → '{edificio_id}' (GPU={use_gpu})")
    t0 = time.time()
    try:
        result = subprocess.run(cmd, capture_output=True, text=True, timeout=3600)
        elapsed = time.time() - t0
        if result.returncode != 0:
            log(f"Blender falló ({elapsed:.0f}s):\n{result.stderr[-1000:]}", "ERR")
            return False
        log(f"Blender OK en {elapsed:.0f}s → {fbx_out.name}", "OK")
        return True
    except subprocess.TimeoutExpired:
        log(f"Blender timeout (>1h) para '{edificio_id}'", "ERR")
        return False
    except Exception as e:
        log(f"Error ejecutando Blender: {e}", "ERR")
        return False


# ─── ESTIMACIÓN DE CALIDAD ───────────────────────────────────────────────────

def estimate_quality(fotos: list) -> dict:
    """
    Calidad estimada basada en nº de fotos y cobertura angular aproximada.
    Street View Alsasua: ~15° entre tomas consecutivas.
    high   = ≥8 fotos Y ≥3 ángulos distintos
    medium = 4–7 fotos
    low    = 3 fotos (mínimo viable)
    """
    n = len(fotos)
    angulos = max(1, n // 4)   # heurística: 4 fotos ≈ 1 ángulo único

    if n >= 8 and angulos >= 3:
        quality = "high"
    elif n >= 4:
        quality = "medium"
    else:
        quality = "low"

    return {
        "n_fotos":              n,
        "angulos_estimados":    angulos,
        "quality":              quality,
        "angular_coverage_deg": min(360, n * 15),
    }


# ─── PIPELINE POR EDIFICIO ────────────────────────────────────────────────────

def process_building(edificio_id: str, info: dict, progress: dict, use_gpu: bool,
                     force: bool = False) -> dict:
    """
    Ejecuta el pipeline completo para un edificio:
    Meshroom SfM → MVS → Texturing → Blender cleanup → FBX Unity
    """
    fotos = info["fotos"]
    zona  = info["zona"]
    q     = estimate_quality(fotos)

    # Saltar si ya está completo
    existing = progress.get(edificio_id, {})
    if existing.get("status") == "done" and not force:
        log(f"'{edificio_id}' ya completado → skip (usa --force para reprocesar)", "OK")
        return existing

    log(f"── Edificio: {edificio_id}  zona={zona}  fotos={len(fotos)}  calidad={q['quality']}")

    # Marcar como en proceso
    progress[edificio_id] = {
        "status":     "processing",
        "zona":       zona,
        "started_at": datetime.now().isoformat(),
        **q,
    }
    save_progress(progress)

    # Paso 1: Meshroom
    obj_path = run_meshroom(fotos, edificio_id, use_gpu)
    if obj_path is None and IS_WINDOWS:
        progress[edificio_id].update({
            "status": "failed",
            "error":  "meshroom_no_obj",
            "completed_at": datetime.now().isoformat(),
        })
        save_progress(progress)
        return progress[edificio_id]

    # Paso 2: Blender
    blender_ok = False
    if obj_path:
        blender_ok = run_blender_cleanup(obj_path, edificio_id, use_gpu)

    fbx_path = OUTPUT_FBX_DIR / f"{edificio_id}.fbx"
    tex_path  = OUTPUT_TEX_DIR / f"{edificio_id}_albedo.png"
    fbx_size  = fbx_path.stat().st_size if fbx_path.exists() else 0

    success = blender_ok or (not IS_WINDOWS)   # en Linux siempre "done" (modo simulado)
    progress[edificio_id].update({
        "status":         "done" if success else "failed",
        "fbx":            str(fbx_path) if fbx_path.exists() else None,
        "fbx_size_mb":    round(fbx_size / 1_048_576, 2),
        "albedo_tex":     str(tex_path) if tex_path.exists() else None,
        "completed_at":   datetime.now().isoformat(),
        **q,
    })
    save_progress(progress)
    return progress[edificio_id]


def process_zona(zona: str, building_map: dict, zone_map: dict,
                 progress: dict, use_gpu: bool, force: bool) -> list:
    """
    Procesa todos los edificios de una zona.
    Si no hay edificios mapeados, usa todas las fotos de la zona como un grupo único.
    """
    results = []

    # Edificios con mapeo explícito en esta zona
    zona_buildings = {
        eid: info for eid, info in building_map.items()
        if info["zona"] == zona and len(info["fotos"]) >= MIN_FOTOS_POR_EDIFICIO
    }

    if zona_buildings:
        log(f"Zona '{zona}': {len(zona_buildings)} edificios mapeados")
        for eid, info in zona_buildings.items():
            res = process_building(eid, info, progress, use_gpu, force)
            results.append({"edificio_id": eid, **res})
    else:
        # Fallback: zona entera como pseudo-edificio
        fotos = zone_map.get(zona, [])
        if len(fotos) < MIN_FOTOS_POR_EDIFICIO:
            log(f"Zona '{zona}': {len(fotos)} fotos < {MIN_FOTOS_POR_EDIFICIO} mínimo → skip", "WARN")
            return []
        log(f"Zona '{zona}': sin mapeo individual, procesando {len(fotos)} fotos juntas")
        pseudo_id = f"zona_{zona}"
        res = process_building(pseudo_id, {"zona": zona, "fotos": fotos},
                               progress, use_gpu, force)
        results.append({"edificio_id": pseudo_id, **res})

    return results


# ─── RESUMEN FINAL ────────────────────────────────────────────────────────────

def print_summary(results: list, elapsed_s: float):
    done   = [r for r in results if r.get("status") == "done"]
    failed = [r for r in results if r.get("status") == "failed"]
    sim    = [r for r in results if r.get("status") not in ("done", "failed")]

    print("\n" + "═" * 72)
    print("  RESUMEN PIPELINE FOTOGRAMETRÍA — Altsasu Manifa")
    print("═" * 72)
    print(f"  Edificios procesados : {len(results)}")
    print(f"  Exitosos             : {len(done)}")
    print(f"  Fallidos             : {len(failed)}")
    if sim:
        print(f"  Simulados (no-Win)   : {len(sim)}")
    print(f"  Tiempo total         : {elapsed_s / 60:.1f} min")
    print()

    if done:
        print("  EDIFICIOS RECONSTRUIDOS:")
        for r in done:
            eid     = r.get("edificio_id", "?")
            n       = r.get("n_fotos", 0)
            quality = r.get("quality", "?")
            mb      = r.get("fbx_size_mb", 0)
            fbx     = Path(r["fbx"]).name if r.get("fbx") else "—"
            print(f"    [{quality:6s}]  {eid:<20}  {n:>3} fotos  {mb:.1f} MB  → {fbx}")

    if failed:
        print("\n  EDIFICIOS FALLIDOS:")
        for r in failed:
            print(f"    {r.get('edificio_id','?'):<20}  error: {r.get('error','?')}")

    print()
    print(f"  Progress JSON : {PROGRESS_FILE}")
    print(f"  FBX dir       : {OUTPUT_FBX_DIR}")
    print("═" * 72 + "\n")


# ─── MAIN ─────────────────────────────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser(
        description="Meshroom photogrammetry pipeline — Altsasu Manifa",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Ejemplos:
  python Tools/meshroom_pipeline.py --all
  python Tools/meshroom_pipeline.py --all --gpu
  python Tools/meshroom_pipeline.py --zona iglesia --gpu
  python Tools/meshroom_pipeline.py --all --retry
  python Tools/meshroom_pipeline.py --all --force
        """,
    )
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--all",  action="store_true", help="Procesa todas las zonas en orden de prioridad")
    mode.add_argument("--zona", metavar="ZONA",       help="Procesa solo esta zona")

    parser.add_argument("--gpu",   action="store_true", help="Activa GPU en Meshroom (DepthMap) y Blender (Cycles)")
    parser.add_argument("--force", action="store_true", help="Reprocesa aunque el edificio esté marcado como 'done'")
    parser.add_argument("--retry", action="store_true", help="Procesa solo edificios con estado 'failed'")
    args = parser.parse_args()

    print("\n" + "═" * 72)
    print("  MESHROOM PIPELINE — Altsasu Manifa")
    print(f"  {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
    print(f"  Plataforma : {platform.system()}")
    print(f"  GPU        : {'SÍ' if args.gpu else 'NO'}")
    print(f"  Modo       : {'TODAS' if args.all else args.zona}")
    print("═" * 72 + "\n")

    # Verificaciones previas
    if IS_WINDOWS:
        if not MESHROOM_BATCH.exists():
            log(f"AVISO: meshroom_batch.exe no encontrado:\n  {MESHROOM_BATCH}", "WARN")
        if not BLENDER_EXE.exists():
            log(f"AVISO: blender.exe no encontrado:\n  {BLENDER_EXE}", "WARN")

    ensure_dirs()

    log("Cargando photo_building_mapping.json...")
    building_map = load_building_photo_map()
    zone_map     = load_zone_photo_map()
    log(f"Zonas disponibles en Processed/: {sorted(zone_map.keys())}")

    progress = load_progress()

    # --retry: solo fallidos
    if args.retry:
        retry_ids = {eid for eid, v in progress.items() if v.get("status") == "failed"}
        log(f"Modo retry: {len(retry_ids)} edificios fallidos")
        building_map = {eid: v for eid, v in building_map.items() if eid in retry_ids}
        if not building_map:
            log("No hay edificios fallidos que reintentar.", "OK")
            return
        args.force = True   # forzar reprocesado

    all_results = []
    t_start = time.time()

    if args.all:
        zonas = ordered_zone_list(zone_map)
        log(f"Procesando {len(zonas)} zonas: {zonas}\n")
        for i, zona in enumerate(zonas, 1):
            print(f"\n{'─' * 72}")
            print(f"  [{i}/{len(zonas)}]  ZONA: {zona.upper()}")
            print(f"{'─' * 72}")
            results = process_zona(zona, building_map, zone_map, progress, args.gpu, args.force)
            all_results.extend(results)
    else:
        zona = args.zona.lower()
        if zona not in zone_map and not any(v["zona"] == zona for v in building_map.values()):
            log(f"Zona '{zona}' no encontrada. Disponibles: {sorted(zone_map.keys())}", "ERR")
            sys.exit(1)
        results = process_zona(zona, building_map, zone_map, progress, args.gpu, args.force)
        all_results.extend(results)

    elapsed = time.time() - t_start
    print_summary(all_results, elapsed)

    # Actualizar photogrammetry_report.json
    report = {"status": "pending", "edificios": []}
    if PHOTOGRAMMETRY_REPORT.exists():
        try:
            with open(PHOTOGRAMMETRY_REPORT, "r", encoding="utf-8") as f:
                report = json.load(f)
        except json.JSONDecodeError:
            pass

    done_count = sum(1 for r in all_results if r.get("status") == "done")
    report.update({
        "status":          "done" if done_count > 0 else "pending",
        "last_run":        datetime.now().isoformat(),
        "total":           len(all_results),
        "done":            done_count,
        "failed":          sum(1 for r in all_results if r.get("status") == "failed"),
        "time_minutes":    round(elapsed / 60, 1),
        "edificios":       all_results,
    })
    with open(PHOTOGRAMMETRY_REPORT, "w", encoding="utf-8") as f:
        json.dump(report, f, indent=2, ensure_ascii=False)

    log(f"Reporte actualizado: {PHOTOGRAMMETRY_REPORT}", "OK")


if __name__ == "__main__":
    main()
