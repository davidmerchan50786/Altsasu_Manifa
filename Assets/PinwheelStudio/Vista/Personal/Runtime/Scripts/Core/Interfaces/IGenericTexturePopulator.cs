#if VISTA
using System.Collections.Generic;
using UnityEngine;

namespace Pinwheel.Vista
{
    /// <summary>
    /// Defines a contract for receiving generic texture outputs generated for a tile.
    /// </summary>
    public interface IGenericTexturePopulator
    {
        /// <summary>
        /// Applies generic texture outputs to the implementation.
        /// </summary>
        /// <param name="labels">Labels paired with <paramref name="textures"/> by index.</param>
        /// <param name="textures">Generic texture outputs.</param>
        void PopulateGenericTextures(List<string> labels, List<RenderTexture> textures);
    }
}
#endif


