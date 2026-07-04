#if VISTA
using System;
using UnityEditor;
using UnityEngine;

namespace Pinwheel.VistaEditor
{
    internal static class SplineProviderDetection
    {
        private readonly struct ProviderInfo
        {
            public readonly string TypeName;
            public readonly string DisplayName;

            public ProviderInfo(string typeName, string displayName)
            {
                TypeName = typeName;
                DisplayName = displayName;
            }
        }

        private static readonly ProviderInfo[] PROVIDERS = new ProviderInfo[]
        {
            new ProviderInfo("UnityEngine.Splines.SplineContainer", "Unity Splines"),
            new ProviderInfo("FluffyUnderware.Curvy.CurvySpline", "Curvy Splines"),
            new ProviderInfo("BezierSolution.BezierSpline", "Bezier Solution"),
            new ProviderInfo("Dreamteck.Splines.SplineComputer", "Dreamteck Splines"),
            new ProviderInfo("Pinwheel.Griffin.SplineTool.GSplineCreator", "Polaris Splines")
        };

        public static bool HasSupportedSplineComponent(GameObject gameObject)
        {
            string providerName;
            return TryGetSupportedSplineProvider(gameObject, out providerName);
        }

        public static bool TryGetSupportedSplineProvider(GameObject gameObject, out string providerName)
        {
            providerName = null;
            if (gameObject == null)
            {
                return false;
            }

            MonoBehaviour[] behaviours = gameObject.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; ++i)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null)
                {
                    continue;
                }

                string fullName = behaviour.GetType().FullName;
                if (string.IsNullOrEmpty(fullName))
                {
                    continue;
                }

                for (int j = 0; j < PROVIDERS.Length; ++j)
                {
                    ProviderInfo provider = PROVIDERS[j];
                    if (string.Equals(fullName, provider.TypeName, StringComparison.Ordinal))
                    {
                        providerName = provider.DisplayName;
                        return true;
                    }
                }
            }

            return false;
        }
    }

    [InitializeOnLoad]
    internal static class SplineSelectionTracker
    {
        public static GameObject SelectedGameObject { get; private set; }
        public static bool HasSupportedSplineSelection { get; private set; }
        public static string SelectedProviderName { get; private set; }

        static SplineSelectionTracker()
        {
            Selection.selectionChanged += OnSelectionChanged;
            OnSelectionChanged();
        }

        private static void OnSelectionChanged()
        {
            SelectedGameObject = Selection.activeGameObject;
            HasSupportedSplineSelection =
                SplineProviderDetection.TryGetSupportedSplineProvider(
                    SelectedGameObject,
                    out string providerName);
            SelectedProviderName = providerName;
        }
    }
}
#endif
