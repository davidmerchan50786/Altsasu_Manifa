#if VISTA
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Pinwheel.Vista;
using Pinwheel.Vista.Graphics;
using Pinwheel.Vista.Graph;
using System;
using System.Reflection;

namespace Pinwheel.VistaEditor.Graph
{
    public class SearcherCandidatePlaceholder
    {
        public static bool ShouldShowInCurrentEdition(Type placeholderType)
        {
            SearcherCandidatePlaceholder instance = Activator.CreateInstance(placeholderType) as SearcherCandidatePlaceholder;
            return instance.ShouldShow();
        }

        protected virtual bool ShouldShow()
        {
            return true;
        }

        public virtual void OnCommit(Rect activatorRect)
        {
        }
    }

    [NodeMetadata(
        path = "Subgraph [?]",
        description = "Subgraph helps you embed a terrain graph inside another as reusable building blocks. Available in Indie and Pro.\n\n<b>Press Enter to see more.</b>")]
    public class SubgraphPlaceholder : SearcherCandidatePlaceholder
    {
        protected override bool ShouldShow()
        {
            return EditorCommon.IsPersonalEdition();
        }

        public override void OnCommit(Rect activatorRect)
        {
            HowSubgraphWorksPopupContent popupContent = new HowSubgraphWorksPopupContent();
            UnityEditor.PopupWindow.Show(activatorRect, popupContent);
        }
    }


    [NodeMetadata(
        path = "Data/Real World Data [?]",
        description = "Bring real world elevation and imagery into your graph in Vista Pro, so you can turn a real place into a terrain workflow faster.\n\n<b>Press Enter to see how it works.<b>",
        keywords = "dem, satellite, imagery, earth, location, gis, map, geo, import")]
    public class RealWorldDataPlaceholder : SearcherCandidatePlaceholder
    {
        protected override bool ShouldShow()
        {
            return !EditorCommon.IsProEdition();
        }

        public override void OnCommit(Rect activatorRect)
        {
            HowRwdWorksPopupContent popupContent = new HowRwdWorksPopupContent();
            UnityEditor.PopupWindow.Show(activatorRect, popupContent);
        }
    }

    [NodeMetadata(
        path = "General/Spline [?]",
        description = "Use scene splines to drive terrain generation in Vista Pro. Paint paths, conform height, extract spline data, or clear instances along roads, rivers, and other linear features.\n\n<b>Press Enter to see how it works.<b>",
        keywords = "spline, path, road, river, trail, ramp, curve, bezier, route, corridor, conform, line")]
    public class SplinePlaceholder : SearcherCandidatePlaceholder
    {
        protected override bool ShouldShow()
        {
            return !EditorCommon.IsProEdition();
        }

        public override void OnCommit(Rect activatorRect)
        {
            HowSplineWorksPopupContent popupContent = new HowSplineWorksPopupContent();
            UnityEditor.PopupWindow.Show(activatorRect, popupContent);
        }
    }
}
#endif
