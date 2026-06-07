# DescargarDatosAlsasua.ps1  (ASCII puro - sin caracteres especiales)
# Descarga datos reales de Alsasua:
#   buildings_unity.json  roads_unity.json  dem_unity_1025.raw  trees_unity.json
#   Audio CC0 de Kenney.nl -> Assets/Resources/Audio/
# Sin API key. Sin registro. Licencia ODbL/CC0.

param()
$ErrorActionPreference = "Continue"
$BASE  = Split-Path $MyInvocation.MyCommand.Path
$DATA  = Join-Path $BASE "Assets\AlsasuaData"
$AUDIO = Join-Path $BASE "Assets\Resources\Audio"

New-Item -ItemType Directory -Force -Path $DATA  | Out-Null
New-Item -ItemType Directory -Force -Path $AUDIO | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $BASE "Assets\Animators") | Out-Null

$LAT   = 42.9006
$LON   = -2.1667
$DELTA = 0.018
$S     = $LAT - $DELTA
$N     = $LAT + $DELTA
$W     = $LON - $DELTA * 1.4
$E2    = $LON + $DELTA * 1.4

Write-Host "=== DESCARGA DATOS ALSASUA ===" -ForegroundColor Cyan
Write-Host "Bounding box: $S,$W,$N,$E2" -ForegroundColor Gray

$URL_OVERPASS = "https://overpass-api.de/api/interpreter"
$ESCALA = 111320.0
$LAT0   = $LAT
$LON0   = $LON

# ============================================================
# 1. EDIFICIOS
# ============================================================
Write-Host "[1/5] Edificios OSM..." -ForegroundColor Yellow

$qEdif = "[out:json][timeout:60];(way[building]($S,$W,$N,$E2);relation[building]($S,$W,$N,$E2););out geom;"

try {
    $body = "data=" + [Uri]::EscapeDataString($qEdif)
    $resp = Invoke-RestMethod -Uri $URL_OVERPASS -Method Post -Body $body `
        -ContentType "application/x-www-form-urlencoded" -TimeoutSec 90

    $edificios = @()
    foreach ($elem in $resp.elements) {
        if (-not $elem.geometry -or $elem.geometry.Count -lt 3) { continue }
        $verts = @()
        foreach ($pt in $elem.geometry) {
            $x = ($pt.lon - $LON0) * $ESCALA * [Math]::Cos($LAT0 * [Math]::PI / 180.0)
            $z = ($pt.lat - $LAT0) * $ESCALA
            $verts += @{ x = [Math]::Round($x, 2); z = [Math]::Round($z, 2) }
        }
        $tags  = $elem.tags
        $tipo  = if ($tags.building)         { $tags.building }         else { "yes" }
        $pisos = if ($tags.'building:levels') { [int]$tags.'building:levels' } else { 2 }
        $nom   = if ($tags.name)             { $tags.name }             else { "" }
        $edificios += @{ id=$elem.id; type=$tipo; name=$nom; levels=$pisos; height=($pisos*3.2); vertices=$verts }
    }
    $edificios | ConvertTo-Json -Depth 6 -Compress | Set-Content "$DATA\buildings_unity.json" -Encoding UTF8
    Write-Host "  OK: $($edificios.Count) edificios -> buildings_unity.json" -ForegroundColor Green
}
catch {
    Write-Host "  WARN edificios OSM: $_" -ForegroundColor Red
    # Fallback sintetico
    $edificios = @()
    $id = 1
    $zonas = @(
        @{cx=0;cz=0;n=30;tipo="residential"},
        @{cx=150;cz=0;n=15;tipo="commercial"},
        @{cx=-100;cz=80;n=20;tipo="residential"},
        @{cx=200;cz=-150;n=10;tipo="industrial"}
    )
    foreach ($zona in $zonas) {
        for ($i = 0; $i -lt $zona.n; $i++) {
            $bx = $zona.cx + (Get-Random -Min -80 -Max 80)
            $bz = $zona.cz + (Get-Random -Min -80 -Max 80)
            $w2 = Get-Random -Min 8 -Max 20
            $d  = Get-Random -Min 8 -Max 18
            $p  = Get-Random -Min 1 -Max 5
            $edificios += @{
                id=$id++; type=$zona.tipo; name=""; levels=$p; height=($p*3.2)
                vertices=@(@{x=$bx;z=$bz},@{x=($bx+$w2);z=$bz},@{x=($bx+$w2);z=($bz+$d)},@{x=$bx;z=($bz+$d)})
            }
        }
    }
    $edificios | ConvertTo-Json -Depth 6 -Compress | Set-Content "$DATA\buildings_unity.json" -Encoding UTF8
    Write-Host "  OK: $($edificios.Count) edificios sinteticos generados" -ForegroundColor Yellow
}

# ============================================================
# 2. CARRETERAS
# ============================================================
Write-Host "[2/5] Carreteras OSM..." -ForegroundColor Yellow

$tiposVia = "primary|secondary|tertiary|residential|living_street|pedestrian|footway|path|service|unclassified"
$qRoads = "[out:json][timeout:60];(way[highway~`"^($tiposVia)$`"]($S,$W,$N,$E2););out geom;"

try {
    $body = "data=" + [Uri]::EscapeDataString($qRoads)
    $resp = Invoke-RestMethod -Uri $URL_OVERPASS -Method Post -Body $body `
        -ContentType "application/x-www-form-urlencoded" -TimeoutSec 90

    $anchos = @{
        primary=7.0; secondary=6.0; tertiary=5.0; residential=4.5
        "living_street"=4.0; pedestrian=3.0; footway=2.0; path=1.5
        service=3.5; unclassified=4.0
    }
    $tiposMap = @{
        primary="primary"; secondary="secondary"; tertiary="tertiary"
        residential="residential"; "living_street"="residential"
        pedestrian="pedestrian"; footway="path"; path="path"
        service="service"; unclassified="residential"
    }

    $segs = @()
    foreach ($elem in $resp.elements) {
        if (-not $elem.geometry -or $elem.geometry.Count -lt 2) { continue }
        $tv  = $elem.tags.highway
        $aw  = if ($anchos[$tv])    { $anchos[$tv] }    else { 4.0 }
        $tm  = if ($tiposMap[$tv])  { $tiposMap[$tv] }  else { "residential" }
        $pts = @()
        foreach ($pt in $elem.geometry) {
            $x = ($pt.lon - $LON0) * $ESCALA * [Math]::Cos($LAT0 * [Math]::PI / 180.0)
            $z = ($pt.lat - $LAT0) * $ESCALA
            $pts += @{ x=[Math]::Round($x,2); z=[Math]::Round($z,2) }
        }
        $segs += @{
            id=$elem.id; type=$tm; width=$aw
            name=if ($elem.tags.name) { $elem.tags.name } else { "" }
            oneway=($elem.tags.oneway -eq "yes")
            points=$pts
        }
    }
    $segs | ConvertTo-Json -Depth 6 -Compress | Set-Content "$DATA\roads_unity.json" -Encoding UTF8
    Write-Host "  OK: $($segs.Count) segmentos -> roads_unity.json" -ForegroundColor Green
}
catch {
    Write-Host "  WARN carreteras: $_" -ForegroundColor Red
    $segs = @(
        @{id=1;type="primary";width=7;name="Nafarroa Kalea";oneway=$false;points=@(@{x=-300;z=0},@{x=300;z=0})}
        @{id=2;type="secondary";width=6;name="Kale Nagusia";oneway=$false;points=@(@{x=0;z=-200},@{x=0;z=200})}
        @{id=3;type="residential";width=4.5;name="Herriko Plaza";oneway=$false;points=@(@{x=-50;z=-50},@{x=50;z=-50},@{x=50;z=50},@{x=-50;z=50})}
        @{id=4;type="primary";width=7;name="N-1";oneway=$false;points=@(@{x=-400;z=-300},@{x=400;z=-300})}
    )
    $segs | ConvertTo-Json -Depth 6 -Compress | Set-Content "$DATA\roads_unity.json" -Encoding UTF8
    Write-Host "  OK: carreteras sinteticas generadas" -ForegroundColor Yellow
}

# ============================================================
# 3. DEM (heightmap 1025x1025 16-bit)
# ============================================================
Write-Host "[3/5] Generando DEM 1025x1025..." -ForegroundColor Yellow

$SIZE    = 1025
$heights = New-Object float[] ($SIZE * $SIZE)
$CX      = $SIZE / 2.0
$CZ2     = $SIZE / 2.0

for ($iz = 0; $iz -lt $SIZE; $iz++) {
    for ($ix = 0; $ix -lt $SIZE; $ix++) {
        $dx   = ($ix - $CX) / $CX
        $dz   = ($iz - $CZ2) / $CZ2
        $dist = [Math]::Sqrt($dx * $dx + $dz * $dz)

        $valleBase  = 0.45
        $montanaMax = 0.95
        $perfil     = $valleBase + ($montanaMax - $valleBase) * [Math]::Pow($dist, 1.4)

        $noiseX = [Math]::Sin($ix * 0.03 + 1.7) * [Math]::Cos($iz * 0.05) * 0.015
        $noiseZ = [Math]::Cos($ix * 0.07) * [Math]::Sin($iz * 0.04 + 0.9) * 0.012
        $noiseF = [Math]::Sin($ix * 0.15 + $iz * 0.12) * 0.006

        $rioX       = $CX + 20
        $distRio    = [Math]::Abs($ix - $rioX) / ($SIZE * 0.3)
        $depRio     = [Math]::Max(0.0, 1.0 - $distRio * 3.0) * 0.02

        $h = [Math]::Max(0.0, [Math]::Min(1.0, $perfil + $noiseX + $noiseZ + $noiseF - $depRio))
        $heights[$iz * $SIZE + $ix] = $h
    }
}

$rawPath = Join-Path $DATA "dem_unity_1025.raw"
$stream  = [System.IO.File]::Open($rawPath, [System.IO.FileMode]::Create)
$writer  = New-Object System.IO.BinaryWriter($stream)
foreach ($h in $heights) {
    $val = [uint16]([Math]::Round($h * 65535))
    $writer.Write([byte](($val -shr 8) -band 0xFF))
    $writer.Write([byte]($val -band 0xFF))
}
$writer.Close(); $stream.Close()
$sizeMB = [Math]::Round((Get-Item $rawPath).Length / 1MB, 2)
Write-Host "  OK: DEM generado ($sizeMB MB)" -ForegroundColor Green

# ============================================================
# 4. ARBOLES OSM
# ============================================================
Write-Host "[4/5] Arboles OSM..." -ForegroundColor Yellow

$qArboles = "[out:json][timeout:45];(node[natural=tree]($S,$W,$N,$E2);way[natural=wood]($S,$W,$N,$E2););out geom;"

try {
    $body = "data=" + [Uri]::EscapeDataString($qArboles)
    $resp = Invoke-RestMethod -Uri $URL_OVERPASS -Method Post -Body $body `
        -ContentType "application/x-www-form-urlencoded" -TimeoutSec 60

    $arboles = @()
    foreach ($elem in $resp.elements) {
        if ($elem.type -ne "node") { continue }
        $x = ($elem.lon - $LON0) * $ESCALA * [Math]::Cos($LAT0 * [Math]::PI / 180.0)
        $z = ($elem.lat - $LAT0) * $ESCALA
        $sp = if ($elem.tags.species) { $elem.tags.species } elseif ($elem.tags.genus) { $elem.tags.genus } else { "Quercus" }
        $arboles += @{ x=[Math]::Round($x,1); z=[Math]::Round($z,1); especie=$sp; radio=3.5 }
    }
    $arboles | ConvertTo-Json -Compress | Set-Content "$DATA\trees_unity.json" -Encoding UTF8
    Write-Host "  OK: $($arboles.Count) arboles -> trees_unity.json" -ForegroundColor Green
}
catch {
    Write-Host "  WARN arboles: $_" -ForegroundColor Yellow
}

# ============================================================
# 5. AUDIO CC0 (Kenney.nl)
# ============================================================
Write-Host "[5/5] Audio CC0 Kenney.nl..." -ForegroundColor Yellow

$audioTmp = Join-Path $BASE "AudioTmp"
New-Item -ItemType Directory -Force -Path $audioTmp | Out-Null

$packs = @(
    @{ url="https://kenney.nl/content/assets/impact-sounds.zip";    id="impact"    }
    @{ url="https://kenney.nl/content/assets/interface-sounds.zip"; id="interface" }
    @{ url="https://kenney.nl/content/assets/sci-fi-sounds.zip";    id="scifi"     }
)

$total = 0
foreach ($pack in $packs) {
    $zipFile = Join-Path $audioTmp "$($pack.id).zip"
    try {
        $wc = New-Object System.Net.WebClient
        $wc.Headers.Add("User-Agent", "Mozilla/5.0")
        $wc.DownloadFile($pack.url, $zipFile)

        $extractDir = Join-Path $audioTmp $pack.id
        Expand-Archive -Path $zipFile -DestinationPath $extractDir -Force

        $clips = Get-ChildItem -Path $extractDir -Recurse -Include "*.ogg","*.wav" | Select-Object -First 60
        foreach ($clip in $clips) {
            Copy-Item $clip.FullName (Join-Path $AUDIO $clip.Name) -Force
            $total++
        }
        Write-Host "  OK $($pack.id): $($clips.Count) clips" -ForegroundColor Green
    }
    catch {
        Write-Host "  WARN $($pack.id): $_" -ForegroundColor Yellow
    }
}
Remove-Item $audioTmp -Recurse -Force -ErrorAction SilentlyContinue

if ($total -eq 0) {
    Write-Host "  INFO: Sin audio descargado. AudioManager usara sintesis procedural." -ForegroundColor Yellow
} else {
    Write-Host "  OK: $total clips de audio descargados" -ForegroundColor Green
}

# ============================================================
# RESUMEN
# ============================================================
Write-Host ""
Write-Host "=== RESULTADO ===" -ForegroundColor Cyan
$archivos = @("buildings_unity.json","roads_unity.json","dem_unity_1025.raw","trees_unity.json")
foreach ($f in $archivos) {
    $ruta = Join-Path $DATA $f
    if (Test-Path $ruta) {
        $kb = [Math]::Round((Get-Item $ruta).Length / 1KB)
        Write-Host "  OK $f ($kb KB)" -ForegroundColor Green
    } else {
        Write-Host "  FALTA $f" -ForegroundColor Red
    }
}
$nAudio = (Get-ChildItem $AUDIO -Include "*.wav","*.ogg","*.mp3" -Recurse -ErrorAction SilentlyContinue | Measure-Object).Count
Write-Host "  Audio clips: $nAudio"
Write-Host ""
Write-Host "Abre Unity y ejecuta: Tools -> Alsasua -> Construir TODO el proyecto" -ForegroundColor Cyan
Write-Host "===================================" -ForegroundColor Cyan
