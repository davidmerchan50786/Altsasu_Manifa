#if VISTA
using System.Collections.Generic;

namespace Pinwheel.Vista.Graph
{
    [System.Obsolete]
    /// <summary>
    /// Obsolete legacy contract for serializing Unity object references outside the main graph JSON payload.
    /// </summary>
    public interface IHasAssetReferences
    {
        /// <summary>
        /// Writes this object's referenced assets into the provided legacy reference list.
        /// </summary>
        /// <param name="refs">Destination list receiving serialized asset references.</param>
        void OnSerializeAssetReferences(List<ObjectRef> refs);
        /// <summary>
        /// Restores this object's referenced assets from the provided legacy reference list.
        /// </summary>
        /// <param name="refs">Source list containing serialized asset references.</param>
        void OnDeserializeAssetReferences(List<ObjectRef> refs);
    }
}
#endif


