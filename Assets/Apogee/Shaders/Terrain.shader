Shader "Apogee/Terrain"
{
    Properties
    {
        _MainTex ("Planet map", 2D) = "white" {}
        _DetailTex ("Detail", 2D) = "gray" {}
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _SunDir ("Sun Direction (world)", Vector) = (0, 1, 0, 0)
        _FogColor ("Haze Color", Color) = (0.6, 0.72, 0.9, 1)
        _FogDensity ("Haze Density", Float) = 0.00002
        _Ambient ("Ambient", Range(0, 0.5)) = 0.06
        _DetailStrength ("Detail Strength", Range(0, 1)) = 0.55
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex, _DetailTex;
            float4 _Tint, _SunDir, _FogColor;
            float _FogDensity, _Ambient, _DetailStrength;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float2 duv : TEXCOORD1; float3 n : TEXCOORD2; float3 wp : TEXCOORD3; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.duv = v.uv2;
                o.n = UnityObjectToWorldNormal(v.normal);
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float3 n = normalize(i.n);
                float3 toCam = normalize(_WorldSpaceCameraPos - i.wp);
                if (dot(n, toCam) < 0) n = -n;                       // robust to winding: always light the visible side
                float3 L = normalize(_SunDir.xyz);
                float3 alb = tex2D(_MainTex, i.uv).rgb * _Tint.rgb;
                // orbital imagery is dark and hazy-free; lift it to how the ground looks from low altitude
                alb = saturate(pow(alb, 0.8) * 1.35);
                float d1 = tex2D(_DetailTex, i.duv).r;
                float d2 = tex2D(_DetailTex, i.duv * 0.071).r;
                float d3 = tex2D(_DetailTex, i.duv * 0.0049).r;
                float detail = (d1 * 0.4 + d2 * 0.35 + d3 * 0.25) * 2.0;
                alb *= lerp(1.0, detail, _DetailStrength);
                float ndl = saturate(dot(n, L));
                float3 col = alb * (ndl * 1.05 + _Ambient);
                float dist = length(i.wp - _WorldSpaceCameraPos);
                float fog = 1 - exp(-dist * _FogDensity);
                col = lerp(col, _FogColor.rgb * saturate(L.y * 0 + dot(n, L) * 0.8 + 0.25), saturate(fog));
                return float4(col, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
