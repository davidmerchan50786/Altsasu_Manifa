// Assets/Scripts/Core/MarcadorTerrenoAltsasua.cs
// ═══════════════════════════════════════════════════════════════════════════
//  MARCADOR DEL SUELO JUGABLE OFICIAL
//
//  ServicioTerreno (Systems) lo añade al generar/adoptar el suelo; su presencia
//  valida el terreno en arranques posteriores y permite reconocer un mosaico
//  bakeado en escena. En CORE porque lo leen tanto Systems (ServicioTerreno,
//  CargadorMosaicoTerreno, StreamerColliderTerreno) como Runtime
//  (SistemaTerreno, SistemaDiagnostico) — y Runtime no referencia Systems.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

/// <summary>
/// Marca el suelo jugable oficial de Alsasua. ServicioTerreno lo añade al
/// generar/adoptar; su presencia valida el terreno en arranques posteriores
/// (también sirve para terrenos guardados en escena desde el editor).
/// </summary>
public class MarcadorTerrenoAltsasua : MonoBehaviour
{
    [Tooltip("Proveedor que creó este suelo.")]
    public FuenteTerreno fuente = FuenteTerreno.Ninguna;

    [Tooltip("Solo mosaico V2: anillo del tile (0 urbano, 1 valle, 2 sierras).")]
    public int anillo = -1;

    [Tooltip("Solo mosaico V2: índices fila/columna del tile dentro de su anillo.")]
    public int fila = -1;
    public int columna = -1;
}
