# Plan de render AAA "estilo GTA VI" — Altsasu Manifa

> **Objetivo medible:** pasar de **77.693 draw calls / 0,6 FPS** (medido en `[DIAG]`)
> a **< 2.500 draw calls / 60 FPS estables**, con look hiperrealista (luz horneada +
> PBR + post-procesado). El `[DIAG]` de `DiagnosticoRendimiento.cs` es el marcador
> oficial: ninguna fase se da por buena sin mejorar su número.

## Principio rector (lo que hace RAGE/GTA y todos los AAA)

**Separar AUTORÍA de JUEGO.**
- **Autoría** = offline, en el Editor, lento, **una sola vez**: generar la ciudad desde
  IGN/LIDAR/OSM, fusionar mallas, hornear luz y oclusión, construir LODs e impostores.
- **Juego** = runtime, barato: solo **streamea y dibuja** datos ya masticados.

Hoy Altsasu hace TODA la autoría en cada Play (genera 77k GameObjects en vivo). Ese es
el error de raíz. El generador procedural es excelente — pero es la fase de autoría,
está ejecutándose en el momento equivocado.

---

## Fase 0 — Instrumentación y baseline (medir SIEMPRE)
**Meta:** que el `[DIAG]` mida lo que importa de GPU y sea el gate de todo.
- Añadir a `DiagnosticoRendimiento.cs`: **draw calls / SetPass / batches / triángulos /
  vértices** (vía `UnityEngine.Profiling` / `FrameTimingManager` / contadores del
  Frame Debugger), no solo "renderers activos".
- Fijar la **baseline** actual (77.693 / 0,6 FPS) y registrar cada fase contra ella.
- **Salida:** tabla de métricas por fase en este doc.

## Fase 1 — EL BAKE (el 80% del resultado) ⭐ PRIORIDAD MÁXIMA
**Meta:** mover la generación procedural de **runtime → Editor** y colapsar draw calls.
Mapea a: `GeneradorMundoOSM`, `SistemaEdificiosAAA`, `FusionadorEdificiosUltra`,
`GeneradorGeometriaPrecisa`, `OptimizadorMallaOBJ`, `SistemaZonas`.
- **1a — Generación en Editor:** menú `Tools/Alsasua/Mundo/🏗️ Hornear Ciudad`. Ejecuta
  los generadores en modo editor y **serializa el resultado** como prefabs por celda
  (200 m) en `Assets/CiudadHorneada/`. En Play NO se genera nada: se carga.
- **1b — Mesh merging por celda+material:** `Mesh.CombineMeshes` (IndexFormat.UInt32)
  agrupando por material → **1 draw call por material por celda**. Marcar `static`.
  *Esperado: 77.000 → unos cientos/pocos miles.*
- **1c — GPU instancing:** activar `enableInstancing` en materiales repetidos (ventanas,
  farolas, props, vallas) → miles de copias = 1 draw call.
- **1d — Unity 6 GPU Resident Drawer + GPU Occlusion Culling:** activar en el asset HDRP
  (auto-batching + culling en GPU, casi gratis si las mallas son instancing-compatibles).
- **Salida (gate):** `[DIAG]` < 5.000 draw calls; arranque sin estampida.

## Fase 2 — LODs e impostores AUTORizados
**Meta:** que lo lejano cueste casi nada. Sustituye el "impostor-lite" actual
(`StreamerMundoEstatico`, que solo apaga sombras) por impostores reales.
- **LOD0–3** por edificio/celda (decimación offline: Mesh LOD de Unity 6 o
  UnityMeshSimplifier) + `LODGroup`.
- **Impostores billboard** (atlas octaédrico) para > ~400 m: un edificio = 2 triángulos
  con una foto. (Amplify Impostors / octahedral impostors / shader propio.)
- **HLOD jerárquico:** fusionar celdas lejanas enteras en un proxy único de baja
  resolución (equivalente al "aggregate mesh" de RAGE / HLOD de UE5 World Partition).
- **Salida:** draw calls casi planos respecto a la distancia de visión.

## Fase 3 — Streaming AAA de celdas pre-construidas
**Meta:** cargar de disco solo lo cercano, async y predictivo. Reescribe `SistemaZonas` +
`StreamerMundoEstatico` para streamear **prefabs horneados**, no para esconder objetos.
- **Addressables**: cada celda horneada = un addressable; carga/descarga async en hilo
  de fondo (ya hay diseño en `[[streaming-addressables]]`, define `ALSASUA_ADDRESSABLES`).
- **Predicción por velocidad/dirección** del jugador (pre-cargar hacia donde va).
- **Anillos:** Full (0–300 m) · LOD (300–1000 m) · HLOD+impostor (1–5 km) · Cesium (>5 km).
- **Presupuesto por frame + pooling**, cero `Instantiate` en caliente. El `GobernadorRender`
  (ya hecho) controla el radio dinámico; ahora actúa sobre datos que sí pesan.
- **Salida:** memoria acotada; sin picos de CPU al moverse.

## Fase 4 — GPU-driven rendering (lo que hace GTA VI)
**Meta:** draw calls **independientes del número de objetos**.
- **Mosaico V3** (ya en tu plan): terreno como **clipmap GPU** → 1–2 draw calls para los
  14×14 km, sustituye los 48 `Terrain` (que ya petan colliders + splatmaps).
  Mapea a `CargadorMosaicoTerreno` → reemplazar por clipmap.
- **BatchRendererGroup + `DrawMeshInstancedIndirect`** para vegetación, props y multitud
  masiva (ya lo usa la multitud BRG — extenderlo a árboles y props).
- **GPU occlusion culling (Hi-Z):** subir `SistemaOcclusion` a GPU (o usar el de Unity 6).
- **Salida:** 5.000 personas o 1 cuestan casi lo mismo.

## Fase 5 — Iluminación horneada + GI (de aquí sale el "hiperrealismo")
**Meta:** la luz, no la geometría, es el 70% del look AAA.
- **Lightmaps horneados** para todo lo estático de cada celda (en el bake de Fase 1).
- **APV (Adaptive Probe Volumes)** de HDRP para iluminación de dinámicos (ya referenciado
  en `SistemaAPVScenarios`). Escenarios día/noche.
- **Reflection probes** horneados + SSR/SSGI/SSAO + volumetrics (ya tienes
  `SistemaVolumenHDRP`). Exposición Automatic 11–15 (ya configurada).
- **Salida:** sombras de contacto, rebotes, oclusión ambiental — sin coste de runtime.

## Fase 6 — Materiales, texturas y detalle (PBR a escala)
**Meta:** calidad sin reventar VRAM ni draw calls.
- **Atlas PBR** + pocos materiales maestros compartidos (clave para instancing).
- **Virtual Texturing** de HDRP (streaming de texturas) → texel alto sin llenar los 8 GB.
- **Decals** (HDRP Decal Projector) y **detail maps** para variedad sin más geometría
  (manchas, carteles, desgaste — el truco de RDR2/Cyberpunk).
- **Salida:** look rico con presupuesto de VRAM/draw calls controlado.

## Fase 7 — Mundo vivo a escala ("y más")
- **Tráfico y peatones GPU-driven** (BRG) + IA con Sim-LOD (ya tienes el
  `GlobalSimulationOrchestrator`: actores cerca, proxies lejos, ghosts al fondo).
- **Clima y agua** (HDRP Water Surface), **interiores streameados**, audio espacial.
- **Gestor de presupuesto de memoria** + frame pacing + "async everything".
- **Salida:** densidad de ciudad real sin caídas.

---

## Orden de impacto (haz en este orden)
1. **Fase 1 (bake + merge + instancing)** — sola da el 80%. Es lo único que mueve el
   `[DIAG]` de 77k a cientos.
2. **Fase 4 (terreno clipmap Mosaico V3)** — mata el coste de los 48 Terrain.
3. **Fase 2 (LOD + impostores + HLOD)** — hace barato lo lejano.
4. **Fase 3 (streaming Addressables)** — acota memoria y picos.
5. **Fase 5 (luz horneada)** — aquí llega el hiperrealismo.
6. **Fases 6–7** — pulido y vida.

## Realidad / riesgos
- Esto es trabajo de **semanas/meses**, no de una tarde. GTA VI lo hace un estudio de
  cientos de personas con motor propio (RAGE). Unity HDRP **no tiene Nanite**; el
  equivalente práctico es **HLOD agresivo + impostores + GPU Resident Drawer**.
- **Empieza por la Fase 1.** Sin el bake, ninguna otra optimización importa: seguirás
  pagando 77k objetos en cada Play.

## Tabla de métricas (rellenar al cerrar cada fase)
| Fase | Draw calls | FPS | Notas |
|------|-----------|-----|-------|
| Baseline | 77.693 | 0,6 | medido 2026-06-15 |
| F1 |  |  |  |
| F2 |  |  |  |
| F4 |  |  |  |
