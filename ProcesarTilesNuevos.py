# ProcesarTilesNuevos.py
# ═══════════════════════════════════════════════════════════════════════════
#  Procesa TODOS los tiles LAZ de E:/567/ que cubran Alsasua/Altsasua.
#  Ejecutar cuando hayas copiado los nuevos tiles a E:/567/
#
#  Lo que hace:
#  1. Lee todas las cabeceras LAZ → identifica cuáles cubren Alsasua
#  2. Procesa con laspy (si instalado): extrae suelo, edificios, vegetación
#  3. Fusiona con los datos existentes → buildings_final.json mejorado
#  4. Copia tiles usados a Assets/AlsasuaData/lidar_tiles/
#
#  USO:  python ProcesarTilesNuevos.py
# ═══════════════════════════════════════════════════════════════════════════

import os, sys, json, math, struct, shutil
from pathlib import Path

TILES_DIR  = Path("E:/567")
OUT_DIR    = Path("Assets/AlsasuaData")
LIDAR_DIR  = OUT_DIR / "lidar_tiles"
LIDAR_DIR.mkdir(parents=True, exist_ok=True)

# Bounding box Alsasua en UTM 30N ETRS89 (ampliado para cubrir todo el municipio)
ALTS_E_MIN, ALTS_E_MAX = 571000, 578000
ALTS_N_MIN, ALTS_N_MAX = 4748000, 4755000

# Origen Unity: Herriko Plaza aprox en UTM
E_ORIG = 574900
N_ORIG = 4751600
OX, OZ = 1918.0, 8570.0

def utm_to_unity(utm_e, utm_n):
    """UTM 30N ETRS89 → Unity XZ (aproximación lineal local)."""
    # Usamos la misma escala que el proyecto: M_LON=76400, M_LAT=111320
    # lon = LON0 + (utm_e - E_ORIG) / M_LON_REAL  → luego * M_LON_PROYECTO + OX
    M_LON_REAL    = 81612.0  # cos(42.9°) * 111320
    M_LON_PROYECTO= 76400.0
    M_LAT         = 111320.0
    ux = (utm_e - E_ORIG) / M_LON_REAL * M_LON_PROYECTO + OX
    uz = (utm_n - N_ORIG) / M_LAT * M_LAT + OZ  # = utm_n - N_ORIG + OZ
    return ux, uz

# ─────────────────────────────────────────────────────────────────────────

def leer_cabecera_laz(path):
    """Lee min/max XYZ de la cabecera LAS 1.x."""
    try:
        with open(path, 'rb') as f:
            data = f.read(375)
        sig = data[:4].decode('ascii', 'ignore')
        if sig != 'LASF':
            return None
        # LAS 1.x header offsets para x_min, x_max, y_min, y_max, z_min, z_max
        # Bytes 179-242 (doubles de 8 bytes)
        x_min = struct.unpack_from('<d', data, 179)[0]
        x_max = struct.unpack_from('<d', data, 187)[0]
        y_min = struct.unpack_from('<d', data, 195)[0]
        y_max = struct.unpack_from('<d', data, 203)[0]
        z_min = struct.unpack_from('<d', data, 211)[0]
        z_max = struct.unpack_from('<d', data, 219)[0]
        # Corregir si min/max están intercambiados (cabecera mal escrita)
        if x_min > x_max: x_min, x_max = x_max, x_min
        if y_min > y_max: y_min, y_max = y_max, y_min
        if z_min > z_max: z_min, z_max = z_max, z_min
        return {'x_min': x_min, 'x_max': x_max,
                'y_min': y_min, 'y_max': y_max,
                'z_min': z_min, 'z_max': z_max}
    except:
        return None

def tiles_que_cubren_alsasua():
    """Devuelve lista de rutas LAZ que cubren el área de Alsasua."""
    todos  = sorted(TILES_DIR.glob("*.laz")) + sorted(TILES_DIR.glob("*.las"))
    cubren = []
    otros  = []
    print(f"Tiles encontrados en {TILES_DIR}: {len(todos)}")

    for t in todos:
        h = leer_cabecera_laz(t)
        if not h:
            print(f"  ✗ {t.name}: no se pudo leer cabecera")
            continue
        solapa = (h['x_max'] > ALTS_E_MIN and h['x_min'] < ALTS_E_MAX and
                  h['y_max'] > ALTS_N_MIN and h['y_min'] < ALTS_N_MAX)
        kb = t.stat().st_size // 1024
        if solapa:
            print(f"  ✅ {t.name:50s} E={h['x_min']:.0f}-{h['x_max']:.0f} N={h['y_min']:.0f}-{h['y_max']:.0f} ({kb}KB)")
            cubren.append(t)
        else:
            otros.append(t.name)

    if otros:
        print(f"\n  (otros {len(otros)} tiles fuera del área de Alsasua)")
    print(f"\nTiles para Alsasua: {len(cubren)}")
    return cubren

def procesar_con_laspy(tiles):
    """Extrae suelo, edificios y vegetación de los tiles Alsasua."""
    try:
        import laspy
        import numpy as np
    except ImportError:
        print("\n⚠ laspy no instalado. Para procesamiento LIDAR completo:")
        print("  pip install laspy lazrs-python numpy scipy")
        print("\nCopias los tiles a Assets/AlsasuaData/lidar_tiles/ para uso posterior.")
        return False

    print(f"\nProcesando {len(tiles)} tiles con laspy...")

    all_ground    = []
    all_buildings = {}   # edificio_id → puntos
    all_veg       = []

    # Cargar footprints para asignar puntos de edificio
    fp_path = OUT_DIR / "buildings_unity.json"
    footprints = []
    if fp_path.exists():
        with open(fp_path) as f:
            eds = json.load(f)
        for ed in eds:
            if not ed.get('vertices'): continue
            verts = ed['vertices']
            xs = [v['x']+OX for v in verts]
            zs = [v['z']+OZ for v in verts]
            footprints.append({
                'id': ed['id'],
                'xmin': min(xs)-1, 'xmax': max(xs)+1,
                'zmin': min(zs)-1, 'zmax': max(zs)+1,
                'verts': list(zip(xs, zs)),
            })

    for tile_path in tiles:
        print(f"  Leyendo {tile_path.name} ...", end=' ', flush=True)
        try:
            las = laspy.read(str(tile_path))
            x   = np.array(las.x, dtype=np.float32)
            y   = np.array(las.y, dtype=np.float32)
            z   = np.array(las.z, dtype=np.float32)
            cls = np.array(las.classification, dtype=np.uint8)
            print(f"{len(x):,} pts")

            # Convertir UTM → Unity
            ux, uz = utm_to_unity(x, y)

            # Suelo (clase 2)
            m_g = cls == 2
            if m_g.any():
                gnd = np.stack([ux[m_g], z[m_g], uz[m_g]], axis=1)
                all_ground.extend(gnd[::3].tolist())  # 1 de cada 3

            # Edificios (clase 6)
            m_b = cls == 6
            if m_b.any():
                bx, bz_vals, balt = ux[m_b], uz[m_b], z[m_b]
                # Asignar cada punto al edificio cuyo footprint lo contiene
                for ed in footprints:
                    in_bb = ((bx >= ed['xmin']) & (bx <= ed['xmax']) &
                             (bz_vals >= ed['zmin']) & (bz_vals <= ed['zmax']))
                    if in_bb.any():
                        eid = ed['id']
                        if eid not in all_buildings:
                            all_buildings[eid] = []
                        all_buildings[eid].extend(balt[in_bb].tolist())

            # Vegetación alta (clase 5)
            m_v = cls == 5
            if m_v.any():
                veg = np.stack([ux[m_v], z[m_v], uz[m_v]], axis=1)
                all_veg.extend(veg[::5].tolist())

        except Exception as e:
            print(f"ERROR: {e}")

    # Guardar puntos de suelo
    if all_ground:
        out = OUT_DIR / "lidar_ground.xyz"
        with open(out, 'w') as f:
            for pt in all_ground:
                f.write(f"{pt[0]:.2f} {pt[1]:.3f} {pt[2]:.2f}\n")
        print(f"\n✅ lidar_ground.xyz: {len(all_ground):,} puntos suelo")

    # Derivar altura y forma de tejado por edificio
    if all_buildings:
        resultados = []
        for eid, z_vals in all_buildings.items():
            import numpy as np
            zv = np.array(z_vals)
            z_min = float(np.percentile(zv, 5))
            z_max = float(np.percentile(zv, 95))
            altura = z_max - z_min
            hist, _ = np.histogram(zv, bins=15)
            bimodal = float(hist.max()) / (float(hist.mean()) + 0.1) > 2.5
            forma = "flat" if altura < 1.5 else ("gabled" if bimodal else "hipped")
            resultados.append({
                "id": eid,
                "lidar_z_min":  round(z_min, 2),
                "lidar_z_max":  round(z_max, 2),
                "lidar_altura": round(altura, 2),
                "lidar_forma":  forma,
                "lidar_pts":    len(z_vals),
            })
        out = OUT_DIR / "lidar_buildings.json"
        with open(out, 'w') as f:
            json.dump(resultados, f, indent=2)
        print(f"✅ lidar_buildings.json: {len(resultados)} edificios con datos LIDAR")

    # Detectar árboles
    if all_veg:
        guardar_arboles_lidar(all_veg)

    return True

def guardar_arboles_lidar(veg_pts):
    try:
        import numpy as np
        from scipy.ndimage import label
    except ImportError:
        return

    veg = np.array(veg_pts)
    xoff = float(veg[:, 0].min())
    zoff = float(veg[:, 2].min())
    xr = np.clip(((veg[:, 0] - xoff) / 1.5).astype(int), 0, 4000)
    zr = np.clip(((veg[:, 2] - zoff) / 1.5).astype(int), 0, 8000)
    grid = np.zeros((xr.max()+2, zr.max()+2), bool)
    for xi, zi in zip(xr, zr):
        grid[xi, zi] = True

    labeled, n = label(grid)
    arboles = []
    for cid in range(1, n+1):
        ys_idx, zs_idx = np.where(labeled == cid)
        if len(ys_idx) < 4: continue
        cx = float(ys_idx.mean()) * 1.5 + xoff
        cz = float(zs_idx.mean()) * 1.5 + zoff
        mask = labeled[xr, zr] == cid
        z_max = float(veg[mask, 1].max()) if mask.any() else 0
        radio = math.sqrt(len(ys_idx) * 1.5**2 / math.pi)
        arboles.append({"x": round(cx,1), "z": round(cz,1),
                        "altura": round(z_max,1), "radio": round(radio,1)})

    out = OUT_DIR / "lidar_trees.json"
    with open(out, 'w') as f:
        json.dump(arboles, f)
    print(f"✅ lidar_trees.json: {len(arboles)} arboles detectados")

def fusionar_alturas_lidar():
    """Actualiza buildings_final.json con las alturas LIDAR recién calculadas."""
    lb_path = OUT_DIR / "lidar_buildings.json"
    bf_path = OUT_DIR / "buildings_final.json"
    if not lb_path.exists() or not bf_path.exists():
        return

    with open(lb_path) as f:
        lidar = {b['id']: b for b in json.load(f)}
    with open(bf_path) as f:
        edificios = json.load(f)

    actualizados = 0
    for ed in edificios:
        if ed['id'] in lidar:
            ld = lidar[ed['id']]
            if ld['lidar_pts'] >= 5 and ld['lidar_altura'] > 1.5:
                ed['height']       = round(ld['lidar_altura'], 2)
                ed['lidar_altura'] = ld['lidar_altura']
                ed['lidar_forma']  = ld['lidar_forma']
                ed['lidar_pts']    = ld['lidar_pts']
                ed['levels']       = max(1, round(ld['lidar_altura'] / 3.2))
                actualizados += 1

    with open(bf_path, 'w', encoding='utf-8') as f:
        json.dump(edificios, f, ensure_ascii=False, separators=(',',':'))
    print(f"✅ buildings_final.json: {actualizados} alturas LIDAR actualizadas")

# ─────────────────────────────────────────────────────────────────────────

if __name__ == "__main__":
    print("=" * 60)
    print("  PROCESAR TILES LIDAR NUEVOS → Alsasua/Altsasua")
    print("=" * 60)

    tiles = tiles_que_cubren_alsasua()

    if not tiles:
        print("\nNo hay tiles que cubran Alsasua.")
        print("Necesitas tiles UTM E=571-576km, N=4749-4753km.")
        print("Descarga en: https://centrodedescargas.cnig.es/CentroDescargas/lidar-tercera-cobertura")
        sys.exit(0)

    # Copiar tiles encontrados al proyecto Unity
    copiados = 0
    for t in tiles:
        dst = LIDAR_DIR / t.name
        if not dst.exists():
            shutil.copy2(t, dst)
            copiados += 1
    if copiados:
        print(f"\nCopiados {copiados} tiles a Assets/AlsasuaData/lidar_tiles/")

    # Procesar
    ok = procesar_con_laspy(tiles)

    if ok:
        fusionar_alturas_lidar()
        print("\n" + "=" * 60)
        print("  LISTO — En Unity, dale Play directamente.")
        print("  El proyecto cargara lidar_buildings.json automaticamente.")
        print("=" * 60)
    else:
        print("\nInstala laspy y vuelve a ejecutar:")
        print("  pip install laspy lazrs-python numpy scipy")
        print("  python ProcesarTilesNuevos.py")
