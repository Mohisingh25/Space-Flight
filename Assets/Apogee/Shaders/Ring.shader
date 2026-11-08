Shader "Apogee/Ring"
{
    Properties
    {
        _MainTex ("Rings (A)", 2D) = "white" {}
        _SunDir ("Sun Direction (world)", Vector) = (0, 1, 0, 0)
        _Brightness ("Brightness", Range(0, 3)) = 1.2
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex; float4 _SunDir; float _Brightness;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 n : TEXCOORD1; };
            v2f vert (appdata_base v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord.xy; o.n = UnityObjectToWorldNormal(v.normal); return o; }
            float4 frag (v2f i) : SV_Target
            {
                float4 c = tex2D(_MainTex, i.uv);
                float lit = 0.35 + 0.65 * abs(dot(normalize(i.n), normalize(_SunDir.xyz)));
                return float4(c.rgb * lit * _Brightness, c.a);
            }
            ENDCG
        }
    }
    Fallback Off
}
