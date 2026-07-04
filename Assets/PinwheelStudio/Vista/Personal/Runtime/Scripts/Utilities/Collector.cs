#if VISTA
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Pinwheel.Vista
{
    /// <summary>
    /// Lightweight accumulation container used by Vista collection callbacks and paired runtime data lists.
    /// </summary>
    /// <remarks>
    /// This type appears in two main patterns:
    /// event-driven discovery flows such as tile, biome, or version collection, and paired internal
    /// storage inside <see cref="Core.BiomeData"/>. Items are stored in insertion order and duplicates
    /// are allowed while collecting; distinct filtering only happens when exporting with
    /// <see cref="ToList"/> or <see cref="ToArray"/>.
    /// </remarks>
    public class Collector<T> : IEnumerable<T>
    {
        private List<T> m_list;

        /// <summary>
        /// Number of items currently stored in the collector, including duplicates.
        /// </summary>
        public int Count
        {
            get
            {
                return m_list.Count;
            }
        }

        /// <summary>
        /// Creates an empty collector.
        /// </summary>
        public Collector()
        {
            m_list = new List<T>();
        }

        /// <summary>
        /// Appends an item to the collector.
        /// </summary>
        /// <param name="item">
        /// Item to add.
        /// </param>
        public void Add(T item)
        {
            m_list.Add(item);
        }

        /// <summary>
        /// Determines whether the collector currently contains a specific item.
        /// </summary>
        public bool Contains(T item)
        {
            return m_list.Contains(item);
        }

        /// <summary>
        /// Returns the index of the first occurrence of an item in the collector.
        /// </summary>
        public int IndexOf(T item)
        {
            return m_list.IndexOf(item);
        }

        /// <summary>
        /// Returns the item stored at a zero-based index.
        /// </summary>
        public T At(int index)
        {
            return m_list[index];
        }

        /// <summary>
        /// Returns an enumerator over the raw collected items in insertion order.
        /// </summary>
        public IEnumerator<T> GetEnumerator()
        {
            return m_list.GetEnumerator();
        }

        /// <summary>
        /// Exports the collected items as a distinct list.
        /// </summary>
        /// <returns>
        /// A new list that preserves first-seen order while removing duplicates through
        /// <see cref="Enumerable.Distinct{TSource}(IEnumerable{TSource})"/>.
        /// </returns>
        public List<T> ToList()
        {
            return m_list.Distinct().ToList();
        }

        /// <summary>
        /// Exports the collected items as a distinct array.
        /// </summary>
        public T[] ToArray()
        {
            return m_list.Distinct().ToArray();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return m_list.GetEnumerator();
        }

        /// <summary>
        /// Removes all currently collected items.
        /// </summary>
        public void Clear()
        {
            m_list.Clear();
        }
    }
}
#endif


