// VertexColorBuildings.shader
// Shader para mostrar el mesh de CloudCompare con sus vertex colors reales.
// Copiarlo en Assets/Shaders/ del proyecto Altsasu_Manifa.
// Funciona tanto en HDRP como en URP como Built-in (selecciona el apropiado).

Shader "Altsasu/VertexColorBuildings"
{
    Properties
    {
        _BaseColor   ("Tinte base",   Color)  = (1,1,1,1)
        _Brightness  ("Brillo",       Range(0.5, 2.0)) = 1.0
        _Roughness   ("Rugosidad",    Range(0.0, 1.0)) = 0.85
        _AmbientOcc  ("AO simulado",  Range(0.0, 1.0)) = 0.15
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.0

        struct Input
        {
            float4 vertColor : COLOR;
            float3 worldPos;
        };

        half  _Brightness;
        half  _Roughness;
        half  _AmbientOcc;
        fixed4 _BaseColor;

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.vertColor = v.color;
            o.worldPos  = mul(unity_ObjectToWorld, v.vertex).xyz;
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            // Colores del vertex del LiDAR (RGB capturado por el escáner)
            fixed4 c = IN.vertColor * _BaseColor * _Brightness;

            // Simular AO suave basado en la altura (parte baja más oscura)
            float heightFactor = saturate(IN.worldPos.y / 25.0);
            c.rgb *= lerp(1.0 - _AmbientOcc, 1.0, heightFactor);

            o.Albedo    = c.rgb;
            o.Metallic  = 0.0;
            o.Smoothness = 1.0 - _Roughness;
            o.Alpha     = 1.0;
        }
        ENDCG
    }

    // Fallback para cuando el shader no compila
    FallBack "Diffuse"
}
