#if VISTA
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Pinwheel.Vista;
using Pinwheel.Vista.Graphics;

namespace Pinwheel.Vista.Graph
{
    /// <summary>
    /// Helper queries over generic <see cref="INode"/> implementations.
    /// </summary>
    public static class INodeExtensions
    {
        /// <summary>
        /// Returns the first output slot whose adapter exposes either <see cref="MaskSlot"/> or <see cref="ColorTextureSlot"/>.
        /// </summary>
        /// <param name="node">Node whose output slots will be scanned.</param>
        /// <returns>The first texture-based output slot, or <see langword="null"/> when none exists.</returns>
        public static ISlot GetFirstTextureBasedSlot(this INode node)
        {
            ISlot[] outputSlots = node.GetOutputSlots();
            ISlot firstTextureBasedSlot = null;
            for (int i = 0; i < outputSlots.Length; ++i)
            {
                System.Type slotType = outputSlots[i].GetAdapter().slotType;
                if (slotType.Equals(typeof(MaskSlot)) || slotType.Equals(typeof(ColorTextureSlot)))
                {
                    firstTextureBasedSlot = outputSlots[i];
                    break;
                }
            }
            return firstTextureBasedSlot;
        }
    }
}
#endif


