#if VISTA
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Pinwheel.Vista;
using Pinwheel.Vista.Graph;
using UnityEditor;
using UnityEngine;

namespace Pinwheel.Vista.Diagnostics
{
    public static class VistaDebugger
    {
        public const int CAPTURE_RESOLUTION = 128;

        private static DebugSession s_activeSession;
        private static float s_sessionStartTime;
        private static readonly Stack<string> s_scopeStack = new Stack<string>();

        public static bool isRecording
        {
            get
            {
                return s_activeSession != null;
            }
        }

        public static void BeginSession()
        {
            Debug.Log($"[VistaDebugger] Session started.");
            if (isRecording)
            {
                EndSession();
            }

            s_activeSession = DebugSession.Create();
            s_activeSession.EnsureDirectoriesExist();
            s_sessionStartTime = Time.realtimeSinceStartup;
            s_scopeStack.Clear();

            Application.logMessageReceived += OnLogMessageReceived;
        }

        public static void SetSessionSeeds(IList<SeedSnapshot> seeds)
        {
            if (!isRecording)
            {
                return;
            }

            s_activeSession.seeds.Clear();
            if (seeds == null)
            {
                return;
            }

            for (int i = 0; i < seeds.Count; ++i)
            {
                SeedSnapshot seedSnapshot = seeds[i];
                if (seedSnapshot == null)
                {
                    continue;
                }

                SeedSnapshot clone = new SeedSnapshot();
                clone.label = seedSnapshot.label;
                clone.seed = seedSnapshot.seed;
                s_activeSession.seeds.Add(clone);
            }
        }

        public static string EndSession()
        {
            if (!isRecording)
            {
                return string.Empty;
            }

            Application.logMessageReceived -= OnLogMessageReceived;

            if (s_scopeStack.Count > 0)
            {
                Debug.LogWarning($"[VistaDebugger] EndSession called with {s_scopeStack.Count} unclosed scope(s): {string.Join(" > ", s_scopeStack)}.");
            }

            s_activeSession.WriteSessionJson();

            string directory = s_activeSession.sessionDirectory;
            s_activeSession = null;
            s_scopeStack.Clear();
            Debug.Log($"[VistaDebugger] Session saved to: {directory}");

            return directory;
        }

        public static void OpenScope(string label, DebugScopeType scopeType)
        {
            if (!isRecording)
            {
                return;
            }

            DebugEvent scopeEvent = new DebugEvent();
            scopeEvent.type = DebugEventType.ScopeBegin;
            scopeEvent.timestamp = GetTimestamp();
            scopeEvent.depth = s_scopeStack.Count;
            scopeEvent.label = label;
            scopeEvent.scopeType = scopeType;
            s_activeSession.events.Add(scopeEvent);

            s_scopeStack.Push(label);
        }

        public static void CloseScope()
        {
            if (!isRecording)
            {
                return;
            }

            if (s_scopeStack.Count > 0)
            {
                s_scopeStack.Pop();
            }

            DebugEvent scopeEvent = new DebugEvent();
            scopeEvent.type = DebugEventType.ScopeEnd;
            scopeEvent.timestamp = GetTimestamp();
            scopeEvent.depth = s_scopeStack.Count;
            s_activeSession.events.Add(scopeEvent);
        }

        public static void OnBeforeNodeExecute(ExecutableNodeBase node, GraphContext context)
        {
            if (!isRecording)
            {
                return;
            }

            string nodeTypeName = node.GetType().Name;
            OpenScope($"{nodeTypeName} ({node.id})", DebugScopeType.NodeExecution);
            CaptureSerializedAssets(node);
            CaptureVariableName(node);
            CaptureNodeSlots(node, context, SlotDirection.Input);
        }

        public static void OnAfterNodeExecute(ExecutableNodeBase node, GraphContext context)
        {
            if (!isRecording)
            {
                return;
            }

            CaptureNodeSlots(node, context, SlotDirection.Output);
            CloseScope();
        }

        public static void OnBeforeTextureDispatch(string label, RenderTexture destination)
        {
            if (!isRecording)
            {
                return;
            }

            OpenScope(label, DebugScopeType.BlendDispatch);

            if (destination != null)
            {
                CaptureTexture($"{label} (dest before)", destination);
            }
        }

        public static void OnAfterTextureDispatch(string label, RenderTexture destination)
        {
            if (!isRecording)
            {
                return;
            }

            if (destination != null)
            {
                CaptureTexture($"{label} (dest after)", destination);
            }

            CloseScope();
        }

        public static void OnBeforeBufferDispatch(string label, ComputeBuffer buffer)
        {
            if (!isRecording)
            {
                return;
            }

            OpenScope(label, DebugScopeType.BlendDispatch);

            if (buffer != null)
            {
                RecordBufferEvent($"{label} (before)", buffer, DebugBufferInterpretation.Unknown);
            }
        }

        public static void OnAfterBufferDispatch(string label, ComputeBuffer buffer)
        {
            if (!isRecording)
            {
                return;
            }

            if (buffer != null)
            {
                RecordBufferEvent($"{label} (after)", buffer, DebugBufferInterpretation.Unknown);
            }

            CloseScope();
        }

        public static void Capture(string label, RenderTexture renderTexture)
        {
            if (!isRecording)
            {
                return;
            }
            if (renderTexture != null)
            {
                CaptureTexture(label, renderTexture);
            }
            else
            {
                CaptureString(label, "Render texture is null");
            }

        }

        public static void Capture(string label, IList<RenderTexture> renderTextures)
        {
            if (!isRecording)
            {
                return;
            }
            if (renderTextures != null && renderTextures.Count > 0)
            {
                CaptureTexture(label, renderTextures);
            }
            else
            {
                CaptureString(label, "Texture array is null or empty");
            }
        }

        public static void Capture(string label, IList<Texture> textures)
        {
            if (!isRecording)
            {
                return;
            }
            if (textures != null && textures.Count > 0)
            {
                CaptureTexture(label, textures);
            }
            else
            {
                CaptureString(label, "Texture array is null or empty");
            }
        }

        public static void Capture(string label, ComputeBuffer buffer)
        {
            Capture(label, buffer, DebugBufferInterpretation.Unknown);
        }

        public static void Capture(string label, ComputeBuffer buffer, DebugBufferInterpretation bufferInterpretation)
        {
            if (!isRecording)
            {
                return;
            }
            if (buffer != null)
            {
                RecordBufferEvent(label, buffer, bufferInterpretation);
            }
            else
            {
                CaptureString(label, "Buffer is null");
            }
        }

        public static void CaptureSeparator()
        {
            CaptureString(string.Empty, "\n--------------------------------\n");
        }

        public static void CaptureTexture(string label, params RenderTexture[] textures)
        {
            CaptureTextureInternal(label, textures);
        }

        public static void CaptureTexture(string label, IList<RenderTexture> textures)
        {
            if (textures == null)
            {
                CaptureTextureInternal(label, null);
            }
            else if (textures is IReadOnlyList<RenderTexture> readOnlyTextures)
            {
                CaptureTextureInternal(label, readOnlyTextures);
            }
            else
            {
                CaptureTextureInternal(label, new List<RenderTexture>(textures));
            }
        }

        public static void CaptureTexture(string label, IList<Texture> textures)
        {
            if (!isRecording)
            {
                return;
            }
            if (textures == null || textures.Count == 0)
            {
                CaptureString(label, "Texture array is null or empty");
                return;
            }

            bool unscoped = s_scopeStack.Count == 0;
            if (unscoped) OpenScope("Unscoped", DebugScopeType.Custom);

            DebugEvent captureEvent = new DebugEvent();
            captureEvent.type = DebugEventType.TextureCapture;
            captureEvent.timestamp = GetTimestamp();
            captureEvent.depth = s_scopeStack.Count;
            captureEvent.label = label;

            for (int i = 0; i < textures.Count; ++i)
            {
                captureEvent.textures.Add(RecordTextureCapture(textures[i]));
            }

            s_activeSession.events.Add(captureEvent);
            if (unscoped) CloseScope();
        }

        private static void CaptureTextureInternal(string label, IReadOnlyList<RenderTexture> textures)
        {
            if (!isRecording)
            {
                return;
            }

            if (textures == null || textures.Count == 0)
            {
                CaptureString(label, "Texture array is null or empty");
                return;
            }

            if (textures.Count == 1 && textures[0] == null)
            {
                CaptureString(label, "Render texture is null");
                return;
            }

            RecordTextureEvent(label, textures);
        }

        public static void CaptureString(string message)
        {
            CaptureString(null, message);
        }

        public static void CaptureString(string label, string message)
        {
            if (!isRecording)
            {
                return;
            }

            bool unscoped = s_scopeStack.Count == 0;
            if (unscoped) OpenScope("Unscoped", DebugScopeType.Custom);

            DebugEvent stringEvent = new DebugEvent();
            stringEvent.type = DebugEventType.StringCapture;
            stringEvent.timestamp = GetTimestamp();
            stringEvent.depth = s_scopeStack.Count;
            stringEvent.label = string.IsNullOrEmpty(label) ? "" : label;
            stringEvent.message = message ?? string.Empty;
            s_activeSession.events.Add(stringEvent);

            if (unscoped) CloseScope();
        }

        private static void CaptureSerializedAssets(ExecutableNodeBase node)
        {
            FieldInfo[] fields = node.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < fields.Length; ++i)
            {
                FieldInfo field = fields[i];
                if (field.GetCustomAttribute<SerializeAssetAttribute>() == null)
                {
                    continue;
                }

                Object asset = field.GetValue(node) as Object;
                string assetName = asset != null ? asset.name : "null";
                CaptureString(field.Name, assetName);
            }
        }

        private static void CaptureVariableName(ExecutableNodeBase node)
        {
            string varName = null;
            if (node is GetVariableNode getVariableNode)
            {
                varName = getVariableNode.varName;
            }
            else if (node is SetVariableNode setVariableNode)
            {
                varName = setVariableNode.varName;
            }

            if (varName != null)
            {
                CaptureString("Variable", string.IsNullOrEmpty(varName) ? "(empty)" : varName);
            }
        }

        private static void CaptureNodeSlots(
            ExecutableNodeBase node, GraphContext context, SlotDirection direction)
        {
            ISlot[] slots = direction == SlotDirection.Input
                ? node.GetInputSlots()
                : node.GetOutputSlots();

            if (slots == null)
            {
                return;
            }

            foreach (ISlot slot in slots)
            {
                SlotRef dataRef;
                if (direction == SlotDirection.Input)
                {
                    dataRef = context.GetInputLink(node.id, slot.id);
                    if (string.IsNullOrEmpty(dataRef.nodeId))
                    {
                        continue;
                    }
                }
                else
                {
                    dataRef = new SlotRef(node.id, slot.id);
                }

                RenderTexture renderTexture = context.GetTexture(dataRef);
                if (renderTexture != null)
                {
                    CaptureTexture($"{slot.name} ({direction})", renderTexture);
                    continue;
                }

                ComputeBuffer buffer = context.GetBuffer(dataRef);
                if (buffer != null)
                {
                    DebugBufferInterpretation interpretation = GetBufferInterpretation(node, slot, direction);
                    RecordBufferEvent($"{slot.name} ({direction})", buffer, interpretation);
                }
            }
        }

        private static DebugBufferInterpretation GetBufferInterpretation(
            ExecutableNodeBase node, ISlot slot, SlotDirection direction)
        {
            if (node is InstanceOutputNodeBase instanceOutputNode)
            {
                if (direction == SlotDirection.Input
                    && slot.id == instanceOutputNode.positionInputSlot.id)
                {
                    return DebugBufferInterpretation.PositionSample;
                }

                if (direction == SlotDirection.Output
                    && slot.id == instanceOutputNode.outputSlot.id)
                {
                    return DebugBufferInterpretation.InstanceSample;
                }

                return DebugBufferInterpretation.Unknown;
            }

            return DebugBufferInterpretation.PositionSample;
        }

        private static void RecordTextureEvent(string label, IReadOnlyList<RenderTexture> sourceTextures)
        {
            bool unscoped = s_scopeStack.Count == 0;
            if (unscoped) OpenScope("Unscoped", DebugScopeType.Custom);

            DebugEvent captureEvent = new DebugEvent();
            captureEvent.type = DebugEventType.TextureCapture;
            captureEvent.timestamp = GetTimestamp();
            captureEvent.depth = s_scopeStack.Count;
            captureEvent.label = label;

            for (int i = 0; i < sourceTextures.Count; ++i)
            {
                captureEvent.textures.Add(RecordTextureCapture(sourceTextures[i]));
            }

            s_activeSession.events.Add(captureEvent);
            if (unscoped) CloseScope();
        }

        private static DebugTextureCapture RecordTextureCapture(RenderTexture sourceRt)
        {
            DebugTextureCapture textureCapture = new DebugTextureCapture();
            if (sourceRt == null)
            {
                return textureCapture;
            }

            int capturedWidth = Mathf.Min(sourceRt.width, CAPTURE_RESOLUTION);
            int capturedHeight = Mathf.Min(sourceRt.height, CAPTURE_RESOLUTION);
            TextureFormat readbackFormat = GetReadbackFormat(sourceRt.format);

            RenderTexture scaledRt = RenderTexture.GetTemporary(capturedWidth, capturedHeight, 0, sourceRt.format);
            UnityEngine.Graphics.Blit(sourceRt, scaledRt);

            Texture2D texture2d = ReadRenderTexture(scaledRt, readbackFormat);
            RenderTexture.ReleaseTemporary(scaledRt);

            int fileIndex = s_activeSession.textureFileCount;
            s_activeSession.textureFileCount += 1;

            string filePath = s_activeSession.GetTextureFilePath(fileIndex);
            try
            {
                File.WriteAllBytes(filePath, texture2d.GetRawTextureData());
            }
            catch (IOException ex)
            {
                AbortSessionOnWriteFailure("texture", filePath, ex);
                if (Application.isPlaying)
                {
                    Object.Destroy(texture2d);
                }
                else
                {
                    Object.DestroyImmediate(texture2d);
                }
                return new DebugTextureCapture();
            }
            if (Application.isPlaying)
            {
                Object.Destroy(texture2d);
            }
            else
            {
                Object.DestroyImmediate(texture2d);
            }

            textureCapture.originalWidth = sourceRt.width;
            textureCapture.originalHeight = sourceRt.height;
            textureCapture.capturedWidth = capturedWidth;
            textureCapture.capturedHeight = capturedHeight;
            textureCapture.format = sourceRt.format.ToString();
            textureCapture.textureDataFormat = readbackFormat.ToString();
            textureCapture.textureFileIndex = fileIndex;
            return textureCapture;
        }

        private static DebugTextureCapture RecordTextureCapture(Texture sourceTexture)
        {
            if (sourceTexture == null)
            {
                return new DebugTextureCapture();
            }

            if (sourceTexture is RenderTexture renderTexture)
            {
                return RecordTextureCapture(renderTexture);
            }

            int width = sourceTexture.width;
            int height = sourceTexture.height;
            if (width <= 0 || height <= 0)
            {
                return new DebugTextureCapture();
            }

            RenderTexture tempRt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGBFloat);
            UnityEngine.Graphics.Blit(sourceTexture, tempRt);
            DebugTextureCapture textureCapture = RecordTextureCapture(tempRt);
            RenderTexture.ReleaseTemporary(tempRt);
            return textureCapture;
        }

        private static void RecordBufferEvent(
            string label, ComputeBuffer buffer, DebugBufferInterpretation bufferInterpretation)
        {
            bool unscoped = s_scopeStack.Count == 0;
            if (unscoped) OpenScope("Unscoped", DebugScopeType.Custom);
            int totalFloats = buffer.count * buffer.stride / sizeof(float);
            float[] data = new float[totalFloats];
            buffer.GetData(data);

            int fileIndex = s_activeSession.bufferFileCount;
            s_activeSession.bufferFileCount += 1;

            byte[] bytes = new byte[data.Length * sizeof(float)];
            System.Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
            string filePath = s_activeSession.GetBufferFilePath(fileIndex);
            try
            {
                File.WriteAllBytes(filePath, bytes);
            }
            catch (IOException ex)
            {
                AbortSessionOnWriteFailure("buffer", filePath, ex);
                return;
            }

            DebugEvent bufferEvent = new DebugEvent();
            bufferEvent.type = DebugEventType.BufferCapture;
            bufferEvent.timestamp = GetTimestamp();
            bufferEvent.depth = s_scopeStack.Count;
            bufferEvent.label = label;
            bufferEvent.elementCount = buffer.count;
            bufferEvent.stride = buffer.stride;
            bufferEvent.bufferFileIndex = fileIndex;
            bufferEvent.bufferInterpretation = bufferInterpretation;
            s_activeSession.events.Add(bufferEvent);
            if (unscoped) CloseScope();
        }

        private static void OnLogMessageReceived(string logMessage, string stackTrace, LogType logType)
        {
            if (!isRecording)
            {
                return;
            }

            bool unscoped = s_scopeStack.Count == 0;
            if (unscoped) OpenScope("Unscoped", DebugScopeType.Custom);
            DebugEvent logEvent = new DebugEvent();
            logEvent.type = DebugEventType.Log;
            logEvent.timestamp = GetTimestamp();
            logEvent.depth = s_scopeStack.Count;
            logEvent.message = logMessage;
            logEvent.stackTrace = stackTrace;
            logEvent.logType = logType;
            s_activeSession.events.Add(logEvent);
            if (unscoped) CloseScope();
        }

        private static float GetTimestamp()
        {
            return (Time.realtimeSinceStartup - s_sessionStartTime) * 1000f;
        }

        private static void AbortSessionOnWriteFailure(string captureType, string filePath, IOException exception)
        {
            if (!isRecording)
            {
                return;
            }

            string sessionDirectory = s_activeSession.sessionDirectory;
            Application.logMessageReceived -= OnLogMessageReceived;
            s_activeSession = null;
            s_scopeStack.Clear();

            string message =
                $"[VistaDebugger] Failed to save {captureType} capture to '{filePath}'. " +
                $"The debugger session was stopped. This usually means the LocalLow storage used for VistaDebug is full. " +
                $"Delete old folders under '{sessionDirectory}' or the parent VistaDebug folder, then run the capture again. " +
                $"Original error: {exception.Message}";
            Debug.LogError(message);
        }

        private static TextureFormat GetReadbackFormat(RenderTextureFormat rtFormat)
        {
            switch (rtFormat)
            {
                case RenderTextureFormat.RFloat: return TextureFormat.RFloat;
                case RenderTextureFormat.RGFloat: return TextureFormat.RGFloat;
                case RenderTextureFormat.ARGBFloat: return TextureFormat.RGBAFloat;
                case RenderTextureFormat.ARGBHalf: return TextureFormat.RGBAHalf;
                case RenderTextureFormat.RHalf: return TextureFormat.RHalf;
                default: return TextureFormat.RGBA32;
            }
        }

        private static Texture2D ReadRenderTexture(RenderTexture sourceRt, TextureFormat format)
        {
            Texture2D texture2d = new Texture2D(sourceRt.width, sourceRt.height, format, false, true);
            GraphicsUtils.ReadRenderTexture(sourceRt, texture2d);
            return texture2d;
        }

    }
}
#endif
