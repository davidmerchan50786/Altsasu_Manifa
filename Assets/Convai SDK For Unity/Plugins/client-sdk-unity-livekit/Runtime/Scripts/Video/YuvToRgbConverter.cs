using System;
using UnityEngine;

namespace LiveKit
{
	internal sealed class YuvToRgbConverter : IDisposable
	{
		public bool UseGpuShader { get; set; } = true;
		public RenderTexture Output { get; private set; }

		private Material _yuvToRgbMaterial;
		private Texture2D _planeY;
		private Texture2D _planeU;
		private Texture2D _planeV;

		public bool EnsureOutput(int width, int height)
		{
			var changed = false;
			if (Output == null || Output.width != width || Output.height != height)
			{
				if (Output != null)
				{
					Output.Release();
					UnityEngine.Object.Destroy(Output);
				}
				Output = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
				Output.Create();
				changed = true;
			}
			return changed;
		}

		public void Convert(VideoFrameBuffer buffer)
		{
			if (buffer == null || !buffer.IsValid)
				return;

			int width = (int)buffer.Width;
			int height = (int)buffer.Height;

			EnsureOutput(width, height);

			if (UseGpuShader)
			{
				EnsureGpuMaterial();
				EnsureYuvPlaneTextures(width, height);
				UploadYuvPlanes(buffer);

				if (_yuvToRgbMaterial != null)
				{
					GpuConvertToRenderTarget();
					return;
				}
			}

			CpuConvertToRenderTarget(buffer, width, height);
		}

		public void Dispose()
		{
			if (_planeY != null) UnityEngine.Object.Destroy(_planeY);
			if (_planeU != null) UnityEngine.Object.Destroy(_planeU);
			if (_planeV != null) UnityEngine.Object.Destroy(_planeV);
			if (Output != null)
			{
				Output.Release();
				UnityEngine.Object.Destroy(Output);
			}
			if (_yuvToRgbMaterial != null) UnityEngine.Object.Destroy(_yuvToRgbMaterial);
		}

		private void EnsureGpuMaterial()
		{
			if (_yuvToRgbMaterial == null)
			{
				var shader = Shader.Find("Hidden/LiveKit/YUV2RGB");
				if (shader != null)
					_yuvToRgbMaterial = new Material(shader);
			}
		}

		private static void EnsurePlaneTexture(ref Texture2D tex, int width, int height, TextureFormat format, FilterMode filterMode)
		{
			if (tex == null || tex.width != width || tex.height != height)
			{
				if (tex != null) UnityEngine.Object.Destroy(tex);
				tex = new Texture2D(width, height, format, false, true);
				tex.filterMode = filterMode;
				tex.wrapMode = TextureWrapMode.Clamp;
			}
		}

		private void EnsureYuvPlaneTextures(int width, int height)
		{
			EnsurePlaneTexture(ref _planeY, width, height, TextureFormat.R8, FilterMode.Bilinear);
			var chromaW = width / 2;
			var chromaH = height / 2;
			EnsurePlaneTexture(ref _planeU, chromaW, chromaH, TextureFormat.R8, FilterMode.Bilinear);
			EnsurePlaneTexture(ref _planeV, chromaW, chromaH, TextureFormat.R8, FilterMode.Bilinear);
		}

		private void UploadYuvPlanes(VideoFrameBuffer buffer)
		{
			var info = buffer.Info;
			if (info.Components.Count < 3) return;
			var yComp = info.Components[0];
			var uComp = info.Components[1];
			var vComp = info.Components[2];

			_planeY.LoadRawTextureData((IntPtr)yComp.DataPtr, (int)yComp.Size);
			_planeY.Apply(false, false);
			_planeU.LoadRawTextureData((IntPtr)uComp.DataPtr, (int)uComp.Size);
			_planeU.Apply(false, false);
			_planeV.LoadRawTextureData((IntPtr)vComp.DataPtr, (int)vComp.Size);
			_planeV.Apply(false, false);
		}

		private void CpuConvertToRenderTarget(VideoFrameBuffer buffer, int width, int height)
		{
			var rgba = buffer.ToRGBA();
			var tempTex = new Texture2D(width, height, TextureFormat.RGBA32, false);
			try
			{
				tempTex.LoadRawTextureData((IntPtr)rgba.Info.DataPtr, (int)rgba.GetMemorySize());
				tempTex.Apply();
				Graphics.Blit(tempTex, Output);
			}
			finally
			{
				UnityEngine.Object.Destroy(tempTex);
				rgba.Dispose();
			}
		}

		private void GpuConvertToRenderTarget()
		{
			_yuvToRgbMaterial.SetTexture("_TexY", _planeY);
			_yuvToRgbMaterial.SetTexture("_TexU", _planeU);
			_yuvToRgbMaterial.SetTexture("_TexV", _planeV);
			Graphics.Blit(Texture2D.blackTexture, Output, _yuvToRgbMaterial);
		}
	}
}

