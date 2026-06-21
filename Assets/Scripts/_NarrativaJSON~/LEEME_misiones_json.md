# Misiones en JSON para SistemaMisiones — esquema y activación

Carpeta `~` → **Unity no la compila**. Datos del arco narrativo (`misiones_altsasu.json`)
+ cargador staged (`CargadorMisionesJSON.cs`). El JSON guarda **datos**; las
`Condicion`/`AlCompletar` (delegados C#) las construye el cargador con una **factoría por
`tipo`** que usa tus sistemas reales.

## Modelo destino (ya en el proyecto)
`Mision` (base, en `SistemaMisiones.cs`): `Nombre`, `AlIniciar:Action`, `Objetivos:List<Objetivo>`,
`SiguienteMision:Mision`. `Objetivo`: `Descripcion`, `Condicion:Func<bool>`, `AlCompletar:Action`.

## Cómo activar
1. Mueve `CargadorMisionesJSON.cs` (y las clases de datos) → `Assets/Scripts/Runtime/`.
2. Mueve `misiones_altsasu.json` → `Assets/StreamingAssets/` (o deja la ruta que use el cargador).
3. Añade un **almacén de flags** narrativos (3 líneas): `public static class FlagsNarrativos {
   static readonly HashSet<string> _f = new(); public static void Set(string k)=>_f.Add(k);
   public static bool Get(string k)=>_f.Contains(k); }`.
4. En el arranque, `var cadena = CargadorMisionesJSON.CargarTodas(); SistemaMisiones.Instance.Iniciar(cadena["M01"]);`

## Vocabulario de `tipo` → factoría (qué construye el cargador)

| `tipo` | Condicion (Func<bool>) | AlCompletar (Action) |
|--------|------------------------|----------------------|
| `llegar` / `llegar_sigilo` | `Dist2D(JugadorPos, punto) < radio` (sigilo: + `SinWanted()`) | aplicar apoyo/dinero |
| `escolta_lenta` | llegar a target manteniendo `velMax` | — |
| `minijuego` | flag de minijuego (`FlagsNarrativos.Get(minijuego+"_ok")`) | — |
| `interactuar` | trigger/raycast sobre `objeto` | — |
| `recolectar` / `recolectar_eleccion` | contador de `item` ≥ `n` | — |
| `pintar` | contador `SistemaGrafitis.OnPintadaRealizada` ≥ `n` | `RestarApoyo` si "sucio" |
| `escapar_wanted` | `MisionHelper.NivelBusqueda == 0` (o baja de `estrellas`) | — |
| `escapar_zona` / `escapar_temporizado` | `Dist2D > radio` (y/o `timer < segundos`) | — |
| `decision` / `decision_opcional` / `decision_final` | el jugador elige opción → fija `flag`, aplica `apoyoDelta`/`dineroDelta` | bifurca |
| `escuchar_audio` / `cinematica` | clip reproducido hasta el final | — |
| `manifestacion` / `defender` | `SistemaManifestacion` aguanta `oleadas`; si capturado → `PlayerArrestedEvent` | — |
| `puzzle_orden` / `puzzle_evidencias` | minijuego de puzzle resuelto → flag | — |
| `persuadir` / `sabotaje` / `puja` | sistemas sociales de M10 (apoyo facción) | — |
| `cruzar_multitud` / `recuperar_vehiculo` | M11 (multitud BRG + recuperar 207D) | apoyo según cuidado |
| `pesca` / `encargo_comico` | laterales repetibles | apoyo pequeño |

**AlCompletar genérico** (siempre): `apoyoDelta`>0 → `SistemaApoyoPopular.Instance.SumarApoyo`;
`<0` → `RestarApoyo`; `dineroDelta` → `MisionHelper.GanarDinero`; `flagsSet` → `FlagsNarrativos.Set`.

## Encadenado y finales
- `siguiente` lineal M01→M11. **M12** usa `ramas`: se evalúan en orden, la primera cuyo
  `condicion` (`apoyoMin` y/o `flag`) se cumpla decide el `resultado` (FINAL_A/B/C). La última
  rama con `condicion: {}` es el default (FINAL_B).
- `apoyoMin` se compara contra `SistemaApoyoPopular.Instance.apoyo` (0–100).
- `flag` se consulta en `FlagsNarrativos.Get`.

## Notas
- Campos `apoyoDeltaSiSucio`, `apoyoDeltaSiArrestado`, `apoyoDeltaDiferido`, `apoyoDeltaSegunCruce`
  son modificadores condicionales: el cargador los aplica según el resultado de la misión.
- `desbloquea` activa laterales/BSO (`L2` desbloquea `bso_tema_quinta`).
- Es un esqueleto: los `tipo` marcados como TODO en el cargador devuelven una condición trivial
  (log + true) para que la cadena avance mientras implementas cada mecánica en el editor.
