"""
ConvertirGLB.py — script Blender headless para convertir GLB/GLTF a FBX
Uso: blender.exe --background --python ConvertirGLB.py -- archivo.glb salida.fbx
"""
import bpy, sys, os

argv = sys.argv
idx  = argv.index("--") + 1 if "--" in argv else len(argv)
args = argv[idx:]

if len(args) < 2:
    print("Uso: blender --background --python ConvertirGLB.py -- entrada.glb salida.fbx")
    sys.exit(1)

entrada = args[0]
salida  = args[1]

# Limpiar escena
bpy.ops.wm.read_factory_settings(use_empty=True)
for obj in bpy.data.objects:
    bpy.data.objects.remove(obj, do_unlink=True)

# Importar GLB/GLTF
ext = os.path.splitext(entrada)[1].lower()
if ext == ".glb" or ext == ".gltf":
    bpy.ops.import_scene.gltf(filepath=entrada)
else:
    print(f"Extension no soportada: {ext}")
    sys.exit(1)

# Seleccionar todo
bpy.ops.object.select_all(action='SELECT')

# Exportar FBX
bpy.ops.export_scene.fbx(
    filepath=salida,
    use_selection=True,
    apply_unit_scale=True,
    apply_scale_options='FBX_SCALE_ALL',
    bake_space_transform=False,
    object_types={'MESH','ARMATURE','EMPTY'},
    use_mesh_modifiers=True,
    use_armature_deform_only=False,
    add_leaf_bones=False,
    primary_bone_axis='Y',
    secondary_bone_axis='X',
    use_tspace=True,
    use_custom_props=False,
    path_mode='COPY',
    embed_textures=True,
    axis_forward='-Z',
    axis_up='Y',
    global_scale=1.0
)
print(f"Convertido: {entrada} -> {salida}")
