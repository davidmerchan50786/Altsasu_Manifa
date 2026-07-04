#if VISTA
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace Pinwheel.Vista.ExposeProperty
{
    /// <summary>
    /// Categorizes exposed graph properties by the editor/runtime handling path they require.
    /// </summary>
    /// <remarks>
    /// These values do not mirror CLR types one-to-one. Instead they describe the UI widget and override-storage path used
    /// by the expose-property system after a source property has been normalized by
    /// <c>PropertyDescriptorExtensions.SyncWithGraph</c>.
    /// </remarks>
    public enum PropertyType
    {
        /// <summary>
        /// Integer-valued property handled through the integer override field and integer range metadata.
        /// </summary>
        IntegerNumber = 0,
        /// <summary>
        /// Floating-point property handled through the float override field and float range metadata.
        /// </summary>
        RealNumber = 20,
        /// <summary>
        /// Boolean property handled through the boolean override field.
        /// </summary>
        TrueFalse = 40,
        /// <summary>
        /// String property handled through the string override field.
        /// </summary>
        Text = 50,
        /// <summary>
        /// Vector-valued property handled through the shared vector override field.
        /// </summary>
        Vector = 60,
        /// <summary>
        /// Enum-backed property handled through the integer enum override field and enum metadata.
        /// </summary>
        Options = 70,
        /// <summary>
        /// Color-valued property handled through the color override field.
        /// </summary>
        Color = 80,
        /// <summary>
        /// Gradient-valued property handled through the gradient override field.
        /// </summary>
        Gradient = 90,
        /// <summary>
        /// Animation-curve property handled through the curve override field.
        /// </summary>
        Curve = 100,
        /// <summary>
        /// Unity object reference property handled through the object-reference override field.
        /// </summary>
        UnityObject = 110
    }
}
#endif


