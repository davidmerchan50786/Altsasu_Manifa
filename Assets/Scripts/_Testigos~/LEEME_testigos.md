# Sistema de testigos — "aquí nos conocemos todos" (staging)

Carpeta `~` → **Unity no la compila**. El mundo abierto se vuelve reactivo: si un vecino te ve
delinquir, te **delata** (sube wanted/paranoia); si el **apoyo** es alto, te **cubre**. Espejo
de la coartada (que enfría el calor; los testigos lo generan).

## Qué hay
- `TestigoNPC.cs`: marca a un NPC civil como posible testigo. `Chivarse()` (con retardo) /
  `Cubrir()`. Corrutina guardada y cancelada en OnDestroy.
- `SistemaTestigos.cs`: manager. `ReportarDelito(lugar, gravedad)` busca testigos en rango + LOS
  y, por probabilidad según apoyo, los hace chivarse o cubrir.

## Cómo conectarlo — YA HECHO ✓ (vía DelitoEvent)
El cableado está hecho y **desacoplado por EventBus**: el sistema se suscribe a
`DelitoEvent` (Core) en OnEnable. Los sitios de delito ya publican el evento (no
dependen de este sistema, así que compilan aunque Testigos esté sin activar):
- `SistemaDestruccion` (molotov, gravedad 0.6)
- `SistemaConsecuencias` (civil muerto, gravedad 1.0)
- `SistemaArmasExtendido` (disparo 0.6 · tirachinas 0.3 · molotov 0.8 · bomba lapa 1.0)

Para añadir un sitio nuevo, publica el evento donde subas paranoia:
```
EventBus.Publish(new DelitoEvent { lugar = pos, gravedad = 0.5f });
```
(o, equivalente, el atajo directo `SistemaTestigos.ReportarDelito(pos, 0.5f)`).
`gravedad` ~ 0.2 (pintada) … 1.0 (algo gordo).

## Lógica
1. `probChivar = lerp(0.9, 0.05, apoyo/100) × lerp(0.3, 1, gravedad)`.
   - apoyo 0 → casi todos chivan; apoyo 100 → casi nadie.
2. Solo cuentan testigos en `rangoTestigo` **y con línea de visión** (Linecast contra
   `capaObstaculos`: si hay muro, no te vio).
3. Si se chiva: tras `retardoReporte` s, `AumentarBusqueda(+gravedad×2)` y `SumarParanoia(gravedad×5)`.
4. Si te cubre: nada (feedback breve). El que ya está `Ocupado` no vuelve a chivarse (anti-spam).

## Activar
1. Mueve los 2 `.cs` a `Assets/Scripts/Runtime/`.
2. Pon `TestigoNPC` en los NPC civiles (o que el spawner/multitud se lo añada al activarse).
3. Pon `SistemaTestigos` en la escena, asigna `capaObstaculos`.
4. Llama `SistemaTestigos.ReportarDelito(...)` en tus eventos de delito.

## Encaje en el ecosistema (ver INTEGRACION_Sistemas.md)
- **Generan calor**: testigos → wanted/paranoia ↑ (modulado por apoyo).
- **Alivian calor**: coartada → wanted/paranoia ↓ (modulado por apoyo).
- El **apoyo** es el dial: calle alta = nadie te vende y te escondes mejor; calle baja = te
  delatan y te cuesta esconderte. Cierra el bucle con la conversión a Guardia Civil.

## Punto de integración (cuando abras Unity)
- LOS: igual que la coartada, podría consultar `SistemaDeteccionIA` (visión jobificada) en vez
  del Linecast propio. Aceptable de momento (solo corre en el instante de un delito, barato).
