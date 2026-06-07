# ExtractAssetsFolder.ps1
# ═══════════════════════════════════════════════════════════════════════════
#  Extrae y convierte los assets de E:\assets al proyecto de Alsasua
#  Foco: personajes Meshy AI, árboles nativos, vehículos, entorno vasco
# ═══════════════════════════════════════════════════════════════════════════

$7ZIP   = "C:\Program Files\7-Zip\7z.exe"
$BLENDER = "C:\Program Files\Blender Foundation\Blender 5.1\blender.exe"
$ORIGEN  = "E:\assets"
$DESTINO = "E:\Desk\DAM\Altsasu_Manifa\Assets\_ExtractedAssets"

# Script Python para conversión USD/GLB/BLEND → FBX
$PYCONV = "E:\Desk\DAM\Altsasu_Manifa\_conv.py"
@'
import bpy, sys, os
argv = sys.argv
try:
    sep = argv.index("--") + 1
    src = argv[sep]; dst = argv[sep+1]
except: sys.exit(1)
ext = os.path.splitext(src)[1].lower()
if ext in ('.blend',):
    bpy.ops.wm.open_mainfile(filepath=src)
elif ext in ('.usd','.usdc','.usdz'):
    bpy.ops.wm.usd_import(filepath=src)
elif ext in ('.glb','.gltf'):
    bpy.ops.import_scene.gltf(filepath=src)
elif ext in ('.obj',):
    bpy.ops.wm.obj_import(filepath=src)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=dst, use_selection=False,
    apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
    bake_space_transform=True, add_leaf_bones=False, path_mode='COPY', embed_textures=False)
print(f"OK: {dst}")
'@ | Out-File -Encoding UTF8 $PYCONV

function Extraer($zip, $carpeta) {
    New-Item -ItemType Directory -Force -Path $carpeta | Out-Null
    & $7ZIP x $zip -o"$carpeta" -y -bso0 -bsp0 2>$null
}

function Convertir($src, $dst) {
    New-Item -ItemType Directory -Force -Path (Split-Path $dst) | Out-Null
    & $BLENDER --background --python $PYCONV -- $src $dst 2>$null | Out-Null
    return Test-Path $dst
}

function Copiar3D($origen, $destino) {
    New-Item -ItemType Directory -Force -Path $destino | Out-Null
    $modelos  = @(Get-ChildItem $origen -Recurse -Include "*.fbx","*.FBX","*.obj","*.OBJ","*.glb","*.gltf")
    $texturas = @(Get-ChildItem $origen -Recurse -Include "*.png","*.jpg","*.jpeg","*.tga","*.tif")
    foreach ($m in $modelos)  { Copy-Item $m.FullName (Join-Path $destino $m.Name)  -Force }
    foreach ($t in $texturas) { Copy-Item $t.FullName (Join-Path $destino $t.Name) -Force }
    return $modelos.Count
}

Write-Host "═══ EXTRACTOR E:\assets → Alsasua ═══" -ForegroundColor Cyan
$total = 0

# ── 1. PERSONAJES MESHY AI (FBX directos) ────────────────────────────────
Write-Host "`n[PERSONAJES MESHY AI]" -ForegroundColor Yellow
$meshyOut = "$DESTINO\Personajes\MeshyAI"
New-Item -ItemType Directory -Force $meshyOut | Out-Null

$meshyItems = @(
    @{zip="Meshy_AI_Guardia_Civil_Officer_0501071058_texture_fbx.zip"; nombre="GuardiaCivil_Officer_01"},
    @{zip="Meshy_AI_Guardia_Civil_Open_Ar_0501071239_texture_fbx.zip"; nombre="GuardiaCivil_OpenArms"},
    @{zip="Meshy_AI_Guardia_Civil_Officer_biped.zip";                   nombre="GuardiaCivil_Biped_Anims"},
    @{zip="Meshy_AI_Casual_Confidence_0421161928_texture_fbx.zip";      nombre="Civil_Casual_01"},
    @{zip="Meshy_AI_Casual_Denim_Overalls_0421162223_texture_fbx.zip";  nombre="Civil_DenimOveralls"},
    @{zip="Meshy_AI_Casual_Summer_Street__0421162005_texture_fbx.zip";  nombre="Civil_SummerStreet_01"},
    @{zip="Meshy_AI_Urban_Edge_0501071204_texture_fbx.zip";             nombre="Civil_UrbanEdge"},
    @{zip="Meshy_AI_Pruu_0421162445_texture_fbx.zip";                   nombre="Civil_Pruu"},
    @{zip="Meshy_AI_Park_Ranger_in_Unifor_biped (1).zip";               nombre="Ranger_Unifor_Biped"},
    @{zip="Meshy_AI_Code_and_Kicks_0421162111_texture_fbx.zip";         nombre="Civil_CodeKicks"},
    @{zip="Meshy_AI_White_Coat_Physician__0421162314_texture_fbx.zip";  nombre="Civil_Doctor"},
    @{zip="Meshy_AI_Confident_Executive_i_0421162135_texture_fbx.zip";  nombre="Civil_Executive"},
    @{zip="Meshy_AI_Soft_Gaze_in_Warm_Lig_biped.zip";                   nombre="Civil_SoftGaze_Biped"}
)

# FBX sueltos de animación
$fbxSueltos = @(
    "Meshy_AI_Animation_Walking_frame_rate_60.fbx",
    "Meshy_AI_Animation_Walking_withSkin.fbx",
    "Meshy_AI_Animation_Walking_withSkin(1).fbx"
)
foreach ($f in $fbxSueltos) {
    $src = Join-Path $ORIGEN $f
    if (Test-Path $src) { Copy-Item $src "$meshyOut\$f" -Force; $total++ }
}

foreach ($m in $meshyItems) {
    $src = Join-Path $ORIGEN $m.zip
    if (-not (Test-Path $src)) { Write-Host "  ⬛ No encontrado: $($m.zip)" -ForegroundColor DarkGray; continue }
    $tmp = "$env:TEMP\meshy_$($m.nombre)"
    Extraer $src $tmp
    $n = Copiar3D $tmp "$meshyOut\$($m.nombre)"
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
    $total += $n
    Write-Host "  ✅ $($m.nombre) ($n archivos)" -ForegroundColor Green
}

# ── 2. ÁRBOLES NATIVOS DE NAVARRA (USD → FBX via Blender) ────────────────
Write-Host "`n[ÁRBOLES NATIVOS — USD → FBX]" -ForegroundColor Yellow
$treesOut = "$DESTINO\Vegetation\TreesNavarra"
New-Item -ItemType Directory -Force $treesOut | Out-Null

$trees = @(
    @{zip="tree_english_oak_forest_01_usd.zip";     nombre="Roble_Ingles"},     # Quercus robur
    @{zip="tree_european_beech_01_usd.zip";          nombre="Haya_Europea"},     # Fagus sylvatica
    @{zip="tree_baltic_pine_01_usd.zip";             nombre="Pino_Baltico"},     # Pinus sylvestris
    @{zip="tree_black_alder_01_usd.zip";             nombre="Aliso_Negro"},      # Alnus glutinosa
    @{zip="tree_common_hazel_01_usd.zip";            nombre="Avellano"},         # Corylus avellana
    @{zip="tree_european_aspen_01_usd.zip";          nombre="Alamo_Templon"},    # Populus tremula
    @{zip="tree_goat_willow_01_usd.zip";             nombre="Sauce_Caprio"},     # Salix caprea
    @{zip="tree_norway_maple_01_usd.zip";            nombre="Arce_Noruego"},     # Acer platanoides
    @{zip="tree_aleppo_pine_01_usd.zip";             nombre="Pino_Carrasco"},    # Pinus halepensis
    @{zip="fall-grijze-populier-tree-2k.zip";        nombre="Chopo_Otoño"},
    @{zip="realistic-trees-pack-of-2-free.zip";      nombre="Arboles_Realistas"},
    @{zip="highpoly_tree_model.zip";                 nombre="Arbol_HighPoly"},
    @{zip="tree_baltic_pine_saplings_01.zip";        nombre="Pino_Plantones"}
)

foreach ($t in $trees) {
    $src = Join-Path $ORIGEN $t.zip
    if (-not (Test-Path $src)) { continue }
    $tmp = "$env:TEMP\tree_$($t.nombre)"
    Extraer $src $tmp

    # Buscar USD o FBX o GLB dentro
    $usdFiles = Get-ChildItem $tmp -Recurse -Include "*.usd","*.usdc","*.usdz" | Select-Object -First 1
    $fbxFiles = Get-ChildItem $tmp -Recurse -Include "*.fbx","*.FBX","*.glb","*.gltf" | Select-Object -First 1

    if ($fbxFiles) {
        $n = Copiar3D $tmp "$treesOut\$($t.nombre)"
        $total += $n
        Write-Host "  ✅ $($t.nombre) — FBX/GLB directo ($n archivos)" -ForegroundColor Green
    } elseif ($usdFiles) {
        $fbxOut = "$treesOut\$($t.nombre)\$($t.nombre).fbx"
        New-Item -ItemType Directory -Force "$treesOut\$($t.nombre)" | Out-Null
        # Copiar texturas
        Get-ChildItem $tmp -Recurse -Include "*.png","*.jpg","*.tga" |
            ForEach-Object { Copy-Item $_.FullName "$treesOut\$($t.nombre)\" -Force }
        if (Convertir $usdFiles.FullName $fbxOut) {
            Write-Host "  ✅ $($t.nombre) — USD→FBX convertido" -ForegroundColor Green; $total++
        } else {
            # Guardar USD directamente (compatible con Unity USD package)
            Copy-Item $usdFiles.FullName "$treesOut\$($t.nombre)\$($t.nombre).usd" -Force
            Write-Host "  ⚠️ $($t.nombre) — USD guardado (instala com.unity.formats.usd)" -ForegroundColor Yellow
            $total++
        }
    }
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}

# ── 3. VEHÍCULOS (GLB/BLEND → FBX) ───────────────────────────────────────
Write-Host "`n[VEHÍCULOS]" -ForegroundColor Yellow
$vehOut = "$DESTINO\Vehicles"
New-Item -ItemType Directory -Force $vehOut | Out-Null

$vehiculos = @(
    @{file="red-tractor.zip";               nombre="Tractor_Rojo";      conv=$true},
    @{file="compact-hatchback.zip";          nombre="Coche_Compacto";    conv=$true},
    @{file="ambulance.zip";                  nombre="Ambulancia";        conv=$true},
    @{file="abandoned-junk-car.zip";         nombre="Coche_Abandonado";  conv=$true},
    @{file="marcopolo-ideale-770.zip";       nombre="Autobus";           conv=$true},
    @{file="trabant_601_wip.zip";            nombre="Trabant_601";       conv=$true},
    @{file="bicycle-collection-free.zip";    nombre="Bicicletas";        conv=$true},
    @{file="road-roller-truck.zip";          nombre="Camion_Apisonadora";conv=$true},
    @{file="red_car_wreck.zip";              nombre="Coche_Siniestrado"; conv=$true},
    @{file="retextured-m725-military-ambulance.zip"; nombre="Ambulancia_Militar"; conv=$true},
    @{file="sm_subwaycar.fbx";               nombre="Vagon_Metro";       conv=$false},
    @{file="photogrammetry-train-station-model.zip"; nombre="Estacion_Tren"; conv=$true}
)

foreach ($v in $vehiculos) {
    $srcPath = Join-Path $ORIGEN $v.file
    if (-not (Test-Path $srcPath)) { continue }
    $outDir = "$vehOut\$($v.nombre)"
    New-Item -ItemType Directory -Force $outDir | Out-Null

    if (-not $v.conv) {
        # FBX directo
        Copy-Item $srcPath $outDir -Force; $total++
        Write-Host "  ✅ $($v.nombre)" -ForegroundColor Green
        continue
    }

    $tmp = "$env:TEMP\veh_$($v.nombre)"
    Extraer $srcPath $tmp

    $modelos = Get-ChildItem $tmp -Recurse -Include "*.fbx","*.FBX","*.glb","*.gltf","*.obj","*.blend" |
               Sort-Object Length -Descending | Select-Object -First 1

    if ($modelos) {
        $ext = $modelos.Extension.ToLower()
        if ($ext -in ".fbx",".obj") {
            Copiar3D $tmp $outDir | Out-Null; $total++
            Write-Host "  ✅ $($v.nombre) — $ext directo" -ForegroundColor Green
        } else {
            $fbxOut = "$outDir\$($v.nombre).fbx"
            Get-ChildItem $tmp -Recurse -Include "*.png","*.jpg","*.tga" |
                ForEach-Object { Copy-Item $_.FullName $outDir -Force }
            if (Convertir $modelos.FullName $fbxOut) {
                $total++
                Write-Host "  ✅ $($v.nombre) — $ext→FBX" -ForegroundColor Green
            } else {
                # Copiar original
                Copy-Item $modelos.FullName $outDir -Force; $total++
                Write-Host "  ⚠️ $($v.nombre) — conversión falló, original copiado" -ForegroundColor Yellow
            }
        }
    }
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}

# ── 4. ENTORNO VASCO-NAVARRO (fotogrametría) ──────────────────────────────
Write-Host "`n[ENTORNO VASCO-NAVARRO — Fotogrametría]" -ForegroundColor Yellow
$entornoOut = "$DESTINO\EntornoVasco"
New-Item -ItemType Directory -Force $entornoOut | Out-Null

$entorno = @(
    "dolmen-de-sorginetxe-alava.zip",
    "parte-muralla-ciudad-de-viana-navarra.zip",
    "central-nuclear-de-lemoniz-spain.zip",
    "photogrammetry-train-station-model.zip",
    "sator-zuloak.zip",
    "antiguo-colegio-de-segoviela.zip"
)
foreach ($e in $entorno) {
    $src = Join-Path $ORIGEN $e
    if (-not (Test-Path $src)) { continue }
    $nombre = [System.IO.Path]::GetFileNameWithoutExtension($e)
    $tmp = "$env:TEMP\ent_$nombre"
    Extraer $src $tmp
    $n = Copiar3D $tmp "$entornoOut\$nombre"
    if ($n -eq 0) {
        # Intentar conversión
        $m = Get-ChildItem $tmp -Recurse -Include "*.blend","*.usd","*.glb","*.gltf" |
             Sort-Object Length -Descending | Select-Object -First 1
        if ($m) {
            $fbxOut = "$entornoOut\$nombre\$nombre.fbx"
            New-Item -ItemType Directory -Force "$entornoOut\$nombre" | Out-Null
            if (Convertir $m.FullName $fbxOut) { $n = 1 }
        }
    }
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
    $total += $n
    if ($n -gt 0) { Write-Host "  ✅ $nombre ($n archivos)" -ForegroundColor Green }
    else          { Write-Host "  ⚠️ $nombre — sin modelos 3D reconocidos" -ForegroundColor Yellow }
}

# ── 5. PROPS Y NATURALEZA ─────────────────────────────────────────────────
Write-Host "`n[PROPS Y NATURALEZA]" -ForegroundColor Yellow
$propsOut = "$DESTINO\Props"
New-Item -ItemType Directory -Force "$propsOut\Urban"   | Out-Null
New-Item -ItemType Directory -Force "$propsOut\Nature"  | Out-Null
New-Item -ItemType Directory -Force "$propsOut\Weapons" | Out-Null

$props = @(
    @{file="trashbin.zip";               cat="Urban";   nombre="Contenedor"},
    @{file="trashcan_fbx.zip";           cat="Urban";   nombre="Papelera"},
    @{file="container.zip";              cat="Urban";   nombre="Container_Metal"},
    @{file="electrical_panel.zip";       cat="Urban";   nombre="Panel_Electrico"},
    @{file="picnic-table-low-poly.zip";  cat="Urban";   nombre="Mesa_Picnic"},
    @{file="playground_fbx.fbx";         cat="Urban";   nombre="Parque_Infantil"},
    @{file="old-steel-animal-cage-trailer.zip"; cat="Urban"; nombre="Remolque_Ganadero"},
    @{file="football.zip";               cat="Urban";   nombre="Balon_Futbol"},
    @{file="gas-mask.zip";               cat="Urban";   nombre="Masca_Gas"},
    @{file="bigleaf_hydrangea_vgztealha_low.zip"; cat="Nature"; nombre="Hortensia"},
    @{file="bolete_mushrooms_pdvcb_low.zip";      cat="Nature"; nombre="Seta_Boletus"},
    @{file="polyporus-mushroom.zip";     cat="Nature";  nombre="Hongo_Polyporus"},
    @{file="wild_grass_vczndjqja_low.zip"; cat="Nature"; nombre="Hierba_Silvestre"},
    @{file="cliff-rock-boulder-field.zip"; cat="Nature"; nombre="Rocas_Acantilado"},
    @{file="grass-vegitation-mix.zip";   cat="Nature";  nombre="Hierba_Mix"},
    @{file="realistic-trees-pack-of-2-free.zip"; cat="Nature"; nombre="Arboles_Realistas"},
    @{file="ak47fbx.zip";                cat="Weapons"; nombre="AK47_HD"},
    @{file="fps_gun_4k_pistol_2.zip";    cat="Weapons"; nombre="Pistola_4K"},
    @{file="cc0-bomb.zip";               cat="Weapons"; nombre="Bomba"},
    @{file="undercover-cop-animated.zip";cat="Weapons"; nombre="Policia_Encubierto"}
)

foreach ($p in $props) {
    $src = Join-Path $ORIGEN $p.file
    if (-not (Test-Path $src)) { continue }
    $outDir = "$propsOut\$($p.cat)\$($p.nombre)"
    New-Item -ItemType Directory -Force $outDir | Out-Null

    if ($p.file -match "\.fbx$") {
        Copy-Item $src $outDir -Force; $total++
        Write-Host "  ✅ $($p.nombre)" -ForegroundColor Green; continue
    }

    $tmp = "$env:TEMP\prop_$($p.nombre)"
    Extraer $src $tmp
    $n = Copiar3D $tmp $outDir
    if ($n -eq 0) {
        $m = Get-ChildItem $tmp -Recurse -Include "*.blend","*.usd","*.glb","*.gltf" |
             Sort-Object Length -Descending | Select-Object -First 1
        if ($m) {
            $fbxOut = "$outDir\$($p.nombre).fbx"
            if (Convertir $m.FullName $fbxOut) { $n = 1 }
            else {
                Copy-Item $m.FullName $outDir -Force; $n = 1
            }
            Get-ChildItem $tmp -Recurse -Include "*.png","*.jpg","*.tga" |
                ForEach-Object { Copy-Item $_.FullName $outDir -Force }
        }
    }
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
    $total += $n
    if ($n -gt 0) { Write-Host "  ✅ $($p.nombre)" -ForegroundColor Green }
}

# ── 6. UNITYPACKAGES ÚTILES → copiar a PackagesToImport ──────────────────
Write-Host "`n[UNITYPACKAGES → PackagesToImport]" -ForegroundColor Yellow
$pkgDest = "E:\Desk\DAM\Altsasu_Manifa\PackagesToImport"
$utiles = @(
    "Metal and Concrete Barriers.unitypackage",
    "Modular self-stand fence.unitypackage",
    "campfires.unitypackage",
    "femalecivilian_2021328f1.unitypackage",
    "animation_pack_basic_motions.unitypackage",
    "the_village_well.unitypackage",
    "street_lights_pack_01.unitypackage",
    "banterman.unitypackage",
    "pc3d_birch_bark_v1.unitypackage",
    "Nature - Essentials.unitypackage",
    "oldbarn.unitypackage",
    "Free Snow Mountain.unitypackage",
    "fast dynamic sky.unitypackage"
)
foreach ($p in $utiles) {
    $src = Get-ChildItem $ORIGEN -Filter $p -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $src) {
        # Try case insensitive
        $src = Get-ChildItem $ORIGEN | Where-Object { $_.Name -ieq $p } | Select-Object -First 1
    }
    if ($src -and -not (Test-Path "$pkgDest\$($src.Name)")) {
        Copy-Item $src.FullName $pkgDest -Force
        Write-Host "  ✅ Copiado a PackagesToImport: $($src.Name)" -ForegroundColor Green
    }
}

# ── RESUMEN ───────────────────────────────────────────────────────────────
Remove-Item $PYCONV -Force -ErrorAction SilentlyContinue
Write-Host "`n═══ COMPLETADO ═══" -ForegroundColor Cyan
Write-Host "  Total archivos procesados: $total" -ForegroundColor White
Write-Host "  Destino: $DESTINO" -ForegroundColor White
Write-Host "`n  Estructura:" -ForegroundColor White
Get-ChildItem $DESTINO -Directory | ForEach-Object {
    $n = (Get-ChildItem $_.FullName -Recurse -Include "*.fbx","*.FBX","*.obj","*.usd","*.glb").Count
    Write-Host "    📁 $($_.Name)/  ($n modelos)" -ForegroundColor Gray
}
Write-Host "`n  → En Unity: Tools → Alsasua → 📥 Importar Assets Extraídos" -ForegroundColor Yellow
