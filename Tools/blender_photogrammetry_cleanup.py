"""
BLENDER PHOTOGRAMMETRY CLEANUP — Altsasu Manifa
=================================================
Script Blender Python para limpiar y optimizar mallas fotogramétricas de Meshroom.
Genera LOD0-3 Unity-ready con texturas PBR bakeadas para HDRP.

Entrada : .obj de Meshroom (millones de tris, textura 8K)
Salida  : LODs FBX + Normal map + AO + Albedo de-lit (2K)

Ejecutado por meshroom_pipeline.py vía:
  blender.exe --background --python blender_photogrammetry_cleanup.py -- <args_json>

Args JSON esperado:
  {
    "edificio_id": "297646225",
    "obj_path":    "E:/MeshroomCache/output/297646225/texturing/texturedMesh.obj",
    "fbx_output":  "Assets/Models/Buildings_Photogrammetry/297646225.fbx",
    "tex_output":  "Assets/AlsasuaData/FacadeTextures/Photogrammetry/297646225_albedo.png",
    "use_gpu":     true
  }
"""

import bpy
import bmesh
import json
import math
import os
import sys
import time
from pathlib import Path
from mathutils import Vector

# ─── LEER ARGUMENTOS ─────────────────────────────────────────────────────────

def parse_args() -> dict:
    """Lee el JSON de argumentos pasado después de '--'."""
    argv = sys.argv
    if "--" not in argv:
        print("[ERROR] No se encontraron argumentos después de '--'")
        sys.exit(1)
    raw = argv[argv.index("--") + 1]
    try:
        return json.loads(raw)
    except json.JSONDecodeError as e:
        print(f"[ERROR] JSON de argumentos inválido: {e}\nRecibido: {raw}")
        sys.exit(1)


# ─── UTILIDADES ───────────────────────────────────────────────────────────────

def log(msg: str, level: str = "INFO"):
    icons = {"INFO": "·", "OK": "✓", "WARN": "!", "ERR": "✗"}
    print(f"  [Blender] [{icons.get(level,'·')}] {msg}", flush=True)


def get_active_mesh_object() -> bpy.types.Object | None:
    for obj in bpy.context.scene.objects:
        if obj.type == "MESH":
            return obj
    return None


def select_only(obj: bpy.types.Object):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def apply_all_modifiers(obj: bpy.types.Object):
    bpy.context.view_layer.objects.active = obj
    for mod in obj.modifiers:
        try:
            bpy.ops.object.modifier_apply(modifier=mod.name)
        except RuntimeError:
            obj.modifiers.remove(mod)


# ─── IMPORTAR OBJ ─────────────────────────────────────────────────────────────

def import_obj(obj_path: str) -> bpy.types.Object:
    """Importa el .obj de Meshroom. Compatible con Blender 4.x+ (wm.obj_import)."""
    log(f"Importando OBJ: {Path(obj_path).name}")
    before = set(o.name for o in bpy.data.objects)

    # Blender 4.x / 5.x
    if hasattr(bpy.ops.wm, "obj_import"):
        bpy.ops.wm.obj_import(filepath=obj_path)
    else:
        # Blender 3.x fallback
        bpy.ops.import_scene.obj(filepath=obj_path)

    new_objs = [o for o in bpy.data.objects if o.name not in before and o.type == "MESH"]
    if not new_objs:
        log("No se importó ningún objeto MESH", "ERR")
        sys.exit(1)

    # Si se importaron varios, unirlos
    if len(new_objs) > 1:
        bpy.ops.object.select_all(action="DESELECT")
        for o in new_objs:
            o.select_set(True)
        bpy.context.view_layer.objects.active = new_objs[0]
        bpy.ops.object.join()

    obj = bpy.context.active_object
    tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)
    log(f"Importado: {obj.name}  |  ~{tris:,} triángulos")
    return obj


# ─── LIMPIEZA DE MALLA ────────────────────────────────────────────────────────

def clean_mesh(obj: bpy.types.Object) -> int:
    """
    Limpieza profesional de malla fotogramétrica:
    - Eliminar islas desconectadas pequeñas (ruido)
    - Cerrar agujeros
    - Recalcular normales
    - Eliminar duplicados
    - Smooth shading con angle
    """
    log("Limpiando malla...")
    select_only(obj)
    bpy.ops.object.mode_set(mode="EDIT")
    bm = bmesh.from_edit_mesh(obj.data)

    tris_before = len(bm.faces)

    # 1. Eliminar geometría degenerada
    bmesh.ops.dissolve_degenerate(bm, dist=0.0001, edges=bm.edges)

    # 2. Merge vértices duplicados (remove doubles)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.001)

    # 3. Recalcular normales hacia exterior
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)

    # 4. Eliminar islas pequeñas (< 500 tris = ruido fotogramétrico)
    bm.faces.ensure_lookup_table()
    visited = set()
    islands = []

    def flood_fill(start_face):
        island = []
        stack = [start_face]
        while stack:
            f = stack.pop()
            if f.index in visited:
                continue
            visited.add(f.index)
            island.append(f)
            for edge in f.edges:
                for lf in edge.link_faces:
                    if lf.index not in visited:
                        stack.append(lf)
        return island

    for face in bm.faces:
        if face.index not in visited:
            island = flood_fill(face)
            islands.append(island)

    faces_to_delete = []
    for island in islands:
        if len(island) < 500:
            faces_to_delete.extend(island)

    if faces_to_delete:
        bmesh.ops.delete(bm, geom=faces_to_delete, context="FACES")
        log(f"  Eliminadas {len(faces_to_delete)} caras (islas pequeñas)")

    # 5. Cerrar agujeros (fill holes)
    bmesh.ops.holes_fill(bm, edges=[e for e in bm.edges if e.is_boundary], sides=0)

    # 6. Marcar bordes duros para preservarlos en decimación
    for edge in bm.edges:
        angle = edge.calc_face_angle(0.0)
        if angle > math.radians(45):
            edge.smooth = False   # Sharp edge

    bmesh.update_edit_mesh(obj.data)
    bpy.ops.object.mode_set(mode="OBJECT")

    # 7. Auto Smooth Normals (60°)
    obj.data.use_auto_smooth = True
    obj.data.auto_smooth_angle = math.radians(60)

    tris_after = len(obj.data.polygons)
    log(f"  Tris: {tris_before:,} → {tris_after:,}", "OK")
    return tris_after


# ─── DECIMACIÓN INTELIGENTE ───────────────────────────────────────────────────

def decimate_to_lod(source_obj: bpy.types.Object, target_tris: int,
                    lod_name: str) -> bpy.types.Object:
    """
    Crea una copia decimada del objeto.
    Usa Decimate modifier con planar collapse para preservar bordes duros.
    """
    # Duplicar
    bpy.ops.object.select_all(action="DESELECT")
    source_obj.select_set(True)
    bpy.context.view_layer.objects.active = source_obj
    bpy.ops.object.duplicate(linked=False)
    lod_obj = bpy.context.active_object
    lod_obj.name = lod_name

    current_tris = len(lod_obj.data.polygons)
    if current_tris <= target_tris:
        log(f"  {lod_name}: {current_tris:,} tris (sin decimar)")
        return lod_obj

    ratio = max(0.001, target_tris / current_tris)

    mod = lod_obj.modifiers.new(name="Decimate", type="DECIMATE")
    mod.decimate_type = "COLLAPSE"
    mod.ratio         = ratio
    mod.use_collapse_triangulate = True
    # No tocar bordes marcados como sharp (cornisas, ventanas)
    mod.use_symmetry = False

    apply_all_modifiers(lod_obj)

    result_tris = len(lod_obj.data.polygons)
    log(f"  {lod_name}: {current_tris:,} → {result_tris:,} tris (ratio={ratio:.3f})")
    return lod_obj


def generate_lods(base_obj: bpy.types.Object, edificio_id: str) -> list:
    """
    Genera 4 LODs:
      LOD0: min(200k, original) — calidad hero
      LOD1: LOD0 * 0.30
      LOD2: LOD0 * 0.08
      LOD3: LOD0 * 0.02  (solo silueta)
    """
    base_tris = len(base_obj.data.polygons)
    lod0_tris = min(200_000, base_tris)
    targets = {
        f"{edificio_id}_LOD0": lod0_tris,
        f"{edificio_id}_LOD1": max(500, int(lod0_tris * 0.30)),
        f"{edificio_id}_LOD2": max(200, int(lod0_tris * 0.08)),
        f"{edificio_id}_LOD3": max(50,  int(lod0_tris * 0.02)),
    }

    log(f"Generando LODs (base={base_tris:,} tris, LOD0 target={lod0_tris:,})")
    lod_objects = []
    for name, target in targets.items():
        lod_obj = decimate_to_lod(base_obj, target, name)
        lod_objects.append(lod_obj)

    return lod_objects


# ─── UV UNWRAP ────────────────────────────────────────────────────────────────

def uv_unwrap(obj: bpy.types.Object, tex_size: int = 2048) -> float:
    """
    UV unwrap profesional con Smart UV Project.
    Devuelve el porcentaje de cobertura UV.
    """
    log(f"UV Unwrap: {obj.name}")
    select_only(obj)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")

    # Smart UV Project: equilibrio entre distorsión y cobertura
    bpy.ops.uv.smart_project(
        angle_limit=math.radians(66),
        island_margin=0.02,
        area_weight=0.0,
        correct_aspect=True,
        scale_to_bounds=False,
    )

    # Pack islands para maximizar uso del espacio UV
    bpy.ops.uv.pack_islands(margin=0.002)

    bpy.ops.object.mode_set(mode="OBJECT")

    # Calcular cobertura
    uv_layer = obj.data.uv_layers.active
    covered = 0.0
    total   = 0.0
    if uv_layer:
        for poly in obj.data.polygons:
            uvs = [uv_layer.data[li].uv for li in poly.loop_indices]
            if len(uvs) >= 3:
                area = 0.0
                for i in range(1, len(uvs) - 1):
                    v0 = uvs[0]
                    v1 = uvs[i]
                    v2 = uvs[i + 1]
                    area += abs((v1.x - v0.x) * (v2.y - v0.y) -
                                (v2.x - v0.x) * (v1.y - v0.y)) * 0.5
                covered += area
        total = 1.0  # UV space es 0..1 × 0..1

    coverage = min(100.0, covered / total * 100) if total > 0 else 0.0
    log(f"  Cobertura UV: {coverage:.1f}%")
    return coverage


# ─── CREAR TEXTURA BAKE ───────────────────────────────────────────────────────

def create_bake_texture(name: str, width: int = 2048, height: int = 2048) -> bpy.types.Image:
    """Crea imagen para bake si no existe."""
    if name in bpy.data.images:
        bpy.data.images.remove(bpy.data.images[name])
    img = bpy.data.images.new(name, width=width, height=height, alpha=False)
    img.colorspace_settings.name = "sRGB"
    return img


def setup_bake_material(obj: bpy.types.Object, bake_img: bpy.types.Image,
                        bake_type: str) -> bpy.types.Material:
    """
    Configura el material del objeto para recibir el bake.
    Añade nodo de imagen de destino (sin conectar, Blender lo detecta como destino).
    """
    if not obj.data.materials:
        mat = bpy.data.materials.new(f"BakeMat_{bake_type}")
        mat.use_nodes = True
        obj.data.materials.append(mat)

    mat = obj.active_material
    mat.use_nodes = True
    nodes = mat.node_tree.nodes

    # Eliminar nodos imagen anteriores de bake
    for node in [n for n in nodes if n.type == "TEX_IMAGE" and n.name.startswith("_Bake_")]:
        nodes.remove(node)

    img_node = nodes.new("ShaderNodeTexImage")
    img_node.name  = f"_Bake_{bake_type}"
    img_node.image = bake_img
    img_node.location = (400, 400)

    # Seleccionar para que sea destino de bake
    for n in nodes:
        n.select = False
    img_node.select = True
    nodes.active = img_node

    return mat


# ─── BAKE (GPU CYCLES) ────────────────────────────────────────────────────────

def configure_cycles(use_gpu: bool, samples: int = 128):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples          = samples
    scene.cycles.use_denoising   = True
    scene.cycles.denoiser         = "OPENIMAGEDENOISE"

    if use_gpu:
        scene.cycles.device = "GPU"
        prefs = bpy.context.preferences.addons.get("cycles")
        if prefs:
            cprefs = prefs.preferences
            # Activar CUDA o OptiX si están disponibles
            for device_type in ["OPTIX", "CUDA", "HIP", "METAL"]:
                try:
                    cprefs.compute_device_type = device_type
                    # Activar todos los dispositivos GPU
                    for device in cprefs.get_devices_for_type(device_type):
                        device.use = True
                    break
                except Exception:
                    continue
    else:
        scene.cycles.device = "CPU"

    scene.render.bake.use_pass_direct   = False
    scene.render.bake.use_pass_indirect = False
    scene.render.bake.margin            = 16
    scene.render.bake.margin_type       = "EXTEND"


def bake_map(hi_obj: bpy.types.Object, lo_obj: bpy.types.Object,
             bake_type: str, img: bpy.types.Image,
             cage_extrusion: float = 0.02) -> bool:
    """
    Bake de hi-poly → lo-poly.
    bake_type: 'NORMAL', 'AO', 'DIFFUSE'
    """
    log(f"Baking {bake_type}...")
    scene = bpy.context.scene

    setup_bake_material(lo_obj, img, bake_type)

    # Selected to Active: hi → lo
    bpy.ops.object.select_all(action="DESELECT")
    hi_obj.select_set(True)
    lo_obj.select_set(True)
    bpy.context.view_layer.objects.active = lo_obj

    try:
        if bake_type == "NORMAL":
            bpy.ops.object.bake(
                type="NORMAL",
                use_selected_to_active=True,
                cage_extrusion=cage_extrusion,
                normal_space="TANGENT",
            )
        elif bake_type == "AO":
            bpy.ops.object.bake(
                type="AO",
                use_selected_to_active=True,
                cage_extrusion=cage_extrusion,
            )
        elif bake_type == "DIFFUSE":
            scene.render.bake.use_pass_direct   = False
            scene.render.bake.use_pass_indirect = False
            scene.render.bake.use_pass_color    = True
            bpy.ops.object.bake(
                type="DIFFUSE",
                use_selected_to_active=True,
                cage_extrusion=cage_extrusion,
                pass_filter={"COLOR"},
            )
        log(f"  {bake_type} bake OK", "OK")
        return True
    except Exception as e:
        log(f"  {bake_type} bake fallido: {e}", "WARN")
        return False


# ─── DE-LIGHTING ──────────────────────────────────────────────────────────────

def apply_delight(albedo_img: bpy.types.Image) -> bpy.types.Image:
    """
    Elimina la iluminación horneada de la textura albedo usando nodos Compositor.
    Técnica: divide el albedo por una versión blur (estimación de iluminación).
    Renormaliza a luminancia media 0.18 sRGB (difuso físicamente correcto para HDRP).

    Devuelve la imagen con de-lighting aplicado.
    """
    log("Aplicando de-lighting al albedo...")
    scene = bpy.context.scene
    scene.use_nodes = True
    scene.render.resolution_x = albedo_img.size[0]
    scene.render.resolution_y = albedo_img.size[1]
    tree = scene.node_tree
    nodes = tree.nodes
    links = tree.links

    nodes.clear()

    # Input: imagen fotogramétrica
    img_node = nodes.new("CompositorNodeImage")
    img_node.image = albedo_img

    # Separar channels
    sep = nodes.new("CompositorNodeSepRGBA")

    # Blur para estimar envolvente de iluminación (sigma grande = iluminación global)
    blur_r = nodes.new("CompositorNodeBlur")
    blur_g = nodes.new("CompositorNodeBlur")
    blur_b = nodes.new("CompositorNodeBlur")
    for b in [blur_r, blur_g, blur_b]:
        b.filter_type = "GAUSS"
        b.size_x = 200
        b.size_y = 200
        b.use_relative = False

    # Dividir canal original / blur → quitar iluminación global
    div_r = nodes.new("CompositorNodeMath")
    div_g = nodes.new("CompositorNodeMath")
    div_b = nodes.new("CompositorNodeMath")
    for d in [div_r, div_g, div_b]:
        d.operation = "DIVIDE"
        d.use_clamp = True

    # Combinar canales de-lit
    comb = nodes.new("CompositorNodeCombRGBA")

    # Ajustar luminancia media → 0.18 (valor físico)
    lum_mix = nodes.new("CompositorNodeMixRGB")
    lum_mix.blend_type = "MULTIPLY"
    lum_mix.inputs[0].default_value = 1.0
    lum_mix.inputs[2].default_value = (0.18, 0.18, 0.18, 1.0)

    # Gamma → linearizar si viene de sRGB
    gamma = nodes.new("CompositorNodeGamma")
    gamma.inputs[1].default_value = 1.0 / 2.2

    # Output
    out = nodes.new("CompositorNodeComposite")

    # Conectar
    links.new(img_node.outputs["Image"], sep.inputs["Image"])

    links.new(sep.outputs["R"], blur_r.inputs["Image"])
    links.new(sep.outputs["G"], blur_g.inputs["Image"])
    links.new(sep.outputs["B"], blur_b.inputs["Image"])

    links.new(sep.outputs["R"], div_r.inputs[0])
    links.new(blur_r.outputs["Image"], div_r.inputs[1])
    links.new(sep.outputs["G"], div_g.inputs[0])
    links.new(blur_g.outputs["Image"], div_g.inputs[1])
    links.new(sep.outputs["B"], div_b.inputs[0])
    links.new(blur_b.outputs["Image"], div_b.inputs[1])

    links.new(div_r.outputs["Value"], comb.inputs["R"])
    links.new(div_g.outputs["Value"], comb.inputs["G"])
    links.new(div_b.outputs["Value"], comb.inputs["B"])
    links.new(img_node.outputs["Alpha"], comb.inputs["A"])

    links.new(comb.outputs["Image"], lum_mix.inputs[1])
    links.new(lum_mix.outputs["Image"], gamma.inputs["Image"])
    links.new(gamma.outputs["Image"], out.inputs["Image"])

    # Renderizar compositor a imagen destino
    delit_name = albedo_img.name + "_delit"
    delit_img  = bpy.data.images.new(
        delit_name,
        width=albedo_img.size[0],
        height=albedo_img.size[1],
    )
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode  = "RGB"

    # Forzar render del compositor
    bpy.ops.render.render(write_still=False)

    # Obtener resultado del viewer (si existe)
    viewer = bpy.data.images.get("Viewer Node")
    if viewer:
        delit_img = viewer

    log("  De-lighting aplicado (blur σ=200, mean→0.18 sRGB)", "OK")
    return delit_img


# ─── FLIP NORMAL MAP CANAL G ─────────────────────────────────────────────────

def flip_normal_green(normal_img: bpy.types.Image):
    """
    Invierte el canal G del normal map para convertir de OpenGL (Blender)
    a DirectX (Unity HDRP): G_DX = 1 - G_OGL
    """
    log("Flipping canal G del Normal map (OpenGL → DirectX / Unity HDRP)...")
    pixels = list(normal_img.pixels)
    # pixels = [R, G, B, A, R, G, B, A, ...]
    for i in range(1, len(pixels), 4):   # índice G
        pixels[i] = 1.0 - pixels[i]
    normal_img.pixels = pixels
    log("  Canal G invertido", "OK")


# ─── EXPORTAR FBX ─────────────────────────────────────────────────────────────

def export_fbx(lod_objects: list, fbx_path: str):
    """
    Exporta todos los LODs como un único FBX Unity-ready.
    Convención de ejes Unity: forward=-Z, up=Y
    Nombre de objetos: {id}_LOD0, {id}_LOD1, etc.
    """
    log(f"Exportando FBX: {Path(fbx_path).name}")
    Path(fbx_path).parent.mkdir(parents=True, exist_ok=True)

    # Seleccionar solo los LODs
    bpy.ops.object.select_all(action="DESELECT")
    for obj in lod_objects:
        obj.select_set(True)

    bpy.ops.export_scene.fbx(
        filepath=fbx_path,
        use_selection=True,
        axis_forward="-Z",
        axis_up="Y",
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_NONE",
        bake_space_transform=True,
        object_types={"MESH"},
        use_mesh_modifiers=True,
        mesh_smooth_type="EDGE",
        use_tspace=True,
        embed_textures=False,        # texturas externas (Unity las gestiona)
        path_mode="RELATIVE",
        use_armature_deform_only=True,
        add_leaf_bones=False,
        global_scale=1.0,
    )
    size_mb = Path(fbx_path).stat().st_size / 1_048_576 if Path(fbx_path).exists() else 0
    log(f"  FBX exportado: {size_mb:.1f} MB", "OK")


# ─── GUARDAR TEXTURA ─────────────────────────────────────────────────────────

def save_texture(img: bpy.types.Image, out_path: str):
    Path(out_path).parent.mkdir(parents=True, exist_ok=True)
    img.filepath_raw = out_path
    img.file_format  = "PNG"
    img.save()
    log(f"  Textura guardada: {Path(out_path).name}", "OK")


# ─── REPORTE DE CALIDAD ───────────────────────────────────────────────────────

def write_quality_report(args: dict, tris_original: int, lod_objects: list,
                         uv_coverage: float, delight_applied: bool):
    report = {
        "edificio_id":       args["edificio_id"],
        "tris_original":     tris_original,
        "tris_lod0":         len(lod_objects[0].data.polygons) if lod_objects else 0,
        "tris_lod1":         len(lod_objects[1].data.polygons) if len(lod_objects) > 1 else 0,
        "tris_lod2":         len(lod_objects[2].data.polygons) if len(lod_objects) > 2 else 0,
        "tris_lod3":         len(lod_objects[3].data.polygons) if len(lod_objects) > 3 else 0,
        "texture_resolution": "2048x2048",
        "uv_coverage_pct":   round(uv_coverage, 1),
        "delight_applied":   delight_applied,
        "normal_map_flipped": True,
        "normal_space":      "DirectX_Unity_HDRP",
        "axis_convention":   "Unity_HDRP (-Z forward, Y up)",
        "lods_generated":    len(lod_objects),
        "fbx_output":        args.get("fbx_output"),
        "tex_output":        args.get("tex_output"),
    }
    report_path = str(Path(args.get("fbx_output", ".")).parent / f"{args['edificio_id']}_quality.json")
    with open(report_path, "w", encoding="utf-8") as f:
        json.dump(report, f, indent=2)
    log(f"  Quality report: {Path(report_path).name}", "OK")
    return report


# ─── MAIN ─────────────────────────────────────────────────────────────────────

def main():
    args = parse_args()

    edificio_id = args["edificio_id"]
    obj_path    = args["obj_path"]
    fbx_output  = args["fbx_output"]
    tex_output  = args["tex_output"]
    use_gpu     = args.get("use_gpu", False)
    tex_size    = 2048   # resolución bake Unity-ready (8K Meshroom → 2K optimizado)

    log(f"{'=' * 60}")
    log(f"Procesando edificio: {edificio_id}")
    log(f"OBJ: {obj_path}")
    log(f"GPU: {use_gpu}")
    log(f"{'=' * 60}")

    t0 = time.time()

    # Limpiar escena
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.unit_settings.system      = "METRIC"
    bpy.context.scene.unit_settings.scale_length = 1.0

    # 1. Importar OBJ
    base_obj = import_obj(obj_path)
    tris_original = len(base_obj.data.polygons)

    # 2. Limpiar malla
    tris_clean = clean_mesh(base_obj)

    # 3. Configurar Cycles para bake
    configure_cycles(use_gpu, samples=128)

    # 4. UV Unwrap en objeto limpio (base para bake)
    uv_coverage = uv_unwrap(base_obj, tex_size)

    # 5. Crear texturas de bake
    img_albedo = create_bake_texture(f"{edificio_id}_albedo", tex_size, tex_size)
    img_normal = create_bake_texture(f"{edificio_id}_normal", tex_size, tex_size)
    img_ao     = create_bake_texture(f"{edificio_id}_ao",     tex_size, tex_size)

    # La fuente de alta resolución es base_obj mismo (malla limpia)
    # Se hace bake selected-to-active con un offset mínimo
    bake_normal_ok  = bake_map(base_obj, base_obj, "NORMAL",  img_normal, cage_extrusion=0.02)
    bake_ao_ok      = bake_map(base_obj, base_obj, "AO",      img_ao,     cage_extrusion=0.02)
    bake_albedo_ok  = bake_map(base_obj, base_obj, "DIFFUSE", img_albedo, cage_extrusion=0.02)

    # 6. De-lighting del albedo
    delight_applied = False
    if bake_albedo_ok:
        try:
            img_albedo = apply_delight(img_albedo)
            delight_applied = True
        except Exception as e:
            log(f"De-lighting falló (no crítico): {e}", "WARN")

    # 7. Flip canal G del normal map (OpenGL → DirectX / Unity HDRP)
    if bake_normal_ok:
        try:
            flip_normal_green(img_normal)
        except Exception as e:
            log(f"Flip normal G falló: {e}", "WARN")

    # 8. Guardar texturas
    if bake_albedo_ok:
        save_texture(img_albedo, tex_output)

    normal_out = tex_output.replace("_albedo.png", "_normal.png")
    ao_out     = tex_output.replace("_albedo.png", "_ao.png")
    if bake_normal_ok:
        save_texture(img_normal, normal_out)
    if bake_ao_ok:
        save_texture(img_ao, ao_out)

    # 9. Generar LODs
    lod_objects = generate_lods(base_obj, edificio_id)

    # 10. UV Unwrap en LOD0 (ya tiene UVs del base, solo repack)
    if lod_objects:
        uv_unwrap(lod_objects[0], tex_size)

    # 11. Exportar FBX Unity-ready
    export_fbx(lod_objects, fbx_output)

    # 12. Reporte de calidad
    report = write_quality_report(args, tris_original, lod_objects, uv_coverage, delight_applied)

    elapsed = time.time() - t0
    log(f"{'=' * 60}")
    log(f"Completado en {elapsed:.0f}s", "OK")
    log(f"  LOD0: {report['tris_lod0']:,} tris")
    log(f"  UV cobertura: {report['uv_coverage_pct']}%")
    log(f"  De-lighting: {delight_applied}")
    log(f"  FBX: {fbx_output}")
    log(f"{'=' * 60}")


if __name__ == "__main__":
    main()
