# Impostores billboard — staging (fase 1)

Esta carpeta termina en `~` → **Unity NO la compila**. Es código real listo para
activar, pero aislado para que un fallo no te meta en Modo Seguro mientras no lo
pruebes en el editor. Diseño completo en `Docs/ADR_001_AAA_impostores_clipmapV3.md`.

## Qué hay (fase 1, hecho)
- `ImpostorAtlasSO.cs` — datos del atlas: textura, nº de vistas yaw, y por edificio
  (id OSM, UV de su tira, tamaño del quad, pivote en mundo).
- `BakeadorImpostores.cs` — Editor: hornea N vistas yaw de cada edificio
  seleccionado a un atlas de albedo y crea el `ImpostorAtlasSO`.

## Cómo ACTIVAR
1. Mueve `ImpostorAtlasSO.cs` → `Assets/Scripts/Runtime/Impostores/` (capa Runtime).
2. Mueve `BakeadorImpostores.cs` → `Assets/Scripts/Editor/` (capa Editor).
3. Deja que Unity compile. Si hay error, está aislado a estos 2 ficheros.
4. Selecciona 5-10 edificios en la jerarquía →
   **Tools ▸ Alsasua ▸ Impostores ▸ 🔆 Bake atlas (selección)**.
5. Revisa `Assets/AlsasuaData/impostores_v1/atlas_albedo.png` y el `.asset`.

## Caveat HDRP (importante)
El clear transparente de `Camera.Render()` puede salir **opaco** en HDRP. Si el
alfa del atlas no es transparente: hornea sobre un fondo croma y recórtalo, o usa
una cámara HDRP dedicada / ShaderGraph Unlit para el preview. Es la razón por la
que esto va como DRAFT staged y no directo a `Assets`.

## Hecho (fase 2)
- `ImpostorBillboard.cs` (Runtime): quad orientado a cámara que elige en CPU la
  vista yaw del atlas según el ángulo cámara→edificio y la fija por
  MaterialPropertyBlock.
- `ImpostorUnlit.shader`: unlit de referencia que muestrea la celda `_UvCell`.
  ⚠ Recrear como ShaderGraph **HDRP/Unlit** para producción (en CG sale magenta en HDRP).

### Hook en StreamerMundoEstatico (cuando actives)
En la clasificación por bandas, sustituye el "impostor-lite" de la banda media por:
```
// al ENTRAR en [RadioActivacion, RadioImpostor]:
meshRenderer.enabled = false;                       // apaga la geometría real
_impostor = ImpostorBillboard.Crear(atlasSO, idOSM, transform);
// al VOLVER a 'Activo':
if (_impostor) { Destroy(_impostor.gameObject); _impostor = null; }
meshRenderer.enabled = true;
```

## Pendiente (fase 3-4, en el ADR)
- **Batching BRG**: agrupar todos los impostores de un atlas en 1 draw call
  (`BatchRendererGroup` / `Graphics.RenderMeshInstanced` con UVs por instancia),
  en vez de un MeshRenderer por edificio. Es el gran ahorro de draw calls.
- Normales + profundidad (parallax) y sombra fake en el atlas.
- Mezcla por dither 0.25 s en la transición Activo↔Impostor (anti-pop).

## Validación
Tras integrar, el gate `Tools ▸ Alsasua ▸ Calidad ▸ ✅ Validar georreferenciación`
debe seguir en verde, y comprobar visualmente que no hay *pop* Activo↔Impostor
(mezcla por dither 0.25 s + histéresis del JobBandasMundo).
