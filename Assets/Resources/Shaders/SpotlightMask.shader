// (2026-11-23) 튜토리얼 스포트라이트: 화면 전체를 어둡게 덮고, 지정한 원(화면 픽셀 좌표)만 뚫는 UI 셰이더.
//  - TutorialOverlay가 전체 화면 Image에 이 재질을 씌우고 _Center / _Radius / _Feather를 매 프레임 넣음
//  - 원 안 = 투명, 가장자리 _Feather 픽셀 동안 부드럽게 어두워짐, 원 밖 = _Color (기본 검정 α0.7)
//  - Shader.Find로만 쓰므로 빌드에서 빠지지 않게 Resources/Shaders에 둠
Shader "Custom/SpotlightMask"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Overlay Color", Color) = (0, 0, 0, 0.7)
        _Center ("Center (screen px)", Vector) = (0, 0, 0, 0)
        _Radius ("Radius (px)", Float) = 0
        _Feather ("Feather (px)", Float) = 40
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float4 color : COLOR; float4 screen : TEXCOORD0; };

            fixed4 _Color;
            float4 _Center;
            float _Radius;
            float _Feather;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.screen = ComputeScreenPos(o.pos); // 플랫폼별 y 뒤집힘 처리 포함
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 px = i.screen.xy / i.screen.w * _ScreenParams.xy; // 화면 픽셀 (왼쪽 아래 0,0)
                float d = distance(px, _Center.xy);
                // 반경 0이면 구멍 없음 (전체 어둡게)
                float hole = _Radius > 0.5 ? 1.0 - smoothstep(max(_Radius - _Feather, 0.0), _Radius, d) : 0.0;
                fixed4 c = _Color * i.color; // Image 색(알파)으로 전체 페이드
                c.a *= 1.0 - hole;
                #if !defined(UNITY_COLORSPACE_GAMMA)
                // Linear 색 공간: 섞기가 선형이라 α0.7 검정이 눈에는 58% 밝기로만 보임 → 감마 공간에서처럼 보이게 알파 보정
                c.a = 1.0 - pow(saturate(1.0 - c.a), 2.2);
                #endif
                return c;
            }
            ENDCG
        }
    }
}
