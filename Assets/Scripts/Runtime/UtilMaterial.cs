// Assets/Scripts/Runtime/UtilMaterial.cs
// Tinte de color seguro para HDRP/Lit (usa _BaseColor) con fallback a .color.
using UnityEngine;

public static class UtilMaterial
{
    public static void Tenir(Renderer r, Color c)
    {
        if (r == null) return;
        var m = r.material;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        else m.color = c;
    }
}
