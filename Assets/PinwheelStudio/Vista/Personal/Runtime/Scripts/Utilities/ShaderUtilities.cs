#if VISTA
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Pinwheel.Vista;
using Pinwheel.Vista.Graphics;

namespace Pinwheel.Vista
{
    /// <summary>
    /// Caches shader lookups so repeated calls do not rely on repeated global <see cref="Shader.Find(string)"/> searches.
    /// </summary>
    public static class ShaderUtilities
    {
        private static Dictionary<string, Shader> s_shaderCache = new Dictionary<string, Shader>();

        /// <summary>
        /// Finds a shader by name and caches the successful lookup for later reuse.
        /// </summary>
        /// <param name="shaderName">Exact shader name passed to <see cref="Shader.Find(string)"/>.</param>
        /// <returns>The resolved shader, or <see langword="null"/> if the shader cannot be found.</returns>
        public static Shader Find(string shaderName)
        {
            Shader s;
            if (s_shaderCache.TryGetValue(shaderName, out s))
            {
                return s;
            }
            else
            {
                s = Shader.Find(shaderName);
                if (s != null)
                {
                    s_shaderCache.Add(shaderName, s);
                }
                return s;
            }
        }
    }
}
#endif


