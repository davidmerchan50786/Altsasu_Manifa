#if VISTA

namespace Pinwheel.VistaEditor.Graph
{
    public class BiomeMaskGraphAdapter2 : IGraphAdapter
    {
        private GraphEditorBase m_editor;

        public ISearcherProvider searcherProvider { get; private set; }
        public ICommandHandler commandHandler { get; private set; }
        public IGraphPropertiesDrawer propertiesDrawer { get; private set; }

        public void Init(GraphEditorBase editor)
        {
            this.m_editor = editor;
            SearcherProvider sp = new SearcherProvider();
            //sp.SetSubGraphTypes(typeof(TerrainGraph), typeof(TerrainSubGraphNode));
            searcherProvider = sp;

            TerrainGraphCommandHandler ch = new TerrainGraphCommandHandler();
            ch.editor = m_editor;
            commandHandler = ch;

            TerrainGraphPropertiesDrawer2 pd = new TerrainGraphPropertiesDrawer2();
            pd.editor = m_editor;
            propertiesDrawer = pd;
        }
    }
}
#endif
