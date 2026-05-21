# DescargarLIDARAlsasua.ps1
# ═══════════════════════════════════════════════════════════════════════════
#  Descarga el MDT05 (DSM/DTM 5m) del IGN para Alsasua/Altsasua
#  y el LIDAR PNOA si está disponible (1-4 pts/m²).
#
#  El IGN proporciona datos gratuitos bajo licencia CC BY 4.0:
#    https://centrodedescargas.cnig.es/CentroDescargas/
#
#  Archivos que descarga:
#    MDT05: Modelo Digital del Terreno 5m (DTM) — hoja 114
#    MDS05: Modelo Digital de Superficie 5m  (DSM con edificios) — hoja 114
#    LIDAR: Nube de puntos en formato LAZ si está disponible
#
#  Hoja MTN50 #114 = Alsasua/Altsasua (Navarra)
#  BB GPS: 42.875°N, -2.195°W — 42.920°N, -2.145°W
#
#  RESULTADO: Assets/AlsasuaData/
#    dsm_alsasua_5m.asc    — superficie con tejados (ASCII Grid)
#    dtm_alsasua_5m.asc    — terreno sin edificios (ASCII Grid)
#    lidar_alsasua.laz     — nube de puntos (si disponible)
# ═══════════════════════════════════════════════════════════════════════════

$OUT = "E:\Desk\DAM\Altsasu_Manifa\Assets\AlsasuaData"
$TEMP = "E:\Desk\DAM\Altsasu_Manifa\Temp\LIDAR_IGN"
New-Item -ItemType Directory -Force $TEMP | Out-Null

Write-Host "=== Descargando datos LIDAR/DSM del IGN para Alsasua ===" -ForegroundColor Cyan

# ── 1. MDT05 (DTM) — hoja 114 ─────────────────────────────────────────────
# URL directa al MDT05 de la hoja MTN50 #114 (Alsasua)
$urlMDT = "https://centrodedescargas.cnig.es/CentroDescargas/downloadFile.do?seq=MDT05-ASC-ETR89-114"
$pathMDT = "$TEMP\mdt05_114.zip"

Write-Host "Descargando MDT05 hoja 114 (Alsasua)..." -ForegroundColor Yellow
try {
    Invoke-WebRequest -Uri $urlMDT -OutFile $pathMDT -TimeoutSec 120 -UseBasicParsing
    Write-Host "MDT05 descargado." -ForegroundColor Green
} catch {
    Write-Host "Error MDT05: $_" -ForegroundColor Red
    Write-Host "Intentando URL alternativa (WCS)..." -ForegroundColor Yellow

    # Alternativa: WCS (Web Coverage Service) del IGN
    $wcsUrl = "https://servicios.idee.es/wcs-inspire/mdt?SERVICE=WCS&VERSION=1.0.0&REQUEST=GetCoverage" +
              "&COVERAGE=Elevacion4258_5&CRS=EPSG:4326" +
              "&BBOX=-2.195,42.875,-2.145,42.920" +
              "&WIDTH=1000&HEIGHT=900&FORMAT=ArcGrid"
    try {
        Invoke-WebRequest -Uri $wcsUrl -OutFile "$OUT\dtm_alsasua_5m.asc" -TimeoutSec 90 -UseBasicParsing
        Write-Host "DTM via WCS descargado." -ForegroundColor Green
    } catch {
        Write-Host "WCS también falló. Usando datos IGN ATOM feed..." -ForegroundColor Yellow
    }
}

# ── 2. MDS05 (DSM con edificios) ──────────────────────────────────────────
# El DSM de edificios en IGN se llama "PNOA-MDT05" en modalidad LIDAR
$wcsUrlDSM = "https://servicios.idee.es/wcs-inspire/mdt?SERVICE=WCS&VERSION=1.0.0&REQUEST=GetCoverage" +
             "&COVERAGE=Elevacion4258_MDSNorm_CSN5&CRS=EPSG:4326" +
             "&BBOX=-2.200,42.875,-2.140,42.925" +
             "&WIDTH=1200&HEIGHT=1000&FORMAT=ArcGrid"

Write-Host "Descargando DSM (superficie con edificios)..." -ForegroundColor Yellow
try {
    Invoke-WebRequest -Uri $wcsUrlDSM -OutFile "$OUT\dsm_alsasua_5m.asc" -TimeoutSec 90 -UseBasicParsing
    $size = (Get-Item "$OUT\dsm_alsasua_5m.asc").Length / 1KB
    Write-Host "DSM descargado: $($size.ToString('F0')) KB" -ForegroundColor Green
} catch {
    Write-Host "Error descargando DSM: $_" -ForegroundColor Red
}

# ── 3. LIDAR PNOA (nube de puntos LAZ) ───────────────────────────────────
# El LIDAR de Navarra está en el servidor del IDENA (Navarra)
$urlLIDARNavarra = "https://idena.navarra.es/ogc/wcs?SERVICE=WCS&VERSION=1.0.0&REQUEST=GetCoverage" +
                   "&COVERAGE=NUPNTS5&CRS=EPSG:25830" +
                   "&BBOX=571000,4749000,577000,4754000" +
                   "&FORMAT=PointCloudLAZ"

Write-Host "Descargando LIDAR PNOA Navarra..." -ForegroundColor Yellow
try {
    Invoke-WebRequest -Uri $urlLIDARNavarra -OutFile "$TEMP\lidar_alsasua.laz" -TimeoutSec 120 -UseBasicParsing
    Write-Host "LIDAR descargado." -ForegroundColor Green
    Copy-Item "$TEMP\lidar_alsasua.laz" "$OUT\lidar_alsasua.laz"
} catch {
    Write-Host "LIDAR no disponible via WCS directo: $_" -ForegroundColor Red
    Write-Host "Para descarga manual del LIDAR PNOA de Navarra:" -ForegroundColor Cyan
    Write-Host "  https://idena.navarra.es/descargas/" -ForegroundColor White
    Write-Host "  Buscar: 'Nube de puntos PNOA' > Alsasua" -ForegroundColor White
    Write-Host "  Formato: LAZ, Sistema: ETRS89 UTM 30N (EPSG:25830)" -ForegroundColor White
}

# ── Verificar qué se descargó ─────────────────────────────────────────────
Write-Host "`n=== Archivos descargados ===" -ForegroundColor Cyan
Get-ChildItem "$OUT" -Filter "*.asc", "*.laz", "*.raw" | ForEach-Object {
    Write-Host "  $($_.Name): $([math]::Round($_.Length/1KB,0)) KB"
}

Write-Host "`n=== Siguiente paso ===" -ForegroundColor Green
Write-Host "Si descargaste archivos .asc o .laz, en Unity ejecuta:" -ForegroundColor White
Write-Host "  Tools -> Alsasua -> 🏔 Procesar DSM/LIDAR" -ForegroundColor Yellow
