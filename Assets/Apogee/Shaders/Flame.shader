// Procedural rocket exhaust plume (additive). Quad with uv.y = 0 at the nozzle, 1 at the tail.
Shader "Apogee/Flame"
{
    Properties
    {
        _Color ("Plume Color", Color) = (1, 0.55, 0.2, 1)
        _Core ("Core Color", Color) = (1, 0.95, 0.85, 1)
        _Intensity ("Intensity", Range(0, 4)) = 1.5
        _Expansion ("Vacuum Expansion", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+20" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Color, _Core;
            float _Intensity, _Expansion;

            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                return o;
            }

            float hash(float n) { return frac(sin(n) * 43758.5453); }
            float noise1(float x) { float i = floor(x); float f = frac(x); return lerp(hash(i), hash(i + 1), f * f * (3 - 2 * f)); }

            float4 frag (v2f i) : SV_Target
            {
                float v = i.uv.y;
                float u = (i.uv.x - 0.5) * 2;
                // plume widens with distance (more in vacuum)
                float width = lerp(0.35, 1.0, saturate(v * (1.2 + _Expansion * 2)));
                float radial = saturate(1 - abs(u) / width);
                radial = radial * radial;
                float t = _Time.y;
                float flicker = 0.8 + 0.2 * noise1(t * 23 + v * 9) + 0.1 * noise1(t * 41 - v * 17);
                float fade = pow(saturate(1 - v), 1.6 - _Expansion * 0.8);
                // shock diamonds near the nozzle at sea level
                float diamonds = (1 - _Expansion) * 0.35 * pow(abs(sin(v * 28)), 6) * saturate(1 - v * 2.2);
                float core = saturate(1 - v * 3.5) * pow(radial, 3);
                float3 col = _Color.rgb * radial * fade * flicker + _Core.rgb * (core + diamonds * radial);
                return float4(col * _Intensity, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
