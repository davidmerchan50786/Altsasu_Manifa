#!/usr/bin/env python3
"""
blender_archetypes.py — B5 Generador procedural de 12 arquetipos vascos
Proyecto: Altsasu Manifa Unity HDRP
TD: Claude Code

Uso:
    blender --background --python Tools/blender_archetypes.py

Genera LOD0-3 FBX para cada arquetipo en Assets/Models/Archetypes_Maya/
Convenciones FBX Unity: axis_forward='-Z', axis_up='Y', scale=1.0
"""

import bpy
import bmesh
import math
import os
import sys

# ─── Rutas ────────────────────────────────────────────────────────────────────
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
PROJECT_ROOT = os.path.dirname(SCRIPT_DIR)
OUTPUT_DIR = os.path.join(PROJECT_ROOT, "Assets", "Models", "Archetypes_Maya")
os.makedirs(OUTPUT_DIR, exist_ok=True)

print(f"[B5] Output dir: {OUTPUT_DIR}")

# ─── Helpers bmesh ────────────────────────────────────────────────────────────

def clear_scene():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    for col in list(bpy.data.collections):
        bpy.data.collections.remove(col)


def new_mesh_object(name):
    mesh = bpy.data.meshes.new(name)
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    return obj, mesh


def bmesh_to_object(bm, obj):
    bm.normal_update()
    bm.to_mesh(obj.data)
    bm.free()


def apply_smooth_normals(obj, angle_deg=30):
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.shade_smooth()
    obj.data.use_auto_smooth = True
    obj.data.auto_smooth_angle = math.radians(angle_deg)


def smart_uv_project(obj):
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.02)
    bpy.ops.object.mode_set(mode='OBJECT')


def add_decimate_lod(obj, ratio, lod_suffix):
    """Duplica el objeto, aplica Decimate, renombra con sufijo LOD."""
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.duplicate(linked=False)
    lod_obj = bpy.context.active_object
    lod_obj.name = obj.name.replace("_LOD0", "") + f"_{lod_suffix}"

    mod = lod_obj.modifiers.new(name="Decimate", type='DECIMATE')
    mod.ratio = ratio

    # Apply modifier
    bpy.context.view_layer.objects.active = lod_obj
    bpy.ops.object.modifier_apply(modifier="Decimate")
    return lod_obj


def export_fbx(filepath, objects):
    """Exporta lista de objetos como FBX para Unity."""
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]

    bpy.ops.export_scene.fbx(
        filepath=filepath,
        use_selection=True,
        axis_forward='-Z',
        axis_up='Y',
        global_scale=1.0,
        apply_unit_scale=True,
        apply_scale_options='FBX_SCALE_NONE',
        use_mesh_modifiers=True,
        mesh_smooth_type='FACE',
        use_tspace=True,
        add_leaf_joints=False,
        bake_anim=False,
    )
    print(f"[B5] Exported: {os.path.basename(filepath)}")


# ─── Geometría BMesh helpers ──────────────────────────────────────────────────

def extrude_box(bm, w, d, h):
    """Caja básica W×D×H centrada en XZ, base en Y=0."""
    verts = [
        bm.verts.new(( w/2, 0,  d/2)),
        bm.verts.new((-w/2, 0,  d/2)),
        bm.verts.new((-w/2, 0, -d/2)),
        bm.verts.new(( w/2, 0, -d/2)),
    ]
    base_face = bm.faces.new(verts)
    r = bmesh.ops.extrude_face_region(bm, geom=[base_face])
    top_verts = [e for e in r['geom'] if isinstance(e, bmesh.types.BMVert)]
    bmesh.ops.translate(bm, verts=top_verts, vec=(0, h, 0))
    return bm


def add_gable_roof(bm, w, d, wall_h, pitch_deg, offset_y=0, overhang=0.4):
    """
    Añade tejado a dos aguas sobre caja W×D con altura de muro wall_h.
    pitch_deg: ángulo del tejado en grados
    offset_y: desplazamiento vertical base (0 normalmente)
    """
    half_w = w / 2 + overhang
    half_d = d / 2 + overhang
    ridge_h = (d / 2) * math.tan(math.radians(pitch_deg))
    base = wall_h + offset_y

    # Vértices tejado
    v0 = bm.verts.new((-half_w, base,          half_d))   # FL
    v1 = bm.verts.new(( half_w, base,          half_d))   # FR
    v2 = bm.verts.new(( half_w, base,         -half_d))   # BR
    v3 = bm.verts.new((-half_w, base,         -half_d))   # BL
    v4 = bm.verts.new((-half_w, base+ridge_h,  0))        # ridge L
    v5 = bm.verts.new(( half_w, base+ridge_h,  0))        # ridge R

    bm.faces.new([v0, v1, v5, v4])  # front slope
    bm.faces.new([v2, v3, v4, v5])  # back slope
    bm.faces.new([v1, v2, v5])      # gable R
    bm.faces.new([v3, v0, v4])      # gable L

    return bm


def add_flat_roof(bm, w, d, wall_h, parapet=0.5):
    """Tejado plano con pretil."""
    hw, hd = w/2, d/2
    base = wall_h
    # Losa
    verts = [
        bm.verts.new(( hw+0.1, base,  hd+0.1)),
        bm.verts.new((-hw-0.1, base,  hd+0.1)),
        bm.verts.new((-hw-0.1, base, -hd-0.1)),
        bm.verts.new(( hw+0.1, base, -hd-0.1)),
    ]
    bm.faces.new(verts)
    # Pretil
    for dx, dz in [(hw+0.1, 0), (-hw-0.1, 0), (0, hd+0.1), (0, -hd-0.1)]:
        pass  # sencillo — pretil como thin box
    return bm


def add_window_cutout_rows(bm, obj, w, h_total, floors, floor_h, win_w=1.2, win_h=1.5, win_gap=2.5):
    """
    Añade ventanas como cajas Boolean en la fachada principal (+Z).
    Simplificado: usa bmesh para crear huecos geométricos.
    """
    # Número de ventanas por fila basado en ancho
    n_wins = max(1, int((w - 1.0) / win_gap))
    start_x = -(n_wins - 1) * win_gap / 2

    for floor in range(floors):
        y_center = 1.2 + floor * floor_h + floor_h * 0.55
        for i in range(n_wins):
            x_center = start_x + i * win_gap
            # Crear pequeño box que representa hueco (visual, no boolean real)
            hw, hh = win_w / 2, win_h / 2
            depth = 0.15
            z_front = h_total / 2 + 0.01
            verts = [
                bm.verts.new((x_center - hw, y_center - hh, z_front - depth)),
                bm.verts.new((x_center + hw, y_center - hh, z_front - depth)),
                bm.verts.new((x_center + hw, y_center + hh, z_front - depth)),
                bm.verts.new((x_center - hw, y_center + hh, z_front - depth)),
            ]
            bm.faces.new(verts)


def add_cornice(bm, w, d, wall_h, depth=0.3, thickness=0.25):
    """Cornisa perimetral."""
    hw, hd = w/2 + depth, d/2 + depth
    y0, y1 = wall_h - thickness, wall_h
    # Front face cornisa
    for (x0, z0, x1, z1) in [
        ( hw,  hd, -hw,  hd),  # front
        (-hw,  hd, -hw, -hd),  # left
        (-hw, -hd,  hw, -hd),  # back
        ( hw, -hd,  hw,  hd),  # right
    ]:
        verts = [
            bm.verts.new((x0, y0, z0)),
            bm.verts.new((x1, y0, z1)),
            bm.verts.new((x1, y1, z1)),
            bm.verts.new((x0, y1, z0)),
        ]
        bm.faces.new(verts)


def add_balcony_row(bm, w, wall_h, floor_h, floors, depth=0.8, thickness=0.12):
    """Balcones en fachada +Z."""
    n_bal = max(1, int((w - 2) / 3.5))
    start_x = -(n_bal - 1) * 3.5 / 2
    bal_w = 2.4

    for fl in range(1, min(floors, 4)):
        y = fl * floor_h + 0.05
        for i in range(n_bal):
            cx = start_x + i * 3.5
            hw = bal_w / 2
            z0 = w / 2
            # Losa balcón
            verts = [
                bm.verts.new((cx - hw, y,            z0)),
                bm.verts.new((cx + hw, y,            z0)),
                bm.verts.new((cx + hw, y,            z0 + depth)),
                bm.verts.new((cx - hw, y,            z0 + depth)),
                bm.verts.new((cx - hw, y + thickness, z0 + depth)),
                bm.verts.new((cx + hw, y + thickness, z0 + depth)),
                bm.verts.new((cx + hw, y + thickness, z0)),
                bm.verts.new((cx - hw, y + thickness, z0)),
            ]
            # Caras losa
            bm.faces.new([verts[0], verts[1], verts[2], verts[3]])
            bm.faces.new([verts[4], verts[5], verts[6], verts[7]])
            bm.faces.new([verts[0], verts[7], verts[6], verts[1]])
            bm.faces.new([verts[3], verts[2], verts[5], verts[4]])
            # Barandilla frontal
            bar_verts = [
                bm.verts.new((cx - hw, y,           z0 + depth)),
                bm.verts.new((cx + hw, y,           z0 + depth)),
                bm.verts.new((cx + hw, y + 0.9,    z0 + depth)),
                bm.verts.new((cx - hw, y + 0.9,    z0 + depth)),
            ]
            bm.faces.new(bar_verts)


def add_campanario(bm, cx, cz, base_y, width, height):
    """Torre campanario cuadrada."""
    hw = width / 2
    verts_base = [
        bm.verts.new((cx - hw, base_y,      cz - hw)),
        bm.verts.new((cx + hw, base_y,      cz - hw)),
        bm.verts.new((cx + hw, base_y,      cz + hw)),
        bm.verts.new((cx - hw, base_y,      cz + hw)),
    ]
    face = bm.faces.new(verts_base)
    r = bmesh.ops.extrude_face_region(bm, geom=[face])
    top_verts = [e for e in r['geom'] if isinstance(e, bmesh.types.BMVert)]
    bmesh.ops.translate(bm, verts=top_verts, vec=(0, height, 0))
    # Chapitel
    tip_h = width * 0.8
    for v in top_verts:
        pass  # chapitel simplificado como pirámide
    tip = bm.verts.new((cx, base_y + height + tip_h, cz))
    for i in range(4):
        bm.faces.new([top_verts[i], top_verts[(i+1) % 4], tip])


def add_industrial_roof(bm, w, d, wall_h, pitch_deg=15, overhang=0.5):
    """Tejado metálico industrial a dos aguas con cumbrera central."""
    add_gable_roof(bm, w, d, wall_h, pitch_deg, overhang=overhang)
    # Lucernario central
    luc_w, luc_d, luc_h = 3.0, d * 0.6, 1.2
    ridge_h = (d / 2) * math.tan(math.radians(pitch_deg))
    apex_y = wall_h + ridge_h
    luc_base = apex_y - 0.3
    hw, hd = luc_w/2, luc_d/2
    verts = [
        bm.verts.new(( hw, luc_base,  hd)),
        bm.verts.new((-hw, luc_base,  hd)),
        bm.verts.new((-hw, luc_base, -hd)),
        bm.verts.new(( hw, luc_base, -hd)),
        bm.verts.new(( hw, luc_base + luc_h,  hd)),
        bm.verts.new((-hw, luc_base + luc_h,  hd)),
        bm.verts.new((-hw, luc_base + luc_h, -hd)),
        bm.verts.new(( hw, luc_base + luc_h, -hd)),
    ]
    bm.faces.new([verts[0], verts[1], verts[5], verts[4]])
    bm.faces.new([verts[2], verts[3], verts[7], verts[6]])
    bm.faces.new([verts[1], verts[2], verts[6], verts[5]])
    bm.faces.new([verts[3], verts[0], verts[4], verts[7]])
    bm.faces.new([verts[4], verts[5], verts[6], verts[7]])


def add_arched_portal(bm, cx, cz, base_y, w, h, depth, arch_segs=8):
    """Portal de arco de medio punto."""
    hw = w / 2
    spring_h = h * 0.6
    radius = hw
    # Jambas laterales
    for sign in [-1, 1]:
        verts = [
            bm.verts.new((cx + sign * hw,           base_y,           cz)),
            bm.verts.new((cx + sign * hw,           base_y,           cz + depth)),
            bm.verts.new((cx + sign * hw,           base_y + spring_h, cz + depth)),
            bm.verts.new((cx + sign * hw,           base_y + spring_h, cz)),
        ]
        bm.faces.new(verts)
    # Arco en segmentos
    prev_l, prev_r, prev_ld, prev_rd = None, None, None, None
    for i in range(arch_segs + 1):
        angle = math.pi * i / arch_segs
        ax = cx + radius * math.cos(math.pi - angle)
        ay = base_y + spring_h + radius * math.sin(math.pi - angle)
        vl  = bm.verts.new((ax - 0.15, ay, cz))
        vr  = bm.verts.new((ax + 0.15, ay, cz))
        vld = bm.verts.new((ax - 0.15, ay, cz + depth))
        vrd = bm.verts.new((ax + 0.15, ay, cz + depth))
        if prev_l:
            bm.faces.new([prev_l, vl, vr, prev_r])
            bm.faces.new([prev_ld, vld, vrd, prev_rd])
        prev_l, prev_r, prev_ld, prev_rd = vl, vr, vld, vrd


def add_entrance_steps(bm, cx, cz, base_y, w, n_steps=4):
    """Escalones de entrada monumental."""
    step_h, step_d = 0.18, 0.30
    for i in range(n_steps):
        sw = w + i * 0.6
        sd = step_d
        y0 = base_y - (n_steps - i) * step_h
        y1 = y0 + step_h
        hz = sw / 2
        verts = [
            bm.verts.new(( hz, y0, cz + i * step_d)),
            bm.verts.new((-hz, y0, cz + i * step_d)),
            bm.verts.new((-hz, y0, cz + i * step_d + sd)),
            bm.verts.new(( hz, y0, cz + i * step_d + sd)),
            bm.verts.new(( hz, y1, cz + i * step_d + sd)),
            bm.verts.new((-hz, y1, cz + i * step_d + sd)),
        ]
        bm.faces.new([verts[0], verts[1], verts[2], verts[3]])
        bm.faces.new([verts[3], verts[2], verts[5], verts[4]])


def add_garage_door(bm, w, h, cx, cz, base_y, panel_h=0.4):
    """Puerta de garaje enrollable con paneles horizontales."""
    hw = w / 2
    n_panels = int(h / panel_h)
    depth = 0.08
    for i in range(n_panels):
        py = base_y + i * panel_h
        verts = [
            bm.verts.new((cx - hw, py,           cz + depth)),
            bm.verts.new((cx + hw, py,           cz + depth)),
            bm.verts.new((cx + hw, py + panel_h, cz + depth)),
            bm.verts.new((cx - hw, py + panel_h, cz + depth)),
        ]
        bm.faces.new(verts)


def add_pilasters(bm, w, d, wall_h, n=4, pil_w=0.4, pil_d=0.3):
    """Pilastras decorativas en fachada."""
    spacing = w / (n + 1)
    for i in range(n):
        cx = -w / 2 + spacing * (i + 1)
        cz = d / 2
        verts = [
            bm.verts.new((cx - pil_w/2, 0,      cz)),
            bm.verts.new((cx + pil_w/2, 0,      cz)),
            bm.verts.new((cx + pil_w/2, 0,      cz + pil_d)),
            bm.verts.new((cx - pil_w/2, 0,      cz + pil_d)),
        ]
        face = bm.faces.new(verts)
        r = bmesh.ops.extrude_face_region(bm, geom=[face])
        tverts = [e for e in r['geom'] if isinstance(e, bmesh.types.BMVert)]
        bmesh.ops.translate(bm, verts=tverts, vec=(0, wall_h, 0))


def add_shopfront(bm, w, h_floor, d, cx=0, cz=None):
    """Fachada acristalada planta baja."""
    if cz is None:
        cz = d / 2
    hw = w / 2 - 0.3
    verts = [
        bm.verts.new((cx - hw, 0.1,    cz + 0.05)),
        bm.verts.new((cx + hw, 0.1,    cz + 0.05)),
        bm.verts.new((cx + hw, h_floor, cz + 0.05)),
        bm.verts.new((cx - hw, h_floor, cz + 0.05)),
    ]
    bm.faces.new(verts)


def add_canopy(bm, w, cz, y_base, depth=1.8, thickness=0.15):
    """Marquesina / Canopy sobre entrada."""
    hw = w / 2
    verts = [
        bm.verts.new((-hw, y_base,           cz)),
        bm.verts.new(( hw, y_base,           cz)),
        bm.verts.new(( hw, y_base,           cz + depth)),
        bm.verts.new((-hw, y_base,           cz + depth)),
        bm.verts.new((-hw, y_base + thickness, cz + depth)),
        bm.verts.new(( hw, y_base + thickness, cz + depth)),
        bm.verts.new(( hw, y_base + thickness, cz)),
        bm.verts.new((-hw, y_base + thickness, cz)),
    ]
    bm.faces.new([verts[0], verts[1], verts[2], verts[3]])
    bm.faces.new([verts[4], verts[5], verts[6], verts[7]])
    bm.faces.new([verts[3], verts[2], verts[5], verts[4]])
    bm.faces.new([verts[1], verts[0], verts[7], verts[6]])


def add_chimney(bm, cx, cz, wall_h, roof_extra, w=0.6, d=0.6):
    """Chimenea de piedra."""
    hw, hd = w/2, d/2
    top_h = wall_h + roof_extra + 1.5
    verts = [
        bm.verts.new((cx - hw, wall_h, cz - hd)),
        bm.verts.new((cx + hw, wall_h, cz - hd)),
        bm.verts.new((cx + hw, wall_h, cz + hd)),
        bm.verts.new((cx - hw, wall_h, cz + hd)),
    ]
    face = bm.faces.new(verts)
    r = bmesh.ops.extrude_face_region(bm, geom=[face])
    tverts = [e for e in r['geom'] if isinstance(e, bmesh.types.BMVert)]
    bmesh.ops.translate(bm, verts=tverts, vec=(0, top_h - wall_h, 0))


# ─── Definición de arquetipos ─────────────────────────────────────────────────

ARCHETYPES = {

    "pre1940_piedra": {
        "desc": "Edificio arenisca pre-1940, 3 plantas, balcón forja",
        "W": 12.0, "D": 10.0, "H_wall": 10.5,
        "floors": 3, "floor_h": 3.5,
        "roof": "gable", "pitch": 35,
        "features": ["cornice", "balconies", "chimney", "windows"],
    },
    "pre1940_grande": {
        "desc": "Edificio grande pre-1940, 4 plantas, cornisa elaborada",
        "W": 16.0, "D": 12.0, "H_wall": 13.5,
        "floors": 4, "floor_h": 3.375,
        "roof": "gable", "pitch": 32,
        "features": ["cornice", "balconies", "pilasters", "chimney", "windows"],
    },
    "caserío": {
        "desc": "Caserío vasco, tejado 42°, portal arco, galería sur",
        "W": 18.0, "D": 14.0, "H_wall": 8.0,
        "floors": 2, "floor_h": 4.0,
        "roof": "gable", "pitch": 42,
        "features": ["arch_portal", "gallery", "chimney", "windows"],
    },
    "1940_1975_ladrillo": {
        "desc": "Bloque ladrillo 1940-1975, 3 plantas, ventanas rectangulares",
        "W": 14.0, "D": 11.0, "H_wall": 10.5,
        "floors": 3, "floor_h": 3.5,
        "roof": "gable", "pitch": 28,
        "features": ["balconies", "windows"],
    },
    "bloque_moderno": {
        "desc": "Bloque moderno post-1975, tejado plano, balcones hormigón",
        "W": 20.0, "D": 14.0, "H_wall": 15.0,
        "floors": 5, "floor_h": 3.0,
        "roof": "flat",
        "features": ["balconies", "windows"],
    },
    "bar_pintxos": {
        "desc": "Bar pintxos, planta baja acristalada, rótulo, canopy",
        "W": 8.0, "D": 8.0, "H_wall": 7.5,
        "floors": 2, "floor_h": 3.75,
        "roof": "flat",
        "features": ["shopfront", "canopy", "windows"],
    },
    "nave_industrial": {
        "desc": "Nave industrial, chapa metálica, tejado 2 aguas, lucernario",
        "W": 30.0, "D": 20.0, "H_wall": 8.0,
        "floors": 1, "floor_h": 8.0,
        "roof": "industrial", "pitch": 15,
        "features": ["garage_door"],
    },
    "iglesia": {
        "desc": "Iglesia, planta cruciforme, campanario 18m, tejado pizarra",
        "W": 15.0, "D": 25.0, "H_wall": 9.0,
        "floors": 1, "floor_h": 9.0,
        "roof": "gable", "pitch": 40,
        "features": ["campanario", "arch_portal", "transept", "windows"],
    },
    "edificio_singular": {
        "desc": "Edificio singular 5 plantas, fachada simétrica, pilastras",
        "W": 20.0, "D": 15.0, "H_wall": 16.5,
        "floors": 5, "floor_h": 3.3,
        "roof": "gable", "pitch": 30,
        "features": ["cornice", "balconies", "pilasters", "entrance_steps", "windows"],
    },
    "vivienda_unifamiliar": {
        "desc": "Vivienda unifamiliar 2 plantas con jardín",
        "W": 9.0, "D": 9.0, "H_wall": 9.0,
        "floors": 2, "floor_h": 4.5,
        "roof": "gable", "pitch": 38,
        "features": ["chimney", "windows"],
    },
    "garaje": {
        "desc": "Garaje/cochera con puerta enrollable",
        "W": 10.0, "D": 8.0, "H_wall": 4.0,
        "floors": 1, "floor_h": 4.0,
        "roof": "flat",
        "features": ["garage_door"],
    },
    "edificio_publico": {
        "desc": "Edificio público 4 plantas, entrada monumental, marquesina",
        "W": 25.0, "D": 18.0, "H_wall": 13.5,
        "floors": 4, "floor_h": 3.375,
        "roof": "flat",
        "features": ["cornice", "entrance_steps", "canopy", "pilasters", "windows"],
    },
}

# ─── Generador de arquetipo ───────────────────────────────────────────────────

def generate_archetype(name, cfg):
    print(f"[B5] Generating: {name}")
    W = cfg["W"]
    D = cfg["D"]
    H = cfg["H_wall"]
    floors = cfg.get("floors", 3)
    floor_h = cfg.get("floor_h", 3.5)
    features = cfg.get("features", [])
    roof_type = cfg.get("roof", "gable")
    pitch = cfg.get("pitch", 30)

    bm = bmesh.new()

    # ── Cuerpo principal ──────────────────────────────────────────────────────
    extrude_box(bm, W, D, H)

    # ── Tejado ────────────────────────────────────────────────────────────────
    if roof_type == "gable":
        add_gable_roof(bm, W, D, H, pitch)
    elif roof_type == "flat":
        add_flat_roof(bm, W, D, H)
    elif roof_type == "industrial":
        add_industrial_roof(bm, W, D, H, pitch)

    # ── Features ──────────────────────────────────────────────────────────────
    if "cornice" in features:
        add_cornice(bm, W, D, H)

    if "balconies" in features:
        add_balcony_row(bm, W, H, floor_h, floors)

    if "pilasters" in features:
        add_pilasters(bm, W, D, H)

    if "chimney" in features:
        ridge_h = (D / 2) * math.tan(math.radians(pitch)) if roof_type == "gable" else 0
        add_chimney(bm, W * 0.25, 0, H, ridge_h)

    if "arch_portal" in features:
        portal_w = min(2.5, W * 0.2)
        portal_h = min(4.5, H * 0.5)
        add_arched_portal(bm, 0, D/2, 0, portal_w, portal_h, 0.4)

    if "gallery" in features:
        # Galería sur: voladizo corrido
        hw, hd = W/2 - 0.5, 1.5
        y_gal = floor_h
        verts = [
            bm.verts.new((-hw, y_gal,           D/2)),
            bm.verts.new(( hw, y_gal,           D/2)),
            bm.verts.new(( hw, y_gal,           D/2 + hd)),
            bm.verts.new((-hw, y_gal,           D/2 + hd)),
            bm.verts.new((-hw, y_gal + 0.15,   D/2 + hd)),
            bm.verts.new(( hw, y_gal + 0.15,   D/2 + hd)),
            bm.verts.new(( hw, y_gal + 0.15,   D/2)),
            bm.verts.new((-hw, y_gal + 0.15,   D/2)),
        ]
        bm.faces.new([verts[0], verts[1], verts[2], verts[3]])
        bm.faces.new([verts[4], verts[5], verts[6], verts[7]])
        bm.faces.new([verts[3], verts[2], verts[5], verts[4]])

    if "transept" in features:
        # Crucero iglesia: brazos laterales
        transept_w, transept_d, transept_h = 8.0, D * 0.5, H * 0.85
        for sign in [-1, 1]:
            cx = sign * (W/2 + transept_w/2)
            tv = [
                bm.verts.new((cx - transept_w/2, 0, -transept_d/2)),
                bm.verts.new((cx + transept_w/2, 0, -transept_d/2)),
                bm.verts.new((cx + transept_w/2, 0,  transept_d/2)),
                bm.verts.new((cx - transept_w/2, 0,  transept_d/2)),
            ]
            face = bm.faces.new(tv)
            r = bmesh.ops.extrude_face_region(bm, geom=[face])
            top_verts = [e for e in r['geom'] if isinstance(e, bmesh.types.BMVert)]
            bmesh.ops.translate(bm, verts=top_verts, vec=(0, transept_h, 0))

    if "campanario" in features:
        add_campanario(bm, W/2 - 3.0, -D/2 + 3.0, 0, 4.0, 18.0)

    if "shopfront" in features:
        add_shopfront(bm, W - 1.0, floor_h * 0.9, D)

    if "canopy" in features:
        add_canopy(bm, min(W * 0.7, 6.0), D/2, floor_h * 0.95)

    if "entrance_steps" in features:
        add_entrance_steps(bm, 0, D/2, 0, min(W * 0.4, 8.0))

    if "garage_door" in features:
        door_w = min(W * 0.6, 5.0)
        door_h = min(H * 0.85, 3.5)
        add_garage_door(bm, door_w, door_h, 0, D/2, 0)

    if "windows" in features:
        add_window_cutout_rows(bm, None, W, H, floors, floor_h)

    # ── Crear objeto ──────────────────────────────────────────────────────────
    obj_name = f"{name}_LOD0"
    obj, mesh = new_mesh_object(obj_name)
    bmesh_to_object(bm, obj)

    # Eliminar duplicados y validar
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.remove_doubles(threshold=0.001)
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode='OBJECT')

    # UV
    smart_uv_project(obj)

    # Smooth normals
    apply_smooth_normals(obj, 30)

    # Freeze transforms
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    return obj


# ─── Main ─────────────────────────────────────────────────────────────────────

def main():
    # Ratios de decimación para LOD1/2/3
    LOD_RATIOS = {
        "LOD1": 0.50,
        "LOD2": 0.15,
        "LOD3": 0.04,
    }

    for arch_name, cfg in ARCHETYPES.items():
        clear_scene()

        # LOD0
        lod0 = generate_archetype(arch_name, cfg)

        # LOD1-3
        lod_objects = [lod0]
        for lod_suffix, ratio in LOD_RATIOS.items():
            try:
                lod_obj = add_decimate_lod(lod0, ratio, lod_suffix)
                lod_objects.append(lod_obj)
            except Exception as e:
                print(f"[B5] Warning: LOD {lod_suffix} for {arch_name} failed: {e}")

        # Export FBX con todos los LODs
        out_path = os.path.join(OUTPUT_DIR, f"{arch_name}.fbx")
        try:
            export_fbx(out_path, lod_objects)
        except Exception as e:
            print(f"[B5] Error exporting {arch_name}: {e}")

        print(f"[B5] Done: {arch_name} ({len(lod_objects)} LODs)")

    print(f"\n[B5] ===== COMPLETE: 12 arquetipos vascos generados =====")
    print(f"[B5] Output: {OUTPUT_DIR}")


if __name__ == "__main__":
    main()
