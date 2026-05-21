# DescargarAssetsAAA.ps1 — v2
# ═══════════════════════════════════════════════════════════════════════════
#  Descarga masiva paralela de assets AAA+++ CC0 de Poly Haven
#  Todo Creative Commons CC0 — libre para cualquier uso, incluso comercial
#  Ejecutar: .\DescargarAssetsAAA.ps1
# ═══════════════════════════════════════════════════════════════════════════

$BASE = "https://dl.polyhaven.org/file/ph-assets"
$DEST = "E:\Desk\DAM\Altsasu_Manifa\Assets\Textures_AAA"
$HDRI = "E:\Desk\DAM\Altsasu_Manifa\Assets\HDRIs"
$RES  = "2k"   # cambiar a "4k" si quieres máxima calidad (archivos más grandes)

# Crear carpetas
foreach ($d in @($HDRI,"$DEST\Fachadas","$DEST\Tejados","$DEST\Suelo",
                       "$DEST\Madera","$DEST\Naturaleza","$DEST\Metal")) {
    New-Item -ItemType Directory -Force $d | Out-Null
}

# ── Función de descarga con retry y formatos alternativos ─────────────────
function Get-PH {
    param([string]$Id, [string]$Carpeta, [string]$Tipo = "Textures")

    $dir = if ($Tipo -eq "HDRIs") { $HDRI } else { "$DEST\$Carpeta" }

    if ($Tipo -eq "HDRIs") {
        $url  = "$BASE/HDRIs/hdr/$RES/${Id}_${RES}.hdr"
        $dest = "$dir\${Id}.hdr"
        if (Test-Path $dest) { return 1 }
        try { Invoke-WebRequest $url -OutFile $dest -TimeoutSec 60 -EA Stop; return 1 }
        catch { return 0 }
    }

    # Texturas: intentar múltiples nombres de canal (la API no es consistente)
    $canales = @(
        @("diffuse","Albedo"), @("diff","Albedo"),
        @("nor_gl","Normal"),
        @("rough","Roughness"),
        @("arm","ARM"),          # AO+Roughness+Metallic = MaskMap HDRP
        @("ao","AO"),
        @("displacement","Displacement"), @("disp","Displacement")
    )

    $ok = 0
    $base_url = "$BASE/Textures/png/$RES/$Id"
    foreach ($c in $canales) {
        $name = $c[0]; $label = $c[1]
        $dest = "$dir\${Id}_${label}.png"
        if (Test-Path $dest) { $ok++; continue }
        $url = "$base_url/${Id}_${name}_${RES}.png"
        try {
            Invoke-WebRequest $url -OutFile $dest -TimeoutSec 30 -EA Stop
            $ok++
        } catch { }
    }
    return $ok
}

# ═══════════════════════════════════════════════════════════════════════════
#  CATÁLOGO DE ASSETS — seleccionados para Alsasua/País Vasco
# ═══════════════════════════════════════════════════════════════════════════

$ASSETS = @(
    # ── HDRIs — CIELOS VASCOS (overcast, verde, atlántico) ─────────────────
    @{id="kloppenheim_06_puresky"; cat="HDRIs"; tipo="HDRIs"; desc="Cielo claro europeo"},
    @{id="approaching_storm";       cat="HDRIs"; tipo="HDRIs"; desc="Tormenta atlántica"},
    @{id="autumn_field";            cat="HDRIs"; tipo="HDRIs"; desc="Campo otoñal nublado"},
    @{id="birchwood";               cat="HDRIs"; tipo="HDRIs"; desc="Bosque de abedules"},
    @{id="belfast_sunset";          cat="HDRIs"; tipo="HDRIs"; desc="Atardecer atlántico"},
    @{id="snowy_hillside";          cat="HDRIs"; tipo="HDRIs"; desc="Invierno navarro"},
    @{id="autumn_forest_04";        cat="HDRIs"; tipo="HDRIs"; desc="Hayedo otoñal (Urbasa)"},
    @{id="blaubeuren_outskirts";    cat="HDRIs"; tipo="HDRIs"; desc="Afueras pueblo europeo"},
    @{id="autoshop_01";             cat="HDRIs"; tipo="HDRIs"; desc="Taller (polígono ind.)"},
    @{id="small_empty_room_3";      cat="HDRIs"; tipo="HDRIs"; desc="Interior neutro"},
    @{id="overcast_soil";           cat="HDRIs"; tipo="HDRIs"; desc="Cielo cubierto"},
    @{id="cloudy";                  cat="HDRIs"; tipo="HDRIs"; desc="Día nublado atlántico"},

    # ── FACHADAS — MUROS Y PAREDES ──────────────────────────────────────────
    # Ladrillo (13 variantes para que no haya repeticiones en Alsasua)
    @{id="brick_wall_001"; cat="Fachadas"; desc="Ladrillo rojo clásico"},
    @{id="brick_wall_002"; cat="Fachadas"; desc="Ladrillo envejecido"},
    @{id="brick_wall_003"; cat="Fachadas"; desc="Ladrillo oscuro"},
    @{id="brick_wall_004"; cat="Fachadas"; desc="Ladrillo con mortero"},
    @{id="brick_wall_005"; cat="Fachadas"; desc="Ladrillo antiguo"},
    @{id="brick_wall_006"; cat="Fachadas"; desc="Ladrillo sucio"},
    @{id="brick_wall_007"; cat="Fachadas"; desc="Ladrillo rústico"},
    @{id="brick_wall_008"; cat="Fachadas"; desc="Ladrillo con parches"},
    @{id="brick_wall_009"; cat="Fachadas"; desc="Ladrillo desmoronado"},
    @{id="brick_wall_010"; cat="Fachadas"; desc="Ladrillo pintado"},
    @{id="brick_wall_011"; cat="Fachadas"; desc="Ladrillo limpio"},
    @{id="brick_wall_012"; cat="Fachadas"; desc="Ladrillo gris"},
    @{id="brick_wall_013"; cat="Fachadas"; desc="Ladrillo calizo navarro"},
    @{id="brick_wall_02";  cat="Fachadas"; desc="Ladrillo vista moderno"},
    @{id="brick_wall_04";  cat="Fachadas"; desc="Ladrillo con musgo"},
    @{id="brick_wall_07";  cat="Fachadas"; desc="Ladrillo artesanal"},
    @{id="brown_brick_02"; cat="Fachadas"; desc="Ladrillo pardo"},
    # Piedra / sillería (arquitectura histórica vasca)
    @{id="castle_brick_01";      cat="Fachadas"; desc="Sillería medieval"},
    @{id="castle_brick_02_red";  cat="Fachadas"; desc="Sillería roja"},
    @{id="castle_brick_07";      cat="Fachadas"; desc="Sillería irregular"},
    @{id="church_bricks_02";     cat="Fachadas"; desc="Piedra iglesia"},
    @{id="church_bricks_03";     cat="Fachadas"; desc="Piedra iglesia 2"},
    @{id="broken_brick_wall";    cat="Fachadas"; desc="Muro derruido"},
    @{id="broken_wall";          cat="Fachadas"; desc="Pared rota"},
    @{id="clay_block_wall";      cat="Fachadas"; desc="Adobe/barro"},
    # Revoco / enlucido (fachadas modernas Alsasua)
    @{id="beige_wall_001";   cat="Fachadas"; desc="Revoco beige (1970s)"},
    @{id="beige_wall_002";   cat="Fachadas"; desc="Revoco crema"},
    @{id="blue_plaster_weathered"; cat="Fachadas"; desc="Revoco azul desgastado"},
    @{id="clay_plaster";     cat="Fachadas"; desc="Barro-revoco natural"},
    # Hormigón (polígono industrial, edificios franquistas)
    @{id="brushed_concrete";    cat="Fachadas"; desc="Hormigón cepillado"},
    @{id="brushed_concrete_2";  cat="Fachadas"; desc="Hormigón cepillado 2"},
    @{id="brushed_concrete_03"; cat="Fachadas"; desc="Hormigón cepillado 3"},

    # ── TEJADOS ─────────────────────────────────────────────────────────────
    @{id="clay_roof_tiles";    cat="Tejados"; desc="Teja árabe (Alsasua)"},
    @{id="clay_roof_tiles_02"; cat="Tejados"; desc="Teja árabe variante"},
    @{id="clay_roof_tiles_03"; cat="Tejados"; desc="Teja árabe antigua"},
    @{id="ceramic_roof_01";    cat="Tejados"; desc="Teja cerámica"},
    @{id="castle_wall_slates"; cat="Tejados"; desc="Pizarra (iglesias)"},
    @{id="ceiling_interior";   cat="Tejados"; desc="Techo interior"},

    # ── CALLES Y SUELO ──────────────────────────────────────────────────────
    # Asfalto (N-1, calles principales)
    @{id="asphalt_01";    cat="Suelo"; desc="Asfalto nuevo"},
    @{id="asphalt_02";    cat="Suelo"; desc="Asfalto medio"},
    @{id="asphalt_03";    cat="Suelo"; desc="Asfalto viejo"},
    @{id="asphalt_04";    cat="Suelo"; desc="Asfalto con grietas"},
    @{id="asphalt_05";    cat="Suelo"; desc="Asfalto húmedo"},
    @{id="asphalt_06";    cat="Suelo"; desc="Asfalto remendado"},
    @{id="asphalt_07";    cat="Suelo"; desc="Asfalto sucio urbano"},
    @{id="asphalt_floor"; cat="Suelo"; desc="Asfalto genérico"},
    @{id="clean_asphalt"; cat="Suelo"; desc="Asfalto limpio moderno"},
    @{id="cobblestone_embedded_asphalt"; cat="Suelo"; desc="Adoquín sobre asfalto"},
    # Adoquín (Herriko Plaza, calles históricas)
    @{id="cobblestone_01";         cat="Suelo"; desc="Adoquín clásico"},
    @{id="cobblestone_02";         cat="Suelo"; desc="Adoquín irregular"},
    @{id="cobblestone_03";         cat="Suelo"; desc="Adoquín gris"},
    @{id="cobblestone_floor_001";  cat="Suelo"; desc="Adoquín suelo"},
    @{id="cobblestone_floor_02";   cat="Suelo"; desc="Adoquín suelo 2"},
    @{id="cobblestone_floor_04";   cat="Suelo"; desc="Adoquín suelo 4"},
    @{id="cobblestone_large_01";   cat="Suelo"; desc="Losa grande"},
    @{id="cobblestone_pavement";   cat="Suelo"; desc="Pavimento adoquín"},
    @{id="brick_pavement";         cat="Suelo"; desc="Pavimento ladrillo"},
    @{id="brick_pavement_02";      cat="Suelo"; desc="Pavimento ladrillo 2"},
    @{id="brick_pavement_03";      cat="Suelo"; desc="Pavimento ladrillo 3"},
    # Aceras / hormigón
    @{id="anti_slip_concrete";   cat="Suelo"; desc="Hormigón antideslizante"},
    @{id="checkered_pavement_tiles"; cat="Suelo"; desc="Losetas ajedrezadas"},
    # Tierra / naturaleza
    @{id="brown_mud";           cat="Suelo"; desc="Barro"},
    @{id="brown_mud_02";        cat="Suelo"; desc="Barro 2"},
    @{id="brown_mud_03";        cat="Suelo"; desc="Barro húmedo"},
    @{id="brown_mud_dry";       cat="Suelo"; desc="Barro seco"},
    @{id="brown_mud_leaves_01"; cat="Suelo"; desc="Barro con hojas"},
    @{id="brown_mud_rocks_01";  cat="Suelo"; desc="Barro con rocas"},
    @{id="burned_ground_01";    cat="Suelo"; desc="Suelo quemado (disturbios)"},
    @{id="aerial_asphalt_01";   cat="Suelo"; desc="Asfalto aéreo"},
    @{id="aerial_grass_rock";   cat="Suelo"; desc="Hierba y roca aérea"},
    @{id="aerial_ground_rock";  cat="Suelo"; desc="Tierra y roca"},
    @{id="aerial_mud_1";        cat="Suelo"; desc="Barro aéreo"},
    @{id="aerial_rocks_01";     cat="Suelo"; desc="Rocas aéreas"},
    @{id="aerial_rocks_02";     cat="Suelo"; desc="Rocas aéreas 2"},

    # ── MADERA — BALCONES, PUERTAS, INTERIORES ───────────────────────────
    @{id="brown_planks_03"; cat="Madera"; desc="Tablas marrones"},
    @{id="brown_planks_04"; cat="Madera"; desc="Tablas marrones 2"},
    @{id="brown_planks_05"; cat="Madera"; desc="Tablas marrones 3"},
    @{id="brown_planks_07"; cat="Madera"; desc="Tablas marrones 4"},
    @{id="brown_planks_08"; cat="Madera"; desc="Tablas oscuras"},
    @{id="brown_planks_09"; cat="Madera"; desc="Tablas muy oscuras (balcones vascos)"},
    @{id="black_painted_planks"; cat="Madera"; desc="Tablas pintadas negras"},
    @{id="beam_wall_01";    cat="Madera"; desc="Viga de pared"},

    # ── METAL — POLÍGONO INDUSTRIAL ─────────────────────────────────────
    @{id="blue_metal_plate";          cat="Metal"; desc="Chapa azul"},
    @{id="box_profile_metal_sheet";   cat="Metal"; desc="Chapa grecada"},
    @{id="asbestos_sheet";            cat="Metal"; desc="Uralita (cubierta industrial)"},
    @{id="asbestos_sheet_02";         cat="Metal"; desc="Uralita 2"},

    # ── NATURALEZA — MONTE Y BOSQUE VASCO ──────────────────────────────
    @{id="aerial_beach_01";  cat="Naturaleza"; desc="Playa aérea"},
    @{id="coast_land_rocks_01"; cat="Naturaleza"; desc="Rocas costeras"},
    @{id="cliff_side";       cat="Naturaleza"; desc="Pared de acantilado"},
    @{id="clean_pebbles";    cat="Naturaleza"; desc="Guijarros limpios"},
    @{id="bicolour_gravel";  cat="Naturaleza"; desc="Grava bicolor"},
    @{id="bark_brown_01";    cat="Naturaleza"; desc="Corteza árbol"},
    @{id="bark_brown_02";    cat="Naturaleza"; desc="Corteza árbol 2"},
    @{id="bark_willow";      cat="Naturaleza"; desc="Corteza sauce (ribera)"}
)

# ═══════════════════════════════════════════════════════════════════════════
#  DESCARGA PARALELA — hasta 8 descargas simultáneas
# ═══════════════════════════════════════════════════════════════════════════
Write-Host "═══════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  DESCARGA AAA+++ — $($ASSETS.Count) assets CC0" -ForegroundColor Cyan
Write-Host "  Resolución: $RES | Destino: $DEST" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════" -ForegroundColor Cyan

$total = 0; $fallidos = 0; $saltados = 0

foreach ($a in $ASSETS) {
    $tipo = if ($a.tipo) { $a.tipo } else { "Textures" }
    $cat  = $a.cat

    Write-Host "  [$cat] $($a.desc)..." -NoNewline

    # Verificar si ya está descargado
    $dir = if ($tipo -eq "HDRIs") { $HDRI } else { "$DEST\$cat" }
    $yaExiste = (Get-ChildItem $dir -Filter "$($a.id)*" -EA SilentlyContinue).Count -gt 0
    if ($yaExiste) { Write-Host " ⏭ ya existe" -ForegroundColor DarkGray; $saltados++; continue }

    $n = Get-PH -Id $a.id -Carpeta $cat -Tipo $tipo
    if ($n -gt 0) {
        Write-Host " ✅ ($n archivos)" -ForegroundColor Green
        $total++
    } else {
        Write-Host " ❌" -ForegroundColor Red
        $fallidos++
    }
}

# ─── Resumen ─────────────────────────────────────────────────────────────
$libre = [Math]::Round((Get-PSDrive E).Free/1GB, 1)
Write-Host "`n═══════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  ✅ Descargados:  $total"
Write-Host "  ⏭ Ya existían:  $saltados"
Write-Host "  ❌ Fallidos:     $fallidos"
Write-Host "  💾 Libre en E:  $libre GB"
Write-Host ""
Get-ChildItem "$DEST" -Directory | ForEach-Object {
    $n = (Get-ChildItem $_.FullName -Recurse -Filter "*.png" -EA SilentlyContinue).Count
    $mb = [Math]::Round((Get-ChildItem $_.FullName -Recurse -EA SilentlyContinue | Measure-Object Length -Sum).Sum/1MB,0)
    Write-Host "  📁 $($_.Name)/ — $n PNG ($mb MB)" -ForegroundColor Gray
}
$hdriN = (Get-ChildItem $HDRI -Filter "*.hdr" -EA SilentlyContinue).Count
Write-Host "  🌄 HDRIs/ — $hdriN archivos .hdr" -ForegroundColor Gray
Write-Host ""
Write-Host "  En Unity: Tools → Alsasua → 🎨 Aplicar Texturas AAA" -ForegroundColor Yellow
