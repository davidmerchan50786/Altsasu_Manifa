#if VISTA
using UnityEngine;

namespace Pinwheel.Vista
{
    /// <summary>
    /// Helper extension methods for updating biome state and resolving the owning <see cref="VistaManager"/>.
    /// </summary>
    public static class BiomeExtensions
    {
        /// <summary>
        /// Marks a biome as changed by updating its <see cref="IBiome.updateCounter"/> to the current timestamp.
        /// </summary>
        /// <param name="b">Biome to mark as changed.</param>
        /// <remarks>
        /// This is a lightweight change notification mechanism used by runtime and editor workflows before requesting regeneration.
        /// </remarks>
        public static void MarkChanged(this IBiome b)
        {
            b.updateCounter = System.DateTime.Now.Ticks;
        }

        /// <summary>
        /// Requests regeneration for the manager associated with the specified biome.
        /// </summary>
        /// <param name="b">Biome whose owning manager should regenerate.</param>
        /// <remarks>
        /// This method resolves a manager through <see cref="GetVistaManagerInstance(IBiome)"/> and then calls <see cref="VistaManager.GenerateAll"/>.
        /// If no manager can be resolved, nothing happens.
        /// </remarks>
        public static void GenerateBiomesInGroup(this IBiome b)
        {
            VistaManager manager = GetVistaManagerInstance(b);
            if (manager != null)
            {
                manager.GenerateAll();
            }
        }

        /// <summary>
        /// Resolves the <see cref="VistaManager"/> that owns a biome.
        /// </summary>
        /// <param name="b">Biome to resolve from.</param>
        /// <returns>
        /// The nearest parent <see cref="VistaManager"/> when one exists; otherwise a manager matched by
        /// <see cref="BiomeVMConnector.managerId"/>; otherwise <see langword="null"/>.
        /// </returns>
        /// <remarks>
        /// The parent lookup path handles standard biome hierarchies. The connector fallback supports biomes that are
        /// not parented under a manager but are linked by id instead.
        /// </remarks>
        public static VistaManager GetVistaManagerInstance(this IBiome b)
        {
            VistaManager manager = b.gameObject.GetComponentInParent<VistaManager>();
            if (manager == null)
            {
                BiomeVMConnector connector = b.gameObject.GetComponent<BiomeVMConnector>();
                if (connector != null)
                {
                    foreach (VistaManager vm in VistaManager.allInstances)
                    {
                        if (string.Equals(vm.id, connector.managerId))
                        {
                            manager = vm;
                            break;
                        }
                    }
                }
            }
            return manager;
        }
    }
}
#endif


