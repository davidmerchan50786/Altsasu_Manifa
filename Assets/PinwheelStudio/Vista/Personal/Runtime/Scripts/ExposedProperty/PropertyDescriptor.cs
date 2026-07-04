#if VISTA
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Pinwheel.Vista;
using Pinwheel.Vista.Graphics;
using System;
using System.Reflection;
using Pinwheel.Vista.Graph;

namespace Pinwheel.Vista.ExposeProperty
{
    [System.Serializable]
    /// <summary>
    /// Describes one graph property that has been exposed for external editing or override.
    /// </summary>
    /// <remarks>
    /// A <see cref="PropertyDescriptor"/> is stored on the graph asset, not on the biome. It captures the identity of the
    /// exposed node property together with the metadata needed to render UI and validate overrides, such as labels,
    /// grouping, value ranges, and the normalized <see cref="PropertyType"/>.
    /// </remarks>
    public class PropertyDescriptor
    {
        [SerializeField]
        internal PropertyId m_id;
        /// <summary>
        /// Gets the stable identifier of the exposed graph property.
        /// </summary>
        public PropertyId id
        {
            get
            {
                return m_id;
            }
        }

        [SerializeField]
        internal string m_label;
        /// <summary>
        /// Gets or sets the display label shown for this exposed property.
        /// </summary>
        /// <remarks>
        /// The label is used mainly by editor and inspector UI when rendering exposed-property controls.
        /// </remarks>
        public string label
        {
            get
            {
                return m_label;
            }
            set
            {
                m_label = value;
            }
        }

        [SerializeField]
        internal string m_description;
        /// <summary>
        /// Gets or sets the descriptive text shown alongside this exposed property.
        /// </summary>
        public string description
        {
            get
            {
                return m_description;
            }
            set
            {
                m_description = value;
            }
        }

        [SerializeField]
        internal string m_groupName;
        /// <summary>
        /// Gets or sets the logical group name used to organize this property in UI.
        /// </summary>
        public string groupName
        {
            get
            {
                return m_groupName;
            }
            set
            {
                m_groupName = value;
            }
        }

        [SerializeField]
        internal string m_enumTypeName;
        /// <summary>
        /// Gets the enum type of the exposed property when <see cref="propertyType"/> is <see cref="PropertyType.Options"/>.
        /// </summary>
        /// <remarks>
        /// The type is reconstructed from the stored assembly-qualified name each time it is queried.
        /// </remarks>
        public Type enumType
        {
            get
            {
                return Type.GetType(m_enumTypeName);
            }
        }

        [SerializeField]
        internal string m_objectTypeName;
        /// <summary>
        /// Gets the Unity object type accepted by this property when <see cref="propertyType"/> is <see cref="PropertyType.UnityObject"/>.
        /// </summary>
        /// <remarks>
        /// The type is reconstructed from the stored assembly-qualified name and may be <see langword="null"/> when the
        /// property does not target a Unity object reference.
        /// </remarks>
        public Type objectType
        {
            get
            {
                return !string.IsNullOrEmpty(m_objectTypeName) ? Type.GetType(m_objectTypeName) : null;
            }
        }

        [SerializeField]
        internal PropertyType m_propertyType;
        /// <summary>
        /// Gets the normalized exposed-property category used by UI and override application.
        /// </summary>
        public PropertyType propertyType
        {
            get
            {
                return m_propertyType;
            }
        }

        [SerializeField]
        internal MinMaxInt m_intValueRange;
        /// <summary>
        /// Gets or sets the integer range metadata used when the exposed property is an integer value.
        /// </summary>
        public MinMaxInt intValueRange
        {
            get
            {
                return m_intValueRange;
            }
            set
            {
                m_intValueRange = value;
            }
        }

        [SerializeField]
        internal MinMaxFloat m_floatValueRange;
        /// <summary>
        /// Gets or sets the floating-point range metadata used when the exposed property is a real-number value.
        /// </summary>
        public MinMaxFloat floatValueRange
        {
            get
            {
                return m_floatValueRange;
            }
            set
            {
                m_floatValueRange = value;
            }
        }

        internal PropertyDescriptor(GraphAsset graph, string nodeId, string propertyName)
        {
            INode node = graph.GetNode(nodeId);
            if (node == null)
                throw new System.Exception($"Failed to create exposed property. Node {nodeId} not found.");
            PropertyInfo propertyInfo = node.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.SetProperty);
            if (propertyInfo == null)
                throw new System.Exception($"Cannot expose property {propertyName} for node {node.id}. Property not found.");
            if (!IsExposable(propertyInfo.PropertyType))
                throw new System.Exception($"Property {propertyInfo.Name} of type {propertyInfo.PropertyType.Name} is not exposable.");

            m_id = new PropertyId(nodeId, propertyName);
            m_intValueRange = MinMaxInt.FULL_RANGE;
            m_floatValueRange = MinMaxFloat.FULL_RANGE;
        }

        /// <summary>
        /// Returns whether a CLR property type can be exposed through Vista's exposed-property system.
        /// </summary>
        /// <param name="t">The property type to test.</param>
        /// <returns>
        /// <see langword="true"/> when Vista knows how to serialize, display, and override the type; otherwise,
        /// <see langword="false"/>.
        /// </returns>
        /// <remarks>
        /// Supported types currently include enums, Unity object references, primitive numeric and boolean types, strings,
        /// vectors, colors, gradients, and animation curves.
        /// </remarks>
        public static bool IsExposable(Type t)
        {
            if (t.IsEnum)
                return true;

            if (t.IsSubclassOf(typeof(UnityEngine.Object)))
                return true;

            if (t == typeof(int) ||
                t == typeof(float) ||
                t == typeof(bool) ||
                t == typeof(string) ||
                t == typeof(Vector2) ||
                t == typeof(Vector3) ||
                t == typeof(Vector4) ||
                t == typeof(Color) ||
                t == typeof(Color32) ||
                t == typeof(Gradient) ||
                t == typeof(AnimationCurve))
                return true;

            return false;
        }

        
    }
}
#endif


