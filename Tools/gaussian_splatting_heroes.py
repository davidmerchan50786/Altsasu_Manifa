#!/usr/bin/env python3
"""
GAUSSIAN SPLATTING PARA EDIFICIOS HERO — Altsasu Manifa
=========================================================
3D Gaussian Splatting para iglesia, ayuntamiento y plaza_fueros.
Calidad visual muy superior a fotogrametría MVS clásica para edificios icónicos.

Estrategia:
  - Intentar nerfstudio splatfacto (mejor calidad, requiere GPU NVIDIA/ROCm)
  - Fallback: COLMAP + OpenMVS (densificación MVS de mayor calidad)
  - Convertir .ply gaussiano a malla usable con Open3D (isosuperficie)
  - Simplificar a 50k tris con quadric decimation
  - Exportar .obj listo para Blender cleanup

Requiere (automático si falta):
  pip install nerfstudio open3d

Uso:
    python Tools/gaussian_splatting_heroes.py
    python Tools/gaussian_splatting_heroes.py --edificio iglesia
    python Tools/gaussian_splatting_heroes.py --all --force
    python Tools/gaussian_splatting_heroes.py --colmap-only  # sin nerfstudio
    python Tools/gaussian_splatting_heroes.py --target-tris 80000

Rutas Windows esperadas:
    Fotos hero : Assets/AlsasuaData/FacadeTextures/Processed/{edificio}_*.png
    Salida splat: E:\\MeshroomCache\\splats\\{edificio}\\
    Salida malla: Assets/Models/Buildings_Photogrammetry/{edificio}_splat.obj
    COLMAP      : E:\\COLMAP\\COLMAP.bat
    OpenMVS     : E:\\OpenMVS\\bin\\
"""

import argparse
import json
import os
import platform
import shutil
import subprocess
import sys
import time
from datetime import datetime
from pathlib import Path

# ─── CONFIGURACIÓN ────────────────────────────────────────────────────────────

IS_WINDOWS = platform.system() == "Windows"

SCRIPT_DIR   = Path(__file__).resolve().parent
PROJECT_ROOT = SCRIPT_DIR.parent

PROCESSED_DIR   = PROJECT_ROOT / "Assets" / "AlsasuaData" / "FacadeTextures" / "Processed"
ENHANCED_DIR    = PROJECT_ROOT / "Assets" / "AlsasuaData" / "FacadeTextures" / "Processed_Enhanced"
OUTPUT_MESH_DIR = PROJECT_ROOT / "Assets" / "Models" / "Buildings_Photogrammetry"
SPLAT_CACHE     = Path(r"E:\MeshroomCache\splats")
COLMAP_EXE      = Path(r"E:\COLMAP\COLMAP.bat")
OPENMVS_BIN     = Path(r"E:\OpenMVS\bin")

# Edificios hero con Gaussian Splatting
HERO_BUILDINGS = {
    "iglesia":        "Iglesia de la Asunción de Alsasua",
    "ayto":           "Ayuntamiento de Alsasua",
    "plaza_fueros_1": "Plaza de los Fueros — fachada norte",
    "plaza_fueros_2": "Plaza de los Fueros — fachada sur",
    "plaza_fueros_3": "Plaza de los Fueros — fachada este",
    "plaza_fueros_4": "Plaza de los Fueros — fachada oeste",
    "plaza_fueros_5": "Plaza de los Fueros — esquina central",
}

# Triangulos objetivo en malla final (alta pero manejable en Unity HDRP)
DEFAULT_TARGET_TRIS = 50_000


# ─── UTILIDADES ───────────────────────────────────────────────────────────────

def log(msg: str, level: str = "INFO"):
    ts = datetime.now().strftime("%H:%M:%S")
    icons = {"INFO": "·", "OK": "✓", "WARN": "!", "ERR": "✗"}
    print(f"  {ts}  [{icons.get(level,'·')}]  {msg}", flush=True)


def find_hero_photos(edificio_id: str) -> list:
    """
    Busca fotos del edificio hero en Processed_Enhanced/ (preferido) o Processed/.
    """
    sources = [ENHANCED_DIR, PROCESSED_DIR]
    for src in sources:
        if not src.exists():
            continue
        fotos = sorted(src.glob(f"{edificio_id}_*.png"))
        if fotos:
            log(f"  Fotos '{edificio_id}': {len(fotos)} en {src.name}")
            return fotos

    # También buscar sin sufijo numérico estricto
    for src in sources:
        if not src.exists():
            continue
        fotos = sorted(src.glob(f"{edificio_id}*.png"))
        if fotos:
            log(f"  Fotos '{edificio_id}': {len(fotos)} en {src.name}")
            return fotos

    log(f"  No se encontraron fotos para '{edificio_id}'", "WARN")
    return []


def prepare_images_dir(edificio_id: str, fotos: list) -> Path:
    """Copia fotos a directorio temporal estructurado para COLMAP/nerfstudio."""
    img_dir = SPLAT_CACHE / edificio_id / "images"
    img_dir.mkdir(parents=True, exist_ok=True)
    for old in img_dir.glob("*"):
        old.unlink(missing_ok=True)
    for i, foto in enumerate(fotos):
        shutil.copy2(foto, img_dir / f"{i:04d}_{foto.name}")
    log(f"  Imágenes preparadas: {len(fotos)} → {img_dir}")
    return img_dir


# ─── VERIFICAR E INSTALAR DEPENDENCIAS ────────────────────────────────────────

def ensure_nerfstudio() -> bool:
    """
    Verifica si nerfstudio está instalado.
    Si no, intenta instalar con pip.
    Devuelve True si disponible.
    """
    try:
        result = subprocess.run(
            [sys.executable, "-c", "import nerfstudio"],
            capture_output=True, timeout=10,
        )
        if result.returncode == 0:
            log("nerfstudio detectado", "OK")
            return True
    except Exception:
        pass

    log("nerfstudio no encontrado. Intentando instalar...", "WARN")
    try:
        r = subprocess.run(
            [sys.executable, "-m", "pip", "install", "nerfstudio", "--quiet"],
            capture_output=True, text=True, timeout=300,
        )
        if r.returncode == 0:
            log("nerfstudio instalado correctamente", "OK")
            return True
        else:
            log(f"pip install nerfstudio falló: {r.stderr[-400:]}", "WARN")
    except subprocess.TimeoutExpired:
        log("Timeout instalando nerfstudio (>5min)", "WARN")
    except Exception as e:
        log(f"Error instalando nerfstudio: {e}", "WARN")

    return False


def ensure_open3d() -> bool:
    """Verifica/instala open3d."""
    try:
        result = subprocess.run(
            [sys.executable, "-c", "import open3d"],
            capture_output=True, timeout=10,
        )
        if result.returncode == 0:
            return True
    except Exception:
        pass

    log("open3d no encontrado. Instalando...", "WARN")
    try:
        r = subprocess.run(
            [sys.executable, "-m", "pip", "install", "open3d", "--quiet"],
            capture_output=True, text=True, timeout=300,
        )
        return r.returncode == 0
    except Exception:
        return False


# ─── COLMAP: SfM + POSES ──────────────────────────────────────────────────────

def run_colmap_sfm(edificio_id: str, img_dir: Path) -> Path | None:
    """
    Ejecuta COLMAP para obtener poses de cámara (necesarias para nerfstudio y OpenMVS).
    Modo: feature_extractor + exhaustive_matcher + mapper + image_undistorter.

    Devuelve workspace_dir si OK, None si falla.
    """
    if not COLMAP_EXE.exists():
        log(f"COLMAP no encontrado: {COLMAP_EXE}", "WARN")
        log("  Descarga: https://colmap.github.io/install.html", "WARN")
        return None

    ws_dir  = SPLAT_CACHE / edificio_id / "colmap_ws"
    db_path = ws_dir / "database.db"
    ws_dir.mkdir(parents=True, exist_ok=True)

    log(f"COLMAP SfM para '{edificio_id}' ({img_dir.name})")
    t0 = time.time()

    env = os.environ.copy()
    env["CUDA_VISIBLE_DEVICES"] = ""   # Usar OpenGL/CPU si no hay CUDA (AMD compatible)

    steps = [
        # Extracción de features (SIFT)
        [str(COLMAP_EXE), "feature_extractor",
         "--database_path", str(db_path),
         "--image_path",    str(img_dir),
         "--SiftExtraction.use_gpu", "0",       # CPU: funciona en AMD sin CUDA
         "--SiftExtraction.max_image_size", "3200",
         "--SiftExtraction.max_num_features", "8192"],

        # Matching exhaustivo (mejor para pocos frames de fachada)
        [str(COLMAP_EXE), "exhaustive_matcher",
         "--database_path", str(db_path),
         "--SiftMatching.use_gpu", "0"],

        # Reconstrucción SfM
        [str(COLMAP_EXE), "mapper",
         "--database_path",  str(db_path),
         "--image_path",     str(img_dir),
         "--output_path",    str(ws_dir / "sparse"),
         "--Mapper.num_threads", "8"],

        # Undistort imágenes para MVS
        [str(COLMAP_EXE), "image_undistorter",
         "--image_path",   str(img_dir),
         "--input_path",   str(ws_dir / "sparse" / "0"),
         "--output_path",  str(ws_dir / "dense"),
         "--output_type",  "COLMAP"],
    ]

    (ws_dir / "sparse").mkdir(parents=True, exist_ok=True)

    for step_cmd in steps:
        step_name = step_cmd[1]
        log(f"  COLMAP {step_name}...")
        try:
            r = subprocess.run(step_cmd, capture_output=True, text=True,
                               timeout=3600, env=env)
            if r.returncode != 0:
                log(f"  COLMAP {step_name} falló:\n{r.stderr[-600:]}", "WARN")
                # No es fatal: algunos pasos pueden fallar parcialmente
        except subprocess.TimeoutExpired:
            log(f"  COLMAP {step_name} timeout", "WARN")
        except Exception as e:
            log(f"  COLMAP {step_name} error: {e}", "WARN")

    log(f"  COLMAP SfM completado en {(time.time()-t0)/60:.1f} min", "OK")
    return ws_dir


# ─── NERFSTUDIO SPLATFACTO ───────────────────────────────────────────────────

def run_gaussian_splatting(edificio_id: str, colmap_ws: Path) -> Path | None:
    """
    Entrena 3D Gaussian Splatting con nerfstudio (splatfacto).
    Usa las poses COLMAP como inicialización.
    Requiere GPU con suficiente VRAM (≥8GB recomendado).

    Devuelve path al .ply del splat final, o None si falla.
    """
    output_dir = SPLAT_CACHE / "splats" / edificio_id
    output_dir.mkdir(parents=True, exist_ok=True)

    dense_dir = colmap_ws / "dense"
    if not dense_dir.exists():
        log(f"  COLMAP dense dir no encontrado: {dense_dir}", "WARN")
        return None

    log(f"  Entrenando splatfacto para '{edificio_id}'...")
    t0 = time.time()

    # ns-train splatfacto con configuración para fachadas urbanas
    cmd = [
        "ns-train", "splatfacto",
        "--data",        str(dense_dir),
        "--output-dir",  str(output_dir),
        "--vis",         "wandb",          # sin viewer (background)
        "--max-num-iterations", "30000",   # 30k iters: calidad vs tiempo
        "--pipeline.model.cull-alpha-thresh", "0.005",  # más gaussianos finos
        "--pipeline.model.densify-grad-thresh", "0.0002",
    ]

    env = os.environ.copy()
    # AMD ROCm: intentar HIP si disponible
    env["HIP_VISIBLE_DEVICES"]          = "0"
    env["ROCR_VISIBLE_DEVICES"]         = "0"

    try:
        r = subprocess.run(cmd, capture_output=True, text=True,
                           timeout=14400, env=env)   # 4h max
        elapsed = (time.time() - t0) / 60
        if r.returncode != 0:
            log(f"  splatfacto falló ({elapsed:.1f}min):\n{r.stderr[-800:]}", "WARN")
            return None
        log(f"  splatfacto OK en {elapsed:.1f} min", "OK")
    except subprocess.TimeoutExpired:
        log("  splatfacto timeout (>4h)", "WARN")
        return None
    except FileNotFoundError:
        log("  ns-train no encontrado en PATH (nerfstudio no instalado correctamente)", "WARN")
        return None
    except Exception as e:
        log(f"  splatfacto error: {e}", "WARN")
        return None

    # Buscar .ply de splat exportado
    ply_candidates = list(output_dir.rglob("splat.ply")) + \
                     list(output_dir.rglob("point_cloud.ply")) + \
                     list(output_dir.rglob("*.ply"))

    if not ply_candidates:
        log("  No se encontró .ply del splat", "WARN")
        return None

    ply_path = max(ply_candidates, key=lambda p: p.stat().st_size)
    log(f"  Splat PLY: {ply_path.name}  ({ply_path.stat().st_size // 1024} KB)", "OK")
    return ply_path


# ─── OPENMVS: DENSIFICACIÓN ALTERNATIVA ──────────────────────────────────────

def run_openmvs_dense(edificio_id: str, colmap_ws: Path) -> Path | None:
    """
    Alternativa a nerfstudio: COLMAP + OpenMVS para densificación MVS.
    Mayor calidad que Meshroom en CPU AMD, sin necesidad de GPU.

    Flujo:
        COLMAP dense/mvs → OpenMVS DensifyPointCloud → ReconstructMesh → TextureMesh
    """
    openmvs_densify = OPENMVS_BIN / "DensifyPointCloud.exe"
    openmvs_recon   = OPENMVS_BIN / "ReconstructMesh.exe"
    openmvs_texture = OPENMVS_BIN / "TextureMesh.exe"

    if not openmvs_recon.exists():
        log(f"OpenMVS no encontrado: {OPENMVS_BIN}", "WARN")
        log("  Descarga: https://github.com/cdcseacave/openMVS/releases", "WARN")
        return None

    ws_dir  = SPLAT_CACHE / edificio_id
    dense_dir = colmap_ws / "dense"

    if not dense_dir.exists():
        log("  COLMAP dense dir no encontrado para OpenMVS", "WARN")
        return None

    # COLMAP MVS denso (patch-match stereo)
    log("  COLMAP patch_match_stereo...")
    t0 = time.time()

    env = os.environ.copy()
    env["CUDA_VISIBLE_DEVICES"] = ""

    stereo_cmd = [
        str(COLMAP_EXE), "patch_match_stereo",
        "--workspace_path", str(dense_dir),
        "--workspace_format", "COLMAP",
        "--PatchMatchStereo.geom_consistency", "true",
        "--PatchMatchStereo.gpu_index", "-1",   # CPU fallback
    ]
    try:
        r = subprocess.run(stereo_cmd, capture_output=True, text=True,
                           timeout=7200, env=env)
        if r.returncode != 0:
            log(f"  patch_match_stereo falló: {r.stderr[-400:]}", "WARN")
    except Exception as e:
        log(f"  patch_match_stereo error: {e}", "WARN")

    # Fusión de nube de puntos densa
    fused_ply = dense_dir / "fused.ply"
    stereo_cmd2 = [
        str(COLMAP_EXE), "stereo_fusion",
        "--workspace_path", str(dense_dir),
        "--workspace_format", "COLMAP",
        "--input_type", "geometric",
        "--output_path", str(fused_ply),
    ]
    try:
        subprocess.run(stereo_cmd2, capture_output=True, text=True,
                       timeout=3600, env=env)
    except Exception as e:
        log(f"  stereo_fusion error: {e}", "WARN")

    if not fused_ply.exists():
        log("  COLMAP no generó fused.ply", "WARN")
        return None

    log(f"  Nube densa COLMAP: {fused_ply.stat().st_size // 1024} KB", "OK")

    # OpenMVS ReconstructMesh
    mesh_mvs = ws_dir / "scene_dense_mesh.mvs"
    mesh_obj  = ws_dir / f"{edificio_id}_openmvs.obj"

    log("  OpenMVS ReconstructMesh...")
    try:
        r2 = subprocess.run(
            [str(openmvs_recon),
             f"--input-file={fused_ply}",
             f"--output-file={mesh_mvs}",
             "--remove-spurious=20",
             "--smooth=2",
             "--max-threads=0"],   # todos los cores
            capture_output=True, text=True, timeout=3600,
        )
        if r2.returncode != 0:
            log(f"  ReconstructMesh warning: {r2.stderr[-300:]}", "WARN")
    except Exception as e:
        log(f"  ReconstructMesh error: {e}", "WARN")
        return None

    # OpenMVS TextureMesh
    log("  OpenMVS TextureMesh...")
    try:
        r3 = subprocess.run(
            [str(openmvs_texture),
             f"--input-file={mesh_mvs}",
             "--export-type=obj",
             "--texture-size=8192",      # 8K textura (mismo que Meshroom)
             f"--output-file={mesh_obj}"],
            capture_output=True, text=True, timeout=3600,
        )
        if r3.returncode == 0 and mesh_obj.exists():
            log(f"  OpenMVS TextureMesh OK: {mesh_obj.name}", "OK")
            return mesh_obj
        else:
            log(f"  TextureMesh falló: {r3.stderr[-300:]}", "WARN")
    except Exception as e:
        log(f"  TextureMesh error: {e}", "WARN")

    # Fallback: devolver fused.ply sin textura
    return fused_ply


# ─── CONVERTIR SPLAT .PLY A MALLA ────────────────────────────────────────────

def ply_splat_to_mesh(ply_path: Path, edificio_id: str,
                      target_tris: int = DEFAULT_TARGET_TRIS) -> Path | None:
    """
    Convierte .ply de Gaussian Splatting a malla usable:
    1. Carga con Open3D
    2. Estima normales (radio adaptativo)
    3. Isosuperficie con Ball Pivoting o Poisson (según densidad)
    4. Simplifica a target_tris con quadric decimation
    5. Exporta como .obj

    Devuelve path al .obj resultante o None si falla.
    """
    try:
        import open3d as o3d
        import numpy as np
    except ImportError:
        log("open3d no disponible para conversión de splat a malla", "WARN")
        return None

    log(f"  Convirtiendo splat .ply → malla ({target_tris:,} tris target)...")
    t0 = time.time()

    try:
        pcd = o3d.io.read_point_cloud(str(ply_path))
    except Exception as e:
        log(f"  No se pudo leer .ply con Open3D: {e}", "WARN")
        return None

    n_points = len(pcd.points)
    if n_points < 1000:
        log(f"  Nube de puntos muy pequeña: {n_points} puntos", "WARN")
        return None

    log(f"  Puntos en nube: {n_points:,}")

    # Estimar normales si no las tiene
    if not pcd.has_normals():
        radius = pcd.get_max_bound().max() * 0.02   # 2% del bbox
        pcd.estimate_normals(
            search_param=o3d.geometry.KDTreeSearchParamHybrid(radius=radius, max_nn=30)
        )
        pcd.orient_normals_consistent_tangent_plane(100)

    # Intentar reconstrucción Poisson (mejor para fachadas planas)
    mesh = None
    try:
        log("  Reconstrucción Poisson...")
        mesh, densities = o3d.geometry.TriangleMesh.create_from_point_cloud_poisson(
            pcd, depth=9, linear_fit=False
        )
        import numpy as np
        # Eliminar triángulos de baja densidad (artefactos de bordes)
        densities_np = np.asarray(densities)
        thresh       = densities_np.quantile(0.10)
        vertices_to_remove = densities_np < thresh
        mesh.remove_vertices_by_mask(vertices_to_remove)
        log(f"  Poisson OK: {len(mesh.triangles):,} tris")
    except Exception as e:
        log(f"  Poisson falló ({e}), intentando Ball Pivoting...", "WARN")
        try:
            radii = [0.005, 0.01, 0.02, 0.04]
            mesh = o3d.geometry.TriangleMesh.create_from_point_cloud_ball_pivoting(
                pcd,
                o3d.utility.DoubleVector(radii)
            )
            log(f"  Ball Pivoting OK: {len(mesh.triangles):,} tris")
        except Exception as e2:
            log(f"  Ball Pivoting también falló: {e2}", "ERR")
            return None

    if mesh is None or len(mesh.triangles) < 100:
        log("  Malla resultante vacía", "WARN")
        return None

    # Simplificar a target_tris con quadric decimation
    current_tris = len(mesh.triangles)
    if current_tris > target_tris:
        log(f"  Simplificando: {current_tris:,} → {target_tris:,} tris...")
        mesh = mesh.simplify_quadric_decimation(target_number_of_triangles=target_tris)
        log(f"  Resultado: {len(mesh.triangles):,} tris", "OK")

    # Limpiar malla: eliminar degenerados
    mesh.remove_degenerate_triangles()
    mesh.remove_duplicated_triangles()
    mesh.remove_duplicated_vertices()
    mesh.remove_non_manifold_edges()
    mesh.compute_vertex_normals()

    # Exportar .obj
    out_obj = OUTPUT_MESH_DIR / f"{edificio_id}_splat.obj"
    OUTPUT_MESH_DIR.mkdir(parents=True, exist_ok=True)

    o3d.io.write_triangle_mesh(str(out_obj), mesh, write_ascii=False)
    if out_obj.exists():
        size_kb = out_obj.stat().st_size // 1024
        log(f"  OBJ exportado: {out_obj.name}  ({size_kb} KB)  "
            f"en {(time.time()-t0):.0f}s", "OK")
        return out_obj
    else:
        log("  Fallo al exportar .obj", "ERR")
        return None


# ─── PIPELINE POR EDIFICIO HERO ───────────────────────────────────────────────

def process_hero_building(edificio_id: str, description: str,
                          use_nerfstudio: bool, use_colmap_only: bool,
                          target_tris: int, force: bool) -> dict:
    """
    Pipeline completo para un edificio hero:
    1. Preparar fotos
    2. COLMAP SfM (poses)
    3a. nerfstudio splatfacto (si disponible)
    3b. OpenMVS fallback (si no hay nerfstudio)
    4. Convertir splat/PLY a malla .obj
    5. El .obj puede ser consumido por Blender cleanup (meshroom_pipeline.py)
    """
    log(f"{'─' * 65}")
    log(f"EDIFICIO HERO: {edificio_id}  —  {description}")
    log(f"{'─' * 65}")

    # Verificar si ya está procesado
    out_obj = OUTPUT_MESH_DIR / f"{edificio_id}_splat.obj"
    if out_obj.exists() and not force:
        log(f"'{edificio_id}_splat.obj' ya existe → skip (usa --force para reprocesar)", "OK")
        return {"edificio_id": edificio_id, "status": "skipped",
                "obj": str(out_obj)}

    if not IS_WINDOWS:
        log(f"[SIMULADO] Solo en Windows — {edificio_id}", "WARN")
        return {"edificio_id": edificio_id, "status": "simulated"}

    # Buscar fotos
    fotos = find_hero_photos(edificio_id)
    if len(fotos) < 3:
        log(f"  Insuficientes fotos para '{edificio_id}' ({len(fotos)} < 3)", "WARN")
        return {"edificio_id": edificio_id, "status": "skipped_no_fotos",
                "n_fotos": len(fotos)}

    # Preparar directorio de imágenes
    img_dir = prepare_images_dir(edificio_id, fotos)

    # COLMAP SfM (necesario para nerfstudio y OpenMVS)
    colmap_ws = run_colmap_sfm(edificio_id, img_dir)

    ply_path = None
    method   = "unknown"

    # Intentar nerfstudio splatfacto
    if use_nerfstudio and not use_colmap_only and colmap_ws:
        ply_path = run_gaussian_splatting(edificio_id, colmap_ws)
        if ply_path:
            method = "gaussian_splatting"

    # Fallback: OpenMVS (mejor calidad MVS que Meshroom)
    if ply_path is None and colmap_ws:
        log(f"  Usando COLMAP + OpenMVS fallback para '{edificio_id}'")
        ply_path = run_openmvs_dense(edificio_id, colmap_ws)
        if ply_path:
            method = "colmap_openmvs"

    if ply_path is None:
        log(f"  No se pudo generar malla para '{edificio_id}'", "ERR")
        return {"edificio_id": edificio_id, "status": "failed",
                "error": "no_mesh_generated"}

    # Si el resultado ya es .obj (OpenMVS), usarlo directamente
    if ply_path.suffix.lower() == ".obj":
        out_obj = ply_path
        log(f"  .obj directo de OpenMVS: {out_obj.name}", "OK")
    else:
        # Convertir PLY gaussiano a malla con Open3D
        out_obj = ply_splat_to_mesh(ply_path, edificio_id, target_tris)

    if out_obj is None or not out_obj.exists():
        return {"edificio_id": edificio_id, "status": "failed",
                "error": "ply_to_mesh_failed"}

    result = {
        "edificio_id":  edificio_id,
        "status":       "done",
        "method":       method,
        "n_fotos":      len(fotos),
        "ply_path":     str(ply_path),
        "obj_path":     str(out_obj),
        "obj_size_kb":  out_obj.stat().st_size // 1024,
    }

    log(f"  Edificio hero completado: {edificio_id}", "OK")
    log(f"  Método: {method}  |  OBJ: {out_obj.name}  ({result['obj_size_kb']} KB)")

    return result


# ─── MAIN ─────────────────────────────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser(
        description="Gaussian Splatting para edificios hero — Altsasu Manifa",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Edificios hero disponibles:
  iglesia, ayto,
  plaza_fueros_1 .. plaza_fueros_5

Ejemplos:
  python Tools/gaussian_splatting_heroes.py --all
  python Tools/gaussian_splatting_heroes.py --edificio iglesia
  python Tools/gaussian_splatting_heroes.py --all --colmap-only
  python Tools/gaussian_splatting_heroes.py --all --target-tris 80000
        """,
    )
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--all",       action="store_true",
                      help="Procesa todos los edificios hero")
    mode.add_argument("--edificio",  metavar="ID",
                      help=f"Procesa solo este edificio ({', '.join(HERO_BUILDINGS)})")

    parser.add_argument("--colmap-only",  action="store_true",
                        help="Usa COLMAP+OpenMVS en vez de nerfstudio (sin GPU dedicada)")
    parser.add_argument("--force",        action="store_true",
                        help="Reprocesa aunque ya exista el .obj")
    parser.add_argument("--target-tris",  type=int, default=DEFAULT_TARGET_TRIS,
                        metavar="N",
                        help=f"Tris objetivo en malla final (default: {DEFAULT_TARGET_TRIS})")
    args = parser.parse_args()

    print("\n" + "=" * 70)
    print("  GAUSSIAN SPLATTING HEROES — Altsasu Manifa")
    print(f"  {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
    print(f"  Plataforma     : {platform.system()}")
    print(f"  Método         : {'COLMAP+OpenMVS' if args.colmap_only else 'nerfstudio splatfacto + fallback OpenMVS'}")
    print(f"  Target tris    : {args.target_tris:,}")
    print("=" * 70 + "\n")

    # Verificar dependencias
    use_nerfstudio = False
    if not args.colmap_only:
        use_nerfstudio = ensure_nerfstudio()
        if not use_nerfstudio:
            log("nerfstudio no disponible → usando COLMAP+OpenMVS", "WARN")

    open3d_ok = ensure_open3d()
    if not open3d_ok:
        log("open3d no disponible — conversión PLY→OBJ no disponible", "WARN")

    SPLAT_CACHE.mkdir(parents=True, exist_ok=True)
    OUTPUT_MESH_DIR.mkdir(parents=True, exist_ok=True)

    # Seleccionar edificios a procesar
    if args.all:
        to_process = list(HERO_BUILDINGS.items())
    else:
        eid = args.edificio.lower()
        if eid not in HERO_BUILDINGS:
            log(f"Edificio '{eid}' no es hero. Disponibles: {list(HERO_BUILDINGS)}", "ERR")
            sys.exit(1)
        to_process = [(eid, HERO_BUILDINGS[eid])]

    log(f"Procesando {len(to_process)} edificios hero\n")

    t_start = time.time()
    results = []

    for eid, desc in to_process:
        res = process_hero_building(
            edificio_id=eid,
            description=desc,
            use_nerfstudio=use_nerfstudio,
            use_colmap_only=args.colmap_only,
            target_tris=args.target_tris,
            force=args.force,
        )
        results.append(res)

    elapsed = time.time() - t_start

    # Guardar resultados
    results_json = SPLAT_CACHE / "hero_results.json"
    results_json.parent.mkdir(parents=True, exist_ok=True)
    with open(results_json, "w", encoding="utf-8") as f:
        json.dump({
            "generated": datetime.now().isoformat(),
            "method":    "gaussian_splatting" if use_nerfstudio else "colmap_openmvs",
            "results":   results,
        }, f, indent=2, ensure_ascii=False)

    # Resumen
    done    = [r for r in results if r.get("status") == "done"]
    failed  = [r for r in results if r.get("status") == "failed"]
    skipped = [r for r in results if r.get("status") in ("skipped", "simulated",
                                                           "skipped_no_fotos")]

    print("\n" + "=" * 70)
    print("  RESUMEN GAUSSIAN SPLATTING HEROES")
    print("=" * 70)
    print(f"  Completados    : {len(done)}")
    print(f"  Fallidos       : {len(failed)}")
    print(f"  Omitidos       : {len(skipped)}")
    print(f"  Tiempo total   : {elapsed/60:.1f} min")
    print()

    if done:
        print("  EDIFICIOS PROCESADOS:")
        for r in done:
            obj_kb = r.get("obj_size_kb", 0)
            method = r.get("method", "?")
            print(f"    [OK]  {r['edificio_id']:<22}  {method:<20}  {obj_kb} KB")

    if failed:
        print("\n  FALLIDOS:")
        for r in failed:
            print(f"    [ERR] {r['edificio_id']:<22}  {r.get('error','?')}")

    print()
    print(f"  Resultados: {results_json}")
    print()
    print("  SIGUIENTE PASO:")
    print("  Los .obj generados pueden pasarse directamente a Blender cleanup:")
    print("  python Tools/meshroom_pipeline.py --zona iglesia --gpu")
    print("=" * 70 + "\n")


if __name__ == "__main__":
    main()
