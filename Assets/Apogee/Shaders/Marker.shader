// Always-on-top markers (centre of mass / thrust / pressure, attach nodes).
Shader "Apogee/Marker"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.85, 0.1, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Overlay" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest Always
        Cull Back
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Color;

            struct v2f { float4 pos : SV_POSITION; float3 n : TEXCOORD0; };

            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.n = mul((float3x3)UNITY_MATRIX_V, UnityObjectToWorldNormal(v.normal));
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float facing = saturate(normalize(i.n).z); // view space: +Z faces the camera
                float shade = 0.6 + 0.4 * facing;
                return float4(_Color.rgb * shade, _Color.a);
            }
            ENDCG
        }
    }
    Fallback Off
}
