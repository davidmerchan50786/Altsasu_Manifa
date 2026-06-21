// Assets/Scripts/Runtime/GestorEstadoCiudad.cs
// ═══════════════════════════════════════════════════════════════════════════
//  GESTOR ESTADO CIUDAD — conecta el bake de ciudad con PersistenceManager
//
//  PROBLEMA QUE RESUELVE:
//    El PersistenceManager guarda deltas indexados por id de objeto. Si se
//    rehornea la ciudad (🏗️ Hornear Ciudad), los ids de los GameObjects
//    cambian → los deltas guardados apuntan a objetos inexistentes → el juego
//    intentará aplicar cambios (destruir/mover) a objetos que no existen.
//
//  SOLUCIÓN:
//    Escribe una "firma de bake" en el archivo de persistencia (totalCeldas +
//    drawCallsAprox como huella ligera). Al cargar, compara la firma actual
//    con la guardada. Si difieren → la ciudad fue rehorneada → los deltas
//    son inválidos → los descarta y emite un log de advertencia.
//
//  FLUJO:
//    BeforeSceneLoad (orden -180 antes que PersistenceManager):
//      1. Carga ManifestCiudadSO (firma actual del bake)
//      2. Lee el archivo "city_bake_signature.json"
//      3. Si la firma cambió: llama IPersistenceService.Limpiar() para
//         evitar que se apliquen deltas obsoletos
//      4. Escribe la firma nueva al persistentDataPath
//
//  La firma es una tupla (totalCeldas, drawCallsAprox) — ligera y suficiente
//  para detectar un rehornado sin calcular un hash completo de la geometría.
// ═══════════════════════════════════════════════════════════════════════════

using System.IO;
using UnityEngine;

public static class GestorEstadoCiudad
{
    const string SO_RESOURCES  = "CiudadHorneada/ManifestCiudadSO";
    const string FIRMA_ARCHIVO = "city_bake_signature.json";

    [System.Serializable]
    struct Firma { public int totalCeldas; public int drawCallsAprox; }

    static string RutaFirma => Path.Combine(Application.persistentDataPath, FIRMA_ARCHIVO);

    // Orden -150: después de PersistenceManager (BeforeSceneLoad, sin orden) pero
    // antes que CargadorCiudadHorneada (-180) para que la limpieza ocurra primero.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void VerificarFirmaBake()
    {
        var so = Resources.Load<ManifestCiudadSO>(SO_RESOURCES);
        if (so == null) return;   // ciudad no horneada → nada que verificar

        var firmaActual = new Firma
        {
            totalCeldas   = so.totalCeldas,
            drawCallsAprox = so.drawCallsAprox,
        };

        // Leer firma guardada
        if (File.Exists(RutaFirma))
        {
            Firma firmaGuardada;
            try { firmaGuardada = JsonUtility.FromJson<Firma>(File.ReadAllText(RutaFirma)); }
            catch { firmaGuardada = default; }

            bool cambio = firmaGuardada.totalCeldas    != firmaActual.totalCeldas ||
                          firmaGuardada.drawCallsAprox != firmaActual.drawCallsAprox;

            if (cambio)
            {
                Debug.LogWarning("[EstadoCiudad] La ciudad fue rehorneada desde el último guardado " +
                    $"({firmaGuardada.totalCeldas}→{firmaActual.totalCeldas} celdas). " +
                    "Los deltas de persistencia son inválidos — se descartan.");

                // Limpiar deltas obsoletos vía IPersistenceService
                var persistencia = ServiceLocator.Get<IPersistenceService>();
                if (persistencia != null)
                    persistencia.Limpiar();
                else
                    Debug.LogWarning("[EstadoCiudad] IPersistenceService no registrado aún — " +
                        "asegúrate de que PersistenceManager se registra antes.");
            }
        }

        // Escribir firma actual (para la próxima sesión)
        try { File.WriteAllText(RutaFirma, JsonUtility.ToJson(firmaActual)); }
        catch (System.Exception ex)
        { Debug.LogWarning($"[EstadoCiudad] No se pudo escribir firma: {ex.Message}"); }
    }

    /// <summary>Fuerza la invalidación de deltas en la próxima sesión
    /// (llamar tras un rebake manual desde el editor).</summary>
    public static void InvalidarFirmaGuardada()
    {
        try { if (File.Exists(RutaFirma)) File.Delete(RutaFirma); }
        catch { /* ignorar */ }
        Debug.Log("[EstadoCiudad] Firma invalidada — los deltas se limpiarán en el próximo Play.");
    }
}
