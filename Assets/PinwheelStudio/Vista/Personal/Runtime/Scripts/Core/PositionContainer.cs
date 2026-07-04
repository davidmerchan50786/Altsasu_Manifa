#if VISTA
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace Pinwheel.Vista
{
    [CreateAssetMenu(menuName = "Vista/Position Container")]
    /// <summary>
    /// Stores a serialized collection of <see cref="PositionSample"/> values that can be injected into a terrain graph.
    /// </summary>
    /// <remarks>
    /// This asset is typically referenced by a <see cref="Graph.PositionInput"/> on a
    /// <see cref="LocalProceduralBiome"/>. During graph setup, <see cref="Graph.LPBInputProvider"/> uploads the stored
    /// samples directly into a temporary graph buffer so nodes can consume authored point data without rebuilding it at
    /// runtime.
    /// </remarks>
    public class PositionContainer : ScriptableObject
    {
        [SerializeField]
        private PositionSample[] m_positions;
        /// <summary>
        /// Gets or sets the authored point samples stored by this asset.
        /// </summary>
        /// <remarks>
        /// The array is returned and assigned by reference. Callers that modify the returned array are editing the
        /// container's serialized contents directly. Each sample is uploaded as-is to the graph buffer, so validity flags
        /// and positions should already be prepared in the format expected by consuming nodes.
        /// </remarks>
        public PositionSample[] positions
        {
            get
            {
                return m_positions;
            }
            set
            {
                m_positions = value;
            }
        }        
    }
}
#endif


