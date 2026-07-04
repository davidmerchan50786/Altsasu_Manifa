# Sintonía Altsasu — panel único de tuning del calor (staging)

Carpeta `~` → **Unity no la compila**. Un solo ScriptableObject para balancear todo
el bucle "calor y alivio" desde un sitio, en vez de tener 70/90/3… repartidos por
cinco scripts.

## Qué centraliza
- **Paranoia → Guardia Civil**: umbrales 70/90, máx convertidos, freno por apoyo.
- **Controles de carretera**: umbral de activación, máx controles, umbral de arresto,
  prob. de colarte por apoyo.
- **Testigos**: rango, retardo, prob. de chivarse por apoyo y gravedad.
- **Coartada**: ritmo de enfriamiento y bonus por apoyo.

Incluye los helpers ya hechos (`FactorApoyo`, `ConvertidosObjetivo`, `ControlesObjetivo`,
`ControlProbPasar`, `TestigoProbChivar`, `CoartadaRitmo`) con la misma matemática que
hoy tienen los managers, para que la migración sea sustituir un cálculo por una llamada.

## Cómo conectarlo (cero regresión)
1. Mueve `SintoniaAltsasu.cs` a `Assets/Scripts/Core/` o `Runtime/` (lo leen varios
   sistemas; Core es lo más seguro).
2. En cada manager (`SistemaParanoiaGuardiaCivil`, `SistemaControlesGC`,
   `SistemaTestigos`, `SistemaCoartada`) añade:
   ```
   public SintoniaAltsasu sintonia;
   ```
   y donde haya un umbral/cálculo, léelo de `sintonia` si no es null:
   ```
   float umbral = sintonia ? sintonia.controlesUmbralActivacion : umbralActivacion;
   int objetivo = sintonia ? sintonia.ControlesObjetivo(paranoia) : /* cálculo local */;
   ```
3. `Assets ▸ Create ▸ Alsasua ▸ Sintonía (calor)` → asigna el asset a los managers.

Mientras `sintonia` sea null, cada sistema usa sus defaults serializados → el
comportamiento no cambia hasta que tú lo enchufes.

## Por qué un solo asset
El balance de un sandbox vive de iterar estos números rápido y de forma coherente:
si subes el apoyo como "comodín", quieres ver a la vez menos tricornios, más gente
que te cuela y menos chivatos. Tenerlos juntos evita que se desincronicen.
