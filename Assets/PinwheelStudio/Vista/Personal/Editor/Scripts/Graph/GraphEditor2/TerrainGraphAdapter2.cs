#if VISTA

namespace Pinwheel.VistaEditor.Graph
{
    public class TerrainGraphAdapter2 : TerrainGraphAdapter
    {
        public override void Init(GraphEditorBase editor)
        {
            base.Init(editor);

            TerrainGraphPropertiesDrawer2 pd = new TerrainGraphPropertiesDrawer2();
            pd.editor = m_editor;
            propertiesDrawer = pd;
        }
    }
}
#endif
