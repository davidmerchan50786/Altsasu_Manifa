# Guía de Desarrollo — Alsasua Manifa

## Índice

1. [Cómo añadir NPCs](#cómo-añadir-npcs)
2. [Cómo modificar el mapa](#cómo-modificar-el-mapa)
3. [Cómo crear eventos](#cómo-crear-eventos)
4. [Pipeline de arte](#pipeline-de-arte)
5. [Flujo de trabajo del equipo](#flujo-de-trabajo-del-equipo)

---

## Cómo añadir NPCs

### 1. Preparar el prefab del NPC

Un NPC funcional requiere los siguientes componentes en su GameObject raíz:

| Componente | Notas |
|---|---|
| `Animator` | Controlador con estados: `Idle`, `Run`, `Death` mínimo |
| `Rigidbody` | `Use Gravity: true`, `Freeze Rotation XZ: true` |
| `CapsuleCollider` | Ajustado a las dimensiones del modelo |
| `NavMeshAgent` | `Speed`, `Stopping Distance`, `Angular Speed` configurados |
| `ThirdPersonCharacter` | Del paquete Standard Assets (controla el movimiento físico) |
| `AICharacterControl` | Busca al jugador por tag `"Player"` y navega hacia él |
| `Health` | `CurrentHealth` con el valor inicial de vida |
| `AutoDestroy` | `TimeToDestroy` en segundos (p. ej. 5) para eliminar el cadáver |

El tag del NPC debe ser `"Enemy"` para que el sistema de armas y `CarDamage` lo detecten.

### 2. Configurar el Animator Controller

El Animator del NPC debe tener al menos:

```
Any State  ──[Death trigger]──►  Death (clip de muerte)
  Idle  ◄────────────────────►  Run  (parámetro float Speed)
```

- Parámetro `float Speed` controlado por `ThirdPersonCharacter`.
- Parámetro `trigger Death` o estado por condición en `Health.cs` (llamada a `animator.Play("Death")`).

### 3. Usar el modelo `Ch20_nonPBR.fbx` existente

El modelo `Assets/Models/Ch20_nonPBR.fbx` con su material `Ch20_body.mat` es el personaje NPC base del proyecto:

1. Arrastra `Ch20_nonPBR.fbx` a la escena o a la carpeta de prefabs.
2. Añade los componentes de la tabla anterior.
3. En `Health` asigna `CurrentHealth = 100`.
4. En `AutoDestroy` asigna `TimeToDestroy = 5`.
5. Arrastra el GameObject completo a `Assets/#Xtra/Prefabs` (o crea una subcarpeta `Assets/#Xtra/Prefabs/NPCs/`) para guardarlo como prefab.

### 4. Añadir el NPC a la escena

**Método A — Colocación manual:**
Arrastra el prefab del NPC a la escena en el punto deseado del mapa.

**Método B — Spawner en tiempo de ejecución:**
Usa `TestSpawner.cs`:
1. Crea un GameObject vacío en la escena y añade el componente `TestSpawner`.
2. Asigna `Spawnee` al prefab del NPC y `SpawnPos` al Transform de aparición.
3. Llama a `spawner.Spawn()` desde un evento de UI, Timeline o código.

```csharp
// Ejemplo: spawn desde otro script
GetComponent<TestSpawner>().Spawn();
```

### 5. Regenerar el NavMesh

Después de colocar nuevos obstáculos o cambiar la geometría del escenario:

1. **Window → AI → Navigation**.
2. Pestaña **Bake** → ajusta `Agent Radius`, `Agent Height` y `Max Slope`.
3. Pulsa **Bake**.

Los NPCs sólo se moverán por zonas marcadas como caminables en el NavMesh.

---

## Cómo modificar el mapa

### 1. Escena principal

Abre `Assets/OutdoorsScene.unity`. La escena contiene:

- **Terrain** — el suelo del escenario (si existe en la escena).
- **EscenarioSuelo** — assets del suelo (`Assets/EscenarioSuelo/`).
- **Luces y volúmenes HDRP** — Sky, Fog, Post-process.

### 2. Editar el terreno

Unity Terrain permite modelar el suelo, pintar texturas y añadir vegetación directamente desde el Inspector:

| Herramienta | Función |
|---|---|
| Raise / Lower Terrain | Esculpir alturas (montículos, depresiones) |
| Paint Texture | Pintar capas de textura (suelo, hierba, asfalto) |
| Paint Details | Colocar hierba y plantas (detalle de terreno) |
| Paint Trees | Colocar árboles instanciados |

**Para usar AutoGrass** (herramienta del proyecto):
1. Abre **Doctrina → AutoGrass** desde la barra de menús del Editor.
2. Asigna el Terrain en el campo correspondiente.
3. Selecciona el índice de textura base y el índice del detalle de hierba.
4. Ajusta la densidad y pulsa **Apply**.

### 3. Añadir edificios y props

1. Importa tu modelo (FBX, OBJ) a `Assets/Models/` o a una subcarpeta temática.
2. Arrastra el modelo a la escena.
3. En el Inspector activa **Static → Navigation Static** para que el NavMesh lo reconozca como obstáculo.
4. Asigna un material HDRP (`HDRP/Lit`) al renderer.
5. Ejecuta el bake de NavMesh (ver sección anterior).

### 4. Ajustar iluminación y atmósfera

- Selecciona el volumen de cielo/niebla en la jerarquía de la escena.
- Modifica el componente `Physical Sky` (posición del sol, temperatura de color, exposición) y `Volumetric Fog` (distancia, albedo, anisotropía).
- Para pruebas rápidas, cambia el perfil HDRP activo en **Edit → Project Settings → Quality** (Performant / Balanced / High Fidelity).

### 5. Configurar puntos de spawn del jugador

Crea un GameObject vacío en el punto de inicio deseado y asígnalo como posición inicial del jugador desde el script correspondiente o desde la cámara principal.

---

## Cómo crear eventos

Los eventos del juego se articulan mediante tres mecanismos disponibles: **Triggers de física**, **Timeline de Unity** y **Spawners**.

### A. Trigger de física

Es el mecanismo más sencillo. Añade un `BoxCollider` (o `SphereCollider`) con `Is Trigger: true` a un GameObject vacío. Adjunta un script con `OnTriggerEnter`:

```csharp
public class ManifaEvent : MonoBehaviour
{
    public TestSpawner spawner;
    public AudioSource crowd;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            spawner.Spawn();      // Spawn de NPCs
            crowd.Play();         // Reproducir sonido de multitud
            gameObject.SetActive(false); // Evitar re-trigger
        }
    }
}
```

Guarda el script en `Assets/#Xtra/` o en una carpeta `Assets/Events/`.

### B. Timeline de Unity

Unity Timeline permite coordinar animaciones, sonidos, activaciones de GameObjects y llamadas a scripts de forma sincronizada:

1. **Window → Sequencing → Timeline**.
2. Crea un `PlayableDirector` en la escena y un asset `Timeline` en `Assets/`.
3. Añade pistas:
   - **Activation Track** — activa/desactiva GameObjects (aparición de NPCs, barreras).
   - **Audio Track** — reproduce AudioClips sincronizados (megafonía, consignas).
   - **Animation Track** — anima cámaras o personajes cinemáticos.
   - **Signal Track** — emite señales para llamar métodos de scripts en momentos precisos.
4. Inicia el Timeline desde código o desde un trigger:

```csharp
public class EventTrigger : MonoBehaviour
{
    public PlayableDirector director;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
            director.Play();
    }
}
```

### C. Spawner parametrizado

Para crear oleadas de NPCs extiende `TestSpawner.cs` con un sistema de oleadas:

```csharp
using System.Collections;
using UnityEngine;

public class WaveSpawner : MonoBehaviour
{
    public GameObject npcPrefab;
    public Transform[] spawnPoints;
    public int npcsPerWave = 5;
    public float timeBetweenWaves = 10f;

    public void StartWaves()
    {
        StartCoroutine(SpawnWaves());
    }

    private IEnumerator SpawnWaves()
    {
        while (true)
        {
            for (int i = 0; i < npcsPerWave; i++)
            {
                Transform point = spawnPoints[i % spawnPoints.Length];
                Instantiate(npcPrefab, point.position, point.rotation);
            }
            yield return new WaitForSeconds(timeBetweenWaves);
        }
    }
}
```

Guarda el script en `Assets/#Xtra/` y adjúntalo a un GameObject coordinador en la escena.

---

## Pipeline de arte

### Modelado 3D

**Software recomendado:** Blender (gratuito) / Maya / 3ds Max.

**Convenciones de exportación:**

| Parámetro | Valor |
|---|---|
| Formato | FBX |
| Escala | 1 unidad Unity = 1 metro → exportar a escala 1:1 |
| Eje adelante | Z adelante, Y arriba (por defecto en Blender FBX export) |
| Suavizado | Smooth Groups o por normas de vértice |
| Animaciones | Cada clip como acción separada; usa Bake Action en Blender |

**Directrices de optimización:**

- Personajes jugables/NPCs: < 10 000 triángulos.
- Props de escenario: < 5 000 triángulos.
- Edificios y estructuras grandes: usar LOD Groups (LOD0, LOD1, LOD2).
- Usa `SimplestMeshBaker` (incluido en `Assets/#Xtra/SimplestMeshBaker/`) para combinar mallas de personajes skinned y reducir draw calls.

### Texturas

**Workflow de materiales HDRP (Metallic/Roughness):**

| Mapa | Canal | Resolución típica |
|---|---|---|
| Albedo (Color) | RGB | 2048×2048 |
| Normal | RGB (DirectX) | 2048×2048 |
| Mask (Metal/AO/Detail/Smooth) | RGBA | 2048×2048 |

El modelo existente `Ch20_nonPBR.fbx` usa un flujo Specular/Glossiness (no PBR estándar). Para integrarlo correctamente en HDRP:
1. Crea un material `HDRP/Lit` en modo Specular Color.
2. Asigna `Ch20_1001_Diffuse.png` → Albedo.
3. Asigna `Ch20_1001_Normal.png` → Normal Map.
4. Asigna `Ch20_1001_Specular.png` → Specular Color.
5. Asigna `Ch20_1001_Glossiness.png` → Smoothness.

**Software de texturas:** Substance Painter / Adobe Photoshop / GIMP.

### Audio

| Tipo | Formato | Sample rate | Bitrate |
|---|---|---|---|
| Efectos de sonido cortos (< 1 s) | WAV | 44 100 Hz | 16-bit |
| Efectos largos / ambiente | OGG | 44 100 Hz | 128 kbps |
| Música | OGG | 44 100 Hz | 192 kbps |

Coloca los archivos en la subcarpeta temática correspondiente:
- Armas → `Assets/#Sounds/`
- Motor / vehículos → `Assets/#Tools/Resources/Sounds/`
- Ambiente / multitudes → crea `Assets/Audio/Ambient/`

---

## Flujo de trabajo del equipo

### Ramas de Git

```
main          ← versión estable, sólo merges desde develop aprobados
develop       ← integración continua
feature/xxx   ← nuevas funcionalidades (una rama por tarea)
fix/xxx       ← correcciones de bugs
art/xxx       ← importación de assets de arte
```

**Reglas:**
- Nunca hacer commits directamente a `main`.
- Las ramas `feature/` y `art/` se mergean a `develop` mediante Pull Request con al menos una aprobación.
- Incluir `.meta` files en todos los commits de assets Unity (evita perder referencias).

### Estructura de commits

```
<tipo>(<ámbito>): <descripción breve en presente>

Tipos: feat, fix, art, docs, refactor, test, chore
Ejemplos:
  feat(npc): añadir sistema de patrulla a AICharacterControl
  fix(weapons): corregir índice fuera de rango en weaponsSetup
  art(mapa): importar texturas del centro de Alsasua
  docs: actualizar guía de desarrollo con sección de eventos
```

### Configuración de Unity para trabajo en equipo

El archivo `.gitignore` del proyecto ya excluye:
- `Library/` — caché de importación (se regenera localmente).
- `Temp/`, `Logs/`, `obj/`, `Build/` — archivos temporales.

**Importante:** El archivo `.gitattributes` ya configura Git LFS para los tipos de archivo binario de Unity (FBX, PNG, WAV, etc.). Asegúrate de tener Git LFS instalado antes de clonar:

```bash
git lfs install
git clone <url-del-repositorio>
```

### Flujo de integración de assets de arte

1. El artista exporta el FBX/PNG y lo coloca en la subcarpeta correcta de `Assets/`.
2. Abre Unity — la importación automática asigna configuración por defecto.
3. Ajusta la configuración de importación en el Inspector (compresión de textura, rig del modelo, clips de animación).
4. Crea el material HDRP y asigna las texturas.
5. Hace commit del FBX, PNG, `.meta` files y materiales en una rama `art/xxx`.
6. Abre un Pull Request hacia `develop` para que un programador valide las referencias y los meta files.

### Revisión de código

Antes de mergear una rama `feature/`:

- [ ] No hay errores de compilación en la Consola de Unity.
- [ ] El NavMesh está regenerado si se modificó la geometría.
- [ ] Los prefabs nuevos tienen los componentes mínimos requeridos (ver sección NPCs).
- [ ] Los `.meta` files de todos los assets nuevos están incluidos en el commit.
- [ ] Los nombres de GameObjects, scripts y variables están en inglés o castellano de forma coherente con el resto del proyecto.
- [ ] Se ha probado en Play Mode sin errores ni warnings críticos en la Consola.
