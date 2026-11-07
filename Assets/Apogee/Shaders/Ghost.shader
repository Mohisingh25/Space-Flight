// Translucent hologram look for parts being placed in the builder.
Shader "Apogee/Ghost"
{
    Properties
    {
        _Color ("Color", Color) = (0.3, 0.9, 1, 0.45)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Color;

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
                float rim = pow(1 - saturate(dot(n, V)), 2);
                float shade = 0.55 + 0.45 * saturate(dot(n, normalize(float3(0.4, 0.8, -0.3))));
                float3 c = _Color.rgb * shade + rim * _Color.rgb * 0.8;
                return float4(c, saturate(_Color.a + rim * 0.35));
            }
            ENDCG
        }
    }
    Fallback Off
}
