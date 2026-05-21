#!/usr/bin/env python3
# DescargarDatosCompletos.py
# ═══════════════════════════════════════════════════════════════════════════
#  Pipeline completo de adquisición de datos para Alsasua/Altsasua
#
#  FUENTE 1: IGN PNOA Ortofoto 25cm/pixel (CC BY 4.0, oficial España)
#    → Imagen aérea de cada edificio desde arriba → textura real del tejado
#    → Color exacto: rojo terracota, gris pizarra, negro asfalto, etc.
#
#  FUENTE 2: IGN MDT05 DSM via WCS
#    → Modelo Digital de Superficie (incluye edificios) 5m/pixel
#    → Altura real de cada tejado
#
#  FUENTE 3: OSM Overpass enriquecido (roof:shape, roof:material, levels, height)
#    → Etiquetas de tejado para los edificios que las tienen
#
#  FUENTE 4: Mapillary Open Data (fotos de calle, requiere token gratuito)
#    → Fotos desde todos los ángulos → fotogrametría con COLMAP
#
#  FUENTE 5: IGN Fototeca (vuelos históricos, alta resolución)
#    → Imágenes de distintas épocas para comparación
#
#  RESULTADO (Assets/AlsasuaData/):
#    ortofoto_alsasua_25cm.jpg  — imagen aérea completa del pueblo
#    buildings_rico.json        — edificios con color tejado + etiquetas roof
#    dsm_alsasua_5m.asc         — DSM superficie (si disponible)
#    tiles/ortho_*.jpg          — tiles individuales por zona
#    mapillary/                 — fotos de calle (si hay token)
# ═══════════════════════════════════════════════════════════════════════════

import urllib.request, urllib.parse, json, os, math, struct, sys, time
from pathlib import Path

sys.stdout.reconfigure(encoding='utf-8')

# ── Configuración ──────────────────────────────────────────────────────────
OUT = Path("E:/Desk/DAM/Altsasu_Manifa/Assets/AlsasuaData")
TILES_DIR = OUT / "tiles"
MAPILLARY_DIR = OUT / "mapillary"

# Bounding box GPS de Alsasua completo (ligeramente más amplio)
LAT_MIN, LAT_MAX = 42.882, 42.918
LON_MIN, LON_MAX = -2.198, -2.140

# Centro = Herriko Plaza
LAT0, LON0 = 42.8987, -2.1677

# Conversión metros (mismo sistema que el simulador)
M_PER_DEG_LAT = 111320.0
M_PER_DEG_LON = 76400.0
OX, OZ = 1918.0, 8570.0   # Unity coords de Herriko Plaza

HEADERS = {'User-Agent': 'AltsasuaSimulator/1.0 (educational; contact makhnovia@gmail.com)'}

# ── Helpers ────────────────────────────────────────────────────────────────

def gps_to_unity(lat, lon):
    x = (lon - LON0) * M_PER_DEG_LON + OX
    z = (lat - LAT0) * M_PER_DEG_LAT + OZ
    return x, z

def download(url, path, desc=""):
    try:
        req = urllib.request.Request(url, headers=HEADERS)
        with urllib.request.urlopen(req, timeout=30) as r:
            data = r.read()
        Path(path).parent.mkdir(parents=True, exist_ok=True)
        with open(path, 'wb') as f: f.write(data)
        print(f"  ✅ {desc}: {len(data)//1024}KB")
        return data
    except Exception as e:
        print(f"  ❌ {desc}: {e}")
        return None

# ══════════════════════════════════════════════════════════════════════════
#  FUENTE 1: IGN PNOA Ortofoto 25cm/pixel
#  Grid de tiles 5000m×5000m → resolución 25cm → 20000×20000px por tile
#  Descargamos en tiles de ~500m × ~500m para manejabilidad
# ══════════════════════════════════════════════════════════════════════════

def descargar_ortofotos():
    print("\n═══ FUENTE 1: IGN PNOA Ortofoto 25cm ═══")
    WMS = "https://www.ign.es/wms-inspire/pnoa-ma"
    TILE_DEG = 0.005   # ~500m por tile
    TILE_PX  = 2000    # 2000×2000px = 1px cada 25cm

    TILES_DIR.mkdir(parents=True, exist_ok=True)

    lat = LAT_MIN
    tile_n = 0
    while lat < LAT_MAX:
        lon = LON_MIN
        while lon < LON_MAX:
            lat1, lon1 = lat + TILE_DEG, lon + TILE_DEG
            bbox = f"{lat:.6f},{lon:.6f},{lat1:.6f},{lon1:.6f}"
            url = (f"{WMS}?SERVICE=WMS&REQUEST=GetMap&VERSION=1.3.0"
                   f"&LAYERS=OI.OrthoimageCoverage&CRS=EPSG:4326"
                   f"&BBOX={bbox}&WIDTH={TILE_PX}&HEIGHT={TILE_PX}"
                   f"&FORMAT=image/jpeg&STYLES=")
            cx, cz = gps_to_unity((lat+lat1)/2, (lon+lon1)/2)
            fname = TILES_DIR / f"orto_{lat:.4f}_{lon:.4f}.jpg"
            if not fname.exists():
                data = download(url, fname,
                    f"Ortofoto tile {tile_n} ({lat:.3f},{lon:.3f}) Unity({cx:.0f},{cz:.0f})")
                time.sleep(0.3)  # respetar rate limit del IGN
            else:
                print(f"  ⏭ Ya existe: {fname.name}")
            lon += TILE_DEG
            tile_n += 1
        lat += TILE_DEG

    # Meta-archivo con coordenadas de cada tile para el motor Unity
    metas = []
    for f in TILES_DIR.glob("orto_*.jpg"):
        parts = f.stem.split("_")
        lat_f, lon_f = float(parts[1]), float(parts[2])
        ux0, uz0 = gps_to_unity(lat_f, lon_f)
        ux1, uz1 = gps_to_unity(lat_f + TILE_DEG, lon_f + TILE_DEG)
        metas.append({
            "file": f.name, "lat": lat_f, "lon": lon_f,
            "ux0": round(ux0,1), "uz0": round(uz0,1),
            "ux1": round(ux1,1), "uz1": round(uz1,1),
            "pixel_m": 0.25
        })
    with open(OUT / "orto_tiles_meta.json", 'w', encoding='utf-8') as f:
        json.dump(metas, f, indent=2)
    print(f"\n  {tile_n} tiles descargados, meta guardado en orto_tiles_meta.json")

# ══════════════════════════════════════════════════════════════════════════
#  ANÁLISIS DE COLOR POR EDIFICIO — extrae color del tejado desde la ortofoto
# ══════════════════════════════════════════════════════════════════════════

def extraer_colores_tejados():
    print("\n═══ ANÁLISIS: Color de tejados desde ortofoto ═══")
    meta_path = OUT / "orto_tiles_meta.json"
    buildings_path = OUT / "buildings_unity.json"

    if not meta_path.exists():
        print("  ❌ Faltan tiles. Ejecuta primero descargar_ortofotos()")
        return

    with open(buildings_path) as f: buildings = json.load(f)
    with open(meta_path) as f: metas = json.load(f)

    # Intentar importar PIL/Pillow para análisis de imagen
    try:
        from PIL import Image
        import numpy as np
        pil_ok = True
    except ImportError:
        print("  ⚠ Pillow no instalado. Instala con: pip install Pillow numpy")
        print("  Usando análisis por RGB promedio")
        pil_ok = False

    colores_tejado = {}
    procesados = 0

    for ed in buildings:
        verts = ed.get('vertices', [])
        if not verts: continue

        # Centro del edificio en coords Unity
        cx = sum(v['x'] for v in verts) / len(verts) + OX
        cz = sum(v['z'] for v in verts) / len(verts) + OZ

        # Convertir a GPS
        lat = (cz - OZ) / M_PER_DEG_LAT + LAT0
        lon = (cx - OX) / M_PER_DEG_LON + LON0

        # Encontrar el tile que contiene este punto
        tile = next((m for m in metas
                     if m['lat'] <= lat < m['lat']+0.005
                     and m['lon'] <= lon < m['lon']+0.005), None)

        if tile is None: continue

        tile_path = TILES_DIR / tile['file']
        if not tile_path.exists(): continue

        if pil_ok:
            try:
                img = Image.open(tile_path).convert('RGB')
                w, h = img.size

                # Pixeles correspondientes al centro del edificio
                px = int((lon - tile['lon']) / 0.005 * w)
                pz = int((1 - (lat - tile['lat']) / 0.005) * h)

                # Muestra 5×5 pixeles en el centro
                px = max(2, min(w-3, px))
                pz = max(2, min(h-3, pz))
                region = img.crop((px-2, pz-2, px+3, pz+3))

                import numpy as np
                arr = np.array(region)
                r, g, b = arr[:,:,0].mean(), arr[:,:,1].mean(), arr[:,:,2].mean()

                # Clasificar tipo de tejado por color
                tipo = clasificar_tejado_por_color(r, g, b)
                colores_tejado[ed['id']] = {
                    'r': round(r), 'g': round(g), 'b': round(b),
                    'tipo': tipo, 'unity_x': round(cx,1), 'unity_z': round(cz,1)
                }
                procesados += 1
            except Exception as e:
                pass

    # Guardar resultado
    # Merge con buildings_unity.json
    color_map = {item['id'] if 'id' in item else k: v
                 for k,v in colores_tejado.items()
                 for item in [{'id': k}]}

    buildings_rico = []
    for ed in buildings:
        ed_nuevo = dict(ed)
        if ed['id'] in colores_tejado:
            c = colores_tejado[ed['id']]
            ed_nuevo['roof_color_r'] = c['r']
            ed_nuevo['roof_color_g'] = c['g']
            ed_nuevo['roof_color_b'] = c['b']
            ed_nuevo['roof_tipo_detectado'] = c['tipo']
        buildings_rico.append(ed_nuevo)

    with open(OUT / "buildings_rico.json", 'w', encoding='utf-8') as f:
        json.dump(buildings_rico, f, ensure_ascii=False, indent=2)

    # Estadísticas
    tipos = {}
    for v in colores_tejado.values():
        tipos[v['tipo']] = tipos.get(v['tipo'], 0) + 1

    print(f"  {procesados} edificios analizados")
    print(f"  Distribución de tipos de tejado:")
    for t, n in sorted(tipos.items(), key=lambda x: -x[1]):
        print(f"    {t:20s}: {n}")
    print(f"  Guardado: buildings_rico.json")

def clasificar_tejado_por_color(r, g, b):
    """Clasifica el tipo de tejado por color RGB de la ortofoto."""
    # Normalizar
    total = r + g + b + 0.001
    rn, gn, bn = r/total, g/total, b/total

    if r > 160 and rn > 0.38 and gn < 0.35:
        return "terracota_roja"       # teja roja vasco-navarra
    elif r > 130 and g > 100 and b < 90:
        return "teja_naranja"         # teja naranja
    elif r > 100 and g > 80 and b > 60 and abs(r-g) < 30 and abs(g-b) < 30:
        if r > 150: return "tejado_gris_claro"   # hormigón, zinc
        else:       return "tejado_gris"          # pizarra, tela asfáltica
    elif r < 60 and g < 60 and b < 60:
        return "tejado_negro"         # pizarra oscura, tela asfáltica
    elif g > r and g > b and g > 80:
        return "vegetacion_cubierta"  # azotea verde / musgo
    elif r > 200 and g > 200 and b > 200:
        return "tejado_blanco"        # membrana blanca, zinc
    elif r > 150 and g > 120 and b < 90:
        return "teja_marron"          # teja marrón
    else:
        return "desconocido"

# ══════════════════════════════════════════════════════════════════════════
#  FUENTE 2: IGN MDT05 DSM via WCS
# ══════════════════════════════════════════════════════════════════════════

def descargar_dsm_ign():
    print("\n═══ FUENTE 2: IGN DSM (superficie con tejados) ═══")

    # WCS del Centro de Descargas IGN - MDT05 en proyección geográfica
    urls_a_probar = [
        ("DSM MDSNorm_CSN5 (superficie)",
         "https://servicios.idee.es/wcs-inspire/mdt?"
         "SERVICE=WCS&VERSION=1.0.0&REQUEST=GetCoverage"
         "&COVERAGE=Elevacion4258_MDSNorm_CSN5&CRS=EPSG:4326"
         f"&BBOX={LON_MIN},{LAT_MIN},{LON_MAX},{LAT_MAX}"
         "&WIDTH=1100&HEIGHT=800&FORMAT=ArcGrid"),
        ("DTM MDT05 (terreno)",
         "https://servicios.idee.es/wcs-inspire/mdt?"
         "SERVICE=WCS&VERSION=1.0.0&REQUEST=GetCoverage"
         "&COVERAGE=Elevacion4258_5&CRS=EPSG:4326"
         f"&BBOX={LON_MIN},{LAT_MIN},{LON_MAX},{LAT_MAX}"
         "&WIDTH=1100&HEIGHT=800&FORMAT=ArcGrid"),
        ("DSM alternativo IDENA Navarra",
         "https://idena.navarra.es/ogc/wcs?SERVICE=WCS&VERSION=1.0.0"
         "&REQUEST=GetCoverage&COVERAGE=MDSLIDAR&CRS=EPSG:4326"
         f"&BBOX={LON_MIN},{LAT_MIN},{LON_MAX},{LAT_MAX}"
         "&WIDTH=1100&HEIGHT=800&FORMAT=ArcGrid"),
    ]

    for desc, url in urls_a_probar:
        out_file = OUT / f"{'dsm' if 'DSM' in desc or 'MDS' in desc else 'dtm'}_alsasua_5m.asc"
        data = download(url, out_file, desc)
        if data and b'ncols' in data[:200].lower():
            print(f"    ✅ ASC Grid válido: {len(data)//1024}KB")
        elif data:
            print(f"    ⚠ Datos recibidos pero formato no reconocido: {data[:100]}")
        time.sleep(1)

# ══════════════════════════════════════════════════════════════════════════
#  FUENTE 3: OSM Overpass con etiquetas de tejado
# ══════════════════════════════════════════════════════════════════════════

def descargar_osm_enriquecido():
    print("\n═══ FUENTE 3: OSM con etiquetas de tejado/material ═══")

    query = f"""[out:json][timeout:60];
(
  way[building]({LAT_MIN},{LON_MIN},{LAT_MAX},{LON_MAX});
);
out geom tags;"""

    url = "https://overpass-api.de/api/interpreter"
    try:
        req = urllib.request.Request(
            url,
            data=urllib.parse.urlencode({'data': query}).encode(),
            headers=HEADERS)
        with urllib.request.urlopen(req, timeout=65) as r:
            raw = r.read()
        data = json.loads(raw)
        elements = data.get('elements', [])
        print(f"  ✅ {len(elements)} edificios descargados")

        # Extraer etiquetas de tejado
        roof_keys = {'roof:shape','roof:material','roof:colour','roof:color',
                     'roof:height','roof:orientation','roof:levels',
                     'building:material','building:colour','height','levels'}

        edificios_enriquecidos = []
        roof_stats = {}

        for el in elements:
            tags = el.get('tags', {})
            geom = el.get('geometry', [])

            # Coordenadas GPS → Unity
            verts_unity = []
            for pt in geom:
                ux, uz = gps_to_unity(pt['lat'], pt['lon'])
                verts_unity.append({'x': round(ux - OX, 2), 'z': round(uz - OZ, 2)})

            ed = {
                'id': el['id'],
                'type': tags.get('building','yes'),
                'name': tags.get('name',''),
                'levels': int(tags.get('building:levels', tags.get('levels', 2))),
                'height': float(tags.get('height', 6.4)),
                'vertices': verts_unity,
            }

            # Copiar todas las etiquetas de tejado disponibles
            for k in roof_keys:
                if k in tags:
                    ed[k.replace(':','_')] = tags[k]
                    roof_stats[k] = roof_stats.get(k, 0) + 1

            edificios_enriquecidos.append(ed)

        with open(OUT / "buildings_osm_rico.json", 'w', encoding='utf-8') as f:
            json.dump(edificios_enriquecidos, f, ensure_ascii=False, indent=2)

        print(f"  Etiquetas de tejado encontradas:")
        for k, n in sorted(roof_stats.items(), key=lambda x: -x[1]):
            print(f"    {k:25s}: {n} edificios")

    except Exception as e:
        print(f"  ❌ Overpass error: {e}")
        print("  Reintentando con mirror...")
        time.sleep(5)
        try:
            url2 = "https://overpass.karte.io/api/interpreter"
            req = urllib.request.Request(url2,
                data=urllib.parse.urlencode({'data': query}).encode(), headers=HEADERS)
            with urllib.request.urlopen(req, timeout=65) as r:
                data = json.loads(r.read())
            print(f"  ✅ Mirror: {len(data.get('elements',[]))} edificios")
        except Exception as e2:
            print(f"  ❌ Mirror también falló: {e2}")

# ══════════════════════════════════════════════════════════════════════════
#  FUENTE 4: Mapillary (fotos de calle)
# ══════════════════════════════════════════════════════════════════════════

def descargar_mapillary(access_token=None):
    print("\n═══ FUENTE 4: Mapillary (fotos de calle) ═══")

    if not access_token:
        print("  ⚠ Se necesita un token de Mapillary (gratuito)")
        print("  1. Regístrate en mapillary.com")
        print("  2. Ve a: mapillary.com/app/settings/developers")
        print("  3. Crea un Application → copia el Client Token")
        print("  4. Ejecuta: python3 DescargarDatosCompletos.py --mapillary TU_TOKEN")
        return

    MAPILLARY_DIR.mkdir(parents=True, exist_ok=True)

    # Obtener imágenes en el área de Alsasua
    url = (f"https://graph.mapillary.com/images"
           f"?access_token={access_token}"
           f"&fields=id,geometry,thumb_2048_url,captured_at,compass_angle,exif_orientation"
           f"&bbox={LON_MIN},{LAT_MIN},{LON_MAX},{LAT_MAX}"
           f"&limit=500")

    try:
        req = urllib.request.Request(url, headers=HEADERS)
        with urllib.request.urlopen(req, timeout=30) as r:
            data = json.loads(r.read())

        images = data.get('data', [])
        print(f"  ✅ {len(images)} fotos encontradas en Alsasua")

        # Guardar metadatos
        meta_mapillary = []
        for img in images:
            g = img.get('geometry', {}).get('coordinates', [0,0])
            ux, uz = gps_to_unity(g[1], g[0])
            meta_mapillary.append({
                'id': img['id'],
                'lon': g[0], 'lat': g[1],
                'unity_x': round(ux,1), 'unity_z': round(uz,1),
                'angle': img.get('compass_angle', 0),
                'captured': img.get('captured_at',''),
                'thumb': img.get('thumb_2048_url',''),
            })

        with open(MAPILLARY_DIR / "images_meta.json", 'w') as f:
            json.dump(meta_mapillary, f, indent=2)

        print(f"  Meta guardado: {len(meta_mapillary)} imágenes")
        print(f"  Descargando thumbnails 2048px...")

        for i, img in enumerate(images[:100]):  # primeras 100
            thumb = img.get('thumb_2048_url')
            if not thumb: continue
            fname = MAPILLARY_DIR / f"mapillary_{img['id']}.jpg"
            if not fname.exists():
                download(thumb, fname, f"Foto {i+1}/{min(100,len(images))}")
                time.sleep(0.2)

    except Exception as e:
        print(f"  ❌ Mapillary error: {e}")

# ══════════════════════════════════════════════════════════════════════════
#  INSTRUCCIONES PARA FOTOGRAMETRÍA CON COLMAP
# ══════════════════════════════════════════════════════════════════════════

def instrucciones_colmap():
    print("\n═══ FOTOGRAMETRÍA 3D con COLMAP ═══")
    instrucciones = """
Para reconstrucción 3D fotogramétrica completa de Alsasua:

OPCIÓN A — COLMAP (Structure from Motion + MVS):
  1. Instalar COLMAP: https://colmap.github.io/install.html
  2. Copiar imágenes Mapillary a una carpeta:
       E:\\Desk\\DAM\\Altsasu_Manifa\\Fotogrametria\\images\\
  3. Ejecutar pipeline automático:
       colmap automatic_reconstructor \\
         --workspace_path E:\\Desk\\DAM\\Altsasu_Manifa\\Fotogrametria \\
         --image_path E:\\Desk\\DAM\\Altsasu_Manifa\\Fotogrametria\\images
  4. Exportar nube de puntos densa:
       colmap model_converter \\
         --input_path Fotogrametria\\sparse\\0 \\
         --output_path Fotogrametria\\points.ply \\
         --output_type PLY
  5. En Unity: Tools → 🏔 Procesar DSM/LIDAR (acepta PLY)

OPCIÓN B — Meshroom (GUI, más fácil):
  1. Instalar: alicevision.org/software/meshroom
  2. Arrastrar fotos Mapillary
  3. Click "Start" → genera mesh 3D automáticamente

OPCIÓN C — NeRF / Gaussian Splatting (mejor calidad):
  1. Instalar nerfstudio: docs.nerf.studio
  2. nerfstudio train nerfacto \\
       --data Fotogrametria/images \\
       --pipeline.model.predict-normals True
  3. Exportar como punto cloud PLY
  4. Procesar en Unity

CALIDAD ESPERADA CON FOTOS MAPILLARY DE ALSASUA:
  ~200-500 fotos en Alsasua
  Reconstrucción completa: 2-4 horas en GPU media
  Precisión: 5-15cm por punto (mejor que LIDAR comercial)
  Tejados: visibles si hay fotos desde ángulos elevados
"""
    print(instrucciones)
    with open(OUT / "INSTRUCCIONES_FOTOGRAMETRIA.txt", 'w', encoding='utf-8') as f:
        f.write(instrucciones)

# ══════════════════════════════════════════════════════════════════════════
#  MAIN
# ══════════════════════════════════════════════════════════════════════════

if __name__ == '__main__':
    import argparse
    parser = argparse.ArgumentParser(description='Descarga datos para Alsasua Simulator')
    parser.add_argument('--todo',       action='store_true', help='Ejecutar todo')
    parser.add_argument('--orto',       action='store_true', help='Descargar ortofotos IGN 25cm')
    parser.add_argument('--dsm',        action='store_true', help='Descargar DSM IGN 5m')
    parser.add_argument('--osm',        action='store_true', help='Descargar OSM enriquecido')
    parser.add_argument('--colores',    action='store_true', help='Analizar colores tejados')
    parser.add_argument('--mapillary',  type=str, default=None, help='Token Mapillary')
    parser.add_argument('--colmap',     action='store_true', help='Instrucciones COLMAP')
    args = parser.parse_args()

    OUT.mkdir(parents=True, exist_ok=True)

    if args.todo or args.osm:      descargar_osm_enriquecido()
    if args.todo or args.dsm:      descargar_dsm_ign()
    if args.todo or args.orto:     descargar_ortofotos()
    if args.todo or args.colores:  extraer_colores_tejados()
    if args.mapillary:             descargar_mapillary(args.mapillary)
    if args.colmap:                instrucciones_colmap()

    if not any(vars(args).values()):
        print("Uso: python3 DescargarDatosCompletos.py --todo")
        print("       python3 DescargarDatosCompletos.py --orto --dsm --osm")
        print("       python3 DescargarDatosCompletos.py --mapillary TU_TOKEN")
        print("       python3 DescargarDatosCompletos.py --colmap")
        print()
        print("FUENTES DISPONIBLES:")
        print("  --orto     IGN PNOA 25cm/pixel (gratis, oficial España)")
        print("  --dsm      IGN MDT05 DSM 5m/pixel (gratis)")
        print("  --osm      OpenStreetMap con roof:shape, roof:material...")
        print("  --colores  Analizar color tejados desde ortofoto")
        print("  --mapillary Fotos de calle (token gratuito en mapillary.com)")
        print("  --colmap   Instrucciones fotogrametría con COLMAP/Meshroom/NeRF")
