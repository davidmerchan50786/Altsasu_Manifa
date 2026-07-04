#if VISTA
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;
using Object = UnityEngine.Object;

namespace Pinwheel.Vista.Graph
{
    [Serializable]
    /// <summary>
    /// Stores one serialized Unity object reference used by graph asset serialization.
    /// </summary>
    /// <remarks>
    /// <see cref="GraphAsset"/> uses this type to preserve fields marked with
    /// <see cref="Attributes.SerializeAssetAttribute"/> while the rest of the graph is serialized as
    /// JSON. The <see cref="key"/> is a synthetic identifier derived from node id and field name.
    /// </remarks>
    public struct ObjectReference
    {
        [SerializeField]
        private string m_key;
        /// <summary>
        /// Synthetic serialization key that identifies which node field this object belongs to.
        /// </summary>
        public string key
        {
            get
            {
                return m_key;
            }
        }

        [SerializeField]
        private Object m_target;
        /// <summary>
        /// Unity object stored for the serialized field identified by <see cref="key"/>.
        /// </summary>
        public Object target
        {
            get
            {
                return m_target;
            }
        }

        /// <summary>
        /// Creates a serialized object-reference entry for one Unity object field.
        /// </summary>
        /// <param name="key">
        /// The synthetic key that identifies the serialized field.
        /// </param>
        /// <param name="target">
        /// The Unity object assigned to that field.
        /// </param>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="key"/> is null or empty.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="target"/> is <see langword="null"/>.
        /// </exception>
        public ObjectReference(string key, Object target)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException($"{nameof(key)} should not be null or empty");
            }
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }
            m_key = key;
            m_target = target;
        }
    }
}
#endif


