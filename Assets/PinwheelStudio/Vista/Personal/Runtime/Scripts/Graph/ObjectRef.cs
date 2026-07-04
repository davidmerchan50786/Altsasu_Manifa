#if VISTA
using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Pinwheel.Vista.Graph
{
    [Obsolete]
    [System.Serializable]
    /// <summary>
    /// Legacy serialized wrapper for a Unity object reference.
    /// </summary>
    /// <remarks>
    /// This type is marked <see cref="Obsolete"/> and has been superseded by
    /// <see cref="ObjectReference"/>, which stores a deterministic serialization key instead of a
    /// random GUID.
    /// </remarks>
    public struct ObjectRef : IEquatable<ObjectRef>
    {
        [SerializeField]
        private string m_guid;
        /// <summary>
        /// Legacy per-reference GUID generated when the wrapper is created.
        /// </summary>
        public string guid
        {
            get
            {
                return m_guid;
            }
        } 

        [SerializeField]
        private Object m_target;
        /// <summary>
        /// Unity object carried by this legacy wrapper.
        /// </summary>
        public Object target
        {
            get
            {
                return m_target;
            }
        }

        /// <summary>
        /// Creates a legacy object reference wrapper for a non-null Unity object.
        /// </summary>
        /// <param name="target">
        /// The Unity object to store.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="target"/> is <see langword="null"/>.
        /// </exception>
        public ObjectRef(Object target)
        {
            if (target == null)
            {
                throw new ArgumentNullException("target");
            }
            m_guid = Utilities.GenerateId();
            m_target = target;
        }

        /// <summary>
        /// Compares two legacy wrappers by both generated GUID and referenced Unity object.
        /// </summary>
        public bool Equals(ObjectRef other)
        {
            return this.m_guid.Equals(other.m_guid) && this.m_target.Equals(other.m_target);
        }
    }
}
#endif


