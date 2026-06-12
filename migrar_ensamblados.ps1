# ============================================================
#  Migracion a ensamblados - Altsasu Manifa
#  Generado automaticamente desde analisis de dependencias.
#  Ejecutar con Unity CERRADO, desde la carpeta del proyecto.
#  Uso:  powershell -ExecutionPolicy Bypass -File .\migrar_ensamblados.ps1
# ============================================================
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
if (-not (Test-Path (Join-Path $root 'Assets\Scripts'))) { Write-Error 'Coloca este script en la RAIZ del proyecto (donde esta la carpeta Assets).'; exit 1 }
$S = Join-Path $root 'Assets\Scripts'
Write-Host 'Proyecto:' $root -ForegroundColor Cyan

# --- 1. Crear carpetas de ensamblado ---
foreach ($d in 'Runtime','Systems','Modules') { New-Item -ItemType Directory -Force -Path (Join-Path $S $d) | Out-Null }

# --- 2. Escribir los .asmdef ---
$asm_Core = @'
{
    "name": "Alsasua.Core",
    "rootNamespace": "",
    "references": [
        "CesiumForUnity",
        "DelaunayER",
        "Den.Tools",
        "EasyRoads3Dv3",
        "MapMagic",
        "Newtonsoft.Json",
        "Unity.Burst",
        "Unity.Cinemachine",
        "Unity.Collections",
        "Unity.InputSystem",
        "Unity.InputSystem.ForUI",
        "Unity.Mathematics",
        "Unity.ProBuilder",
        "Unity.RenderPipelines.Core.Runtime",
        "Unity.RenderPipelines.Core.Runtime.Shared",
        "Unity.RenderPipelines.GPUDriven.Runtime",
        "Unity.RenderPipelines.HighDefinition.Config.Runtime",
        "Unity.RenderPipelines.HighDefinition.Runtime",
        "Unity.Services.Core",
        "Unity.Splines",
        "Unity.TerrainTools",
        "Unity.TextMeshPro",
        "Unity.Timeline",
        "Unity.UnifiedRayTracing.Runtime",
        "Unity.VisualEffectGraph.Runtime",
        "Unity.VisualScripting.Core",
        "Unity.VisualScripting.Flow",
        "ALP8310_ControllerGlobal",
        "Autodesk.Fbx",
        "Unity.Formats.Fbx.Runtime",
        "Unity.Multiplayer.Center.Common"
    ],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
'@
Set-Content -Path (Join-Path $S 'Core\Alsasua.Core.asmdef') -Value $asm_Core -Encoding UTF8
$asm_Runtime = @'
{
    "name": "Alsasua.Runtime",
    "rootNamespace": "",
    "references": [
        "Alsasua.Core",
        "CesiumForUnity",
        "DelaunayER",
        "Den.Tools",
        "EasyRoads3Dv3",
        "MapMagic",
        "Newtonsoft.Json",
        "Unity.Burst",
        "Unity.Cinemachine",
        "Unity.Collections",
        "Unity.InputSystem",
        "Unity.InputSystem.ForUI",
        "Unity.Mathematics",
        "Unity.ProBuilder",
        "Unity.RenderPipelines.Core.Runtime",
        "Unity.RenderPipelines.Core.Runtime.Shared",
        "Unity.RenderPipelines.GPUDriven.Runtime",
        "Unity.RenderPipelines.HighDefinition.Config.Runtime",
        "Unity.RenderPipelines.HighDefinition.Runtime",
        "Unity.Services.Core",
        "Unity.Splines",
        "Unity.TerrainTools",
        "Unity.TextMeshPro",
        "Unity.Timeline",
        "Unity.UnifiedRayTracing.Runtime",
        "Unity.VisualEffectGraph.Runtime",
        "Unity.VisualScripting.Core",
        "Unity.VisualScripting.Flow",
        "ALP8310_ControllerGlobal",
        "Autodesk.Fbx",
        "Unity.Formats.Fbx.Runtime",
        "Unity.Multiplayer.Center.Common"
    ],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
'@
Set-Content -Path (Join-Path $S 'Runtime\Alsasua.Runtime.asmdef') -Value $asm_Runtime -Encoding UTF8
$asm_Systems = @'
{
    "name": "Alsasua.Systems",
    "rootNamespace": "",
    "references": [
        "Alsasua.Core",
        "Alsasua.Runtime",
        "Alsasua.Modules",
        "CesiumForUnity",
        "DelaunayER",
        "Den.Tools",
        "EasyRoads3Dv3",
        "MapMagic",
        "Newtonsoft.Json",
        "Unity.Burst",
        "Unity.Cinemachine",
        "Unity.Collections",
        "Unity.InputSystem",
        "Unity.InputSystem.ForUI",
        "Unity.Mathematics",
        "Unity.ProBuilder",
        "Unity.RenderPipelines.Core.Runtime",
        "Unity.RenderPipelines.Core.Runtime.Shared",
        "Unity.RenderPipelines.GPUDriven.Runtime",
        "Unity.RenderPipelines.HighDefinition.Config.Runtime",
        "Unity.RenderPipelines.HighDefinition.Runtime",
        "Unity.Services.Core",
        "Unity.Splines",
        "Unity.TerrainTools",
        "Unity.TextMeshPro",
        "Unity.Timeline",
        "Unity.UnifiedRayTracing.Runtime",
        "Unity.VisualEffectGraph.Runtime",
        "Unity.VisualScripting.Core",
        "Unity.VisualScripting.Flow",
        "ALP8310_ControllerGlobal",
        "Autodesk.Fbx",
        "Unity.Formats.Fbx.Runtime",
        "Unity.Multiplayer.Center.Common"
    ],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
'@
Set-Content -Path (Join-Path $S 'Systems\Alsasua.Systems.asmdef') -Value $asm_Systems -Encoding UTF8
$asm_Modules = @'
{
    "name": "Alsasua.Modules",
    "rootNamespace": "",
    "references": [
        "Alsasua.Core",
        "CesiumForUnity",
        "DelaunayER",
        "Den.Tools",
        "EasyRoads3Dv3",
        "MapMagic",
        "Newtonsoft.Json",
        "Unity.Burst",
        "Unity.Cinemachine",
        "Unity.Collections",
        "Unity.InputSystem",
        "Unity.InputSystem.ForUI",
        "Unity.Mathematics",
        "Unity.ProBuilder",
        "Unity.RenderPipelines.Core.Runtime",
        "Unity.RenderPipelines.Core.Runtime.Shared",
        "Unity.RenderPipelines.GPUDriven.Runtime",
        "Unity.RenderPipelines.HighDefinition.Config.Runtime",
        "Unity.RenderPipelines.HighDefinition.Runtime",
        "Unity.Services.Core",
        "Unity.Splines",
        "Unity.TerrainTools",
        "Unity.TextMeshPro",
        "Unity.Timeline",
        "Unity.UnifiedRayTracing.Runtime",
        "Unity.VisualEffectGraph.Runtime",
        "Unity.VisualScripting.Core",
        "Unity.VisualScripting.Flow",
        "ALP8310_ControllerGlobal",
        "Autodesk.Fbx",
        "Unity.Formats.Fbx.Runtime",
        "Unity.Multiplayer.Center.Common"
    ],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
'@
Set-Content -Path (Join-Path $S 'Modules\Alsasua.Modules.asmdef') -Value $asm_Modules -Encoding UTF8
$asm_Editor = @'
{
    "name": "Alsasua.Editor",
    "rootNamespace": "",
    "references": [
        "Alsasua.Core",
        "Alsasua.Runtime",
        "Alsasua.Modules",
        "Alsasua.Systems",
        "CesiumForUnity",
        "DelaunayER",
        "Den.Tools",
        "EasyRoads3Dv3",
        "MapMagic",
        "Newtonsoft.Json",
        "Unity.Burst",
        "Unity.Cinemachine",
        "Unity.Collections",
        "Unity.InputSystem",
        "Unity.InputSystem.ForUI",
        "Unity.Mathematics",
        "Unity.ProBuilder",
        "Unity.RenderPipelines.Core.Runtime",
        "Unity.RenderPipelines.Core.Runtime.Shared",
        "Unity.RenderPipelines.GPUDriven.Runtime",
        "Unity.RenderPipelines.HighDefinition.Config.Runtime",
        "Unity.RenderPipelines.HighDefinition.Runtime",
        "Unity.Services.Core",
        "Unity.Splines",
        "Unity.TerrainTools",
        "Unity.TextMeshPro",
        "Unity.Timeline",
        "Unity.UnifiedRayTracing.Runtime",
        "Unity.VisualEffectGraph.Runtime",
        "Unity.VisualScripting.Core",
        "Unity.VisualScripting.Flow",
        "ALP8310_ControllerGlobal",
        "Autodesk.Fbx",
        "Unity.Formats.Fbx.Runtime",
        "Unity.Multiplayer.Center.Common",
        "Unity.RenderPipelines.Core.Editor",
        "Unity.RenderPipelines.HighDefinition.Editor",
        "Unity.TextMeshPro.Editor",
        "Unity.Cinemachine.Editor",
        "Unity.Burst.Editor",
        "Unity.Collections.Editor",
        "Unity.Mathematics.Editor",
        "Unity.ProBuilder.Editor",
        "Unity.Splines.Editor",
        "Unity.TerrainTools.Editor",
        "Unity.Timeline.Editor",
        "Unity.InputSystem",
        "Den.Tools.Editor",
        "MapMagic.Editor",
        "Autodesk.Fbx.Editor",
        "Unity.Formats.Fbx.Editor",
        "Unity.EditorCoroutines.Editor",
        "ALP8310_ControllerGlobal.Editor"
    ],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
'@
Set-Content -Path (Join-Path $S 'Editor\Alsasua.Editor.asmdef') -Value $asm_Editor -Encoding UTF8
Write-Host '5 asmdef escritos' -ForegroundColor Green

# --- 3. Mover scripts (con su .meta) a cada ensamblado ---
function Move-Cs($rel,$destDir){
  $src = Join-Path $S $rel
  $base = Split-Path $rel -Leaf
  $dst = Join-Path (Join-Path $S $destDir) $base
  if (-not (Test-Path $src)) { Write-Warning "NO existe: $rel"; return }
  Move-Item -Force $src $dst
  if (Test-Path ($src + '.meta')) { Move-Item -Force ($src + '.meta') ($dst + '.meta') }
  else { Write-Warning "sin .meta: $rel" }
}
$mv_Core = @(
  'AlsasuaLogger.cs',
  'ConfiguradorAssetsAAA.cs',
  'DirectorMundo.cs',
  'GestorMaterialesAlsasua.cs',
  'HUDManifestacion.cs',
  'IAgente.cs',
  'IDamageable.cs',
  'Jobs/JobsArboles.cs',
  'Jobs/JobsOptimizacion.cs',
  'MenuPausa.cs',
  'MeshBuilder.cs',
  'SingletonMono.cs',
  'SistemaApoyoPopular.cs',
  'SistemaCalidadGrafica.cs',
  'SistemaDecalesHDRP.cs',
  'SistemaDestruccion.cs',
  'SistemaDeteccionIA.cs',
  'SistemaFootIK.cs',
  'SistemaHuellasAsfalto.cs',
  'SistemaIA.cs',
  'SistemaMultitud.cs',
  'SistemaOpciones.cs',
  'SistemaRagdoll.cs',
  'TexturasVivo.cs',
  'VehiculoBase.cs'
)
foreach ($f in $mv_Core) { Move-Cs $f 'Core' }
Write-Host 'Movidos a Core:' $mv_Core.Count -ForegroundColor Green
$mv_Runtime = @(
  'AlsasuaTreeStreamer.cs',
  'AltsasuCore.cs',
  'AudioManager.cs',
  'ControladorJugador.cs',
  'ControladorVehiculoJugador.cs',
  'EventManager.cs',
  'FusionadorEdificiosUltra.cs',
  'GameManagerAltsasua.cs',
  'GeneradorGeometriaPrecisa.cs',
  'GeneradorMundoOSM.cs',
  'GeneradorRiosYPuentes.cs',
  'GeoDataAlsasua.cs',
  'HUDCanvas.cs',
  'IInteractable.cs',
  'IntegradorMatematicas.cs',
  'Jobs/MatematicasAlsasua.cs',
  'JuegoManifestacion.cs',
  'MisionesAltsasua.cs',
  'MisionesSec.cs',
  'NPCBase.cs',
  'NPCCivil.cs',
  'NPCGuard.cs',
  'PoliciaForalIA.cs',
  'PosicionadorPrecisionUrbana.cs',
  'SemaforoNodo.cs',
  'SembradoVegetacionManual.cs',
  'SistemaArmasExtendido.cs',
  'SistemaAssets.cs',
  'SistemaCargasPoliciales.cs',
  'SistemaCharcos.cs',
  'SistemaClima.cs',
  'SistemaDiagnostico.cs',
  'SistemaDisparo.cs',
  'SistemaEdificiosAAA.cs',
  'SistemaExplosion.cs',
  'SistemaFauna.cs',
  'SistemaGrafitis.cs',
  'SistemaGuardado.cs',
  'SistemaImpactos.cs',
  'SistemaLogros.cs',
  'SistemaManifestacion.cs',
  'SistemaMisiones.cs',
  'SistemaMoralManifestacion.cs',
  'SistemaNavMesh.cs',
  'SistemaOptimizacion.cs',
  'SistemaPolish.cs',
  'SistemaPostProcesoAAA.cs',
  'SistemaReverbZonas.cs',
  'SistemaTerreno.cs',
  'SistemaTrafico.cs',
  'SistemaTren.cs',
  'SistemaTutorial.cs',
  'SistemaVolumenHDRP.cs',
  'SistemaZonas.cs',
  'SistemasInfraestructura.cs',
  'SistemasSimulacion.cs',
  'VehiculoNPC.cs'
)
foreach ($f in $mv_Runtime) { Move-Cs $f 'Runtime' }
Write-Host 'Movidos a Runtime:' $mv_Runtime.Count -ForegroundColor Green
$mv_Systems = @(
  'AplicadorOrtofoto.cs',
  'CesiumCapasAlsasua.cs',
  'ConductorMundo.cs',
  'ConfiguradorPersonajeAAA.cs',
  'ConversorMaterialesHDRP.cs',
  'DiagnosticoArranque.cs',
  'DiagnosticoGrafico.cs',
  'GeneradorFachadasAAA.cs',
  'GeneradorInterioresAAA.cs',
  'GeneradorRocasProcedurales.cs',
  'GeneradorTejadosAAA.cs',
  'GeneradorTerrenoUltraPreciso.cs',
  'GestorZonasAlsasua.cs',
  'HUDSistemas.cs',
  'IndicadorEntradaVehiculo.cs',
  'IntegradorAssets.cs',
  'MobiliarioUrbano.cs',
  'ProcesadorMapillaryObjetos.cs',
  'ProcesadorNubePuntos.cs',
  'PropsDestruccionManifestacion.cs',
  'SceneBootstrapper.cs',
  'SistemaAgendaNPC.cs',
  'SistemaAguaRio.cs',
  'SistemaAmbientParticulas.cs',
  'SistemaAnimacionesRuntime.cs',
  'SistemaCalidadGate.cs',
  'SistemaCamaraCinetica.cs',
  'SistemaChunks.cs',
  'SistemaClimaEfectos.cs',
  'SistemaDetalleTerreno.cs',
  'SistemaDirectorConsumos.cs',
  'SistemaEdificiosFotogrametria.cs',
  'SistemaHumoFabricas.cs',
  'SistemaIKProcedural.cs',
  'SistemaMobiliarioUrbano.cs',
  'SistemaMontesFondo.cs',
  'SistemaNeblina.cs',
  'SistemaNevadasTerreno.cs',
  'SistemaOcclusion.cs',
  'SistemaReaccionNPCs.cs',
  'SistemaReflexiones.cs',
  'SistemaRocasHD.cs',
  'SistemaRotulosZona.cs',
  'SistemaSeguridad.cs',
  'SistemaShaderGlobals.cs',
  'SistemaSpawnCiviles.cs',
  'SistemaSueloAAA.cs',
  'SistemaTuneles.cs',
  'SistemaVidaNocturna.cs',
  'SistemaVientoVegetacion.cs',
  'SistemaWater.cs',
  'SmokeTestRunner.cs',
  'TuningFisica.cs'
)
foreach ($f in $mv_Systems) { Move-Cs $f 'Systems' }
Write-Host 'Movidos a Systems:' $mv_Systems.Count -ForegroundColor Green
$mv_Modules = @(
  'AplicadorManchaChistorra.cs',
  'AplicadorTexturasReales.cs',
  'AutoImportadorIncoming.cs',
  'CatalogoVivo.cs',
  'Core/ArchitectureIndex.cs',
  'Core/Events/FaccionEvents.cs',
  'ExtensionesSeguras.cs',
  'FaccionDefinition.cs',
  'GeneradorCallesAltsasu.cs',
  'GeneradorInterioresSimples.cs',
  'IntegradorAssetsInteriores.cs',
  'InterioresExplorables.cs',
  'Jobs/JobsNavMesh.cs',
  'Jobs/JobsOSM.cs',
  'MenuPrincipal.cs',
  'OptimizadorTerreno.cs',
  'OptimizadorVisualHDRP.cs',
  'SistemaAPV.cs',
  'SistemaAPVScenarios.cs',
  'SistemaFacciones.cs',
  'SistemaFachadasDinamicas.cs',
  'SistemaImpostores.cs',
  'SistemaMusicaAdaptativa.cs',
  'SistemaTelemetria.cs',
  'VariadorAparienciaNPC.cs'
)
foreach ($f in $mv_Modules) { Move-Cs $f 'Modules' }
Write-Host 'Movidos a Modules:' $mv_Modules.Count -ForegroundColor Green

# --- 4. Eliminar asmdef vacios antiguos (Gameplay/World/Render) ---
foreach ($old in 'Gameplay','World','Render') {
  $p = Join-Path $S $old
  if (Test-Path $p) { Remove-Item -Recurse -Force $p; Remove-Item -Force ($p + '.meta') -ErrorAction SilentlyContinue; Write-Host 'Eliminado asmdef vacio:' $old }
}

Write-Host '' 
Write-Host 'LISTO. Abre Unity y deja que recompile. Revisa la consola.' -ForegroundColor Cyan
Write-Host 'Si hay errores CSxxxx por tipos de paquetes, pasamelos y ajusto los asmdef.' -ForegroundColor Yellow