#if VISTA
using System;
using System.Collections.Generic;
using UnityEngine;
using System.Reflection;
using Pinwheel.Vista.ExposeProperty;

namespace Pinwheel.Vista.Graph
{
    /// <summary>
    /// Stores the serialized structure and shared mutation logic for a Vista graph asset.
    /// </summary>
    /// <remarks>
    /// <see cref="GraphAsset"/> is the core persistence layer of the graph system. It owns the serialized node, edge,
    /// grouping, sticky-note, sticky-image, object-reference, and exposed-property state for a graph, and rebuilds the
    /// runtime object model from that serialized data after deserialization. Concrete graph types such as
    /// <see cref="TerrainGraph"/> build execution behavior on top of this backbone.
    /// </remarks>
    public abstract partial class GraphAsset : ScriptableObject, ISerializationCallbackReceiver
    {
        /// <summary>
        /// Represents a callback raised when a graph asset reports that its structure or settings have changed.
        /// </summary>
        /// <param name="sender">The graph asset that raised the change event.</param>
        public delegate void ChangeHandler(GraphAsset sender);
        /// <summary>
        /// Occurs when a graph explicitly broadcasts that it has changed.
        /// </summary>
        /// <remarks>
        /// Consumers such as <see cref="LocalProceduralBiome"/> use this event to invalidate cached biome data when their
        /// source graph changes.
        /// </remarks>
        public static event ChangeHandler graphChanged;

        [System.NonSerialized]
        internal protected List<INode> m_nodes = new List<INode>();
        [SerializeField]
        protected List<Serializer.JsonObject> m_nodeData;

        [System.NonSerialized]
        internal protected List<IEdge> m_edges = new List<IEdge>();
        [SerializeField]
        protected List<Serializer.JsonObject> m_edgeData;

        [System.NonSerialized]
        internal protected List<IGroup> m_groups = new List<IGroup>();
        [SerializeField]
        protected List<Serializer.JsonObject> m_groupData;

        [System.NonSerialized]
        internal protected List<IStickyNote> m_stickyNotes = new List<IStickyNote>();
        [SerializeField]
        protected List<Serializer.JsonObject> m_stickyNoteData;

        [System.NonSerialized]
        internal protected List<IStickyImage> m_stickyImages = new List<IStickyImage>();
        [SerializeField]
        protected List<Serializer.JsonObject> m_stickyImageData;

        [SerializeField]
        protected List<ObjectReference> m_objectReferences;

        [SerializeField]
        [HideInInspector]
        internal List<PropertyDescriptor> m_exposedProperties;

        /// <summary>
        /// Gets whether this graph currently contains any exposed properties.
        /// </summary>
        /// <remarks>
        /// Exposed-property metadata is stored in <see cref="m_exposedProperties"/> and is used by the optional expose
        /// property extension for inspector-driven graph overrides.
        /// </remarks>
        public bool HasExposedProperties
        {
            get
            {
                return m_exposedProperties != null && m_exposedProperties.Count > 0;
            }
        }

        /// <summary>
        /// Reinitializes all graph collections to an empty graph state.
        /// </summary>
        /// <remarks>
        /// This clears both runtime object collections and their serialized backing lists, along with object-reference and
        /// exposed-property metadata.
        /// </remarks>
        public virtual void Reset()
        {
            m_nodes = new List<INode>();
            m_edges = new List<IEdge>();
            m_groups = new List<IGroup>();
            m_stickyNotes = new List<IStickyNote>();
            m_stickyImages = new List<IStickyImage>();
            m_nodeData = new List<Serializer.JsonObject>();
            m_edgeData = new List<Serializer.JsonObject>();
            m_groupData = new List<Serializer.JsonObject>();
            m_stickyNoteData = new List<Serializer.JsonObject>();
            m_stickyImageData = new List<Serializer.JsonObject>();
            m_objectReferences = new List<ObjectReference>();
            m_exposedProperties = new List<PropertyDescriptor>();
        }

        protected void Awake()
        {
            //Reset();
        }

        protected virtual void OnEnable()
        {
        }

        protected virtual void OnDisable()
        {
        }

        /// <summary>
        /// Serializes the runtime graph model into Unity-serializable backing data.
        /// </summary>
        /// <remarks>
        /// Nodes, edges, groups, sticky notes, and sticky images are serialized into JSON-object lists. Unity object fields
        /// marked with <see cref="SerializeAssetAttribute"/> are captured separately in <see cref="m_objectReferences"/>
        /// because the generic serializer works on graph objects rather than Unity asset references directly. Nodes that
        /// implement <see cref="ISerializationCallbackReceiver"/> receive their own pre-serialize callback before node data is
        /// captured.
        /// </remarks>
        public virtual void OnBeforeSerialize()
        {
            using (Serializer.TargetScope s = new Serializer.TargetScope(this))
            {
                m_objectReferences = new List<ObjectReference>();
                if (m_nodes != null)
                {
                    foreach (INode node in m_nodes)
                    {
                        SerializeAssetReferences(m_objectReferences, node);

                        if (node is ISerializationCallbackReceiver serializeReceiver)
                        {
                            serializeReceiver.OnBeforeSerialize();
                        }
                    }
                    m_nodeData = Serializer.Serialize<INode>(m_nodes);
                }
                else
                {
                    m_nodeData = new List<Serializer.JsonObject>();
                }

                if (m_edges != null)
                {
                    m_edgeData = Serializer.Serialize<IEdge>(m_edges);
                }
                else
                {
                    m_edgeData = new List<Serializer.JsonObject>();
                }

                if (m_groups != null)
                {
                    m_groupData = Serializer.Serialize<IGroup>(m_groups);
                }
                else
                {
                    m_groupData = new List<Serializer.JsonObject>();
                }

                if (m_stickyNotes != null)
                {
                    m_stickyNoteData = Serializer.Serialize<IStickyNote>(m_stickyNotes);
                }
                else
                {
                    m_stickyNoteData = new List<Serializer.JsonObject>();
                }

                if (m_stickyImages != null)
                {
                    m_stickyImageData = Serializer.Serialize<IStickyImage>(m_stickyImages);
                }
                else
                {
                    m_stickyImageData = new List<Serializer.JsonObject>();
                }
            }
        }

        /// <summary>
        /// Rebuilds the runtime graph model from the serialized backing data.
        /// </summary>
        /// <remarks>
        /// After deserializing the JSON-backed graph elements, the method restores Unity asset references recorded in
        /// <see cref="m_objectReferences"/> and forwards <see cref="ISerializationCallbackReceiver.OnAfterDeserialize"/> to
        /// nodes that implement it.
        /// </remarks>
        public virtual void OnAfterDeserialize()
        {
            using (Serializer.TargetScope s = new Serializer.TargetScope(this)) 
            {
                if (m_nodeData != null)
                {
                    m_nodes = Serializer.Deserialize<INode>(m_nodeData);
                }
                else
                {
                    m_nodes = new List<INode>();
                }

                foreach (INode node in m_nodes)
                {
                    DeserializeAssetReferences(m_objectReferences, node);
                    if (node is ISerializationCallbackReceiver serializeReceiver)
                    {
                        serializeReceiver.OnAfterDeserialize();
                    }
                }

                if (m_edgeData != null)
                {
                    m_edges = Serializer.Deserialize<IEdge>(m_edgeData);
                }
                else
                {
                    m_edges = new List<IEdge>();
                }

                if (m_groupData != null)
                {
                    m_groups = Serializer.Deserialize<IGroup>(m_groupData);
                }
                else
                {
                    m_groups = new List<IGroup>();
                }

                if (m_stickyNoteData != null)
                {
                    m_stickyNotes = Serializer.Deserialize<IStickyNote>(m_stickyNoteData);
                }
                else
                {
                    m_stickyNotes = new List<IStickyNote>();
                }

                if (m_stickyImageData != null)
                {
                    m_stickyImages = Serializer.Deserialize<IStickyImage>(m_stickyImageData);
                }
                else
                {
                    m_stickyImages = new List<IStickyImage>();
                }

                //Validate();
            }
        }

        internal static void SerializeAssetReferences(List<ObjectReference> referencesList, INode n)
        {
            IEnumerable<FieldInfo> serializedObjectFields = GetSerializedAssetFields(n.GetType());
            foreach (FieldInfo f in serializedObjectFields)
            {
                UnityEngine.Object target = f.GetValue(n) as UnityEngine.Object;
                if (target != null)
                {
                    string key = GetObjectSerializeKey(n.id, f.Name);
                    ObjectReference oRef = new ObjectReference(key, target);
                    referencesList.Add(oRef);
                }
            }
        }

        internal static void DeserializeAssetReferences(List<ObjectReference> referencesList, INode n)
        {
            IEnumerable<FieldInfo> serializedObjectFields = GetSerializedAssetFields(n.GetType());
            foreach (FieldInfo f in serializedObjectFields)
            {
                ObjectReference oRef = referencesList.Find(r =>
                {
                    return string.Equals(r.key, GetObjectSerializeKey(n.id, f.Name));
                });
                if (oRef.Equals(default))
                    continue;
                try
                {
                    f.SetValue(n, oRef.target);
                }
                catch (Exception)
                {
                    Debug.Log($"Failed to deserialize {n.GetType().Name}.{f.Name}. This happens when an asset referenced by the graph has been removed from the project.");
                }
            }
        }

        /// <summary>
        /// Gets the fields on a node type that should be stored through the graph asset-reference bridge.
        /// </summary>
        /// <param name="type">The node type to inspect.</param>
        /// <returns>
        /// The instance fields marked with <see cref="SerializeAssetAttribute"/> whose field type derives from
        /// <see cref="UnityEngine.Object"/>.
        /// </returns>
        /// <remarks>
        /// These fields are excluded from the regular graph serializer and are instead stored in
        /// <see cref="m_objectReferences"/> using a synthetic key composed from node ID and field name.
        /// </remarks>
        public static IEnumerable<FieldInfo> GetSerializedAssetFields(Type type)
        {
            FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            List<FieldInfo> serializedObjectFields = new List<FieldInfo>();
            foreach (FieldInfo f in fields)
            {
                SerializeAssetAttribute serializeAttribute = f.GetCustomAttribute<SerializeAssetAttribute>();
                bool serializable = serializeAttribute != null;
                bool isObject = f.FieldType.IsSubclassOf(typeof(UnityEngine.Object));

                if (serializable && isObject)
                {
                    serializedObjectFields.Add(f);
                }
            }
            return serializedObjectFields;
        }

        private static string GetObjectSerializeKey(string elementGuid, string fieldName)
        {
            return $"{elementGuid}.{fieldName}";
        }

        /// <summary>
        /// Removes invalid graph state and reports whether anything was changed during cleanup.
        /// </summary>
        /// <returns>
        /// <see langword="true"/> when invalid nodes, edges, groups, notes, or exposed-property entries were removed;
        /// otherwise, <see langword="false"/>.
        /// </returns>
        /// <remarks>
        /// Validation removes null elements, edges that point to missing or incompatible slots, self-connections, and
        /// exposed properties whose target node no longer exists. It does not rebuild IDs or repair slot wiring beyond
        /// removing invalid entries.
        /// </remarks>
        public virtual bool Validate()
        {
            int count = 0;
            count += m_nodes.RemoveAll(n => n == null);
            count += m_edges.RemoveAll(e => e == null);
            count += m_edges.RemoveAll(e =>
            {
                SlotRef outputSlot = e.outputSlot;
                INode n0 = GetNode(outputSlot.nodeId);
                if (n0 == null)
                    return true; //point to non-exist node
                ISlot slot0 = n0.GetSlot(outputSlot.slotId);
                if (slot0 == null)
                    return true; //point to non-exist slot

                SlotRef inputSlot = e.inputSlot;
                INode n1 = GetNode(inputSlot.nodeId);
                if (n1 == null)
                    return true; //point to non-exist node
                ISlot slot1 = n1.GetSlot(inputSlot.slotId);
                if (slot1 == null)
                    return true; //point to non-exist slot

                if (outputSlot.nodeId == inputSlot.nodeId)
                    return true; //self connect, point to the same node

                if (slot0.direction == slot1.direction)
                    return true; //input to input; output to output

                ISlotAdapter a0 = slot0.GetAdapter();
                ISlotAdapter a1 = slot1.GetAdapter();
                if (!a0.CanConnectTo(a1))
                    return true; //incompatible slot types

                if (!a1.CanConnectTo(a0))
                    return true; //incompatible slot types

                return false;
            });
            count += m_groups.RemoveAll(g => g == null);
            count += m_stickyNotes.RemoveAll(n => n == null);

            //remove exposed properties of non-existing nodes
            if (HasExposedProperties)
            {
                count += m_exposedProperties.RemoveAll(p =>
                {
                    INode targetNode = GetNode(p.id.nodeId);
                    return targetNode == null;
                });
            }

            return count != 0;
        }

        /// <summary>
        /// Returns whether an ID is already used by any graph element stored in this asset.
        /// </summary>
        /// <param name="id">The graph-element ID to test.</param>
        /// <returns><see langword="true"/> when the ID is already present on a node, edge, group, or sticky note; otherwise, <see langword="false"/>.</returns>
        public bool HasID(string id)
        {
            foreach (INode n in m_nodes)
            {
                if (n.id.Equals(id))
                {
                    return true;
                }
            }
            foreach (IEdge e in m_edges)
            {
                if (e.id.Equals(id))
                {
                    return true;
                }
            }
            foreach (IGroup g in m_groups)
            {
                if (g.id.Equals(id))
                {
                    return true;
                }
            }
            foreach (IStickyNote n in m_stickyNotes)
            {
                if (n.id.Equals(id))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Adds a node to the graph.
        /// </summary>
        /// <param name="n">The node to add.</param>
        /// <exception cref="System.ArgumentException">
        /// Thrown when the node type is not accepted by this graph or when its ID collides with an existing graph element.
        /// </exception>
        public virtual void AddNode(INode n)
        {
            Type type = n.GetType();
            if (!AcceptNodeType(type))
            {
                throw new System.ArgumentException($"Node of type {type.Name} is not accepted");
            }
            if (HasID(n.id))
            {
                throw new System.ArgumentException($"Graph element with the id {n.id} is already exist.");
            }
            m_nodes.Add(n);
        }

        /// <summary>
        /// Adds an edge to the graph.
        /// </summary>
        /// <param name="e">The edge to add.</param>
        /// <exception cref="System.ArgumentException">
        /// Thrown when the edge would introduce a recursive connection or when its ID collides with an existing graph element.
        /// </exception>
        public virtual void AddEdge(IEdge e)
        {
            if (WillCreateRecursive(e))
            {
                throw new System.ArgumentException("New edge suppose to create a recursive connection.");
            }
            if (HasID(e.id))
            {
                throw new System.ArgumentException($"Graph element with the id {e.id} is already exist.");
            }
            m_edges.Add(e);
        }

        /// <summary>
        /// Adds a group to the graph.
        /// </summary>
        /// <param name="g">The group to add.</param>
        /// <exception cref="System.ArgumentException">
        /// Thrown when the group's ID collides with an existing graph element.
        /// </exception>
        public virtual void AddGroup(IGroup g)
        {
            if (HasID(g.id))
            {
                throw new System.ArgumentException($"Graph element with the id {g.id} is already exist.");
            }
            m_groups.Add(g);
        }

        /// <summary>
        /// Removes a node and every edge connected to its input or output slots.
        /// </summary>
        /// <param name="id">The ID of the node to remove.</param>
        /// <returns>
        /// A record describing the removed node and any edges removed with it. When no node matches the ID, the returned
        /// record contains no removed node.
        /// </returns>
        public virtual RemovedElements RemoveNode(string id)
        {
            RemovedElements result = new RemovedElements();
            INode node = GetNode(id);
            if (node != null)
            {
                m_nodes.Remove(node);
                result.node = node;

                List<IEdge> edgeToRemove = new List<IEdge>();

                List<ISlot> slots = new List<ISlot>();
                slots.AddRange(node.GetInputSlots());
                slots.AddRange(node.GetOutputSlots());
                foreach (ISlot slot in slots)
                {
                    List<IEdge> connectedEdges = GetEdges(node.id, slot.id);
                    foreach (IEdge edge in connectedEdges)
                    {
                        m_edges.Remove(edge);
                        edgeToRemove.Add(edge);
                    }
                }

                if (edgeToRemove.Count > 0)
                {
                    result.edges = edgeToRemove;
                }
            }

            return result;
        }

        /// <summary>
        /// Removes one edge by ID.
        /// </summary>
        /// <param name="id">The ID of the edge to remove.</param>
        /// <returns>A record describing the removed edge, if any.</returns>
        public virtual RemovedElements RemoveEdge(string id)
        {
            RemovedElements result = new RemovedElements();
            IEdge edge = GetEdge(id);
            if (edge != null)
            {
                m_edges.Remove(edge);
                result.edges = new List<IEdge>() { edge };
            }
            return result;
        }

        /// <summary>
        /// Returns whether the graph contains a node with the specified ID.
        /// </summary>
        /// <param name="id">The node ID to test.</param>
        /// <returns><see langword="true"/> when a node with the ID exists; otherwise, <see langword="false"/>.</returns>
        public bool HasNode(string id)
        {
            return m_nodes.Exists(n => n.id.Equals(id));
        }

        /// <summary>
        /// Gets a node by ID.
        /// </summary>
        /// <param name="id">The node ID.</param>
        /// <returns>The matching node, or <see langword="null"/> when no node matches.</returns>
        public INode GetNode(string id)
        {
            return m_nodes.Find(n => n.id.Equals(id));
        }

        /// <summary>
        /// Gets a copy of the node list.
        /// </summary>
        /// <returns>A new list containing the graph's current nodes.</returns>
        public List<INode> GetNodes()
        {
            return new List<INode>(m_nodes);
        }

        /// <summary>
        /// Gets an edge by ID.
        /// </summary>
        /// <param name="id">The edge ID.</param>
        /// <returns>The matching edge, or <see langword="null"/> when no edge matches.</returns>
        public IEdge GetEdge(string id)
        {
            return m_edges.Find(e => e.id.Equals(id));
        }

        /// <summary>
        /// Gets a copy of the edge list.
        /// </summary>
        /// <returns>A new list containing the graph's current edges.</returns>
        public List<IEdge> GetEdges()
        {
            return new List<IEdge>(m_edges);
        }

        protected List<IEdge> GetEdges(string nodeId, int slotId)
        {
            List<IEdge> result = new List<IEdge>();
            for (int i = 0; i < m_edges.Count; ++i)
            {
                IEdge e = m_edges[i];
                if (e.inputSlot.nodeId.Equals(nodeId) && e.inputSlot.slotId.Equals(slotId))
                {
                    result.Add(e);
                }
                else if (e.outputSlot.nodeId.Equals(nodeId) && e.outputSlot.slotId.Equals(slotId))
                {
                    result.Add(e);
                }
            }
            return result;
        }

        /// <summary>
        /// Returns whether adding an edge would create a recursive dependency path.
        /// </summary>
        /// <param name="edge">The candidate edge to test.</param>
        /// <returns>
        /// <see langword="true"/> when the candidate edge would allow traversal from its input node back to its own output
        /// node; otherwise, <see langword="false"/>.
        /// </returns>
        /// <remarks>
        /// The check walks outward from the candidate input node through existing outgoing edges using a stack-based search.
        /// </remarks>
        public bool WillCreateRecursive(IEdge edge)
        {
            string startNodeId = edge.outputSlot.nodeId;
            Stack<IEdge> edgeStack = new Stack<IEdge>();
            edgeStack.Push(edge);

            while (edgeStack.Count > 0)
            {
                IEdge e = edgeStack.Pop();
                if (e.inputSlot.nodeId.Equals(startNodeId))
                {
                    return true;
                }
                else
                {
                    string currentNodeId = e.inputSlot.nodeId;
                    List<IEdge> nextConnections = m_edges.FindAll(e0 => e0.outputSlot.nodeId.Equals(currentNodeId));
                    for (int i = 0; i < nextConnections.Count; ++i)
                    {
                        edgeStack.Push(nextConnections[i]);
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Gets a copy of the group list.
        /// </summary>
        /// <returns>A new list containing the graph's current groups.</returns>
        public List<IGroup> GetGroups()
        {
            return new List<IGroup>(m_groups);
        }

        /// <summary>
        /// Gets a group by ID.
        /// </summary>
        /// <param name="id">The group ID.</param>
        /// <returns>The matching group, or <see langword="null"/> when no group matches.</returns>
        public IGroup GetGroup(string id)
        {
            return m_groups.Find(g => g.id.Equals(id));
        }

        /// <summary>
        /// Removes a group by ID.
        /// </summary>
        /// <param name="id">The group ID.</param>
        /// <returns>The removed group, or <see langword="null"/> when no group matches.</returns>
        public IGroup RemoveGroup(string id)
        {
            IGroup g = GetGroup(id);
            m_groups.Remove(g);
            return g;
        }

        /// <summary>
        /// Adds a sticky note to the graph canvas state.
        /// </summary>
        /// <param name="n">The sticky note to add.</param>
        /// <exception cref="System.ArgumentException">
        /// Thrown when the sticky note's ID collides with an existing graph element.
        /// </exception>
        public virtual void AddStickyNote(IStickyNote n)
        {
            if (HasID(n.id))
            {
                throw new System.ArgumentException($"Graph element with the id {n.id} is already exist.");
            }
            m_stickyNotes.Add(n);
        }

        /// <summary>
        /// Gets a sticky note by ID.
        /// </summary>
        /// <param name="id">The sticky-note ID.</param>
        /// <returns>The matching sticky note, or <see langword="null"/> when none matches.</returns>
        public IStickyNote GetStickyNote(string id)
        {
            return m_stickyNotes.Find(n => n.id.Equals(id));
        }

        /// <summary>
        /// Gets a copy of the sticky-note list.
        /// </summary>
        /// <returns>A new list containing the graph's current sticky notes.</returns>
        public List<IStickyNote> GetStickyNotes()
        {
            return new List<IStickyNote>(m_stickyNotes);
        }

        /// <summary>
        /// Removes a sticky note by ID.
        /// </summary>
        /// <param name="id">The sticky-note ID.</param>
        /// <returns>The removed sticky note, or <see langword="null"/> when none matches.</returns>
        public IStickyNote RemoveStickyNote(string id)
        {
            IStickyNote note = GetStickyNote(id);
            m_stickyNotes.Remove(note);
            return note;
        }

        /// <summary>
        /// Adds a sticky image to the graph canvas state.
        /// </summary>
        /// <param name="i">The sticky image to add.</param>
        /// <exception cref="System.ArgumentException">
        /// Thrown when the sticky image's ID collides with an existing graph element.
        /// </exception>
        public virtual void AddStickyImage(IStickyImage i)
        {
            if (HasID(i.id))
            {
                throw new System.ArgumentException($"Graph element with the id {i.id} is already exist.");
            }
            m_stickyImages.Add(i);
        }

        /// <summary>
        /// Gets a sticky image by ID.
        /// </summary>
        /// <param name="id">The sticky-image ID.</param>
        /// <returns>The matching sticky image, or <see langword="null"/> when none matches.</returns>
        public IStickyImage GetStickyImage(string id)
        {
            return m_stickyImages.Find(n => n.id.Equals(id));
        }

        /// <summary>
        /// Gets a copy of the sticky-image list.
        /// </summary>
        /// <returns>A new list containing the graph's current sticky images.</returns>
        public List<IStickyImage> GetStickyImages()
        {
            return new List<IStickyImage>(m_stickyImages);
        }

        /// <summary>
        /// Removes a sticky image by ID.
        /// </summary>
        /// <param name="id">The sticky-image ID.</param>
        /// <returns>The removed sticky image, or <see langword="null"/> when none matches.</returns>
        public IStickyImage RemoveStickyImage(string id)
        {
            IStickyImage img = GetStickyImage(id);
            m_stickyImages.Remove(img);
            return img;
        }

        /// <summary>
        /// Gets nodes assignable to a specific CLR type.
        /// </summary>
        /// <returns>A new list containing nodes assignable to <typeparamref name="T"/>.</returns>
        public List<INode> GetNodes<T>()
        {
            return GetNodes(typeof(T));
        }

        /// <summary>
        /// Gets nodes assignable to a specific CLR type.
        /// </summary>
        /// <param name="t">The node base type or interface to match.</param>
        /// <returns>A new list containing nodes assignable to <paramref name="t"/>.</returns>
        public List<INode> GetNodes(Type t)
        {
            List<INode> result = m_nodes.FindAll(n => t.IsAssignableFrom(n.GetType()));
            return result;
        }

        /// <summary>
        /// Gets nodes assignable to a specific CLR type and casts them to that type.
        /// </summary>
        /// <returns>A new list containing nodes assignable to <typeparamref name="T"/>.</returns>
        public List<T> GetNodesOfType<T>() where T : class, INode
        {
            List<T> result = m_nodes.FindAll(n => typeof(T).IsAssignableFrom(n.GetType())).ConvertAll(n => n as T);
            return result;
        }

        /// <summary>
        /// Gets the first node assignable to a specific CLR type.
        /// </summary>
        /// <returns>The first node assignable to <typeparamref name="T"/>, or <see langword="null"/> when none match.</returns>
        public INode GetNode<T>()
        {
            return GetNode(typeof(T));
        }

        /// <summary>
        /// Gets the first node assignable to a specific CLR type.
        /// </summary>
        /// <param name="t">The node base type or interface to match.</param>
        /// <returns>The first node assignable to <paramref name="t"/>, or <see langword="null"/> when none match.</returns>
        public INode GetNode(Type t)
        {
            return m_nodes.Find(n => t.IsAssignableFrom(n.GetType()));
        }

        /// <summary>
        /// Raises the shared graph-changed event for this graph asset.
        /// </summary>
        public void InvokeChangeEvent()
        {
            if (graphChanged != null)
            {
                graphChanged.Invoke(this);
            }
        }

        /// <summary>
        /// Returns whether this graph type accepts a node type for insertion.
        /// </summary>
        /// <param name="t">The node type to test.</param>
        /// <returns>
        /// <see langword="true"/> when the node type is allowed in this graph; otherwise, <see langword="false"/>.
        /// </returns>
        /// <remarks>
        /// The base implementation accepts all node types. Specialized graph types can override this to enforce stricter
        /// node families.
        /// </remarks>
        public virtual bool AcceptNodeType(Type t)
        {
            return true;
        }

        /// <summary>
        /// Gets subgraphs that should be treated as execution dependencies of this graph.
        /// </summary>
        /// <returns>The dependent subgraphs referenced by this graph.</returns>
        /// <remarks>
        /// <see cref="GraphContext"/> uses this to detect dependency cycles across nested graphs before execution begins.
        /// The base implementation reports no dependencies.
        /// </remarks>
        public virtual IEnumerable<GraphAsset> GetDependencySubGraphs()
        {
            return new GraphAsset[0];
        }
    }
}
#endif


