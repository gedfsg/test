// 실내 화면 마스크 2단계: 화면 전체를 덮는 사각형. 스텐실 8번 비트가 없는 픽셀(= 실내 상자 밖 표면)을 검정으로 칠함.
// 정점을 클립 공간에 바로 찍어서 카메라 위치/회전과 상관없이 항상 화면 전체를 덮음.
Shader "Hidden/IndoorScreenMask"
{
    Properties { _Color ("Color", Color) = (0,0,0,1) }
    SubShader
    {
        Tags { "Queue"="Overlay-5" "RenderType"="Opaque" "IgnoreProjector"="True" }
        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off
            Stencil { Ref 8 ReadMask 8 Comp NotEqual }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color;
            float4 vert(float4 v : POSITION) : SV_POSITION { return float4(v.xy * 2.0, 0.5, 1.0); }
            fixed4 frag() : SV_Target { return _Color; }
            ENDCG
        }
    }
}
