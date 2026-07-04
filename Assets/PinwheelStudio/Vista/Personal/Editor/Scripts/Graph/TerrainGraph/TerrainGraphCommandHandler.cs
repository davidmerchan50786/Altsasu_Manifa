#if VISTA
using UnityEditor;
using Pinwheel.Vista.Graph;
using System.Collections.Generic;

namespace Pinwheel.VistaEditor.Graph
{
    public class TerrainGraphCommandHandler : GraphCommandHandlerBase<TerrainGraph>
    {
        protected override void CopyGraphData(TerrainGraph from, TerrainGraph to)
        {
            string toName = to.name;
            string json = EditorJsonUtility.ToJson(from);
            EditorJsonUtility.FromJsonOverwrite(json, to);
            to.name = toName;
        }
    }
}
#endif
