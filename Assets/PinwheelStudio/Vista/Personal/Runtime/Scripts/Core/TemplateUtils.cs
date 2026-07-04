#if VISTA

namespace Pinwheel.Vista
{
    /// <summary>
    /// Provides runtime feature checks shared by Vista template assets.
    /// </summary>
    /// <remarks>
    /// The current utility surface exposes whether template variant arrays should be considered available. Template types
    /// such as <see cref="DetailTemplate"/>, <see cref="ObjectTemplate"/>, and <see cref="TreeTemplate"/> use this check to
    /// decide whether variant getters should return their stored data or an empty array.
    /// </remarks>
    public static class TemplateUtils
    {
        internal delegate bool EnableVariantsSupportHandler();
        internal static event EnableVariantsSupportHandler enableVariantsSupportCallback;

        /// <summary>
        /// Returns whether the current runtime integration supports template variants.
        /// </summary>
        /// <returns>
        /// <see langword="true"/> when a runtime component has registered a variant-support callback and that callback
        /// returns <see langword="true"/>; otherwise, <see langword="false"/>.
        /// </returns>
        /// <remarks>
        /// When this method returns <see langword="false"/>, template variant properties intentionally behave as if no
        /// variants were authored, even if backing arrays are populated. This lets integrations opt in to variant handling
        /// without forcing every backend to expose the same behavior.
        /// </remarks>
        public static bool IsVariantsSupported()
        {
            return enableVariantsSupportCallback != null && enableVariantsSupportCallback.Invoke() == true;
        }
    }
}
#endif


