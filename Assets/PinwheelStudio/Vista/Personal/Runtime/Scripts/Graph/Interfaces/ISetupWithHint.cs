#if VISTA

namespace Pinwheel.Vista.Graph
{
    /// <summary>
    /// Marks a node that can configure its initial state from a textual creation hint.
    /// </summary>
    public interface ISetupWithHint
    {
        /// <summary>
        /// Applies an editor-supplied hint, typically from node search keywords or quick-create shortcuts.
        /// </summary>
        /// <param name="hint">Creation hint to interpret.</param>
        void SetupWithHint(string hint);
    }
}
#endif


