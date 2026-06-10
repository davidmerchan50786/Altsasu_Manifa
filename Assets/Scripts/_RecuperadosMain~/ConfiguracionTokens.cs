using UnityEngine;

/// <summary>
/// ScriptableObject que almacena los tokens de acceso del proyecto.
/// Se guarda en Assets/Resources/ConfiguracionTokens.asset
///
/// ⚠️ IMPORTANTE: No subas este archivo a GitHub/repositorios públicos.
/// Añade "ConfiguracionTokens.asset" a tu .gitignore.
/// </summary>
[CreateAssetMenu(fileName = "ConfiguracionTokens", menuName = "Alsasua/Configuración de Tokens")]
public class ConfiguracionTokens : ScriptableObject
{
    [Header("Google Maps Platform")]
    [Tooltip("API Key de Google Maps Platform — Map Tiles API")]
    // SEGURIDAD: NO hardcodear la API Key aquí. Introdúcela en el Inspector y
    // asegúrate de que ConfiguracionTokens.asset está en el .gitignore para no
    // subir credenciales a repositorios públicos.
    public string apiKeyGoogle = "";

    [Header("Cesium Ion")]
    [Tooltip("Token de Cesium Ion — https://cesium.com/ion/tokens")]
    public string tokenCesiumIon = "";
}
