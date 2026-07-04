#if VISTA
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Pinwheel.Vista;
using Pinwheel.Vista.Graphics;

namespace Pinwheel.Vista.Graph
{
    /// <summary>
    /// Structural comparison helpers for slot definitions.
    /// </summary>
    public static class SlotUtilities
    {
        /// <summary>
        /// Compares two slots by id, direction, name, and adapted slot type.
        /// </summary>
        /// <param name="s0">First slot to compare.</param>
        /// <param name="s1">Second slot to compare.</param>
        /// <returns><see langword="true"/> if both slots are structurally equivalent.</returns>
        public static bool IsEqual(ISlot s0, ISlot s1)
        {
            if (s0 == null && s1 != null)
                return false;
            if (s0 != null && s1 == null)
                return false;
            if (s0 != null && s1 != null)
            {
                if (s0.id != s1.id)
                    return false;
                if (s0.direction != s1.direction)
                    return false;
                if (!string.Equals(s0.name, s1.name))
                    return false;
                ISlotAdapter a0 = s0.GetAdapter();
                ISlotAdapter a1 = s1.GetAdapter();
                if (a0.slotType != a1.slotType)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Compares two slot arrays element by element using <see cref="IsEqual(ISlot, ISlot)"/>.
        /// </summary>
        /// <param name="slotArray0">First slot array to compare.</param>
        /// <param name="slotArray1">Second slot array to compare.</param>
        /// <returns><see langword="true"/> if both arrays are structurally equivalent.</returns>
        public static bool AreEqual(ISlot[] slotArray0, ISlot[] slotArray1)
        {
            if (slotArray0 == null && slotArray1 != null)
                return false;
            if (slotArray0 != null && slotArray1 == null)
                return false;
            if (slotArray0 != null && slotArray1 != null)
            {
                if (slotArray0.Length != slotArray1.Length)
                    return false;
                for (int i = 0; i < slotArray0.Length; ++i)
                {
                    if (!IsEqual(slotArray0[i], slotArray1[i]))
                        return false;
                }
            }
            return true;
        }
    }
}
#endif


