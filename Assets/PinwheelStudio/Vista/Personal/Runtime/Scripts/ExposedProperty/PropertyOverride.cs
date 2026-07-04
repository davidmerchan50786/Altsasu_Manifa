#if VISTA
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Pinwheel.Vista;
using Pinwheel.Vista.Graphics;
using System.Reflection;
using System;

namespace Pinwheel.Vista.ExposeProperty
{
    [System.Serializable]
    /// <summary>
    /// Stores the serialized override payload for one exposed graph property.
    /// </summary>
    /// <remarks>
    /// A <see cref="PropertyOverride"/> lives on a biome and carries value storage for every supported exposed-property
    /// shape. At runtime, the override system chooses which field to read based on the graph's
    /// <see cref="PropertyDescriptor.propertyType"/> for the matching <see cref="id"/>.
    /// </remarks>
    public class PropertyOverride
    {
        [SerializeField]
        private PropertyId m_id;
        /// <summary>
        /// Gets the identifier of the exposed property this override targets.
        /// </summary>
        public PropertyId id
        {
            get
            {
                return m_id;
            }
        }

        [SerializeField]
        private int m_intValue;
        /// <summary>
        /// Gets or sets the stored integer payload.
        /// </summary>
        /// <remarks>
        /// This field is used when the target descriptor resolves to <see cref="PropertyType.IntegerNumber"/>.
        /// </remarks>
        public int intValue
        {
            get
            {
                return m_intValue;
            }
            set
            {
                m_intValue = value;
            }
        }

        [SerializeField]
        private float m_floatValue;
        /// <summary>
        /// Gets or sets the stored floating-point payload.
        /// </summary>
        /// <remarks>
        /// This field is used when the target descriptor resolves to <see cref="PropertyType.RealNumber"/>.
        /// </remarks>
        public float floatValue
        {
            get
            {
                return m_floatValue;
            }
            set
            {
                m_floatValue = value;
            }
        }

        [SerializeField]
        private bool m_boolValue;
        /// <summary>
        /// Gets or sets the stored boolean payload.
        /// </summary>
        /// <remarks>
        /// This field is used when the target descriptor resolves to <see cref="PropertyType.TrueFalse"/>.
        /// </remarks>
        public bool boolValue
        {
            get
            {
                return m_boolValue;
            }
            set
            {
                m_boolValue = value;
            }
        }

        [SerializeField]
        private string m_stringValue;
        /// <summary>
        /// Gets or sets the stored string payload.
        /// </summary>
        /// <remarks>
        /// This field is used when the target descriptor resolves to <see cref="PropertyType.Text"/>.
        /// </remarks>
        public string stringValue
        {
            get
            {
                return m_stringValue;
            }
            set
            {
                m_stringValue = value;
            }
        }

        [SerializeField]
        private Vector4 m_vectorValue;
        /// <summary>
        /// Gets or sets the stored vector payload.
        /// </summary>
        /// <remarks>
        /// This field is shared by exposed <see cref="Vector2"/>, <see cref="Vector3"/>, and <see cref="Vector4"/> graph
        /// properties, with unused components ignored according to the target property type.
        /// </remarks>
        public Vector4 vectorValue
        {
            get
            {
                return m_vectorValue;
            }
            set
            {
                m_vectorValue = value;
            }
        }

        [SerializeField]
        private int m_enumValue;
        /// <summary>
        /// Gets or sets the stored enum payload as the underlying integer value.
        /// </summary>
        /// <remarks>
        /// This field is used when the target descriptor resolves to <see cref="PropertyType.Options"/>.
        /// </remarks>
        public int enumValue
        {
            get
            {
                return m_enumValue;
            }
            set
            {
                m_enumValue = value;
            }
        }


        [SerializeField]
        private Color m_colorValue;
        /// <summary>
        /// Gets or sets the stored color payload.
        /// </summary>
        /// <remarks>
        /// This field is used when the target descriptor resolves to <see cref="PropertyType.Color"/>.
        /// </remarks>
        public Color colorValue
        {
            get
            {
                return m_colorValue;
            }
            set
            {
                m_colorValue = value;
            }
        }

        [SerializeField]
        private Gradient m_gradientValue;
        /// <summary>
        /// Gets or sets the stored gradient payload.
        /// </summary>
        /// <remarks>
        /// This field is used when the target descriptor resolves to <see cref="PropertyType.Gradient"/>.
        /// </remarks>
        public Gradient gradientValue
        {
            get
            {
                return m_gradientValue;
            }
            set
            {
                m_gradientValue = value;
            }
        }

        [SerializeField]
        private AnimationCurve m_curveValue;
        /// <summary>
        /// Gets or sets the stored animation-curve payload.
        /// </summary>
        /// <remarks>
        /// This field is used when the target descriptor resolves to <see cref="PropertyType.Curve"/>.
        /// </remarks>
        public AnimationCurve curveValue
        {
            get
            {
                return m_curveValue;
            }
            set
            {
                m_curveValue = value;
            }
        }

        [SerializeField]
        private UnityEngine.Object m_objectValue;
        /// <summary>
        /// Gets or sets the stored Unity object reference payload.
        /// </summary>
        /// <remarks>
        /// This field is used when the target descriptor resolves to <see cref="PropertyType.UnityObject"/>.
        /// </remarks>
        public UnityEngine.Object objectValue
        {
            get
            {
                return m_objectValue;
            }
            set
            {
                m_objectValue = value;
            }
        }

        /// <summary>
        /// Creates a new override entry targeting a specific node property.
        /// </summary>
        /// <param name="nodeId">The ID of the node that owns the exposed property.</param>
        /// <param name="propertyName">The name of the exposed property on that node.</param>
        public PropertyOverride(string nodeId, string propertyName)
        {
            PropertyId id = new PropertyId(nodeId, propertyName);
            m_id = id;
        }
    }
}
#endif


