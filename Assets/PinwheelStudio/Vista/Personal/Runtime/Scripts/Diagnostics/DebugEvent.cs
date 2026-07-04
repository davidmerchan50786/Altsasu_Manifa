#if VISTA
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pinwheel.Vista.Diagnostics
{
    public enum DebugEventType
    {
        ScopeBegin,
        ScopeEnd,
        TextureCapture,
        BufferCapture,
        Log,
        StringCapture
    }

    public enum DebugScopeType
    {
        GraphExecution,
        NodeExecution,
        BlendPass,
        BlendDispatch,
        Custom
    }

    public enum DebugBufferInterpretation
    {
        Unknown,
        PositionSample,
        InstanceSample
    }

    [Serializable]
    public class DebugTextureCapture
    {
        public int originalWidth;
        public int originalHeight;
        public int capturedWidth;
        public int capturedHeight;
        public string format;
        public string textureDataFormat;
        public int textureFileIndex = -1;
    }

    [Serializable]
    public class DebugEvent
    {
        public DebugEventType type;
        public float timestamp;
        public int depth;

        public string label;
        public DebugScopeType scopeType;

        public List<DebugTextureCapture> textures = new List<DebugTextureCapture>();

        public int elementCount;
        public int stride;
        public int bufferFileIndex;
        public DebugBufferInterpretation bufferInterpretation;

        public string message;
        public string stackTrace;
        public LogType logType;
    }
}
#endif
