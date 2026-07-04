# Guía de activación detallada — orden exacto

Orden correcto, paso a paso, para activar y validar TODO lo preparado en disco.
Hazlo en este orden: cada fase depende de la anterior. No saltes gates en rojo.

Notación: **[Menú]** = barra de menú de Unity · **(asmdef)** = capa/ensamblado ·
⏱️ gate = comprobación obligatoria antes de seguir.

Regla base: las carpetas que terminan en `~` NO las compila Unity. "Activar" un
sistema = mover sus `.cs` a la carpeta de capa indicada (sin el `~`). Si algo falla
al compilar, queda aislado a esos ficheros (no entra en Modo Seguro el resto).

> ⚡ ATAJO AUTOMÁTICO — **[Tools ▸ Alsasua ▸ Activar AAA ▸ ▶ TODO]** (`ActivadorAAA.cs`)
> hace por ti las partes mecánicas de las fases 2-4: mueve los scripts staged, crea
> `SintoniaAltsasu` + `ParanoiaGCConfig` + materiales y monta los GameObjects wireados
> (`AAA_Gameplay`, `AAA_ClipmapV3`, `AAA_Impostores`). Tras mover recompila y continúa
> solo. Queda MANUAL: los 2 Shader Graphs, el bake del atlas, las líneas
> `ReportarDelito` y el Play/validación. La FASE 1 (UTM real) hazla antes a mano.
> Si tras el atajo algo no compiló, revisa la referencia Newtonsoft del asmdef de Systems.

---

## FASE 0 — Arranque limpio (prerequisito)

1. Si Unity quedó colgado antes: **reinicia el PC** (los procesos se enganchan al
   driver GPU y `taskkill` no basta).
2. Con Unity cerrado, en la carpeta del proyecto borra:
   - `Temp/` (siempre regenerable)
   - `UnityLockfile` (si existe y está suelto)
   - *Opcional si hubo crash raro*: `Library/ScriptAssemblies/` (fuerza recompilar).
   No borres `Library/` entero salvo necesidad (reimporta horas).
3. Abre el proyecto desde Unity Hub (versión del proyecto, no otra).

⏱️ **Gate 0**: el proyecto abre sin diálogo de **Safe Mode** y la consola no tiene
errores rojos de compilación. Si los hay, mándamelos antes de seguir.

---

## FASE 1 — Georreferencia UTM real (deja el mundo en su sitio)  ★ PRIMERO

Por qué primero: reconstruye terreno y vectores (edificios/calles/río/vías) con la
escala isótropa SX=1. Todo lo demás (clipmap, gameplay) asume estas coordenadas.

1. Terminal en la raíz del proyecto:
   ```
   python Tools/ValidarGeorrefDatos.py
   ```
   ⏱️ **Gate 1a**: salida **12/12 verde**. Si algo sale rojo, los datos en disco están
   mal: páralo aquí.
2. En Unity: **[Tools ▸ Alsasua ▸ ▶▶ APLICAR TODO (UTM real)]**.
   Reconstruye el terreno (mosaico V2 regenerado, SX=1) y reproyecta los vectores.
   Espera a que termine sin errores en consola.
3. **[Tools ▸ Alsasua ▸ Calidad ▸ ✅ Validar georreferenciación]**.
   ⏱️ **Gate 1b**: verde. Cota de Herriko Plaza ≈ **531.94 m** (acepta 531.94–531.98).
4. **[Window ▸ General ▸ Test Runner ▸ EditMode ▸ Run All]** → `GeoDataAlsasua Tests`.
   ⏱️ **Gate 1c**: todos en verde (escala isótropa, ida-vuelta UTM).

Detalle de fondo: `Assets/AlsasuaData/CORRECCION_UTM_REAL.md`.

> En este punto ya tienes el mundo correcto sobre Mosaico V2 (48 Terrain). Puedes
> jugar así. Las fases 2-4 son las mejoras AAA encima.

---

## FASE 2 — Mosaico V3: clipmap GPU (terreno a 1-2 draw calls)  ★ mayor salto AAA

Sustituye los 48 Terrain por una malla clipmap desplazada en GPU. Detalle:
`Assets/Scripts/_ClipmapV3~/LEEME_clipmapV3.md`.

### 2.1 Mover scripts a una capa que compile
Crea `Assets/Scripts/Systems/ClipmapV3/` y mueve ahí estos 6 ficheros de
`_ClipmapV3~/` (Systems ya referencia Newtonsoft.Json y Core, que es lo que usan):
`ConstructorMallaClipmap.cs`, `ClipmapTerrenoV3.cs`, `MuestreadorHeightmapV3.cs`,
`CargadorTexturaHeightmapV3.cs`, `MuestreadorAlturaClipmapV3.cs`,
`ColliderParcheClipmapV3.cs`. (El `.hlsl` NO se mueve aún; lo usa el shader.)

⏱️ **Gate 2.1**: compila sin errores. Si se queja de `Newtonsoft.Json`, añade la
referencia al asmdef de Systems (Inspector del `.asmdef` ▸ Assembly Definition
References / o "Override References" ▸ añade `Newtonsoft.Json.dll`).

### 2.2 Copiar el HLSL a un sitio accesible por el shader
Copia `ClipmapDisplacement.hlsl` a `Assets/Scripts/Systems/ClipmapV3/` (junto a los
`.cs`). El Custom Function lo referenciará por ruta relativa al `.shadergraph`.

### 2.3 Crear el Shader Graph (HDRP/Lit)
1. **[Assets ▸ Create ▸ Shader Graph ▸ HDRP ▸ Lit Shader Graph]**, nómbralo
   `ClipmapTerreno`.
2. Ábrelo. En **Graph Inspector ▸ Blackboard** crea estas propiedades (botón +).
   El **Reference** debe ser EXACTO (con guion bajo):
   - `_Height` — Texture2D. (Marca Mode = **Default**, no sRGB; en el importador del
     R16 se fuerza Linear desde el cargador.)
   - `_ClipmapOrigen` — Vector2.
   - `_Half` — Float.   · `_OX` — Float.   · `_OZ` — Float.
   - `_Base` — Float.   · `_ZMin` — Float. · `_Res` — Float.
3. Añade un nodo **Custom Function** (clic derecho ▸ Create Node ▸ Utility ▸ Custom
   Function). En su Graph Inspector:
   - **Type** = File.
   - **Source** = `ClipmapDisplacement.hlsl` (selecciónalo).
   - **Name** = `ClipmapDisplace`  (sin el sufijo `_float`; Unity lo añade).
   - **Inputs** (en ESTE orden y tipo):
     `PosOS` Vector3 · `OrigenXZ` Vector2 · `Height` Texture2D · `SS` SamplerState ·
     `Half` Float · `OX` Float · `OZ` Float · `Base` Float · `ZMin` Float · `Res` Float.
   - **Outputs**: `OutPosOS` Vector3 · `OutNormalOS` Vector3.
4. Cablea las entradas:
   - **Position** node (Space = **Object**) → `PosOS`.
   - `_ClipmapOrigen` (arrastra del Blackboard) → `OrigenXZ`.
   - `_Height` → `Height`.  Añade un **Sampler State** node (Filter = Linear, Wrap =
     Clamp) → `SS`.
   - `_Half _OX _OZ _Base _ZMin _Res` → sus floats homónimos.
5. Cablea las salidas a los bloques de **vértice** del master:
   - `OutPosOS` → **Vertex ▸ Position**.
   - `OutNormalOS` → **Vertex ▸ Normal**.
6. **Graph Settings**: deja Surface = Opaque. (El terreno es sólido.) Guarda (**Save
   Asset**).

⏱️ **Gate 2.3**: el shader compila sin errores en el panel del Graph (abajo). Si sale
error de "Sample" en vertex, revisa que el Sampler State esté conectado a `SS`.

### 2.4 Material y objeto de terreno
1. **[Assets ▸ Create ▸ Material]** `ClipmapTerreno_Mat`; Shader = `Shader Graphs/
   ClipmapTerreno`.
2. En la escena crea un GameObject vacío `ClipmapV3` en (0,0,0). Añade componentes:
   - `ClipmapTerrenoV3` → asigna `material` = `ClipmapTerreno_Mat`; `jugador` = tu
     transform de jugador (o déjalo y usará Camera.main).
   - `CargadorTexturaHeightmapV3`.
   - `MuestreadorAlturaClipmapV3` (activarEnStart = ON).
   - `ColliderParcheClipmapV3` → `jugador` = jugador.
3. Añade las 2 líneas de wiring en `ClipmapTerrenoV3` (las indica el LEEME):
   - en `OnEnable`, tras asignar el material:
     `GetComponent<CargadorTexturaHeightmapV3>()?.Configurar(material);`
   - en `Recolocar`, descomenta:
     `material.SetVector("_ClipmapOrigen", new Vector4(x, 0, z, 0));`

### 2.5 Play y validación
1. Entra en **Play**.
2. ⏱️ **Gate 2.5a**: en consola, `MuestreadorAlturaClipmapV3` loguea *"Clipmap V3 listo …
   cota plaza ~531.9x m (✓)"*. Si dice "Auto-validación FALLÓ", NO se registró (el
   juego sigue con V2): revisa que el `.r16` y `meta.json` existan en
   `Assets/AlsasuaData/terrain_clipmap_v3/`.
3. ⏱️ **Gate 2.5b**: visualmente el terreno tiene relieve (no plano), el jugador pisa
   suelo (collider-parche), y edificios/árboles caen en su sitio (leen del adaptador).
4. ⏱️ **Gate 2.5c**: re-corre **[✅ Validar georreferenciación]** en Play → verde.
5. Cuando todo cuadre, **desactiva el Mosaico V2** en la escena (los 48 Terrain) para
   ver el ahorro de draw calls (**[Window ▸ Analysis ▸ Frame Debugger]**). Deja los
   datos de V2 como backup; no los borres aún.

Si algo va mal aquí, vuelve a Mosaico V2 (reactiva los Terrain) — sigues jugable.

---

## FASE 3 — Impostores (edificios/props lejanos → billboards)

Detalle: `Assets/Scripts/_Impostores~/LEEME_impostores.md`.

### 3.1 Mover scripts
- `ImpostorAtlasSO.cs`, `ImpostorBillboard.cs`, `GestorImpostores.cs` →
  `Assets/Scripts/Runtime/Impostores/` (Runtime).
- `BakeadorImpostores.cs` → `Assets/Scripts/Editor/` (Editor).
⏱️ **Gate 3.1**: compila limpio.

### 3.2 Shader Graph HDRP/Unlit (quita el magenta)
1. **[Assets ▸ Create ▸ Shader Graph ▸ HDRP ▸ Unlit Shader Graph]**, nómbralo de modo
   que su ruta de shader sea `Alsasua/ImpostorUnlit` (renómbralo en Graph Settings ▸
   **Precision/Target**… realmente: en el asset, el nombre del shader se fija en
   Graph Inspector ▸ "Shader" no editable; usa **clic en el asset ▸ rename** y dentro,
   en el **Graph Settings**, no hay campo de nombre → para forzar `Alsasua/ImpostorUnlit`
   deja el asset con ese nombre de archivo `ImpostorUnlit` en una carpeta `Alsasua/`, o
   ajusta el `Shader.Find` del billboard al nombre real que muestre el material).
2. Blackboard (Reference exacto):
   - `_Atlas` — Texture2D (Mode sRGB, es albedo).
   - `_UvCell` — Vector4.
   - `_Cutoff` — Float, default 0.5.
3. **Graph Settings**: Surface = Opaque; **Alpha Clipping = ON**; Render Face = **Both**.
4. Grafo:
   - **UV** node (UV0) → **Multiply** por `_UvCell.zw` (usa un **Split** de `_UvCell`:
     B,A = z,w) → **Add** `_UvCell.xy` (R,G del Split) → resultado `uv`.
   - **Sample Texture 2D**: Texture = `_Atlas`, UV = `uv`.
   - RGBA del sample: **RGBA→Base Color** (no hay base color en Unlit: usa el bloque
     **Fragment ▸ Base Color**), **A → Fragment ▸ Alpha**.
   - `_Cutoff` → **Fragment ▸ Alpha Clip Threshold**.
5. Guarda. Crea material `ImpostorUnlit_Mat` con ese shader (lo usará el billboard;
   si el nombre del shader no es `Alsasua/ImpostorUnlit`, ajusta el `Shader.Find` en
   `ImpostorBillboard.Inicializar`).

### 3.3 Bakear el atlas
1. En la jerarquía, selecciona 5-10 edificios con Renderer (piloto).
2. **[Tools ▸ Alsasua ▸ Impostores ▸ 🔆 Bake atlas (selección)]**.
3. ⏱️ **Gate 3.3**: se crea `Assets/AlsasuaData/impostores_v1/atlas_albedo.png` y
   `ImpostorAtlas.asset`. Abre el PNG: el **alfa debe ser transparente** alrededor del
   edificio. Si sale opaco (caveat HDRP), hornea sobre fondo croma y recórtalo, o usa
   una cámara HDRP dedicada (ver LEEME).

### 3.4 Gestor + hook de streaming
1. GameObject `Impostores` con `GestorImpostores`; asigna `atlas` = `ImpostorAtlas.asset`.
2. En `StreamerMundoEstatico`, en la banda media (`[RadioActivacion, RadioImpostor]`),
   sustituye el "impostor-lite" por (snippet del LEEME):
   ```
   meshRenderer.enabled = false;
   _impostor = GestorImpostores.Instance.Adquirir(idOSM, transform);
   // al volver a 'Activo':
   if (_impostor) { GestorImpostores.Instance.Liberar(_impostor); _impostor = null; }
   meshRenderer.enabled = true;
   ```
⏱️ **Gate 3.4**: al alejarte, los edificios lejanos pasan a billboard sin *pop* brusco;
Frame Debugger muestra menos draw calls de geometría lejana.

---

## FASE 4 — Gameplay "calor y alivio"

Bucle completo e integración: `Docs/Narrativa/INTEGRACION_Sistemas.md`. Orden interno
IMPORTA (el panel y la paranoia canónica van antes que quienes los leen).

### 4.0 Prerrequisito: paranoia canónica
Asegúrate de que `SistemaApoyoPopular` está en la escena (es la única fuente de
paranoia/apoyo; `SistemaParanoia` es ya solo fachada). Si no, añádelo.

### 4.1 Panel de tuning (primero)
1. Mueve `_Tuning~/SintoniaAltsasu.cs` → `Assets/Scripts/Core/`.
2. **[Assets ▸ Create ▸ Alsasua ▸ Sintonía (calor)]** → crea `SintoniaAltsasu.asset`.
⏱️ **Gate 4.1**: compila; el asset aparece y se puede editar en el Inspector.

### 4.2 Capas de física (necesarias para LOS)
En **[Edit ▸ Project Settings ▸ Tags and Layers]** ten claras dos capas:
- una para **autoridad** (policía/Guardia Civil) — la usan Coartada y controles.
- la de **obstáculos/muros** — para los Linecast de visión.
Asigna esas capas a los prefabs correspondientes.

### 4.3 Paranoia → Guardia Civil
1. Mueve `_ParanoiaGC~/` → `Assets/Scripts/Runtime/ParanoiaGC/`.
2. Crea un `ParanoiaGCConfig` (**[Create ▸ Alsasua ▸ …]** si tiene CreateAssetMenu, o
   asigna sus campos en el componente) con `maxNpc`, `maxCoches`, umbrales 70/90.
3. GameObject `ParanoiaGC` con `SistemaParanoiaGuardiaCivil`; asigna `config` y
   `sintonia` = tu asset.
4. Marca con `ConvertibleGuardiaCivil` los NPC/coches candidatos (los que pueden
   volverse tricornios); marca `esCoche` en los coches.
⏱️ **Gate 4.3**: sube paranoia (forzando delitos o por código) ≥70 fuera de cámara →
NPC/coches se convierten gradualmente; baja la paranoia → revierten.

### 4.4 Controles de carretera
1. Mueve `_ControlesGC~/` → `Assets/Scripts/Runtime/ControlesGC/`.
2. Coloca GameObjects `ControlGuardiaCivil` en los pasos clave: **calle San Juan,
   puentes del Arakil, salidas de la N-1**. Cada uno: un BoxCollider (isTrigger) que
   tape la calzada + un hijo `barrera` (cono/foco). Asigna `sintonia`.
3. GameObject `ControlesGC` con `SistemaControlesGC`; deja que autodetecte o asigna la
   lista; asigna `sintonia`.
⏱️ **Gate 4.4**: a paranoia alta aparecen N controles (off-screen); cruzar uno con
búsqueda ≥1 da el alto (cacheo o arresto); con apoyo alto te cuelan.

### 4.5 Testigos
1. Mueve `_Testigos~/` → `Assets/Scripts/Runtime/Testigos/`.
2. Pon `TestigoNPC` en los NPC civiles (o que el spawner se lo añada). GameObject
   `Testigos` con `SistemaTestigos`; asigna `capaObstaculos` y `sintonia`.
3. ✓ YA HECHO: los sitios de delito (`SistemaDestruccion`, `SistemaConsecuencias`,
   `SistemaArmasExtendido`) publican `DelitoEvent` y `SistemaTestigos` se suscribe.
   No hay que tocar código; solo coloca el componente. (Para delitos nuevos, publica
   `EventBus.Publish(new DelitoEvent{ lugar=pos, gravedad=0..1 })`.)
⏱️ **Gate 4.5**: cometer un delito a la vista de un vecino con apoyo bajo → sube
wanted/paranoia tras el retardo; con apoyo alto → te cubren (sin reporte).

### 4.6 Coartada (refugios)
1. Mueve `_Coartada~/` → `Assets/Scripts/Runtime/Coartada/`.
2. GameObject `Coartada` con `SistemaCoartada`; asigna `capaAutoridad`,
   `capaObstaculos` y `sintonia`.
3. Pon `ZonaCoartada` (Collider isTrigger) en refugios: bares de la calle San Juan,
   portales, gaztetxe. Ajusta `calidad` (0-1) por refugio.
⏱️ **Gate 4.6**: dentro de un refugio y sin que te vea autoridad, wanted y paranoia
bajan (más rápido con apoyo alto); al bajar paranoia, los tricornios revierten.

> Todos los `sintonia` apuntan al MISMO asset → balanceas el sandbox desde un sitio.

---

## FASE 5 — Misiones data-driven (opcional)

1. Mueve `_NarrativaJSON~/CargadorMisionesJSON.cs` → `Assets/Scripts/Runtime/`.
2. Desde `SistemaMisiones`, carga `_NarrativaJSON~/misiones_altsasu.json` (12 misiones
   + 4 laterales + 3 finales). Guion y biblia: `Docs/Narrativa/`.
⏱️ **Gate 5**: las misiones M01–M12 se enlazan en cadena; M00 (tutorial) arranca con
`saltarIntro=false`.

---

## FASE 6 — Gates finales (antes de dar por bueno)

- ⏱️ **[✅ Validar georreferenciación]** verde con el clipmap activo.
- ⏱️ Cota de Herriko Plaza ≈ 531.94 m (log del adaptador V3).
- ⏱️ Sin *pop* visible al cruzar bandas de streaming (histéresis + dither).
- ⏱️ Frame-time estable: los dos directores (`GlobalSimulationOrchestrator` CPU,
  `GobernadorRender` GPU) y `StreamerMundoEstatico` regulan carga sin estampida.
- ⏱️ Frame Debugger: el terreno son 1-2 draw calls (clipmap) y los edificios lejanos
  van por impostor.

---

## Resumen del orden (una línea por fase)
0. Reinicio limpio + abrir + compila sin Safe Mode.
1. **UTM real**: validar datos → APLICAR TODO → gate georreferencia + tests.
2. **Clipmap V3**: mover scripts → ShaderGraph + HLSL → material + componentes →
   wiring → Play → validar cota → retirar V2.
3. **Impostores**: mover scripts → ShaderGraph Unlit → bake atlas → gestor + hook.
4. **Calor y alivio**: panel → capas → ParanoiaGC → controles → testigos → coartada.
5. **Misiones JSON** (opcional).
6. **Gates finales**.

Si te atascas en cualquier paso (sobre todo el Shader Graph del clipmap), dime el
número de paso y el error exacto y lo resolvemos.
