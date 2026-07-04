#if VISTA
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Pinwheel.Vista;
using Pinwheel.Vista.Graphics;
using System;

namespace Pinwheel.Vista.Graph
{
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    /// <summary>
    /// Marks a node property so the exposed-property system skips it even if its type would otherwise be supported.
    /// </summary>
    public class NonExposableAttribute : Attribute
    {
    }
}
#endif


