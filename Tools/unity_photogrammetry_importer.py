#!/usr/bin/env python3
"""
UNITY PHOTOGRAMMETRY IMPORTER — Altsasu Manifa
===============================================
Actualiza el proyecto Unity para usar mallas fotogramétricas donde están disponibles.

Lee    : Assets/Models/Buildings_Photogrammetry/*.fbx  (generados por Meshroom+Blender)
Lee    : Assets/AlsasuaData/photo_building_mapping.json
Escribe: Assets/AlsasuaData/buildings_fusion_final.json  (añade campos photogrammetry_*)
Genera : Assets/AlsasuaData/photogrammetry_report.json  (estadísticas completas)

Los campos añadidos permiten que ImportadorEdificiosFBX.cs priorice mallas
fotogramétricas sobre arquetipos procedurales de SistemaEdificiosAAA.cs.

Campos añadidos por edificio:
  "photogrammetry_fbx":       "Assets/Models/Buildings_Photogrammetry/{id}.fbx"
  "photogrammetry_quality":   "high" | "medium" | "low"
  "photogrammetry_tris_lod0": N
  "photogrammetry_normal":    "Assets/AlsasuaData/FacadeTextures/Photogrammetry/{id}_normal.png"
  "photogrammetry_ao":        "Assets/AlsasuaData/FacadeTextures/Photogrammetry/{id}_ao.png"
  "photogrammetry_albedo":    "Assets/AlsasuaData/FacadeTextures/Photogrammetry/{id}_albedo.png"

Uso:
    python Tools/unity_photogrammetry_importer.py
    python Tools/unity_photogrammetry_importer.py --dry-run
    python Tools/unity_photogrammetry_importer.py --report-only
"""

import argparse
import json
import os
import sys
from collections import defaultdict
from datetime import datetime
from pathlib import Path

# ─── RUTAS ────────────────────────────────────────────────────────────────────

SCRIPT_DIR   = Path(__file__).resolve().parent
PROJECT_ROOT = SCRIPT_DIR.parent

FBX_DIR     = PROJECT_ROOT / "Assets" / "Models" / "Buildings_Photogrammetry"
TEX_DIR     = PROJECT_ROOT / "Assets" / "AlsasuaData" / "FacadeTextures" / "Photogrammetry"
QUALITY_DIR = FBX_DIR      # los {id}_quality.json se guardan junto a los FBX

MAPPING_JSON      = PROJECT_ROOT / "Assets" / "AlsasuaData" / "photo_building_mapping.json"
FUSION_JSON       = PROJECT_ROOT / "Assets" / "AlsasuaData" / "buildings_fusion_final.json"
REPORT_JSON       = PROJECT_ROOT / "Assets" / "AlsasuaData" / "photogrammetry_report.json"
PROGRESS_JSON     = Path(r"E:\MeshroomCache\progress.json")  # solo en Windows

# Rutas Unity (relativas a Assets/, con / no \)
UNITY_FBX_PREFIX = "Assets/Models/Buildings_Photogrammetry"
UNITY_TEX_PREFIX = "Assets/AlsasuaData/FacadeTextures/Photogrammetry"


# ─── UTILIDADES ───────────────────────────────────────────────────────────────

def log(msg: str, level: str = "INFO"):
    icons = {"INFO": "·", "OK": "✓", "WARN": "!", "ERR": "✗"}
    print(f"  [{icons.get(level,'·')}] {msg}", flush=True)


def unity_path(absolute: Path) -> str:
    """Convierte ruta absoluta a ruta relativa Unity (Assets/...)."""
    try:
        rel = absolute.relative_to(PROJECT_ROOT)
        return str(rel).replace("\\", "/")
    except ValueError:
        return str(absolute).replace("\\", "/")


def fbx_to_unity(fbx_path: Path) -> str:
    return f"{UNITY_FBX_PREFIX}/{fbx_path.name}"


def tex_to_unity(tex_path: Path) -> str:
    return f"{UNITY_TEX_PREFIX}/{tex_path.name}"


# ─── ESCANEAR FBX GENERADOS ──────────────────────────────────────────────────

def scan_photogrammetry_fbx() -> dict:
    """
    Escanea Assets/Models/Buildings_Photogrammetry/ buscando FBX.
    Lee el {id}_quality.json asociado si existe.
    Devuelve: {edificio_id: {fbx_path, quality_data, tex_paths}}
    """
    results = {}

    if not FBX_DIR.exists():
        log(f"Directorio FBX no encontrado: {FBX_DIR}", "WARN")
        return results

    fbx_files = list(FBX_DIR.glob("*.fbx"))
    if not fbx_files:
        log(f"No se encontraron FBX en {FBX_DIR}", "WARN")
        return results

    log(f"FBX encontrados: {len(fbx_files)}")

    for fbx_path in sorted(fbx_files):
        eid = fbx_path.stem  # nombre sin extensión = edificio_id

        entry = {
            "edificio_id": eid,
            "fbx_path":    fbx_path,
            "fbx_size_mb": round(fbx_path.stat().st_size / 1_048_576, 2),
            "quality":     None,
            "tris_lod0":   None,
            "tris_lod1":   None,
            "tris_lod2":   None,
            "tris_lod3":   None,
            "uv_coverage": None,
            "delight":     None,
        }

        # Leer quality report si existe
        quality_json = QUALITY_DIR / f"{eid}_quality.json"
        if quality_json.exists():
            try:
                with open(quality_json, "r", encoding="utf-8") as f:
                    q = json.load(f)
                entry.update({
                    "tris_lod0":   q.get("tris_lod0"),
                    "tris_lod1":   q.get("tris_lod1"),
                    "tris_lod2":   q.get("tris_lod2"),
                    "tris_lod3":   q.get("tris_lod3"),
                    "uv_coverage": q.get("uv_coverage_pct"),
                    "delight":     q.get("delight_applied"),
                })
            except json.JSONDecodeError:
                pass

        # Buscar texturas asociadas
        albedo_path = TEX_DIR / f"{eid}_albedo.png"
        normal_path = TEX_DIR / f"{eid}_normal.png"
        ao_path     = TEX_DIR / f"{eid}_ao.png"

        entry["albedo_exists"] = albedo_path.exists()
        entry["normal_exists"] = normal_path.exists()
        entry["ao_exists"]     = ao_path.exists()
        entry["albedo_path"]   = albedo_path if albedo_path.exists() else None
        entry["normal_path"]   = normal_path if normal_path.exists() else None
        entry["ao_path"]       = ao_path     if ao_path.exists()     else None

        results[eid] = entry

    return results


# ─── CALCULAR CALIDAD DESDE MAPPING ──────────────────────────────────────────

def load_photo_counts() -> dict:
    """
    Lee photo_building_mapping.json para obtener el conteo de fotos por edificio.
    Devuelve: {edificio_id: {"n_fotos": N, "zona": str, "n_angulos": N}}
    """
    if not MAPPING_JSON.exists():
        log(f"photo_building_mapping.json no encontrado: {MAPPING_JSON}", "WARN")
        return {}

    with open(MAPPING_JSON, "r", encoding="utf-8") as f:
        data = json.load(f)

    counts: dict = defaultdict(lambda: {"n_fotos": 0, "zona": "", "zonas": set()})
    for entry in data.get("mappings", []):
        eid  = entry.get("edificio_id", "")
        zona = entry.get("zona", "")
        if eid:
            counts[eid]["n_fotos"] += 1
            counts[eid]["zona"]     = zona
            counts[eid]["zonas"].add(zona)

    # Convertir sets a listas para serialización
    result = {}
    for eid, v in counts.items():
        n = v["n_fotos"]
        angulos = max(1, n // 4)
        if n >= 8 and angulos >= 3:
            quality = "high"
        elif n >= 4:
            quality = "medium"
        else:
            quality = "low"
        result[eid] = {
            "n_fotos":   n,
            "zona":      v["zona"],
            "n_angulos": angulos,
            "quality":   quality,
        }
    return result


# ─── CARGAR Y ACTUALIZAR buildings_fusion_final.json ─────────────────────────

def load_buildings_fusion() -> dict:
    """Lee buildings_fusion_final.json. Devuelve dict vacío si no existe."""
    if not FUSION_JSON.exists():
        log(f"buildings_fusion_final.json no encontrado: {FUSION_JSON}", "WARN")
        log("  Se creará photogrammetry_report.json igualmente.", "INFO")
        return {}

    try:
        with open(FUSION_JSON, "r", encoding="utf-8") as f:
            data = json.load(f)
        # Puede ser lista o dict con clave "buildings"
        if isinstance(data, list):
            return {str(b.get("id", b.get("osm_id", i))): b
                    for i, b in enumerate(data)}
        elif isinstance(data, dict) and "buildings" in data:
            return {str(b.get("id", b.get("osm_id", i))): b
                    for i, b in enumerate(data["buildings"])}
        elif isinstance(data, dict):
            return data
    except (json.JSONDecodeError, KeyError) as e:
        log(f"Error leyendo buildings_fusion_final.json: {e}", "WARN")
    return {}


def update_buildings_fusion(buildings_map: dict, fbx_data: dict,
                             photo_counts: dict, dry_run: bool) -> int:
    """
    Añade/actualiza los campos photogrammetry_* en buildings_fusion_final.json.
    Devuelve el número de edificios actualizados.
    """
    updated = 0

    for eid, fbx_entry in fbx_data.items():
        if eid not in buildings_map:
            # Edificio con FBX pero sin entrada en fusion: crear entrada mínima
            buildings_map[eid] = {"id": eid}

        b = buildings_map[eid]

        # Determinar calidad (quality report > photo_counts)
        quality = fbx_entry.get("quality")
        if not quality:
            pc = photo_counts.get(eid, {})
            quality = pc.get("quality", "low")

        # Campos photogrammetry_*
        phot_fields = {
            "photogrammetry_fbx":       fbx_to_unity(fbx_entry["fbx_path"]),
            "photogrammetry_quality":    quality,
            "photogrammetry_tris_lod0":  fbx_entry.get("tris_lod0"),
            "photogrammetry_tris_lod1":  fbx_entry.get("tris_lod1"),
            "photogrammetry_tris_lod2":  fbx_entry.get("tris_lod2"),
            "photogrammetry_tris_lod3":  fbx_entry.get("tris_lod3"),
            "photogrammetry_uv_coverage": fbx_entry.get("uv_coverage"),
            "photogrammetry_delit":       fbx_entry.get("delight"),
            "photogrammetry_updated":     datetime.now().isoformat(),
        }

        if fbx_entry.get("albedo_path"):
            phot_fields["photogrammetry_albedo"] = tex_to_unity(fbx_entry["albedo_path"])
        if fbx_entry.get("normal_path"):
            phot_fields["photogrammetry_normal"] = tex_to_unity(fbx_entry["normal_path"])
        if fbx_entry.get("ao_path"):
            phot_fields["photogrammetry_ao"] = tex_to_unity(fbx_entry["ao_path"])

        b.update(phot_fields)
        updated += 1

    if not dry_run and updated > 0 and FUSION_JSON.exists():
        # Reescribir preservando formato original
        try:
            with open(FUSION_JSON, "r", encoding="utf-8") as f:
                raw = json.load(f)

            if isinstance(raw, list):
                # Actualizar edificios en lista por id
                id_to_idx = {}
                for i, b in enumerate(raw):
                    bid = str(b.get("id", b.get("osm_id", "")))
                    if bid:
                        id_to_idx[bid] = i
                for eid, b in buildings_map.items():
                    if eid in id_to_idx:
                        raw[id_to_idx[eid]].update(b)
                    else:
                        raw.append(b)
                with open(FUSION_JSON, "w", encoding="utf-8") as f:
                    json.dump(raw, f, indent=2, ensure_ascii=False)

            elif isinstance(raw, dict) and "buildings" in raw:
                id_to_idx = {}
                for i, b in enumerate(raw["buildings"]):
                    bid = str(b.get("id", b.get("osm_id", "")))
                    if bid:
                        id_to_idx[bid] = i
                for eid, b in buildings_map.items():
                    if eid in id_to_idx:
                        raw["buildings"][id_to_idx[eid]].update(b)
                    else:
                        raw["buildings"].append(b)
                raw["photogrammetry_updated"] = datetime.now().isoformat()
                with open(FUSION_JSON, "w", encoding="utf-8") as f:
                    json.dump(raw, f, indent=2, ensure_ascii=False)

            log(f"buildings_fusion_final.json actualizado ({updated} edificios)", "OK")
        except Exception as e:
            log(f"Error escribiendo buildings_fusion_final.json: {e}", "ERR")
    elif dry_run:
        log(f"[DRY-RUN] Se actualizarían {updated} edificios en buildings_fusion_final.json")

    return updated


# ─── GENERAR REPORTE ─────────────────────────────────────────────────────────

def generate_report(fbx_data: dict, photo_counts: dict,
                    updated_count: int, dry_run: bool) -> dict:
    """
    Genera photogrammetry_report.json con estadísticas completas.
    """
    by_quality = defaultdict(list)
    by_zona    = defaultdict(list)

    edificios = []
    for eid, entry in fbx_data.items():
        pc = photo_counts.get(eid, {})

        quality = entry.get("quality")
        if not quality:
            quality = pc.get("quality", "low")

        zona = pc.get("zona", "unknown")
        n_fotos = pc.get("n_fotos", 0)

        e_record = {
            "edificio_id":    eid,
            "zona":           zona,
            "quality":        quality,
            "n_fotos":        n_fotos,
            "n_angulos":      pc.get("n_angulos", 0),
            "fbx_size_mb":    entry.get("fbx_size_mb"),
            "tris_lod0":      entry.get("tris_lod0"),
            "tris_lod1":      entry.get("tris_lod1"),
            "tris_lod2":      entry.get("tris_lod2"),
            "tris_lod3":      entry.get("tris_lod3"),
            "uv_coverage":    entry.get("uv_coverage"),
            "albedo_ok":      entry.get("albedo_exists", False),
            "normal_ok":      entry.get("normal_exists", False),
            "ao_ok":          entry.get("ao_exists", False),
            "delit_applied":  entry.get("delight", False),
            "unity_fbx":      fbx_to_unity(entry["fbx_path"]),
        }
        edificios.append(e_record)
        by_quality[quality].append(eid)
        by_zona[zona].append(eid)

    report = {
        "status":            "done" if edificios else "pending",
        "generated":         datetime.now().isoformat(),
        "total_fbx":         len(fbx_data),
        "total_with_albedo": sum(1 for e in edificios if e["albedo_ok"]),
        "total_with_normal": sum(1 for e in edificios if e["normal_ok"]),
        "total_with_ao":     sum(1 for e in edificios if e["ao_ok"]),
        "buildings_updated_in_fusion": updated_count,
        "by_quality": {
            "high":   len(by_quality["high"]),
            "medium": len(by_quality["medium"]),
            "low":    len(by_quality["low"]),
        },
        "by_zona": {zona: len(ids) for zona, ids in sorted(by_zona.items())},
        "edificios": edificios,
        "paths": {
            "fbx_dir":          unity_path(FBX_DIR),
            "texture_dir":      unity_path(TEX_DIR),
            "buildings_fusion": unity_path(FUSION_JSON),
        },
        "unity_integration": {
            "script_to_use":    "ImportadorEdificiosFBX.cs",
            "priority_field":   "photogrammetry_fbx",
            "fallback":         "SistemaEdificiosAAA.cs arquetipos procedurales",
            "lod_convention":   "{id}_LOD0 ... {id}_LOD3",
            "normal_space":     "DirectX (Unity HDRP compatible)",
        },
    }

    if not dry_run:
        with open(REPORT_JSON, "w", encoding="utf-8") as f:
            json.dump(report, f, indent=2, ensure_ascii=False)
        log(f"photogrammetry_report.json guardado: {REPORT_JSON}", "OK")
    else:
        log("[DRY-RUN] photogrammetry_report.json NO guardado")

    return report


# ─── IMPRIMIR RESUMEN ────────────────────────────────────────────────────────

def print_summary(report: dict):
    print("\n" + "═" * 70)
    print("  UNITY PHOTOGRAMMETRY IMPORTER — Altsasu Manifa")
    print("═" * 70)
    print(f"  FBX procesados        : {report['total_fbx']}")
    print(f"  Con albedo texture    : {report['total_with_albedo']}")
    print(f"  Con normal map        : {report['total_with_normal']}")
    print(f"  Con AO map            : {report['total_with_ao']}")
    print(f"  Actualizados en fusion: {report['buildings_updated_in_fusion']}")
    print()
    print(f"  Calidad:")
    q = report["by_quality"]
    print(f"    high   : {q.get('high', 0)}")
    print(f"    medium : {q.get('medium', 0)}")
    print(f"    low    : {q.get('low', 0)}")
    print()
    print(f"  Por zona:")
    for zona, n in sorted(report["by_zona"].items()):
        print(f"    {zona:<22}: {n} edificios")
    print()

    if report["edificios"]:
        print("  EDIFICIOS CON FOTOGRAMETRÍA:")
        for e in report["edificios"]:
            tris = f"{e['tris_lod0']:,}" if e.get("tris_lod0") else "N/A"
            qual = e["quality"]
            maps = ("A" if e["albedo_ok"] else "·") + \
                   ("N" if e["normal_ok"] else "·") + \
                   ("O" if e["ao_ok"] else "·")
            print(f"    [{qual:6s}] [{maps}]  {e['edificio_id']:<20}  "
                  f"{e['n_fotos']:>3} fotos  LOD0={tris} tris")

    print()
    print(f"  Reporte : {REPORT_JSON}")
    print(f"  Fusion  : {FUSION_JSON}")
    print("═" * 70 + "\n")
    print("  INTEGRACIÓN UNITY:")
    print("  Añade campo 'photogrammetry_fbx' en buildings_fusion_final.json.")
    print("  ImportadorEdificiosFBX.cs debe leer este campo y priorizar el FBX")
    print("  fotogramétrico sobre el arquetipo procedural de SistemaEdificiosAAA.")
    print("  LODs: {id}_LOD0..LOD3 dentro del FBX (Unity LODGroup automático).")
    print("  Normal maps: espacio DirectX, flip G ya aplicado.\n")


# ─── MAIN ─────────────────────────────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser(
        description="Unity photogrammetry importer — Altsasu Manifa"
    )
    parser.add_argument("--dry-run",     action="store_true",
                        help="Muestra qué se haría sin modificar archivos")
    parser.add_argument("--report-only", action="store_true",
                        help="Solo genera photogrammetry_report.json, sin tocar buildings_fusion_final.json")
    args = parser.parse_args()

    print("\n" + "═" * 70)
    print("  UNITY PHOTOGRAMMETRY IMPORTER — Altsasu Manifa")
    print(f"  {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
    if args.dry_run:
        print("  MODO: DRY-RUN (no se modificarán archivos)")
    print("═" * 70 + "\n")

    # 1. Escanear FBX generados
    log("Escaneando FBX fotogramétricos...")
    fbx_data = scan_photogrammetry_fbx()
    if not fbx_data:
        log("No se encontraron FBX fotogramétricos. Ejecuta primero meshroom_pipeline.py", "WARN")
        # Igualmente generar reporte vacío
        report = {"status": "pending", "edificios": [], "total_fbx": 0}
        if not args.dry_run:
            with open(REPORT_JSON, "w", encoding="utf-8") as f:
                json.dump(report, f, indent=2)
        sys.exit(0)

    # 2. Cargar conteos de fotos para calcular calidad
    log("Cargando photo_building_mapping.json...")
    photo_counts = load_photo_counts()

    # 3. Actualizar buildings_fusion_final.json
    updated_count = 0
    if not args.report_only:
        log("Actualizando buildings_fusion_final.json...")
        buildings_map = load_buildings_fusion()
        updated_count = update_buildings_fusion(
            buildings_map, fbx_data, photo_counts,
            dry_run=args.dry_run
        )

    # 4. Generar reporte completo
    log("Generando photogrammetry_report.json...")
    report = generate_report(fbx_data, photo_counts, updated_count, dry_run=args.dry_run)

    # 5. Resumen
    print_summary(report)


if __name__ == "__main__":
    main()
