using UnityEngine;
using LiveKit.Proto;
using UnityEngine.Rendering;
using Unity.Collections;
using UnityEngine.Experimental.Rendering;
using System;

namespace LiveKit
{
    public class TextureVideoSource : RtcVideoSource
    {
        TextureFormat _textureFormat;

        public Texture Texture { get; }

        public override int GetWidth()
        {
            return Texture.width;
        }

        public override int GetHeight()
        {
            return Texture.height;
        }

        protected override VideoRotation GetVideoRotation()
        {
            return VideoRotation._0;
        }

        public TextureVideoSource(Texture texture, int targetFrameRate = 30,
            VideoBufferType bufferType = VideoBufferType.Rgba) : base(VideoStreamSource.Texture, bufferType)
        {
            Texture = texture;
            TargetFrameRate = targetFrameRate > 0 ? targetFrameRate : 30;
            base.Init();
        }

        ~TextureVideoSource()
        {
            Dispose(false);
        }

        protected override bool ReadBuffer()
        {
            if (_reading)
                return false;
            _reading = true;
            var textureChanged = false;

            if (_previewTexture == null || _previewTexture.width != GetWidth() || _previewTexture.height != GetHeight()) {
                var compatibleFormat = SystemInfo.GetCompatibleFormat(Texture.graphicsFormat, GraphicsFormatUsage.ReadPixels);
                _textureFormat = GraphicsFormatUtility.GetTextureFormat(compatibleFormat);
                _bufferType = GetVideoBufferType(_textureFormat);
                if (_captureBuffer.IsCreated)
                    _captureBuffer.Dispose();
                if (_previewTexture != null)
                    UnityEngine.Object.Destroy(_previewTexture);
                _captureBuffer = new NativeArray<byte>(GetWidth() * GetHeight() * GetStrideForBuffer(_bufferType), Allocator.Persistent);
                _previewTexture = new Texture2D(GetWidth(), GetHeight(), _textureFormat, false);
                textureChanged = true;
            }
            Graphics.CopyTexture(Texture, _previewTexture);
            AsyncGPUReadback.RequestIntoNativeArray(ref _captureBuffer, _previewTexture, 0, _textureFormat, OnReadback);
            return textureChanged;
        }
    }
}
