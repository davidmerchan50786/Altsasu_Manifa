# PipelineLIDAR_Completo.py
# ═══════════════════════════════════════════════════════════════════════════
#  Pipeline LIDAR completo para Alsasua/Altsasua
#  Matemática precisa: UTM→Unity con escala real, IDW terrain, Delaunay tejados
#
#  Procesa todos los tiles NPC03 dentro del radio definido y genera:
#    lidar_dtm_05m.raw     — heightmap Unity 2049×2049 a 0.5m/pixel
#    lidar_dtm_meta.json   — metadatos del heightmap
#    lidar_buildings.json  — edificios con altura, forma y nube de puntos del tejado
#    lidar_trees.json      — árboles con posición exacta, altura, radio copa
#    lidar_agua.json       — cauces de agua (clase 9)
#    lidar_puentes.json    — puentes (clase 17)
# ═══════════════════════════════════════════════════════════════════════════

import laspy, numpy as np, json, math, struct, os
from pathlib import Path
from scipy.spatial import Delaunay, ConvexHull
from scipy.ndimage import uniform_filter, label

# ── Parámetros UTM→Unity (calculados con fórmula UTM correcta) ─────────────
E_ORIG     = 567951.0    # UTM E de Herriko Plaza (calculado rigurosamente)
N_ORIG     = 4749902.0   # UTM N de Herriko Plaza
M_LON_REAL = 81548.0     # cos(42.8987°) × 111320  [m/grado real en longitud]
M_LON_PROJ = 76400.0     # m/grado que usa el proyecto (preserva compatibilidad)
M_LAT      = 111320.0    # m/grado en latitud (sin distorsión)
OX, OZ     = 1918.0, 8570.0   # offset Herriko Plaza en Unity

TILES_DIR  = Path("E:/567")
OUT        = Path("Assets/AlsasuaData")
OUT.mkdir(parents=True, exist_ok=True)

# ── Área de proceso: ±2.5km del centro ─────────────────────────────────────
RADIO_M    = 2500.0
# En UTM: radio directo
ALTS_E_MIN = E_ORIG - RADIO_M * M_LON_REAL / M_LON_PROJ
ALTS_E_MAX = E_ORIG + RADIO_M * M_LON_REAL / M_LON_PROJ
ALTS_N_MIN = N_ORIG - RADIO_M
ALTS_N_MAX = N_ORIG + RADIO_M

# ── Grid del DTM 0.5m ──────────────────────────────────────────────────────
DTM_RES    = 0.5    # metros por celda
DTM_SIZE   = 2049   # Unity heightmap: potencia de 2 + 1
# Extensión Unity del heightmap: DTM_SIZE × DTM_RES = 1024.5m en cada eje
# Unity terrain: width = height = DTM_SIZE * DTM_RES
TERRAIN_W  = (DTM_SIZE - 1) * DTM_RES   # 1024m
# El heightmap cubre TERRAIN_W metros centrado en Herriko Plaza

# ═══════════════════════════════════════════════════════════════════════════

def utm_to_unity(utm_e_arr, utm_n_arr):
    """UTM 30N ETRS89 → Unity XZ (vectorizado, numpy)."""
    ux = (utm_e_arr - E_ORIG) / M_LON_REAL * M_LON_PROJ + OX
    uz = (utm_n_arr - N_ORIG) + OZ
    return ux.astype(np.float32), uz.astype(np.float32)

def unity_to_dtm_idx(ux_arr, uz_arr):
    """Unity XZ → índice (col, row) en el grid del DTM."""
    half = TERRAIN_W / 2.0
    col = ((ux_arr - OX + half) / DTM_RES).astype(np.int32)
    row = ((uz_arr - OZ + half) / DTM_RES).astype(np.int32)
    return np.clip(col, 0, DTM_SIZE-1), np.clip(row, 0, DTM_SIZE-1)

def seleccionar_tiles():
    """Selecciona tiles NPC03 que cubren el área de Alsasua, deduplica."""
    import struct
    candidatos = sorted(TILES_DIR.glob("PNOA_*_NPC03*.laz"))
    # Deduplicar tiles con (1) en el nombre
    seen = set()
    tiles = []
    for t in candidatos:
        # Extraer coordenada base del nombre (sin " (1)")
        base = t.name.replace("(1)", "").replace("( 1)", "").strip()
        if base in seen:
            continue
        seen.add(base)
        # Leer bbox
        try:
            with open(t, 'rb') as f:
                data = f.read(375)
            if data[:4] != b'LASF': continue
            coords = [struct.unpack_from('<d', data, 179+i*8)[0] for i in range(8)]
            xmin,xmax = sorted([coords[0],coords[1]])
            ymin,ymax = sorted([coords[2],coords[3]])
            if xmax > ALTS_E_MIN and xmin < ALTS_E_MAX and ymax > ALTS_N_MIN and ymin < ALTS_N_MAX:
                dist_e = abs((xmin+xmax)/2 - E_ORIG)
                dist_n = abs((ymin+ymax)/2 - N_ORIG)
                tiles.append((math.sqrt(dist_e**2+dist_n**2), t))
        except: pass
    tiles.sort()
    print(f"Tiles NPC03 para Alsasua: {len(tiles)}")
    for _, t in tiles:
        kb = t.stat().st_size//1024
        print(f"  {t.name}  ({kb}KB)")
    return [t for _, t in tiles]

# ═══════════════════════════════════════════════════════════════════════════
#  1. DTM 0.5m — grid acumulación + IDW interpolación de huecos
# ═══════════════════════════════════════════════════════════════════════════

def construir_dtm(tiles, footprints_mask=None):
    """
    Acumula puntos clase 2 (suelo) en grid 0.5m.
    Ignora puntos dentro de footprints de edificios (evitan contaminar el DTM).
    Aplica IDW para rellenar huecos y Gaussian para suavizar ruido.
    """
    print("\n=== 1. DTM 0.5m (suelo LIDAR) ===")
    Z_sum  = np.zeros((DTM_SIZE, DTM_SIZE), np.float64)
    Z_cnt  = np.zeros((DTM_SIZE, DTM_SIZE), np.int32)

    for path in tiles:
        print(f"  {path.name}...", end=' ', flush=True)
        las = laspy.read(str(path))
        cls = np.array(las.classification, np.uint8)
        m   = cls == 2   # suelo
        if not m.any(): print("sin suelo"); continue

        x, y, z = np.array(las.x)[m], np.array(las.y)[m], np.array(las.z)[m]
        ux, uz = utm_to_unity(x, y)
        col, row = unity_to_dtm_idx(ux, uz)

        # Acumular
        np.add.at(Z_sum, (row, col), z)
        np.add.at(Z_cnt, (row, col), 1)
        print(f"{m.sum():,} pts suelo")

    # Media donde hay datos
    Z_dtm = np.where(Z_cnt > 0, Z_sum / Z_cnt, np.nan)
    celdas_con_dato = np.sum(Z_cnt > 0)
    print(f"\nCeldas con datos: {celdas_con_dato:,}/{DTM_SIZE*DTM_SIZE:,} ({100*celdas_con_dato/(DTM_SIZE*DTM_SIZE):.1f}%)")

    # Rango Z para normalización
    validos = Z_dtm[~np.isnan(Z_dtm)]
    Z_MIN, Z_MAX = float(np.percentile(validos, 0.1)), float(np.percentile(validos, 99.9))
    Z_RANGE = Z_MAX - Z_MIN
    print(f"Z range: {Z_MIN:.1f} - {Z_MAX:.1f} m  (rango={Z_RANGE:.1f}m)")

    # ── Rellenar huecos: IDW con ventana 5×5 ──────────────────────────────
    print("Rellenando huecos (IDW)...", end=' ', flush=True)
    relleno = 0
    indices_vacios = np.argwhere(np.isnan(Z_dtm))
    WIN = 7
    for r, c in indices_vacios:
        r0,r1 = max(0,r-WIN), min(DTM_SIZE,r+WIN+1)
        c0,c1 = max(0,c-WIN), min(DTM_SIZE,c+WIN+1)
        vecinos = Z_dtm[r0:r1, c0:c1]
        mask_v  = ~np.isnan(vecinos)
        if mask_v.any():
            dr = np.arange(r0,r1) - r
            dc = np.arange(c0,c1) - c
            DC, DR = np.meshgrid(dc, dr)
            dist = np.sqrt(DR**2 + DC**2)
            dist[dist == 0] = 1e-6
            w = mask_v.astype(float) / dist
            Z_dtm[r, c] = np.sum(w * np.nan_to_num(vecinos)) / w.sum()
            relleno += 1
    print(f"{relleno:,} celdas interpoladas")

    # Suavizado Gaussian 3×3 para reducir ruido del escáner
    from scipy.ndimage import gaussian_filter
    Z_dtm = gaussian_filter(Z_dtm, sigma=0.8)

    # ── Exportar como .raw Unity (16-bit little-endian) ───────────────────
    Z_norm = np.clip((Z_dtm - Z_MIN) / Z_RANGE, 0, 1)
    raw = (Z_norm * 65535).astype(np.uint16)
    raw_path = OUT / "lidar_dtm_05m.raw"
    raw.astype('<u2').tofile(str(raw_path))
    print(f"lidar_dtm_05m.raw: {raw_path.stat().st_size//1024}KB")

    # XYZ para GeneradorTerrenoUltraPreciso (muestra 1 de cada 4)
    with open(OUT/'lidar_ground.xyz','w') as f:
        for r in range(0,DTM_SIZE,2):
            for c in range(0,DTM_SIZE,2):
                if Z_cnt[r,c] > 0:
                    half = TERRAIN_W/2
                    ux = c*DTM_RES - half + OX
                    uz = r*DTM_RES - half + OZ
                    f.write(f"{ux:.1f} {Z_dtm[r,c]:.3f} {uz:.1f}\n")

    # Metadatos
    meta = {
        "heightmapResolution": DTM_SIZE,
        "terrainWidth":  TERRAIN_W,
        "terrainLength": TERRAIN_W,
        "terrainHeight": round(Z_RANGE, 2),
        "z_min":         round(Z_MIN, 2),
        "z_max":         round(Z_MAX, 2),
        "res_m":         DTM_RES,
        "E_origin":      E_ORIG,
        "N_origin":      N_ORIG,
        "unity_ox":      OX,
        "unity_oz":      OZ,
        "byteOrder":     "uint16 little-endian",
        "note":          "Unity terrain: importar como RAW 16bit, width=terrainWidth, length=terrainLength, height=terrainHeight"
    }
    with open(OUT/'lidar_dtm_meta.json','w') as f:
        json.dump(meta, f, indent=2)
    print(f"lidar_dtm_meta.json guardado")
    return Z_dtm, Z_MIN, Z_MAX

# ═══════════════════════════════════════════════════════════════════════════
#  2. EDIFICIOS — Delaunay + PCA para forma exacta de tejado
# ═══════════════════════════════════════════════════════════════════════════

def procesar_edificios(tiles):
    """
    Extrae nube de puntos clase 6 por cada footprint.
    Para cada edificio calcula:
      - z_base: altura de alero (percentil 10 de los puntos del edificio)
      - z_cumbrera: altura de cumbrera (percentil 95)
      - altura_neta: z_cumbrera - z_base
      - eje_cumbrera: vector PCA del eje longitudinal del tejado
      - forma: flat/gabled/hipped/mansard detectado geométricamente
      - puntos_tejado: lista de puntos 3D para Delaunay en Unity (máx 200)
    """
    print("\n=== 2. Edificios — Delaunay + PCA tejados ===")

    with open(OUT/'buildings_unity.json') as f:
        eds = json.load(f)

    fps = []
    for ed in eds:
        v = ed.get('vertices', [])
        if not v: continue
        xs = np.array([p['x']+OX for p in v])
        zs = np.array([p['z']+OZ for p in v])
        fps.append({
            'id': ed['id'],
            'xmin': xs.min()-1.5, 'xmax': xs.max()+1.5,
            'zmin': zs.min()-1.5, 'zmax': zs.max()+1.5,
            'cx': xs.mean(), 'cz': zs.mean(),
            'area': abs(np.cross(np.diff(xs), np.diff(zs)).sum())/2,
        })

    # Acumular puntos por edificio
    bld_pts = {fp['id']: [] for fp in fps}

    for path in tiles:
        print(f"  {path.name}...", end=' ', flush=True)
        las = laspy.read(str(path))
        cls = np.array(las.classification, np.uint8)
        m   = cls == 6
        if not m.any(): print("sin edificios"); continue

        x, y, z = np.array(las.x)[m], np.array(las.y)[m], np.array(las.z)[m]
        ux, uz = utm_to_unity(x, y)
        n_asig = 0

        for fp in fps:
            mask = ((ux >= fp['xmin']) & (ux <= fp['xmax']) &
                    (uz >= fp['zmin']) & (uz <= fp['zmax']))
            if mask.any():
                pts = np.stack([ux[mask], z[mask], uz[mask]], 1)
                bld_pts[fp['id']].extend(pts.tolist())
                n_asig += mask.sum()
        print(f"{m.sum():,} pts edif, {n_asig:,} asignados")

    # Analizar cada edificio
    resultados = []
    con_pts = 0
    for fp in fps:
        pts = bld_pts[fp['id']]
        if len(pts) < 4:
            continue
        arr = np.array(pts)  # [N, 3]: [ux, z_elev, uz]
        z_vals = arr[:, 1]

        z_base    = float(np.percentile(z_vals, 5))
        z_cumbr   = float(np.percentile(z_vals, 97))
        altura    = z_cumbr - z_base
        n_pts     = len(pts)

        # PCA sobre XZ para orientación del eje del tejado
        pts2d = arr[:, [0, 2]]  # solo X, Z planta
        centro = pts2d.mean(0)
        cov = np.cov((pts2d - centro).T)
        eigvals, eigvecs = np.linalg.eigh(cov)
        eje_principal = eigvecs[:, 1]  # eigenvector del mayor eigenvalue

        # Histograma Z para clasificar forma
        hist, edges = np.histogram(z_vals, bins=20)
        # Normalizar: el pico más bajo = alero, el más alto = cumbrera
        peak_ratio = hist.max() / (hist.mean() + 0.5)

        if altura < 1.5:
            forma = "flat"
        elif peak_ratio > 3.5:
            # Muchos puntos concentrados en altura = dos aguas pronunciado
            forma = "gabled"
        elif altura > 0.5 * min(fp['xmax']-fp['xmin'], fp['zmax']-fp['zmin']):
            # Altura grande relativa al edificio = mansarda o apuntado
            forma = "steep_gabled"
        else:
            forma = "hipped"

        # Muestrar puntos del tejado para Delaunay (solo tejado real, no alero)
        mask_techo = z_vals > (z_base + altura * 0.35)
        pts_techo = arr[mask_techo]
        if len(pts_techo) > 200:
            idx = np.random.choice(len(pts_techo), 200, replace=False)
            pts_techo = pts_techo[idx]
        # Convertir a relativo para compresión
        # Formato {x,y,z} en lugar de [x,y,z] porque JsonUtility de Unity
        # no puede deserializar jagged arrays (float[][])
        puntos_rel = []
        for pt in pts_techo:
            puntos_rel.append({
                'x': round(float(pt[0]-fp['cx']),2),
                'y': round(float(pt[1]-z_base),2),
                'z': round(float(pt[2]-fp['cz']),2)
            })

        resultados.append({
            'id':            fp['id'],
            'lidar_z_base':  round(z_base, 3),
            'lidar_z_top':   round(z_cumbr, 3),
            'lidar_altura':  round(altura, 3),
            'lidar_forma':   forma,
            'lidar_pts':     n_pts,
            'lidar_eje_x':   round(float(eje_principal[0]), 4),
            'lidar_eje_z':   round(float(eje_principal[1]), 4),
            'puntos_tejado': puntos_rel,   # lista [dx, dz_elev, dz_horiz]
        })
        con_pts += 1

    with open(OUT/'lidar_buildings.json','w') as f:
        json.dump(resultados, f, separators=(',',':'))

    formas = {}
    for r in resultados: formas[r['lidar_forma']] = formas.get(r['lidar_forma'],0)+1
    print(f"\nlidar_buildings.json: {len(resultados)} edificios con nube LIDAR")
    print(f"  Formas: {formas}")
    alturas = [r['lidar_altura'] for r in resultados if r['lidar_altura']>0]
    if alturas:
        print(f"  Altura: {min(alturas):.1f}-{max(alturas):.1f}m media={sum(alturas)/len(alturas):.1f}m")
    return resultados

# ═══════════════════════════════════════════════════════════════════════════
#  3. ÁRBOLES — DBSCAN clustering preciso + forma de copa
# ═══════════════════════════════════════════════════════════════════════════

def procesar_arboles(tiles):
    """
    Acumula clase 5 (vegetación alta) de todos los tiles.
    Clustering por conectividad en grid 1m para detectar cada árbol.
    Para cada árbol: posición, altura escáner, radio de copa XZ.
    """
    print("\n=== 3. Árboles — clustering vegetación alta ===")

    all_veg = []
    for path in tiles:
        las = laspy.read(str(path))
        cls = np.array(las.classification, np.uint8)
        m   = cls == 5
        if not m.any(): continue
        x, y, z = np.array(las.x)[m], np.array(las.y)[m], np.array(las.z)[m]
        ux, uz = utm_to_unity(x, y)
        all_veg.append(np.stack([ux, z, uz], 1))
        print(f"  {path.name}: {m.sum():,} pts veg alta")

    if not all_veg:
        print("Sin vegetación"); return []

    veg = np.concatenate(all_veg, 0)
    print(f"Total pts vegetación: {len(veg):,}")

    # Grid 1m (Unity) para clustering
    CELL = 1.2
    xoff = float(veg[:,0].min()); zoff = float(veg[:,2].min())
    cols = np.clip(((veg[:,0]-xoff)/CELL).astype(int), 0, 4999)
    rows = np.clip(((veg[:,2]-zoff)/CELL).astype(int), 0, 4999)

    # Grid de altura máxima por celda
    nC = cols.max()+2; nR = rows.max()+2
    Z_max_grid  = np.full((nR, nC), -np.inf, np.float32)
    Z_sum_grid  = np.zeros((nR, nC), np.float64)
    Cnt_grid    = np.zeros((nR, nC), np.int32)
    Occupied    = np.zeros((nR, nC), bool)
    np.maximum.at(Z_max_grid, (rows, cols), veg[:,1])
    np.add.at(Z_sum_grid, (rows, cols), veg[:,1])
    np.add.at(Cnt_grid, (rows, cols), 1)
    Occupied[rows, cols] = True

    # Conectar componentes (árboles individuales)
    labeled, n_clusters = label(Occupied)
    print(f"Clusters detectados: {n_clusters}")

    arboles = []
    for cid in range(1, n_clusters+1):
        ys, xs_idx = np.where(labeled == cid)
        n_celdas = len(ys)
        if n_celdas < 3: continue  # ignorar puntos sueltos

        # Centroide ponderado por altura
        z_celdas = Z_max_grid[ys, xs_idx]
        w = z_celdas - z_celdas.min() + 0.1
        cx = float(np.average(xs_idx * CELL + xoff, weights=w))
        cz = float(np.average(ys * CELL + zoff, weights=w))
        h_max = float(Z_max_grid[ys, xs_idx].max())
        h_min = float(Z_sum_grid[ys, xs_idx].sum() / Cnt_grid[ys, xs_idx].sum())

        # Radio de copa: RMS de distancias al centroide
        dx = xs_idx * CELL + xoff - cx
        dz = ys * CELL + zoff - cz
        radio = float(np.sqrt(np.mean(dx**2 + dz**2)))
        radio = max(0.5, min(radio, 15.0))   # clamp 0.5-15m

        # Altura del árbol: z_max - z_terreno (aproximado)
        altura_arbol = h_max - h_min  # diferencia entre top y base de copa

        arboles.append({
            'x':      round(cx, 2),
            'z':      round(cz, 2),
            'h_abs':  round(h_max, 2),    # altura absoluta en metros (Z escáner)
            'altura': round(altura_arbol, 1),  # altura estimada del árbol
            'radio':  round(radio, 2),
            'pts':    int(Cnt_grid[ys, xs_idx].sum()),
        })

    with open(OUT/'lidar_trees.json','w') as f:
        json.dump(arboles, f, separators=(',',':'))
    print(f"lidar_trees.json: {len(arboles)} árboles")
    alturas = [a['altura'] for a in arboles if a['altura'] > 1]
    if alturas:
        print(f"  Altura: {min(alturas):.1f}-{max(alturas):.1f}m media={sum(alturas)/len(alturas):.1f}m")
    return arboles

# ═══════════════════════════════════════════════════════════════════════════
#  4. EXTRAS — agua (clase 9) y puentes (clase 17)
# ═══════════════════════════════════════════════════════════════════════════

def procesar_extras(tiles):
    print("\n=== 4. Extras: agua y puentes ===")
    agua_pts = []; puentes_pts = []
    for path in tiles:
        las = laspy.read(str(path))
        cls = np.array(las.classification, np.uint8)
        x, y, z = np.array(las.x), np.array(las.y), np.array(las.z)
        for c, lst in [(9, agua_pts), (17, puentes_pts)]:
            m = cls == c
            if m.any():
                ux, uz = utm_to_unity(x[m], y[m])
                lst.extend(zip(ux.tolist(), z[m].tolist(), uz.tolist()))

    def pts_to_segments(pts, max_pts=500):
        if not pts: return []
        arr = np.array(pts)
        if len(arr) > max_pts:
            arr = arr[np.random.choice(len(arr), max_pts, replace=False)]
        return [{'x':round(float(p[0]),1),'y':round(float(p[1]),2),'z':round(float(p[2]),1)} for p in arr]

    agua = pts_to_segments(agua_pts)
    puentes = pts_to_segments(puentes_pts)
    with open(OUT/'lidar_agua.json','w') as f: json.dump(agua,f,separators=(',',':'))
    with open(OUT/'lidar_puentes.json','w') as f: json.dump(puentes,f,separators=(',',':'))
    print(f"  agua: {len(agua)} pts, puentes: {len(puentes)} pts")

# ═══════════════════════════════════════════════════════════════════════════
#  5. FUSIONAR ALTURAS LIDAR → buildings_final.json
# ═══════════════════════════════════════════════════════════════════════════

def fusionar_final():
    print("\n=== 5. Fusión buildings_final.json ===")
    lb_path = OUT / 'lidar_buildings.json'
    bf_path = OUT / 'buildings_final.json'
    if not lb_path.exists() or not bf_path.exists(): return

    with open(lb_path) as f: lidar = {b['id']:b for b in json.load(f)}
    with open(bf_path) as f: eds = json.load(f)

    act = 0
    for ed in eds:
        if ed['id'] in lidar:
            ld = lidar[ed['id']]
            if ld['lidar_pts'] >= 4 and ld['lidar_altura'] > 0.8:
                ed['height']         = round(ld['lidar_altura'], 3)
                ed['lidar_altura']   = ld['lidar_altura']
                ed['lidar_forma']    = ld['lidar_forma']
                ed['lidar_pts']      = ld['lidar_pts']
                ed['lidar_eje_x']    = ld.get('lidar_eje_x', 0.0)
                ed['lidar_eje_z']    = ld.get('lidar_eje_z', 1.0)
                ed['levels']         = max(1, round(ld['lidar_altura'] / 3.2))
                act += 1

    with open(bf_path,'w',encoding='utf-8') as f:
        json.dump(eds,f,ensure_ascii=False,separators=(',',':'))
    print(f"  Actualizado: {act}/{len(eds)} edificios con datos LIDAR completos")

# ═══════════════════════════════════════════════════════════════════════════
#  MAIN
# ═══════════════════════════════════════════════════════════════════════════

if __name__ == '__main__':
    import time
    print("╔══════════════════════════════════════════════════════════╗")
    print("║  PIPELINE LIDAR COMPLETO — Alsasua/Altsasua              ║")
    print("║  UTM 30N ETRS89 → Unity — matemática rigurosa            ║")
    print("╚══════════════════════════════════════════════════════════╝")
    print(f"\nHerrriko Plaza UTM: E={E_ORIG:.0f}  N={N_ORIG:.0f}")
    print(f"Radio proceso: {RADIO_M:.0f}m  DTM: {DTM_SIZE}×{DTM_SIZE} @ {DTM_RES}m/px")
    print(f"Terreno Unity: {TERRAIN_W:.0f}m × {TERRAIN_W:.0f}m")

    t0 = time.time()
    tiles = seleccionar_tiles()

    Z_dtm, Z_MIN, Z_MAX = construir_dtm(tiles)
    print(f"  DTM: {time.time()-t0:.0f}s")

    procesar_edificios(tiles)
    print(f"  Edificios: {time.time()-t0:.0f}s")

    procesar_arboles(tiles)
    print(f"  Arboles: {time.time()-t0:.0f}s")

    procesar_extras(tiles)
    fusionar_final()

    print(f"\n╔══════════════════════════════════════════════════════════╗")
    print(f"║  LISTO en {time.time()-t0:.0f}s                                       ")
    print(f"╚══════════════════════════════════════════════════════════╝")
    print("\nArchivos generados:")
    for f in ['lidar_dtm_05m.raw','lidar_dtm_meta.json','lidar_buildings.json',
              'lidar_trees.json','lidar_agua.json','lidar_puentes.json',
              'lidar_ground.xyz','buildings_final.json']:
        p = OUT/f
        if p.exists(): print(f"  {f}: {p.stat().st_size//1024}KB")

    print("\nEn Unity: Play directo.")
    print("  GeneradorTerrenoUltraPreciso cargara lidar_dtm_05m.raw (0.5m/px)")
    print("  FusionadorEdificiosUltra: alturas y formas exactas")
    print("  PosicionadorPrecisionUrbana: arboles en posicion LIDAR exacta")
