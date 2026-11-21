// 바닥 아이템 등급 외곽선 - 마스크 (2026-11-21).
// 아이템 메시가 화면에 보이는 픽셀에 스텐실 비트 64를 기록만 함 (색·깊이 안 씀).
// Custom/RarityOutline이 이 비트가 없는 곳에만 그려서 → 아이템 전체(모든 부품 합친) 실루엣 바깥에만 선이 남음.
//  - 부품 경계·안쪽 디테일에는 선이 안 생김 (모두 마스크 안쪽이라)
//  - 비트 64만 씀(WriteMask) → 다른 스텐실(실내 8, 시야/플레이어 1)과 안 겹침
Shader "Custom/RarityOutlineMask"
{
    SubShader
    {
        // 불투명 물체(아이템 본체)가 다 그려진 뒤, 외곽선(Transparent)보다 먼저
        Tags { "Queue" = "Geometry+20" "RenderType" = "Opaque" "IgnoreProjector" = "True" }
        Pass
        {
            ZWrite Off
            ZTest LEqual
            Offset -1, -1   // 본체와 같은 깊이 → 확실히 통과하게 살짝 앞으로
            ColorMask 0
            Cull Back
            Stencil { Ref 64 WriteMask 64 Comp Always Pass Replace }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert (float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag () : SV_Target { return 0; }
            ENDCG
        }
    }
}
