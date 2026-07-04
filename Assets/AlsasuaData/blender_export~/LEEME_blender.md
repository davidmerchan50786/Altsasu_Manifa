# Flujo Blender — mejorar edificios y reimportar a Unity

Todo en **metros reales** (UTM 30N, 1 unidad = 1 m), eje Y = altura.

## Archivos
- `edificios/<osm_id>.obj` — un OBJ por edificio (1030), **centrado en su propio
  centroide**, footprint exacto de OSM/Catastro extruido a su altura. Ideal para
  editar uno y reimportarlo como reemplazo.
- `edificios_combinado.obj` — toda la ciudad en **coordenadas mundo Unity**
  (para abrirla entera en Blender de una vez).
- `edificios_posiciones.csv` — `osm_id, unity_world_x, unity_world_z, altura_m, nombre`.
  Es donde Unity debe colocar cada edificio mejorado.

## Mejorar un edificio
1. Abre `edificios/<id>.obj` en Blender (File ▸ Import ▸ Wavefront OBJ).
   Está centrado en el origen → cómodo de modelar (tejados, aleros, detalle).
2. Modela. **No muevas el objeto del origen** (así conserva su anclaje).
3. Export ▸ Wavefront/FBX manteniendo escala 1 y eje **Y-up, -Z forward**
   (los presets "Unity" de Blender). Guárdalo con el mismo `<id>`.

## Reimportar a Unity
- Arrastra el FBX/OBJ a `Assets/`. En Unity, instancia el prefab en
  `new Vector3(unity_world_x, alturaTerreno, unity_world_z)` (de la CSV) y aplica
  rotación 0. `alturaTerreno = GeoDataAlsasua.AlturaTerreno(x, z)`.
- Como cada malla está centrada y a escala real, encaja exactamente sobre su
  parcela y sobre el terreno corregido.

## Toda la ciudad de golpe
Importa `edificios_combinado.obj`: aparece ya en coordenadas mundo Unity, así que
en Blender lo ves sobre el mismo sistema que la escena.
