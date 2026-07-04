# -*- coding: utf-8 -*-
# AumentarOSM_AAA.py — enriquece roads/footways/railways con tags de OSM
# (layer, tunnel, bridge, lanes, surface) via Overpass, JOIN por osm id.
# No reproyecta: conserva las coords unity existentes; solo anade tags.
# Salida: *_aaa.json junto a los originales.
import json, urllib.request, urllib.parse, os

D = r"E:\Desk\DAM\Altsasu_Manifa\Assets\AlsasuaData"
BBOX = (42.850, -2.220, 42.940, -2.110)  # S, W, N, E (generoso, cubre 7.2 km)

QL = """[out:json][timeout:180];
(
  way["highway"](%f,%f,%f,%f);
  way["railway"](%f,%f,%f,%f);
);
out tags;""" % (BBOX+BBOX)

def overpass():
    for ep in ("https://overpass-api.de/api/interpreter",
               "https://overpass.kumi.systems/api/interpreter"):
        try:
            print("[OSM] consultando %s ..." % ep)
            req = urllib.request.Request(ep, data=urllib.parse.urlencode({"data": QL}).encode(),
                                         headers={"User-Agent": "AlsasuaSim/1.0"})
            with urllib.request.urlopen(req, timeout=200) as r:
                return json.load(r)
        except Exception as e:
            print("[OSM] fallo %s: %s" % (ep, e))
    return None

def to_int(v, d=0):
    try: return int(str(v).split(";")[0])
    except Exception: return d

def main():
    data = overpass()
    if not data:
        print("[OSM] sin respuesta"); return
    tags = {}
    for el in data.get("elements", []):
        if el.get("type") != "way": continue
        t = el.get("tags", {})
        tags[el["id"]] = {
            "layer":  to_int(t.get("layer"), 0),
            "tunnel": t.get("tunnel") in ("yes", "building_passage", "culvert") or bool(t.get("tunnel")),
            "bridge": t.get("bridge") in ("yes", "viaduct", "aqueduct") or bool(t.get("bridge")),
            "lanes":  to_int(t.get("lanes"), 0),
            "surface": t.get("surface", ""),
            "highway": t.get("highway", ""),
            "maxspeed": to_int(t.get("maxspeed"), 0),
        }
    print("[OSM] %d ways con tags" % len(tags))

    def augment(fname, idkey, out):
        p = os.path.join(D, fname)
        if not os.path.exists(p): print("[OSM] no existe %s" % fname); return
        arr = json.load(open(p, encoding="utf-8"))
        # railways viene como {"rails":[...]}
        lst = arr["rails"] if isinstance(arr, dict) and "rails" in arr else arr
        hit = 0
        for o in lst:
            oid = o.get(idkey) or o.get("id") or o.get("osm_id")
            if oid in tags:
                o.update(tags[oid]); hit += 1
            else:
                o.setdefault("layer", 0); o.setdefault("tunnel", False)
                o.setdefault("bridge", False); o.setdefault("lanes", 0)
        json.dump(arr, open(os.path.join(D, out), "w", encoding="utf-8"))
        # estadistica
        nt = sum(1 for o in lst if o.get("tunnel")); nb = sum(1 for o in lst if o.get("bridge"))
        print("[OSM] %s -> %s: %d/%d con tags, %d tuneles, %d puentes" % (fname, out, hit, len(lst), nt, nb))

    augment("roads_unity.json",    "id",     "roads_aaa.json")
    augment("footways_unity.json", "osm_id", "footways_aaa.json")
    augment("railways_unity.json", "osm_id", "railways_aaa.json")
    print("[OK] hecho")

if __name__ == "__main__":
    main()
