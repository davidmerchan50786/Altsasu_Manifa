#if VISTA
using System;

namespace Pinwheel.Vista.Graph
{
    /// <summary>
    /// Thrown when graph execution detects a recursive subgraph dependency chain.
    /// </summary>
    /// <remarks>
    /// <see cref="GraphContext"/> raises this exception before execution starts when a graph depends,
    /// directly or indirectly, on itself through subgraph references.
    /// </remarks>
    public class RecursiveGraphReferenceException : Exception
    {
        /// <summary>
        /// Creates an exception with the default base message.
        /// </summary>
        public RecursiveGraphReferenceException() : base()
        {

        }

        /// <summary>
        /// Creates an exception describing the detected recursive dependency chain.
        /// </summary>
        /// <param name="message">
        /// The error message to attach to the exception.
        /// </param>
        public RecursiveGraphReferenceException(string message) : base(message)
        {

        }
    }
}
#endif


