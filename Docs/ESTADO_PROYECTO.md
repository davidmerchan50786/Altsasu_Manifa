# Estado del proyecto — Altsasu_Manifa (actualizado 2026-06-21)

Índice único para retomar el trabajo. Resume qué se hizo, qué está verificado y qué
falta, con el orden exacto para continuar dentro de Unity.

---

## 1. Lo grande de esta tanda: migración a UTM real isótropo

El mundo estaba comprimido en X un factor `76400/81548 ≈ 0.93687` (bug del importador
OSM) → se deformaba ~6,3% E-O. **Corregido: ahora `ESCALA_UTM_X = 1` (1 ud = 1 m en X y Z).**

Hecho y verificado **en disco** (no depende de Unity):
- **Datos vectoriales** reproyectados a OSM/Catastro real: edificios a **0,00 m** (iglesia 0,01 m),
  + río, tren, plazas, sendas, zonas verdes y **100 huertas**. Backup: `_backup_pre_utm_real~/`.
- **Autovía A-1/A-10 añadida** (89 tramos; antes no existía en `roads_unity.json`).
- **Terreno V2 regenerado** con `SX=1` (`manifest_v2.json escalaX=1`); cota plaza 531,97 m
  (esperado 531,94). Backup del V2 comprimido: `_backup_terreno_pre_utm~/`.
- **Código**: `GeoDataAlsasua.ESCALA_UTM_X = 1` (`ESCALA_UTM_X_LEGACY` guarda el viejo).
- **Pipeline Python isótropo**: `SX/ESCALA_X = 1` en `GenerarMosaicoTerrenoV2.py`,
  `ValidarMosaicoV2.py`, `DescargarMDT_Mosaico.py`, `DescargarOrtofotoFondo.py`,
  `ReproyectarEdificiosCanonico.py` → no se reintroduce la compresión.
- **Errores de compilación pre-existentes arreglados** (sacaron el proyecto de Modo Seguro):
  `GeneradorFachadasAAA.cs` (`_col` duplicado), `BakeadorNavMeshV3.cs` (NavMeshBuilder
  ambiguo + `showNavigation`), `ConfiguradorFase5.cs` (`LightingSettings` obsoletos).
- `CLAUDE.md` actualizado a la nueva convención.

Detalle: `Assets/AlsasuaData/CORRECCION_UTM_REAL.md`.

## 2. Redes de validación (ambas en verde)
- **Gate de datos (sin Unity)**: `python Tools/ValidarGeorrefDatos.py` → 12/12 verde
  (escala, iglesia, autovía, tren, río, huertas, cotas, V3≈V2). Informe:
  `Docs/validacion_georref_datos.txt`.
- **Gate en editor**: `Tools ▸ Alsasua ▸ Calidad ▸ ✅ Validar georreferenciación`.
- **Tests EditMode**: `Assets/Tests/EditMode/GeoDataAlsasuaTests.cs` (escala isótropa,
  ida-vuelta UTM, AlturaTerreno con ITerrainService).

## 3. Deuda AAA arrancada (staged, no compilado → no rompe el build)
- **Impostores billboard** → `Assets/Scripts/_Impostores~/` (SO + baker + billboard + shader).
- **Clipmap V3** → `Assets/Scripts/_ClipmapV3~/` (malla + follow + sampler CPU validado)
  + `Assets/AlsasuaData/terrain_clipmap_v3/heightmap_unificado.r16` (validado: **V3≈V2 <0,5 m**,
  mediana 8 mm).
- Diseño y plan por fases: `Docs/ADR_001_AAA_impostores_clipmapV3.md`.

## 4. Otros entregables
- **Blender**: 1030 OBJ por edificio en `Assets/AlsasuaData/blender_export~/` (footprint exacto
  extruido + CSV de posiciones). En carpeta `~` para no inflar el import de Unity.
- **Chuleta de prompts** adaptada al proyecto: `Docs/7_Prompts_Altsasu.md`.

---

## 5. PENDIENTE — necesita el editor de Unity abierto

> ⚠ Bloqueo actual: tras un crash por OOM quedaron 2 `Unity.exe` clavados en el driver
> gráfico (no se matan ni elevados). **Hace falta reiniciar el PC** para liberarlos.

Tras reiniciar, **antes** de abrir Unity: cierra navegador y VLC (evita repetir el OOM).
Luego, en orden:

1. `python Tools/ValidarGeorrefDatos.py` (confirma verde sin abrir el editor).
2. Abre `Assets/#Scenes/Alsasua_Main.unity`.
3. **`Tools ▸ Alsasua ▸ ▶▶ APLICAR TODO (UTM real)`** → reconstruye mosaico, ortofoto,
   edificios y calles desde los datos corregidos. (O los pasos sueltos del
   `CORRECCION_UTM_REAL.md`.)
4. `Tools ▸ Alsasua ▸ Calidad ▸ ✅ Validar georreferenciación` (gate en editor).
5. Visualmente: comprobar que edificios/calles caen sobre el terreno y coinciden con la
   ortofoto.

Después, las fases 3 de la deuda AAA (ya con el editor para validar):
- Impostores: activar `_Impostores~` (mover a Runtime/Editor), ShaderGraph HDRP octaédrico,
  batching BRG, hook en `StreamerMundoEstatico`. Ver `_Impostores~/LEEME_impostores.md`.
- Clipmap V3: activar `_ClipmapV3~`, ShaderGraph HDRP de displacement del R16, cablear
  `MuestreadorHeightmapV3` a `ServicioTerreno`. Ver `_ClipmapV3~/LEEME_clipmapV3.md`.

## 6. Revertir (si hiciera falta)
- Vectores: `Assets/AlsasuaData/_backup_pre_utm_real~/`
- Terreno: `Assets/AlsasuaData/_backup_terreno_pre_utm~/`
- Código: `ESCALA_UTM_X = ESCALA_UTM_X_LEGACY` y `SX = 76400/81548` en los `.py`.
