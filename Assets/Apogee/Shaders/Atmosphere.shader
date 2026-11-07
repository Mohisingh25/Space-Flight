// Additive limb glow on a shell slightly larger than the planet, brighter on the sunlit side.
Shader "Apogee/Atmosphere"
{
    Properties
    {
        _Color ("Color", Color) = (0.3, 0.55, 1, 1)
        _SunDir ("Sun Direction (world)", Vector) = (0, 0, 1, 0)
        _Intensity ("Intensity", Range(0, 4)) = 1.4
        _Power ("Falloff", Range(1, 8)) = 3.5
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Back
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Color, _SunDir;
            float _Intensity, _Power;

            struct v2f { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float3 wp : TEXCOORD1; };

            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.n = UnityObjectToWorldNormal(v.normal);
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float3 n = normalize(i.n);
                float3 V = normalize(_WorldSpaceCameraPos - i.wp);
                float rim = pow(1 - saturate(dot(n, V)), _Power);
                float lit = saturate(dot(n, normalize(_SunDir.xyz)) + 0.3);
                float3 sunset = lerp(float3(1.0, 0.55, 0.3), _Color.rgb, saturate(dot(n, normalize(_SunDir.xyz)) * 3 + 0.5));
                return float4(sunset * rim * lit * _Intensity, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
