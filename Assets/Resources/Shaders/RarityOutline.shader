// 바닥 아이템 등급 외곽선 (2026-11-17). 뒤집힌 껍질(inverted hull) 방식:
//  메시를 법선 방향으로 부풀린 뒷면만 그려서 원본 가장자리 밖으로 삐져나온 부분이 외곽선이 됨.
//  부풀리는 양을 클립 공간(화면 픽셀 기준)으로 계산 → 카메라 거리와 상관없이 외곽선 두께가 일정.
//  _Width = 화면 높이 대비 두께 (0.004 ≈ 1080p에서 약 2px), 반투명(색 알파), 깊이 쓰기 안 함.
Shader "Custom/RarityOutline"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (1, 1, 1, 0.9) // (2026-11-18) HDR 발광 색 (Emissive 1.5배) - 조명·안개 영향 없음
        _Width ("Width (screen)", Float) = 0.006
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Cull Front
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            half4 _Color; // (2026-11-18) fixed → half: 1 넘는 HDR 발광 값이 잘리지 않게
            float _Width;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; };
            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                float3 n = mul((float3x3)UNITY_MATRIX_VP, mul((float3x3)unity_ObjectToWorld, v.normal));
                float2 off = normalize(n.xy + 1e-5);
                off.x *= _ScreenParams.y / _ScreenParams.x; // 가로세로 비율 보정
                o.pos.xy += off * _Width * o.pos.w * 2.0;   // w를 곱해서 원근 나눗셈 뒤에도 화면에서 같은 두께
                return o;
            }
            half4 frag (v2f i) : SV_Target { return _Color; }
            ENDCG
        }
    }
}
