// 바닥 아이템 등급 외곽선 - 스프라이트(아이콘 판) 아이템용 (2026-11-17).
// 텍스처의 알파 모양만 등급 색으로 칠함 (아이콘 뒤에 조금 크게 깔아서 테두리처럼 보이게).
Shader "Custom/RaritySilhouette"
{
    Properties
    {
        _MainTex ("Sprite", 2D) = "white" {}
        [HDR] _Color ("Color", Color) = (1, 1, 1, 0.9) // (2026-11-18) HDR 발광 색
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "CanUseSpriteAtlas" = "True" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            half4 _Color; // (2026-11-18) HDR 값이 잘리지 않게
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert (appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }
            half4 frag (v2f i) : SV_Target
            {
                fixed a = tex2D(_MainTex, i.uv).a;
                return half4(_Color.rgb, _Color.a * step(0.1, a));
            }
            ENDCG
        }
    }
}
