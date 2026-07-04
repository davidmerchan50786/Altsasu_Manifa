#if VISTA
using UnityEngine;
using UnityEngine.Rendering;

namespace Pinwheel.Vista
{
    [CreateAssetMenu(menuName = "Vista/Tree Template")]
    [HelpURL("https://docs.pinwheelstud.io/vista/docs/tree-template.html")]
    /// <summary>
    /// Describes how Vista should convert generated tree samples into terrain-system tree prototypes.
    /// </summary>
    /// <remarks>
    /// Tree templates are paired with <see cref="InstanceSample"/> buffers in <see cref="BiomeData"/> and consumed by
    /// terrain-system tree populators. Unity Terrain uses only a subset of the data here, while Polaris uses the fuller
    /// prototype description including billboard, layer, pivot, shadow, and base-transform settings.
    /// </remarks>
    public class TreeTemplate : ScriptableObject
    {
        [SerializeField]
        protected GameObject m_prefab;
        /// <summary>
        /// Gets or sets the primary prefab used to create tree prototypes.
        /// </summary>
        /// <remarks>
        /// This prefab is required for the template to be valid. When no variant is available or selected, generated tree
        /// instances use a prototype built from this prefab.
        /// </remarks>
        public GameObject prefab
        {
            get
            {
                return m_prefab;
            }
            set
            {
                m_prefab = value;
            }
        }

        [SerializeField]
        protected GameObject[] m_prefabVariants;
        /// <summary>
        /// Gets or sets additional prefab variants that can produce extra tree prototypes.
        /// </summary>
        /// <remarks>
        /// The getter returns a copy of the stored array and returns an empty array when variant support is unavailable in
        /// the current integration. Null assignments are converted to an empty array. Each non-null variant becomes an
        /// additional tree prototype alongside <see cref="prefab"/>.
        /// </remarks>
        public GameObject[] prefabVariants
        {
            get
            {
                if (!TemplateUtils.IsVariantsSupported())
                {
                    return new GameObject[0];
                }

                if (m_prefabVariants == null)
                {
                    return new GameObject[0];
                }
                else
                {
                    GameObject[] variants = new GameObject[m_prefabVariants.Length];
                    m_prefabVariants.CopyTo(variants, 0);
                    return variants;
                }
            }
            set
            {
                if (value == null)
                {
                    m_prefabVariants = new GameObject[0];
                }
                else
                {
                    m_prefabVariants = new GameObject[value.Length];
                    value.CopyTo(m_prefabVariants, 0);
                }
            }
        }

        [SerializeField]
        protected int m_navMeshLod;
        /// <summary>
        /// Gets or sets the NavMesh LOD index for Unity Terrain tree prototypes.
        /// </summary>
        /// <remarks>
        /// Values below zero are clamped to zero. This setting is forwarded to <see cref="TreePrototype.navMeshLod"/> in
        /// the Unity Terrain integration and is otherwise ignored unless another backend reads it explicitly.
        /// </remarks>
        public int navMeshLod
        {
            get
            {
                return m_navMeshLod;
            }
            set
            {
                m_navMeshLod = Mathf.Max(0, value);
            }
        }

        [SerializeField]
        protected float m_bendFactor;
        /// <summary>
        /// Gets or sets the bend factor used by tree renderers that support wind bending.
        /// </summary>
        /// <remarks>
        /// Unity Terrain forwards this to <see cref="TreePrototype.bendFactor"/>. Polaris uses the tree prefab and base
        /// transform settings instead and does not currently read this field when building tree prototypes.
        /// </remarks>
        public float bendFactor
        {
            get
            {
                return m_bendFactor;
            }
            set
            {
                m_bendFactor = value;
            }
        }

        [SerializeField]
        protected BillboardAsset m_billboard;
        /// <summary>
        /// Gets or sets the billboard asset used by Polaris tree prototypes.
        /// </summary>
        /// <remarks>
        /// Unity Terrain tree prototypes do not consume this field. Polaris forwards it directly to its tree prototype so
        /// distant tree rendering can use a billboard representation when available.
        /// </remarks>
        public BillboardAsset billboard
        {
            get
            {
                return m_billboard;
            }
            set
            {
                m_billboard = value;
            }
        }

        [SerializeField]
        protected ShadowCastingMode m_shadowCastingMode;
        /// <summary>
        /// Gets or sets the shadow-casting mode for the main tree prefab in Polaris.
        /// </summary>
        /// <remarks>
        /// This field is relevant to Polaris and custom integrations that expose tree shadow settings. Unity Terrain tree
        /// prototypes do not consume it.
        /// </remarks>
        public ShadowCastingMode shadowCastingMode
        {
            get
            {
                return m_shadowCastingMode;
            }
            set
            {
                m_shadowCastingMode = value;
            }
        }

        [SerializeField]
        protected bool m_receiveShadow;
        /// <summary>
        /// Gets or sets whether tree renderers should receive shadows.
        /// </summary>
        /// <remarks>
        /// Polaris copies this flag into its tree prototype. Unity Terrain tree prototypes do not expose an equivalent
        /// setting.
        /// </remarks>
        public bool receiveShadow
        {
            get
            {
                return m_receiveShadow;
            }
            set
            {
                m_receiveShadow = value;
            }
        }

        [SerializeField]
        protected ShadowCastingMode m_billboardShadowCastingMode;
        /// <summary>
        /// Gets or sets the shadow-casting mode for billboard rendering in Polaris.
        /// </summary>
        /// <remarks>
        /// This field affects only backends that support explicit billboard shadow settings. Unity Terrain ignores it.
        /// </remarks>
        public ShadowCastingMode billboardShadowCastingMode
        {
            get
            {
                return m_billboardShadowCastingMode;
            }
            set
            {
                m_billboardShadowCastingMode = value;
            }
        }

        [SerializeField]
        protected bool m_billboardReceiveShadow;
        /// <summary>
        /// Gets or sets whether billboard renderers should receive shadows in Polaris.
        /// </summary>
        /// <remarks>
        /// Unity Terrain ignores this field. Polaris forwards it to the billboard portion of its tree prototype.
        /// </remarks>
        public bool billboardReceiveShadow
        {
            get
            {
                return m_billboardReceiveShadow;
            }
            set
            {
                m_billboardReceiveShadow = value;
            }
        }

        [SerializeField]
        protected int m_layer;
        /// <summary>
        /// Gets or sets the layer assigned to spawned tree objects in integrations that support explicit tree layers.
        /// </summary>
        /// <remarks>
        /// Polaris uses this value when <see cref="keepPrefabLayer"/> is disabled. Unity Terrain tree prototypes do not
        /// expose a comparable layer field.
        /// </remarks>
        public int layer
        {
            get
            {
                return m_layer;
            }
            set
            {
                m_layer = value;
            }
        }

        [SerializeField]
        protected bool m_keepPrefabLayer;
        /// <summary>
        /// Gets or sets whether the prefab's original layer should be preserved.
        /// </summary>
        /// <remarks>
        /// Polaris respects this flag when constructing tree prototypes. If enabled, the configured <see cref="layer"/> is
        /// ignored in favor of the prefab's own layer. Unity Terrain ignores this field.
        /// </remarks>
        public bool keepPrefabLayer
        {
            get
            {
                return m_keepPrefabLayer;
            }
            set
            {
                m_keepPrefabLayer = value;
            }
        }

        [SerializeField]
        protected float m_pivotOffset;
        /// <summary>
        /// Gets or sets the normalized pivot offset used by backends that expose tree pivot adjustment.
        /// </summary>
        /// <remarks>
        /// The value is clamped to the <c>[-1, 1]</c> range before storage. Polaris forwards it to its tree prototype.
        /// Unity Terrain ignores this field.
        /// </remarks>
        public float pivotOffset
        {
            get
            {
                return m_pivotOffset;
            }
            set
            {
                m_pivotOffset = Mathf.Clamp(value, -1, 1);
            }
        }

        [SerializeField]
        protected Quaternion m_baseRotation = Quaternion.identity;
        /// <summary>
        /// Gets or sets the base rotation applied to the tree prefab before per-instance rotation.
        /// </summary>
        /// <remarks>
        /// A zeroed quaternion is normalized back to <see cref="Quaternion.identity"/> on both get and set to protect older
        /// serialized data from producing invalid rotations. Polaris uses this field directly in its tree prototype. Unity
        /// Terrain ignores it.
        /// </remarks>
        public Quaternion baseRotation
        {
            get
            {
                if (m_baseRotation.x == 0 &&
                    m_baseRotation.y == 0 &&
                    m_baseRotation.z == 0 &&
                    m_baseRotation.w == 0)
                {
                    m_baseRotation = Quaternion.identity;
                }
                return m_baseRotation;
            }
            set
            {
                m_baseRotation = value;
                if (m_baseRotation.x == 0 &&
                     m_baseRotation.y == 0 &&
                     m_baseRotation.z == 0 &&
                     m_baseRotation.w == 0)
                {
                    m_baseRotation = Quaternion.identity;
                }
            }
        }

        [SerializeField]
        protected Vector3 m_baseScale = Vector3.one;
        /// <summary>
        /// Gets or sets the base scale applied to the tree prefab before per-instance scale variation.
        /// </summary>
        /// <remarks>
        /// Polaris forwards this to its tree prototype. Unity Terrain tree prototypes do not consume it and instead rely on
        /// per-instance width and height scales carried by the generated sample buffer.
        /// </remarks>
        public Vector3 baseScale
        {
            get
            {
                return m_baseScale;
            }
            set
            {
                m_baseScale = value;
            }
        }

        /// <summary>
        /// Restores the template to Vista's default tree settings.
        /// </summary>
        /// <remarks>
        /// The defaults clear prefab references, set the main tree renderer to cast and receive shadows, disable billboard
        /// shadow casting, and reset pivot and base transform settings to their neutral values.
        /// </remarks>
        public void Reset()
        {
            m_prefab = null;
            m_prefabVariants = null;
            m_navMeshLod = 0;
            m_bendFactor = 0;
            m_billboard = null;
            m_shadowCastingMode = ShadowCastingMode.On;
            m_receiveShadow = true;
            m_billboardShadowCastingMode = ShadowCastingMode.Off;
            m_billboardReceiveShadow = true;
            m_layer = 0;
            m_keepPrefabLayer = false;
            m_pivotOffset = 0;
            m_baseRotation = Quaternion.identity;
            m_baseScale = Vector3.one;
        }

        /// <summary>
        /// Tests whether the template has the minimum data required to build tree prototypes.
        /// </summary>
        /// <returns>
        /// <see langword="true"/> when <see cref="prefab"/> is assigned; otherwise, <see langword="false"/>.
        /// </returns>
        /// <remarks>
        /// Variant prefabs are optional and do not make an otherwise empty template valid.
        /// </remarks>
        public bool IsValid()
        {
            return m_prefab != null;
        }
    }
}
#endif


