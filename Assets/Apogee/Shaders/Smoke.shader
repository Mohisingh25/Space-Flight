// Soft round particles (exhaust smoke, clouds, explosion debris). Uses particle vertex colour.
Shader "Apogee/Smoke"
{
    Properties
    {
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Softness ("Softness", Range(0.5, 4)) = 1.6
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Color;
            float _Softness;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            float4 frag (v2f i) : SV_Target
            {
                float2 d = i.uv - 0.5;
                float r = length(d) * 2;
                float a = pow(saturate(1 - r), _Softness);
                // lumpy edge so puffs don't look like perfect discs
                float lump = 0.85 + 0.15 * hash(floor(i.uv * 6));
                float shade = 0.82 + 0.18 * saturate(0.5 - d.y);
                return float4(i.color.rgb * shade, i.color.a * a * lump);
            }
            ENDCG
        }
    }
    Fallback Off
}
