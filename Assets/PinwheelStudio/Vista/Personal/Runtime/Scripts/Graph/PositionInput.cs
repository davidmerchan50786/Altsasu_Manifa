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
    /// Binds a named point-set input to a <see cref="PositionContainer"/> for graph execution.
    /// </summary>
    /// <remarks>
    /// A <see cref="LocalProceduralBiome"/> stores an array of these bindings. During graph setup,
    /// <see cref="LPBInputProvider"/> uploads the referenced positions to a compute buffer and exposes
    /// that buffer under <see cref="name"/> so input nodes can consume it.
    /// </remarks>
    public class PositionInput
    {
        [SerializeField]
        private string m_name;
        /// <summary>
        /// External input name used by graph nodes to request this point set.
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
        private PositionContainer m_positionContainer;
        /// <summary>
        /// Asset that stores the <see cref="PositionSample"/> array to upload for this input.
        /// </summary>
        public PositionContainer positionContainer
        {
            get
            {
                return m_positionContainer;
            }
            set
            {
                m_positionContainer = value;
            }
        }
    }
}
#endif


