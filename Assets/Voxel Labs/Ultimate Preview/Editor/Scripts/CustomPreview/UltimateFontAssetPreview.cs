#if UNITY_6000_0_OR_NEWER
using System;
using UnityEditor;
using UnityEngine.TextCore.Text;
using VoxelLabs.UltimatePreview.Core;

namespace VoxelLabs.UltimatePreview.CustomPreview
{
    [CustomPreview(typeof(FontAsset))]
    internal class UltimateFontAssetPreview : UltimateFontPreviewBase
    {
        protected override Type GetOriginalEditorType()
        {
            return typeof(FontAsset);
        }
    }
}
#endif