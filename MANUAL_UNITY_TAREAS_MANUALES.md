# MANUAL DE TAREAS MANUALES EN UNITY
## Altsasua Simulator — Lo que la IA no puede hacer por ti

> Todo lo demás (código, sistemas, misiones, HUD, físicas, audio, etc.) está automatizado.
> Este manual cubre SOLO lo que requiere interacción directa con el Editor de Unity.

---

## 📋 ÍNDICE DE PRIORIDAD

| Prioridad | Tarea | Bloquea el juego |
|-----------|-------|-----------------|
| 🔴 CRÍTICO | Build Settings (escenas) | Sí — sin esto no arranca |
| 🔴 CRÍTICO | HDRP Pipeline Asset | Sí — todo magenta |
| 🟠 ALTO    | Tags (Player, Enemy…) | Sí — IA no encuentra jugador |
| 🟠 ALTO    | WheelCollider posiciones | Sí — coche no conduce |
| 🟡 MEDIO   | Humanoid Avatar (GC) | No — usa procedural |
| 🟡 MEDIO   | NavMesh bake manual | No — se hornea en runtime |
| 🟢 BAJO    | Occlusion Culling bake | No — solo FPS |
| 🟢 BAJO    | Materiales HDRP conversión | No — funcional pero feo |

---

## 🔴 PASO 1 — HDRP Pipeline Asset (CRÍTICO)

**Síntoma si falta:** Todo el juego se ve de color **magenta/rosa**.

### Pasos:
1. `Edit → Project Settings → Graphics`
2. Campo **Scriptable Render Pipeline Settings** → arrastra:
   `Assets/Settings/HDRenderPipelineAsset.asset`
3. Si no existe ese asset:
   - `Window → Package Manager → High Definition RP → Install`
   - Espera la importación (5-10 min)
   - `Edit → Rendering → Materials → Convert All Built-in Materials to HDRP`
   - En el diálogo → **Proceed**

### Verificación:
- El cielo debe verse azul (no magenta)
- Las cajas de edificios deben ser grises (no rosas)

---

## 🔴 PASO 2 — BUILD SETTINGS (CRÍTICO)

**Síntoma si falta:** El botón "Jugar" del menú principal da error "Escena no encontrada".

### Pasos:
1. `File → Build Settings`
2. Pulsa **Add Open Scenes** con cada escena abierta, O arrastra:
   - `Assets/#Scenes/MenuPrincipal.unity` → posición **0**
   - `Assets/#Scenes/Alsasua_Main.unity` → posición **1**
3. Asegúrate de que MenuPrincipal es la escena 0 (el índice importa)
4. Cierra Build Settings

### Si las escenas no existen aún:
- Ejecuta `Tools → Alsasua → ★ SETUP MAESTRO COMPLETO ★`
- Espera 10-15 minutos

---

## 🟠 PASO 3 — TAGS Y CAPAS (ALTO)

**Síntoma si falta:** `AltsasuCore` no encuentra al jugador. Los NPCs no huyen. La cámara no sigue a nadie.

### Tags necesarios:
1. `Edit → Project Settings → Tags and Layers`
2. En la sección **Tags**, añade (botón `+`):
   - `Player`
   - `Enemy`
   - `Vehicle`
   - `NPC`
   - `Interactable`
   - `Weapon`
   - `Barricada`

### Asignar al jugador:
1. `Hierarchy → [prefab del jugador]`
2. Inspector (arriba) → campo **Tag** → selecciona `Player`
3. Aplicar al prefab si es prefab: `Overrides → Apply All`

### Capas necesarias:
En Tags and Layers → **Layers** (User Layer 8 en adelante):
- Layer 8: `Ground`
- Layer 9: `Building`
- Layer 10: `Vehicle`
- Layer 11: `NPC`
- Layer 12: `Weapon`

### Physics Matrix:
1. `Edit → Project Settings → Physics`
2. En **Layer Collision Matrix**, desmarcar:
   - `NPC` vs `NPC` (evita que los civiles se empujen)
   - `Weapon` vs `Weapon`

---

## 🟠 PASO 4 — WHEELCOLLIDERS DEL INTERCEPTOR (ALTO)

**Síntoma si falta:** El coche flota, se hunde, vira solo o no conduce.

### Pasos:
1. `Project → Assets/Prefabs/Coches/Interceptor_Jugador.prefab`
2. **Doble click** en el prefab → se abre el Prefab Editor
3. En la Hierarchy del prefab verás 4 hijos: `WC_FL`, `WC_FR`, `WC_RL`, `WC_RR`

### Posiciones correctas (ajusta según tu modelo):
```
WC_FL (Front Left):   X = -0.77,  Y = 0.33,  Z =  1.50
WC_FR (Front Right):  X =  0.77,  Y = 0.33,  Z =  1.50
WC_RL (Rear Left):    X = -0.77,  Y = 0.33,  Z = -1.45
WC_RR (Rear Right):   X =  0.77,  Y = 0.33,  Z = -1.45
```

### Cómo verificar:
- Dale **Play** con el coche en escena
- Si **flota** → baja el `Y` de los 4 WheelColliders (prueba 0.25)
- Si se **hunde** → sube el `Y` (prueba 0.40)
- Si **vira solo** → los `X` no son simétricos, iguálalos
- Si no **avanza** → el `Z` de los traseros debe ser negativo

### Radio de la rueda:
- Inspector del WheelCollider → campo **Radius**
- Debe coincidir con el radio visual de la rueda del modelo 3D
- Para el Interceptor típico: `0.35`

---

## 🟠 PASO 5 — PREFAB ENEMIGO EN GAMEMANAGER (ALTO)

**Síntoma si falta:** Ningún policía aparece cuando subes el wanted.

### Pasos:
1. `Hierarchy → GameManager`
2. Inspector → busca campo **Prefab Enemigo**
3. Arrastra desde Project:
   - **Opción A** (si tienes el GC con Humanoid): `Assets/_ExtractedAssets/Personajes/GuardiaCivil_Officer_01.fbx`
   - **Opción B** (fallback): `Assets/LowPolySoldiers_demo/models/Soldier_demo.FBX`
4. Campo **Prefab Coche Policia** → arrastra `Assets/Prefabs/Coches/Interceptor_Jugador.prefab`

---

## 🟡 PASO 6 — HUMANOID AVATAR (GUARDIA CIVIL)

**Síntoma si falta:** El Guardia Civil aparece en T-pose estática.
**Alternativa:** El juego usa NPCs procedurales (cajas coloreadas) — funcional.

### Solo necesario si tienes el FBX del Guardia Civil:
1. `Project → Assets/_ExtractedAssets/Personajes/GuardiaCivil_Officer_01/`
2. Selecciona el `.fbx`
3. Inspector → pestaña **Rig**
4. **Animation Type** → `Humanoid`
5. **Avatar Definition** → `Create From This Model`
6. Pulsa **Apply**
7. Pulsa **Configure...** → verifica círculos verdes en Hips, Spine, Head, brazos y piernas
8. Pulsa **Done**

### Asignar el Animator Controller:
1. Selecciona el FBX del GC
2. Inspector → pestaña **Animation**
3. Rig → Animator Controller → arrastra `Assets/Animators/JugadorAnimator.controller`
4. O ejecuta: `Tools → Alsasua → 🎭 Configurar Animator Jugador`

---

## 🟡 PASO 7 — CLIPS DE ANIMACIÓN (PARA JUGADOR Y NPCS)

**Síntoma si falta:** Los personajes se deslizan sin animar. El sistema IK funciona pero no hay movimiento de extremidades.

### El animator controller ya está creado con placeholders.
### Necesitas asignar clips reales en cada estado:

1. `Project → Assets/Animators/JugadorAnimator.controller`
2. **Doble click** → abre el Animator
3. Para cada estado (Locomotion/Jump/Fall/Drive/etc.):
   - Click en el estado → Inspector → campo **Motion**
   - Arrastra el clip desde tu paquete de animaciones
4. **Clips mínimos necesarios:**
   - `Idle` — personaje quieto respirando
   - `Walk` — caminar a ~1.4 m/s
   - `Run` — correr a ~5 m/s
   - `Jump` — salto (0.5s)
   - `Drive` — sentado conduciendo

### Fuentes de clips gratuitos:
- **Mixamo** (mixamo.com) — descarga con Auto-Rig para Unity
- **Unity Asset Store** → "Basic Motions FREE"
- **Kenny** → kenney.nl/assets

---

## 🟡 PASO 8 — TEXTMESHPRO ESSENTIALS

**Síntoma si falta:** Algunos elementos de UI pueden tener texto roto.
**Nota:** El HUD usa `UnityEngine.UI.Text` (legacy) que funciona sin TMP.

### Solo necesario si añades UI con TextMeshPro:
1. `Window → Package Manager`
2. Busca **TextMeshPro** → Install
3. Tras instalar → popup automático → pulsa **Import TMP Essentials**

---

## 🟡 PASO 9 — OCCLUSION CULLING (RENDIMIENTO)

**Síntoma si falta:** El juego va a 15-25 FPS en la zona densa.
**Sin esto:** El juego funciona, pero lento.

### Pasos:
1. Asegúrate de que la escena `Alsasua_Main` está abierta
2. Los edificios deben ser **Static** (el Setup Maestro los marca así)
3. `Window → Rendering → Occlusion Culling`
4. Pestaña **Bake** → pulsa el botón azul **Bake**
5. Espera 3-8 minutos
6. Aparece: "Bake completed" en la barra de estado

### Verificación:
- `Scene View → botón desplegable → Overdraw` → verás en rojo qué se renderiza de más
- Tras el bake, el rojo dentro de edificios desaparece

---

## 🟡 PASO 10 — CONVERSIÓN MATERIALES HDRP

**Síntoma si falta:** Paquetes de terceros (coches, soldados) tienen materiales magenta/rosas.

### Pasos:
1. `Edit → Rendering → Materials → Convert All Built-in Materials to HDRP`
2. En el diálogo de confirmación → **Proceed**
3. Espera 1-3 minutos

### Si algunos materiales siguen mal:
- Selecciona el material en Project
- Inspector → campo **Shader** → cambia a `HDRP/Lit`
- Asigna las texturas manualmente: `Base Map`, `Normal Map`, `Mask Map`

---

## 🟢 PASO 11 — AUDIOMIXER (OPCIONAL PERO RECOMENDADO)

**Estado actual:** AudioManager genera audio sintético funcional.
**Con AudioMixer:** Control más preciso de reverb, compresión, EQ por categoría.

### Si quieres AudioMixer real:
1. `Assets → Create → Audio Mixer` → nombra "AltsasuaMixer"
2. Crear 4 grupos: `Master`, `SFX`, `Musica`, `Ambiente`
3. En AudioManager.cs línea ~60, asignar el mixer via campo público Inspector

---

## 🟢 PASO 12 — TERRAIN LAYERS (TEXTURAS DEL TERRENO)

**Estado actual:** El terreno usa un color base gris/verde.
**Con layers:** Textura de hierba, tierra, piedra, asfalto según pendiente.

### Pasos:
1. Selecciona el Terrain en Hierarchy
2. Inspector → icono de pincel → **Paint Texture**
3. `Edit Terrain Layers → Add Layer`
4. Asigna las texturas descargadas de Poly Haven:
   - `grass_path` → zonas planas
   - `rocky_ground` → pendientes > 30°
   - `asphalt` → carreteras (sobre el terrain base)
5. Usa el pincel para pintar cada zona

### Automatizado:
`Tools → Alsasua → 🎨 Aplicar Texturas AAA` — aplica automáticamente las texturas PBR.

---

## 🟢 PASO 13 — LUCES HDRP Y HDRI SKY

**Estado actual:** Luz solar procedural astronómica funcional.
**Con HDRI:** Iluminación de imagen basada en fotografía real.

### Asignar HDRI:
1. `Hierarchy → Sky and Fog Volume` (o crea un Volume global)
2. Inspector → Add Override → `HDRI Sky`
3. Campo **HDRI Cubemap** → arrastra uno de:
   `Assets/HDRIs/belfast_sunset_puresky_4k.hdr`

### Exposición:
1. Add Override → `Exposure`
2. Modo: `Automatic` o `Fixed`
3. Compensa en +/- 1 EV según el resultado

---

## 🟢 PASO 14 — CONFIGURACIÓN DEL JUGADOR EN PREFAB

**Lo que el Setup Maestro NO puede hacer automáticamente:**

### En el prefab del jugador (`Assets/Prefabs/Jugador/Jugador_Altsasua.prefab`):
1. Asignar el **SistemaIKProcedural.puntoAgarre** → Transform del grip del arma
2. Ajustar la **altura de la cámara** (campo `alturaCamara` en ControladorJugador)
3. Verificar que el **Rigidbody** tiene:
   - Mass: 75
   - Drag: 0 (se controla por código)
   - Constraints: Freeze Rotation X, Y, Z

---

## 🟢 PASO 15 — QUALITY SETTINGS

**Pasos:**
1. `Edit → Project Settings → Quality`
2. Duplica el nivel "High" y renómbralo "AAA+"
3. Ajustes recomendados:
   - **Shadow Distance**: 400
   - **Shadow Cascades**: 4
   - **LOD Bias**: 2.5
   - **Anisotropic Textures**: Per Texture
   - **Anti Aliasing**: 4x Multi Sampling

---

## ⚡ CHECKLIST RÁPIDO — ANTES DE DARLE PLAY

Marca cuando esté hecho:

```
☐ 1. HDRP Pipeline Asset asignado (Edit → Project Settings → Graphics)
☐ 2. Escenas en Build Settings (MenuPrincipal=0, Alsasua_Main=1)
☐ 3. Tag "Player" asignado al prefab del jugador
☐ 4. Tags Enemy, Vehicle, NPC creados
☐ 5. WheelColliders en posición correcta (Y=0.33 aprox)
☐ 6. Prefab Enemigo asignado en GameManager
☐ 7. Ejecutar Setup Maestro si la escena no existe
☐ 8. Ejecutar Limpiar Missing Scripts
☐ 9. Clic en Play — esperar "[NavMesh] ✅ NavMesh listo"
☐ 10. Probar: WASD mueve, ratón gira, E entra en coche
```

---

## 🚨 ERRORES COMUNES Y SOLUCIONES

| Error en Consola | Causa | Solución |
|-----------------|-------|----------|
| `NullReferenceException: AltsasuCore.I` | Boot order incorrecto | Añade AltsasuCore a la escena con `-100` execution order |
| `Tag: Player is not defined` | Falta crear el tag | Paso 3 de este manual |
| `No NavMesh data` | NavMesh no horneado | Se hornea en runtime — espera 3s tras Play |
| `WheelCollider requires a Rigidbody` | Prefab sin Rigidbody | Añade Rigidbody al GameObject raíz del coche |
| `Shader not found: HDRP/Lit` | HDRP no instalado | Instala el paquete HDRP desde Package Manager |
| `AudioManager.I is null` | Boot order | AltsasuCore crea AudioManager automáticamente |
| `Cannot find animator` | Sin Animator component | Añade Animator al jugador, asigna JugadorAnimator.controller |
| Todo magenta | HDRP sin configurar | Paso 1 de este manual |
| Missing Scripts en Hierarchy | Paquetes incompatibles | Tools → Alsasua → 🧹 Limpiar Missing Scripts |

---

## 📁 ESTRUCTURA DE ARCHIVOS ESPERADA

```
Assets/
├── #Scenes/
│   ├── MenuPrincipal.unity      ← escena 0 en Build Settings
│   └── Alsasua_Main.unity       ← escena 1 en Build Settings
├── Animators/
│   └── JugadorAnimator.controller  ← creado por ConfiguradorAnimator
├── Audio/                       ← clips reales (WAV/MP3) opcionales
├── HDRIs/                       ← *.hdr descargados por DescargarAssetsAAA.ps1
├── Prefabs/
│   ├── Coches/
│   │   └── Interceptor_Jugador.prefab
│   └── Personajes/
│       └── Jugador_Altsasua.prefab
├── Scripts/                     ← 60+ .cs generados automáticamente
├── Settings/
│   └── HDRenderPipelineAsset.asset
└── Textures_AAA/                ← 542 PNGs de Poly Haven
```

---

*Manual generado automáticamente — Altsasua Simulator v3*
*Todo lo que no aparece aquí está automatizado en código.*
