#if VISTA
using Pinwheel.Vista.Graph;
using System.Collections.Generic;

namespace Pinwheel.VistaEditor.Graph
{
    public class BiomeMaskGraphEditor2 : TerrainGraphEditor2
    {
        protected override void SetupAdapter()
        {
            BiomeMaskGraphAdapter2 adapter = new BiomeMaskGraphAdapter2();
            adapter.Init(this);
            m_adapter = adapter;
        }
    }
}
#endif
