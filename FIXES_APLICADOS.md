# Arreglos aplicados (auditoría 20 agentes) — verificados con compilación

Todos compilados a 0 errores en batchmode y respaldados. Rama `fresh`.

| # | Archivo(s) | Bug | Arreglo |
|---|---|---|---|
| 1 | SistemaClima.cs | El viento metía gravedad lateral GLOBAL cada frame (afectaba a todos los rigidbodies: personajes, coches, ragdolls) | Restaurar gravedad normal; el viento ya no toca `Physics.gravity` |
| 2 | SistemaArmasExtendido.cs | Munición infinita: cambiar de arma reseteaba `_municion` a tope | Munición persistente por arma (`_municionPorArma[]`) |
| 3 | SistemaVolumenHDRP.cs | Ciclo día/noche roto: buscaba "HoraActual"/"hora"/"Hour" pero la propiedad real es `HoraDelDia` | Añadido `HoraDelDia` al lookup por reflexión |
| 4 | SistemaDeteccionIA.cs, PoliciaForalIA.cs | Slots de detección nunca se liberaban → tras 32 policías, los nuevos ciegos | Lista de slots libres + `Liberar()` en `OnDestroy` del policía |
| 5 | ConductorMundo.cs | Árboles duplicados (AlsasuaTreeStreamer y PosicionadorPrecisionUrbana cargan el mismo lidar_trees.json) | El Conductor desactiva la colocación de árboles del Posicionador si el Streamer está presente |
| 6 | ControladorJugador.cs, HUDCanvas.cs | Indicador de daño del HUD siempre apuntaba a Vector3.zero | `UltimoOrigenDano` guarda el origen real; el HUD lo usa |
| 7 | HUDCanvas.cs | Minimapa renderizaba la escena COMPLETA cada frame (2º render full-scene) | Cámara sin auto-render + render manual throttled (~6-7 fps) |
| 8 | AudioManager.cs | AudioSources del pool sin rolloff/distancias → audio 3D mal escalado en mundo abierto | Rolloff lineal + minDistance 4 / maxDistance 90 + doppler |

## Falsos positivos descartados (la auditoría se equivocó; verificado)
- "Terreno aplastado (size.y=900 vs 57m)": **correcto que sea 900** — incluye Sierra de Aralar (1400m); el valle es plano de verdad.
- "VolMaster aplicado dos veces": solo se aplica una vez en SistemaOpciones.
- "Material por edificio rompe instancing": `MaterialPorAnio` devuelve un string, no crea Material por edificio.

## Pendiente (features grandes — mejor con playtest en Unity)
- Tráfico que circule de verdad (necesita red de carriles, no waypoints sueltos).
- IA de policía a pie conectada al nivel de búsqueda (estrellas).
- Niebla por clima vía HDRP Fog (hoy usa RenderSettings, inerte en HDRP).
- `capasObstaculo = ~0` en policía (incluye al propio NPC) — necesita conocer las capas reales.

## Cómo verificar
Abrir `E:/Desk/DAM/Altsasu_Manifa` en Unity 6000.3.10f1 → Play. Backup del código en `C:/Altsasu_Backup_Codigo_320ea047/`.
