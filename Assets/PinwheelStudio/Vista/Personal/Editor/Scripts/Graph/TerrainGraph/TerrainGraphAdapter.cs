#if VISTA

namespace Pinwheel.VistaEditor.Graph
{
    public class TerrainGraphAdapter : IGraphAdapter
    {
        internal delegate void ConfigSubGraphHandler(TerrainGraphAdapter adapter, SearcherProvider searcherProvider);
        internal static event ConfigSubGraphHandler configSubGraphCallback;

        protected GraphEditorBase m_editor;

        public ISearcherProvider searcherProvider { get; protected set; }
        public ICommandHandler commandHandler { get; protected set; }
        public IGraphPropertiesDrawer propertiesDrawer { get; protected set; }

        public virtual void Init(GraphEditorBase editor)
        {
            this.m_editor = editor;
            SearcherProvider sp = new SearcherProvider();
                
            if (configSubGraphCallback != null)
            {
                configSubGraphCallback.Invoke(this, sp);
            }
            searcherProvider = sp;

            TerrainGraphCommandHandler ch = new TerrainGraphCommandHandler();
            ch.editor = m_editor;
            commandHandler = ch;

            TerrainGraphPropertiesDrawer pd = new TerrainGraphPropertiesDrawer();
            pd.editor = m_editor;
            propertiesDrawer = pd;
        }
    }
}
#endif
