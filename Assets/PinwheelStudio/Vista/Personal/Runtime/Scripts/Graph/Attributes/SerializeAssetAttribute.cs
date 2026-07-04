#if VISTA
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Pinwheel.Vista;
using Pinwheel.Vista.Graphics;
using System;

namespace Pinwheel.Vista.Graph
{
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    /// <summary>
    /// Marks a Unity object field so <see cref="GraphAsset"/> stores it through the object-reference bridge instead of raw JSON serialization.
    /// </summary>
    public class SerializeAssetAttribute : Attribute
    {
    }
}
#endif


