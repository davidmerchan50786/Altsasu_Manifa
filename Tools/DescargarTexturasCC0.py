# -*- coding: utf-8 -*-
# DescargarTexturasCC0.py — descarga sets PBR CC0 de PolyHaven (2K) para la red viaria
# AAA. Guarda en <UE>/PBR_download/<slot>/ {albedo,normal,rough,ao,disp}.png|jpg
import json, urllib.request, os, sys

OUT = r"E:\Epic Games\UE_5.7\altsasu_gtavii\UnrealProject\PBR_download"
os.makedirs(OUT, exist_ok=True)
API = "https://api.polyhaven.com"
UA = {"User-Agent": "AlsasuaSim/1.0"}

# slot logico -> lista de asset ids candidatos (se usa el primero que exista)
SLOTS = {
    "asfalto":    ["asphalt_02", "asphalt_04", "rough_asphalt_02", "asphalt_road"],
    "adoquin":    ["cobblestone_floor_08", "cobblestone_large_01", "pavement_06", "medieval_cobblestone_02"],
    "hormigon":   ["concrete_floor_02", "concrete_layer_01", "pavement_02", "concrete_wall_008"],
    "grava":      ["gravel_02", "gravel_road", "gravelly_sand", "gravel_floor"],
    "tierra":     ["dirt_track", "brown_mud_02", "dirt_floor", "ground_0024"],
    "lecho_rio":  ["river_small_rocks", "river_rocks", "pebbles", "rocky_terrain_02"],
    "cesped":     ["grass_medium_02", "coast_sand_rocks_02", "leafy_grass", "grass_path_2"],
}
MAPS = {  # map polyhaven -> nombre local
    "Diffuse": "albedo", "nor_gl": "normal", "Rough": "rough", "AO": "ao", "Displacement": "disp",
}
RES = "2k"

def get(url):
    return json.load(urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=60))

def download(url, dst):
    with urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=180) as r, open(dst, "wb") as f:
        f.write(r.read())

def main():
    resumen = {}
    for slot, cands in SLOTS.items():
        sdir = os.path.join(OUT, slot)
        os.makedirs(sdir, exist_ok=True)
        done = False
        for aid in cands:
            try:
                files = get("%s/files/%s" % (API, aid))
            except Exception:
                continue
            got = []
            for phmap, local in MAPS.items():
                node = files.get(phmap)
                if not node:
                    continue
                # elegir resolucion; los mapas cuelgan de [res][ext]
                resnode = node.get(RES) or node.get("1k") or next(iter(node.values()), None)
                if not resnode:
                    continue
                # resnode: {"png": {...,"url":...}} o directamente {"url":...}
                ext, url = None, None
                if isinstance(resnode, dict) and "url" in resnode:
                    url = resnode["url"]; ext = url.split(".")[-1]
                else:
                    for e in ("jpg", "png", "exr"):
                        if e in resnode and isinstance(resnode[e], dict) and "url" in resnode[e]:
                            url = resnode[e]["url"]; ext = e; break
                if not url:
                    continue
                dst = os.path.join(sdir, "%s.%s" % (local, ext))
                try:
                    download(url, dst); got.append(local)
                except Exception as e:
                    print("   ! %s %s: %s" % (aid, local, e))
            if "albedo" in got:
                print("[PBR] %-10s <- %-24s maps: %s" % (slot, aid, ",".join(got)))
                resumen[slot] = {"asset": aid, "maps": got}
                done = True
                break
        if not done:
            print("[PBR] %-10s SIN DESCARGA" % slot)
    json.dump(resumen, open(os.path.join(OUT, "_resumen.json"), "w"), indent=1)
    print("[OK] %d/%d slots" % (len(resumen), len(SLOTS)))

if __name__ == "__main__":
    main()
