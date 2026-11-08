Shader "Apogee/Sky"
{
    Properties
    {
        _MainTex ("Stars", 2D) = "black" {}
        _Brightness ("Brightness", Range(0, 2)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Background" "Queue" = "Background" }
        Cull Front ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex; float _Brightness;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert (appdata_base v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord.xy; return o; }
            float4 frag (v2f i) : SV_Target { return float4(tex2D(_MainTex, i.uv).rgb * _Brightness, 1); }
            ENDCG
        }
    }
    Fallback Off
}
