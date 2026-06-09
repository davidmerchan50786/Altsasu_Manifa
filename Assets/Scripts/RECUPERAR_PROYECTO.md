# RECUPERAR PROYECTO — Altsasu_Manifa

> Ejecutar en orden desde la raíz del repo:
> `E:\Desk\DAM\Altsasu_Manifa`

---

## 1. Restaurar todos los ficheros borrados del working tree

```powershell
cd E:\Desk\DAM\Altsasu_Manifa
git checkout HEAD -- $(git ls-files -d)
```

Esto restaura del HEAD todo lo que está borrado en disco (AlsasuaData, ProjectSettings, Packages, escenas, assets, etc.) **sin tocar los scripts modificados/nuevos**.

Si `$(git ls-files -d)` no funciona en PowerShell, usa la alternativa:

```powershell
git ls-files -d | ForEach-Object { git checkout HEAD -- $_ }
```

---

## 2. Bajar ficheros LFS (ortofoto, DEM/LIDAR, modelos pesados)

```powershell
git lfs pull
```

---

## 3. Verificar que los scripts nuevos siguen presentes

```powershell
git status Assets/Scripts
```

Los scripts nuevos (untracked) deben aparecer en rojo como `Untracked files`. Los modificados en amarillo como `modified`. Ninguno debe haber sido borrado por el checkout.

---

## 4. Abrir Unity y comprobar compilación

- Abrir `E:\Desk\DAM\Altsasu_Manifa` en Unity Hub
- Esperar compilación completa
- Verificar que el mundo carga (AlsasuaData presente → generación de edificios/calles/árboles funcional)

---

## 5. Commit selectivo de los cambios de esta sesión

```powershell
git add Assets/Scripts
git commit -m "Fix compilacion (Core/Jobs/metas/duplicados) + sistemas AAA+++ (quality tier, musica adaptativa, camara cinetica, humanoide+FootIK, APV/Water/Occlusion guarded, DirectorMundo)"
git push
```

---

## Qué contienen los scripts de esta sesión

| Fichero | Descripción |
|---|---|
| `SistemaCalidadGrafica.cs` | Quality tier automático (GPU benchmark) |
| `SistemaMusicaAdaptativa.cs` | Música procedural por zona/tensión/wanted |
| `SistemaCamaraCinetica.cs` | Cámara con cinética, shake, trauma, FOV dinámico |
| `SistemaAPV.cs` | Adaptive Probe Volumes guarded |
| `SistemaWater.cs` | Water System HDRP guarded |
| `SistemaOcclusion.cs` | GPU Occlusion Culling guarded |
| `SistemaFootIK.cs` | Foot IK (humanoid Animator, auto-noop si procedural) |
| `DirectorMundo.cs` | AI Director de eventos dinámicos (estilo L4D) |
| `AAA_PLUS_BLUEPRINT.md` | Blueprint completo del proyecto |
| `GRAFICOS_PIPELINE.md` | Pipeline gráfico HDRP documentado |
| `RECUPERAR_PROYECTO.md` | Este fichero |

---

## Por qué NO hacer `git add -A` antes de restaurar

`git add -A` ahora registraría el borrado de ~12.000 ficheros (AlsasuaData, ProjectSettings, Packages, escenas, assets) como commit permanente. El repo quedaría con solo scripts. Siempre restaurar primero con los pasos 1-2, luego commit selectivo con `git add Assets/Scripts`.
