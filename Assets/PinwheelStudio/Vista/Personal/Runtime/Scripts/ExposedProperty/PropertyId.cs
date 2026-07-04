#if VISTA
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Pinwheel.Vista;
using Pinwheel.Vista.Graphics;
using System;

namespace Pinwheel.Vista.ExposeProperty
{
    [System.Serializable]
    /// <summary>
    /// Identifies one exposed node property inside a graph.
    /// </summary>
    /// <remarks>
    /// The ID is composed of the source node's runtime ID plus the property name on that node type. It is used to match
    /// graph descriptors with biome-side <see cref="PropertyOverride"/> entries during override application.
    /// </remarks>
    public class PropertyId : IEquatable<PropertyId>
    {
        [SerializeField]
        private string m_nodeId;
        /// <summary>
        /// Gets the ID of the node that owns the exposed property.
        /// </summary>
        public string nodeId
        {
            get
            {
                return m_nodeId;
            }
        }

        [SerializeField]
        private string m_propertyName;
        /// <summary>
        /// Gets the name of the exposed CLR property on the node type.
        /// </summary>
        public string propertyName
        {
            get
            {
                return m_propertyName;
            }
        }

        /// <summary>
        /// Creates a new identifier for a node property.
        /// </summary>
        /// <param name="nodeId">The ID of the node that owns the property.</param>
        /// <param name="propertyName">The name of the exposed property on that node.</param>
        public PropertyId(string nodeId, string propertyName)
        {
            this.m_nodeId = nodeId;
            this.m_propertyName = propertyName;
        }

        /// <summary>
        /// Returns the identifier in <c>nodeId.propertyName</c> form.
        /// </summary>
        /// <returns>A readable string representation of this identifier.</returns>
        public override string ToString()
        {
            return $"{nodeId}.{propertyName}";
        }

        /// <summary>
        /// Tests whether this identifier refers to the same node property as another identifier.
        /// </summary>
        /// <param name="other">The identifier to compare against.</param>
        /// <returns>
        /// <see langword="true"/> when both the node ID and property name match exactly; otherwise, <see langword="false"/>.
        /// </returns>
        public bool Equals(PropertyId other)
        {
            return string.Equals(this.m_nodeId, other.m_nodeId) && string.Equals(propertyName, other.propertyName);
        }
    }
}
#endif


