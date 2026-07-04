#!/usr/bin/env python3
"""
DescargarPOIsAlsasua.py
========================
Descarga nodos POI de OpenStreetMap (Overpass API) para Alsasua/Altsasu
y los convierte a coordenadas Unity para su uso como marcadores en el juego.

POIs incluidos: bares, restaurantes, tiendas, iglesias, escuelas, bancos,
ayuntamiento, farmacia, hospital, gasolinera, cementerio, cuarteles, etc.

Salida: Assets/AlsasuaData/pois_unity.json

Uso:
    cd E:\Desk\DAM\Altsasu_Manifa
    python Tools/DescargarPOIsAlsasua.py
"""

import json, urllib.request, urllib.parse, os, math

# ── Sistema de coordenadas ────────────────────────────────────────────────
E0 = 567951.0; N0 = 4749902.0; OX = 1918.0; OZ = 8570.0

def utm_to_unity(lat, lon):
    """Convierte lat/lon WGS84 a coordenadas Unity (mismo origen que el resto del mundo)."""
    # Proyección UTM 30N (EPSG:25830) - fórmula simplificada de alta precisión
    k0 = 0.9996; a = 6378137.0; e2 = 0.00669437999014
    lon0 = math.radians(-3.0)  # meridiano central zona 30
    lat_r = math.radians(lat); lon_r = math.radians(lon)
    N = a / math.sqrt(1 - e2 * math.sin(lat_r)**2)
    T = math.tan(lat_r)**2; C = e2 / (1 - e2) * math.cos(lat_r)**2
    A = math.cos(lat_r) * (lon_r - lon0)
    e4 = e2*e2; e6 = e4*e2
    M = a * ((1 - e2/4 - 3*e4/64 - 5*e6/256) * lat_r
             - (3*e2/8 + 3*e4/32 + 45*e6/1024) * math.sin(2*lat_r)
             + (15*e4/256 + 45*e6/1024) * math.sin(4*lat_r)
             - (35*e6/3072) * math.sin(6*lat_r))
    E = k0 * N * (A + (1-T+C)*A**3/6 + (5-18*T+T**2+72*C-58*e2/(1-e2))*A**5/120) + 500000
    N_utm = k0 * (M + N*math.tan(lat_r)*(A**2/2 + (5-T+9*C+4*C**2)*A**4/24
                  + (61-58*T+T**2+600*C-330*e2/(1-e2))*A**6/720))
    ux = (E - E0) + OX
    uz = (N_utm - N0) + OZ
    return ux, uz

# ── Consulta Overpass ─────────────────────────────────────────────────────
# BBox amplio alrededor de Alsasua (lat_min, lon_min, lat_max, lon_max)
BBOX = "42.870,  -2.200, 42.930, -2.130"

QUERY = f"""
[out:json][timeout:30];
node["amenity"](42.870,-2.200,42.930,-2.130);
out body;
"""

OVERPASS_URL = "https://overpass.openstreetmap.ru/api/interpreter"

# ── Categorización de iconos/tipos ────────────────────────────────────────
def categorizar(tags: dict) -> str:
    a = tags.get("amenity", "")
    s = tags.get("shop", "")
    t = tags.get("tourism", "")
    h = tags.get("historic", "")
    l = tags.get("leisure", "")
    o = tags.get("office", "")
    # Orden de prioridad
    if a == "bar" or a == "pub":         return "bar"
    if a == "restaurant" or a == "cafe": return "restaurante"
    if a == "fast_food":                 return "comida_rapida"
    if a == "place_of_worship":          return "iglesia"
    if a == "school" or a == "college":  return "escuela"
    if a == "bank":                      return "banco"
    if a == "pharmacy":                  return "farmacia"
    if a == "hospital" or a == "clinic": return "hospital"
    if a == "fuel":                      return "gasolinera"
    if a == "police":                    return "policia"
    if a == "townhall":                  return "ayuntamiento"
    if a == "fire_station":              return "bomberos"
    if a == "post_office":               return "correos"
    if a == "library":                   return "biblioteca"
    if a == "marketplace":               return "mercado"
    if a == "parking":                   return "parking"
    if a == "toilets":                   return "wc"
    if a == "cinema" or a == "theatre":  return "cine_teatro"
    if a == "community_centre":          return "centro_civico"
    if a == "social_facility":           return "servicio_social"
    if s == "supermarket" or s == "convenience": return "supermercado"
    if s == "bakery":                    return "panaderia"
    if s == "butcher":                   return "carniceria"
    if s == "clothes":                   return "ropa"
    if s == "hardware":                  return "ferreteria"
    if s:                                return f"tienda_{s}"
    if t == "hotel" or t == "hostel":    return "hotel"
    if t == "information":               return "turismo"
    if t:                                return f"turismo_{t}"
    if h == "memorial" or h == "monument": return "monumento"
    if h == "castle" or h == "ruins":    return "historico"
    if h:                                return f"historico_{h}"
    if l == "sports_centre":             return "polideportivo"
    if l == "park" or l == "garden":     return "parque"
    if l:                                return f"ocio_{l}"
    if o == "government":                return "gobierno"
    if o:                                return f"oficina_{o}"
    return "poi_generico"

# ── Main ──────────────────────────────────────────────────────────────────
def main():
    print("Consultando Overpass API para POIs de Alsasua…")
    data_encoded = urllib.parse.urlencode({"data": QUERY}).encode()
    req = urllib.request.Request(OVERPASS_URL, data=data_encoded,
                                  headers={"User-Agent": "AlsasuaManifa/1.0 Unity"})
    with urllib.request.urlopen(req, timeout=90) as resp:
        raw = json.loads(resp.read().decode())

    elementos = raw.get("elements", [])
    print(f"  {len(elementos)} nodos POI descargados.")

    pois = []
    for el in elementos:
        if el.get("type") != "node": continue
        lat, lon = el["lat"], el["lon"]
        ux, uz = utm_to_unity(lat, lon)
        tags = el.get("tags", {})
        nombre = tags.get("name", tags.get("name:eu", tags.get("name:es", "")))
        categoria = categorizar(tags)
        poi = {
            "osm_id":   el["id"],
            "nombre":   nombre,
            "categoria": categoria,
            "x":        round(ux, 3),
            "z":        round(uz, 3),
            "tags":     {k: v for k, v in tags.items()
                         if k in ("amenity","shop","tourism","historic","leisure","office",
                                  "name","name:eu","name:es","opening_hours","phone","website")}
        }
        pois.append(poi)
        if nombre:
            print(f"    [{categoria}] {nombre[:30]} ({ux:.1f}, {uz:.1f})")

    # Guardar
    out_path = os.path.join(os.path.dirname(os.path.dirname(__file__)),
                            "Assets", "AlsasuaData", "pois_unity.json")
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(pois, f, ensure_ascii=False, indent=2)

    # Resumen por categoría
    from collections import Counter
    cats = Counter(p["categoria"] for p in pois)
    print(f"\n✅ {len(pois)} POIs guardados en {out_path}")
    print("Categorías:")
    for cat, n in cats.most_common():
        print(f"  {cat}: {n}")

if __name__ == "__main__":
    main()
