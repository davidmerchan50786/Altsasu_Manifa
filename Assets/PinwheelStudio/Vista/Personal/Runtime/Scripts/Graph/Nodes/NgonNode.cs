#if VISTA
using Pinwheel.Vista.Graphics;
using System.Collections;
using UnityEngine;

namespace Pinwheel.Vista.Graph
{
    [NodeMetadata(
        title = "N-gon",
        path = "Base Shape/N-gon",
        icon = "",
        documentation = "",
        keywords = "ngon, polygon, triangle, pentagon, hexagon, octagon, pyramid, truncated",
        description = "Generate an N-sided polygon pyramid mask with configurable truncation.")]
    public class NgonNode : ImageNodeBase
    {
        public readonly MaskSlot outputSlot = new MaskSlot("Output", SlotDirection.Output, 100);

        [SerializeField]
        private int m_edgeCount;
        public int edgeCount
        {
            get
            {
                return m_edgeCount;
            }
            set
            {
                m_edgeCount = Mathf.Clamp(value, 3, 100);
            }
        }

        [SerializeField]
        private float m_height;
        public float height
        {
            get
            {
                return m_height;
            }
            set
            {
                m_height = Mathf.Clamp01(value);
            }
        }

        [SerializeField]
        private float m_innerRadius;
        public float innerRadius
        {
            get
            {
                return m_innerRadius;
            }
            set
            {
                m_innerRadius = Mathf.Clamp01(value);
            }
        }

        [SerializeField]
        private float m_outerRadius;
        public float outerRadius
        {
            get
            {
                return m_outerRadius;
            }
            set
            {
                m_outerRadius = Mathf.Max(0.001f, value);
            }
        }

        [SerializeField]
        private bool m_pointUp;
        public bool pointUp
        {
            get
            {
                return m_pointUp;
            }
            set
            {
                m_pointUp = value;
            }
        }

        private static readonly string SHADER_NAME = "Hidden/Vista/Graph/Ngon";
        private Material m_material;

        public NgonNode() : base()
        {
            m_edgeCount = 6;
            m_height = 1f;
            m_innerRadius = 0f;
            m_outerRadius = 0.5f;
            m_pointUp = false;
        }

        public override void ExecuteImmediate(GraphContext context)
        {
            int baseResolution = context.GetArg(Args.RESOLUTION).intValue;
            int resolution = this.CalculateResolution(baseResolution, baseResolution);
            DataPool.RtDescriptor desc = DataPool.RtDescriptor.Create(resolution, resolution);
            SlotRef outputRef = new SlotRef(m_id, outputSlot.id);
            RenderTexture targetRt = context.CreateRenderTarget(desc, outputRef);

            m_material = new Material(ShaderUtilities.Find(SHADER_NAME));
            DrawNgon(targetRt, m_material);
            Object.DestroyImmediate(m_material);
        }

        public override IEnumerator Execute(GraphContext context)
        {
            ExecuteImmediate(context);
            yield return null;
        }

        private void DrawNgon(RenderTexture targetRt, Material mat)
        {
            int n = m_edgeCount;
            float angleStep = 2f * Mathf.PI / n;
            float angleOffset = m_pointUp ? Mathf.PI * 0.5f : 0f;
            Vector2 center = Vector2.one * 0.5f;

            float halfCanvas = 0.5f;
            float outerDist = m_outerRadius * halfCanvas;
            float innerDist = outerDist * m_innerRadius;

            Vector2[] outerVerts = new Vector2[n];
            Vector2[] innerVerts = new Vector2[n];
            for (int i = 0; i < n; ++i)
            {
                float angle = i * angleStep + angleOffset;
                Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                outerVerts[i] = center + dir * outerDist;
                innerVerts[i] = center + dir * innerDist;
            }

            RenderTexture.active = targetRt;
            GL.PushMatrix();
            mat.SetPass(0);
            GL.LoadOrtho();

            if (m_innerRadius <= 0f)
            {
                GL.Begin(GL.TRIANGLES);
                for (int i = 0; i < n; ++i)
                {
                    int next = (i + 1) % n;
                    GL.Vertex3(center.x, center.y, m_height);
                    GL.Vertex3(outerVerts[i].x, outerVerts[i].y, 0f);
                    GL.Vertex3(outerVerts[next].x, outerVerts[next].y, 0f);
                }
                GL.End();
            }
            else
            {
                GL.Begin(GL.TRIANGLES);
                for (int i = 0; i < n; ++i)
                {
                    int next = (i + 1) % n;
                    GL.Vertex3(center.x, center.y, m_height);
                    GL.Vertex3(innerVerts[i].x, innerVerts[i].y, m_height);
                    GL.Vertex3(innerVerts[next].x, innerVerts[next].y, m_height);
                }
                GL.End();

                GL.Begin(GL.QUADS);
                for (int i = 0; i < n; ++i)
                {
                    int next = (i + 1) % n;
                    GL.Vertex3(innerVerts[i].x, innerVerts[i].y, m_height);
                    GL.Vertex3(outerVerts[i].x, outerVerts[i].y, 0f);
                    GL.Vertex3(outerVerts[next].x, outerVerts[next].y, 0f);
                    GL.Vertex3(innerVerts[next].x, innerVerts[next].y, m_height);
                }
                GL.End();
            }

            GL.PopMatrix();
            RenderTexture.active = null;
        }
    }
}
#endif
