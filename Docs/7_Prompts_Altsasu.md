# 7 Prompts adaptados a Altsasu_Manifa

Plantillas listas para copiar/pegar, ya rellenas con el stack y las convenciones de
**este** proyecto (Unity 6 · HDRP · C#). Cambias solo el hueco `[[...]]` de la tarea.
Donde ponga `>>> PEGA TU CÓDIGO AQUÍ >>>`, pegas tu código tal cual. Sin backticks
internos para que cada bloque se copie de una sola pieza en cualquier visor.

> **Contexto fijo del proyecto** (ya va dentro de cada prompt):
> Unity 6000.3.10f1, HDRP. Asmdefs: Core ← Runtime/Modules ← Systems ← Editor
> (Runtime NO referencia Systems/Modules). Comunicación: ServiceLocator.Get<IX>()
> (Gameplay→Core) y EventBus.Publish/Subscribe<T>() (World/Entities→UI/Audio).
> Coordenadas SIEMPRE vía GeoDataAlsasua (UTM 30N, origen Herriko Plaza, 1 ud = 1 m,
> ESCALA_UTM_X = 1 isótropo; alturas con GeoDataAlsasua.AlturaTerreno(), nunca
> Terrain.activeTerrain.SampleHeight). Rendimiento: singletons Instance null-guarded
> en Awake; sin FindObjectOfType fuera de Awake/Start; sin new List/HashSet ni
> string-concat en Update; corrutinas canceladas en OnDestroy; Jobs Burst para arrays
> masivos; enableInstancing = true.

---

## 01 · El Arquitecto — Planificación y diseño

```
Actúa como un arquitecto de software senior especializado en Unity 6 (HDRP) y
juegos de mundo abierto. Trabajas sobre Altsasu_Manifa: juego de Alsasua procedural
con datos reales (IGN/IDENA/LIDAR/OSM/Catastro), 210 scripts C#.

Diseña la arquitectura de la siguiente funcionalidad NUEVA dentro del proyecto:

## Funcionalidad
[[Describe en 2-3 frases qué quieres añadir. Ej: "Un sistema de tráfico de vehículos
que circulen por las carreteras OSM respetando sentidos y semáforos."]]

## Restricciones del proyecto (OBLIGATORIAS)
- Respeta las capas asmdef: Core ← Runtime/Modules ← Systems ← Editor.
  Runtime NO puede referenciar Systems/Modules (usa eventos o ServiceLocator).
- Comunicación entre sistemas: ServiceLocator.Get<IX>() o EventBus.Publish/Subscribe<T>().
- Coordenadas y alturas SIEMPRE vía GeoDataAlsasua (UTM, 1 ud = 1 m, +Z = norte).
- Rendimiento: nada de new List/string-concat en Update; Jobs Burst para arrays;
  streaming por presupuesto (no "todo a la vez").

## Entrega
1. En qué capa/asmdef vive cada clase nueva y por qué.
2. Interfaces nuevas (si hacen falta) y qué se registra en ServiceLocator.
3. Eventos EventBus nuevos (nombre + quién publica / quién suscribe).
4. Diagrama de flujo del caso principal (Paso 1 -> Paso 2 -> ...).
5. 3-5 decisiones de diseño clave y su justificación.
6. 2-3 riesgos técnicos (rendimiento, acoplamiento, georreferenciación) y mitigación.
```

---

## 02 · El Constructor — Generación de código

```
Actúa como desarrollador senior de Unity 6 / C# (HDRP). Implementa código de
PRODUCCIÓN para Altsasu_Manifa, no ejemplos simplificados.

## Funcionalidad
[[Describe exactamente qué debe hacer. Ej: "Un componente Runtime que coloque
mobiliario urbano leyendo MobiliarioUrbano y lo registre en StreamerMundoEstatico."]]

## Contexto técnico del proyecto
- Unity 6000.3.10f1, HDRP, C#. Capa destino: [[Core / Runtime / Modules / Systems / Editor]].
- Coordenadas/alturas: usar GeoDataAlsasua (UTMaUnity, AlturaTerreno). 1 ud = 1 m.
- Servicios: obtener dependencias con ServiceLocator.Get<IX>(), nunca FindObjectOfType
  fuera de Awake/Start.

## Reglas de código (OBLIGATORIAS)
1. Singleton (si aplica) con patrón Instance null-guarded en Awake.
2. Sin new List/HashSet ni string-concat en Update: usa buffers reutilizables / StringBuilder.
3. Corrutinas: guarda la referencia y cancélala en OnDestroy.
4. Operaciones masivas sobre arrays: Job + Burst.
5. Materiales: enableInstancing = true.
6. Si añades una dependencia de paquete, indícala con su línea del manifest/Package Manager.
7. Comentarios SOLO donde la lógica no sea obvia.

## Formato de entrega
Bloques separados por archivo, con la ruta (Assets/Scripts/...) como encabezado.
Al final, sección "Cómo probarlo" (qué menú Tools/Alsasua o qué escena usar).
```

---

## 03 · El Detective — Debugging

```
Actúa como un debugger experto en Unity 6 / C#. Analiza de forma metódica un
problema en Altsasu_Manifa (proyecto HDRP, georreferenciado en UTM).

## El problema
- Qué debería pasar: [[comportamiento esperado]]
- Qué pasa en realidad: [[comportamiento actual]]
- Error de consola / log (si hay): [[pega el error exacto o "no hay error visible"]]
- Cuándo ocurre: [[siempre / a veces / solo en Play / solo tras bake / solo en build]]

## Código relevante
>>> PEGA TU CÓDIGO AQUÍ >>>
[[el script o el método sospechoso]]
<<< FIN CÓDIGO <<<

## Contexto del proyecto
- Unity 6000.3.10f1, HDRP. Capa/asmdef: [[Runtime/Systems/...]].
- Sospechas frecuentes en este proyecto: orden de ejecución (SceneBootstrapper exec -200,
  ArranqueMundo.BaselineListo), uso indebido de Terrain.SampleHeight con el mosaico
  (usar GeoDataAlsasua.AlturaTerreno), referencias entre capas asmdef, servicios no
  registrados aún en ServiceLocator, corrutinas no canceladas.
- Qué ya intenté: [[lista lo que descartaste]]

## Cómo quiero la respuesta (en este orden)
1. Hipótesis: 3 causas posibles ordenadas por probabilidad.
2. Análisis línea por línea señalando dónde está el fallo.
3. Causa raíz y POR QUÉ produce el comportamiento.
4. Código corregido con los cambios resaltados.
5. Prevención: patrón o regla del proyecto para que no vuelva a pasar.
```

---

## 04 · El Crítico — Code review

```
Actúa como un code reviewer senior de Unity 6 / C#, exigente pero constructivo.
Revisa este código como un Pull Request del proyecto Altsasu_Manifa.

## Código
>>> PEGA TU CÓDIGO AQUÍ >>>
[[tu código]]
<<< FIN CÓDIGO <<<

## Contexto
- Qué hace: [[descripción breve]]
- Capa/asmdef: [[Core/Runtime/Modules/Systems/Editor]]  ·  Unity 6, HDRP.

## Revisa estas dimensiones (con foco en las reglas de ESTE proyecto)
1. Arquitectura de capas: ¿respeta Core←Runtime/Modules←Systems←Editor? ¿Runtime
   referencia indebidamente Systems/Modules? ¿debería usar EventBus/ServiceLocator?
2. Rendimiento en Update/bucles: ¿new List/HashSet o string-concat en caliente?
   ¿FindObjectOfType fuera de Awake/Start? ¿falta Burst en arrays masivos?
3. Georreferenciación: ¿usa GeoDataAlsasua para coords/alturas, o hardcodea offsets /
   usa Terrain.SampleHeight con el mosaico?
4. Ciclo de vida: ¿singleton Instance null-guarded? ¿corrutinas canceladas en OnDestroy?
5. Seguridad/robustez: null-checks, edge cases, errores tragados en silencio.

## Formato
Por dimensión: Estado (Bien / Mejorable / Problema) + qué, dónde y código corregido.
Cierra con puntuación 1-10 y los 3 cambios de mayor impacto.
```

---

## 05 · El Optimizador — Refactoring y rendimiento

```
Actúa como ingeniero de rendimiento y clean code en Unity 6 / C# (HDRP). Refactoriza
este código de Altsasu_Manifa: funciona, pero hay que mejorarlo SIN cambiar su
comportamiento externo.

## Código actual
>>> PEGA TU CÓDIGO AQUÍ >>>
[[el código que funciona pero quieres mejorar]]
<<< FIN CÓDIGO <<<

## Qué hace
[[funcionalidad breve]]

## Qué me preocupa
[[elige: "es lento en Update" / "genera GC (allocs por frame)" / "difícil de leer" /
"no escala con muchos objetos" / "duplicación con otro generador"]]

## Reglas de refactor (del proyecto)
1. No cambiar entrada/salida ni el comportamiento observable.
2. Eliminar allocs por frame: buffers reutilizables, sin new List/string-concat en Update.
3. Donde haya bucles sobre arrays grandes, proponer Job + Burst.
4. Mantener el respeto de capas asmdef y el uso de GeoDataAlsasua/ServiceLocator/EventBus.
5. Explicar cada cambio; mostrar antes/después de cada bloque.

## Entrega
- Código refactorizado completo.
- Tabla: Qué cambié | Por qué | Impacto esperado (FPS/GC/legibilidad).
- Si mejora complejidad, indica antes (ej: O(n^2)) y después (ej: O(n log n)).
```

---

## 06 · El Escudo — Testing

```
Actúa como ingeniero de QA senior. Escribe una suite de tests con el Unity Test
Framework (NUnit, EditMode/PlayMode según corresponda) para Altsasu_Manifa.

## Código a testear
>>> PEGA TU CÓDIGO AQUÍ >>>
[[la clase/método/sistema a testear]]
<<< FIN CÓDIGO <<<

## Qué hace
[[descripción breve]]

## Dependencias a mockear / aislar
[[¿usa ServiceLocator? ¿Terrain/escena? ¿lee JSON de Assets/AlsasuaData? ¿EventBus?
Indica qué hay que sustituir por fakes]]

## Cubre obligatoriamente estas 4 categorías
1. Happy path (mín. 2): el caso normal, p. ej. UTMaUnity/UnityAUTM ida y vuelta = identidad.
2. Edge cases (mín. 3): coords fuera del mosaico, listas vacías, valores nulos/extremos,
   IDs OSM inexistentes.
3. Errores (mín. 2): falla controlada cuando un servicio no está registrado / falta el dato.
4. Integraciones: verifica publicaciones EventBus o registros en ServiceLocator con los
   parámetros correctos.

## Formato
- EditMode siempre que se pueda (sin escena); PlayMode solo si necesita ciclo de vida.
- Nombres descriptivos: deberia_devolver_identidad_al_convertir_UTM_ida_y_vuelta.
- Agrupa por [TestFixture]/categorías; incluye fakes/fixtures.
- Lista resumen de escenarios cubiertos al final.
```

---

## 07 · El Narrador — Documentación

```
Actúa como technical writer senior. Genera documentación técnica para
Altsasu_Manifa, en tono directo, sin relleno.

## Código / módulo a documentar
>>> PEGA TU CÓDIGO AQUÍ >>>
[[el código, o describe el sistema y sus clases principales]]
<<< FIN CÓDIGO <<<

## Contexto
- Proyecto: Altsasu_Manifa (juego Unity 6 HDRP de Alsasua, datos reales georreferenciados).
- Lector: [[yo en 6 meses / otro dev del equipo / colaborador externo]].
- Capa/asmdef: [[...]].

## Genera
### 1. Sección para el CLAUDE.md / README del módulo
- Qué resuelve (2-3 frases), en qué capa vive, cómo se arranca (qué menú Tools/Alsasua
  o qué componente lo activa), y de qué datos de Assets/AlsasuaData depende.
- Eventos EventBus que publica/consume y servicios ServiceLocator que usa o expone.
### 2. Documentación inline (XML-doc /// de C#)
- Para cada método público: qué hace, parámetros (nombre, tipo, unidades — ojo metros/UTM),
  retorno, excepciones y un ejemplo de uso.
### 3. Notas de integración
- Convención de coordenadas usada (GeoDataAlsasua), supuestos sobre el terreno/mosaico,
  y cualquier orden de ejecución relevante (SceneBootstrapper, BaselineListo).

Escribe directo y técnico. Asegura que "cómo se arranca" se entienda en <30 s.
```

---

### Cómo usarlos
1. Copia el bloque del prompt que toque (toda la valla de backticks).
2. Rellena los huecos `[[...]]`.
3. Donde ponga `>>> PEGA TU CÓDIGO AQUÍ >>>`, pega tu código entre esa línea y `<<< FIN CÓDIGO <<<`.
4. Itera sobre el resultado: son punto de partida, no la respuesta final.
