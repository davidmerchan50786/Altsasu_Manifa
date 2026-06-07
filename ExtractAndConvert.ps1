# ExtractAndConvert.ps1
# ═══════════════════════════════════════════════════════════════════════════
#  Pipeline de extracción y conversión de assets para Alsasua GTA
#
#  Paso 1: Extrae todos los .zip/.7z/.rar con 7-Zip
#  Paso 2: Convierte .blend → .fbx con Blender 5.1 headless
#  Paso 3: Copia FBX/OBJ/TGA/PNG a Assets/_ExtractedAssets/
#
#  Ejecutar desde PowerShell como Administrador:
#    cd "E:\Desk\DAM\Altsasu_Manifa"
#    .\ExtractAndConvert.ps1
# ═══════════════════════════════════════════════════════════════════════════

$ErrorActionPreference = "Continue"

$BLENDER    = "C:\Program Files\Blender Foundation\Blender 5.1\blender.exe"
$7ZIP       = "C:\Program Files\7-Zip\7z.exe"
$ORIGEN     = "E:\Desk\DAM\02_PROYECTOS_PRACTICAS\Alsasua_Simulator\Assets\Downloads"
$STAGING    = "E:\Desk\DAM\Altsasu_Manifa\_Staging"
$DESTINO    = "E:\Desk\DAM\Altsasu_Manifa\Assets\_ExtractedAssets"
$BLEND_SCRIPT = "E:\Desk\DAM\Altsasu_Manifa\_blend_to_fbx.py"

# ── Catálogo de utilidad para Alsasua ─────────────────────────────────────
# Indica qué carpetas son útiles y a qué subcarpeta van en _ExtractedAssets
$CATALOGO = @{
    "destroyedWalls"         = "Destruction"     # paredes destruidas
    "Hedges"                 = "Vegetation"      # setos
    "bushes"                 = "Vegetation"      # arbustos
    "plant-pack"             = "Vegetation"      # plantas
    "Stylish plants"         = "Vegetation"      # plantas
    "tiny weeds"             = "Vegetation"      # malas hierbas
    "grass_02"               = "Vegetation"      # hierba
    "jkm_oaktree"            = "Vegetation"      # roble
    "modular_village"        = "Buildings"       # módulos de pueblo
    "House"                  = "Buildings"       # casa
    "hq_building"            = "Buildings"       # edificio HQ (gltf)
    "chicken"                = "Animals"         # gallina
    "rooster"                = "Animals"         # gallo
    "Hatchet"                = "Weapons"         # hacha
    "smg"                    = "Weapons"         # SMG
    "smallarms"              = "Weapons"         # armas pequeñas (ak47, m4)
    "animated-characters"    = "Characters"      # Kenney characters
    "mini-characters"        = "Characters"      # Kenney mini
}

Write-Host "═══════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  PIPELINE ASSETS ALSASUA — Extracción + Conversión" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════════" -ForegroundColor Cyan

# ── 1. Crear carpetas ─────────────────────────────────────────────────────
New-Item -ItemType Directory -Force -Path $STAGING  | Out-Null
New-Item -ItemType Directory -Force -Path $DESTINO  | Out-Null
foreach ($cat in $CATALOGO.Values | Sort-Object -Unique) {
    New-Item -ItemType Directory -Force -Path "$DESTINO\$cat" | Out-Null
}
Write-Host "`n[1/4] Carpetas creadas en $DESTINO" -ForegroundColor Green

# ── 2. Generar script Python para Blender ─────────────────────────────────
$blendScript = @'
# _blend_to_fbx.py — Blender 5.1 headless batch converter
# Uso: blender --background --python _blend_to_fbx.py -- <input.blend> <output.fbx>
import bpy, sys, os

argv = sys.argv
try:
    sep = argv.index("--") + 1
    blend_in  = argv[sep]
    fbx_out   = argv[sep + 1]
except (ValueError, IndexError):
    print("ERROR: Uso: blender --background --python script.py -- input.blend output.fbx")
    sys.exit(1)

print(f"[BlendToFBX] Abriendo: {blend_in}")
bpy.ops.wm.open_mainfile(filepath=blend_in)

# Seleccionar todos los objetos mesh
bpy.ops.object.select_all(action='DESELECT')
for obj in bpy.context.scene.objects:
    if obj.type in ('MESH', 'ARMATURE', 'EMPTY'):
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj

print(f"[BlendToFBX] Exportando FBX: {fbx_out}")
bpy.ops.export_scene.fbx(
    filepath        = fbx_out,
    use_selection   = False,
    apply_scale_options = 'FBX_SCALE_ALL',
    axis_forward    = '-Z',
    axis_up         = 'Y',
    bake_space_transform = True,
    mesh_smooth_type = 'FACE',
    use_mesh_modifiers = True,
    add_leaf_bones  = False,
    path_mode       = 'COPY',
    embed_textures  = False,
)
print(f"[BlendToFBX] ✅ Listo: {fbx_out}")
'@
$blendScript | Out-File -Encoding UTF8 -FilePath $BLEND_SCRIPT
Write-Host "[2/4] Script Python de conversión generado" -ForegroundColor Green

# ── 3. Extraer y procesar cada archivo comprimido ─────────────────────────
Write-Host "`n[3/4] Extrayendo archivos..." -ForegroundColor Yellow

$archivos = Get-ChildItem $ORIGEN -Recurse -Include "*.zip","*.7z","*.rar"
# Añadir también los Kenney zips del proyecto antiguo
$kenneyPath = "E:\Desk\DAM\02_PROYECTOS_PRACTICAS\Alsasua_Simulator\Assets\Personajes\Kenney"
if (Test-Path $kenneyPath) {
    $archivos += Get-ChildItem $kenneyPath -Include "*.zip","*.7z","*.rar"
}

$totalExtraidos  = 0
$totalConvertidos = 0
$totalCopiados    = 0

foreach ($archivo in $archivos) {
    $nombre = [System.IO.Path]::GetFileNameWithoutExtension($archivo.Name)

    # Determinar categoría
    $categoria = "Otros"
    foreach ($kv in $CATALOGO.GetEnumerator()) {
        if ($nombre -ilike "*$($kv.Key)*" -or $archivo.DirectoryName -ilike "*$($kv.Key)*") {
            $categoria = $kv.Value
            break
        }
    }

    $carpetaStaging = "$STAGING\$nombre"
    Write-Host "  → Extrayendo: $($archivo.Name) ($([Math]::Round($archivo.Length/1MB,1)) MB)" -ForegroundColor White

    # Extraer con 7-Zip
    & $7ZIP x $archivo.FullName -o"$carpetaStaging" -y -bso0 -bsp0 2>$null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "    ⚠️ Error extrayendo $($archivo.Name)" -ForegroundColor Red
        continue
    }
    $totalExtraidos++

    # Buscar archivos 3D aprovechables
    $archivos3D = Get-ChildItem $carpetaStaging -Recurse -Include "*.fbx","*.FBX","*.obj","*.OBJ","*.gltf","*.glb","*.3ds"
    $archivosBlend = Get-ChildItem $carpetaStaging -Recurse -Include "*.blend"
    $texturas   = Get-ChildItem $carpetaStaging -Recurse -Include "*.png","*.jpg","*.jpeg","*.tga","*.tif","*.bmp"

    $carpetaDest = "$DESTINO\$categoria\$nombre"
    New-Item -ItemType Directory -Force -Path $carpetaDest | Out-Null

    # Copiar modelos directamente importables
    foreach ($m in $archivos3D) {
        Copy-Item $m.FullName -Destination $carpetaDest -Force
        $totalCopiados++
        Write-Host "    ✅ Copiado: $($m.Name)" -ForegroundColor Green
    }

    # Copiar texturas
    foreach ($t in $texturas) {
        Copy-Item $t.FullName -Destination $carpetaDest -Force
    }

    # Convertir .blend → .fbx con Blender headless
    foreach ($blend in $archivosBlend) {
        $fbxOut = "$carpetaDest\$([System.IO.Path]::GetFileNameWithoutExtension($blend.Name)).fbx"
        Write-Host "    🔄 Convirtiendo: $($blend.Name) → FBX..." -ForegroundColor Yellow

        $resultado = & $BLENDER --background --python $BLEND_SCRIPT -- $blend.FullName $fbxOut 2>&1
        if (Test-Path $fbxOut) {
            $totalConvertidos++
            Write-Host "    ✅ Convertido: $([System.IO.Path]::GetFileName($fbxOut))" -ForegroundColor Green
        } else {
            Write-Host "    ❌ Falló conversión: $($blend.Name)" -ForegroundColor Red
            # Guardar el .blend directamente (Unity no lo importa pero queda de referencia)
            Copy-Item $blend.FullName -Destination "$carpetaDest\$($blend.Name).REFERENCE_ONLY" -Force
        }
    }
}

# ── 4. Resumen ────────────────────────────────────────────────────────────
Write-Host "`n═══════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  RESUMEN" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  Archivos extraídos:  $totalExtraidos" -ForegroundColor White
Write-Host "  Modelos copiados:    $totalCopiados  (FBX/OBJ directos)" -ForegroundColor Green
Write-Host "  .blend convertidos:  $totalConvertidos  (via Blender 5.1)" -ForegroundColor Green
Write-Host ""
Write-Host "  Destino: $DESTINO" -ForegroundColor White
Write-Host ""
Write-Host "  Próximo paso en Unity:" -ForegroundColor Yellow
Write-Host "    Tools → Alsasua → 📥 Importar Assets Extraídos" -ForegroundColor Yellow
Write-Host ""
Write-Host "  Estructura generada:" -ForegroundColor White
Get-ChildItem $DESTINO -Directory | ForEach-Object {
    $count = (Get-ChildItem $_.FullName -Recurse -Include "*.fbx","*.FBX","*.obj","*.OBJ","*.gltf").Count
    Write-Host "    📁 $($_.Name)/  ($count modelos)" -ForegroundColor Gray
}

# Limpiar staging
Write-Host "`n  Limpiando carpeta temporal..." -ForegroundColor Gray
Remove-Item $STAGING -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $BLEND_SCRIPT -Force -ErrorAction SilentlyContinue

Write-Host "`n✅ Pipeline completado. Abre Unity y ejecuta el importador." -ForegroundColor Green
