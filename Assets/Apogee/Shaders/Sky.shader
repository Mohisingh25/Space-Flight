Shader "Apogee/Sky"
{
    Properties
    {
        _MainTex ("Stars", 2D) = "black" {}
        _Brightness ("Star brightness", Range(0, 2)) = 1
        _Zenith ("Zenith colour", Color) = (0.16, 0.36, 0.78, 1)
        _Horizon ("Horizon colour", Color) = (0.62, 0.76, 0.92, 1)
        _Ground ("Below-horizon haze", Color) = (0.5, 0.58, 0.66, 1)
        _Up ("Local up (world)", Vector) = (0, 1, 0, 0)
        _SunDir ("Sun direction (world)", Vector) = (0, 1, 0, 0)
        _SunGlow ("Sun glow colour", Color) = (1, 0.92, 0.75, 1)
        _AtmoAmount ("Atmosphere amount 0..1", Range(0, 1)) = 1
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
            sampler2D _MainTex;
            float _Brightness, _AtmoAmount;
            float4 _Zenith, _Horizon, _Ground, _Up, _SunDir, _SunGlow;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 dir : TEXCOORD1; };
            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                o.dir = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 0)).xyz;
                return o;
            }
            float4 frag (v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float3 up = normalize(_Up.xyz);
                float3 sun = normalize(_SunDir.xyz);
                float h = dot(d, up);
                // Rayleigh-like gradient: deep blue overhead, pale near the horizon, hazy below it
                float3 sky = h >= 0 ? lerp(_Horizon.rgb, _Zenith.rgb, pow(saturate(h), 0.45)) : lerp(_Horizon.rgb, _Ground.rgb, saturate(-h * 6));
                float s = saturate(dot(d, sun));
                sky += _SunGlow.rgb * (pow(s, 8) * 0.35 + pow(s, 200) * 1.5);
                float3 stars = tex2D(_MainTex, i.uv).rgb * _Brightness;
                float3 col = sky * _AtmoAmount + stars * (1 - saturate(_AtmoAmount * 1.4));
                // the Sun's disc itself is drawn by the Sun body; above the atmosphere only stars remain
                return float4(col, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
