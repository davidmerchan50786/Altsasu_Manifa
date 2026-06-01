<#
.SYNOPSIS
    Pipeline completo fotogrametría Alsasua Manifa  (v2 — AMD OpenCL, COLMAP, Gaussian Splatting)
    Meshroom → Blender cleanup → Unity import

.DESCRIPTION
    Ejecutar desde E:\DAM\Altsasu_Manifa como administrador.
    Requiere: Meshroom 2025.1.0, Blender 5.1, Python 3.x

    Flujo completo:
      0. Detección GPU AMD y activación OpenCL/ROCm/HIP
      1. Verificar herramientas instaladas
      2. Crear directorios de caché en E:\MeshroomCache\
      3. Copiar fotos procesadas al input del pipeline
      4. [Opcional] Pre-procesado avanzado de fotos (preprocess_photos_advanced.py)
      5. [Opcional] Gaussian Splatting para edificios hero (gaussian_splatting_heroes.py)
      6. Ejecutar meshroom_pipeline.py (SfM + MVS + Texturing + Blender cleanup)
      7. Ejecutar unity_photogrammetry_importer.py (actualizar buildings_fusion_final.json)
      8. Mostrar resumen: edificios procesados, tiempo, espacio usado
      9. Opción de hacer git add + commit de los FBX generados
     10. [Opcional] Abrir Unity con el proyecto

    Caché en E:\MeshroomCache\ (NO en C:\) — requiere ~100 GB libres por zona.

.PARAMETER Zona
    Nombre de la zona a procesar (iglesia, ayto, plaza_fueros, casco_viejo, etc.)
    Si se omite con -All, procesa todas las zonas.

.PARAMETER All
    Procesa todas las zonas en orden de prioridad:
    iglesia → ayto → plaza_fueros → casco_viejo → gaztetxe → plaza_zubeztia → ferial → garcia_jimenez

.PARAMETER GPU
    Activa aceleración GPU AMD:
    - Meshroom DepthMap con OpenCL (ALICEVISION_OPENCL_PLATFORM=0)
    - ROCm/HIP si disponible (RX 6000+)
    - Blender Cycles con HIP AMD (fallback a OpenCL)

.PARAMETER Force
    Reprocesa edificios aunque ya estén marcados como completados.

.PARAMETER Retry
    Procesa solo edificios con estado "failed" en progress.json.

.PARAMETER SkipGit
    No pregunta sobre git commit al final.

.PARAMETER UseColmap
    Usa COLMAP + OpenMVS en vez de Meshroom (mayor calidad MVS en CPU AMD).
    No requiere GPU — usa CPU multihilo con patch-match stereo.

.PARAMETER UseGaussianSplatting
    Ejecuta Gaussian Splatting (nerfstudio splatfacto) para edificios hero
    antes del pipeline principal. Requiere GPU con ROCm o NVIDIA CUDA.

.PARAMETER PreprocessPhotos
    Ejecuta preprocess_photos_advanced.py antes del pipeline principal.
    Aplica CLAHE, denoising, sharpening y normalización de exposición.

.PARAMETER OpenUnity
    Abre Unity Hub con el proyecto al final del pipeline (si Unity está instalado).

.EXAMPLE
    .\Tools\run_full_photogrammetry_pipeline.ps1 -Zona iglesia
    .\Tools\run_full_photogrammetry_pipeline.ps1 -Zona iglesia -GPU
    .\Tools\run_full_photogrammetry_pipeline.ps1 -All
    .\Tools\run_full_photogrammetry_pipeline.ps1 -All -GPU
    .\Tools\run_full_photogrammetry_pipeline.ps1 -All -GPU -Force
    .\Tools\run_full_photogrammetry_pipeline.ps1 -All -Retry
    .\Tools\run_full_photogrammetry_pipeline.ps1 -All -GPU -UseColmap
    .\Tools\run_full_photogrammetry_pipeline.ps1 -All -GPU -UseGaussianSplatting -PreprocessPhotos
    .\Tools\run_full_photogrammetry_pipeline.ps1 -Zona iglesia -UseGaussianSplatting -OpenUnity
#>

param(
    [string]$Zona                  = "",
    [switch]$All,
    [switch]$GPU,
    [switch]$Force,
    [switch]$Retry,
    [switch]$SkipGit,
    [switch]$UseColmap,
    [switch]$UseGaussianSplatting,
    [switch]$PreprocessPhotos,
    [switch]$OpenUnity
)

# ─── CONFIGURACIÓN ────────────────────────────────────────────────────────────

$MeshroomBatch = "E:\Meshroom\Meshroom-2025.1.0\meshroom_batch.exe"
$BlenderExe    = "C:\Program Files\Blender Foundation\Blender 5.1\blender.exe"
$CacheRoot     = "E:\MeshroomCache"
$ProjectRoot   = $PSScriptRoot | Split-Path -Parent   # Tools/ → raíz del proyecto

$ProcessedDir  = Join-Path $ProjectRoot "Assets\AlsasuaData\FacadeTextures\Processed"
$FbxOutDir     = Join-Path $ProjectRoot "Assets\Models\Buildings_Photogrammetry"
$ReportJson    = Join-Path $ProjectRoot "Assets\AlsasuaData\photogrammetry_report.json"
$ProgressJson  = Join-Path $CacheRoot "progress.json"

$MeshroomScript    = Join-Path $PSScriptRoot "meshroom_pipeline.py"
$ImportScript      = Join-Path $PSScriptRoot "unity_photogrammetry_importer.py"
$PreprocessScript  = Join-Path $PSScriptRoot "preprocess_photos_advanced.py"
$SplatScript       = Join-Path $PSScriptRoot "gaussian_splatting_heroes.py"

# Rutas opcionales para COLMAP, OpenMVS y Unity Hub
$ColmapExe    = "E:\COLMAP\COLMAP.bat"
$OpenMvsDir   = "E:\OpenMVS\bin"
$UnityHubExe  = "C:\Program Files\Unity Hub\Unity Hub.exe"
$UnityProject = $ProjectRoot   # el proyecto Unity es la raíz

# Colores para output
$C_OK    = "Green"
$C_WARN  = "Yellow"
$C_ERR   = "Red"
$C_INFO  = "Cyan"
$C_HEAD  = "Magenta"

# ─── FUNCIONES ───────────────────────────────────────────────────────────────

function Write-Header {
    param([string]$Title)
    $line = "=" * 70
    Write-Host ""
    Write-Host $line -ForegroundColor $C_HEAD
    Write-Host "  $Title" -ForegroundColor $C_HEAD
    Write-Host $line -ForegroundColor $C_HEAD
}

function Write-Step {
    param([int]$N, [string]$Msg)
    Write-Host ""
    Write-Host "  [$N] $Msg" -ForegroundColor $C_INFO
}

function Write-OK   { param([string]$M) Write-Host "    [+] $M" -ForegroundColor $C_OK   }
function Write-Warn { param([string]$M) Write-Host "    [!] $M" -ForegroundColor $C_WARN }
function Write-Err  { param([string]$M) Write-Host "    [x] $M" -ForegroundColor $C_ERR  }
function Write-Info { param([string]$M) Write-Host "    [·] $M" -ForegroundColor White    }

function Get-DiskSpaceGB {
    param([string]$Path)
    try {
        $drive = Split-Path -Qualifier $Path
        $disk  = Get-PSDrive -Name $drive.TrimEnd(':') -ErrorAction Stop
        return [math]::Round($disk.Free / 1GB, 1)
    } catch {
        return -1
    }
}

function Get-FolderSizeMB {
    param([string]$Path)
    if (-not (Test-Path $Path)) { return 0 }
    $size = (Get-ChildItem -Path $Path -Recurse -File -ErrorAction SilentlyContinue |
             Measure-Object -Property Length -Sum).Sum
    return [math]::Round($size / 1MB, 1)
}

function Find-Python {
    $candidates = @("python", "python3", "py")
    foreach ($cmd in $candidates) {
        try {
            $v = & $cmd --version 2>&1
            if ($v -match "Python 3") { return $cmd }
        } catch {}
    }
    return $null
}

function Detect-AmdGpu {
    <#
    .SYNOPSIS Detecta GPU AMD y devuelve info de ROCm/OpenCL disponible. #>
    $result = @{
        Found    = $false
        Name     = ""
        HasROCm  = $false
        HasHIP   = $false
        HasOCL   = $false
    }
    try {
        $gpus = Get-WmiObject Win32_VideoController -ErrorAction Stop |
                Where-Object { $_.Name -match "AMD|Radeon|RX|Vega|Navi|RDNA" }
        if ($gpus) {
            $result.Found = $true
            $result.Name  = ($gpus | Select-Object -First 1).Name

            # Detectar ROCm (HIP) — presente si existe el directorio
            $rocmPaths = @("C:\Program Files\AMD\ROCm", "C:\rocm", "C:\ROCm")
            foreach ($p in $rocmPaths) {
                if (Test-Path $p) { $result.HasROCm = $true; $result.HasHIP = $true; break }
            }

            # OpenCL siempre disponible en AMD con drivers Adrenalin
            $result.HasOCL = $true
        }
    } catch {
        # Sin WMI (no es crítico)
    }
    return $result
}

function Show-ProgressBar {
    <#
    .SYNOPSIS Muestra barra de progreso leyendo progress.json cada intervalo.
    .PARAMETER ProgressFile Ruta al progress.json.
    .PARAMETER IntervalSec  Intervalo de refresco en segundos (default: 10).
    .PARAMETER TotalBuildings Total de edificios esperados.
    #>
    param(
        [string]$ProgressFile,
        [int]$IntervalSec     = 10,
        [int]$TotalBuildings  = 0
    )
    if (-not (Test-Path $ProgressFile)) { return }
    try {
        $data     = Get-Content $ProgressFile -Raw -ErrorAction Stop | ConvertFrom-Json
        $done     = ($data.PSObject.Properties | Where-Object { $_.Value.status -eq "done" }).Count
        $failed   = ($data.PSObject.Properties | Where-Object { $_.Value.status -eq "failed" }).Count
        $proc     = ($data.PSObject.Properties | Where-Object { $_.Value.status -eq "processing" }).Count
        $total    = if ($TotalBuildings -gt 0) { $TotalBuildings } else { $done + $failed + $proc }

        if ($total -gt 0) {
            $pct   = [math]::Round(($done + $failed) / $total * 100, 0)
            $bar   = "#" * [math]::Floor($pct / 5)
            $empty = "-" * (20 - [math]::Floor($pct / 5))
            Write-Host "    [$bar$empty] $pct%  (done=$done  failed=$failed  proc=$proc / $total)" `
                       -ForegroundColor $C_INFO
        }
    } catch {
        # progress.json vacío o bloqueado (Meshroom escribiendo)
    }
}

function Get-ETA {
    <#
    .SYNOPSIS Calcula tiempo estimado restante basado en tiempo por edificio.
    #>
    param(
        [datetime]$StartTime,
        [string]$ProgressFile,
        [int]$TotalBuildings
    )
    if (-not (Test-Path $ProgressFile) -or $TotalBuildings -le 0) { return "N/A" }
    try {
        $data  = Get-Content $ProgressFile -Raw -ErrorAction Stop | ConvertFrom-Json
        $done  = ($data.PSObject.Properties | Where-Object { $_.Value.status -eq "done" }).Count
        $failed = ($data.PSObject.Properties | Where-Object { $_.Value.status -eq "failed" }).Count
        $nDone = $done + $failed

        if ($nDone -le 0) { return "calculando..." }

        $elapsed  = (Get-Date) - $StartTime
        $secPerB  = $elapsed.TotalSeconds / $nDone
        $remaining = ($TotalBuildings - $nDone) * $secPerB
        $etaMins  = [math]::Round($remaining / 60, 0)
        return "${etaMins} min restantes (~$([math]::Round($secPerB/60,1)) min/edificio)"
    } catch {
        return "N/A"
    }
}

# ─── CABECERA ─────────────────────────────────────────────────────────────────

Write-Header "PIPELINE FOTOGRAMETRÍA v2 — Altsasu Manifa (AMD OpenCL)"
Write-Info "Fecha          : $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
Write-Info "Modo           : $(if ($All) {'TODAS LAS ZONAS'} elseif ($Zona) {$Zona} else {'(ninguno)'})"
Write-Info "GPU AMD        : $(if ($GPU) {'SÍ — OpenCL/ROCm/HIP'} else {'NO (CPU mode)'})"
Write-Info "Motor          : $(if ($UseColmap) {'COLMAP + OpenMVS'} else {'Meshroom'})"
Write-Info "Gauss Splat    : $(if ($UseGaussianSplatting) {'SÍ (edificios hero)'} else {'NO'})"
Write-Info "Preprocesar    : $(if ($PreprocessPhotos) {'SÍ (CLAHE+denoise+sharpen)'} else {'NO'})"
Write-Info "Force          : $(if ($Force) {'SÍ'} else {'NO'})"
Write-Info "Retry          : $(if ($Retry) {'SÍ'} else {'NO'})"
Write-Info "Proyecto       : $ProjectRoot"

# ─── PASO 0: DETECCIÓN GPU AMD ───────────────────────────────────────────────

Write-Step 0 "Detección GPU AMD"

$amdGpu = Detect-AmdGpu
if ($amdGpu.Found) {
    Write-OK "GPU AMD detectada: $($amdGpu.Name)"
    if ($amdGpu.HasROCm) {
        Write-OK "ROCm/HIP disponible — aceleración máxima en Cycles y nerfstudio"
    } elseif ($amdGpu.HasOCL) {
        Write-OK "OpenCL disponible — DepthMap Meshroom acelerado (ALICEVISION_OPENCL_PLATFORM=0)"
        Write-Info "ROCm no detectado (opcional para RX 6000+): https://rocm.docs.amd.com"
    }

    if ($GPU) {
        # Configurar variables de entorno AMD para sesión actual
        [System.Environment]::SetEnvironmentVariable("ALICEVISION_GPU_MEMORY_LIMIT", "0")
        [System.Environment]::SetEnvironmentVariable("ALICEVISION_OPENCL_PLATFORM", "0")
        [System.Environment]::SetEnvironmentVariable("HIP_VISIBLE_DEVICES",          "0")
        [System.Environment]::SetEnvironmentVariable("GPU_MAX_HEAP_SIZE",             "100")
        [System.Environment]::SetEnvironmentVariable("GPU_MAX_ALLOC_PERCENT",         "100")
        Write-OK "Variables AMD OpenCL configuradas para esta sesión:"
        Write-Info "  ALICEVISION_GPU_MEMORY_LIMIT=0 (sin límite VRAM)"
        Write-Info "  ALICEVISION_OPENCL_PLATFORM=0  (primera plataforma = AMD)"
        Write-Info "  HIP_VISIBLE_DEVICES=0           (ROCm GPU 0)"
        Write-Info "  GPU_MAX_HEAP_SIZE=100           (heap OpenCL 100%)"
    }
} else {
    Write-Warn "No se detectó GPU AMD. Usando CPU."
    Write-Info "Si tienes GPU AMD y no se detecta, verifica drivers Adrenalin."
    if ($GPU) {
        Write-Warn "-GPU ignorado sin GPU AMD detectada → modo CPU"
    }
}

# ─── VALIDAR PARÁMETROS ───────────────────────────────────────────────────────

if (-not $All -and -not $Zona -and -not $Retry) {
    Write-Err "Debes especificar -All, -Zona <nombre> o -Retry."
    Write-Host ""
    Write-Host "  Uso:" -ForegroundColor $C_INFO
    Write-Host "    .\Tools\run_full_photogrammetry_pipeline.ps1 -All" -ForegroundColor White
    Write-Host "    .\Tools\run_full_photogrammetry_pipeline.ps1 -All -GPU" -ForegroundColor White
    Write-Host "    .\Tools\run_full_photogrammetry_pipeline.ps1 -Zona iglesia" -ForegroundColor White
    exit 1
}

# ─── PASO 1: VERIFICAR HERRAMIENTAS ──────────────────────────────────────────

Write-Step 1 "Verificando herramientas instaladas"

$toolsOk = $true

# Meshroom (o COLMAP)
if ($UseColmap) {
    if (Test-Path $ColmapExe) {
        Write-OK "COLMAP encontrado: $ColmapExe"
    } else {
        Write-Warn "COLMAP no encontrado: $ColmapExe"
        Write-Warn "  Descarga: https://colmap.github.io/install.html"
        $toolsOk = $false
    }
    if (Test-Path $OpenMvsDir) {
        Write-OK "OpenMVS encontrado: $OpenMvsDir"
    } else {
        Write-Warn "OpenMVS no encontrado: $OpenMvsDir"
        Write-Warn "  Descarga: https://github.com/cdcseacave/openMVS/releases"
    }
} else {
    if (Test-Path $MeshroomBatch) {
        Write-OK "Meshroom 2025.1.0: $MeshroomBatch"
    } else {
        Write-Warn "Meshroom no encontrado: $MeshroomBatch"
        Write-Warn "  Descarga: https://alicevision.org/#meshroom"
        $toolsOk = $false
    }
}

# Blender
if (Test-Path $BlenderExe) {
    Write-OK "Blender 5.1: $BlenderExe"
} else {
    Write-Warn "Blender no encontrado: $BlenderExe"
    Write-Warn "  Descarga: https://www.blender.org/download/"
    $toolsOk = $false
}

# Python
$pythonCmd = Find-Python
if ($pythonCmd) {
    $pyVer = & $pythonCmd --version 2>&1
    Write-OK "Python: $pyVer ($pythonCmd)"
} else {
    Write-Err "Python 3 no encontrado en PATH."
    Write-Err "  Descarga: https://www.python.org/downloads/"
    exit 1
}

# Scripts del proyecto
if (Test-Path $MeshroomScript) {
    Write-OK "meshroom_pipeline.py encontrado"
} else {
    Write-Err "meshroom_pipeline.py no encontrado: $MeshroomScript"
    exit 1
}

if (Test-Path $ImportScript) {
    Write-OK "unity_photogrammetry_importer.py encontrado"
} else {
    Write-Err "unity_photogrammetry_importer.py no encontrado: $ImportScript"
    exit 1
}

# Fotos procesadas
if (Test-Path $ProcessedDir) {
    $nFotos = (Get-ChildItem -Path $ProcessedDir -Filter "*.png").Count
    Write-OK "Fotos procesadas: $nFotos en $ProcessedDir"
} else {
    Write-Err "Directorio de fotos no encontrado: $ProcessedDir"
    Write-Err "  Ejecuta primero: python Tools\process_streetview_real.py"
    exit 1
}

if (-not $toolsOk) {
    Write-Warn ""
    Write-Warn "AVISO: Herramientas faltantes. El pipeline se ejecutará en modo simulado."
    Write-Warn "       Instala Meshroom y/o Blender para reconstrucción real."
    Write-Host ""
    $confirm = Read-Host "  ¿Continuar de todos modos? [s/N]"
    if ($confirm -notmatch "^[sS]") { exit 0 }
}

# ─── PASO 2: CREAR DIRECTORIOS DE CACHÉ ──────────────────────────────────────

Write-Step 2 "Preparando directorios de caché"

# Verificar espacio en E:\
$freeGB = Get-DiskSpaceGB "E:\"
if ($freeGB -ge 0) {
    if ($freeGB -lt 50) {
        Write-Warn "Espacio libre en E:\: $freeGB GB (recomendado: >100 GB)"
    } else {
        Write-OK "Espacio libre en E:\: $freeGB GB"
    }
}

$cacheDirs = @(
    $CacheRoot,
    (Join-Path $CacheRoot "input"),
    (Join-Path $CacheRoot "cache"),
    (Join-Path $CacheRoot "output")
)

foreach ($d in $cacheDirs) {
    if (-not (Test-Path $d)) {
        New-Item -ItemType Directory -Path $d -Force | Out-Null
        Write-Info "Creado: $d"
    } else {
        Write-Info "Ya existe: $d"
    }
}

# Crear directorios de salida Unity
$unityDirs = @($FbxOutDir, (Join-Path $ProjectRoot "Assets\AlsasuaData\FacadeTextures\Photogrammetry"))
foreach ($d in $unityDirs) {
    if (-not (Test-Path $d)) {
        New-Item -ItemType Directory -Path $d -Force | Out-Null
        Write-OK "Creado: $d"
    }
}

# ─── PASO 3: COPIAR FOTOS AL INPUT ───────────────────────────────────────────

Write-Step 3 "Verificando fotos en input"
$inputDir = Join-Path $CacheRoot "input"

# Las fotos se copian individualmente por edificio desde meshroom_pipeline.py
# Aquí solo verificamos que el origen existe
if (Test-Path $ProcessedDir) {
    $nFotos = (Get-ChildItem -Path $ProcessedDir -Filter "*.png" -Recurse).Count
    Write-OK "$nFotos fotos disponibles en Processed/"
    Write-Info "meshroom_pipeline.py las copiará por edificio a $inputDir\{id}\images\"
} else {
    Write-Err "Directorio Processed/ no encontrado"
    exit 1
}

# ─── PASO 3b: PRE-PROCESADO AVANZADO DE FOTOS (OPCIONAL) ─────────────────────

if ($PreprocessPhotos) {
    Write-Step 3 "Pre-procesado avanzado de fotos (CLAHE + denoising + sharpening)"

    if (-not (Test-Path $PreprocessScript)) {
        Write-Warn "preprocess_photos_advanced.py no encontrado: $PreprocessScript"
    } else {
        $prepArgs = @($PreprocessScript)
        Write-Info "Comando: $pythonCmd $($prepArgs -join ' ')"
        Write-Info "Aplicando: CLAHE LAB, fastNlMeans denoising, unsharp mask, RANSAC perspectiva..."

        $tPrep = Get-Date
        $proc0 = Start-Process -FilePath $pythonCmd `
                               -ArgumentList $prepArgs `
                               -NoNewWindow -PassThru -Wait `
                               -WorkingDirectory $ProjectRoot

        $elPrep = [math]::Round(((Get-Date) - $tPrep).TotalMinutes, 1)
        if ($proc0.ExitCode -eq 0) {
            Write-OK "Pre-procesado completado en $elPrep min"
            Write-Info "Fotos mejoradas en: Assets\AlsasuaData\FacadeTextures\Processed_Enhanced\"
        } else {
            Write-Warn "Pre-procesado terminó con código $($proc0.ExitCode) — continuando"
        }
    }
}

# ─── PASO 3c: GAUSSIAN SPLATTING EDIFICIOS HERO (OPCIONAL) ───────────────────

if ($UseGaussianSplatting) {
    Write-Step 3 "Gaussian Splatting para edificios hero (iglesia, ayto, plaza_fueros)"

    if (-not (Test-Path $SplatScript)) {
        Write-Warn "gaussian_splatting_heroes.py no encontrado: $SplatScript"
    } else {
        $splatArgs = @($SplatScript, "--all")
        if ($Force) { $splatArgs += "--force" }
        if ($UseColmap) { $splatArgs += "--colmap-only" }

        Write-Info "Comando: $pythonCmd $($splatArgs -join ' ')"
        Write-Info "Procesando: iglesia, ayto, plaza_fueros_1..5"
        if (-not $UseColmap) {
            Write-Info "Motor: nerfstudio splatfacto (AMD ROCm) + COLMAP+OpenMVS fallback"
        } else {
            Write-Info "Motor: COLMAP + OpenMVS (CPU, sin nerfstudio)"
        }
        Write-Host ""

        $tSplat = Get-Date
        $procS  = Start-Process -FilePath $pythonCmd `
                                -ArgumentList $splatArgs `
                                -NoNewWindow -PassThru -Wait `
                                -WorkingDirectory $ProjectRoot

        $elSplat = [math]::Round(((Get-Date) - $tSplat).TotalMinutes, 1)
        if ($procS.ExitCode -eq 0) {
            Write-OK "Gaussian Splatting heroes completado en $elSplat min"
        } else {
            Write-Warn "Gaussian Splatting terminó con código $($procS.ExitCode) — continuando"
        }
    }
}

# ─── PASO 4: EJECUTAR MESHROOM / COLMAP PIPELINE ─────────────────────────────

Write-Step 4 "Ejecutando pipeline $(if ($UseColmap) {'COLMAP + OpenMVS'} else {'Meshroom'})"

$tStart = Get-Date

# Construir argumentos
$pyArgs = @($MeshroomScript)
if ($All)             { $pyArgs += "--all"            }
if ($Zona)            { $pyArgs += @("--zona", $Zona) }
if ($GPU)             { $pyArgs += "--gpu"             }
if ($Force)           { $pyArgs += "--force"           }
if ($Retry)           { $pyArgs += "--retry"           }
if ($UseColmap)       { $pyArgs += "--colmap"          }
if ($PreprocessPhotos){ $pyArgs += "--enhanced-input"  }

Write-Info "Comando: $pythonCmd $($pyArgs -join ' ')"
if ($GPU -and $amdGpu.Found) {
    Write-Info "GPU AMD: OpenCL activado (ALICEVISION_OPENCL_PLATFORM=0)"
}
Write-Info "Esto puede tardar varias horas. No cierres esta ventana."
Write-Host ""

# Estimar total de edificios para barra de progreso
$totalBuildings = 0
if (Test-Path $ProcessedDir) {
    $nFotos = (Get-ChildItem -Path $ProcessedDir -Filter "*.png").Count
    $totalBuildings = [math]::Max(1, [math]::Ceiling($nFotos / 4))   # ~4 fotos/edificio
    Write-Info "Edificios estimados: ~$totalBuildings (basado en $nFotos fotos)"
}

# Lanzar pipeline en background para monitorizar progreso
$proc = Start-Process -FilePath $pythonCmd `
                      -ArgumentList $pyArgs `
                      -NoNewWindow -PassThru `
                      -WorkingDirectory $ProjectRoot

# Monitorizar progreso cada 10 segundos mientras corre
Write-Host ""
Write-Info "Monitorizando progreso (progress.json, intervalo 10s)..."
Write-Host ""

while (-not $proc.HasExited) {
    Show-ProgressBar -ProgressFile $ProgressJson -IntervalSec 10 -TotalBuildings $totalBuildings
    $eta = Get-ETA -StartTime $tStart -ProgressFile $ProgressJson -TotalBuildings $totalBuildings
    Write-Host "    ETA: $eta  |  Elapsed: $([math]::Round(((Get-Date) - $tStart).TotalMinutes, 1)) min" `
               -ForegroundColor DarkGray
    Start-Sleep -Seconds 10
}

# Esperar resultado final
$proc.WaitForExit()
$meshroomExitCode = $proc.ExitCode
$tMeshroom = (Get-Date) - $tStart

# Barra final
Show-ProgressBar -ProgressFile $ProgressJson -TotalBuildings $totalBuildings

if ($meshroomExitCode -eq 0) {
    Write-OK "Pipeline $(if ($UseColmap) {'COLMAP+OpenMVS'} else {'Meshroom'}) completado en $([math]::Round($tMeshroom.TotalMinutes, 1)) min"
} else {
    Write-Err "Pipeline terminó con error (código: $meshroomExitCode)"
    Write-Err "Revisa los logs arriba para más detalles."
    Write-Warn "Continuando con unity_photogrammetry_importer.py..."
}

# ─── PASO 5: ACTUALIZAR UNITY PROJECT ────────────────────────────────────────

Write-Step 5 "Actualizando buildings_fusion_final.json (Unity import)"

$importArgs = @($ImportScript)
Write-Info "Comando: $pythonCmd $($importArgs -join ' ')"

$proc2 = Start-Process -FilePath $pythonCmd `
                       -ArgumentList $importArgs `
                       -NoNewWindow -PassThru -Wait `
                       -WorkingDirectory $ProjectRoot

if ($proc2.ExitCode -eq 0) {
    Write-OK "Unity importer completado"
} else {
    Write-Warn "Unity importer terminó con advertencias (código: $($proc2.ExitCode))"
}

# ─── PASO 6: RESUMEN ─────────────────────────────────────────────────────────

Write-Step 6 "Resumen"

$tTotal = (Get-Date) - $tStart

# Contar FBX generados
$nFbx = 0
$fbxSizeMB = 0
if (Test-Path $FbxOutDir) {
    $fbxFiles = Get-ChildItem -Path $FbxOutDir -Filter "*.fbx"
    $nFbx = $fbxFiles.Count
    $fbxSizeMB = Get-FolderSizeMB $FbxOutDir
}

# Leer reporte JSON si existe
$reportData = $null
if (Test-Path $ReportJson) {
    try {
        $reportData = Get-Content $ReportJson -Raw | ConvertFrom-Json
    } catch {}
}

Write-Host ""
Write-Host ("  " + ("=" * 68)) -ForegroundColor $C_HEAD
Write-Host "  RESUMEN FINAL" -ForegroundColor $C_HEAD
Write-Host ("  " + ("=" * 68)) -ForegroundColor $C_HEAD

Write-Host "    Tiempo total        : $([math]::Round($tTotal.TotalMinutes, 1)) min" -ForegroundColor White
Write-Host "    FBX generados       : $nFbx" -ForegroundColor White
Write-Host "    Tamaño FBX total    : $fbxSizeMB MB" -ForegroundColor White
Write-Host "    Caché usado         : $(Get-FolderSizeMB $CacheRoot) MB en $CacheRoot" -ForegroundColor White

if ($reportData) {
    Write-Host "    Edificios done      : $($reportData.done)" -ForegroundColor $C_OK
    Write-Host "    Edificios failed    : $($reportData.failed)" -ForegroundColor $(if ($reportData.failed -gt 0) {$C_ERR} else {$C_OK})
    if ($reportData.by_quality) {
        Write-Host "    Calidad high        : $($reportData.by_quality.high)" -ForegroundColor $C_OK
        Write-Host "    Calidad medium      : $($reportData.by_quality.medium)" -ForegroundColor $C_WARN
        Write-Host "    Calidad low         : $($reportData.by_quality.low)" -ForegroundColor White
    }
}

Write-Host ""
Write-Host "    Reporte   : $ReportJson" -ForegroundColor White
Write-Host "    FBX dir   : $FbxOutDir" -ForegroundColor White
Write-Host "    Progress  : $ProgressJson" -ForegroundColor White
Write-Host ("  " + ("=" * 68)) -ForegroundColor $C_HEAD

# ─── PASO 7: GIT COMMIT OPCIONAL ─────────────────────────────────────────────

if (-not $SkipGit -and $nFbx -gt 0) {
    Write-Host ""
    Write-Warn "NOTA: Los FBX de fotogrametría pueden ser muy grandes para git."
    Write-Warn "      Considera usar Git LFS: git lfs track '*.fbx'"
    Write-Host ""
    $doCommit = Read-Host "  ¿Hacer git add + commit de los archivos generados? [s/N]"

    if ($doCommit -match "^[sS]") {
        Write-Host ""
        Write-Info "Verificando git LFS..."
        $lfsCheck = & git lfs version 2>&1
        if ($lfsCheck -match "git-lfs") {
            Write-OK "Git LFS disponible: $lfsCheck"
            # Asegurar tracking de FBX
            $gitattributes = Join-Path $ProjectRoot ".gitattributes"
            if (Test-Path $gitattributes) {
                $content = Get-Content $gitattributes -Raw
                if ($content -notmatch "\*.fbx") {
                    Write-Info "Añadiendo *.fbx a .gitattributes para LFS..."
                    Add-Content $gitattributes "`n*.fbx filter=lfs diff=lfs merge=lfs -text"
                    & git -C $ProjectRoot add ".gitattributes"
                }
            }
        } else {
            Write-Warn "Git LFS no encontrado. Commiteando sin LFS (puede ser lento)."
        }

        # Stage archivos generados
        $filesToStage = @(
            "Assets/Models/Buildings_Photogrammetry/",
            "Assets/AlsasuaData/FacadeTextures/Photogrammetry/",
            "Assets/AlsasuaData/photogrammetry_report.json",
            "Assets/AlsasuaData/buildings_fusion_final.json",
        )

        Write-Info "Staging archivos..."
        foreach ($f in $filesToStage) {
            $fullPath = Join-Path $ProjectRoot $f
            if (Test-Path $fullPath) {
                & git -C $ProjectRoot add $f
                Write-Info "  git add $f"
            }
        }

        # Generar mensaje de commit
        $zonaMsg = if ($All) {"todas las zonas"} elseif ($Zona) {"zona $Zona"} else {"edificios fallidos"}
        $commitMsg = "feat: fotogrametría Meshroom+Blender — $nFbx FBX ($zonaMsg)"
        if ($GPU) { $commitMsg += " [GPU]" }

        Write-Info "Commiteando: '$commitMsg'"
        $gitResult = & git -C $ProjectRoot commit -m $commitMsg 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-OK "Commit creado: $commitMsg"
        } else {
            Write-Warn "git commit devolvió código $LASTEXITCODE"
            Write-Warn $gitResult
        }

        # Preguntar push
        $doPush = Read-Host "  ¿Hacer git push? [s/N]"
        if ($doPush -match "^[sS]") {
            Write-Info "Ejecutando git push..."
            & git -C $ProjectRoot push
            if ($LASTEXITCODE -eq 0) {
                Write-OK "Push completado"
            } else {
                Write-Warn "git push devolvió código $LASTEXITCODE"
            }
        }
    }
}

# ─── PASO 8: ABRIR UNITY (OPCIONAL) ──────────────────────────────────────────

if ($OpenUnity) {
    Write-Step 8 "Abriendo Unity con el proyecto"

    # Buscar Unity Hub o Unity.exe directamente
    $unityFound = $false

    if (Test-Path $UnityHubExe) {
        Write-OK "Unity Hub encontrado: $UnityHubExe"
        Write-Info "Abriendo proyecto: $UnityProject"
        try {
            Start-Process -FilePath $UnityHubExe `
                          -ArgumentList @("--", "--projectPath", $UnityProject) `
                          -NoNewWindow
            Write-OK "Unity Hub lanzado"
            $unityFound = $true
        } catch {
            Write-Warn "Error abriendo Unity Hub: $_"
        }
    }

    if (-not $unityFound) {
        # Intentar buscar Unity.exe en Program Files
        $unityExePaths = @(
            "C:\Program Files\Unity\Hub\Editor\*\Editor\Unity.exe",
            "C:\Program Files\Unity\Editor\Unity.exe",
        )
        foreach ($pattern in $unityExePaths) {
            $found = Get-Item $pattern -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($found) {
                Write-OK "Unity encontrado: $($found.FullName)"
                Start-Process -FilePath $found.FullName `
                              -ArgumentList @("-projectPath", $UnityProject) `
                              -NoNewWindow
                Write-OK "Unity lanzado con proyecto"
                $unityFound = $true
                break
            }
        }
    }

    if (-not $unityFound) {
        Write-Warn "Unity Hub/Unity.exe no encontrado."
        Write-Warn "Abre Unity manualmente y reimporta Assets/Models/Buildings_Photogrammetry/"
        Write-Warn "Unity Hub: https://unity.com/download"
    }
}

# ─── FIN ──────────────────────────────────────────────────────────────────────

Write-Host ""
if ($nFbx -gt 0) {
    Write-OK "Pipeline completado. $nFbx FBX generados."
} else {
    Write-OK "Pipeline completado."
}
Write-Info "Reimporta en Unity: Assets/Models/Buildings_Photogrammetry/"
Write-Info "Normal maps:        DirectX (flip G ya aplicado, compatible HDRP)"
Write-Info "De-lighting:        Retinex MSR multiescala (σ=15/80/250)"
if (Test-Path (Join-Path $ProjectRoot "Assets\AlsasuaData\FacadeTextures\Photogrammetry\*_albedo_4k.png")) {
    Write-OK "Texturas 4K disponibles (Real-ESRGAN upscale)"
}
Write-Host ""
