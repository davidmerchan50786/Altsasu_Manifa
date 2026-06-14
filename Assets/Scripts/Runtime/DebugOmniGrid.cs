// Assets/Scripts/Runtime/DebugOmniGrid.cs
// ═══════════════════════════════════════════════════════════════════════════
//  DEBUG OMNI-GRID — overlay para VALIDAR el grid en Play (capa RUNTIME)
//
//  Arrástralo a un GameObject vacío y dale Play. Muestra en pantalla:
//    · estado del grid (Listo) y nº total de entidades publicadas
//    · ghosts vivos en PoolGhosts (Sim-LOD Nivel 2)
//    · conteos por TIPO en un radio alrededor de la cámara (QueryRadioContar)
//  Sirve para confirmar que productores (NPCBase/PublicadorGrid/PoolGhosts) y
//  consumidores funcionan end-to-end. No toca nada: solo lee.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

[AddComponentMenu("Alsasua/Espacial/Debug Omni-Grid")]
public sealed class DebugOmniGrid : MonoBehaviour
{
    [Tooltip("Radio (m) alrededor de la cámara para los conteos por tipo.")]
    [SerializeField] float radioConteo = 100f;
    [Tooltip("Mostrar el panel.")]
    [SerializeField] bool mostrar = true;

    GUIStyle _estilo;

    void OnGUI()
    {
        if (!mostrar) return;

        var grid = ServiceLocator.Get<IUnifiedSpatialGrid>();
        var pool = ServiceLocator.Get<IPoolGhosts>();

        _estilo ??= new GUIStyle(GUI.skin.box)
        {
            fontSize = 12,
            alignment = TextAnchor.UpperLeft,
            padding = new RectOffset(8, 8, 6, 6)
        };

        string txt;
        if (grid == null)
        {
            txt = "OMNI-GRID\nServicio NO registrado.";
        }
        else
        {
            Vector3 c = Camera.main != null ? Camera.main.transform.position : transform.position;
            int man = grid.Listo ? grid.QueryRadioContar(c, radioConteo, TipoEspacial.Manifestante) : 0;
            int pol = grid.Listo ? grid.QueryRadioContar(c, radioConteo, TipoEspacial.Policia)      : 0;
            int veh = grid.Listo ? grid.QueryRadioContar(c, radioConteo, TipoEspacial.Vehiculo)     : 0;
            int jug = grid.Listo ? grid.QueryRadioContar(c, radioConteo, TipoEspacial.Jugador)      : 0;

            txt =
                $"OMNI-GRID   ·   Listo: {(grid.Listo ? "sí" : "no")}\n" +
                $"Entidades en el grid: {grid.Conteo}\n" +
                $"Ghosts vivos (PoolGhosts): {(pool != null ? pool.Vivos : 0)}\n" +
                $"── en {radioConteo:F0} m de la cámara ──\n" +
                $"Manifestantes: {man}   Policías: {pol}\n" +
                $"Vehículos: {veh}   Jugador: {jug}";
        }

        GUI.Box(new Rect(10, 10, 320, 122), txt, _estilo);
    }
}
