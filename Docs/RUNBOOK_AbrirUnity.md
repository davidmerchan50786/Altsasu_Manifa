# RUNBOOK — qué hacer al abrir Unity

Lista ordenada para activar y validar todo el trabajo hecho en disco (2026-06).
Sigue los pasos en orden; cada bloque tiene su gate. Detalle fino en los `LEEME_*`
de cada carpeta `~`. Nada de esto se ha podido validar en el editor todavía.

> Las carpetas que terminan en `~` (`_ClipmapV3~`, `_Impostores~`, `_ParanoiaGC~`,
> `_ControlesGC~`, `_Testigos~`, `_Coartada~`, `_Tuning~`, `_NarrativaJSON~`) NO las
> compila Unity. "Activar" = mover sus `.cs` a la carpeta de capa que se indica.

---

## 0. Arranque limpio
- Si Unity quedó colgado en sesiones previas: **reinicia el PC** (procesos enganchados
  en el driver GPU). Borra `Temp/` y `UnityLockfile` del proyecto si siguen.
- Abre `Altsasu_Manifa`. Deja que compile en frío. No debería entrar en Modo Seguro
  (los errores previos ya se arreglaron y el código nuevo está staged).

## 1. Georreferencia UTM real (datos → escena)  ★ primero
1. Terminal: `python Tools/ValidarGeorrefDatos.py` → debe dar **12/12 verde**.
2. Editor: `Tools/Alsasua/▶▶ APLICAR TODO (UTM real)` (reconstruye terreno/vectores
   en escena con escala isótropa SX=1).
3. Gate: `Tools/Alsasua/Calidad/✅ Validar georreferenciación` → verde.
   Tests EditMode `GeoDataAlsasuaTests` → verde.
   (Detalle: `Assets/AlsasuaData/CORRECCION_UTM_REAL.md`.)

## 2. Mosaico V3 — clipmap GPU (terreno a 1-2 draw calls)  ★ mayor salto AAA
Sigue `Assets/Scripts/_ClipmapV3~/LEEME_clipmapV3.md`. Resumen:
1. Mueve los `.cs` de `_ClipmapV3~/` a `Assets/Scripts/Runtime/ClipmapV3/`.
2. Crea el Lit Shader Graph del clipmap con el Custom Function `ClipmapDisplacement.hlsl`
   (receta paso a paso en el LEEME: propiedades `_Height _ClipmapOrigen _Half _OX _OZ
   _Base _ZMin _Res`, salidas a Vertex Position/Normal). Crea el material.
3. GameObject con `ClipmapTerrenoV3` (+ material) `+ CargadorTexturaHeightmapV3`
   `+ MuestreadorAlturaClipmapV3` `+ ColliderParcheClipmapV3`. Añade las 2 líneas de
   wiring del LEEME (Configurar + SetVector `_ClipmapOrigen`).
4. Play. Gates: cota de Herriko Plaza ≈ **531.94 m** (el adaptador auto-valida y solo
   se registra si pasa), `✅ Validar georreferenciación` verde, edificios/árboles/NavMesh
   siguen en su sitio (leen del adaptador sin cambios).
5. Cuando todo cuadre, retira el Mosaico V2 de la escena (deja los datos como backup).

## 3. Impostores (edificios/props lejanos → billboards)
Sigue `Assets/Scripts/_Impostores~/LEEME_impostores.md`. Resumen:
1. Mueve `ImpostorAtlasSO.cs`, `ImpostorBillboard.cs`, `GestorImpostores.cs` a
   `Assets/Scripts/Runtime/Impostores/`; `BakeadorImpostores.cs` a `Assets/Scripts/Editor/`.
2. Recrea el shader como **ShaderGraph HDRP/Unlit** `Alsasua/ImpostorUnlit` (receta en
   el LEEME) — si no, sale magenta en HDRP.
3. Selecciona 5-10 edificios → `Tools ▸ Alsasua ▸ Impostores ▸ 🔆 Bake atlas`. Revisa el
   alfa del atlas (nota HDRP).
4. Pon `GestorImpostores` en escena con el atlas; engancha el streamer (snippet del LEEME):
   en banda media `Adquirir`, al volver a Activo `Liberar`.

## 4. Gameplay "calor y alivio"
Bucle e integración: `Docs/Narrativa/INTEGRACION_Sistemas.md`. Orden de activación:
1. `_Tuning~/SintoniaAltsasu.cs` → `Assets/Scripts/Core/`. Crea el asset
   `Assets ▸ Create ▸ Alsasua ▸ Sintonía (calor)`.
2. `_ParanoiaGC~/` → Runtime. Pon `SistemaParanoiaGuardiaCivil` en escena; marca como
   `ConvertibleGuardiaCivil` los NPC/coches que puedan volverse tricornios. Asigna `sintonia`.
3. `_ControlesGC~/` → Runtime. Coloca `ControlGuardiaCivil` en los pasos (calle San Juan,
   puentes del Arakil, salidas N-1) y un `SistemaControlesGC`. Asigna `sintonia`.
4. `_Testigos~/` → Runtime. `TestigoNPC` en civiles + `SistemaTestigos` en escena.
   Añade `SistemaTestigos.ReportarDelito(pos, gravedad)` junto a cada `SumarParanoia`
   del código de delito. Asigna `sintonia`.
5. `_Coartada~/` → Runtime. `SistemaCoartada` en escena + `ZonaCoartada` (trigger) en
   refugios (bares, portales, gaztetxe). Asigna `sintonia`.
6. Todos los `sintonia` apuntan al mismo asset → balanceas el sandbox desde un sitio.

## 5. Misiones data-driven (opcional)
`_NarrativaJSON~/`: mueve `CargadorMisionesJSON.cs` a Runtime y carga
`misiones_altsasu.json` desde `SistemaMisiones`. Guion y biblia en `Docs/Narrativa/`.

## 6. Gate final
- `✅ Validar georreferenciación` verde.
- Cota de plaza ≈ 531.94 m con el clipmap activo.
- Sin *pop* visible al cruzar bandas de streaming (histéresis + dither).
- Frame-time estable con los dos directores (CPU `GlobalSimulationOrchestrator`,
  GPU `GobernadorRender`) y el `StreamerMundoEstatico`.

---
### Estado de validación
| Bloque | En disco | En editor |
|--------|----------|-----------|
| UTM real (datos) | ✅ 12/12 + cota 531.97 | ⏳ aplicar + gate |
| Clipmap V3 (HLSL/cargador/adaptador/collider) | ✅ decode .py=CPU=GPU diff 0.000 | ⏳ ShaderGraph + Play |
| Impostores (SO/baker/billboard/gestor) | ✅ | ⏳ ShaderGraph + bake |
| Calor y alivio (5 sistemas + panel) | ✅ integrado, 1 paranoia | ⏳ colocar + Play |
