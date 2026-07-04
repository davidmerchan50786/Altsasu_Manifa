#if VISTA
using System.Reflection;

namespace Pinwheel.Vista.Graph
{
    /// <summary>
    /// Marks a graph element that exposes a stable identifier.
    /// </summary>
    public interface IHasID
    {
        /// <summary>
        /// Stable identifier used by graph serialization and structural references.
        /// </summary>
        string id { get; }
    }

    /// <summary>
    /// Helper methods for assigning and generating ids on graph elements.
    /// </summary>
    public static class IHasIdExtension
    {
        /// <summary>
        /// Assigns a new id by writing to the private <c>m_id</c> backing field expected by Vista graph elements.
        /// </summary>
        /// <param name="target">Target object whose id field will be overwritten.</param>
        /// <param name="newId">Id string to assign.</param>
        /// <exception cref="System.Exception">Thrown when the target does not declare the expected private <c>m_id</c> field.</exception>
        public static void SetId(object target, string newId)
        {
            FieldInfo idField = target.GetType().GetField("m_id", BindingFlags.Instance | BindingFlags.NonPublic);
            if (idField == null)
                throw new System.Exception("Element should declare a field [m_id] for copy/paste to work");
            idField.SetValue(target, newId);
        }

        /// <summary>
        /// Generates a fresh GUID string suitable for use as a graph element id.
        /// </summary>
        public static string GenerateId()
        {
            return Utilities.GenerateId();
        }
    }
}
#endif


