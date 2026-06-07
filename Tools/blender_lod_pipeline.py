"""
blender_lod_pipeline.py — Altsasu Manifa B4
Pipeline Blender Python: buildings_fusion_final.json → mallas LOD0-3 → FBX Unity HDRP

Uso:
    blender --background --python Tools/blender_lod_pipeline.py -- \
        --input  Assets/AlsasuaData/buildings_fusion_final.json \
        --output Assets/Models/Buildings_Generated/ \
        --textures Assets/AlsasuaData/FacadeTextures/Processed/ \
        --max_buildings 100 \
        [--archetype pre1940_piedra]

Requiere Blender 3.6+ (bmesh, bpy.ops.export_scene.fbx).
"""

import sys
import os
import json
import math
import time
import argparse
import traceback

import bpy
import bmesh
from mathutils import Vector, Matrix

# ---------------------------------------------------------------------------
# Constantes
# ---------------------------------------------------------------------------

ROOF_ANGLE_DEG = {
    "pre1940_piedra":   35.0,
    "pre1940_ladrillo": 35.0,
    "casero":           42.0,
    "caserio":          42.0,
    "moderno":          28.0,
    "industrial":       15.0,
    "default":          32.0,
}

HEIGHT_MIN = 3.0          # metros mínimos forzados
CORNISA_H  = 0.12         # extrusión perimetral de cornisa
PLINTO_H   = 0.28         # zócalo base
ALERO_W    = 0.30         # vuelo alero tejado abuhardillado/hip
BORDILLO_H = 0.15         # bordillo tejado plano

LOD_DECIMATE = {
    "LOD0": None,          # original
    "LOD1": 0.50,
    "LOD2": 0.15,
    "LOD3": 0.04,
}

# ---------------------------------------------------------------------------
# Utilidades geométricas
# ---------------------------------------------------------------------------

def _signed_area_2d(pts):
    """Área con signo del polígono 2D (lista de [x, y])."""
    n = len(pts)
    a = 0.0
    for i in range(n):
        x0, y0 = pts[i]
        x1, y1 = pts[(i + 1) % n]
        a += x0 * y1 - x1 * y0
    return a * 0.5


def _is_convex_hull_needed(pts):
    """Detecta si el polígono es auto-intersectante mediante signo de cross-products."""
    n = len(pts)
    sign = None
    for i in range(n):
        ax, ay = pts[(i + 1) % n][0] - pts[i][0], pts[(i + 1) % n][1] - pts[i][1]
        bx, by = pts[(i + 2) % n][0] - pts[(i + 1) % n][0], pts[(i + 2) % n][1] - pts[(i + 1) % n][1]
        cross = ax * by - ay * bx
        if cross == 0.0:
            continue
        s = 1 if cross > 0 else -1
        if sign is None:
            sign = s
        elif sign != s:
            return True
    return False


def _convex_hull_2d(pts):
    """Graham scan convex hull en 2D, devuelve lista de [x, y]."""
    pts = sorted(set(map(tuple, pts)))
    if len(pts) <= 2:
        return [list(p) for p in pts]

    def cross(O, A, B):
        return (A[0] - O[0]) * (B[1] - O[1]) - (A[1] - O[1]) * (B[0] - O[0])

    lower = []
    for p in pts:
        while len(lower) >= 2 and cross(lower[-2], lower[-1], p) <= 0:
            lower.pop()
        lower.append(p)
    upper = []
    for p in reversed(pts):
        while len(upper) >= 2 and cross(upper[-2], upper[-1], p) <= 0:
            upper.pop()
        upper.append(p)
    hull = lower[:-1] + upper[:-1]
    return [list(p) for p in hull]


def _pca_axis_2d(pts):
    """
    PCA 2D sobre los puntos del footprint.
    Devuelve (eje_largo_normalizado, eje_corto_normalizado) como tuples (x, y).
    """
    n = len(pts)
    cx = sum(p[0] for p in pts) / n
    cy = sum(p[1] for p in pts) / n
    sxx = sum((p[0] - cx) ** 2 for p in pts)
    syy = sum((p[1] - cy) ** 2 for p in pts)
    sxy = sum((p[0] - cx) * (p[1] - cy) for p in pts)
    # eigenvalores de [[sxx, sxy],[sxy, syy]]
    trace = sxx + syy
    det   = sxx * syy - sxy * sxy
    disc  = math.sqrt(max(0.0, (trace / 2) ** 2 - det))
    l1 = trace / 2 + disc   # mayor
    # eigenvector del mayor eigenvalor
    if abs(sxy) > 1e-10:
        vx, vy = l1 - syy, sxy
    elif sxx >= syy:
        vx, vy = 1.0, 0.0
    else:
        vx, vy = 0.0, 1.0
    mag = math.sqrt(vx * vx + vy * vy)
    vx /= mag; vy /= mag
    return (vx, vy), (-vy, vx)


def _ensure_ccw(pts):
    """Garantiza orientación CCW (requerido por bmesh fill)."""
    if _signed_area_2d(pts) < 0:
        return list(reversed(pts))
    return pts

# ---------------------------------------------------------------------------
# Construcción bmesh del edificio
# ---------------------------------------------------------------------------

def crear_edificio_base(b):
    """
    Recibe dict de edificio de buildings_fusion_final.json.
    Devuelve bpy.types.Object con la malla LOD0 construida con bmesh.
    Devuelve None si el footprint es inválido.
    """
    bid     = str(b.get("id", "unknown"))
    fp_raw  = b.get("footprint_unity", [])
    height  = max(float(b.get("height_m", 5.0)), HEIGHT_MIN)
    roof_t  = b.get("roof_type", "gabled")
    archtyp = b.get("archetype", "default")

    # --- Validar footprint ---
    # Eliminar cierre redundante (último == primero)
    if len(fp_raw) > 1 and fp_raw[0] == fp_raw[-1]:
        fp_raw = fp_raw[:-1]

    if len(fp_raw) < 3:
        print(f"[WARN] {bid}: footprint < 3 puntos ({len(fp_raw)}), skip")
        return None

    # Coordenadas 2D del footprint en Unity XZ
    pts2d = [[p[0], p[1]] for p in fp_raw]

    # Fallback convex hull si self-intersectante
    if _is_convex_hull_needed(pts2d):
        print(f"[WARN] {bid}: polígono self-intersectante, usando convex hull")
        pts2d = _convex_hull_2d(pts2d)
        if len(pts2d) < 3:
            print(f"[WARN] {bid}: convex hull degenerado, skip")
            return None

    pts2d = _ensure_ccw(pts2d)
    n_pts = len(pts2d)

    # --- Crear malla con bmesh ---
    mesh = bpy.data.meshes.new(f"{bid}_LOD0_mesh")
    obj  = bpy.data.objects.new(f"{bid}_LOD0", mesh)
    bpy.context.collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj

    bm = bmesh.new()

    # === PLINTO (zócalo 0 → PLINTO_H) ===
    # Footprint base → anillo inferior
    verts_bot = [bm.verts.new(Vector((p[0], 0.0, p[1]))) for p in pts2d]

    # Anillo en cota plinto (con pequeña extrusión hacia afuera de 0.03m)
    PLINTO_OFFSET = 0.03
    cx = sum(p[0] for p in pts2d) / n_pts
    cz = sum(p[1] for p in pts2d) / n_pts
    verts_plinto = []
    for p in pts2d:
        dx, dz = p[0] - cx, p[1] - cz
        mag = math.sqrt(dx*dx + dz*dz) or 1.0
        nx, nz = dx/mag, dz/mag
        verts_plinto.append(bm.verts.new(Vector((
            p[0] + nx * PLINTO_OFFSET,
            PLINTO_H,
            p[1] + nz * PLINTO_OFFSET
        ))))

    # Cara inferior (suelo)
    bm.faces.new(verts_bot)
    # Caras laterales plinto
    for i in range(n_pts):
        j = (i + 1) % n_pts
        bm.faces.new([verts_bot[i], verts_bot[j], verts_plinto[j], verts_plinto[i]])

    # === PAREDES (plinto → wall_top) ===
    wall_top_y = height - CORNISA_H
    verts_wall_top = [bm.verts.new(Vector((p[0], wall_top_y, p[1]))) for p in pts2d]

    for i in range(n_pts):
        j = (i + 1) % n_pts
        bm.faces.new([verts_plinto[i], verts_plinto[j], verts_wall_top[j], verts_wall_top[i]])

    # === CORNISA (extrusión perimetral 0.12m hacia afuera) ===
    verts_cornisa = []
    for p in pts2d:
        dx, dz = p[0] - cx, p[1] - cz
        mag = math.sqrt(dx*dx + dz*dz) or 1.0
        nx, nz = dx/mag, dz/mag
        verts_cornisa.append(bm.verts.new(Vector((
            p[0] + nx * CORNISA_H,
            height,
            p[1] + nz * CORNISA_H
        ))))

    # Cara frontal cornisa (banda vertical)
    for i in range(n_pts):
        j = (i + 1) % n_pts
        bm.faces.new([verts_wall_top[i], verts_wall_top[j], verts_cornisa[j], verts_cornisa[i]])

    # === TEJADO ===
    _build_roof(bm, pts2d, verts_cornisa, height, roof_t, archtyp, cx, cz)

    # --- Finalizar malla ---
    bm.normal_update()
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.005)
    bm.to_mesh(mesh)
    bm.free()

    # UV Smart Project
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.02)
    bpy.ops.object.mode_set(mode='OBJECT')

    return obj


def _build_roof(bm, pts2d, verts_base, height, roof_type, archetype, cx, cz):
    """Construye la geometría del tejado sobre verts_base (anillo de cornisa)."""
    n = len(pts2d)
    roof_y = height  # base ya en height, tejado encima

    if roof_type == "flat":
        # Losa plana + bordillo
        verts_losa = [bm.verts.new(Vector((v.co.x, roof_y + BORDILLO_H, v.co.z))) for v in verts_base]
        # Cara superior
        bm.faces.new(verts_losa)
        # Bandas bordillo
        for i in range(n):
            j = (i + 1) % n
            bm.faces.new([verts_base[i], verts_base[j], verts_losa[j], verts_losa[i]])

    elif roof_type == "hipped":
        # Pirámide truncada con alero ALERO_W
        # Anillo exterior alero
        verts_alero = []
        for v in verts_base:
            dx, dz = v.co.x - cx, v.co.z - cz
            mag = math.sqrt(dx*dx + dz*dz) or 1.0
            nx, nz = dx/mag, dz/mag
            verts_alero.append(bm.verts.new(Vector((
                v.co.x + nx * ALERO_W,
                roof_y,
                v.co.z + nz * ALERO_W
            ))))
        # Plano del alero
        for i in range(n):
            j = (i + 1) % n
            bm.faces.new([verts_base[i], verts_base[j], verts_alero[j], verts_alero[i]])

        # Apex de la pirámide truncada (cumbrera reducida 40% del span)
        ridge_y = roof_y + min(3.0, (max(abs(v.co.x - cx) for v in verts_base)) * math.tan(math.radians(30)))
        scale = 0.4
        verts_ridge = [bm.verts.new(Vector((
            cx + (v.co.x - cx) * scale,
            ridge_y,
            cz + (v.co.z - cz) * scale
        ))) for v in verts_alero]

        # Planos del tejado hip
        for i in range(n):
            j = (i + 1) % n
            bm.faces.new([verts_alero[i], verts_alero[j], verts_ridge[j], verts_ridge[i]])
        # Cara superior
        bm.faces.new(verts_ridge)

    else:
        # "gabled" — tejado a dos aguas con PCA 2D
        angle_deg = ROOF_ANGLE_DEG.get(archetype, ROOF_ANGLE_DEG["default"])
        angle_rad = math.radians(angle_deg)

        # Eje largo mediante PCA
        axis_long, axis_short = _pca_axis_2d(pts2d)
        lx, lz = axis_long

        # Proyectar todos los vértices sobre el eje corto para calcular span
        projections = [v.co.x * axis_short[0] + v.co.z * axis_short[1] for v in verts_base]
        span = max(projections) - min(projections)
        ridge_height = (span / 2.0) * math.tan(angle_rad)

        # Proyectar sobre eje largo para extremos de cumbrera
        proj_long = [v.co.x * lx + v.co.z * lz for v in verts_base]
        p_min = min(proj_long)
        p_max = max(proj_long)

        # Centro de cumbrera
        ridge_y = roof_y + ridge_height
        r0 = Vector((cx + lx * p_min, ridge_y, cz + lz * p_min))
        r1 = Vector((cx + lx * p_max, ridge_y, cz + lz * p_max))
        # Offset cumbrera al centro real
        mean_l = (p_min + p_max) / 2.0
        r0 = Vector((cx + lx * (p_min - mean_l + (p_min + p_max)/2.0 * 0 ), ridge_y,
                     cz + lz * (p_min - mean_l + (p_min + p_max)/2.0 * 0 )))
        # Recalcular correctamente
        r0 = Vector((cx + lx * p_min, ridge_y, cz + lz * p_min))
        r1 = Vector((cx + lx * p_max, ridge_y, cz + lz * p_max))

        vr0 = bm.verts.new(r0)
        vr1 = bm.verts.new(r1)

        # Asignar cada vértice base a uno de los dos lados (según signo proj eje corto)
        mid_proj = (max(projections) + min(projections)) / 2.0
        side_a = [v for v, pr in zip(verts_base, projections) if pr >= mid_proj]
        side_b = [v for v, pr in zip(verts_base, projections) if pr <  mid_proj]

        # Triángulos laterales: conectar cada lado a la cumbrera
        def _triangulate_to_ridge(side_verts, rv0, rv1):
            if len(side_verts) < 2:
                return
            # Ordenar por proyección en eje largo
            side_sorted = sorted(side_verts,
                                 key=lambda v: v.co.x * lx + v.co.z * lz)
            # Polígono: rv0, side_sorted..., rv1
            face_verts = [rv0] + side_sorted + [rv1]
            try:
                bm.faces.new(face_verts)
            except Exception:
                pass

        _triangulate_to_ridge(side_a, vr0, vr1)
        _triangulate_to_ridge(side_b, vr1, vr0)

        # Hastiales (triángulos en los extremos)
        eps_l = 0.05
        end_a = [v for v, pr in zip(verts_base, proj_long) if abs(pr - p_min) < eps_l + span * 0.2]
        end_b = [v for v, pr in zip(verts_base, proj_long) if abs(pr - p_max) < eps_l + span * 0.2]
        if end_a:
            try:
                bm.faces.new([vr0] + sorted(end_a, key=lambda v: v.co.x * axis_short[0] + v.co.z * axis_short[1]))
            except Exception:
                pass
        if end_b:
            try:
                bm.faces.new([vr1] + sorted(end_b, key=lambda v: v.co.x * axis_short[0] + v.co.z * axis_short[1], reverse=True))
            except Exception:
                pass

# ---------------------------------------------------------------------------
# Generación de LODs
# ---------------------------------------------------------------------------

def generar_lods(obj_base, bid):
    """
    Genera LOD1-3 mediante Decimate sobre una copia del objeto base LOD0.
    Devuelve dict: {"LOD0": obj_base, "LOD1": obj1, "LOD2": obj2, "LOD3": obj3}
    """
    lods = {"LOD0": obj_base}

    for lod_name, ratio in LOD_DECIMATE.items():
        if lod_name == "LOD0" or ratio is None:
            continue

        # Duplicar objeto
        bpy.ops.object.select_all(action='DESELECT')
        obj_base.select_set(True)
        bpy.context.view_layer.objects.active = obj_base
        bpy.ops.object.duplicate(linked=False)
        obj_lod = bpy.context.active_object
        obj_lod.name = f"{bid}_{lod_name}"

        # Añadir modificador Decimate
        mod = obj_lod.modifiers.new(name="Decimate", type='DECIMATE')
        mod.ratio = ratio
        mod.use_collapse_triangulate = True

        # Aplicar modificador
        bpy.context.view_layer.objects.active = obj_lod
        bpy.ops.object.modifier_apply(modifier="Decimate")

        # Remove doubles en LOD pesados
        if ratio >= 0.15:
            mesh = obj_lod.data
            bm = bmesh.new()
            bm.from_mesh(mesh)
            bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.01)
            bm.to_mesh(mesh)
            bm.free()

        lods[lod_name] = obj_lod

    return lods


def contar_triangulos(obj):
    """Cuenta triángulos en la malla del objeto."""
    mesh = obj.data
    count = 0
    for poly in mesh.polygons:
        count += len(poly.vertices) - 2
    return max(count, 0)

# ---------------------------------------------------------------------------
# Exportación FBX
# ---------------------------------------------------------------------------

def exportar_fbx(lods, output_path):
    """
    Exporta todos los LODs de un edificio a un único FBX.
    output_path: ruta completa del archivo .fbx
    """
    # Seleccionar solo los objetos LOD
    bpy.ops.object.select_all(action='DESELECT')
    for obj in lods.values():
        obj.select_set(True)

    os.makedirs(os.path.dirname(output_path), exist_ok=True)

    bpy.ops.export_scene.fbx(
        filepath=output_path,
        use_selection=True,
        axis_forward='-Z',
        axis_up='Y',
        global_scale=1.0,
        apply_unit_scale=True,
        apply_scale_options='FBX_SCALE_NONE',
        use_mesh_modifiers=True,
        bake_space_transform=True,
        mesh_smooth_type='FACE',
        use_mesh_edges=False,
        use_tspace=True,
        embed_textures=False,
        path_mode='COPY',
        batch_mode='OFF',
        use_batch_own_dir=False,
    )


# ---------------------------------------------------------------------------
# Limpieza de escena
# ---------------------------------------------------------------------------

def limpiar_escena():
    """Elimina todos los objetos mesh de la escena."""
    bpy.ops.object.select_all(action='DESELECT')
    for obj in list(bpy.data.objects):
        if obj.type == 'MESH':
            bpy.data.objects.remove(obj, do_unlink=True)
    for mesh in list(bpy.data.meshes):
        bpy.data.meshes.remove(mesh)


# ---------------------------------------------------------------------------
# Argparse desde sys.argv (blender pasa args después de --)
# ---------------------------------------------------------------------------

def parse_args():
    argv = sys.argv
    if "--" in argv:
        argv = argv[argv.index("--") + 1:]
    else:
        argv = []

    parser = argparse.ArgumentParser(description="Blender LOD pipeline — Altsasu Manifa B4")
    parser.add_argument("--input",         required=True,  help="Ruta a buildings_fusion_final.json")
    parser.add_argument("--output",        required=True,  help="Directorio de salida FBX")
    parser.add_argument("--textures",      default="",     help="Directorio texturas fachada")
    parser.add_argument("--max_buildings", type=int, default=50, help="Máximo de edificios a procesar")
    parser.add_argument("--archetype",     default=None,   help="Filtrar por archetype (ej: pre1940_piedra)")
    return parser.parse_args(argv)


# ---------------------------------------------------------------------------
# MAIN
# ---------------------------------------------------------------------------

def main():
    args = parse_args()

    # --- Cargar JSON ---
    with open(args.input, "r", encoding="utf-8") as f:
        buildings = json.load(f)

    print(f"[B4] Edificios en JSON: {len(buildings)}")

    # Filtrar por archetype
    if args.archetype:
        buildings = [b for b in buildings if b.get("archetype") == args.archetype]
        print(f"[B4] Tras filtro archetype='{args.archetype}': {len(buildings)} edificios")

    # Limitar
    buildings = buildings[:args.max_buildings]
    total = len(buildings)
    print(f"[B4] Procesando {total} edificios → {args.output}")

    os.makedirs(args.output, exist_ok=True)

    log_entries = []
    stats = {
        "total":    total,
        "ok":       0,
        "skip":     0,
        "error":    0,
        "archetypes": {},
        "roof_types": {},
        "timestamp": time.strftime("%Y-%m-%dT%H:%M:%S"),
    }

    for i, b in enumerate(buildings):
        bid     = str(b.get("id", f"bld_{i}"))
        arch    = b.get("archetype", "default")
        height  = b.get("height_m", 0.0)
        roof    = b.get("roof_type", "gabled")

        try:
            limpiar_escena()

            # Crear LOD0
            obj_base = crear_edificio_base(b)
            if obj_base is None:
                print(f"[B4] {i+1}/{total} {bid} SKIP (footprint inválido)")
                stats["skip"] += 1
                log_entries.append({"id": bid, "status": "skip", "reason": "invalid_footprint"})
                continue

            obj_base.name = f"{bid}_LOD0"

            # Generar LOD1-3
            lods = generar_lods(obj_base, bid)

            # Contar triángulos LOD0
            tris_lod0 = contar_triangulos(obj_base)
            tris_lod3 = contar_triangulos(lods.get("LOD3", obj_base))

            # Exportar FBX
            fbx_path = os.path.join(args.output, f"{bid}.fbx")
            exportar_fbx(lods, fbx_path)

            print(f"[B4] {i+1}/{total} {bid} arch={arch} h={height:.1f}m roof={roof} "
                  f"→ LOD0:{tris_lod0}tris LOD3:{tris_lod3}tris → {os.path.basename(fbx_path)}")

            stats["ok"] += 1
            stats["archetypes"][arch] = stats["archetypes"].get(arch, 0) + 1
            stats["roof_types"][roof]  = stats["roof_types"].get(roof, 0) + 1

            log_entries.append({
                "id":         bid,
                "status":     "ok",
                "archetype":  arch,
                "height_m":   height,
                "roof_type":  roof,
                "tris_lod0":  tris_lod0,
                "tris_lod3":  tris_lod3,
                "fbx":        fbx_path,
            })

        except Exception as e:
            print(f"[B4] ERROR {bid}: {e}")
            traceback.print_exc()
            stats["error"] += 1
            log_entries.append({"id": bid, "status": "error", "message": str(e)})

    # --- Guardar log JSON ---
    log_data = {"stats": stats, "buildings": log_entries}
    # Buscar raíz del proyecto (Tools/../)
    script_dir  = os.path.dirname(os.path.abspath(__file__))
    project_dir = os.path.dirname(script_dir)
    log_path    = os.path.join(project_dir, "Assets", "AlsasuaData", "blender_pipeline_log.json")
    os.makedirs(os.path.dirname(log_path), exist_ok=True)
    with open(log_path, "w", encoding="utf-8") as f:
        json.dump(log_data, f, indent=2, ensure_ascii=False)

    print(f"\n[B4] COMPLETADO: {stats['ok']} OK / {stats['skip']} skip / {stats['error']} errores")
    print(f"[B4] Log guardado en: {log_path}")


if __name__ == "__main__":
    main()
