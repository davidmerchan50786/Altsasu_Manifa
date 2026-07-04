#if VISTA
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Pinwheel.Vista;
using Pinwheel.Vista.Graphics;
using UnityEditor.Searcher;

namespace Pinwheel.VistaEditor.Graph
{
    public class NodeSearcherDatabase : SearcherDatabase
    {
        public delegate void EmptySearchResultHandler(NodeSearcherDatabase sender, string query);
        public event EmptySearchResultHandler emptySearchResultCallback;

        public NodeSearcherDatabase(IReadOnlyCollection<SearcherItem> db) : base(db)
        {
        }

        public override List<SearcherItem> Search(string query, out float localMaxScore)
        {
            List<SearcherItem> results = base.Search(query, out localMaxScore);
            if (results.Count == 0)
            {
                emptySearchResultCallback?.Invoke(this, query);
            }

            return results;
        }
    }
}
#endif
