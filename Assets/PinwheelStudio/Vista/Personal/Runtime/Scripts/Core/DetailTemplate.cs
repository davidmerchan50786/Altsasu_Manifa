#if VISTA
#if GRIFFIN
using Pinwheel.Griffin;
#endif
using UnityEngine;
using UnityEngine.Rendering;

namespace Pinwheel.Vista
{
    [CreateAssetMenu(menuName = "Vista/Detail Template")]
    [HelpURL("https://docs.pinwheelstud.io/vista/docs/detail-template.html")]
    /// <summary>
    /// Describes one detail asset set that Vista can output to a terrain system.
    /// </summary>
    /// <remarks>
    /// A detail template is referenced by detail density and detail instance output nodes, then consumed by
    /// terrain-system-specific populators when generated data is applied to a tile. Unity Terrain uses the template
    /// to build <see cref="DetailPrototype"/> entries, while Polaris converts it into grass prototypes. Some fields are
    /// therefore only meaningful for one backend.
    /// </remarks>
    public class DetailTemplate : ScriptableObject
    {
        [SerializeField]
        protected DetailRenderMode m_renderMode;
        /// <summary>
        /// Gets or sets how the detail is rendered by the target terrain system.
        /// </summary>
        /// <remarks>
        /// This value determines which source asset must be assigned for the template to be valid. Grass-style modes
        /// require <see cref="texture"/>, while <see cref="DetailRenderMode.VertexLit"/> requires <see cref="prefab"/>.
        /// </remarks>
        public DetailRenderMode renderMode
        {
            get
            {
                return m_renderMode;
            }
            set
            {
                m_renderMode = value;
            }
        }

        [SerializeField]
        protected Texture2D m_texture;
        /// <summary>
        /// Gets or sets the main texture used by grass-style detail modes.
        /// </summary>
        /// <remarks>
        /// Unity Terrain assigns this texture to <see cref="DetailPrototype.prototypeTexture"/> when
        /// <see cref="renderMode"/> is not <see cref="DetailRenderMode.VertexLit"/>. Polaris uses it to build clump-based
        /// grass prototypes. This field is ignored for mesh-based details.
        /// </remarks>
        public Texture2D texture
        {
            get
            {
                return m_texture;
            }
            set
            {
                m_texture = value;
            }
        }

        [SerializeField]
        protected Texture2D[] m_textureVariants;
        /// <summary>
        /// Gets or sets additional texture variants for grass-style details.
        /// </summary>
        /// <remarks>
        /// The getter returns a copy of the stored array and returns an empty array when template variants are not
        /// supported by the current integration. Null assignments are converted to an empty array. Each non-null entry
        /// becomes an extra grass/detail prototype alongside <see cref="texture"/>.
        /// </remarks>
        public Texture2D[] textureVariants
        {
            get
            {
                if (!TemplateUtils.IsVariantsSupported())
                {
                    return new Texture2D[0];
                }

                if (m_textureVariants == null)
                {
                    return new Texture2D[0];
                }
                else
                {
                    Texture2D[] variants = new Texture2D[m_textureVariants.Length];
                    m_textureVariants.CopyTo(variants, 0);
                    return variants;
                }
            }
            set
            {
                if (value == null)
                {
                    m_textureVariants = new Texture2D[0];
                }
                else
                {
                    m_textureVariants = new Texture2D[value.Length];
                    value.CopyTo(m_textureVariants, 0);
                }
            }
        }

        [SerializeField]
        protected GameObject m_prefab;
        /// <summary>
        /// Gets or sets the main prefab used by mesh-based details.
        /// </summary>
        /// <remarks>
        /// This field is consumed only when <see cref="renderMode"/> is <see cref="DetailRenderMode.VertexLit"/>. Unity
        /// Terrain assigns it to <see cref="DetailPrototype.prototype"/>, and Polaris creates a detail-object grass
        /// prototype from it.
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
        /// Gets or sets additional prefab variants for mesh-based details.
        /// </summary>
        /// <remarks>
        /// The getter returns a copy of the stored array and returns an empty array when variants are unsupported by
        /// the current integration. Null assignments are converted to an empty array. Each non-null prefab produces an
        /// additional detail prototype alongside <see cref="prefab"/>.
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
        protected Color m_primaryColor;
        /// <summary>
        /// Gets or sets the primary tint applied to the detail prototype.
        /// </summary>
        /// <remarks>
        /// Unity Terrain maps this to <see cref="DetailPrototype.healthyColor"/>. Polaris uses it as the prototype color.
        /// </remarks>
        public Color primaryColor
        {
            get
            {
                return m_primaryColor;
            }
            set
            {
                m_primaryColor = value;
            }
        }

        [SerializeField]
        protected Color m_secondaryColor;
        /// <summary>
        /// Gets or sets the secondary tint used by terrain systems that support color variation.
        /// </summary>
        /// <remarks>
        /// Unity Terrain maps this to <see cref="DetailPrototype.dryColor"/>. Polaris does not currently consume this value
        /// when creating grass prototypes.
        /// </remarks>
        public Color secondaryColor
        {
            get
            {
                return m_secondaryColor;
            }
            set
            {
                m_secondaryColor = value;
            }
        }

        [SerializeField]
        protected float m_minHeight;
        /// <summary>
        /// Gets or sets the minimum instance height multiplier.
        /// </summary>
        /// <remarks>
        /// Values below zero are clamped to zero. Unity Terrain forwards this to <see cref="DetailPrototype.minHeight"/>.
        /// Polaris combines it with <see cref="minWidth"/> when building prototype size.
        /// </remarks>
        public float minHeight
        {
            get
            {
                return m_minHeight;
            }
            set
            {
                m_minHeight = Mathf.Max(0, value);
            }
        }

        [SerializeField]
        protected float m_maxHeight;
        /// <summary>
        /// Gets or sets the maximum instance height multiplier.
        /// </summary>
        /// <remarks>
        /// Values below zero are clamped to zero. Unity Terrain forwards this to <see cref="DetailPrototype.maxHeight"/>.
        /// </remarks>
        public float maxHeight
        {
            get
            {
                return m_maxHeight;
            }
            set
            {
                m_maxHeight = Mathf.Max(0, value);
            }
        }

        [SerializeField]
        protected float m_minWidth;
        /// <summary>
        /// Gets or sets the minimum instance width multiplier.
        /// </summary>
        /// <remarks>
        /// Values below zero are clamped to zero. Unity Terrain forwards this to <see cref="DetailPrototype.minWidth"/>.
        /// Polaris also uses this value as the X and Z size components for generated grass prototypes.
        /// </remarks>
        public float minWidth
        {
            get
            {
                return m_minWidth;
            }
            set
            {
                m_minWidth = Mathf.Max(0, value);
            }
        }

        [SerializeField]
        protected float m_maxWidth;
        /// <summary>
        /// Gets or sets the maximum instance width multiplier.
        /// </summary>
        /// <remarks>
        /// Values below zero are clamped to zero. Unity Terrain forwards this to <see cref="DetailPrototype.maxWidth"/>.
        /// </remarks>
        public float maxWidth
        {
            get
            {
                return m_maxWidth;
            }
            set
            {
                m_maxWidth = Mathf.Max(0, value);
            }
        }

        [SerializeField]
        protected float m_noiseSpread;
        /// <summary>
        /// Gets or sets the noise spread used by Unity Terrain's detail placement.
        /// </summary>
        /// <remarks>
        /// This value is passed through to <see cref="DetailPrototype.noiseSpread"/>. Polaris does not currently use it.
        /// </remarks>
        public float noiseSpread
        {
            get
            {
                return m_noiseSpread;
            }
            set
            {
                m_noiseSpread = value;
            }
        }

        [SerializeField]
        protected float m_holeEdgePadding;
        /// <summary>
        /// Gets or sets how aggressively Unity Terrain removes details near terrain holes.
        /// </summary>
        /// <remarks>
        /// The value is clamped to the <c>[0, 1]</c> range before storage and mapped to
        /// <see cref="DetailPrototype.holeEdgePadding"/>. Polaris does not consume this property.
        /// </remarks>
        public float holeEdgePadding
        {
            get
            {
                return m_holeEdgePadding;
            }
            set
            {
                m_holeEdgePadding = Mathf.Clamp01(value);
            }
        }

#if UNITY_2021_2_OR_NEWER
        [SerializeField]
        protected bool m_useInstancing;
        /// <summary>
        /// Gets or sets whether mesh-based details should use GPU instancing on Unity Terrain.
        /// </summary>
        /// <remarks>
        /// This flag is applied only when <see cref="renderMode"/> is <see cref="DetailRenderMode.VertexLit"/>. Grass
        /// texture modes force instancing off in the Unity Terrain integration.
        /// </remarks>
        public bool useInstancing
        {
            get
            {
                return m_useInstancing;
            }
            set
            {
                m_useInstancing = value;
            }
        }
#endif

        [SerializeField]
        protected float m_pivotOffset;
        /// <summary>
        /// Gets or sets the vertical pivot offset used by Polaris grass rendering.
        /// </summary>
        /// <remarks>
        /// This value is forwarded to Polaris grass prototypes. Unity Terrain does not expose an equivalent field for
        /// details, so this property has no effect there.
        /// </remarks>
        public float pivotOffset
        {
            get
            {
                return m_pivotOffset;
            }
            set
            {
                m_pivotOffset = value;
            }
        }

        [SerializeField]
        protected float m_bendFactor;
        /// <summary>
        /// Gets or sets the bend factor used by wind-reactive detail rendering.
        /// </summary>
        /// <remarks>
        /// Polaris forwards this value directly to its grass prototype. Unity Terrain uses it only for its
        /// <see cref="DetailPrototype"/> bend factor support.
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
        protected int m_layer;
        /// <summary>
        /// Gets or sets the rendering layer assigned to Polaris detail prototypes.
        /// </summary>
        /// <remarks>
        /// Unity Terrain detail prototypes do not expose a matching layer setting, so this value is only meaningful for
        /// Polaris and custom terrain integrations that read it explicitly.
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
        protected bool m_alignToSurface;
        /// <summary>
        /// Gets or sets whether Polaris should align spawned details to the terrain normal.
        /// </summary>
        /// <remarks>
        /// Unity Terrain detail prototypes do not consume this field. In Polaris, it is copied to the grass prototype's
        /// surface-alignment flag.
        /// </remarks>
        public bool alignToSurface
        {
            get
            {
                return m_alignToSurface;
            }
            set
            {
                m_alignToSurface = value;
            }
        }

        [SerializeField]
        protected ShadowCastingMode m_castShadow;
        /// <summary>
        /// Gets or sets the shadow-casting mode used by Polaris detail prototypes.
        /// </summary>
        /// <remarks>
        /// Unity Terrain detail prototypes do not expose this setting. It is currently relevant to Polaris and any custom
        /// terrain system that maps the value onto its own detail renderer.
        /// </remarks>
        public ShadowCastingMode castShadow
        {
            get
            {
                return m_castShadow;
            }
            set
            {
                m_castShadow = value;
            }
        }

        [SerializeField]
        protected bool m_receiveShadow;
        /// <summary>
        /// Gets or sets whether Polaris detail prototypes receive shadows.
        /// </summary>
        /// <remarks>
        /// This flag is not used by Unity Terrain detail prototypes, but Polaris forwards it to the generated grass
        /// prototype.
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
        protected int m_density;
        /// <summary>
        /// Gets or sets the base density multiplier used when converting generated density maps into terrain detail counts.
        /// </summary>
        /// <remarks>
        /// Values below one are clamped to one. Unity Terrain divides this value across all generated prototype variants
        /// from the same template before converting density textures into integer detail layers.
        /// </remarks>
        public int density
        {
            get
            {
                return m_density;
            }
            set
            {
                m_density = Mathf.Max(1, value);
            }
        }


        public enum TextureBasedGrassShape
        {
            Default, Quad, Cross, TriCross, Clump
        }

        [SerializeField]
        protected TextureBasedGrassShape m_textureBasedGrassShape = TextureBasedGrassShape.Default;
        public TextureBasedGrassShape textureBasedGrassShape
        {
            get
            {
                return m_textureBasedGrassShape;
            }
            set
            {
                m_textureBasedGrassShape=value;
            }
        }

        /// <summary>
        /// Restores the template to Vista's default detail settings.
        /// </summary>
        /// <remarks>
        /// These defaults describe a simple grass-texture detail asset with neutral tint, moderate size variation, no
        /// hole padding, and a base density of 100. The method does not allocate or clear variant arrays.
        /// </remarks>
        public void Reset()
        {
            m_renderMode = DetailRenderMode.Grass;
            m_texture = null;
            m_prefab = null;
            m_primaryColor = Color.white;
            m_secondaryColor = Color.white;
            m_minHeight = 0.5f;
            m_maxHeight = 1f;
            m_minWidth = 0.5f;
            m_maxWidth = 1f;
            m_noiseSpread = 0.1f;
            m_holeEdgePadding = 0;
#if UNITY_2021_2_OR_NEWER
            m_useInstancing = true;
#endif
            m_pivotOffset = 0;
            m_bendFactor = 1f;
            m_layer = 0;
            m_alignToSurface = false;
            m_castShadow = ShadowCastingMode.Off;
            m_receiveShadow = false;
            m_density = 100;
            m_textureBasedGrassShape = TextureBasedGrassShape.Default;
        }

        /// <summary>
        /// Tests whether the template has the minimum required source asset for its current render mode.
        /// </summary>
        /// <returns>
        /// <see langword="true"/> when grass-style modes have a non-null <see cref="texture"/>, or when
        /// <see cref="DetailRenderMode.VertexLit"/> has a non-null <see cref="prefab"/>; otherwise, <see langword="false"/>.
        /// </returns>
        /// <remarks>
        /// This validation only checks the primary asset used to construct prototypes. Variant entries are optional and
        /// are ignored when null.
        /// </remarks>
        public bool IsValid()
        {
            if (m_renderMode == DetailRenderMode.Grass || m_renderMode == DetailRenderMode.GrassBillboard)
            {
                return m_texture != null;
            }
            else if (m_renderMode == DetailRenderMode.VertexLit)
            {
                return m_prefab != null;
            }
            return false;
        }

#if GRIFFIN
        public static GGrassShape ToPolarisGrassShape(TextureBasedGrassShape s)
        {
            switch (s)
            {
                case TextureBasedGrassShape.Quad: return GGrassShape.Quad;
                case TextureBasedGrassShape.Cross: return GGrassShape.Cross;
                case TextureBasedGrassShape.TriCross: return GGrassShape.TriCross;
                case TextureBasedGrassShape.Clump: return GGrassShape.Clump;
                default: return GGrassShape.Clump;
            }
        }
#endif
    }
}
#endif


