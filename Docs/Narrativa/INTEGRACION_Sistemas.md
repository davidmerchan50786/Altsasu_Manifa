# Integración de sistemas "calor y alivio" (heat & relief)

Auditoría de solapes entre los sistemas nuevos (GuardiaCivil, coartada, HUD paranoia) y los
del proyecto, y cómo quedan integrados. Una sola fuente de verdad por señal.

## El bucle (cómo encajan todos)
```
        delito / mani / destrucción
                 │  (SumarParanoia, AumentarBusqueda)
                 │  + SistemaTestigos.ReportarDelito() → vecinos que VEN
                 │    se chivan (calor↑) o te cubren — según APOYO
                 ▼
  ┌─────────────────────────────┐        feed wanted≥3 → +paranoia
  │  WANTED (IWantedSystem)      │ ───────────────────────────────┐
  └─────────────┬───────────────┘                                 ▼
                │                       ┌──────────────────────────────────┐
                │                       │  PARANOIA (SistemaApoyoPopular)   │◄── ÚNICA fuente
                │                       │  eventos: OnParanoiaCambia/Critica│
                │                       │  decay propio (decayParanoia)     │
                │                       └───────┬───────────────┬──────────┘
                │                               │ ≥70           │ alto apoyo FRENA
                │                               ▼               ▼
                │                   ┌────────────────────┐  (FactorApoyo)
                │                   │ SistemaParanoiaGC  │  convierte NPCs/coches → tricornios (móviles)
                │                   │ SistemaControlesGC │  monta controles en los pasos (estáticos)
                │                   └─────────┬──────────┘
                │  AumentarBusqueda(-1)        │ revierten al bajar paranoia
                ▼                               │
  ┌─────────────────────────────┐  RestarParanoia
  │  COARTADA (SistemaCoartada) │ ─────────────► baja wanted Y paranoia si te escondes y
  └─────────────────────────────┘                nadie te ve (calle alta = más rápido)
```
Loop completo: delito → wanted↑ → paranoia↑ → vecinos→Guardia Civil → te escondes (coartada) →
wanted↓ + paranoia↓ → tricornios revierten. **El apoyo popular es el dial que lo modula todo.**

## Solapes encontrados y resolución

| # | Solape | Estado | Resolución |
|---|--------|--------|-----------|
| 1 | **Dos paranoias**: `SistemaApoyoPopular.paranoia` (canónica) y `SistemaParanoia` (suelto, huérfano) | **RESUELTO** | `SistemaParanoia` ahora es **fachada** que delega en `SistemaApoyoPopular`. Una sola paranoia. |
| 2 | **LOS duplicada**: `SistemaDeteccionIA` (slot, jobificado) vs raycast de la coartada / cerebro GC | **Aceptable / punto de integración** | La coartada solo hace raycast cuando el jugador está DENTRO de un refugio (raro y barato). Recomendado: cuando los guardias estén registrados en `SistemaDeteccionIA`, consultar su `TieneVision` en vez de raycast propio. |
| 3 | **HUD**: `HUDParanoia` (OnGUI scaffold) vs `HUDCanvas` (uGUI oficial) | **Punto de integración** | A producción: portar el medidor de paranoia a `HUDCanvas` con la tipografía serigrafiada. OnGUI es scaffold. |
| 4 | **Decay de wanted**: la coartada baja wanted | **Sin solape** | El proyecto NO decae el wanted en ningún sitio; la coartada es el único enfriado de búsqueda. |
| 5 | **Cerebros policía**: `CerebroGOAPPolicia` (Foral, GOAP) vs `CerebroGuardiaCivil` (GC, simple) | **Intencionado (facciones distintas)** | Son dos cuerpos: Foral = policía base GOAP; Guardia Civil = variante de paranoia, más agresiva. No compiten: el GC solo existe mientras hay conversión. |

## Fuente de verdad por señal (tras integrar)
| Señal | Dueño | Escriben | Leen |
|-------|-------|----------|------|
| **Paranoia** | `SistemaApoyoPopular` | destrucción, mani, economía, consecuencias, **testigos** (+), **coartada** (−), wanted≥3 (+) | DirectorMundo, **ParanoiaGC**, **HUDParanoia**, misiones; `SistemaParanoia` = fachada |
| **Apoyo** | `SistemaApoyoPopular` | misiones, eventos | ParanoiaGC (freno), coartada (ritmo), **testigos** (prob. chivar), HUD |
| **Wanted** | `IWantedSystem` (GameManager) | misiones, delitos, **testigos** (+), **coartada** (−) | CerebroGuardiaCivil, PatrullaGC, coartada |

## Recomendaciones de integración pendientes (cuando abras Unity)
1. **LOS unificada**: dar slot de `SistemaDeteccionIA` a los guardias convertidos y que tanto el
   `CerebroGuardiaCivil` como `SistemaCoartada.VistoPorAutoridad` consulten ese sistema (una sola
   visión jobificada) en vez de raycasts sueltos. Punto marcado en el código.
2. **HUD único**: mover el medidor de paranoia y el aviso "tricornios" a `HUDCanvas` (uGUI),
   retirando el OnGUI de scaffold.
3. **Retirar `SistemaParanoia`**: ahora es fachada; cuando confirmes que nada lo necesita por
   compatibilidad, se puede borrar y dejar solo `SistemaApoyoPopular`.

## Resumen
Tras la integración hay **una sola paranoia, un solo apoyo, un solo wanted**, y los sistemas
nuevos cuelgan de ellos por eventos/servicios, sin estado duplicado. Los dos puntos abiertos
(LOS y HUD) son optimizaciones que requieren el editor para validar; están documentados y
marcados en el código.
