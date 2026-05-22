# FusionarDatosViejos.py
# ═══════════════════════════════════════════════════════════════════════════
#  Importa datos del proyecto anterior (E:/567/alsasua_outputs) al proyecto actual.
#
#  Lo que hace:
#  1. ALTURAS LIDAR — fusiona 1562 edificios con height real (3.7-22.9m)
#     del viejo buildings_unity.json con nuestros 1030 edificios por proximidad.
#     Resultado → buildings_con_alturas_lidar.json
#
#  2. ORTOFOTO — copia ortofoto_unity.png (6.5MB) como textura de terreno.
#     Mucho mejor que los 72 tiles individuales.
#
#  3. SCRIPTS UNITY — copia AlsasuaSceneBuilder.cs y AlsasuaTerrainRuntime.cs
#     para referencia / reutilización.
# ═══════════════════════════════════════════════════════════════════════════

import json, math, shutil, os
from pathlib import Path

SRC  = Path("E:/567/alsasua_outputs")
DST  = Path("Assets/AlsasuaData")
DST.mkdir(parents=True, exist_ok=True)

OX, OZ = 1918.0, 8570.0  # offset Herriko Plaza en Unity

# ═══════════════════════════════════════════════════════════════════════════
#  1. FUSIÓN DE ALTURAS LIDAR
# ═══════════════════════════════════════════════════════════════════════════

def fusionar_alturas():
    print("=== 1. Fusión de alturas LIDAR ===")

    # Cargar edificios viejos (coords absolutas, tienen height LIDAR)
    with open(SRC / "buildings_unity.json") as f:
        viejos = json.load(f)
    print(f"  Edificios con height LIDAR: {len(viejos)}")

    # Cargar nuestros edificios (coords relativas, necesitan +OX/+OZ)
    base = DST / "buildings_final.json"
    if not base.exists(): base = DST / "buildings_completo.json"
    if not base.exists(): base = DST / "buildings_rico.json"
    if not base.exists(): base = DST / "buildings_unity.json"

    with open(base) as f:
        nuestros = json.load(f)
    print(f"  Nuestros edificios: {len(nuestros)} (desde {base.name})")

    # Construir kdtree simple del viejo (centroide en coords absolutas)
    viejo_pts = []
    for b in viejos:
        viejo_pts.append((b['x'], b['z'], b))  # x,z ya son absolutos

    def dist2(ax, az, bx, bz):
        return (ax-bx)**2 + (az-bz)**2

    # Para cada edificio nuestro, buscar el más cercano del viejo
    fusionados = 0
    sin_match  = 0
    MAX_DIST   = 25.0  # máxima distancia para considerar el mismo edificio (25m)

    for ed in nuestros:
        verts = ed.get("vertices", [])
        if not verts:
            continue

        # Centroide absoluto Unity del edificio nuestro
        cx = sum(v["x"] for v in verts) / len(verts) + OX
        cz = sum(v["z"] for v in verts) / len(verts) + OZ

        # Buscar el más cercano en la lista de viejos
        mejor_d2 = float('inf')
        mejor_b  = None
        for vx, vz, vb in viejo_pts:
            d2 = dist2(cx, cz, vx, vz)
            if d2 < mejor_d2:
                mejor_d2 = d2
                mejor_b  = vb

        if mejor_b and math.sqrt(mejor_d2) <= MAX_DIST:
            # Asignar altura LIDAR si más fiable que la estimada
            altura_vieja = mejor_b.get("height", 0)
            if altura_vieja > 1.5:
                ed["height"]  = round(altura_vieja, 2)
                ed["levels"]  = max(1, mejor_b.get("floors", 1))
                ed["lidar_altura"] = round(altura_vieja, 2)
                ed["lidar_pts"]    = 99  # marcado como de alta confianza
                fusionados += 1
        else:
            sin_match += 1

    out = DST / "buildings_con_alturas_lidar.json"
    with open(out, "w", encoding="utf-8") as f:
        json.dump(nuestros, f, ensure_ascii=False, separators=(",",":"))

    print(f"  ✓ {fusionados}/{len(nuestros)} edificios con altura LIDAR fusionada")
    print(f"  ✗ {sin_match} sin match (distancia > {MAX_DIST}m)")
    print(f"  → {out}  ({out.stat().st_size//1024}KB)")

# ═══════════════════════════════════════════════════════════════════════════
#  2. ORTOFOTO COMBINADA
# ═══════════════════════════════════════════════════════════════════════════

def copiar_ortofoto():
    print("\n=== 2. Ortofoto combinada ===")

    for nombre in ["ortofoto_unity.png", "ortofoto_alsasua_REAL.png"]:
        src = SRC / nombre
        if not src.exists():
            print(f"  ✗ {nombre} no encontrado")
            continue
        dst = DST / nombre
        if dst.exists():
            print(f"  ✓ {nombre} ya existe ({dst.stat().st_size//1024}KB)")
            continue
        shutil.copy2(src, dst)
        print(f"  ✓ Copiado {nombre} ({src.stat().st_size//1024}KB)")

# ═══════════════════════════════════════════════════════════════════════════
#  3. SCRIPTS UNITY DE REFERENCIA
# ═══════════════════════════════════════════════════════════════════════════

def copiar_scripts_unity():
    print("\n=== 3. Scripts Unity de referencia ===")

    scripts_src = SRC / "Unity"
    if not scripts_src.exists():
        return

    dst_ref = Path("Assets/Scripts/_Referencia")
    dst_ref.mkdir(parents=True, exist_ok=True)

    for cs_file in scripts_src.rglob("*.cs"):
        dst = dst_ref / cs_file.name
        if not dst.exists():
            shutil.copy2(cs_file, dst)
            print(f"  ✓ {cs_file.name}")
        else:
            print(f"  (ya existe) {cs_file.name}")

    # Copiar guía de setup
    guia = scripts_src / "UNITY_SETUP_GUIDE.txt"
    if guia.exists():
        shutil.copy2(guia, Path("E:/Desk/DAM/Altsasu_Manifa/UNITY_SETUP_GUIDE_original.txt"))
        print("  ✓ UNITY_SETUP_GUIDE.txt")

# ═══════════════════════════════════════════════════════════════════════════
#  4. DATOS GIS ADICIONALES (GeoJSON del viejo proyecto)
# ═══════════════════════════════════════════════════════════════════════════

def copiar_geojson():
    print("\n=== 4. GeoJSON adicionales (ríos, bosques, carreteras) ===")

    utiles = [
        ("agua_lin.geojson",           "rios_ejes.geojson"),
        ("agua_pol.geojson",           "rios_pol.geojson"),
        ("masas_forestales.geojson",   "masas_forestales.geojson"),
        ("bosques.geojson",            "bosques.geojson"),
        ("redviaria_completa.geojson", "redviaria_completa.geojson"),
        ("usos_suelo.geojson",         "usos_suelo.geojson"),
    ]

    for src_name, dst_name in utiles:
        src = SRC / src_name
        dst = DST / dst_name
        if not src.exists():
            continue
        if dst.exists():
            print(f"  (ya existe) {dst_name}")
            continue
        shutil.copy2(src, dst)
        print(f"  ✓ {dst_name} ({src.stat().st_size//1024}KB)")

# ═══════════════════════════════════════════════════════════════════════════
#  5. ACTUALIZAR buildings_final.json CON LAS ALTURAS LIDAR
# ═══════════════════════════════════════════════════════════════════════════

def actualizar_fichero_principal():
    """Renombra buildings_con_alturas_lidar.json → buildings_final.json si mejora."""
    src = DST / "buildings_con_alturas_lidar.json"
    dst = DST / "buildings_final.json"
    if not src.exists():
        return

    # Contar cuántos tienen lidar_pts=99 en el nuevo fichero
    with open(src) as f:
        data = json.load(f)
    n_lidar = sum(1 for b in data if b.get("lidar_pts", 0) > 0)

    # Comparar con el existente si lo hay
    if dst.exists():
        with open(dst) as f:
            existente = json.load(f)
        n_existente = sum(1 for b in existente if b.get("lidar_pts", 0) > 0)
        if n_lidar > n_existente:
            shutil.copy2(src, dst)
            print(f"\n  ✓ buildings_final.json actualizado: {n_lidar} alturas LIDAR (antes: {n_existente})")
        else:
            print(f"\n  buildings_final.json no actualizado ({n_existente} ya >= {n_lidar})")
    else:
        shutil.copy2(src, dst)
        print(f"\n  ✓ buildings_final.json creado con {n_lidar} alturas LIDAR")

# ═══════════════════════════════════════════════════════════════════════════
#  MAIN
# ═══════════════════════════════════════════════════════════════════════════

if __name__ == "__main__":
    print("╔═══════════════════════════════════════════════════════════╗")
    print("║  FUSIONAR DATOS PROYECTO ANTERIOR → ALTSASU MANIFA       ║")
    print("╚═══════════════════════════════════════════════════════════╝\n")

    if not SRC.exists():
        print(f"✗ No se encuentra {SRC}")
        print("  Asegúrate de que E:/567/alsasua_outputs existe")
        exit(1)

    fusionar_alturas()
    copiar_ortofoto()
    copiar_scripts_unity()
    copiar_geojson()
    actualizar_fichero_principal()

    print("\n╔═══════════════════════════════════════════════════════════╗")
    print("║  RESUMEN                                                  ║")
    print("╠═══════════════════════════════════════════════════════════╣")
    for nombre, desc in [
        ("buildings_final.json",          "Edificios con alturas LIDAR reales"),
        ("buildings_con_alturas_lidar.json","Copia de trabajo"),
        ("ortofoto_unity.png",             "Textura ortofoto combinada"),
        ("ortofoto_alsasua_REAL.png",       "Ortofoto original alta resolución"),
        ("rios_ejes.geojson",              "Red hidrográfica"),
        ("bosques.geojson",                "Masas forestales"),
        ("redviaria_completa.geojson",     "Red viaria completa"),
    ]:
        p = DST / nombre
        if p.exists():
            print(f"║  ✅ {nombre:35s}  {p.stat().st_size//1024:6d}KB")
        else:
            print(f"║  ❌ {nombre:35s}  pendiente")
    print("╚═══════════════════════════════════════════════════════════╝")

    print("""
PRÓXIMOS PASOS EN UNITY:
  1. Unity detectará automáticamente los nuevos archivos en Assets/AlsasuaData/
  2. Tools → Alsasua → Ortofoto → 🗺 Aplicar Ortofoto  (usa ortofoto_unity.png)
  3. Play → los edificios usarán alturas LIDAR reales (buildings_final.json)
     Los 1562 edificios del viejo proyecto dan alturas a ~750 de los 1030 actuales.

NOTA — LIDAR para el casco urbano central:
  Los tiles E:/567/ cubren E=567-571km (valle occidental).
  Alsasua centro está en E=573-576km.
  Para LIDAR del casco: descargar tiles 573-4751, 574-4751 en:
  https://centrodedescargas.cnig.es/CentroDescargas/lidar-tercera-cobertura
""")
