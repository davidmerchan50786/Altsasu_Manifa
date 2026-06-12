# Corrige los 2 cruces reales entre ensamblados detectados por Unity.
$ErrorActionPreference='Stop'
$root = $PSScriptRoot
$S = Join-Path $root 'Assets\Scripts'
if (-not (Test-Path $S)) { Write-Error 'Coloca este script en la RAIZ del proyecto.'; exit 1 }

function Move-Cs($from,$toDir){
  $src = Join-Path $S $from
  $base = Split-Path $from -Leaf
  $dst = Join-Path (Join-Path $S $toDir) $base
  if (-not (Test-Path $src)) { Write-Warning "NO existe: $from"; return }
  Move-Item -Force $src $dst
  if (Test-Path ($src + '.meta')) { Move-Item -Force ($src + '.meta') ($dst + '.meta') }
  Write-Host "  $from  ->  $toDir\$base" -ForegroundColor Green
}

# 1. ExtensionesSeguras (metodos de extension GetSafe usados por todo) -> Core
Move-Cs 'Modules\ExtensionesSeguras.cs' 'Core'
# 2. IntegradorAssetsInteriores (clase #if UNITY_EDITOR, usa UnityEditor) -> Editor
Move-Cs 'Modules\IntegradorAssetsInteriores.cs' 'Editor'

Write-Host 'Listo. Vuelve a Unity y deja que recompile.' -ForegroundColor Cyan
