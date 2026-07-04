#if VISTA
using UnityEngine;

namespace Pinwheel.Vista
{
    /// <summary>
    /// Coroutine-backed task handle that can be yielded until some Vista process reports completion.
    /// </summary>
    public class ProgressiveTask : CustomYieldInstruction
    {
        /// <summary>
        /// Indicates whether the task has been marked as finished.
        /// </summary>
        public bool isCompleted { get; private set; }
        /// <summary>
        /// Returns <see langword="true"/> while the task is still running so Unity keeps yielding on it.
        /// </summary>
        public override bool keepWaiting
        {
            get
            {
                return !isCompleted;
            }
        }        

        /// <summary>
        /// Marks the task as completed so any waiter stops yielding on it.
        /// </summary>
        public void Complete()
        {
            isCompleted = true;
        }
    }
}
#endif


