#!/usr/bin/env python3
"""
Task 2 — Descarga footprints del Catastro de Navarra via WFS INSPIRE.
BBOX UTM30N ETRS89: 567451,4749402,568451,4750402
"""
import requests
import json
import os

DATA_DIR = "/home/user/Altsasu_Manifa/Assets/AlsasuaData"
OUT_FILE = os.path.join(DATA_DIR, "catastro_navarra_inspire.geojson")

EMPTY_FC = {"type": "FeatureCollection", "features": []}

def try_inspire_navarra():
    """WFS INSPIRE Navarra - Building"""
    url = "https://inspire.navarra.es/services/BU/wfs"
    params = {
        "SERVICE": "WFS",
        "VERSION": "2.0.0",
        "REQUEST": "GetFeature",
        "TYPENAMES": "BU:Building",
        "BBOX": "567451,4749402,568451,4750402,EPSG:25830",
        "outputFormat": "application/json",
        "count": "2000"
    }
    try:
        r = requests.get(url, params=params, timeout=30)
        print(f"INSPIRE Navarra status: {r.status_code}")
        print(f"Content-Type: {r.headers.get('Content-Type','?')}")
        if r.status_code == 200:
            content = r.text
            if "FeatureCollection" in content or "features" in content.lower():
                with open(OUT_FILE, "w", encoding="utf-8") as f:
                    f.write(content)
                try:
                    data = json.loads(content)
                    count = len(data.get("features", []))
                    print(f"OK: {count} features guardados en {OUT_FILE}")
                    return True, count
                except json.JSONDecodeError:
                    # Might be GML/XML
                    print("Respuesta no es JSON, guardando como texto para inspección")
                    with open(OUT_FILE + ".xml", "w") as f:
                        f.write(content)
                    return False, 0
            else:
                print(f"Respuesta inesperada: {content[:300]}")
        return False, 0
    except Exception as e:
        print(f"INSPIRE Navarra fallo: {e}")
        return False, 0

def try_inspire_navarra_gml():
    """WFS INSPIRE Navarra - Building con outputFormat GML"""
    url = "https://inspire.navarra.es/services/BU/wfs"
    params = {
        "SERVICE": "WFS",
        "VERSION": "2.0.0",
        "REQUEST": "GetFeature",
        "TYPENAMES": "BU:Building",
        "BBOX": "567451,4749402,568451,4750402,EPSG:25830",
        "outputFormat": "text/xml; subtype=gml/3.2",
        "count": "2000"
    }
    try:
        r = requests.get(url, params=params, timeout=30)
        print(f"INSPIRE GML status: {r.status_code}, size={len(r.content)}")
        if r.status_code == 200 and len(r.content) > 200:
            # Parse GML to GeoJSON using basic approach
            with open(OUT_FILE + ".gml", "wb") as f:
                f.write(r.content)
            print(f"GML guardado en {OUT_FILE}.gml ({len(r.content)} bytes)")
            # Try converting with ogr2ogr if available
            try:
                import subprocess
                result = subprocess.run(
                    ["ogr2ogr", "-f", "GeoJSON", OUT_FILE, OUT_FILE + ".gml"],
                    capture_output=True, text=True, timeout=30
                )
                if result.returncode == 0 and os.path.exists(OUT_FILE):
                    with open(OUT_FILE) as f:
                        data = json.load(f)
                    count = len(data.get("features", []))
                    print(f"ogr2ogr OK: {count} features")
                    return True, count
            except Exception as e:
                print(f"ogr2ogr fallo: {e}")
            return False, 0
        return False, 0
    except Exception as e:
        print(f"INSPIRE GML fallo: {e}")
        return False, 0

def try_idena_catastro():
    """IDENA WFS - Catastro Parcelas"""
    urls_to_try = [
        ("https://idena.navarra.es/ogc/wfs", "CATAST_Pol_Catastro"),
        ("https://idena.navarra.es/ogc/wfs", "CATAST_Pol_ParcelaUrb"),
        ("https://idena.navarra.es/ogc/wfs", "CATAST_Lin_LimMunicipio"),
    ]
    for url, typename in urls_to_try:
        params = {
            "SERVICE": "WFS",
            "VERSION": "2.0.0",
            "REQUEST": "GetFeature",
            "TYPENAME": typename,
            "BBOX": "567451,4749402,568451,4750402,EPSG:25830",
            "outputFormat": "application/json",
            "count": "2000"
        }
        try:
            r = requests.get(url, params=params, timeout=30)
            print(f"IDENA {typename} status: {r.status_code}")
            if r.status_code == 200:
                content = r.text
                if "FeatureCollection" in content:
                    with open(OUT_FILE, "w", encoding="utf-8") as f:
                        f.write(content)
                    data = json.loads(content)
                    count = len(data.get("features", []))
                    if count > 0:
                        print(f"IDENA OK: {count} features ({typename})")
                        return True, count
        except Exception as e:
            print(f"IDENA {typename} fallo: {e}")
    return False, 0

def try_idena_capabilities():
    """Get IDENA WFS capabilities to find right layer name"""
    url = "https://idena.navarra.es/ogc/wfs"
    params = {
        "SERVICE": "WFS",
        "VERSION": "2.0.0",
        "REQUEST": "GetCapabilities"
    }
    try:
        r = requests.get(url, params=params, timeout=20)
        print(f"IDENA Capabilities status: {r.status_code}")
        if r.status_code == 200:
            # Find layer names related to catastro/buildings
            text = r.text
            import re
            names = re.findall(r'<Name>([^<]*(?:CATAST|BUILD|Edifici|edifici)[^<]*)</Name>', text, re.IGNORECASE)
            print(f"Capas catastro/edificios encontradas: {names[:20]}")
            return names
    except Exception as e:
        print(f"Capabilities fallo: {e}")
    return []

def try_inspire_capabilities():
    """Get INSPIRE WFS capabilities"""
    url = "https://inspire.navarra.es/services/BU/wfs"
    params = {"SERVICE": "WFS", "VERSION": "2.0.0", "REQUEST": "GetCapabilities"}
    try:
        r = requests.get(url, params=params, timeout=20)
        print(f"INSPIRE Capabilities status: {r.status_code}")
        if r.status_code == 200:
            import re
            names = re.findall(r'<Name>([^<]+)</Name>', r.text)
            print(f"Capas INSPIRE disponibles: {names[:20]}")
    except Exception as e:
        print(f"INSPIRE Capabilities fallo: {e}")

def try_idena_dynamic(layer_names):
    """Try each discovered IDENA layer"""
    url = "https://idena.navarra.es/ogc/wfs"
    for typename in layer_names:
        for bbox in [
            "567451,4749402,568451,4750402,EPSG:25830",
            "-2.195,42.885,-2.145,42.915,EPSG:4326"
        ]:
            params = {
                "SERVICE": "WFS", "VERSION": "2.0.0", "REQUEST": "GetFeature",
                "TYPENAME": typename,
                "BBOX": bbox,
                "outputFormat": "application/json",
                "count": "2000"
            }
            try:
                r = requests.get(url, params=params, timeout=20)
                if r.status_code == 200 and "FeatureCollection" in r.text:
                    data = json.loads(r.text)
                    count = len(data.get("features", []))
                    if count > 0:
                        with open(OUT_FILE, "w", encoding="utf-8") as f:
                            f.write(r.text)
                        print(f"IDENA dinámico OK: {count} features ({typename})")
                        return True, count
            except Exception:
                pass
    return False, 0

def create_empty_with_error(errors):
    """Create valid empty GeoJSON with error info"""
    fc = EMPTY_FC.copy()
    fc["_errors"] = errors
    fc["_note"] = "Descarga fallida — todas las fuentes agotadas. Ver errores arriba."
    with open(OUT_FILE, "w") as f:
        json.dump(fc, f, indent=2)
    print(f"Creado GeoJSON vacío en {OUT_FILE}")

if __name__ == "__main__":
    errors = []
    success = False

    # Try INSPIRE JSON
    ok, count = try_inspire_navarra()
    if ok:
        success = True
    else:
        errors.append("INSPIRE Navarra JSON: fallo")

    if not success:
        # Try INSPIRE GML
        ok, count = try_inspire_navarra_gml()
        if ok:
            success = True
        else:
            errors.append("INSPIRE Navarra GML: fallo")

    if not success:
        # Get capabilities to find layer names
        try_inspire_capabilities()
        layer_names = try_idena_capabilities()

        # Try known IDENA layers
        ok, count = try_idena_catastro()
        if ok:
            success = True
        else:
            errors.append("IDENA catastro capas conocidas: fallo")

    if not success:
        if layer_names:
            ok, count = try_idena_dynamic(layer_names)
            if ok:
                success = True
            else:
                errors.append(f"IDENA capas dinámicas ({layer_names[:3]}): fallo")

    if not success:
        create_empty_with_error(errors)
        print(f"AVISO: No se pudo descargar datos del catastro. Errores: {errors}")
    else:
        print(f"Tarea 2 completada: {OUT_FILE}")

    print("Errores registrados:", errors)
