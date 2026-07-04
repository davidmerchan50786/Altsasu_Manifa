#if UNITY_EDITOR
using System;
using Convai.Modules.LipSync.Editor.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Convai.Tests.EditMode.Presentation
{
    public class LipSyncEditorUiDesignSystemTests
    {
        [Test]
        public void SectionStateStore_GetSet_RoundTrips()
        {
            string hostId = $"Host_{Guid.NewGuid():N}";
            string sectionId = "Core Setup";
            string key = ConvaiLipSyncSectionStateStore.BuildKey(hostId, sectionId);

            EditorPrefs.DeleteKey(key);
            Assert.IsFalse(ConvaiLipSyncSectionStateStore.Get(hostId, sectionId, false));

            ConvaiLipSyncSectionStateStore.Set(hostId, sectionId, true);
            Assert.IsTrue(ConvaiLipSyncSectionStateStore.Get(hostId, sectionId, false));

            ConvaiLipSyncSectionStateStore.Set(hostId, sectionId, false);
            Assert.IsFalse(ConvaiLipSyncSectionStateStore.Get(hostId, sectionId, true));

            EditorPrefs.DeleteKey(key);
        }

        [Test]
        public void SectionStateStore_BuildKey_NormalizesWhitespace()
        {
            string key = ConvaiLipSyncSectionStateStore.BuildKey("Map Debug Window", "Validation Results");
            Assert.AreEqual("Convai.LipSync.MapDebugWindow.ValidationResults.Expanded", key);
        }

        [Test]
        public void StyleCache_EnsureInitialized_ReusesStyleInstances()
        {
            ConvaiLipSyncEditorStyleCache.EnsureInitialized();
            GUIStyle firstHeader = ConvaiLipSyncEditorStyleCache.SectionHeaderLabelStyle;
            GUIStyle firstIcon = ConvaiLipSyncEditorStyleCache.SectionIconStyle;
            GUIStyle firstChevron = ConvaiLipSyncEditorStyleCache.SectionChevronStyle;

            ConvaiLipSyncEditorStyleCache.EnsureInitialized();

            Assert.AreSame(firstHeader, ConvaiLipSyncEditorStyleCache.SectionHeaderLabelStyle);
            Assert.AreSame(firstIcon, ConvaiLipSyncEditorStyleCache.SectionIconStyle);
            Assert.AreSame(firstChevron, ConvaiLipSyncEditorStyleCache.SectionChevronStyle);
        }

        [Test]
        public void IconProvider_GetConvaiIcon_ReturnsNonNullTexture()
        {
            Texture2D icon = ConvaiLipSyncIconProvider.GetConvaiIcon();
            Assert.IsNotNull(icon);
        }
    }
}
#endif
