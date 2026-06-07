# ProcessAllAssets.ps1
# ═══════════════════════════════════════════════════════════════════════════
#  Procesa TODOS los 372 archivos de E:\assets para el proyecto Alsasua
#  Clasifica, extrae, convierte y organiza automáticamente.
#
#  Ejecutar como Administrador:
#    cd "E:\Desk\DAM\Altsasu_Manifa"
#    .\ProcessAllAssets.ps1
# ═══════════════════════════════════════════════════════════════════════════

$7Z      = "C:\Program Files\7-Zip\7z.exe"
$BLENDER = "C:\Program Files\Blender Foundation\Blender 5.1\blender.exe"
$ORIGEN  = "E:\assets"
$DEST    = "E:\Desk\DAM\Altsasu_Manifa\Assets\_ExtractedAssets"
$PKGS    = "E:\Desk\DAM\Altsasu_Manifa\PackagesToImport"

# Script Blender para conversión universal
$PY = "$env:TEMP\_conv_all.py"
@'
import bpy, sys, os
argv = sys.argv
try:
    sep = argv.index("--") + 1
    src = argv[sep]; dst = argv[sep+1]
except: sys.exit(1)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete()
ext = os.path.splitext(src)[1].lower()
try:
    if   ext == '.blend': bpy.ops.wm.open_mainfile(filepath=src)
    elif ext in ('.usd','.usdc','.usdz'): bpy.ops.wm.usd_import(filepath=src)
    elif ext in ('.glb','.gltf'): bpy.ops.import_scene.gltf(filepath=src)
    elif ext == '.obj': bpy.ops.wm.obj_import(filepath=src)
    elif ext == '.3ds': bpy.ops.import_scene.autodesk_3ds(filepath=src)
except Exception as e:
    print(f"ERROR import: {e}"); sys.exit(1)
os.makedirs(os.path.dirname(dst), exist_ok=True)
bpy.ops.export_scene.fbx(filepath=dst, use_selection=False,
    apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
    bake_space_transform=True, add_leaf_bones=False,
    path_mode='COPY', embed_textures=False)
print(f"✅ {dst}")
'@ | Out-File -Encoding UTF8 $PY

function Conv($src, $dst) {
    & $BLENDER --background --python $PY -- $src $dst 2>$null | Out-Null
    return (Test-Path $dst)
}

function Ext($zip, $dir) {
    New-Item -ItemType Directory -Force $dir | Out-Null
    & $7Z x $zip -o"$dir" -y -bso0 -bsp0 2>$null
}

function Copy3D($src_dir, $dst_dir) {
    New-Item -ItemType Directory -Force $dst_dir | Out-Null
    $exts3d = "*.fbx","*.FBX","*.obj","*.OBJ","*.glb","*.gltf","*.usd","*.usdc"
    $extsTex = "*.png","*.jpg","*.jpeg","*.tga","*.tif","*.bmp","*.exr"
    $models = @(Get-ChildItem $src_dir -Recurse -Include $exts3d)
    $texs   = @(Get-ChildItem $src_dir -Recurse -Include $extsTex)
    foreach ($f in $models) { Copy-Item $f.FullName (Join-Path $dst_dir $f.Name) -Force }
    foreach ($f in $texs)   { Copy-Item $f.FullName (Join-Path $dst_dir $f.Name) -Force }
    return $models.Count
}

function ProcessZip($zip, $nombre, $categoria) {
    $dst = "$DEST\$categoria\$nombre"
    if (Test-Path "$dst\_done") { return 0 }  # ya procesado
    $tmp = "$env:TEMP\paa_$([System.IO.Path]::GetFileNameWithoutExtension($zip))"
    Ext $zip $tmp
    $n = Copy3D $tmp $dst
    if ($n -eq 0) {
        # Intentar conversión
        $candidatos = @(Get-ChildItem $tmp -Recurse -Include "*.blend","*.usd","*.usdc","*.glb","*.gltf","*.obj","*.3ds" |
                        Sort-Object Length -Descending)
        foreach ($c in $candidatos) {
            $fbxOut = "$dst\$nombre.fbx"
            New-Item -ItemType Directory -Force $dst | Out-Null
            @(Get-ChildItem $c.DirectoryName -Include "*.png","*.jpg","*.tga","*.tif") |
                ForEach-Object { Copy-Item $_.FullName $dst -Force }
            if (Conv $c.FullName $fbxOut) { $n = 1; break }
            else {
                Copy-Item $c.FullName $dst -Force; $n = 1; break
            }
        }
    }
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
    if ($n -gt 0) { New-Item "$dst\_done" -Force | Out-Null }
    return $n
}

function ProcessBlend($blend, $nombre, $categoria) {
    $dst = "$DEST\$categoria\$nombre"
    if (Test-Path "$dst\_done") { return 0 }
    New-Item -ItemType Directory -Force $dst | Out-Null
    $fbxOut = "$dst\$nombre.fbx"
    if (Conv $blend $fbxOut) { New-Item "$dst\_done" -Force | Out-Null; return 1 }
    Copy-Item $blend $dst -Force; return 1
}

function CopyPkg($nombre_pkg) {
    $src = Join-Path $ORIGEN $nombre_pkg
    if (-not (Test-Path $src)) { return }
    $dst = Join-Path $PKGS $nombre_pkg
    if (-not (Test-Path $dst)) {
        Copy-Item $src $PKGS -Force
        Write-Host "  📦 $nombre_pkg" -ForegroundColor Cyan
    }
}

function CopyFBX($fbx, $categoria) {
    $dst = "$DEST\$categoria"
    New-Item -ItemType Directory -Force $dst | Out-Null
    $dstFile = Join-Path $dst (Split-Path $fbx -Leaf)
    if (-not (Test-Path $dstFile)) { Copy-Item $fbx $dstFile -Force; return 1 }
    return 0
}

# ─────────────────────────────────────────────────────────────────────────
Write-Host "═══════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  PROCESS ALL ASSETS — E:\assets → Alsasua HDRP GTA" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════════" -ForegroundColor Cyan
$total = 0

# ── 1. UNITYPACKAGES → PackagesToImport ──────────────────────────────────
Write-Host "`n[1/6] Copiando unitypackages útiles..." -ForegroundColor Yellow
$pkgUtiles = @(
    # Personajes / Animaciones
    "femalecivilian_2021328f1.unitypackage",
    "banterman.unitypackage",
    "animation_pack_basic_motions.unitypackage",
    "GTA Unity 1.0.unitypackage",
    # Vegetación / Naturaleza
    "pc3d_birch_bark_v1.unitypackage",       # corteza de abedul HDRP
    "Nature - Essentials.unitypackage",
    "Free Snow Mountain.unitypackage",
    "Idyllic Fantasy Nature.unitypackage",
    "Free Low Poly Nature Forest.unitypackage",
    "Nature Starter Kit 2.unitypackage",
    "Yughues Free Bushes.unitypackage",
    "Realistic Fences Pack.unitypackage",
    # Props urbanos
    "Metal and Concrete Barriers.unitypackage",
    "Modular self-stand fence.unitypackage",
    "campfires.unitypackage",
    "the_village_well.unitypackage",
    "street_lights_pack_01.unitypackage",
    "Street Lamps 2.unitypackage",
    "simple_street_props.unitypackage",
    "Low Poly Food Lite.unitypackage",
    # Edificios
    "VILLAGE HOUSES PACK.unitypackage",
    "House Pack.unitypackage",
    "oldbarn.unitypackage",                  # granero antiguo
    "Hot Rod.unitypackage",
    # Terreno / Materiales
    "freerealisticoutdoormaterials.unitypackage",
    "terrain_textures_free.unitypackage",
    "hill_rock_mountain_terrain.unitypackage",
    "mountain_terrain_rock_and_tree.unitypackage",
    # Efectos / Shaders
    "Fast Dynamic Sky.unitypackage",
    "Free Fire VFX.unitypackage",
    "Fire Effect Vol1.unitypackage",
    "fx_pack_fire_effects.unitypackage",
    "Free Asset VFX Particles Fireball Pack.unitypackage",
    "Blood Gush.unitypackage",
    "Free Fireworks - Fire FX - Nova Sound.unitypackage",
    "Particle Pack.unitypackage",
    "Particle Shaders Vol 1.unitypackage",
    "Cool Visual Effects - Part 1 - URP Support.unitypackage",
    "Ultimate 10 Shaders.unitypackage",
    "Ultra Skybox Fog.unitypackage",
    "Real Stars Skybox Lite.unitypackage",
    "Camo Shaders Pack.unitypackage",        # camuflaje para militares
    # Audio
    "Free General Ambience Sounds.unitypackage",
    "Free Sound Effects Pack.unitypackage",
    "Free Water Stream Sounds.unitypackage",
    "Nature Sounds Pack - Free.unitypackage",
    "Epic Game Hits SFX.unitypackage",
    # Animales
    "Quirky Series - FREE Animals Pack.unitypackage"
)
foreach ($p in $pkgUtiles) { CopyPkg $p }

# ── 2. FBX SUELTOS → copiar directo ──────────────────────────────────────
Write-Host "`n[2/6] Copiando FBX sueltos..." -ForegroundColor Yellow
$fbxSueltos = @(
    @{file="lisbon_building.fbx";            cat="Buildings"},
    @{file="lisbon_building_2.fbx";          cat="Buildings"},
    @{file="Meshy_AI__div_class_sketchfab_0501070910_generate.fbx"; cat="Personajes"},
    @{file="Meshy_AI_Animation_Walking_frame_rate_60.fbx";  cat="Personajes\Animaciones"},
    @{file="Meshy_AI_Animation_Walking_withSkin.fbx";       cat="Personajes\Animaciones"},
    @{file="Meshy_AI_Animation_Walking_withSkin(1).fbx";    cat="Personajes\Animaciones"},
    @{file="rootsground4meshscanforge.fbx";  cat="Vegetation"},
    @{file="playground_fbx.fbx";             cat="Props\Urban"},
    @{file="sm_subwaycar.fbx";               cat="Vehicles"},
    @{file="X Bot@Crouching Idle.fbx";       cat="Personajes\Animaciones"},
    @{file="X Bot@Dying.fbx";               cat="Personajes\Animaciones"},
    @{file="X Bot@Rifle Aiming Idle.fbx";   cat="Personajes\Animaciones"},
    @{file="X Bot@Shooting.fbx";            cat="Personajes\Animaciones"},
    @{file="X Bot@Walk Crouching Forward.fbx"; cat="Personajes\Animaciones"},
    @{file="X Bot@Walking.fbx";             cat="Personajes\Animaciones"}
)
$fbxSueltos += @(
    @{file="electrical_panel.glb"; cat="Props\Urban"},
    @{file="bridge_roads.glb";     cat="Props\Urban"},
    @{file="tunnels.glb";          cat="Props\Urban"},
    @{file="m4a3e8_sherman.glb";   cat="Vehicles\Military"}
)
foreach ($f in $fbxSueltos) {
    $src = Join-Path $ORIGEN $f.file
    if (Test-Path $src) { $total += CopyFBX $src $f.cat; Write-Host "  ✅ $($f.file)" -ForegroundColor Green }
}

# Texturas sueltas
$texSueltas = @("wall_bricks_old_1024.png","wall_cobblestone_1024px.png",
                "wall_cobblestone_cracks_1024px.png","brick7.png","Noite_Sem.png")
$texDest = "$DEST\Textures"
New-Item -ItemType Directory -Force $texDest | Out-Null
foreach ($t in $texSueltas) {
    $src = Join-Path $ORIGEN $t
    if (Test-Path $src) { Copy-Item $src $texDest -Force }
}

# ── 3. ANIMALES (.blend, .7z, .zip) ──────────────────────────────────────
Write-Host "`n[3/6] Procesando animales..." -ForegroundColor Yellow
$animales = @(
    @{file="sheepies.blend";        nombre="Oveja";       blend=$true},
    @{file="riggedHorse.blend";     nombre="Caballo";     blend=$true},
    @{file="deer1.blend";           nombre="Ciervo_blend";blend=$true},
    @{file="doe.blend";             nombre="Cierva_blend";blend=$true},
    @{file="rabbit.blend";          nombre="Conejo_blend";blend=$true},
    @{file="deer-female-FBX.7z";    nombre="Cierva_FBX";  blend=$false},
    @{file="rabbit-FBX.7z";         nombre="Conejo_FBX";  blend=$false},
    @{file="dog.zip";               nombre="Perro";       blend=$false},
    @{file="blackrat_free_fbx.zip"; nombre="Rata";        blend=$false},
    @{file="chicken.zip";           nombre="Gallina";     blend=$false},
    @{file="rooster.zip";           nombre="Gallo";       blend=$false},
    @{file="Wolf.zip";              nombre="Lobo";        blend=$false}
)
foreach ($a in $animales) {
    $src = Join-Path $ORIGEN $a.file
    if (-not (Test-Path $src)) { continue }
    if ($a.blend) {
        $n = ProcessBlend $src $a.nombre "Animals"
    } else {
        $n = ProcessZip $src $a.nombre "Animals"
    }
    $total += $n
    if ($n -gt 0) { Write-Host "  ✅ $($a.nombre)" -ForegroundColor Green }
}

# ── 4. EDIFICIOS Y ENTORNO URBANO ─────────────────────────────────────────
Write-Host "`n[4/6] Procesando edificios..." -ForegroundColor Yellow
$edificios = @(
    @{file="rural_brick_house_with_barn_and_chicken_coop.zip"; nombre="Casa_Rural_Granero"},
    @{file="background-houses.zip";                             nombre="Casas_Fondo"},
    @{file="abandoned-house.zip";                               nombre="Casa_Abandonada"},
    @{file="spaniards-inn-toll-gate-house.zip";                 nombre="Posada_Espanola"},
    @{file="forest-house-ruin.zip";                             nombre="Casa_Ruinas_Bosque"},
    @{file="mega_moduler_apartment_building_fbx.zip";           nombre="Bloque_Apartamentos"},
    @{file="old-street.zip";                                    nombre="Calle_Antigua"},
    @{file="high_facade_of_the_factory_with_exit_gates.zip";    nombre="Fachada_Fabrica"},
    @{file="postwar-city-exterior-scene.zip";                   nombre="Ciudad_PostGuerra"},
    @{file="apocalyptic-city.zip";                              nombre="Ciudad_Apocaliptica"},
    @{file="House.zip";                                         nombre="Casa_Simple"},
    @{file="hq_building.gltf_.zip";                            nombre="Edificio_HQ"},
    @{file="lowpoly-building.zip";                              nombre="Edificio_LowPoly"},
    @{file="santuario-de-la-virgen-de-la-sierra.zip";           nombre="Santuario_Navarra"},
    @{file="facade-skrivanova-8-brno.zip";                      nombre="Fachada_Europea"},
    @{file="barnwell-priory-cellarers-chequer.zip";              nombre="Priorato_Medieval"},
    @{file="city-3d-model.zip";                                 nombre="Ciudad_3D"},
    @{file="residential_building.blend";                        nombre="Edificio_Residencial"; blend=$true},
    @{file="3TD_Starter Pack.zip";                              nombre="StarterPack_3D"}
)
foreach ($e in $edificios) {
    $src = Join-Path $ORIGEN $e.file
    if (-not (Test-Path $src)) { continue }
    if ($e.blend) { $n = ProcessBlend $src $e.nombre "Buildings" }
    else          { $n = ProcessZip   $src $e.nombre "Buildings" }
    $total += $n
    if ($n -gt 0) { Write-Host "  ✅ $($e.nombre)" -ForegroundColor Green }
}

# ── 5. VEGETACIÓN ADICIONAL ───────────────────────────────────────────────
Write-Host "`n[5/6] Procesando vegetación adicional..." -ForegroundColor Yellow
$veg = @(
    @{file="cypress.zip";                        nombre="Cipres"},
    @{file="bamboo v1.7z";                       nombre="Bambu"},
    @{file="shrub_beach_rose_01_usd.zip";         nombre="Rosa_Silvestre"},
    @{file="trees-and-bush-pack-lowpoly.zip";     nombre="Arboles_LowPoly"},
    @{file="christmass_trees_pack.blend";         nombre="Pinos_Navidad"; blend=$true},
    @{file="rocks.7z";                           nombre="Rocas"},
    @{file="cliff-rock-boulder-field.zip";        nombre="Rocas_Acantilado"},
    @{file="carrara-marble-quarry.zip";           nombre="Cantera_Piedra"},
    @{file="Free Snow Mountain.unitypackage";     skip=$true},  # ya en pkgs
    @{file="wild_grass_vczndjqja_low.zip";        nombre="Hierba_Silvestre_Low"},
    @{file="wild_grass_vlkhcbxia_mid.zip";        nombre="Hierba_Silvestre_Mid"},
    @{file="grass_02.7z";                        nombre="Hierba_02"},
    @{file="grass_07.7z";                        nombre="Hierba_07"},
    @{file="multi stylized grass.7z";            nombre="Hierba_Stylized"},
    @{file="grass-vegitation-mix.zip";           nombre="Hierba_Mix"},
    @{file="bigleaf_hydrangea_vgztealha_low.zip"; nombre="Hortensia"},
    @{file="bolete_mushrooms_pdvcb_low.zip";      nombre="Seta_Boletus_Low"},
    @{file="polyporus-mushroom.zip";              nombre="Hongo_Polyporus"},
    @{file="spotted_red_mushrooms_zbrush.zip";    nombre="Setas_Rojas"},
    @{file="tree_english_oak_forest_01_usd(1).zip"; nombre="Roble_Ingles_B"},  # duplicado extra
    @{file="fall-grijze-populier-tree-2k.zip";    nombre="Chopo_Gris"},
    @{file="moist-clayey-soil-full-of-straw.zip"; nombre="Tierra_Humeda"},
    @{file="rootsground4meshscanforge.fbx";       skip=$true}   # ya copiado
)
foreach ($v in $veg) {
    if ($v.skip) { continue }
    $src = Join-Path $ORIGEN $v.file
    if (-not (Test-Path $src)) { continue }
    if ($v.blend) { $n = ProcessBlend $src $v.nombre "Vegetation" }
    else          { $n = ProcessZip   $src $v.nombre "Vegetation" }
    $total += $n
    if ($n -gt 0) { Write-Host "  ✅ $($v.nombre)" -ForegroundColor Green }
}

# ── 6. PROPS, VEHÍCULOS, ARMAS, TEXTURAS ──────────────────────────────────
Write-Host "`n[6/6] Procesando props, vehículos y texturas..." -ForegroundColor Yellow
$resto = @(
    # Vehículos pendientes
    @{file="ambulance.zip";                      nombre="Ambulancia";         cat="Vehicles"},
    @{file="compact-hatchback.zip";              nombre="Coche_Compacto";     cat="Vehicles"},
    @{file="broken-old-streetcar-free.zip";      nombre="Tranvia_Roto";       cat="Vehicles"},
    @{file="highway-road.zip";                   nombre="Carretera_Autopista";cat="Vehicles"},
    @{file="scan-rail-02-freepolyorg.zip";       nombre="Rail_Tren";          cat="Vehicles"},
    @{file="locomotive00.zip";                   nombre="Locomotora";         cat="Vehicles"},
    @{file="es_fbx.zip";                         nombre="Unkn_ES_FBX";        cat="Vehicles"},

    # Props
    @{file="iy_signs_exp.blend";                nombre="Senales_Trafico"; cat="Props\Urban"; blend=$true},
    @{file="kanistr.zip";                       nombre="Bidon_Combustible"; cat="Props\Urban"},
    @{file="phone.zip";                         nombre="Telefono";         cat="Props\Urban"},
    @{file="jamo-s803-speakers.zip";            nombre="Altavoces";        cat="Props\Urban"},
    @{file="booksOGA_GPL.blend";               nombre="Libros";           cat="Props\Urban"; blend=$true},
    @{file="container.zip";                     nombre="Container_Metal";  cat="Props\Urban"},
    @{file="old-steel-animal-cage-trailer.zip"; nombre="Remolque_Ganado";  cat="Props\Urban"},
    @{file="stone-road.zip";                    nombre="Camino_Piedra";    cat="Props\Urban"},
    @{file="firewood-pack-burned-4k.zip";       nombre="Lena_Quemada";    cat="Props\Urban"},
    @{file="stunt-ramp.zip";                    nombre="Rampa";            cat="Props\Urban"},
    @{file="wooden_cable_reel_uldhdhdva_raw.zip"; nombre="Bobina_Cable";  cat="Props\Urban"},
    @{file="gas-mask.zip";                      nombre="Mascara_Gas";      cat="Props\Urban"},
    @{file="worn-bandages.zip";                 nombre="Vendas";           cat="Props\Urban"},
    @{file="parrot-camo-drone.zip";             nombre="Dron_Camo";        cat="Props\Urban"},
    @{file="cc0-bomb.zip";                      nombre="Bomba";            cat="Props\Weapons"},
    @{file="85-mm-air-defense-gun.zip";         nombre="Canon_Antiaereo";  cat="Props\Weapons"},
    @{file="fpsweapons.zip";                    nombre="FPS_Weapons";      cat="Props\Weapons"},
    @{file="ak47fbx.zip";                       nombre="AK47_HD";          cat="Props\Weapons"},
    @{file="fps_gun_4k_pistol_2.zip";           nombre="Pistola_4K";       cat="Props\Weapons"},
    @{file="undercover-cop-animated.zip";       nombre="Policia_Encubierto"; cat="Personajes"},
    @{file="loyal-guardian-jarman.zip";         nombre="Guardian";          cat="Personajes"},

    # Texturas / materiales
    @{file="asphalt_02_4k.blend.zip";           nombre="Asfalto_4K";      cat="Textures"},
    @{file="pebbled-asphalt1-unity.zip";        nombre="Asfalto_Empedrado"; cat="Textures"},
    @{file="cobblestone_wall_texture.zip";      nombre="Textura_Adoquin";  cat="Textures"},
    @{file="wall_bricks_old_template.zip";      nombre="Textura_Ladrillo"; cat="Textures"},
    @{file="swdirt.zip";                        nombre="Suelo_Sucio";      cat="Textures"},
    @{file="textures.zip";                      nombre="Texturas_Varias";  cat="Textures"},
    @{file="textures(2).zip";                   nombre="Texturas_Varias2"; cat="Textures"},
    @{file="mixed-brick-pile-heap.zip";         nombre="Ladrillos_Rotos";  cat="Textures"},
    @{file="stonepebblesfree.zip";              nombre="Piedras_Guijarros"; cat="Textures"},
    @{file="atlas_1.zip";                       nombre="Atlas_Texturas";   cat="Textures"},
    @{file="lighting-pack-2-forked-lightning.zip"; nombre="Rayos_VFX";    cat="Props\VFX"},

    # Fotogrametría vasco-navarra
    @{file="dolmen-de-sorginetxe-alava.zip";    nombre="Dolmen_Sorginetxe"; cat="EntornoVasco"},
    @{file="parte-muralla-ciudad-de-viana-navarra.zip"; nombre="Muralla_Viana"; cat="EntornoVasco"},
    @{file="central-nuclear-de-lemoniz-spain.zip"; nombre="Lemoniz";       cat="EntornoVasco"},
    @{file="sator-zuloak.zip";                  nombre="Sator_Zuloak";     cat="EntornoVasco"},
    @{file="santuario-de-la-virgen-de-la-sierra.zip"; nombre="Santuario";  cat="EntornoVasco"},
    @{file="Mundo.7z";                          nombre="Mundo";            cat="EntornoVasco"}
)
foreach ($r in $resto) {
    $src = Join-Path $ORIGEN $r.file
    if (-not (Test-Path $src)) { continue }
    if ($r.blend) { $n = ProcessBlend $src $r.nombre $r.cat }
    else          { $n = ProcessZip   $src $r.nombre $r.cat }
    $total += $n
    if ($n -gt 0) { Write-Host "  ✅ $($r.nombre) → $($r.cat)" -ForegroundColor Green }
}

# ── LIMPIEZA ──────────────────────────────────────────────────────────────
Remove-Item $PY -Force -ErrorAction SilentlyContinue

# ── RESUMEN ───────────────────────────────────────────────────────────────
Write-Host "`n═══════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  COMPLETADO" -ForegroundColor Cyan
Write-Host "  Archivos procesados esta ejecución: $total"
Write-Host "  Destino: $DEST"
Write-Host "  PackagesToImport: $PKGS"
Write-Host ""
Write-Host "  Estructura final:" -ForegroundColor White
Get-ChildItem $DEST -Directory -ErrorAction SilentlyContinue | ForEach-Object {
    $n = (Get-ChildItem $_.FullName -Recurse -Include "*.fbx","*.FBX","*.obj","*.glb","*.usd" -ErrorAction SilentlyContinue).Count
    Write-Host "    📁 $($_.Name)/  ($n modelos)" -ForegroundColor Gray
}
Get-ChildItem $PKGS -Filter "*.unitypackage" -ErrorAction SilentlyContinue | Measure-Object | ForEach-Object {
    Write-Host "    📦 PackagesToImport: $($_.Count) paquetes listos para importar en Unity" -ForegroundColor Cyan
}
Write-Host "`n  En Unity: Tools → Alsasua → 📥 Importar Assets Extraídos" -ForegroundColor Yellow
Write-Host "            Tools → Alsasua → 📦 Importar Paquetes" -ForegroundColor Yellow
