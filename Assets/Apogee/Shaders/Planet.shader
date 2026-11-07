// Planet surface: albedo (alpha = ocean mask), soft day/night terminator, ocean sun glint,
// drifting cloud layer and an atmospheric rim. Unlit pass so it works in URP and Built-in.
Shader "Apogee/Planet"
{
    Properties
    {
        _MainTex ("Albedo (A = ocean mask)", 2D) = "white" {}
        _CloudTex ("Clouds (A)", 2D) = "black" {}
        _SunDir ("Sun Direction (world)", Vector) = (0, 0, 1, 0)
        _CloudShift ("Cloud Shift", Float) = 0
        _CloudStrength ("Cloud Strength", Range(0, 1.5)) = 1
        _AtmoColor ("Atmosphere Color", Color) = (0.35, 0.6, 1, 1)
        _AtmoStrength ("Atmosphere Strength", Range(0, 3)) = 1
        _Specular ("Ocean Specular", Range(0, 3)) = 1
        _Ambient ("Ambient", Range(0, 0.3)) = 0.015
        _NightLights ("Night Lights", Range(0, 2)) = 0.6
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _CloudTex;
            float4 _SunDir;
            float _CloudShift, _CloudStrength, _AtmoStrength, _Specular, _Ambient, _NightLights;
            float4 _AtmoColor;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 n : TEXCOORD1;
                float3 wp : TEXCOORD2;
            };

            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                o.n = UnityObjectToWorldNormal(v.normal);
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            float hash21(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }

            float4 frag (v2f i) : SV_Target
            {
                float3 n = normalize(i.n);
                float3 L = normalize(_SunDir.xyz);
                float3 V = normalize(_WorldSpaceCameraPos - i.wp);
                float4 alb = tex2D(_MainTex, i.uv);
                float ndl = dot(n, L);
                float day = saturate(ndl * 1.3 + 0.12);
                float clouds = saturate(tex2D(_CloudTex, i.uv + float2(_CloudShift, 0)).a * _CloudStrength);
                // soft cloud shadows on the ground
                float shadow = tex2D(_CloudTex, i.uv + float2(_CloudShift + 0.002, 0.001)).a * 0.35 * _CloudStrength;

                float3 col = alb.rgb * day * (1 - shadow);
                float3 H = normalize(L + V);
                float spec = pow(saturate(dot(n, H)), 90) * alb.a * _Specular * (1 - clouds) * saturate(ndl * 4);
                col += spec * float3(1.0, 0.92, 0.8);

                // City lights on the night side of land
                float land = 1 - alb.a;
                float cityMask = step(0.78, hash21(floor(i.uv * float2(900, 450)))) * land * step(0.08, alb.g);
                col += float3(1.0, 0.72, 0.38) * cityMask * saturate(-ndl * 4) * _NightLights * (1 - clouds);

                float cloudLight = saturate(ndl * 1.2 + 0.08);
                col = lerp(col, float3(1, 1, 1) * cloudLight, clouds);

                float fres = pow(1 - saturate(dot(n, V)), 3);
                col += _AtmoColor.rgb * fres * _AtmoStrength * saturate(ndl + 0.35);
                col += alb.rgb * _Ambient;
                return float4(col, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
