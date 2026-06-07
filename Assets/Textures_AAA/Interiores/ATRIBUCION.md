# Atribución de assets de interiores — Altsasu Manifa

Este documento cumple los requisitos de licencia de los assets descargados
para los interiores del juego. **Conservar este archivo en el repositorio.**

## Texturas de interior — `Assets/Textures_AAA/Interiores/`

Origen: repositorio **three.js** (https://github.com/mrdoob/three.js),
carpeta `examples/textures/`. Licencia **CC0 / dominio público** (uso libre,
sin atribución obligatoria, incluido uso comercial).

| Carpeta | Archivo origen (three.js) |
|---------|---------------------------|
| `suelo_madera/`    | hardwood2_diffuse / bump / roughness |
| `pared_ladrillo/`  | brick_diffuse / bump / roughness |
| `suelo_azulejo/`   | floors/FloorsCheckerboard_S_Diffuse / Normal |
| `pared_hormigon/`  | disturb.jpg |

## Muebles 3D PBR — `Assets/Resources/Muebles/`

Origen: **Khronos glTF Sample Assets**
(https://github.com/KhronosGroup/glTF-Sample-Assets).
Licencia **CC-BY 4.0** — requiere atribución (incluida aquí).

| Archivo | Modelo original | Autor / Copyright |
|---------|-----------------|-------------------|
| `Silla.glb`   | SheenChair      | © 2022 Wayfair LLC, CC-BY 4.0 |
| `Sofa.glb`    | GlamVelvetSofa  | © 2022 Wayfair LLC, CC-BY 4.0 |
| `Lampara.glb` | Lantern         | © Microsoft, Frank Galligan, CC-BY 4.0 |
| `Botella.glb` | WaterBottle     | © Microsoft, CC-BY 4.0 |

### Texto de atribución para los créditos del juego
```
Muebles 3D: SheenChair y GlamVelvetSofa © Wayfair LLC;
Lantern y WaterBottle © Microsoft — Khronos glTF Sample Assets (CC-BY 4.0).
Texturas de interior: three.js examples (CC0).
```

## HDRIs de interior (pendiente de descarga manual)

Las HDRIs de Poly Haven (CC0) **no se pudieron descargar automáticamente**
porque el host está fuera de la allowlist del entorno de ejecución. Para
obtenerlas, ejecuta en tu PC:

```
python Tools/descargar_assets_interiores.py --solo-hdri
```

Mientras tanto, `GeneradorInterioresAAA` genera cubemaps procedurales como
fallback, así que el interior mapping funciona igualmente.
