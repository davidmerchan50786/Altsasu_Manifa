# ConstruirTodo.ps1 — construye materiales, meta files, carpetas y valida todo
# Ejecuta ANTES de abrir Unity para que todo este listo al importar
param()
$ErrorActionPreference = "Continue"
$BASE    = Split-Path $MyInvocation.MyCommand.Path
$ASSETS  = Join-Path $BASE "Assets"
$SCRIPTS = Join-Path $ASSETS "Scripts"

Write-Host "=== CONSTRUIR TODO ===" -ForegroundColor Cyan

# ============================================================
# 1. CARPETAS NECESARIAS
# ============================================================
Write-Host "[1] Creando carpetas..." -ForegroundColor Yellow
$dirs = @(
    "Assets/#Scenes",
    "Assets/Animators",
    "Assets/Audio",
    "Assets/Resources",
    "Assets/Resources/Audio",
    "Assets/Prefabs",
    "Assets/Prefabs/Coches",
    "Assets/Prefabs/Personajes",
    "Assets/Prefabs/FBX",
    "Assets/Prefabs/FBX/Edificios",
    "Assets/Prefabs/FBX/Vehiculos",
    "Assets/Prefabs/FBX/Personajes",
    "Assets/Prefabs/FBX/Naturaleza",
    "Assets/Prefabs/FBX/Animales",
    "Assets/Prefabs/FBX/Props",
    "Assets/Prefabs/FBX/Armas",
    "Assets/Prefabs/FBX/Mundo",
    "Assets/Materials",
    "Assets/Materials/Edificios",
    "Assets/Materials/Naturaleza",
    "Assets/Materials/Vehiculos",
    "Assets/Materials/Suelo",
    "Assets/Materials/Props"
)
foreach ($d in $dirs) {
    $full = Join-Path $BASE $d
    if (-not (Test-Path $full)) {
        New-Item -ItemType Directory -Force -Path $full | Out-Null
        Write-Host "  + $d"
    }
}

# ============================================================
# 2. .META PARA CARPETAS NUEVAS
# ============================================================
Write-Host "[2] Meta files para carpetas..." -ForegroundColor Yellow
function New-FolderMeta($path) {
    $meta = "$path.meta"
    if (-not (Test-Path $meta)) {
        $guid = [System.Guid]::NewGuid().ToString("N")
        @"
fileFormatVersion: 2
guid: $guid
folderAsset: yes
DefaultImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
"@ | Set-Content $meta -Encoding UTF8
    }
}
$dirs | ForEach-Object { New-FolderMeta (Join-Path $BASE $_) }

# ============================================================
# 3. .META PARA TODOS LOS SCRIPTS SIN META
# ============================================================
Write-Host "[3] Meta files para scripts..." -ForegroundColor Yellow
function New-ScriptMeta($path) {
    $meta = "$path.meta"
    if (-not (Test-Path $meta)) {
        $guid = [System.Guid]::NewGuid().ToString("N")
        @"
fileFormatVersion: 2
guid: $guid
MonoImporter:
  externalObjects: {}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {instanceID: 0}
  userData:
  assetBundleName:
  assetBundleVariant:
"@ | Set-Content $meta -Encoding UTF8
        return $true
    }
    return $false
}

$count = 0
Get-ChildItem -Path $SCRIPTS -Filter "*.cs" -Recurse | ForEach-Object {
    if (New-ScriptMeta $_.FullName) { $count++ }
}
Write-Host "  $count metas de script creados"

# ============================================================
# 4. .META PARA FBX/OBJ SIN META
# ============================================================
Write-Host "[4] Meta files para modelos 3D..." -ForegroundColor Yellow
function New-ModelMeta($path, $humanoid = $false) {
    $meta = "$path.meta"
    if (-not (Test-Path $meta)) {
        $guid  = [System.Guid]::NewGuid().ToString("N")
        $anim  = if ($humanoid) { 3 } else { 1 }  # 3=Humanoid, 1=Generic
        @"
fileFormatVersion: 2
guid: $guid
ModelImporter:
  serializedVersion: 22200
  internalIDToNameTable: []
  externalObjects: {}
  materials:
    materialImportMode: 2
    materialName: 0
    materialSearch: 1
    materialLocation: 1
  meshes:
    lod: 0
    globalScale: 1
    meshCompression: 0
    addColliders: 0
    useSRGBMaterialColor: 1
    sortHierarchyByName: 1
    importVisibility: 1
    importBlendShapes: 1
    importCameras: 0
    importLights: 0
    preserveHierarchy: 0
    skinWeightsMode: 0
    maxBonesPerVertex: 4
    minBoneWeight: 0.001
    optimizeBones: 1
  animations:
    legacyGenerateAnimations: 4
    bakeSimulation: 0
    resampleCurves: 1
    optimizeGameObjects: 0
    motionNodeName:
    rigImportErrors:
    rigImportWarnings:
    animationImportErrors:
    animationImportWarnings:
    animationRetargetingWarnings:
    animationDoRetargetingWarnings: 0
    importAnimatedCustomProperties: 0
    importConstraints: 0
    animationCompression: 1
    animationRotationError: 0.5
    animationPositionError: 0.5
    animationScaleError: 0.5
    animationWrapMode: 0
    extraExposedTransformPaths: []
    extraUserProperties: []
    clipAnimations: []
    isReadable: 0
  platforms: []
  userData:
  assetBundleName:
  assetBundleVariant:
"@ | Set-Content $meta -Encoding UTF8
        return $true
    }
    return $false
}

$extractedDir = Join-Path $ASSETS "_ExtractedAssets"
$mcount = 0
Get-ChildItem -Path $extractedDir -Include "*.fbx","*.obj","*.FBX","*.OBJ" -Recurse | ForEach-Object {
    $isHuman = $_.FullName -like "*Personajes*" -or $_.FullName -like "*Characters*" -or $_.Name -like "*Guardia*" -or $_.Name -like "*Soldier*"
    if (New-ModelMeta $_.FullName $isHuman) { $mcount++ }
}
Write-Host "  $mcount metas de modelos 3D creados"

# ============================================================
# 5. .META PARA TEXTURAS SIN META
# ============================================================
Write-Host "[5] Meta files para texturas..." -ForegroundColor Yellow
function New-TextureMeta($path, $isNormal = $false) {
    $meta = "$path.meta"
    if (-not (Test-Path $meta)) {
        $guid = [System.Guid]::NewGuid().ToString("N")
        $type = if ($isNormal) { 1 } else { 0 }  # 1=NormalMap, 0=Default
        @"
fileFormatVersion: 2
guid: $guid
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {}
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: 1
    sRGBTexture: $(if ($isNormal) { 0 } else { 1 })
    linearTexture: 0
  textureType: $type
  textureShape: 1
  singleChannelComponent: 0
  flipbookRows: 1
  flipbookColumns: 1
  maxTextureSize: 2048
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
    mipBias: 0
    wrapMode: 0
  npotScale: 1
  lightmap: 0
  compressionQuality: 50
  spriteMode: 0
  spriteExtrude: 1
  spriteMeshType: 1
  alignment: 0
  spritePivot: {x: 0.5, y: 0.5}
  spritePixelsToUnits: 100
  spriteBorder: {x: 0, y: 0, z: 0, w: 0}
  spriteGenerateFallbackPhysicsShape: 1
  alphaUsage: 1
  alphaIsTransparency: 0
  spriteTessellationDetail: -1
  textureFormat: -1
  maxTextureSizeSet: 0
  compressionQualitySet: 0
  textureFormatSet: 0
  ignorePngGamma: 0
  applyGammaDecoding: 0
  cookieLightType: 0
  platformSettings:
  - serializedVersion: 3
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 1
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  sprites: []
  outline: []
  physicsShape: []
  bones: []
  spriteID:
  internalID: 0
  vertices: []
  edges: []
  weights: []
  secondaryTextures: []
  nameFileIdTable: {}
  userData:
  assetBundleName:
  assetBundleVariant:
"@ | Set-Content $meta -Encoding UTF8
        return $true
    }
    return $false
}

$tcount = 0
$texDir = Join-Path $ASSETS "Textures_AAA"
if (Test-Path $texDir) {
    Get-ChildItem -Path $texDir -Include "*.png","*.jpg","*.jpeg" -Recurse | ForEach-Object {
        $isNorm = $_.Name -like "*nor_gl*" -or $_.Name -like "*normal*" -or $_.Name -like "*nrm*"
        if (New-TextureMeta $_.FullName $isNorm) { $tcount++ }
    }
}
Write-Host "  $tcount metas de textura creados"

# ============================================================
# 6. .META PARA AUDIO
# ============================================================
Write-Host "[6] Meta files para audio..." -ForegroundColor Yellow
function New-AudioMeta($path) {
    $meta = "$path.meta"
    if (-not (Test-Path $meta)) {
        $guid = [System.Guid]::NewGuid().ToString("N")
        $isLoop = $path -like "*lluvia*" -or $path -like "*viento*" -or $path -like "*motor*" -or $path -like "*pajaros*"
        $load   = if ($isLoop) { 2 } else { 0 }  # 2=Streaming, 0=DecompressOnLoad
        @"
fileFormatVersion: 2
guid: $guid
AudioImporter:
  externalObjects: {}
  serializedVersion: 6
  defaultSettings:
    loadType: $load
    sampleRateSetting: 0
    sampleRateOverride: 44100
    compressionFormat: 3
    quality: 0.7
    conversionMode: 0
  platformSettingOverrides: {}
  forceToMono: 0
  normalize: 0
  preloadAudioData: $(if ($isLoop) { 0 } else { 1 })
  loadInBackground: 1
  3D: 0
  userData:
  assetBundleName:
  assetBundleVariant:
"@ | Set-Content $meta -Encoding UTF8
        return $true
    }
    return $false
}

$acount = 0
$audioDir = Join-Path $ASSETS "Resources\Audio"
if (Test-Path $audioDir) {
    Get-ChildItem -Path $audioDir -Include "*.mp3","*.ogg","*.wav" | ForEach-Object {
        if (New-AudioMeta $_.FullName) { $acount++ }
    }
}
Write-Host "  $acount metas de audio creados"

# ============================================================
# 7. .META PARA HDRIs
# ============================================================
Write-Host "[7] Meta files para HDRIs..." -ForegroundColor Yellow
$hdriDir = Join-Path $ASSETS "HDRIs"
if (Test-Path $hdriDir) {
    Get-ChildItem -Path $hdriDir -Include "*.hdr","*.exr" | ForEach-Object {
        $meta = "$($_.FullName).meta"
        if (-not (Test-Path $meta)) {
            $guid = [System.Guid]::NewGuid().ToString("N")
            @"
fileFormatVersion: 2
guid: $guid
TextureImporter:
  serializedVersion: 13
  textureType: 9
  textureShape: 2
  maxTextureSize: 2048
  textureFormat: -1
  sRGBTexture: 0
  userData:
  assetBundleName:
  assetBundleVariant:
"@ | Set-Content $meta -Encoding UTF8
            Write-Host "  + HDRI: $($_.Name)"
        }
    }
}

# ============================================================
# 8. VERIFICAR BUILD SETTINGS
# ============================================================
Write-Host "[8] Verificando ProjectSettings..." -ForegroundColor Yellow
$graphicsPath = Join-Path $BASE "ProjectSettings\GraphicsSettings.asset"
if (Test-Path $graphicsPath) {
    $content = Get-Content $graphicsPath -Raw
    if ($content -like "*HDRenderPipeline*") {
        Write-Host "  OK: HDRP configurado en GraphicsSettings"
    } else {
        Write-Host "  WARN: HDRP no detectado en GraphicsSettings"
    }
}

# ============================================================
# RESUMEN
# ============================================================
Write-Host ""
Write-Host "=== RESUMEN ===" -ForegroundColor Cyan

$checks = @(
    @{ nombre="buildings_unity.json"; ruta="Assets\AlsasuaData\buildings_unity.json" },
    @{ nombre="roads_unity.json";     ruta="Assets\AlsasuaData\roads_unity.json" },
    @{ nombre="dem_unity_1025.raw";   ruta="Assets\AlsasuaData\dem_unity_1025.raw" },
    @{ nombre="Audio (14 clips)";     ruta="Assets\Resources\Audio" },
    @{ nombre="Textures_AAA (542)";   ruta="Assets\Textures_AAA" },
    @{ nombre="HDRIs (11)";           ruta="Assets\HDRIs" },
    @{ nombre="Scripts (45)";         ruta="Assets\Scripts" },
    @{ nombre="Scripts Editor (26)";  ruta="Assets\Scripts\Editor" },
    @{ nombre="FBX/OBJ (168)";        ruta="Assets\_ExtractedAssets" }
)

foreach ($c in $checks) {
    $full = Join-Path $BASE $c.ruta
    $ok   = Test-Path $full
    $icon = if ($ok) { "OK" } else { "FALTA" }
    Write-Host "  $icon $($c.nombre)"
}

Write-Host ""
Write-Host "Abre Unity y ejecuta:" -ForegroundColor Green
Write-Host "  Tools -> Alsasua -> MEGA BUILD (Construir todo ahora)" -ForegroundColor Green
Write-Host ""
Write-Host "O ejecuta Unity en batch mode:" -ForegroundColor Yellow
Write-Host "  unity_batch.bat" -ForegroundColor Yellow
