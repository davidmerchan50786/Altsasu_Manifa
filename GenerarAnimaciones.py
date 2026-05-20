"""
GenerarAnimaciones.py — genera ciclos de animación humanoid con Blender API
Crea Walk, Run, Idle, Jump, Crouch como FBX con keyframes reales.

Uso: blender --background --python GenerarAnimaciones.py
"""
import bpy, math, os

OUT = "E:/Desk/DAM/Altsasu_Manifa/Assets/Animators/Clips"
os.makedirs(OUT, exist_ok=True)

# ── Limpiar escena ─────────────────────────────────────────────────────────
def limpiar():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete()
    for c in bpy.data.collections:
        bpy.data.collections.remove(c)

# ── Crear armatura humanoid mínima ─────────────────────────────────────────
def crear_armatura():
    bpy.ops.object.armature_add(enter_editmode=True)
    arm = bpy.context.active_object
    arm.name = "Jugador_Rig"
    bones = arm.data.edit_bones

    # Limpiar el hueso inicial
    for b in list(bones):
        bones.remove(b)

    def add_bone(nombre, head, tail, parent=None):
        b = bones.new(nombre)
        b.head = head; b.tail = tail
        if parent: b.parent = bones[parent]; b.use_connect = False
        return b

    # Columna vertebral
    add_bone("Hips",       (0,0,1.0),  (0,0,1.2))
    add_bone("Spine",      (0,0,1.2),  (0,0,1.45), "Hips")
    add_bone("Chest",      (0,0,1.45), (0,0,1.65), "Spine")
    add_bone("Neck",       (0,0,1.65), (0,0,1.75), "Chest")
    add_bone("Head",       (0,0,1.75), (0,0,1.95), "Neck")
    # Brazos
    add_bone("LeftArm",    (0.18,0,1.62), (0.42,0,1.62), "Chest")
    add_bone("LeftForearm",(0.42,0,1.62), (0.62,0,1.55), "LeftArm")
    add_bone("LeftHand",   (0.62,0,1.55), (0.72,0,1.50), "LeftForearm")
    add_bone("RightArm",   (-0.18,0,1.62),(-0.42,0,1.62),"Chest")
    add_bone("RightForearm",(-0.42,0,1.62),(-0.62,0,1.55),"RightArm")
    add_bone("RightHand",  (-0.62,0,1.55),(-0.72,0,1.50),"RightForearm")
    # Piernas
    add_bone("LeftUpLeg",  (0.12,0,1.0), (0.14,0,0.55), "Hips")
    add_bone("LeftLeg",    (0.14,0,0.55),(0.15,0,0.12), "LeftUpLeg")
    add_bone("LeftFoot",   (0.15,0,0.12),(0.15,0.15,0.0),"LeftLeg")
    add_bone("RightUpLeg", (-0.12,0,1.0),(-0.14,0,0.55),"Hips")
    add_bone("RightLeg",   (-0.14,0,0.55),(-0.15,0,0.12),"RightUpLeg")
    add_bone("RightFoot",  (-0.15,0,0.12),(-0.15,0.15,0.0),"RightLeg")

    bpy.ops.object.mode_set(mode='OBJECT')
    return arm

# ── Keyframing helpers ─────────────────────────────────────────────────────

def set_pose_rot(arm, bone_name, euler_xyz, frame):
    """Pone rotación Euler en un hueso en pose mode y keyframea."""
    if bone_name not in arm.pose.bones: return
    pb = arm.pose.bones[bone_name]
    pb.rotation_mode = 'XYZ'
    pb.rotation_euler = euler_xyz
    pb.keyframe_insert("rotation_euler", frame=frame)

def set_pose_loc(arm, bone_name, loc, frame):
    if bone_name not in arm.pose.bones: return
    pb = arm.pose.bones[bone_name]
    pb.location = loc
    pb.keyframe_insert("location", frame=frame)

def interp_smooth(arm):
    """Hace todos los fcurves BEZIER para suavidad. Blender 5.1 compatible."""
    if arm.animation_data is None or arm.animation_data.action is None:
        return
    action = arm.animation_data.action
    # En Blender 5.x las layers reemplazaron fcurves directas
    try:
        # Intentar API clásica
        for fcurve in action.fcurves:
            for kf in fcurve.keyframe_points:
                kf.interpolation = 'BEZIER'
                kf.handle_left_type = 'AUTO_CLAMPED'
                kf.handle_right_type = 'AUTO_CLAMPED'
    except AttributeError:
        # Blender 5.x: usar layers/strips API
        try:
            for layer in action.layers:
                for strip in layer.strips:
                    for channelbag in strip.channelbags:
                        for fcurve in channelbag.fcurves:
                            for kf in fcurve.keyframe_points:
                                kf.interpolation = 'BEZIER'
        except Exception:
            pass  # no crítico — LINEAR también funciona

# ── Crear acción ───────────────────────────────────────────────────────────

def nueva_accion(arm, nombre, num_frames):
    bpy.ops.object.select_all(action='DESELECT')
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    action = bpy.data.actions.new(nombre)
    arm.animation_data_create()
    arm.animation_data.action = action
    bpy.context.scene.frame_start = 1
    bpy.context.scene.frame_end = num_frames
    return action

# ═══════════════════════════════════════════════════════════════════════════
#  ANIMACIONES
# ═══════════════════════════════════════════════════════════════════════════

def generar_idle(arm):
    """Idle: respiración sutil + oscilación de peso."""
    nueva_accion(arm, "Idle", 60)
    fps = 30.0
    for f in range(1, 61):
        t = (f-1) / fps
        breath = math.sin(t * math.pi * 0.8)  # respiración ~0.4 Hz

        # Pecho sube/baja con la respiración
        set_pose_rot(arm, "Chest", (breath * 0.015, 0, 0), f)
        # Hips oscila ligeramente
        set_pose_rot(arm, "Hips",  (0, math.sin(t * math.pi * 0.4) * 0.008, 0), f)
        # Cabeza mira levemente adelante
        set_pose_rot(arm, "Head",  (-0.05 + breath * 0.01, 0, 0), f)
        # Brazos caen con gravedad natural
        set_pose_rot(arm, "LeftArm",   (0, 0,  0.2 + breath * 0.02), f)
        set_pose_rot(arm, "RightArm",  (0, 0, -0.2 - breath * 0.02), f)
    interp_smooth(arm)
    print("  OK: Idle (60 frames, 2s loop)")

def generar_walk(arm):
    """Walk: ciclo de caminar 30 fps, 40 frames = un ciclo completo."""
    nueva_accion(arm, "Walk", 40)
    fps = 30.0
    cycle = 40  # frames por ciclo completo

    for f in range(1, cycle + 1):
        t = (f - 1) / cycle  # 0..1 por ciclo
        fase = t * 2 * math.pi

        # Hips: oscilación lateral + vertical del peso
        hip_lateral  = math.sin(fase * 2) * 0.04         # 2 por ciclo
        hip_vertical = abs(math.sin(fase)) * -0.02        # baja al apoyar
        hip_rot_y    = math.sin(fase) * 0.08              # rotación coronal
        set_pose_rot(arm, "Hips", (0, hip_rot_y, 0), f)
        set_pose_loc(arm, "Hips", (hip_lateral, 0, hip_vertical), f)

        # Pierna izquierda — opuesta a derecha
        lu_swing = math.sin(fase) * 0.45                  # muslo adelante/atrás
        ll_bend  = max(0, math.sin(fase + 0.5) * 0.6)    # rodilla se dobla
        lf_plant = math.sin(fase) * 0.2                   # pie se levanta
        set_pose_rot(arm, "LeftUpLeg",  (lu_swing, 0, 0), f)
        set_pose_rot(arm, "LeftLeg",    (-ll_bend, 0, 0), f)
        set_pose_rot(arm, "LeftFoot",   (lf_plant + 0.1, 0, 0), f)

        # Pierna derecha — desfasada media vuelta
        ru_swing = math.sin(fase + math.pi) * 0.45
        rl_bend  = max(0, math.sin(fase + math.pi + 0.5) * 0.6)
        rf_plant = math.sin(fase + math.pi) * 0.2
        set_pose_rot(arm, "RightUpLeg", (ru_swing, 0, 0), f)
        set_pose_rot(arm, "RightLeg",   (-rl_bend, 0, 0), f)
        set_pose_rot(arm, "RightFoot",  (rf_plant + 0.1, 0, 0), f)

        # Brazos — contralaterales a las piernas
        la_swing = -math.sin(fase + math.pi) * 0.3
        ra_swing = -math.sin(fase) * 0.3
        set_pose_rot(arm, "LeftArm",    (la_swing, 0,  0.18), f)
        set_pose_rot(arm, "RightArm",   (ra_swing, 0, -0.18), f)
        set_pose_rot(arm, "LeftForearm",  (-abs(la_swing) * 0.5, 0, 0), f)
        set_pose_rot(arm, "RightForearm", (-abs(ra_swing) * 0.5, 0, 0), f)

        # Torso contrarrota a la pelvis
        set_pose_rot(arm, "Spine", (0.05, -hip_rot_y * 0.4, 0), f)
        set_pose_rot(arm, "Chest", (0.03, -hip_rot_y * 0.6, 0), f)
        set_pose_rot(arm, "Head",  (-0.04, hip_rot_y * 0.3, 0), f)

    interp_smooth(arm)
    print("  OK: Walk (40 frames, 1.33s cycle)")

def generar_run(arm):
    """Run: ciclo más rápido (28 frames), más amplitud, inclinado adelante."""
    nueva_accion(arm, "Run", 28)
    cycle = 28

    for f in range(1, cycle + 1):
        t = (f - 1) / cycle
        fase = t * 2 * math.pi

        # Hips más activos en carrera
        hip_rot_y = math.sin(fase) * 0.14
        hip_lat   = math.sin(fase * 2) * 0.05
        set_pose_rot(arm, "Hips",  (0.15, hip_rot_y, 0), f)  # inclinado adelante
        set_pose_loc(arm, "Hips",  (hip_lat, 0, 0), f)

        # Piernas con más amplitud
        lu_sw = math.sin(fase) * 0.75
        ll_bn = max(0, math.sin(fase + 0.4) * 0.9)
        lf_pl = math.sin(fase) * 0.35
        set_pose_rot(arm, "LeftUpLeg",  (lu_sw, 0, 0), f)
        set_pose_rot(arm, "LeftLeg",    (-ll_bn, 0, 0), f)
        set_pose_rot(arm, "LeftFoot",   (lf_pl, 0, 0), f)

        ru_sw = math.sin(fase + math.pi) * 0.75
        rl_bn = max(0, math.sin(fase + math.pi + 0.4) * 0.9)
        rf_pl = math.sin(fase + math.pi) * 0.35
        set_pose_rot(arm, "RightUpLeg", (ru_sw, 0, 0), f)
        set_pose_rot(arm, "RightLeg",   (-rl_bn, 0, 0), f)
        set_pose_rot(arm, "RightFoot",  (rf_pl, 0, 0), f)

        # Brazos más activos en carrera (doblados más)
        la_sw = -math.sin(fase + math.pi) * 0.55
        ra_sw = -math.sin(fase) * 0.55
        set_pose_rot(arm, "LeftArm",      (la_sw, 0,  0.12), f)
        set_pose_rot(arm, "RightArm",     (ra_sw, 0, -0.12), f)
        set_pose_rot(arm, "LeftForearm",  (-0.8 - abs(la_sw) * 0.3, 0, 0), f)
        set_pose_rot(arm, "RightForearm", (-0.8 - abs(ra_sw) * 0.3, 0, 0), f)

        set_pose_rot(arm, "Spine", (0.12, -hip_rot_y * 0.5, 0), f)
        set_pose_rot(arm, "Chest", (0.10, -hip_rot_y * 0.7, 0), f)
        set_pose_rot(arm, "Head",  (-0.10, hip_rot_y * 0.3, 0), f)

    interp_smooth(arm)
    print("  OK: Run (28 frames, 0.93s cycle)")

def generar_jump(arm):
    """Jump: despegue → vuelo → aterrizaje en 45 frames."""
    nueva_accion(arm, "Jump", 45)

    for f in range(1, 46):
        t = (f - 1) / 44.0  # 0..1

        if t < 0.2:  # Agacharse antes de saltar
            crouch = t / 0.2
            set_pose_rot(arm, "Hips",     (0.2 * crouch, 0, 0), f)
            set_pose_rot(arm, "LeftUpLeg", (-0.5 * crouch, 0, 0.05), f)
            set_pose_rot(arm, "RightUpLeg",(-0.5 * crouch, 0,-0.05), f)
            set_pose_rot(arm, "LeftLeg",   (-0.7 * crouch, 0, 0), f)
            set_pose_rot(arm, "RightLeg",  (-0.7 * crouch, 0, 0), f)
            set_pose_rot(arm, "LeftArm",   (-0.3 * crouch, 0, 0.2), f)
            set_pose_rot(arm, "RightArm",  (-0.3 * crouch, 0,-0.2), f)
        elif t < 0.45:  # Despegue y ascenso
            fly = (t - 0.2) / 0.25
            set_pose_rot(arm, "Hips",     (-0.1 * fly, 0, 0), f)
            set_pose_rot(arm, "LeftUpLeg", (0.3 * fly, 0, 0.08), f)
            set_pose_rot(arm, "RightUpLeg",(0.3 * fly, 0,-0.08), f)
            set_pose_rot(arm, "LeftLeg",   (-0.4 * fly, 0, 0), f)
            set_pose_rot(arm, "RightLeg",  (-0.4 * fly, 0, 0), f)
            set_pose_rot(arm, "LeftArm",   (-0.6 * fly, 0, 0.15), f)
            set_pose_rot(arm, "RightArm",  (-0.6 * fly, 0,-0.15), f)
        elif t < 0.65:  # Punto más alto — cuerpo extendido
            set_pose_rot(arm, "Hips",     (-0.1, 0, 0), f)
            set_pose_rot(arm, "LeftUpLeg", (0.3, 0, 0.08), f)
            set_pose_rot(arm, "RightUpLeg",(0.3, 0,-0.08), f)
            set_pose_rot(arm, "LeftLeg",   (-0.35, 0, 0), f)
            set_pose_rot(arm, "RightLeg",  (-0.35, 0, 0), f)
            set_pose_rot(arm, "LeftArm",   (-0.6, 0, 0.15), f)
            set_pose_rot(arm, "RightArm",  (-0.6, 0,-0.15), f)
        elif t < 0.8:  # Descenso — preparar aterrizaje
            land = (t - 0.65) / 0.15
            set_pose_rot(arm, "Hips",     (-0.1 + 0.15 * land, 0, 0), f)
            set_pose_rot(arm, "LeftUpLeg", (0.3 - 0.6 * land, 0, 0.05), f)
            set_pose_rot(arm, "RightUpLeg",(0.3 - 0.6 * land, 0,-0.05), f)
            set_pose_rot(arm, "LeftLeg",   (-0.35 - 0.3 * land, 0, 0), f)
            set_pose_rot(arm, "RightLeg",  (-0.35 - 0.3 * land, 0, 0), f)
        else:  # Aterrizaje — absorber impacto
            absorb = (t - 0.8) / 0.2
            set_pose_rot(arm, "Hips",     (0.05 * (1 - absorb), 0, 0), f)
            set_pose_rot(arm, "LeftUpLeg", (-0.3 * (1 - absorb), 0, 0.05), f)
            set_pose_rot(arm, "RightUpLeg",(-0.3 * (1 - absorb), 0,-0.05), f)
            set_pose_rot(arm, "LeftLeg",   (-0.65 * (1 - absorb), 0, 0), f)
            set_pose_rot(arm, "RightLeg",  (-0.65 * (1 - absorb), 0, 0), f)
            set_pose_rot(arm, "LeftArm",   (0.2 * (1 - absorb), 0, 0.2), f)
            set_pose_rot(arm, "RightArm",  (0.2 * (1 - absorb), 0,-0.2), f)

    interp_smooth(arm)
    print("  OK: Jump (45 frames, 1.5s)")

def generar_crouch(arm):
    """Crouch: agachado caminando sigiloso."""
    nueva_accion(arm, "Crouch", 50)
    cycle = 50

    for f in range(1, cycle + 1):
        t = (f - 1) / cycle
        fase = t * 2 * math.pi

        # Postura agachada permanente
        set_pose_rot(arm, "Hips",  (0.3, math.sin(fase) * 0.04, 0), f)
        set_pose_loc(arm, "Hips",  (0, 0, -0.3), f)
        set_pose_rot(arm, "Spine", (0.12, 0, 0), f)
        set_pose_rot(arm, "Chest", (0.08, 0, 0), f)

        # Piernas más dobladas
        lu_sw = math.sin(fase) * 0.25
        ll_bn = 0.6 + max(0, math.sin(fase + 0.3) * 0.2)
        set_pose_rot(arm, "LeftUpLeg",  (lu_sw - 0.2, 0, 0.06), f)
        set_pose_rot(arm, "LeftLeg",    (-ll_bn, 0, 0), f)
        set_pose_rot(arm, "RightUpLeg", (math.sin(fase+math.pi)*0.25 - 0.2, 0,-0.06), f)
        set_pose_rot(arm, "RightLeg",   (-(0.6 + max(0, math.sin(fase+math.pi+0.3)*0.2)), 0, 0), f)

        # Brazos ligeramente adelante
        set_pose_rot(arm, "LeftArm",  (-0.1 + math.sin(fase+math.pi)*0.15, 0, 0.2), f)
        set_pose_rot(arm, "RightArm", (-0.1 + math.sin(fase)*0.15, 0,-0.2), f)

    interp_smooth(arm)
    print("  OK: Crouch (50 frames, 1.67s loop)")

def generar_aim(arm):
    """Aim: apuntando con arma, giro de torso."""
    nueva_accion(arm, "Aim", 30)
    for f in range(1, 31):
        t = (f-1)/29.0
        breath = math.sin(t * math.pi)

        set_pose_rot(arm, "Hips",   (0.05, 0, 0), f)
        set_pose_rot(arm, "Spine",  (0.08, 0, 0), f)
        set_pose_rot(arm, "Chest",  (0.06, -0.15, 0), f)  # torso girado a la derecha
        set_pose_rot(arm, "Head",   (-0.08, -0.1, 0), f)  # mirando al objetivo

        # Brazo derecho: extendido hacia adelante sosteniendo arma
        set_pose_rot(arm, "RightArm",     (-0.5, 0.3, -0.15), f)
        set_pose_rot(arm, "RightForearm", (-0.3 + breath * 0.02, 0, 0), f)

        # Brazo izquierdo: apoya el arma
        set_pose_rot(arm, "LeftArm",     (-0.4, 0.15, 0.15), f)
        set_pose_rot(arm, "LeftForearm", (-0.5 + breath * 0.02, 0, 0), f)

        # Piernas ligeramente separadas
        set_pose_rot(arm, "LeftUpLeg",  (-0.05, 0, 0.08), f)
        set_pose_rot(arm, "RightUpLeg", ( 0.05, 0,-0.08), f)

    interp_smooth(arm)
    print("  OK: Aim (30 frames, 1s loop)")

def generar_die(arm):
    """Die: caída hacia atrás."""
    nueva_accion(arm, "Die", 50)
    for f in range(1, 51):
        t = (f-1)/49.0
        fall = min(1.0, t * 1.8)  # cae más rápido al principio

        # Hips cae hacia atrás
        set_pose_rot(arm, "Hips",  (-fall * 1.3, 0, 0), f)
        set_pose_loc(arm, "Hips",  (0, 0, -fall * 0.9), f)

        # Columna se curva
        set_pose_rot(arm, "Spine", (-fall * 0.3, 0, 0), f)
        set_pose_rot(arm, "Chest", (-fall * 0.2, 0, 0), f)

        # Brazos se abren al caer
        spread = fall * 0.8
        set_pose_rot(arm, "LeftArm",   (fall*0.3, 0, spread), f)
        set_pose_rot(arm, "RightArm",  (fall*0.3, 0,-spread), f)

        # Piernas se doblan al colapsar
        set_pose_rot(arm, "LeftUpLeg",  (-fall*0.2, 0, 0.1), f)
        set_pose_rot(arm, "RightUpLeg", (-fall*0.2, 0,-0.1), f)
        set_pose_rot(arm, "LeftLeg",    (-fall * 0.5, 0, 0), f)
        set_pose_rot(arm, "RightLeg",   (-fall * 0.5, 0, 0), f)

        set_pose_rot(arm, "Head", (-fall * 0.4, 0, 0), f)

    interp_smooth(arm)
    print("  OK: Die (50 frames, 1.67s)")

# ── Exportar acción como FBX ───────────────────────────────────────────────
def exportar_fbx(arm, nombre_accion, nombre_fichero):
    out_path = os.path.join(OUT, nombre_fichero)
    if os.path.exists(out_path):
        print(f"  SKIP (existe): {nombre_fichero}")
        return

    # Seleccionar solo la armatura
    bpy.ops.object.select_all(action='DESELECT')
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm

    bpy.ops.export_scene.fbx(
        filepath=out_path,
        use_selection=True,
        object_types={'ARMATURE'},
        use_armature_deform_only=False,
        add_leaf_bones=False,
        bake_anim=True,
        bake_anim_use_all_bones=True,
        bake_anim_use_all_actions=False,
        bake_anim_force_startend_keying=True,
        bake_anim_step=1.0,
        bake_anim_simplify_factor=0.0,  # Sin simplificar — todas las curvas
        path_mode='COPY',
        axis_forward='-Z',
        axis_up='Y',
        global_scale=1.0,
        apply_unit_scale=True,
    )
    print(f"  EXPORT: {nombre_fichero}")

# ═══════════════════════════════════════════════════════════════════════════
#  MAIN
# ═══════════════════════════════════════════════════════════════════════════

print("=== GENERANDO ANIMACIONES PROCEDURALES ===")

CLIPS = [
    ("Idle",   generar_idle,   "Player_Idle.fbx"),
    ("Walk",   generar_walk,   "Player_Walk.fbx"),
    ("Run",    generar_run,    "Player_Run.fbx"),
    ("Jump",   generar_jump,   "Player_Jump.fbx"),
    ("Crouch", generar_crouch, "Player_Crouch.fbx"),
    ("Aim",    generar_aim,    "Player_Aim.fbx"),
    ("Die",    generar_die,    "Player_Die.fbx"),
]

for nombre, fn_gen, fichero in CLIPS:
    limpiar()
    arm = crear_armatura()
    fn_gen(arm)
    exportar_fbx(arm, nombre, fichero)

print(f"\nAnimaciones en: {OUT}")
print("Total:", len(CLIPS))
