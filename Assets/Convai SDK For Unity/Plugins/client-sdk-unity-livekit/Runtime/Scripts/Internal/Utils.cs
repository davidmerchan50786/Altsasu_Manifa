using System;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace LiveKit.Internal
{
    /// <summary>
    /// The class <c>Utils</c> contains internal utilities used for FfiClient 
    /// The log part is useful to print messages only when "LK_DEBUG" is defined.
    /// </summary>
    internal static class Utils
    {
        private const string PREFIX = "LiveKit";
        private const string LK_DEBUG = "LK_DEBUG";

        [Conditional(LK_DEBUG)]
        public static void Debug(object msg)
        {
            UnityEngine.Debug.unityLogger.Log(LogType.Log, $"{PREFIX}: {msg}");
        }

        public static void Error(object msg)
        {
            UnityEngine.Debug.unityLogger.Log(LogType.Error, $"{PREFIX}: {msg}");
        }

        public static GraphicsFormat GetSupportedGraphicsFormat(GraphicsDeviceType type)
        {
            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
            {
#pragma warning disable CS0618
                switch (type)
                {
                    case GraphicsDeviceType.Direct3D11:
                    case GraphicsDeviceType.Direct3D12:
                    case GraphicsDeviceType.Vulkan:
                        return GraphicsFormat.B8G8R8A8_SRGB;
                    case GraphicsDeviceType.OpenGLCore:
                    case GraphicsDeviceType.OpenGLES2:
                    case GraphicsDeviceType.OpenGLES3:
                        return GraphicsFormat.R8G8B8A8_SRGB;
                    case GraphicsDeviceType.Metal:
                        return GraphicsFormat.B8G8R8A8_SRGB;
                }
#pragma warning restore CS0618
            }
            else
            {
#pragma warning disable CS0618
                switch (type)
                {
                    case GraphicsDeviceType.Vulkan:
                        return GraphicsFormat.B8G8R8A8_UNorm;
                    case GraphicsDeviceType.Direct3D12:
                    case GraphicsDeviceType.Direct3D11:
                    case GraphicsDeviceType.OpenGLCore:
                    case GraphicsDeviceType.OpenGLES2:
                    case GraphicsDeviceType.OpenGLES3:
                        return GraphicsFormat.R8G8B8A8_UNorm;
                    case GraphicsDeviceType.Metal:
                        return GraphicsFormat.R8G8B8A8_UNorm;
                }
#pragma warning restore CS0618
            }

            throw new ArgumentException($"Graphics device type {type} not supported");
        }
    }
}
