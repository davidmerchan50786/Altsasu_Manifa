<#
.SYNOPSIS
    Altsasu Manifa — Pipeline AAA completo (1 click)
    Genera TODOS los edificios 3D con texturas reales desde cero.

.DESCRIPTION
    Paso 1: Captura Street View gratis (Selenium + Chrome headless)
    Paso 2: Texturas sintéticas para edificios sin foto (virtual walker)
    Paso 3: Procesa texturas PBR (Retinex + Normal + AO) via Blender
    Paso 4: Genera mallas LOD0-3 desde footprints reales via Blender
    Paso 5: Genera 12 arquetipos vascos base via Blender

    Resultado: 1140 edificios con texturas PBR + 4 niveles LOD listos para Unity HDRP

.NOTES
    Requisitos:
      - Python 3.10+ en PATH
      - pip install selenium
      - Chrome + chromedriver en PATH o en E:\chromedriver\
      - Blender 5.1 en "C:\Program Files\Blender Foundation\Blender 5.1\blender.exe"

    Opcional:
      - Real-ESRGAN Vulkan en E:\realesrgan-ncnn-vulkan\ (upscale 4K)
      - Token Mapillary gratis (alternativa a Selenium)

.EXAMPLE
    .\Tools\run_everything.ps1
    .\Tools\run_everything.ps1 -SkipStreetView     # si ya tienes capturas
    .\Tools\run_everything.ps1 -OnlyTextures        # solo texturas, sin mallas
    .\Tools\run_everything.ps1 -Limit 50            # solo 50 edificios (test)
#>

param(
    [switch]$SkipStreetView,
    [switch]$SkipSynthetic,
    [switch]$SkipBlenderTextures,
    [switch]$SkipBlenderMeshes,
    [switch]$SkipArchetypes,
    [switch]$OnlyTextures,
    [int]$Limit = 0,
    [string]$MapillaryToken = "",
    [switch]$Visible
)

$ErrorActionPreference = "Continue"
$ProjectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$ToolsDir = Join-Path $ProjectRoot "Tools"

# Buscar Blender
$BlenderPaths = @(
    "C:\Program Files\Blender Foundation\Blender 5.1\blender.exe",
    "C:\Program Files\Blender Foundation\Blender 4.4\blender.exe",
    "C:\Program Files\Blender Foundation\Blender 4.3\blender.exe",
    "C:\Program Files\Blender Foundation\Blender 4.2\blender.exe",
    "C:\Program Files\Blender Foundation\Blender 4.1\blender.exe",
    "C:\Program Files\Blender Foundation\Blender 4.0\blender.exe",
    "C:\Program Files\Blender Foundation\Blender 3.6\blender.exe"
)
$Blender = $null
foreach ($bp in $BlenderPaths) {
    if (Test-Path $bp) { $Blender = $bp; break }
}
if (-not $Blender) {
    $Blender = (Get-Command blender -ErrorAction SilentlyContinue).Source
}

Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "  ALTSASU MANIFA — PIPELINE AAA COMPLETO" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Proyecto: $ProjectRoot"
Write-Host "  Blender:  $(if($Blender){"$Blender"}else{'NO ENCONTRADO'})"
Write-Host "  Python:   $(python --version 2>&1)"
Write-Host ""

$startTime = Get-Date
$stepResults = @{}

function Write-Step($num, $desc) {
    Write-Host ""
    Write-Host "────────────────────────────────────────────────────────────" -ForegroundColor Yellow
    Write-Host "  PASO $num: $desc" -ForegroundColor Yellow
    Write-Host "────────────────────────────────────────────────────────────" -ForegroundColor Yellow
    Write-Host ""
}

function Write-Result($step, $ok, $msg) {
    $stepResults[$step] = @{ ok = $ok; msg = $msg }
    if ($ok) {
        Write-Host "  [OK] $msg" -ForegroundColor Green
    } else {
        Write-Host "  [!!] $msg" -ForegroundColor Red
    }
}

# ══════════════════════════════════════════════════════════════════════════════
# PASO 1: Captura Street View (gratis, Selenium)
# ══════════════════════════════════════════════════════════════════════════════

if (-not $SkipStreetView -and -not $OnlyTextures) {
    Write-Step 1 "Captura Street View (Selenium + Chrome headless)"

    # Primero generar plan de vuelo si no existe
    $waypointsFile = Join-Path $ProjectRoot "Assets\AlsasuaData\drone_waypoints.json"
    if (-not (Test-Path $waypointsFile)) {
        Write-Host "  Generando plan de vuelo..."
        python (Join-Path $ToolsDir "drone_streetview_capture.py") --plan-only
    }

    $svArgs = @()
    if ($Limit -gt 0) { $svArgs += "--limit", $Limit }
    if ($Visible) { $svArgs += "--visible" }
    if ($MapillaryToken) {
        Write-Host "  Usando Mapillary (token proporcionado)..."
        python (Join-Path $ToolsDir "drone_streetview_capture.py") --mapillary-token $MapillaryToken
    } else {
        python (Join-Path $ToolsDir "drone_streetview_browser.py") @svArgs
    }

    $svCount = (Get-ChildItem (Join-Path $ProjectRoot "Assets\AlsasuaData\FacadeTextures\StreetView_Auto\*.png") -ErrorAction SilentlyContinue).Count
    Write-Result 1 ($svCount -gt 0) "Street View: $svCount capturas"
} else {
    Write-Result 1 $true "Street View: SALTADO"
}

# ══════════════════════════════════════════════════════════════════════════════
# PASO 2: Texturas sinteticas para edificios sin foto
# ══════════════════════════════════════════════════════════════════════════════

if (-not $SkipSynthetic) {
    Write-Step 2 "Texturas sinteticas (virtual_streetview_walker.py)"

    python (Join-Path $ToolsDir "virtual_streetview_walker.py")

    $aaaCount = (Get-ChildItem (Join-Path $ProjectRoot "Assets\AlsasuaData\FacadeTextures\AAA\*_albedo.png") -ErrorAction SilentlyContinue).Count
    Write-Result 2 ($aaaCount -gt 0) "Texturas AAA: $aaaCount edificios con albedo"
} else {
    Write-Result 2 $true "Texturas sinteticas: SALTADO"
}

# ══════════════════════════════════════════════════════════════════════════════
# PASO 3: Procesar texturas PBR con Blender (Retinex + Normal + AO)
# ══════════════════════════════════════════════════════════════════════════════

if (-not $SkipBlenderTextures -and $Blender) {
    Write-Step 3 "Texturas PBR via Blender (Retinex + Normal + AO)"

    $texArgs = @("--background", "--python", (Join-Path $ToolsDir "blender_apply_facade_photos.py"), "--")
    $texArgs += "--textures-only", "--all"

    & $Blender @texArgs

    Write-Result 3 $true "Texturas PBR procesadas"
} elseif (-not $Blender) {
    # Sin Blender: modo solo-Python
    Write-Step 3 "Texturas PBR via Python puro (sin Blender)"
    python (Join-Path $ToolsDir "blender_apply_facade_photos.py") --textures-only --all
    Write-Result 3 $true "Texturas PBR procesadas (Python puro)"
} else {
    Write-Result 3 $true "Texturas PBR: SALTADO"
}

# ══════════════════════════════════════════════════════════════════════════════
# PASO 4: Generar mallas LOD0-3 desde footprints reales
# ══════════════════════════════════════════════════════════════════════════════

if (-not $SkipBlenderMeshes -and -not $OnlyTextures -and $Blender) {
    Write-Step 4 "Mallas LOD0-3 via Blender (blender_lod_pipeline.py)"

    $meshArgs = @(
        "--background", "--python", (Join-Path $ToolsDir "blender_lod_pipeline.py"), "--",
        "--input", (Join-Path $ProjectRoot "Assets\AlsasuaData\buildings_fusion_final.json"),
        "--output", (Join-Path $ProjectRoot "Assets\Models\Buildings_Generated"),
        "--textures", (Join-Path $ProjectRoot "Assets\AlsasuaData\FacadeTextures\AAA")
    )
    if ($Limit -gt 0) { $meshArgs += "--max_buildings", $Limit }

    & $Blender @meshArgs

    $fbxCount = (Get-ChildItem (Join-Path $ProjectRoot "Assets\Models\Buildings_Generated\*.fbx") -ErrorAction SilentlyContinue).Count
    Write-Result 4 ($fbxCount -gt 0) "Mallas generadas: $fbxCount FBX"
} elseif (-not $Blender) {
    Write-Result 4 $false "Blender NO encontrado — no se pueden generar mallas"
} else {
    Write-Result 4 $true "Mallas: SALTADO"
}

# ══════════════════════════════════════════════════════════════════════════════
# PASO 5: Generar 12 arquetipos vascos base
# ══════════════════════════════════════════════════════════════════════════════

if (-not $SkipArchetypes -and -not $OnlyTextures -and $Blender) {
    Write-Step 5 "12 arquetipos vascos (blender_archetypes.py)"

    & $Blender --background --python (Join-Path $ToolsDir "blender_archetypes.py")

    $archCount = (Get-ChildItem (Join-Path $ProjectRoot "Assets\Models\Archetypes_Maya\*.fbx") -ErrorAction SilentlyContinue).Count
    Write-Result 5 ($archCount -gt 0) "Arquetipos: $archCount FBX"
} elseif (-not $Blender) {
    Write-Result 5 $false "Blender NO encontrado"
} else {
    Write-Result 5 $true "Arquetipos: SALTADO"
}

# ══════════════════════════════════════════════════════════════════════════════
# RESUMEN
# ══════════════════════════════════════════════════════════════════════════════

$elapsed = (Get-Date) - $startTime

Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "  RESUMEN PIPELINE" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host ""

foreach ($step in ($stepResults.Keys | Sort-Object)) {
    $r = $stepResults[$step]
    $icon = if ($r.ok) { "[OK]" } else { "[!!]" }
    $color = if ($r.ok) { "Green" } else { "Red" }
    Write-Host "  Paso $step $icon $($r.msg)" -ForegroundColor $color
}

Write-Host ""
Write-Host "  Tiempo total: $($elapsed.ToString('hh\:mm\:ss'))" -ForegroundColor White

# Contar resultados finales
$finalTextures = (Get-ChildItem (Join-Path $ProjectRoot "Assets\AlsasuaData\FacadeTextures\AAA\*_albedo.png") -ErrorAction SilentlyContinue).Count
$finalFBX = (Get-ChildItem (Join-Path $ProjectRoot "Assets\Models\Buildings_Generated\*.fbx") -ErrorAction SilentlyContinue).Count

Write-Host ""
Write-Host "  Texturas PBR:  $finalTextures edificios" -ForegroundColor White
Write-Host "  Mallas FBX:    $finalFBX edificios" -ForegroundColor White
Write-Host "  Total edificios en fusion: 1140" -ForegroundColor DarkGray
Write-Host ""

if ($finalTextures -gt 0 -or $finalFBX -gt 0) {
    Write-Host "  Siguiente paso: abre Unity y pulsa Play." -ForegroundColor Green
    Write-Host "  AplicadorTexturasReales.cs cargara las texturas automaticamente." -ForegroundColor Green
} else {
    Write-Host "  Nada generado. Revisa los errores arriba." -ForegroundColor Red
}

Write-Host ""
