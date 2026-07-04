#if VISTA
namespace Pinwheel.Vista
{
    /// <summary>
    /// Describes how a tile channel should behave when the current generation pass does not produce that output.
    /// </summary>
    public enum MissingOutputAction
    {
        Keep = 0,
        Clear = 1
    }
}
#endif
