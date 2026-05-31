#!/usr/bin/env python3
"""Tarea 1: Renombra las fotos de Street View agrupándolas en 8 zonas cronológicas."""
import os
from pathlib import Path

BASE_DIR   = Path(__file__).parent.parent
INPUT_DIR  = BASE_DIR / "Assets/AlsasuaData/FacadeTextures/StreetView"
RENAMED_DIR = INPUT_DIR / "renamed"
RENAMED_DIR.mkdir(exist_ok=True)

ZONAS = [
    "casco_viejo",
    "plaza_fueros",
    "ferial",
    "gaztetxe",
    "iglesia",
    "ayto",
    "plaza_zubeztia",
    "garcia_jimenez",
]

fotos = sorted(
    f for f in INPUT_DIR.glob("*.png")
    if "minimap" not in f.name.lower() and "readme" not in f.name.lower()
)
fotos += sorted(
    f for f in INPUT_DIR.glob("*.jpg")
    if "minimap" not in f.name.lower()
)
fotos.sort()

n = len(fotos)
chunk = max(1, n // 8)

print(f"Total fotos: {n}  →  ~{chunk} por zona")

counters = {z: 0 for z in ZONAS}
for i, foto in enumerate(fotos):
    zona_idx = min(i // chunk, 7)
    zona = ZONAS[zona_idx]
    counters[zona] += 1
    new_name = f"{zona}_{counters[zona]:03d}.png"
    dest = RENAMED_DIR / new_name
    if not dest.exists():
        os.symlink(foto.resolve(), dest)
    print(f"  {foto.name}  →  {new_name}")

print("\nResumen por zona:")
for z, c in counters.items():
    print(f"  {z:20s}: {c}")
