// BuildWebcamDriver's copy of a webcam frame: flipped top row first, brightness scaled by _Gain
Shader "Hidden/CameraBrightness"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Gain ("Gain", Float) = 1
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Gain;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata_img v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = float2(v.texcoord.x, 1 - v.texcoord.y);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv);
                c.rgb = saturate(c.rgb * _Gain);
                return c;
            }
            ENDCG
        }
    }
}
