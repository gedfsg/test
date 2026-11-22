// 바닥 아이템 등급 외곽선 - 스프라이트(아이콘 판) 아이템용 (2026-11-17, 2026-11-21 재작업).
// (2026-11-21) 1.12배 확대 복제 대신, 원본과 같은 크기로 겹쳐 그리고 셰이더에서 알파를 화면 _WidthPx 픽셀만큼 넓혀
//  "넓힌 모양 - 원래 모양" 바깥 테두리만 칠함 → 거리와 상관없이 화면상 두께 일정, 아이콘 안쪽에는 선 없음.
//  (Tight 스프라이트 메시라 DropRarityOutline이 1.3배 여백 판(_UVRect 밖은 투명)을 따로 만들어 이 셰이더로 그림)
// (이전 2026-11-17) 알파 모양 전체를 등급색으로 칠해 1.12배 크게 뒤에 깔았음
Shader "Custom/RaritySilhouette"
{
    Properties
    {
        _MainTex ("Sprite", 2D) = "white" {}
        [HDR] _Color ("Color", Color) = (1, 1, 1, 0.9) // HDR 발광 색
        _WidthPx ("Width (pixels)", Float) = 2
        _UVRect ("Sprite UV rect (minX,minY,maxX,maxY)", Vector) = (0, 0, 1, 1)
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
            half4 _Color;
            float _WidthPx;
            float4 _UVRect;
            // 스프라이트 사각형 밖(여백 판 부분)은 투명으로 취급
            float A(float2 uv)
            {
                float inside = step(_UVRect.x, uv.x) * step(uv.x, _UVRect.z) * step(_UVRect.y, uv.y) * step(uv.y, _UVRect.w);
                return tex2D(_MainTex, uv).a * inside;
            }
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert (appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }
            half4 frag (v2f i) : SV_Target
            {
                float self = step(0.1, A(i.uv));
                // 화면 1픽셀이 UV로 얼마인지 (ddx/ddy) → _WidthPx 픽셀 거리의 8방향 샘플
                float2 dx = ddx(i.uv) * _WidthPx, dy = ddy(i.uv) * _WidthPx;
                float a = 0;
                a = max(a, A(i.uv + dx));
                a = max(a, A(i.uv - dx));
                a = max(a, A(i.uv + dy));
                a = max(a, A(i.uv - dy));
                a = max(a, A(i.uv + (dx + dy) * 0.7071));
                a = max(a, A(i.uv + (dx - dy) * 0.7071));
                a = max(a, A(i.uv - (dx + dy) * 0.7071));
                a = max(a, A(i.uv - (dx - dy) * 0.7071));
                float ring = step(0.1, a) * (1 - self); // 바깥 테두리만
                return half4(_Color.rgb, _Color.a * ring);
            }
            ENDCG
        }
    }
}
