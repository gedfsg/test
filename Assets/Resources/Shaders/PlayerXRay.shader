// 가려졌을 때만 그리는 X-Ray. 단, 플레이어 자신이 보이는 픽셀(스텐실=1)에는 안 그림.
Shader "Custom/PlayerXRay"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color   ("Tint", Color) = (1, 1, 1, 1)
        _Alpha   ("Alpha (occluded)", Range(0,1)) = 0.85
    }
    SubShader
    {
        Tags { "Queue"="Transparent+100" "RenderType"="Transparent" "IgnoreProjector"="True" }

        Pass
        {
            Name "XRayPass"
            ZTest Greater       // 가려졌을 때만 그림
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Back

            Stencil
            {
                Ref 1
                Comp NotEqual   // 플레이어가 보이는 픽셀에는 안 그림 (자기 자신 가려서 안 그려지는 문제 해결)
            }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4    _MainTex_ST;
            fixed4    _Color;
            float     _Alpha;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f     { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv     = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * _Color;
                c.a = _Alpha;
                return c;
            }
            ENDCG
        }
    }
}
