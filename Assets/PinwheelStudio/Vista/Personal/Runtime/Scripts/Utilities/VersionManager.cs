#if VISTA
using System.Collections.Generic;

namespace Pinwheel.Vista
{
    /// <summary>
    /// Aggregates version strings contributed by Vista modules through a shared callback hook.
    /// </summary>
    public class VersionManager
    {
        /// <summary>
        /// Adds one or more version strings to the shared collection returned by <see cref="GetVersionStrings()"/>.
        /// </summary>
        /// <param name="versionStrings">Destination collector that receives version labels from registered modules.</param>
        public delegate void CollectVersionInfoHandler(Collector<string> versionStrings);
        /// <summary>
        /// Raised when Vista gathers version labels from all registered modules.
        /// </summary>
        public static event CollectVersionInfoHandler collectVersionInfoCallback;

        /// <summary>
        /// Collects version strings from every registered provider and returns them as a deduplicated list.
        /// </summary>
        public static List<string> GetVersionStrings()
        {
            Collector<string> versionStrings = new Collector<string>();
            if (collectVersionInfoCallback != null)
            {
                collectVersionInfoCallback.Invoke(versionStrings);
            }

            return versionStrings.ToList();
        }
    }
}
#endif


