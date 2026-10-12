// 실내 화면 마스크 1단계: "보이는 표면이 실내 상자(볼륨) 안에 있는 픽셀"만 스텐실 비트(8)를 켬.
// 그림자 볼륨과 같은 원리 - 상자 앞면은 표면보다 앞에 있고(깊이 통과), 뒷면은 표면보다 뒤에 있어야(깊이 실패) 안쪽.
//  Pass 1: 상자 앞면이 깊이 통과 → 비트 켬 (상자 앞을 가리는 옆 건물 지붕이 있으면 실패 → 안 켜짐)
//  Pass 2: 상자 뒷면이 깊이 통과 → 표면이 상자 뒤쪽(바깥) → 비트 끔
// 색/깊이는 안 씀. 2단계(IndoorScreenMask)가 비트 없는 픽셀을 검정으로 덮음.
// 다른 셰이더(PlayerXRay 등)가 쓰는 스텐실 1번 비트와 안 겹치게 8번 비트만 읽고 씀.
Shader "Hidden/IndoorVolumeMask"
{
    SubShader
    {
        Tags { "Queue"="Overlay-10" "RenderType"="Opaque" "IgnoreProjector"="True" }
        ColorMask 0
        ZWrite Off
        ZTest LEqual

        Pass
        {
            Cull Back
            Stencil { Ref 8 WriteMask 8 Comp Always Pass Replace }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag() : SV_Target { return 0; }
            ENDCG
        }
        Pass
        {
            Cull Front
            Stencil { Ref 0 WriteMask 8 Comp Always Pass Replace }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag() : SV_Target { return 0; }
            ENDCG
        }
    }
}
