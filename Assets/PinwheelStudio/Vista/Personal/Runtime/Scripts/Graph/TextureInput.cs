#if VISTA
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Pinwheel.Vista;
using Pinwheel.Vista.Graphics;

namespace Pinwheel.Vista.Graph
{
    [System.Serializable]
    /// <summary>
    /// Binds a named texture input for graph execution.
    /// </summary>
    /// <remarks>
    /// A <see cref="LocalProceduralBiome"/> stores an array of these bindings. During graph setup,
    /// <see cref="LPBInputProvider"/> converts the referenced <see cref="Texture2D"/> into a temporary
    /// <see cref="RenderTexture"/> and exposes it under <see cref="name"/> so graph input nodes can
    /// sample it.
    /// </remarks>
    public class TextureInput
    {
        [SerializeField]
        private string m_name;
        /// <summary>
        /// External input name used by graph nodes to request this texture.
        /// </summary>
        public string name
        {
            get
            {
                return m_name;
            }
            set
            {
                m_name = value;
            }
        }

        [SerializeField]
        private Texture2D m_texture;
        /// <summary>
        /// Source texture asset that will be uploaded into a temporary render texture for execution.
        /// </summary>
        public Texture2D texture
        {
            get
            {
                return m_texture;
            }
            set
            {
                m_texture = value;
            }
        }
    }
}
#endif


