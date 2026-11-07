// Additive star field: camera-facing quads with a soft glow sprite, tinted by vertex colour.
Shader "Apogee/Stars"
{
    Properties
    {
        _MainTex ("Glow", 2D) = "white" {}
        _Brightness ("Brightness", Range(0, 4)) = 1.2
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Background+10" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Brightness;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float a = tex2D(_MainTex, i.uv).a;
                return float4(i.color.rgb * a * i.color.a * _Brightness, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
