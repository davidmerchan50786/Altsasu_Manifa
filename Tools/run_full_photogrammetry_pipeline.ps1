<#
.SYNOPSIS
    Pipeline completo fotogrametría Alsasua Manifa
    Meshroom → Blender cleanup → Unity import

.DESCRIPTION
    Ejecutar desde E:\DAM\Altsasu_Manifa como administrador.
    Requiere: Meshroom 2025.1.0, Blender 5.1, Python 3.x

    Flujo completo:
      1. Verificar herramientas instaladas
      2. Crear directorios de caché en E:\MeshroomCache\
      3. Copiar fotos procesadas al input del pipeline
      4. Ejecutar meshroom_pipeline.py (SfM + MVS + Texturing + Blender cleanup)
      5. Ejecutar unity_photogrammetry_importer.py (actualizar buildings_fusion_final.json)
      6. Mostrar resumen: edificios procesados, tiempo, espacio usado
      7. Opción de hacer git add + commit de los FBX generados

    Caché en E:\MeshroomCache\ (NO en C:\) — requiere ~100 GB libres por zona.

.PARAMETER Zona
    Nombre de la zona a procesar (iglesia, ayto, plaza_fueros, casco_viejo, etc.)
    Si se omite con -All, procesa todas las zonas.

.PARAMETER All
    Procesa todas las zonas en orden de prioridad:
    iglesia → ayto → plaza_fueros → casco_viejo → gaztetxe → plaza_zubeztia → ferial → garcia_jimenez

.PARAMETER GPU
    Activa aceleración GPU en Meshroom (DepthMap) y Blender (Cycles GPU).
    Requiere GPU NVIDIA con drivers recientes.

.PARAMETER Force
    Reprocesa edificios aunque ya estén marcados como completados.

.PARAMETER Retry
    Procesa solo edificios con estado "failed" en progress.json.

.PARAMETER SkipGit
    No pregunta sobre git commit al final.

.EXAMPLE
    .\Tools\run_full_photogrammetry_pipeline.ps1 -Zona iglesia
    .\Tools\run_full_photogrammetry_pipeline.ps1 -Zona iglesia -GPU
    .\Tools\run_full_photogrammetry_pipeline.ps1 -All
    .\Tools\run_full_photogrammetry_pipeline.ps1 -All -GPU
    .\Tools\run_full_photogrammetry_pipeline.ps1 -All -GPU -Force
    .\Tools\run_full_photogrammetry_pipeline.ps1 -All -Retry
#>

param(
    [string]$Zona     = "",
    [switch]$All,
    [switch]$GPU,
    [switch]$Force,
    [switch]$Retry,
    [switch]$SkipGit
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

$MeshroomScript = Join-Path $PSScriptRoot "meshroom_pipeline.py"
$ImportScript   = Join-Path $PSScriptRoot "unity_photogrammetry_importer.py"

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

# ─── CABECERA ─────────────────────────────────────────────────────────────────

Write-Header "PIPELINE FOTOGRAMETRÍA — Altsasu Manifa"
Write-Info "Fecha     : $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
Write-Info "Modo      : $(if ($All) {'TODAS LAS ZONAS'} elseif ($Zona) {$Zona} else {'(ninguno)'})"
Write-Info "GPU       : $(if ($GPU) {'SÍ'} else {'NO'})"
Write-Info "Force     : $(if ($Force) {'SÍ'} else {'NO'})"
Write-Info "Retry     : $(if ($Retry) {'SÍ'} else {'NO'})"
Write-Info "Proyecto  : $ProjectRoot"

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

Write-Step 1 "Verificando herramientas"

$toolsOk = $true

# Meshroom
if (Test-Path $MeshroomBatch) {
    Write-OK "Meshroom 2025.1.0: $MeshroomBatch"
} else {
    Write-Warn "Meshroom no encontrado: $MeshroomBatch"
    Write-Warn "  Descarga: https://alicevision.org/#meshroom"
    $toolsOk = $false
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

# ─── PASO 4: EJECUTAR MESHROOM PIPELINE ──────────────────────────────────────

Write-Step 4 "Ejecutando Meshroom pipeline"

$tStart = Get-Date

# Construir argumentos
$pyArgs = @($MeshroomScript)
if ($All)   { $pyArgs += "--all"   }
if ($Zona)  { $pyArgs += @("--zona", $Zona) }
if ($GPU)   { $pyArgs += "--gpu"   }
if ($Force) { $pyArgs += "--force" }
if ($Retry) { $pyArgs += "--retry" }

Write-Info "Comando: $pythonCmd $($pyArgs -join ' ')"
Write-Info "Esto puede tardar varias horas. No cierres esta ventana."
Write-Host ""

$proc = Start-Process -FilePath $pythonCmd `
                      -ArgumentList $pyArgs `
                      -NoNewWindow -PassThru -Wait `
                      -WorkingDirectory $ProjectRoot

$meshroomExitCode = $proc.ExitCode
$tMeshroom = (Get-Date) - $tStart

if ($meshroomExitCode -eq 0) {
    Write-OK "Meshroom pipeline completado en $([math]::Round($tMeshroom.TotalMinutes, 1)) min"
} else {
    Write-Err "Meshroom pipeline terminó con error (código: $meshroomExitCode)"
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

# ─── FIN ──────────────────────────────────────────────────────────────────────

Write-Host ""
Write-OK "Pipeline completado. Abre Unity y reimporta Assets/Models/Buildings_Photogrammetry/"
Write-Host ""
