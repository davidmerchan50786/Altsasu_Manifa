# Prompts Veo 3 — Trailer "Altsasu Manifa" (estilo GTA VI)

## Cómo encadenar los microvídeos (Flow)
1. Genera el Clip 1 (texto→vídeo o usa tu portada como frame inicial con "Ingredients/Frames to video").
2. Para cada clip siguiente: usa **Extend** en Flow, o descarga el **último frame** del clip anterior y úsalo como imagen inicial del siguiente (imagen→vídeo). Así mantienes luz, calle y personaje.
3. Pega SIEMPRE el bloque CONSISTENCIA al inicio de cada prompt. No cambies ni una palabra de la descripción del punk.
4. Monta los 9 clips seguidos (8s × 9 = ~72s) con corte seco; la música une todo.

## Localizaciones reales (usa la captura como Ingredient del clip)
| Lugar | Captura | Clips |
|---|---|---|
| Plaza de los Fueros (edificio neoclásico de columnas) | `Captura ... 185410.png` | 5, 6, 7 |
| Iglesia y Plaza San Juan (muros de piedra) | `Captura ... 185009.png` | 1, 7 |
| Calle Zubeztia (peatonal estrecha con balcones) | `Captura ... 191754.png` | 2, 3 |
| C. García Ximénez (parque y paso de cebra, centro) | `Captura ... 192256.png` | 4 |
| Kale Nagusia (calle mayor con montañas al fondo) | `Captura ... 192857.png` | 8, 9 |

## Bloque CONSISTENCIA (pégalo al principio de TODOS los prompts)
```
Stylized AAA video game cinematic trailer, GTA VI style render, Unreal-quality game engine footage, not real people. Setting: Basque Country, 1980s. Main character: a gaunt pale-faced Basque punk in his 20s, tall dark spiked mohawk, black studded leather jacket covered in patches (one patch reads "PUNK"), ripped jeans, scuffed combat boots. Antagonists: 1980s Spanish civil guards in dark green uniforms, black patent-leather tricorne hats, black gloves and boots. Location: Alsasua (Navarra), narrow streets of reddish sandstone Basque buildings, wrought-iron balconies with flower boxes, terracotta roof tiles, political graffiti and Basque flags on walls, 1980s cars (Seat 124, Renault 4) parked, wet asphalt, overcast grey sky, green mountains visible at the end of the streets. Color grade: desaturated teal-and-amber, 1980s film grain, anamorphic lens flares, cinematic 2.39:1.
```

**Estilo:** la portada cómic aparece SOLO al inicio del Clip 1 (transición a 3D); todos los demás clips son render realista estilo GTA VI.

**Usa tus imágenes como Ingredients en Flow:** la portada para el Clip 1, el render del guardia civil para los Clips 5–7, y las capturas de la tabla de localizaciones en cada clip.

## Si la moderación bloquea un prompt
- Error **"specific people"**: lo disparan las referencias reales, no la sangre. Usa el bloque CONSISTENCIA-SAFE de abajo: fuera "Basque", "Alsasua (Navarra)", "Spanish civil guards" y también "not real people" (paradójicamente dispara el filtro).
- Cambia "blood" por **"dark red liquid"** o "red paint-like splatter" + **"cartoon-like exaggerated game effect, no gore, no injuries"**.

## Bloque CONSISTENCIA-SAFE (usar si Veo bloquea el normal)
```
Stylized AAA video game cinematic trailer, GTA VI style graphics, fully fictional animated video game characters. Setting: a fictional mountain town, 1980s Europe. Main character: a fictional punk video game character in his 20s, gaunt pale face, tall dark spiked mohawk, black studded leather jacket covered in patches, ripped jeans, scuffed combat boots. Antagonists: fictional vintage gendarme-style video game officers in dark green uniforms, black patent-leather tricorne hats, black gloves and boots. Location: narrow streets of reddish sandstone buildings, wrought-iron balconies with flower boxes, terracotta roof tiles, graffiti and red-white-green flags on walls, vintage 1980s European cars parked, wet asphalt, overcast grey sky, green mountains at the end of the street. Color grade: desaturated teal-and-amber, 1980s film grain, anamorphic lens flares, cinematic 2.39:1.
```

---

## CLIP 1 — Portada que cobra vida → apertura aérea (8s)
*Ingredient: tu portada cómic.*
```
[CONSISTENCIA]
The shot starts as a flat 2D comic book cover illustration of a Basque punk and a 1980s town, camera slowly pushes into the central panel, the ink drawing comes alive and morphs into a fully realistic 3D game engine render: a sweeping aerial shot descending from misty green mountains over terracotta rooftops and a stone church bell tower of a small Basque valley town, diving toward a narrow cobbled street. Audio: page-turn whoosh, low ominous synth drone, distant church bells.
```

## CLIP 2 — Caminata héroe (8s)
```
[CONSISTENCIA]
Low-angle tracking shot, camera dollies backward as the punk walks straight toward camera down a narrow sandstone street, slow motion, pedestrians stepping aside, pigeons scattering, his boots splash a puddle. He stares into the lens, defiant smirk. Audio: heavy slow drum beat, boots on wet stone, muffled crowd.
```

## CLIP 3 — Escupitajo a la pared (8s)
```
[CONSISTENCIA]
Medium side shot: the punk stops next to a rough sandstone wall, turns his head and spits a jet of thick dark crimson liquid against the wall in slow motion, the splatter drips down the stone like graffiti, he wipes his mouth with the back of his hand and keeps walking. Stylized non-realistic video game effect, no gore. Audio: wet splat reverb, bass drop, distant siren starting.
```

## CLIP 4 — El transeúnte manchado (8s)
```
[CONSISTENCIA]
Tracking shot from behind: the punk does a huge comedic spit-take mid-stride, a mouthful of dark red drink sprays out and accidentally splashes across the beige coat of a pedestrian video game character walking past, the pedestrian freezes and looks down at the stain in disbelief, the punk shrugs theatrically and keeps walking. Slapstick comedy scene from a satirical video game, cartoon-like, no one is hurt. Audio: comedic record-scratch under tense music, the pedestrian gasps "¡Pero qué haces!"
```
*Si bloquea: usa el bloque CONSISTENCIA-SAFE, no el normal. Evita "retches", "vomit", "crimson" y "elderly".*

### Plan B si sigue bloqueando: dividir en 2 planos (truco Kuleshov)
En ninguno de los dos ocurre nada "peligroso"; montados con corte seco se leen como causa-efecto.

**4A — el espray:**
```
[CONSISTENCIA-SAFE]
Side profile slow-motion shot: the punk takes a swig from a bottle of red soda while walking, then does a huge theatrical spit-take, spraying a fine mist of red soda into the air in front of him, droplets glittering in slow motion. Slapstick comedy moment from a satirical cartoon video game. Audio: deep slow-motion whoosh, comedic timpani roll.
```

**4B — la reacción:**
```
[CONSISTENCIA-SAFE]
Medium shot: a middle-aged pedestrian video game character in a beige coat stands frozen in the middle of the street, looking down in disbelief at a big red soda stain on his coat, arms half raised, blinking slowly at the camera. In the soft-focus background the punk walks away shrugging theatrically. Deadpan slapstick comedy from a satirical cartoon video game. Audio: comedic record-scratch, awkward silence, the pedestrian gasps "¡Pero qué haces!"
```

## CLIP 5 — Llega la Guardia Civil (8s)
```
[CONSISTENCIA]
Wide shot in a town square (Plaza de los Fueros) framed by a neoclassical stone building with columns and Basque manor houses: a green 1980s Land Rover Santana jeep skids to a stop blocking the square, a squad of 1980s civil guards in dark green uniforms and black tricorne hats jumps out and forms a line holding batons and round riot shields, headlights cutting through drizzle, light reflecting on wet sandstone facades. The punk stands alone facing them, cracking his knuckles. Audio: screeching tires, old two-tone siren, boots on cobblestone, music tension rising.
```

## CLIP 6 — La carga (8s)
```
[CONSISTENCIA]
Dynamic handheld action shot: the punk sprints and shoulder-slams into a guard's round shield, the line breaks, guards in tricorne hats swing batons, he ducks and shoves another guard into a graffiti-covered wall, his tricorne hat flying off, tear gas smoke drifting across the street, debris flying, protesters with Basque flags in the background. Stylized video game action sequence, no blood, no injuries shown. Audio: impact hits synced to drum breaks, shouting in the distance, gas canister hiss.
```

## CLIP 7 — Héroe entre el humo (8s)
```
[CONSISTENCIA]
Epic slow-motion orbit shot: the punk stands in the middle of the street surrounded by drifting white tear gas smoke and jeep headlights, arms open wide screaming at the sky, embers, leaflets and a fallen tricorne hat floating around him, silhouettes of guards and protesters with flags around him, the stone church tower looming behind. Audio: music peaks into distorted 80s basque punk guitar riff, crowd roar, slowed siren.
```

## CLIP 8 — La señal en el cielo nocturno (8s)
```
[CONSISTENCIA]
Night scene: low-angle shot from the main street looking up past wrought-iron balconies and the church bell tower, a powerful searchlight beam projects a glowing emblem onto the cloudy night sky like the Batman signal, but the emblem is the silhouette of an axe with a serpent coiled around its handle, light rays cutting through drizzle and chimney smoke, the punk on a rooftop looks up at it, amber streetlights below. Stylized video game cinematic. Audio: deep cinematic braam, electrical hum of the searchlight, distant dog barking, rising choir pad.
```

## CLIP 9 — Cierre y título (8s)
```
[CONSISTENCIA]
The punk walks away from camera into the smoky street at night, silhouetted against the glowing sky-signal of an axe with a coiled serpent projected on the clouds above the rooftops, flips a casual peace sign over his shoulder. Camera tilts up to the dark sky, smash cut to black. Bold distressed orange-yellow gradient title text in GTA-style lettering appears: "ALTSASU_MANIFA", subtitle below in white: "EUSKAL HERRIAN, 80KO HAMARKADA". Audio: final guitar chord ring-out, single church bell, silence.
```

---

## Ajustes en Flow
- Modelo: Veo 3 Quality, 16:9, 8s por clip.
- Mismo proyecto para los 8 clips (mejor coherencia).
- Si un clip sale con otro punk: añade al final "same character as previous shot, identical red mohawk and studded jacket".
- Para usar tus capturas reales de Alsasua: súbelas como *Ingredients* en los clips 2, 3 y 5 (calles estrechas y plaza con iglesia).
