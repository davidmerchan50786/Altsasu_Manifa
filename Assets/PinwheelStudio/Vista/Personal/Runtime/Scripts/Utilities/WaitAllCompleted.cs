#if VISTA
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Pinwheel.Vista;
using Pinwheel.Vista.Graphics;

namespace Pinwheel.Vista
{
    /// <summary>
    /// Yield instruction that waits until every supplied <see cref="ProgressiveTask"/> has completed.
    /// </summary>
    public class WaitAllCompleted : CustomYieldInstruction
    {
        private ProgressiveTask[] m_tasks;

        /// <summary>
        /// Creates a wait instruction over a fixed set of tasks.
        /// </summary>
        /// <param name="tasks">Tasks to monitor.</param>
        public WaitAllCompleted(params ProgressiveTask[] tasks)
        {
            m_tasks = tasks;
        }

        /// <summary>
        /// Returns <see langword="true"/> while at least one tracked task is still incomplete.
        /// </summary>
        public override bool keepWaiting
        {
            get
            {
                foreach (ProgressiveTask t in m_tasks)
                {
                    if (!t.isCompleted)
                    {
                        return true;
                    }
                }
                return false;
            }
        }
    }
}
#endif


