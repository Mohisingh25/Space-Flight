Shader "Apogee/Cloud"
{
    Properties
    {
        _MainTex ("Puff", 2D) = "white" {}
        _SunColor ("Sun Light", Color) = (1, 0.97, 0.92, 1)
        _Ambient ("Ambient", Color) = (0.55, 0.62, 0.72, 1)
        _SunUp ("Sun elevation (-1..1)", Float) = 1
        _NearFade ("Near fade distance", Float) = 120
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _SunColor, _Ambient;
            float _SunUp, _NearFade;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; float dist : TEXCOORD1; };
            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                o.dist = length(mul(unity_ObjectToWorld, v.vertex).xyz - _WorldSpaceCameraPos);
                return o;
            }
            float4 frag (v2f i) : SV_Target
            {
                float4 t = tex2D(_MainTex, i.uv);
                // tops lit by the sun, bases in their own shadow
                float top = saturate(i.uv.y * 1.2 - 0.1);
                float lit = lerp(0.55, 1.0, top) * saturate(_SunUp * 2.5 + 0.35);
                float3 col = _Ambient.rgb * 0.55 + _SunColor.rgb * lit * t.r;
                float a = t.a * i.color.a;
                // fade out when the camera flies through a puff instead of hitting a wall of white
                a *= saturate(i.dist / _NearFade - 0.15);
                return float4(col * i.color.rgb, a);
            }
            ENDCG
        }
    }
    Fallback Off
}
