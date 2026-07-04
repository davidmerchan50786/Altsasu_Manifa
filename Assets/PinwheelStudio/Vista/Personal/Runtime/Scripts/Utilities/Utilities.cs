#if VISTA
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Pinwheel.Vista
{
    /// <summary>
    /// Collects small shared helpers used across Vista runtime code for value swapping, texture conversion, resampling, and coordinate mapping.
    /// </summary>
    public static class Utilities
    {
        /// <summary>
        /// Swaps two values in place.
        /// </summary>
        /// <typeparam name="T">Type of the values being swapped.</typeparam>
        /// <param name="a">First value to swap.</param>
        /// <param name="b">Second value to swap.</param>
        public static void Swap<T>(ref T a, ref T b)
        {
            T tmp = a;
            a = b;
            b = tmp;
        }

        /// <summary>
        /// Swaps two elements inside the same array.
        /// </summary>
        /// <typeparam name="T">Element type stored in the array.</typeparam>
        /// <param name="array">Array containing both elements.</param>
        /// <param name="i0">Index of the first element.</param>
        /// <param name="i1">Index of the second element.</param>
        public static void Swap<T>(ref T[] array, int i0, int i1)
        {
            T tmp = array[i0];
            array[i0] = array[i1];
            array[i1] = tmp;
        }

        /// <summary>
        /// Creates a material from a shader resolved through <see cref="ShaderUtilities.Find(string)"/>.
        /// </summary>
        /// <param name="shaderName">Name of the shader to look up.</param>
        /// <returns>A new material if the shader is found; otherwise <see langword="null"/>.</returns>
        public static Material CreateMaterial(string shaderName)
        {
            Shader shader = ShaderUtilities.Find(shaderName);
            Material material;
            if (shader != null)
            {
                material = new Material(shader);
            }
            else
            {
                material = null;
            }
            return material;
        }

        /// <summary>
        /// Bakes an <see cref="AnimationCurve"/> into a one-dimensional floating-point texture.
        /// </summary>
        /// <param name="curve">Curve to sample from left to right.</param>
        /// <param name="resolution">Number of texels to generate.</param>
        /// <returns>A clamp-wrapped bilinear-filtered <see cref="Texture2D"/> containing sampled curve values in all color channels.</returns>
        public static Texture2D TextureFromCurve(AnimationCurve curve, int resolution = 2048)
        {
            Texture2D tex = new Texture2D(resolution, 1, TextureFormat.RGBAFloat, false, true);
            Color[] colors = new Color[resolution];
            for (int i = 0; i < resolution; ++i)
            {
                float f = i * 1.0f / (resolution - 1);
                Color c = Color.white * curve.Evaluate(f);
                colors[i] = c;
            }
            tex.SetPixels(colors);
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            return tex;
        }

        /// <summary>
        /// Creates the hardcoded four-key curve Vista uses for a simple edge falloff profile.
        /// </summary>
        public static AnimationCurve EaseInOutCurve()
        {
            Keyframe[] keys = new Keyframe[]
            {
                new Keyframe(0,0),
                new Keyframe(0.1f, 1),
                new Keyframe(0.9f, 1),
                new Keyframe(1,0)
            };
            return new AnimationCurve(keys);
        }

        /// <summary>
        /// Creates a two-key gradient that interpolates from <paramref name="start"/> to <paramref name="end"/>.
        /// </summary>
        /// <param name="start">Color at time 0.</param>
        /// <param name="end">Color at time 1.</param>
        public static Gradient CreateGradient(Color start, Color end)
        {
            Gradient g = new Gradient();
            GradientColorKey startColor = new GradientColorKey() { time = 0, color = start };
            GradientColorKey endColor = new GradientColorKey() { time = 1, color = end };
            g.colorKeys = new GradientColorKey[] { startColor, endColor };
            return g;
        }

        /// <summary>
        /// Bakes a <see cref="Gradient"/> into a one-dimensional floating-point texture.
        /// </summary>
        /// <param name="gradient">Gradient to sample from left to right.</param>
        /// <param name="resolution">Number of texels to generate.</param>
        /// <returns>A clamp-wrapped bilinear-filtered <see cref="Texture2D"/> containing sampled gradient colors.</returns>
        public static Texture2D TextureFromGradient(Gradient gradient, int resolution = 2048)
        {
            Texture2D tex = new Texture2D(resolution, 1, TextureFormat.RGBAFloat, false, true);
            Color[] colors = new Color[resolution];
            for (int i = 0; i < resolution; ++i)
            {
                float f = i * 1.0f / (resolution - 1);
                Color c = gradient.Evaluate(f);
                colors[i] = c;
            }
            tex.SetPixels(colors);
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            return tex;
        }

        /// <summary>
        /// Enables or disables a shader keyword on a material.
        /// </summary>
        /// <param name="mat">Material to modify.</param>
        /// <param name="kw">Keyword name.</param>
        /// <param name="enable"><see langword="true"/> to enable the keyword; <see langword="false"/> to disable it.</param>
        public static void SetKeywordEnable(this Material mat, string kw, bool enable)
        {
            if (enable)
            {
                mat.EnableKeyword(kw);
            }
            else
            {
                mat.DisableKeyword(kw);
            }
        }

        /// <summary>
        /// Fills every element of an array with the same item.
        /// </summary>
        /// <typeparam name="T">Element type stored in the array.</typeparam>
        /// <param name="array">Array to overwrite.</param>
        /// <param name="value">Value assigned to every element.</param>
        public static void Fill<T>(this T[] array, T value)
        {
            for (int i = 0; i < array.Length; ++i)
            {
                array[i] = value;
            }
        }

        /// <summary>
        /// Appends the same item to a list multiple times.
        /// </summary>
        /// <typeparam name="T">Element type stored in the list.</typeparam>
        /// <param name="list">List to append to.</param>
        /// <param name="value">Value to append.</param>
        /// <param name="count">Number of times to append <paramref name="value"/>.</param>
        public static void AddRepeated<T>(this IList<T> list, T value, int count)
        {
            for (int i = 0; i < count; ++i)
            {
                list.Add(value);
            }
        }

        /// <summary>
        /// Finds the index of a terrain layer inside <see cref="TerrainData.terrainLayers"/>.
        /// </summary>
        /// <param name="data">Terrain data to search.</param>
        /// <param name="layer">Layer instance to look for.</param>
        /// <returns>The matching index, or <c>-1</c> if the layer is not assigned.</returns>
        public static int GetLayerIndex(this TerrainData data, TerrainLayer layer)
        {
            TerrainLayer[] layers = data.terrainLayers;
            for (int i = 0; i < layers.Length; ++i)
            {
                if (layer == layers[i])
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Rounds a value up to the next multiple of 8.
        /// </summary>
        /// <param name="value">Value to normalize.</param>
        public static int MultipleOf8(int value)
        {
            int m = Mathf.CeilToInt(value / 8.0f);
            return m * 8;
        }

        /// <summary>
        /// Computes the inverse interpolation factor without clamping the result to the 0..1 range.
        /// </summary>
        /// <param name="a">Start of the source range.</param>
        /// <param name="b">End of the source range.</param>
        /// <param name="t">Sample value to convert into a normalized factor.</param>
        /// <returns>The unclamped interpolation factor, or 0 when <paramref name="a"/> equals <paramref name="b"/>.</returns>
        public static float InverseLerpUnclamped(float a, float b, float t)
        {
            if (a == b)
            {
                return 0;
            }
            else
            {
                return (t - a) / (b - a);
            }
        }

        /// <summary>
        /// Computes the perpendicular distance from a point to the infinite line passing through two points.
        /// </summary>
        /// <param name="l1">First point on the line.</param>
        /// <param name="l2">Second point on the line.</param>
        /// <param name="p">Point to measure from.</param>
        public static float DistancePointToLine(Vector2 l1, Vector2 l2, Vector2 p)
        {
            float num = Mathf.Abs((l2.x - l1.x) * (l1.y - p.y) - (l1.x - p.x) * (l2.y - l1.y));
            float denom = Mathf.Sqrt((l2.x - l1.x) * (l2.x - l1.x) + (l2.y - l1.y) * (l2.y - l1.y));
            return num / denom;
        }

        /// <summary>
        /// Drops the Y component of a 3D vector and returns its XZ coordinates.
        /// </summary>
        /// <param name="v">Source vector.</param>
        public static Vector2 XZ(this Vector3 v)
        {
            return new Vector2(v.x, v.z);
        }

        /// <summary>
        /// Creates an <see cref="TextureFormat.RFloat"/> texture from a linear array of scalar values.
        /// </summary>
        /// <param name="data">Source float data in row-major order.</param>
        /// <param name="width">Texture width.</param>
        /// <param name="height">Texture height.</param>
        /// <returns>A clamp-wrapped bilinear-filtered floating-point texture containing the supplied values.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="data"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">Thrown when the dimensions are invalid or do not match the data length.</exception>
        public static Texture2D TextureFromFloats(float[] data, int width, int height)
        {
            if (data == null)
                throw new ArgumentNullException("data");
            if (width <= 0 || height <= 0)
                throw new ArgumentException("Width & Height must >0");
            if (data.Length != width * height)
                throw new ArgumentException("Data & Dimension not match");

            Texture2D tex = new Texture2D(width, height, TextureFormat.RFloat, false, true);
            tex.SetPixelData<float>(data, 0);
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            return tex;
        }

        /// <summary>
        /// Extracts the red channel of an <see cref="TextureFormat.RFloat"/> texture into a float array.
        /// </summary>
        /// <param name="tex">Source texture.</param>
        /// <returns>A float array containing one scalar per pixel.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="tex"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">Thrown when the texture format is not <see cref="TextureFormat.RFloat"/>.</exception>
        public static float[] FloatsFromTexture(Texture2D tex)
        {
            if (tex == null)
                throw new ArgumentNullException("tex");
            if (tex.format != TextureFormat.RFloat)
                throw new ArgumentException("Texture format must be RFloat");

            Color[] colors = tex.GetPixels();
            float[] data = new float[colors.Length];
            for (int i = 0; i < data.Length; ++i)
            {
                data[i] = colors[i].r;
            }
            return data;
        }

        /// <summary>
        /// Converts a point in rect space into normalized coordinates relative to that rect.
        /// </summary>
        /// <param name="r">Reference rect.</param>
        /// <param name="p">Point expressed in the same space as <paramref name="r"/>.</param>
        public static Vector2 PointToNormalized(Rect r, Vector2 p)
        {
            return new Vector2(InverseLerpUnclamped(r.min.x, r.max.x, p.x), InverseLerpUnclamped(r.min.y, r.max.y, p.y));
        }

        /// <summary>
        /// Converts normalized coordinates back into a point inside or outside a rect.
        /// </summary>
        /// <param name="r">Reference rect.</param>
        /// <param name="p">Normalized coordinates, not clamped to the 0..1 range.</param>
        public static Vector2 NormalizedToPoint(Rect r, Vector2 p)
        {
            return new Vector2(Mathf.LerpUnclamped(r.min.x, r.max.x, p.x), Mathf.LerpUnclamped(r.min.y, r.max.y, p.y));
        }

        /// <summary>
        /// Resamples scalar image data by uploading it to an RFloat texture, blitting it, and reading the result back.
        /// </summary>
        /// <param name="data">Source float data in row-major order.</param>
        /// <param name="width">Source width.</param>
        /// <param name="height">Source height.</param>
        /// <param name="newWidth">Destination width.</param>
        /// <param name="newHeight">Destination height.</param>
        /// <returns>A new float array containing the bilinearly resampled data.</returns>
        /// <exception cref="ArgumentException">Thrown when the source dimensions do not match the source data length.</exception>
        public static float[] ResampleBilinear(float[] data, int width, int height, int newWidth, int newHeight)
        {
            if (data.Length != width * height)
            {
                throw new ArgumentException("Source data dimension mismatched");
            }

            Texture2D srcTex2D = TextureFromFloats(data, width, height);
            RenderTexture targetRt = RenderTexture.GetTemporary(newWidth, newHeight, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);
            Graphics.Drawing.Blit(srcTex2D, targetRt);

            Texture2D dstTex2D = new Texture2D(newWidth, newHeight, TextureFormat.RFloat, false, true);
            GraphicsUtils.ReadRenderTexture(targetRt, dstTex2D);

            float[] newData = FloatsFromTexture(dstTex2D);

            RenderTexture.ReleaseTemporary(targetRt);
            Object.DestroyImmediate(srcTex2D);
            Object.DestroyImmediate(dstTex2D);

            return newData;
        }

        /// <summary>
        /// Synchronously runs an <see cref="System.Collections.IEnumerator"/> coroutine to completion on the calling thread.
        /// Nested coroutines produced by <c>yield return someIEnumerator</c> are drained recursively.
        /// </summary>
        /// <param name="coroutine">The coroutine to drain.</param>
        public static void DrainCoroutine(System.Collections.IEnumerator coroutine)
        {
            while (coroutine.MoveNext())
            {
                if (coroutine.Current is System.Collections.IEnumerator nested)
                {
                    DrainCoroutine(nested);
                }
            }
        }

        public static string GenerateId(int length = 8)
        {
            if (length <= 0)
                throw new System.ArgumentException("Id length must be greater than zero.", nameof(length));
            const string ALLOWED_CHARS = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

            char[] result = new char[length];
            byte[] randomBytes = new byte[length];

            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(randomBytes);
            }

            for (int i = 0; i < length; i++)
            {
                result[i] = ALLOWED_CHARS[randomBytes[i] % ALLOWED_CHARS.Length];
            }

            return new string(result);
        }

        

    }
}
#endif


