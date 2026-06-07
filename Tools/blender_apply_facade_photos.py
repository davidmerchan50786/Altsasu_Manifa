"""
BLENDER APPLY FACADE PHOTOS — Altsasu Manifa  (AAA Pipeline)
=============================================================
Convierte las fotos Street View procesadas en texturas PBR AAA y las aplica
como materiales HDRP-ready a los FBX de edificios.

Por cada edificio con fotos asignadas:
  1. Carga la mejor foto procesada (o mezcla N fotos del mismo edificio)
  2. Upscale 4K con Real-ESRGAN si está disponible (AMD Vulkan)
  3. Corrige iluminación con Retinex multiescala (des-baking de luces reales)
  4. Genera Normal map via Sobel + altura estimada
  5. Genera AO aproximado desde Normal map
  6. Crea material HDRP Lit (Metallic=0, Roughness=0.65, Normal strength=0.6)
  7. Aplica la textura al UV island de fachada principal del FBX
  8. Re-exporta FBX con texturas embebidas + PNG sueltas para Unity

Salida:
  Assets/Models/Buildings_Generated/{id}_photo.fbx
  Assets/AlsasuaData/FacadeTextures/AAA/{id}_albedo.png   (4096×4096)
  Assets/AlsasuaData/FacadeTextures/AAA/{id}_normal.png
  Assets/AlsasuaData/FacadeTextures/AAA/{id}_ao.png

Uso desde Blender:
  blender --background --python Tools/blender_apply_facade_photos.py -- --all
  blender --background --python Tools/blender_apply_facade_photos.py -- --zona iglesia
  blender --background --python Tools/blender_apply_facade_photos.py -- --id 91927762
  blender --background --python Tools/blender_apply_facade_photos.py -- --all --no-esrgan

Uso directo Python (sin Blender, solo procesa texturas PNG):
  python Tools/blender_apply_facade_photos.py --textures-only --all
"""

import argparse
import json
import math
import os
import platform
import shutil
import struct
import subprocess
import sys
import zlib
from collections import defaultdict
from pathlib import Path

# ─── RUTAS ─────────────────────────────────────────────────────────────────────

SCRIPT_DIR   = Path(__file__).resolve().parent
PROJECT_DIR  = SCRIPT_DIR.parent

MAPPING_JSON    = PROJECT_DIR / "Assets/AlsasuaData/photo_building_mapping.json"
BUILDINGS_JSON  = PROJECT_DIR / "Assets/AlsasuaData/buildings_fusion_final.json"
PROCESSED_DIR   = PROJECT_DIR / "Assets/AlsasuaData/FacadeTextures/Processed"
AAA_DIR         = PROJECT_DIR / "Assets/AlsasuaData/FacadeTextures/AAA"
FBX_IN_DIR      = PROJECT_DIR / "Assets/Models/Buildings_Generated"
FBX_OUT_DIR     = PROJECT_DIR / "Assets/Models/Buildings_Photo"

# Real-ESRGAN — AMD Vulkan
ESRGAN_PATHS = [
    Path(r"E:\realesrgan-ncnn-vulkan\realesrgan-ncnn-vulkan.exe"),
    Path(r"D:\realesrgan-ncnn-vulkan\realesrgan-ncnn-vulkan.exe"),
    Path(r"C:\realesrgan-ncnn-vulkan\realesrgan-ncnn-vulkan.exe"),
]

IS_WINDOWS = platform.system() == "Windows"
IN_BLENDER = False
try:
    import bpy
    IN_BLENDER = True
except ImportError:
    pass

# ─── LOGGING ───────────────────────────────────────────────────────────────────

def log(msg: str, level: str = "INFO"):
    prefix = {"INFO": "   ", "OK": " ✓ ", "WARN": " ⚠ ", "ERR": " ✗ "}.get(level, "   ")
    print(f"[{level}]{prefix}{msg}", flush=True)

# ─── UTILIDADES PNG NATIVAS (sin Pillow) ───────────────────────────────────────

def _png_chunk(name: bytes, data: bytes) -> bytes:
    crc = zlib.crc32(name + data) & 0xFFFFFFFF
    return struct.pack(">I", len(data)) + name + data + struct.pack(">I", crc)

def write_png_rgb(path: Path, pixels: list, w: int, h: int):
    """Escribe PNG RGB 8bpp sin dependencias externas."""
    raw = bytearray()
    for row in range(h):
        raw.append(0)  # filter type None
        for col in range(w):
            idx = (row * w + col) * 3
            raw.extend(pixels[idx:idx+3])
    compressed = zlib.compress(bytes(raw), 6)
    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n")
        f.write(_png_chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0)))
        f.write(_png_chunk(b"IDAT", compressed))
        f.write(_png_chunk(b"IEND", b""))

def read_png_rgb(path: Path):
    """Lee PNG/JPEG básico usando Blender si está disponible, o solo metadatos."""
    if IN_BLENDER:
        img = bpy.data.images.load(str(path), check_existing=False)
        w, h = img.size
        px = list(img.pixels)
        bpy.data.images.remove(img)
        # Convertir RGBA float [0,1] → RGB int [0,255]
        out = []
        for i in range(0, len(px), 4):
            out.extend([int(px[i]*255), int(px[i+1]*255), int(px[i+2]*255)])
        return out, w, h
    return None, 0, 0

# ─── RETINEX MULTIESCALA (des-iluminación) ─────────────────────────────────────

def retinex_channel(channel: list, w: int, h: int, sigmas=(15, 80, 250)) -> list:
    """
    Retinex multiescala en log-space. Elimina el baking de luz solar/sombras
    de las fotos Street View para obtener el color real de fachada.
    Sin numpy — operaciones puras Python (lento pero funcional).
    """
    import math

    def log_safe(v): return math.log(max(v, 1e-6))

    ch = [float(v) / 255.0 for v in channel]
    log_ch = [log_safe(v) for v in ch]

    result = [0.0] * (w * h)

    for sigma in sigmas:
        # Gaussian blur aproximado con caja iterada (3 pasadas)
        radius = max(1, int(sigma * 2.5))
        blurred = ch[:]

        for _ in range(3):  # 3 pasadas caja ≈ Gaussiano
            # Blur horizontal
            tmp = blurred[:]
            for row in range(h):
                s = 0.0
                cnt = 0
                for col in range(min(radius, w)):
                    s += blurred[row * w + col]
                    cnt += 1
                for col in range(w):
                    right = col + radius
                    left  = col - radius - 1
                    if right < w:
                        s += blurred[row * w + right]; cnt += 1
                    if left >= 0:
                        s -= blurred[row * w + left]; cnt -= 1
                    tmp[row * w + col] = s / max(cnt, 1)
            # Blur vertical
            blurred2 = tmp[:]
            for col in range(w):
                s = 0.0
                cnt = 0
                for row in range(min(radius, h)):
                    s += tmp[row * w + col]
                    cnt += 1
                for row in range(h):
                    bottom = row + radius
                    top    = row - radius - 1
                    if bottom < h:
                        s += tmp[bottom * w + col]; cnt += 1
                    if top >= 0:
                        s -= tmp[top * w + col]; cnt -= 1
                    blurred2[row * w + col] = s / max(cnt, 1)
            blurred = blurred2

        # Respuesta Retinex para este sigma
        for i in range(w * h):
            result[i] += log_ch[i] - log_safe(blurred[i])

    # Normalizar al peso de sigmas
    weight = 1.0 / len(sigmas)
    result = [v * weight for v in result]

    # Normalizar p2-p98 → [0,1]
    sorted_r = sorted(result)
    lo = sorted_r[int(len(sorted_r) * 0.02)]
    hi = sorted_r[int(len(sorted_r) * 0.98)]
    span = max(hi - lo, 1e-6)

    normalized = [max(0.0, min(1.0, (v - lo) / span)) for v in result]

    # Ajustar media a 0.18 (gris neutro fotográfico)
    mean = sum(normalized) / len(normalized)
    gain = 0.18 / max(mean, 1e-6)
    normalized = [max(0.0, min(1.0, v * gain)) for v in normalized]

    return [int(v * 255) for v in normalized]

def apply_retinex(pixels: list, w: int, h: int) -> list:
    """Aplica Retinex a los 3 canales RGB por separado."""
    R = [pixels[i*3]   for i in range(w*h)]
    G = [pixels[i*3+1] for i in range(w*h)]
    B = [pixels[i*3+2] for i in range(w*h)]
    log("  Retinex canal R...", "INFO")
    R2 = retinex_channel(R, w, h)
    log("  Retinex canal G...", "INFO")
    G2 = retinex_channel(G, w, h)
    log("  Retinex canal B...", "INFO")
    B2 = retinex_channel(B, w, h)
    out = []
    for i in range(w*h):
        out.extend([R2[i], G2[i], B2[i]])
    return out

# ─── NORMAL MAP (Sobel desde albedo) ─────────────────────────────────────────

def generate_normal_map(pixels: list, w: int, h: int, strength: float = 4.0) -> list:
    """Genera Normal map RGB desde luminancia del albedo usando Sobel 3×3."""
    # Luminancia
    lum = [0.299*pixels[i*3] + 0.587*pixels[i*3+1] + 0.114*pixels[i*3+2] for i in range(w*h)]

    normals = []
    for row in range(h):
        for col in range(w):
            def s(r, c): return lum[max(0,min(h-1,row+r)) * w + max(0,min(w-1,col+c))]
            gx = (-s(-1,-1) - 2*s(0,-1) - s(1,-1) + s(-1,1) + 2*s(0,1) + s(1,1)) / 255.0
            gy = (-s(-1,-1) - 2*s(-1,0) - s(-1,1) + s(1,-1) + 2*s(1,0) + s(1,1)) / 255.0
            nx = -gx * strength
            ny = -gy * strength
            nz = 1.0
            length = max(math.sqrt(nx*nx + ny*ny + nz*nz), 1e-6)
            nx /= length; ny /= length; nz /= length
            # Directx convention (flip G para Unity HDRP)
            nr = int((nx * 0.5 + 0.5) * 255)
            ng = int((-ny * 0.5 + 0.5) * 255)  # Y invertida
            nb = int((nz * 0.5 + 0.5) * 255)
            normals.extend([nr, ng, nb])
    return normals

# ─── AO APROXIMADO (desde Normal map) ────────────────────────────────────────

def generate_ao_map(normal_pixels: list, w: int, h: int) -> list:
    """AO aproximado: desvío del normal respecto a (0,0,1) → zona cóncava = oscura."""
    ao = []
    for i in range(w * h):
        nx = (normal_pixels[i*3]   / 255.0) * 2 - 1
        ny = (normal_pixels[i*3+1] / 255.0) * 2 - 1
        nz = (normal_pixels[i*3+2] / 255.0) * 2 - 1
        # Dot con up = nz; áreas laterales/cóncavas tienen menor nz → más sombra
        occ = max(0.0, min(1.0, nz * 0.5 + 0.5))
        v = int(occ * 255)
        ao.extend([v, v, v])
    return ao

# ─── REAL-ESRGAN UPSCALE (AMD Vulkan) ────────────────────────────────────────

def find_esrgan() -> Path | None:
    for p in ESRGAN_PATHS:
        if p.exists():
            return p
    return None

def esrgan_upscale(src: Path, dst: Path, scale: int = 2) -> bool:
    exe = find_esrgan()
    if not exe or not IS_WINDOWS:
        return False
    try:
        r = subprocess.run([str(exe), "-i", str(src), "-o", str(dst),
                            "-n", "realesrgan-x4plus",
                            "-s", str(scale), "-f", "png"],
                           timeout=120, capture_output=True)
        return r.returncode == 0 and dst.exists()
    except Exception:
        return False

# ─── PROCESADO DE TEXTURA INDIVIDUAL ─────────────────────────────────────────

def process_texture(src_path: Path, building_id: str, use_esrgan: bool = True) -> dict:
    """
    Convierte una foto procesada en texturas PBR AAA.
    Devuelve rutas de albedo/normal/ao generados.
    """
    AAA_DIR.mkdir(parents=True, exist_ok=True)
    albedo_path = AAA_DIR / f"{building_id}_albedo.png"
    normal_path = AAA_DIR / f"{building_id}_normal.png"
    ao_path     = AAA_DIR / f"{building_id}_ao.png"

    log(f"Procesando textura para edificio {building_id}")
    log(f"  Fuente: {src_path.name}")

    # 1. Upscale ESRGAN si disponible
    upscaled = src_path
    if use_esrgan and IS_WINDOWS:
        tmp_up = AAA_DIR / f"{building_id}_esrgan_tmp.png"
        log("  Upscale 4× con Real-ESRGAN (AMD Vulkan)...")
        if esrgan_upscale(src_path, tmp_up, scale=4):
            upscaled = tmp_up
            log("  ESRGAN OK — textura 4K", "OK")
        else:
            log("  ESRGAN no disponible, usando resolución original", "WARN")

    # 2. Leer pixels
    if IN_BLENDER:
        pixels, w, h = read_png_rgb(upscaled)
    else:
        # Modo --textures-only: copiar directamente sin procesar
        shutil.copy(upscaled, albedo_path)
        log(f"  Modo sin Blender: albedo copiado ({albedo_path.name})", "WARN")
        return {"albedo": str(albedo_path), "normal": None, "ao": None}

    if not pixels:
        log("  No se pudo leer la imagen", "ERR")
        return {}

    log(f"  Resolución: {w}×{h}")

    # 3. Retinex (solo si la imagen es pequeña enough para no tardar siglos)
    if w * h <= 2048 * 2048:
        log("  Aplicando Retinex multiescala...")
        pixels = apply_retinex(pixels, w, h)
    else:
        log(f"  Imagen {w}×{h} demasiado grande para Retinex en CPU — saltando", "WARN")

    # 4. Guardar albedo
    write_png_rgb(albedo_path, pixels, w, h)
    log(f"  Albedo guardado: {albedo_path.name}", "OK")

    # 5. Normal map
    log("  Generando Normal map (Sobel)...")
    normal_px = generate_normal_map(pixels, w, h, strength=3.5)
    write_png_rgb(normal_path, normal_px, w, h)
    log(f"  Normal guardado: {normal_path.name}", "OK")

    # 6. AO
    log("  Generando AO aproximado...")
    ao_px = generate_ao_map(normal_px, w, h)
    write_png_rgb(ao_path, ao_px, w, h)
    log(f"  AO guardado:     {ao_path.name}", "OK")

    return {"albedo": str(albedo_path), "normal": str(normal_path), "ao": str(ao_path)}

# ─── BLENDER: APLICAR TEXTURA A FBX ─────────────────────────────────────────

def apply_texture_to_fbx_blender(fbx_path: Path, tex_paths: dict, building_id: str):
    """
    Carga el FBX en Blender, reemplaza el material de fachada con
    las texturas PBR reales y re-exporta FBX.
    Solo se llama cuando IN_BLENDER=True.
    """
    if not IN_BLENDER:
        return

    FBX_OUT_DIR.mkdir(parents=True, exist_ok=True)
    out_fbx = FBX_OUT_DIR / f"{building_id}_photo.fbx"

    # Limpiar escena
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete()

    # Importar FBX si existe, o crear cubo placeholder
    if fbx_path.exists():
        bpy.ops.import_scene.fbx(filepath=str(fbx_path))
        log(f"  FBX cargado: {fbx_path.name}")
    else:
        bpy.ops.mesh.primitive_cube_add()
        log(f"  FBX no encontrado — usando cubo placeholder", "WARN")

    # Crear material HDRP Lit con texturas reales
    mat_name = f"Mat_Photo_{building_id}"
    if mat_name in bpy.data.materials:
        bpy.data.materials.remove(bpy.data.materials[mat_name])
    mat = bpy.data.materials.new(name=mat_name)
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    links = mat.node_tree.links
    nodes.clear()

    # Principled BSDF
    bsdf = nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.location = (300, 0)
    bsdf.inputs["Metallic"].default_value    = 0.0
    bsdf.inputs["Roughness"].default_value   = 0.65
    bsdf.inputs["Specular IOR Level"].default_value = 0.04

    out_node = nodes.new("ShaderNodeOutputMaterial")
    out_node.location = (600, 0)
    links.new(bsdf.outputs["BSDF"], out_node.inputs["Surface"])

    # Nodo albedo
    if tex_paths.get("albedo"):
        tex_alb = nodes.new("ShaderNodeTexImage")
        tex_alb.location = (-400, 200)
        tex_alb.image = bpy.data.images.load(tex_paths["albedo"])
        tex_alb.image.colorspace_settings.name = "sRGB"
        links.new(tex_alb.outputs["Color"], bsdf.inputs["Base Color"])

    # Nodo normal
    if tex_paths.get("normal"):
        tex_nrm = nodes.new("ShaderNodeTexImage")
        tex_nrm.location = (-400, -100)
        tex_nrm.image = bpy.data.images.load(tex_paths["normal"])
        tex_nrm.image.colorspace_settings.name = "Non-Color"
        nrm_map = nodes.new("ShaderNodeNormalMap")
        nrm_map.location = (0, -100)
        nrm_map.inputs["Strength"].default_value = 0.6
        links.new(tex_nrm.outputs["Color"], nrm_map.inputs["Color"])
        links.new(nrm_map.outputs["Normal"], bsdf.inputs["Normal"])

    # Nodo AO (mezcla sobre albedo)
    if tex_paths.get("ao") and tex_paths.get("albedo"):
        tex_ao = nodes.new("ShaderNodeTexImage")
        tex_ao.location = (-400, 400)
        tex_ao.image = bpy.data.images.load(tex_paths["ao"])
        tex_ao.image.colorspace_settings.name = "Non-Color"
        mix = nodes.new("ShaderNodeMixRGB")
        mix.blend_type = "MULTIPLY"
        mix.location = (-50, 300)
        mix.inputs["Fac"].default_value = 0.5
        links.new(tex_alb.outputs["Color"], mix.inputs[1])
        links.new(tex_ao.outputs["Color"], mix.inputs[2])
        links.new(mix.outputs["Color"], bsdf.inputs["Base Color"])

    # Asignar material a todos los objetos de fachada
    for obj in bpy.context.scene.objects:
        if obj.type != 'MESH':
            continue
        # Detectar fachada por nombre (LOD0 o sin LOD suffix)
        name_lower = obj.name.lower()
        is_facade = any(kw in name_lower for kw in ["facade", "fachada", "wall", "muro", "lod0", building_id])
        if not is_facade and len(bpy.context.scene.objects) > 1:
            continue
        obj.data.materials.clear()
        obj.data.materials.append(mat)

    # Exportar FBX
    bpy.ops.export_scene.fbx(
        filepath=str(out_fbx),
        use_selection=False,
        embed_textures=True,
        path_mode='COPY',
        axis_forward='-Z',
        axis_up='Y',
        global_scale=1.0,
    )
    log(f"  FBX exportado: {out_fbx.name}", "OK")

# ─── LEER MAPPING ────────────────────────────────────────────────────────────

def load_mapping() -> dict:
    """
    Carga photo_building_mapping.json y agrupa fotos por edificio_id.
    Devuelve {edificio_id: [foto_path, ...], ...}
    """
    if not MAPPING_JSON.exists():
        log(f"photo_building_mapping.json no encontrado: {MAPPING_JSON}", "ERR")
        return {}

    with open(MAPPING_JSON, encoding="utf-8") as f:
        data = json.load(f)

    mappings = data if isinstance(data, list) else data.get("mappings", [])
    by_id = defaultdict(list)

    for entry in mappings:
        bid  = str(entry.get("edificio_id", "unknown"))
        zona = entry.get("zona", "")
        foto = entry.get("foto_procesada", "")
        conf = entry.get("confidence", "low")
        if foto:
            by_id[bid].append({"path": Path(foto), "zona": zona, "confidence": conf})

    # Si el mapping es deficiente (todos mismo ID), hacer fallback por zona
    if len(by_id) <= 1:
        log("Mapping tiene solo 1 edificio — usando fallback por zona", "WARN")
        by_id = _build_zone_fallback_mapping()

    return by_id

def _build_zone_fallback_mapping() -> dict:
    """
    Fallback: agrupa fotos por zona y las asigna a los edificios más relevantes
    de cada zona según buildings_fusion_final.json.
    """
    # Centroides de zona en coordenadas Unity (metros desde Herriko Plaza)
    # OX=1918, OZ=8570, origen UTM E=567951, N=4749902
    ZONE_CENTROIDS = {
        "iglesia":        (1918 + (568180 - 567951), 8570 + (4749902 - 4749700)),  # ~iglesia
        "ayto":           (1918 + (568050 - 567951), 8570 + (4749902 - 4749830)),
        "plaza_fueros":   (1918 + (567951 - 567951), 8570 + (4749902 - 4749902)),  # Herriko Plaza
        "casco_viejo":    (1918 + (567900 - 567951), 8570 + (4749902 - 4749950)),
        "ferial":         (1918 + (568100 - 567951), 8570 + (4749902 - 4749750)),
        "gaztetxe":       (1918 + (567850 - 567951), 8570 + (4749902 - 4749850)),
        "plaza_zubeztia": (1918 + (567980 - 567951), 8570 + (4749902 - 4749820)),
        "garcia_jimenez": (1918 + (568000 - 567951), 8570 + (4749902 - 4749950)),
    }

    # Cargar edificios
    buildings_by_id = {}
    if BUILDINGS_JSON.exists():
        with open(BUILDINGS_JSON, encoding="utf-8") as f:
            bdata = json.load(f)
        blist = bdata if isinstance(bdata, list) else bdata.get("buildings", bdata.get("features", []))
        for b in blist:
            props = b.get("properties", b)
            bid = str(props.get("id", props.get("osm_id", "")))
            cx  = props.get("center_unity", props.get("centroid_unity", [0, 0]))
            if isinstance(cx, list) and len(cx) >= 2:
                buildings_by_id[bid] = {"x": cx[0], "z": cx[1] if len(cx) > 2 else cx[1]}
            elif isinstance(cx, dict):
                buildings_by_id[bid] = {"x": cx.get("x", 0), "z": cx.get("z", cx.get("y", 0))}

    def closest_building(zone_cx, zone_cz):
        best_id, best_d = None, float("inf")
        for bid, pos in buildings_by_id.items():
            d = math.sqrt((pos["x"]-zone_cx)**2 + (pos["z"]-zone_cz)**2)
            if d < best_d:
                best_d, best_id = d, bid
        return best_id or "unknown"

    # Leer fotos procesadas por zona del processing_report
    report_path = PROJECT_DIR / "Assets/AlsasuaData/FacadeTextures/processing_report.json"
    zone_photos = defaultdict(list)
    if report_path.exists():
        with open(report_path, encoding="utf-8") as f:
            report = json.load(f)
        for item in report.get("lista_procesadas", []):
            zona = item.get("zona", "")
            proc = item.get("procesada", "")
            if zona and proc:
                zone_photos[zona].append(Path(proc))
    else:
        # Escanear directorio Processed
        if PROCESSED_DIR.exists():
            for f in sorted(PROCESSED_DIR.iterdir()):
                if f.suffix.lower() in (".png", ".jpg", ".jpeg") and "_facade" not in f.name:
                    parts = f.stem.split("_")
                    if len(parts) >= 2:
                        zona = "_".join(parts[:-1])
                        zone_photos[zona].append(f)

    # Mapear zona → edificio más cercano al centroide de zona
    by_id = defaultdict(list)
    for zona, photos in zone_photos.items():
        if zona in ZONE_CENTROIDS:
            cx, cz = ZONE_CENTROIDS[zona]
            bid = closest_building(cx, cz)
        else:
            bid = "unknown"
        log(f"  Zona '{zona}' → edificio {bid} ({len(photos)} fotos)")
        conf = "high" if zona in ("iglesia", "ayto") else "medium"
        for p in photos:
            by_id[bid].append({"path": p, "zona": zona, "confidence": conf})

    return by_id

def select_best_photo(foto_list: list) -> Path | None:
    """Elige la mejor foto de la lista: prioriza high confidence, luego mayor tamaño."""
    high = [f for f in foto_list if f.get("confidence") == "high"]
    med  = [f for f in foto_list if f.get("confidence") == "medium"]
    pool = high or med or foto_list
    # La de mayor tamaño en disco suele ser la de mejor resolución
    pool_existing = [f for f in pool if f["path"].exists()]
    if not pool_existing:
        return None
    return max(pool_existing, key=lambda f: f["path"].stat().st_size)["path"]

# ─── PIPELINE PRINCIPAL ───────────────────────────────────────────────────────

def run_pipeline(filter_id: str = None, filter_zona: str = None, use_esrgan: bool = True):
    log("=" * 60)
    log("PIPELINE FOTOS → TEXTURAS PBR AAA")
    log("=" * 60)

    by_id = load_mapping()
    if not by_id:
        log("Sin datos de mapping. Ejecuta primero map_photos_to_buildings.py", "ERR")
        return

    # Filtrar
    if filter_id:
        by_id = {k: v for k, v in by_id.items() if k == filter_id}
    elif filter_zona:
        by_id = {k: v for k, v in by_id.items()
                 if any(f["zona"] == filter_zona for f in v)}

    log(f"Edificios a procesar: {len(by_id)}")
    resultados = []

    for building_id, foto_list in by_id.items():
        log(f"\n── Edificio {building_id} ({len(foto_list)} fotos) ──")

        best_photo = select_best_photo(foto_list)
        if not best_photo:
            log(f"  Ninguna foto disponible en disco — saltando", "WARN")
            continue

        # Generar texturas PBR
        tex_paths = process_texture(best_photo, building_id, use_esrgan=use_esrgan)
        if not tex_paths:
            continue

        # Aplicar a FBX si estamos en Blender
        if IN_BLENDER:
            fbx_candidate = FBX_IN_DIR / f"{building_id}.fbx"
            apply_texture_to_fbx_blender(fbx_candidate, tex_paths, building_id)

        resultados.append({
            "edificio_id": building_id,
            "foto_usada": str(best_photo),
            "n_fotos_disponibles": len(foto_list),
            "albedo": tex_paths.get("albedo"),
            "normal": tex_paths.get("normal"),
            "ao": tex_paths.get("ao"),
            "fbx_out": str(FBX_OUT_DIR / f"{building_id}_photo.fbx") if IN_BLENDER else None,
        })

    # Guardar reporte
    report_out = PROJECT_DIR / "Assets/AlsasuaData/FacadeTextures/aaa_textures_report.json"
    with open(report_out, "w", encoding="utf-8") as f:
        json.dump({"total": len(resultados), "edificios": resultados}, f, indent=2, ensure_ascii=False)

    log(f"\n{'='*60}")
    log(f"Completado: {len(resultados)}/{len(by_id)} edificios procesados", "OK")
    log(f"Texturas AAA: {AAA_DIR}")
    log(f"FBX: {FBX_OUT_DIR}")
    log(f"Reporte: {report_out}")

# ─── ENTRY POINT ─────────────────────────────────────────────────────────────

if __name__ == "__main__":
    # Cuando se llama desde Blender: los args vienen después de '--'
    argv = sys.argv
    if "--" in argv:
        argv = argv[argv.index("--") + 1:]
    else:
        argv = argv[1:]

    p = argparse.ArgumentParser(description="Fotos Street View → Texturas PBR AAA")
    p.add_argument("--all",            action="store_true", help="Procesar todos los edificios")
    p.add_argument("--id",             type=str,            help="Procesar solo este edificio_id")
    p.add_argument("--zona",           type=str,            help="Procesar solo esta zona")
    p.add_argument("--no-esrgan",      action="store_true", help="Saltar upscale Real-ESRGAN")
    p.add_argument("--textures-only",  action="store_true", help="Solo texturas, sin Blender FBX")
    args = p.parse_args(argv)

    run_pipeline(
        filter_id   = args.id,
        filter_zona = args.zona,
        use_esrgan  = not args.no_esrgan,
    )
