// 플레이어가 건물 안에 들어가면 일정 높이(Y) 위쪽 픽셀을 discard로 완전히 잘라냄.
// 알파 블렌딩이 아니라 실제로 안 그려지는 컷어웨이라 지붕만 사라지고 벽은 그대로 남음.
// 이 프로젝트는 URP가 아니라 Built-in Render Pipeline이라 Surface Shader로 작성함
// (PlayerXRay.shader / PlayerStencilWriter.shader와 동일한 CGPROGRAM 계열).
//
// 벽 두께 표현:
//  Kenney 건물 벽은 두께 없는 한 겹 면이라, 위를 잘라내면 카메라 반대편 벽은 "안쪽 면(뒷면)"만
//  보이게 되는데 예전엔 Cull Back이라 그 면이 통째로 안 그려졌음 → 카메라를 돌리면 벽이 사라짐.
//  그래서 Cull Off로 뒷면도 그리고(컷이 켜졌을 때만), 잘린 단면 바로 아래 _WallThickness 높이를
//  조명 영향 없는 진회색(_EdgeColor)으로 칠해서 모든 각도에서 벽 윗면이 두꺼운 캡처럼 보이게 함.
Shader "Custom/BuildingCutaway"
{
    Properties
    {
        [MainTexture] _MainTex ("Base Map", 2D) = "white" {}
        [MainColor]   _Color ("Base Color", Color) = (1,1,1,1)
        _CutHeight ("Cut Height (World Y)", Float) = 1000
        _CutEnabled ("Cut Enabled (0/1)", Range(0,1)) = 0
        _WallThickness ("Cut Edge Thickness", Float) = 0.8
        _EdgeColor ("Cut Edge Color", Color) = (0.22, 0.22, 0.22, 1)
        _InnerShade ("Inner Face Brightness", Range(0,1)) = 0.75
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        Cull Off // 뒷면(벽 안쪽 면)도 그림. 컷이 꺼져 있을 땐 surf에서 뒷면을 버려서 기존과 동일

        CGPROGRAM
        #pragma surface surf Lambert addshadow
        #pragma target 3.0

        sampler2D _MainTex;
        fixed4 _Color;
        float _CutHeight;
        float _CutEnabled;
        float _WallThickness;
        fixed4 _EdgeColor;
        half _InnerShade;

        struct Input
        {
            float2 uv_MainTex;
            float3 worldPos;
            float facing : VFACE; // 앞면 > 0, 뒷면 < 0
        };

        void surf (Input IN, inout SurfaceOutput o)
        {
            bool cutOn = _CutEnabled > 0.5;
            bool backFace = IN.facing < 0;

            // _CutEnabled가 켜져 있을 때만 컷 높이 위쪽 픽셀을 잘라냄
            float distFromCut = _CutHeight - IN.worldPos.y;
            clip(cutOn ? distFromCut : 1.0);

            // 컷이 꺼져 있으면 뒷면은 원래 안 보이던 면이므로 버림 (Cull Back과 같은 결과)
            clip(cutOn || !backFace ? 1.0 : -1.0);

            // 뒷면은 법선을 뒤집어야 조명이 안쪽 면 기준으로 들어감
            o.Normal = float3(0, 0, backFace ? -1 : 1);

            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;

            if (cutOn && distFromCut < _WallThickness)
            {
                // 잘린 단면 바로 아래 띠: 조명 영향 없이 진회색으로 → 앞/뒷면 모두 같은 캡 색
                o.Albedo = 0;
                o.Emission = _EdgeColor.rgb;
            }
            else
            {
                // 벽 안쪽 면은 살짝 어둡게 해서 바깥 면과 구분
                o.Albedo = backFace ? c.rgb * _InnerShade : c.rgb;
            }
            o.Alpha = c.a;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
