// Assets/Scripts/Runtime/DenylistUtility.cs
// Fuente única de la denylist de objetos que NO son geometría estática de mundo.
// Usada por HorneadorCiudad, CargadorCiudadHorneada y DesactivadorMasivo.
// Mantener aquí para que un solo cambio se propague a los tres sistemas.

using UnityEngine;

public static class DenylistUtility
{
    public static readonly string[] DENY = {
        "terrain", "terreno", "mosaico",
        "arbol", "arboles", "árbol", "vegetacion", "vegetación", "tree", "grass", "hierba",
        "agua", "water", "river", "río", "charco",
        "player", "jugador", "npc", "civil", "peaton", "peatón",
        "manifestante", "multitud", "crowd",
        "vehiculo", "vehículo", "coche",
        "camera", "cámara", "light", "luz", "sun", "sol",
        "particle", "particula", "partícula", "vfx",
        "canvas", "eventsystem", "hud",
        "cesium", "georeference",
        "ciudadhorneada",
    };

    /// <summary>True si <paramref name="t"/> o cualquiera de sus padres está en la denylist.</summary>
    public static bool EnDenylist(Transform t)
    {
        for (var cur = t; cur != null; cur = cur.parent)
        {
            var n = cur.name.ToLowerInvariant();
            foreach (var d in DENY)
                if (n.Contains(d)) return true;
        }
        return false;
    }
}
