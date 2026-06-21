# Paranoia → Guardia Civil (staging)

Carpeta `~` → **Unity no la compila**. Scaffold del sistema "a paranoia alta, algunos NPC se
vuelven Guardia Civil y algunos coches, patrulla". Diseño completo en
`Docs/Narrativa/MECANICA_Paranoia_GuardiaCivil.md`.

## Qué hay
- `ParanoiaGCConfig.cs` (SO): materiales (uniforme/librea), umbrales (70/90), MAX, ritmo, curva.
- `ConvertibleGuardiaCivil.cs`: va en cada NPC/coche convertible. Cachea original, `Convertir()`/
  `Revertir()` con swap de material + hijos (tricornio/rotativo) + cerebro.
- `SistemaParanoiaGuardiaCivil.cs`: manager. Se suscribe a `SistemaApoyoPopular.OnParanoiaCambia`,
  convierte/revierte **gradual** y **off-screen**.

## Activar
1. Mueve los 3 `.cs` a `Assets/Scripts/Runtime/` (o `Systems/`).
2. Crea un `ParanoiaGCConfig` y asígnale el material de uniforme GC y el de librea de patrulla.
3. A los NPC/coche convertibles, añádeles `ConvertibleGuardiaCivil` (marca `esCoche` en los
   coches) y, opcional, un hijo desactivado llamado `Tricornio` / `Rotativo`. *(O haz que el
   spawner se los ponga al entrar en streaming.)*
4. Pon `SistemaParanoiaGuardiaCivil` en la escena con el config.
5. Sube la paranoia: el wanted alto la sube sola (`SistemaApoyoPopular`: `if (nivelWanted>=3)
   SumarParanoia(...)`). A partir de 70 empiezan a aparecer tricornios; a 90, oleada.

## Puntos de integración (marcados //★ en el código)
- **Cerebro policía**: `ConvertibleGuardiaCivil.ActivarCerebroPolicia()` busca `CerebroGOAPPolicia`
  por nombre y lo añade/activa. Verifica que ese cerebro se autoinicializa (Context/Metas) o
  pásale lo que necesite. Si no, deja `swapCerebroPolicia=false` y solo cambia el skin.
- **Facción/tag**: en `Convertir()` ajusta el tag (`"Policia"` u otro) para que tu
  `SistemaDeteccionIA`/wanted lo trate como autoridad.
- **Cerebro civil**: asigna `cerebroCivil` en el inspector, o deja que lo autodetecte (busca un
  MonoBehaviour con "NPC" en el nombre).

## Por qué así
- **Convierte, no spawnea**: reutiliza NPCs/coches ya activos → barato, sin pop-in, reversible.
- **Gradual + off-screen**: el morph nunca se ve; la sensación es "el pueblo se va militarizando".
- Compatible con los directores de presupuesto: baja `maxNpc/maxCoches` bajo presión de GPU.
- Encaje narrativo: en M07 (redada) y M11 (San Juan) la paranoia se dispara y los vecinos se
  vuelven tricornios. Apoyo alto puede frenarlo; apoyo bajo, acelerarlo.
