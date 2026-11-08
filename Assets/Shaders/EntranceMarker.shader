// 건물 입구 색 매트 (EntranceMarkers). 지붕/처마에 가려도 위치가 보이게 2패스:
//  1) 가려진 부분 (ZTest Greater) → 연하게 (_OccludedAlpha 배)
//  2) 보이는 부분 (ZTest LEqual)  → 원래 알파
// 교회/병원은 입구 앞이 큰 지붕 아래라 일반 투명 재질로는 탑뷰에서 안 보였음.
Shader "Custom/EntranceMarker"
{
    Properties
    {
        _Color         ("Color", Color) = (1, 1, 1, 0.45)
        _OccludedAlpha ("Alpha multiplier (occluded)", Range(0,1)) = 0.45
    }
    SubShader
    {
        Tags { "Queue"="Transparent+50" "RenderType"="Transparent" "IgnoreProjector"="True" }
        ZWrite Off
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        CGINCLUDE
        #include "UnityCG.cginc"
        fixed4 _Color;
        float  _OccludedAlpha;
        struct appdata { float4 vertex : POSITION; };
        struct v2f     { float4 vertex : SV_POSITION; };
        v2f vert (appdata v) { v2f o; o.vertex = UnityObjectToClipPos(v.vertex); return o; }
        ENDCG

        Pass
        {
            Name "Occluded"
            ZTest Greater
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            fixed4 frag (v2f i) : SV_Target { return fixed4(_Color.rgb, _Color.a * _OccludedAlpha); }
            ENDCG
        }

        Pass
        {
            Name "Visible"
            ZTest LEqual
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            fixed4 frag (v2f i) : SV_Target { return _Color; }
            ENDCG
        }
    }
}
