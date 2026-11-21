// 바닥 아이템 등급 외곽선 (2026-11-17, 2026-11-21 재작업).
// (2026-11-21) "스티커 실루엣" 방식:
//  1) Custom/RarityOutlineMask가 아이템 메시가 보이는 픽셀에 스텐실 비트 64를 먼저 씀
//  2) 이 셰이더는 메시를 화면 기준 _WidthPx 픽셀만큼 바깥으로 부풀려 그리되, 비트 64가 없는 곳에만 그림
//     → 아이템 전체 실루엣 바깥에 한 줄만 남고, 부품 경계·내부 구멍(선 두께보다 작은 것)에는 선이 안 생김
//  3) 그린 픽셀에 비트 64를 다시 써서 한 픽셀에 두 번 안 칠함 → 겹친 곳이 진해지지 않고 알파가 균일
//  두께는 클립 공간에서 픽셀 단위로 계산 → 카메라 거리와 무관하게 화면상 일정.
// (이전 2026-11-17) Cull Front 뒤집힌 껍질 + _Width(화면 높이 대비) → 부품마다 선이 생겨 지저분했음
Shader "Custom/RarityOutline"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (1, 1, 1, 0.9) // HDR 발광 색 (Emissive 1.5배) - 조명·안개 영향 없음
        _WidthPx ("Width (pixels)", Float) = 2
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Cull Off        // 앞·뒷면 모두 → 모서리 틈 줄임 (겹침은 스텐실이 막음)
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha
            Stencil { Ref 64 ReadMask 64 WriteMask 64 Comp NotEqual Pass Replace }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            half4 _Color; // half: 1 넘는 HDR 발광 값이 잘리지 않게
            float _WidthPx;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; };
            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                float3 n = mul((float3x3)UNITY_MATRIX_VP, UnityObjectToWorldNormal(v.normal));
                float2 off = normalize(n.xy + 1e-5);
                // 픽셀 → NDC(-1~1) 변환: 2/화면크기. w를 곱해서 원근 나눗셈 뒤에도 같은 픽셀 두께
                o.pos.xy += off * _WidthPx * 2.0 / _ScreenParams.xy * o.pos.w;
                return o;
            }
            half4 frag (v2f i) : SV_Target { return _Color; }
            ENDCG
        }
    }
}
