#if VISTA
using Pinwheel.Vista.Graphics;
using System.Collections;
using UnityEngine;

namespace Pinwheel.Vista.Graph
{
    [NodeMetadata(
        title = "Shape",
        path = "Base Shape/Shape",
        icon = "",
        documentation = "",
        keywords = "shape, square, disc, hemisphere, cone, paraboloid, bell, thorn, pyramid, brick, torus, hex, hexagon",
        description = "Generate a primitive shape.\n<ss>Tips: Search for the shape directly (eg: square, disc, hemisphere, cone, etc.)</ss>")]
    public class ShapeNode : ImageNodeBase, ISetupWithHint
    {
        public readonly MaskSlot outputSlot = new MaskSlot("Output", SlotDirection.Output, 100);
        public enum Shape
        {
            Square, Disc, Hemisphere, Cone, Paraboloid, Bell, Thorn, Pyramid, Brick, Torus, Hexagon
        }

        [SerializeField]
        private Shape m_shape;
        public Shape shape
        {
            get
            {
                return m_shape;
            }
            set
            {
                m_shape = value;
            }
        }

        [SerializeField]
        private Vector2 m_scale;
        public Vector2 scale
        {
            get
            {
                return m_scale;
            }
            set
            {
                Vector2 v = value;
                m_scale = new Vector2(Mathf.Max(0.001f, v.x), Mathf.Max(0.001f, v.y));
            }
        }

        [SerializeField]
        private float m_innerSize;
        public float innerSize
        {
            get
            {
                return m_innerSize;
            }
            set
            {
                m_innerSize = Mathf.Clamp01(value);
            }
        }

        private static readonly string SHADER_NAME = "Hidden/Vista/Graph/Shape";
        private static readonly int UV_TO_SHAPE_MATRIX = Shader.PropertyToID("_UvToShapeMatrix");
        private static readonly int INNER_SIZE = Shader.PropertyToID("_InnerSize");

        private Material m_material;

        private static readonly Vector2[] hexagon = new Vector2[]
        {
            new Vector2(Mathf.Cos(0*Mathf.Deg2Rad), Mathf.Sin(0*Mathf.Deg2Rad)),
            new Vector2(Mathf.Cos(60*Mathf.Deg2Rad), Mathf.Sin(60*Mathf.Deg2Rad)),
            new Vector2(Mathf.Cos(120*Mathf.Deg2Rad), Mathf.Sin(120*Mathf.Deg2Rad)),

            new Vector2(Mathf.Cos(180*Mathf.Deg2Rad), Mathf.Sin(180*Mathf.Deg2Rad)),
            new Vector2(Mathf.Cos(240*Mathf.Deg2Rad), Mathf.Sin(240*Mathf.Deg2Rad)),
            new Vector2(Mathf.Cos(300*Mathf.Deg2Rad), Mathf.Sin(300*Mathf.Deg2Rad)),
        };
        public ShapeNode() : base()
        {
            m_shape = Shape.Square;
            m_scale = Vector2.one;
            m_innerSize = 0.5f;
        }
        public override void ExecuteImmediate(GraphContext context)
        {
            int baseResolution = context.GetArg(Args.RESOLUTION).intValue;
            int resolution = this.CalculateResolution(baseResolution, baseResolution);
            DataPool.RtDescriptor desc = DataPool.RtDescriptor.Create(resolution, resolution);
            SlotRef outputRef = new SlotRef(m_id, outputSlot.id);
            RenderTexture targetRt = context.CreateRenderTarget(desc, outputRef);

            m_material = new Material(ShaderUtilities.Find(SHADER_NAME));
            Vector3 t = new Vector3(0.5f, 0.5f);
            Quaternion r = Quaternion.identity;
            Vector3 s = new Vector3(m_scale.x, m_scale.y, 1);
            Matrix4x4 uvToShapeMatrix = Matrix4x4.TRS(t, r, s).inverse;
            m_material.SetMatrix(UV_TO_SHAPE_MATRIX, uvToShapeMatrix);
            m_material.SetFloat(INNER_SIZE, m_innerSize);
            if (m_shape == Shape.Hexagon)
            {
                DrawHexagon(targetRt, m_material);
            }
            else
            {
                Drawing.DrawQuad(targetRt, m_material, (int)m_shape);
            }
            Object.DestroyImmediate(m_material);
        }
        public override IEnumerator Execute(GraphContext context)
        {
            ExecuteImmediate(context);
            yield return null;
        }
        public void SetupWithHint(string hint)
        {
            string[] shapeNames = System.Enum.GetNames(typeof(Shape));
            foreach (string n in shapeNames)
            {
                if (n.ToLower().StartsWith(hint))
                {
                    shape = (Shape)System.Enum.Parse(typeof(Shape), n, true);
                    break;
                }
            }
        }

        private void DrawHexagon(RenderTexture targetRt, Material mat)
        {
            Vector2[] inner = new Vector2[hexagon.Length];
            Vector2[] outer = new Vector2[hexagon.Length];
            for (int i = 0; i < hexagon.Length; ++i)
            {
                inner[i] = hexagon[i];
                inner[i].Scale(m_scale);
                inner[i] = inner[i] * innerSize * 0.5f + Vector2.one * 0.5f;

                outer[i] = hexagon[i];
                outer[i].Scale(m_scale);
                outer[i] = outer[i] * 0.5f + Vector2.one * 0.5f;
            }
            Vector2 center = Vector2.one * 0.5f;

            RenderTexture.active = targetRt;
            GL.PushMatrix();
            mat.SetPass((int)Shape.Hexagon);
            GL.LoadOrtho();

            GL.Begin(GL.TRIANGLES);
            GL.Vertex3(center.x, center.y, 1);
            GL.Vertex3(inner[0].x, inner[0].y, 1);
            GL.Vertex3(inner[5].x, inner[5].y, 1);

            GL.Vertex3(center.x, center.y, 1);
            GL.Vertex3(inner[5].x, inner[5].y, 1);
            GL.Vertex3(inner[4].x, inner[4].y, 1);

            GL.Vertex3(center.x, center.y, 1);
            GL.Vertex3(inner[4].x, inner[4].y, 1);
            GL.Vertex3(inner[3].x, inner[3].y, 1);

            GL.Vertex3(center.x, center.y, 1);
            GL.Vertex3(inner[3].x, inner[3].y, 1);
            GL.Vertex3(inner[2].x, inner[2].y, 1);

            GL.Vertex3(center.x, center.y, 1);
            GL.Vertex3(inner[2].x, inner[2].y, 1);
            GL.Vertex3(inner[1].x, inner[1].y, 1);

            GL.Vertex3(center.x, center.y, 1);
            GL.Vertex3(inner[1].x, inner[1].y, 1);
            GL.Vertex3(inner[0].x, inner[0].y, 1);
            GL.End();

            GL.Begin(GL.QUADS);
            GL.Vertex3(inner[0].x, inner[0].y, 1);
            GL.Vertex3(outer[0].x, outer[0].y, 0);
            GL.Vertex3(outer[5].x, outer[5].y, 0);
            GL.Vertex3(inner[5].x, inner[5].y, 1);

            GL.Vertex3(inner[5].x, inner[5].y, 1);
            GL.Vertex3(outer[5].x, outer[5].y, 0);
            GL.Vertex3(outer[4].x, outer[4].y, 0);
            GL.Vertex3(inner[4].x, inner[4].y, 1);

            GL.Vertex3(inner[4].x, inner[4].y, 1);
            GL.Vertex3(outer[4].x, outer[4].y, 0);
            GL.Vertex3(outer[3].x, outer[3].y, 0);
            GL.Vertex3(inner[3].x, inner[3].y, 1);

            GL.Vertex3(inner[3].x, inner[3].y, 1);
            GL.Vertex3(outer[3].x, outer[3].y, 0);
            GL.Vertex3(outer[2].x, outer[2].y, 0);
            GL.Vertex3(inner[2].x, inner[2].y, 1);

            GL.Vertex3(inner[2].x, inner[2].y, 1);
            GL.Vertex3(outer[2].x, outer[2].y, 0);
            GL.Vertex3(outer[1].x, outer[1].y, 0);
            GL.Vertex3(inner[1].x, inner[1].y, 1);

            GL.Vertex3(inner[1].x, inner[1].y, 1);
            GL.Vertex3(outer[1].x, outer[1].y, 0);
            GL.Vertex3(outer[0].x, outer[0].y, 0);
            GL.Vertex3(inner[0].x, inner[0].y, 1);
            GL.End();

            GL.PopMatrix();
            RenderTexture.active = null;
        }
    }
}
#endif


