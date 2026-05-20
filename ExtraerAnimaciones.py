"""
ExtraerAnimaciones.py — extrae animaciones de FBX MeshyAI y las exporta como FBX limpios
Uso: blender --background --python ExtraerAnimaciones.py
"""
import bpy, sys, os

BASE = "E:/Desk/DAM/Altsasu_Manifa/Assets/_ExtractedAssets/Personajes/MeshyAI"
OUT  = "E:/Desk/DAM/Altsasu_Manifa/Assets/Animators/Clips"

os.makedirs(OUT, exist_ok=True)

# Mapa FBX → nombre del clip Unity
CLIPS = {
    "GuardiaCivil_Biped_Anims/Meshy_AI_Guardia_Civil_Officer_biped_Animation_Walking_frame_rate_60.fbx":          "GC_Walk",
    "GuardiaCivil_Biped_Anims/Meshy_AI_Guardia_Civil_Officer_biped_Animation_RunFast_frame_rate_60.fbx":          "GC_Run",
    "GuardiaCivil_Biped_Anims/Meshy_AI_Guardia_Civil_Officer_biped_Animation_Dead_frame_rate_60.fbx":             "GC_Die",
    "GuardiaCivil_Biped_Anims/Meshy_AI_Guardia_Civil_Officer_biped_Animation_Hit_Reaction_frame_rate_60.fbx":     "GC_Hit",
    "GuardiaCivil_Biped_Anims/Meshy_AI_Guardia_Civil_Officer_biped_Animation_Rifle_Charge_inplace_frame_rate_60.fbx": "GC_Idle",
    "GuardiaCivil_Biped_Anims/Meshy_AI_Guardia_Civil_Officer_biped_Animation_Idle_Turn_Left_frame_rate_60.fbx":   "GC_IdleTurnL",
    "GuardiaCivil_Biped_Anims/Meshy_AI_Guardia_Civil_Officer_biped_Animation_Idle_Turn_Right_frame_rate_60.fbx":  "GC_IdleTurnR",
    "GuardiaCivil_Biped_Anims/Meshy_AI_Guardia_Civil_Officer_biped_Animation_Rifle_Aim_Turn_Right_frame_rate_60.fbx": "GC_Aim",
    "GuardiaCivil_Biped_Anims/Meshy_AI_Guardia_Civil_Officer_biped_Animation_Arise_frame_rate_60.fbx":            "GC_Arise",
    "GuardiaCivil_Biped_Anims/Meshy_AI_Guardia_Civil_Officer_biped_Animation_Spartan_Kick_frame_rate_60.fbx":     "GC_Kick",
    "GuardiaCivil_Biped_Anims/Meshy_AI_Guardia_Civil_Officer_biped_Animation_Run_Sharp_Turn_Right_frame_rate_60.fbx": "GC_RunTurn",
    "GuardiaCivil_Biped_Anims/Meshy_AI_Guardia_Civil_Officer_biped_Animation_Injured_Walk_Backward_frame_rate_60.fbx": "GC_WalkInjured",
    "GuardiaCivil_Biped_Anims/Meshy_AI_Guardia_Civil_Officer_biped_Animation_falling_down_frame_rate_60.fbx":     "GC_Fall",
    "GuardiaCivil_Biped_Anims/Meshy_AI_Guardia_Civil_Officer_biped_Animation_Unsteady_Walk_frame_rate_60.fbx":    "GC_WalkUnsteady",
    "GuardiaCivil_Biped_Anims/Meshy_AI_Guardia_Civil_Officer_biped_Animation_Walk_Left_with_Gun_inplace_frame_rate_60.fbx": "GC_WalkGun",
    # Civiles
    "Meshy_AI_Animation_Walking_frame_rate_60.fbx":  "Civil_Walk",
    "Civil_SoftGaze_Biped/Meshy_AI_Soft_Gaze_in_Warm_Lig_biped_Meshy_AI_Meshy_Merged_Animations.fbx": "Civil_Merged",
}

ok = 0
fail = 0
for rel_path, clip_name in CLIPS.items():
    fbx_in  = os.path.join(BASE, rel_path).replace("\\","/")
    fbx_out = os.path.join(OUT, f"{clip_name}.fbx").replace("\\","/")

    if not os.path.exists(fbx_in):
        print(f"SKIP (no existe): {fbx_in}")
        fail += 1
        continue

    if os.path.exists(fbx_out):
        print(f"OK (ya existe): {clip_name}.fbx")
        ok += 1
        continue

    try:
        # Limpiar escena
        bpy.ops.wm.read_factory_settings(use_empty=True)
        for obj in bpy.data.objects:
            bpy.data.objects.remove(obj, do_unlink=True)

        # Importar FBX con animaciones
        bpy.ops.import_scene.fbx(
            filepath=fbx_in,
            use_anim=True,
            anim_offset=1.0,
            ignore_leaf_bones=False,
            force_connect_children=False,
            automatic_bone_orientation=False,
            primary_bone_axis='Y',
            secondary_bone_axis='X',
            use_custom_normals=True,
            use_image_search=True,
            decal_offset=0.0,
            use_alpha_decals=False,
            use_manual_orientation=False,
            global_scale=1.0,
            bake_space_transform=False,
            use_custom_props=True,
        )

        # Seleccionar todo
        bpy.ops.object.select_all(action='SELECT')

        # Exportar FBX limpio con animaciones
        bpy.ops.export_scene.fbx(
            filepath=fbx_out,
            use_selection=True,
            use_active_collection=False,
            global_scale=1.0,
            apply_unit_scale=True,
            apply_scale_options='FBX_SCALE_ALL',
            bake_space_transform=False,
            object_types={'ARMATURE','MESH','EMPTY'},
            use_mesh_modifiers=True,
            use_mesh_modifiers_render=True,
            mesh_smooth_type='OFF',
            use_subsurf=False,
            use_mesh_edges=False,
            use_tspace=True,
            use_triangles=False,
            use_custom_props=False,
            add_leaf_bones=False,
            primary_bone_axis='Y',
            secondary_bone_axis='X',
            use_armature_deform_only=False,
            armature_nodetype='NULL',
            bake_anim=True,
            bake_anim_use_all_bones=True,
            bake_anim_use_nla_strips=True,
            bake_anim_use_all_actions=True,
            bake_anim_force_startend_keying=True,
            bake_anim_step=1.0,
            bake_anim_simplify_factor=1.0,
            path_mode='COPY',
            embed_textures=False,
            batch_mode='OFF',
            axis_forward='-Z',
            axis_up='Y',
        )
        print(f"OK: {clip_name}.fbx")
        ok += 1
    except Exception as e:
        print(f"ERROR {clip_name}: {e}")
        fail += 1

print(f"\nResultado: {ok} OK, {fail} FAIL")
print(f"Clips en: {OUT}")
