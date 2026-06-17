// Assets/Scripts/Core/MundoConfig.cs
// ═══════════════════════════════════════════════════════════════════════════
//  CONFIG DEL MUNDO — fuente única de verdad para QUÉ se genera en runtime
//
//  Decisión del proyecto (jun 2026): los EDIFICIOS y las CALLES pasan a ser de
//  ASSET (prefabs colocados en el editor: Edificios_Asset / Calles_Asset), NO
//  procedurales. Estos flags permiten a los generadores procedurales auto-saltarse
//  su construcción sin borrar su código (reversible, sin espagueti). El terreno,
//  los árboles y el resto del mundo NO se tocan.
//
//  Capa CORE: sin dependencias. Lo leen los generadores (Runtime) en su entrada.
// ═══════════════════════════════════════════════════════════════════════════

public static class MundoConfig
{
    /// <summary>true = edificios procedurales (legacy); false = SOLO edificios de asset
    /// (prefabs en la raíz "Edificios_Asset", colocados con ConstructorCiudadAssets).</summary>
    public static bool EdificiosProcedurales = false;

    /// <summary>true = calles procedurales (legacy); false = malla de asfalto de asset
    /// (raíz "Calles_Asset", construida con ConstructorCallesAssets).</summary>
    public static bool CallesProcedurales = false;
}
