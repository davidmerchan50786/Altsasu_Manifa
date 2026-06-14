// Assets/Scripts/Editor/HeatmapInstanced.shader
// ═══════════════════════════════════════════════════════════════════════════
//  HEATMAP INSTANCED — unlit transparente con color POR INSTANCIA
//
//  Pintado SOLO desde VisualizadorHeatmap (editor) vía Graphics.DrawMeshInstanced
//  contra la cámara del SceneView. ZTest Always → overlay siempre visible sobre el
//  terreno. El color de cada celda viaja en el buffer de instancia (_Color), así un
//  único draw cubre hasta 1023 celdas → coste de pintado despreciable.
//
//  NOTA HDRP: es un pase unlit built-in. En HDRP puro el SRP puede ignorar mallas
//  dibujadas con shaders no-HDRP; por eso la ventana ofrece un fallback con Handles
//  (modo "GPU off") que SIEMPRE renderiza. Si ves las celdas con DrawMeshInstanced,
//  usa GPU; si no, desmarca "GPU" y tira de Handles.
// ═══════════════════════════════════════════════════════════════════════════
Shader "Hidden/Alsasua/HeatmapInstanced"
{
    Properties
    {
        _Alpha ("Alpha global", Range(0,1)) = 0.55
    }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" "IgnoreProjector"="True" "ForceNoShadowCasting"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 col : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
            UNITY_INSTANCING_BUFFER_END(Props)

            float _Alpha;

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.pos = UnityObjectToClipPos(v.vertex);
                float4 c = UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
                c.a *= _Alpha;
                o.col = c;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                return i.col;
            }
            ENDCG
        }
    }
}
