#if VISTA
using System.Collections.Generic;

namespace Pinwheel.Vista
{
    /// <summary>
    /// Represents module manager.
    /// </summary>
    public static class ModuleManager
    {
        private static HashSet<IModuleInfo> s_registeredModules;

        /// <summary>
        /// Performs register.
        /// </summary>
        /// <param name="module">Module value.</param>
        public static void Register(IModuleInfo module)
        {
            if (s_registeredModules == null)
            {
                s_registeredModules = new HashSet<IModuleInfo>();
            }

            s_registeredModules.Add(module);
        }

    }
}
#endif


