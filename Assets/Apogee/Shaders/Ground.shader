// Planet surface seen from the side: vertex colours (biome / ocean along the equator) with a
// tiled detail texture that fades out as the camera zooms away, lit by a fixed sun factor.
Shader "Apogee/Ground"
{
    Properties
    {
        _MainTex ("Detail", 2D) = "white" {}
        _Detail ("Detail Strength", Range(0, 1)) = 1
        _Light ("Light", Range(0, 2)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry-10" }
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _Detail, _Light;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float3 d = tex2D(_MainTex, i.uv).rgb;
                float3 col = i.color.rgb * lerp(float3(1, 1, 1), d * 1.25, _Detail) * _Light;
                return float4(col, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
