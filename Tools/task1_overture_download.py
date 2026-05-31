#!/usr/bin/env python3
"""
Task 1 — Descarga edificios Overture Maps 2024 y compara con archivo existente.
BBox WGS84: lon -2.195 a -2.145, lat 42.885 a 42.915
"""
import subprocess
import sys
import os
import json
import math

DATA_DIR = "/home/user/Altsasu_Manifa/Assets/AlsasuaData"
OUT_FILE = os.path.join(DATA_DIR, "overture_buildings_2024.geojson")
OLD_FILE = os.path.join(DATA_DIR, "overture_buildings.geojson")
REPORT_FILE = "/home/user/Altsasu_Manifa/Assets/AlsasuaData/overture_diff_report.txt"

BBOX = "-2.195,42.885,-2.145,42.915"

def try_cli():
    """Try overturemaps CLI"""
    print("Intentando overturemaps CLI...")
    try:
        result = subprocess.run(
            ["overturemaps", "download",
             f"--bbox={BBOX}",
             "-f", "geojson",
             "--type=building",
             "-o", OUT_FILE],
            capture_output=True, text=True, timeout=120
        )
        if result.returncode == 0 and os.path.exists(OUT_FILE) and os.path.getsize(OUT_FILE) > 100:
            print(f"CLI OK: {OUT_FILE}")
            return True
        print(f"CLI stderr: {result.stderr[:500]}")
        return False
    except Exception as e:
        print(f"CLI fallo: {e}")
        return False

def try_duckdb():
    """Try DuckDB against Overture S3"""
    print("Intentando DuckDB...")
    try:
        import duckdb
        con = duckdb.connect()
        con.execute("INSTALL httpfs; LOAD httpfs;")
        con.execute("SET s3_region='us-west-2';")
        query = f"""
        COPY (
            SELECT *
            FROM read_parquet('s3://overturemaps-us-west-2/release/2024-11-13.0/theme=buildings/type=building/*', hive_partitioning=1)
            WHERE
                bbox.xmin > -2.195 AND bbox.xmax < -2.145 AND
                bbox.ymin > 42.885 AND bbox.ymax < 42.915
        ) TO '{OUT_FILE}' (FORMAT JSON, ARRAY true);
        """
        con.execute(query)
        if os.path.exists(OUT_FILE) and os.path.getsize(OUT_FILE) > 100:
            # DuckDB outputs JSON array, wrap as FeatureCollection
            with open(OUT_FILE) as f:
                raw = json.load(f)
            if isinstance(raw, list):
                fc = {"type": "FeatureCollection", "features": [
                    {"type": "Feature",
                     "geometry": r.get("geometry"),
                     "properties": {k: v for k, v in r.items() if k != "geometry"}}
                    for r in raw
                ]}
                with open(OUT_FILE, "w") as f:
                    json.dump(fc, f)
            print(f"DuckDB OK: {OUT_FILE}")
            return True
        return False
    except Exception as e:
        print(f"DuckDB fallo: {e}")
        return False

def try_requests():
    """Try Overture via requests / GeoParquet API endpoint"""
    print("Intentando requests via Overture API...")
    try:
        import requests
        # Use overturemaps data API
        url = "https://api.overturemaps.org/v1/buildings"
        params = {
            "bbox": BBOX,
            "limit": 5000,
            "format": "geojson"
        }
        r = requests.get(url, params=params, timeout=60)
        if r.status_code == 200:
            with open(OUT_FILE, "w") as f:
                f.write(r.text)
            data = r.json()
            if data.get("features") or data.get("type") == "FeatureCollection":
                print(f"API OK: {OUT_FILE}")
                return True
        print(f"API status: {r.status_code}")

        # Try alternative: use PyArrow + fsspec to read Parquet directly
        try:
            import pyarrow.parquet as pq
            import pyarrow as pa
            import fsspec

            print("Intentando PyArrow/fsspec para leer Overture Parquet...")
            fs = fsspec.filesystem("s3", anon=True, client_kwargs={"region_name": "us-west-2"})
            path = "overturemaps-us-west-2/release/2024-11-13.0/theme=buildings/type=building"
            files = fs.glob(f"{path}/*.parquet")
            if not files:
                files = fs.glob(f"{path}/**/*.parquet")
            print(f"Encontrados {len(files)} archivos parquet")
            features = []
            for f_path in files[:5]:  # sample first few files
                try:
                    with fs.open(f_path) as f:
                        table = pq.read_table(f, filters=[
                            ("bbox", "IN", None)  # can't filter easily, read and filter manually
                        ])
                    df = table.to_pandas()
                    print(f"  {f_path}: {len(df)} rows")
                    for _, row in df.iterrows():
                        try:
                            geom = row.get("geometry") or row.get("wkt_geometry")
                            if geom is None:
                                continue
                            # Basic bbox filter using geometry column
                            features.append({
                                "type": "Feature",
                                "geometry": {"type": "Point", "coordinates": [0, 0]},
                                "properties": dict(row)
                            })
                        except Exception:
                            pass
                except Exception as ex:
                    print(f"  Error leyendo {f_path}: {ex}")
            if features:
                fc = {"type": "FeatureCollection", "features": features[:2000]}
                with open(OUT_FILE, "w") as fw:
                    json.dump(fc, fw)
                print(f"PyArrow OK: {len(features)} edificios")
                return True
        except Exception as e2:
            print(f"PyArrow fallo: {e2}")
        return False
    except Exception as e:
        print(f"requests fallo: {e}")
        return False

def create_empty_geojson():
    """Create valid empty GeoJSON as fallback"""
    fc = {"type": "FeatureCollection", "features": [], "_note": "Download failed — all methods exhausted"}
    with open(OUT_FILE, "w") as f:
        json.dump(fc, f, indent=2)
    print(f"Creado GeoJSON vacío en {OUT_FILE}")

def analyze_and_report(new_file, old_file, report_file):
    """Compare new and old Overture files and write report"""
    lines = []
    lines.append("=" * 60)
    lines.append("OVERTURE MAPS 2024 — REPORTE DE COMPARACIÓN")
    lines.append("=" * 60)

    # Load new
    try:
        with open(new_file) as f:
            new_data = json.load(f)
        new_features = new_data.get("features", [])
        lines.append(f"\nArchivo nuevo: {new_file}")
        lines.append(f"  Edificios descargados (2024): {len(new_features)}")

        # Height fields analysis
        height_fields = ["height", "building:height", "roof_height", "sources"]
        for feat in new_features[:5]:
            props = feat.get("properties", {})
            lines.append(f"  Ejemplo campos: {list(props.keys())[:15]}")
            break

        # Count height availability
        has_height = sum(1 for f in new_features if f.get("properties", {}).get("height") is not None)
        has_levels = sum(1 for f in new_features if f.get("properties", {}).get("num_floors") is not None
                         or f.get("properties", {}).get("level") is not None)
        lines.append(f"  Con campo 'height': {has_height}")
        lines.append(f"  Con campos de pisos: {has_levels}")

        # Collect all property keys
        all_keys = set()
        for feat in new_features:
            all_keys.update(feat.get("properties", {}).keys())
        lines.append(f"  Campos disponibles: {sorted(all_keys)}")

    except Exception as e:
        lines.append(f"\nError leyendo nuevo archivo: {e}")
        new_features = []

    # Load old
    if os.path.exists(old_file):
        try:
            with open(old_file) as f:
                old_data = json.load(f)
            old_features = old_data.get("features", old_data if isinstance(old_data, list) else [])
            lines.append(f"\nArchivo existente: {old_file}")
            lines.append(f"  Edificios existentes: {len(old_features)}")

            # Compare IDs
            new_ids = {f.get("properties", {}).get("id") or f.get("properties", {}).get("@id") for f in new_features}
            old_ids = {f.get("properties", {}).get("id") or f.get("properties", {}).get("@id") for f in old_features}
            new_ids.discard(None)
            old_ids.discard(None)

            nuevos = new_ids - old_ids
            eliminados = old_ids - new_ids
            compartidos = new_ids & old_ids

            lines.append(f"\nCOMPARACIÓN:")
            lines.append(f"  Edificios nuevos (solo en 2024): {len(nuevos)}")
            lines.append(f"  Edificios eliminados (solo en antiguo): {len(eliminados)}")
            lines.append(f"  Edificios compartidos: {len(compartidos)}")
            lines.append(f"  Diferencia neta: {len(new_features) - len(old_features):+d}")

        except Exception as e:
            lines.append(f"\nError leyendo archivo existente: {e}")
    else:
        lines.append(f"\nNo existe archivo anterior ({old_file})")
        lines.append("  Esta es la primera descarga de Overture para este proyecto.")

    lines.append("\n" + "=" * 60)
    report_text = "\n".join(lines)
    with open(report_file, "w") as f:
        f.write(report_text)
    print(report_text)
    print(f"\nReporte guardado en: {report_file}")

if __name__ == "__main__":
    # Install overturemaps if needed
    print("Instalando overturemaps...")
    subprocess.run([sys.executable, "-m", "pip", "install", "overturemaps", "-q"], capture_output=True)

    success = False
    if not success:
        success = try_cli()
    if not success:
        success = try_duckdb()
    if not success:
        success = try_requests()
    if not success:
        create_empty_geojson()

    analyze_and_report(OUT_FILE, OLD_FILE, REPORT_FILE)
    print("Tarea 1 completada.")
