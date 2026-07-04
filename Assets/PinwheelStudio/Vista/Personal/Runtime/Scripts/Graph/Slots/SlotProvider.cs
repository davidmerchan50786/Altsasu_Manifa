#if VISTA
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;

namespace Pinwheel.Vista.Graph
{
    /// <summary>
    /// Discovers supported slot classes and constructs slot instances by runtime type.
    /// </summary>
    public static class SlotProvider
    {
        private static List<Type> slotTypes { get; set; }

        private static void Init()
        {
            slotTypes = new List<Type>();
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (Assembly assembly in assemblies)
            {
                Type[] types = assembly.GetTypes();
                foreach (Type t in types)
                {
                    if (typeof(ISlot).IsAssignableFrom(t))
                    {
                        if (!t.IsClass)
                            continue;
                        if (t.IsGenericType)
                            continue;
                        if (t.IsAbstract)
                            continue;
                        EditorBrowsableAttribute att = t.GetCustomAttribute<EditorBrowsableAttribute>();
                        if (att != null && att.State == EditorBrowsableState.Never)
                            continue;
                        slotTypes.Add(t);
                    }
                }
            }
        }

        /// <summary>
        /// Returns all concrete slot classes visible to the graph editor.
        /// </summary>
        /// <returns>A new list of discovered slot types.</returns>
        public static List<Type> GetAllSlotTypes()
        {
            if (slotTypes == null)
            {
                Init();
            }
            return new List<Type>(slotTypes);
        }

        /// <summary>
        /// Returns the slot types treated as texture slots by graph tools.
        /// </summary>
        public static List<Type> GetTextureSlotTypes()
        {
            return new List<Type>()
            {
                typeof(MaskSlot),
                typeof(ColorTextureSlot)
            };
        }

        /// <summary>
        /// Creates a slot instance from a compile-time slot type.
        /// </summary>
        /// <typeparam name="T">Concrete slot type to instantiate.</typeparam>
        /// <param name="name">Display name of the slot.</param>
        /// <param name="direction">Input or output direction.</param>
        /// <param name="id">Per-node slot identifier.</param>
        public static ISlot Create<T>(string name, SlotDirection direction, int id) where T : ISlot, new()
        {
            return Create(typeof(T), name, direction, id);
        }

        /// <summary>
        /// Creates a slot instance by invoking the standard <c>(string, SlotDirection, int)</c> constructor on the supplied slot type.
        /// </summary>
        /// <param name="t">Concrete slot type to instantiate.</param>
        /// <param name="name">Display name of the slot.</param>
        /// <param name="direction">Input or output direction.</param>
        /// <param name="id">Per-node slot identifier.</param>
        public static ISlot Create(Type t, string name, SlotDirection direction, int id)
        {
            ISlot slot = Activator.CreateInstance(t, new object[] { name, direction, id }) as ISlot;
            return slot;
        }
    }
}
#endif


