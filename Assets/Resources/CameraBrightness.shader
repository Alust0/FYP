// BuildWebcamDriver's copy of a webcam frame: flipped top row first (or turned 180° with _UpsideDown), brightness scaled by _Gain
Shader "Hidden/CameraBrightness"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Gain ("Gain", Float) = 1
        _UpsideDown ("Upside down", Float) = 0
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
            float _UpsideDown; // 1 for a webcam mounted upside down

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata_img v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                // Top row first; a camera upside down is turned 180°, which mirrors it left to right instead
                o.uv = lerp(float2(v.texcoord.x, 1 - v.texcoord.y), float2(1 - v.texcoord.x, v.texcoord.y), _UpsideDown);
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
