// Assets/Scripts/_Impostores~/ImpostorUnlit.shader  (STAGING/DRAFT — carpeta ~ no se compila)
// ─────────────────────────────────────────────────────────────────────────────
//  Unlit de referencia para el impostor: muestrea la celda del atlas indicada por
//  _UvCell (x,y,w,h) que el componente ImpostorBillboard fija por MaterialPropertyBlock.
//  El billboard y la selección de vista se hacen en CPU (en el componente), así que
//  aquí solo hay un texturizado + alpha clip.
//
//  ⚠ HDRP: este shader es builtin/CG → en HDRP saldría magenta. Para producción,
//  recrea esta lógica como una ShaderGraph "HDRP/Unlit": MainTex = _Atlas, UV =
//  _UvCell.xy + uv*_UvCell.zw, Alpha Clip Threshold = _Cutoff. Se deja en CG como
//  referencia legible del algoritmo (y para previsualizar en builtin/URP).
// ─────────────────────────────────────────────────────────────────────────────
Shader "Alsasua/ImpostorUnlit"
{
    Properties
    {
        _Atlas  ("Atlas albedo", 2D) = "white" {}
        _UvCell ("UV Cell (x,y,w,h)", Vector) = (0,0,1,1)
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "IgnoreProjector"="True" }
        LOD 100
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f     { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            sampler2D _Atlas;
            float4    _UvCell;   // x,y = origen; z,w = tamaño (normalizado) de la celda
            float     _Cutoff;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = _UvCell.xy + i.uv * _UvCell.zw;
                fixed4 c = tex2D(_Atlas, uv);
                clip(c.a - _Cutoff);
                return c;
            }
            ENDCG
        }
    }
    Fallback "Unlit/Transparent"
}
