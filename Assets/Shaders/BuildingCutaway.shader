// 플레이어가 건물 안에 들어가면 일정 높이(Y) 위쪽 픽셀을 discard로 완전히 잘라냄.
// 알파 블렌딩이 아니라 실제로 안 그려지는 컷어웨이라 지붕만 사라지고 벽은 그대로 남음.
// 이 프로젝트는 URP가 아니라 Built-in Render Pipeline이라 Surface Shader로 작성함
// (PlayerXRay.shader / PlayerStencilWriter.shader와 동일한 CGPROGRAM 계열).
Shader "Custom/BuildingCutaway"
{
    Properties
    {
        [MainTexture] _MainTex ("Base Map", 2D) = "white" {}
        [MainColor]   _Color ("Base Color", Color) = (1,1,1,1)
        _CutHeight ("Cut Height (World Y)", Float) = 1000
        _CutEnabled ("Cut Enabled (0/1)", Range(0,1)) = 0
        _WallThickness ("Cut Edge Thickness", Float) = 0.5
        _EdgeColor ("Cut Edge Color", Color) = (0.12, 0.12, 0.12, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        Cull Back

        CGPROGRAM
        #pragma surface surf Lambert addshadow
        #pragma target 3.0

        sampler2D _MainTex;
        fixed4 _Color;
        float _CutHeight;
        float _CutEnabled;
        float _WallThickness;
        fixed4 _EdgeColor;

        struct Input
        {
            float2 uv_MainTex;
            float3 worldPos;
        };

        void surf (Input IN, inout SurfaceOutput o)
        {
            // _CutEnabled가 켜져 있을 때만 컷 높이 위쪽 픽셀을 잘라냄
            float distFromCut = _CutHeight - IN.worldPos.y;
            clip(_CutEnabled > 0.5 ? distFromCut : 1.0);

            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;

            // 잘린 단면 바로 아래(_WallThickness 두께만큼)를 진한 색으로 채워서
            // 벽이 종이처럼 얇지 않고 실제 두께가 있는 것처럼 보이게 함
            if (_CutEnabled > 0.5 && distFromCut < _WallThickness)
                c = _EdgeColor;

            o.Albedo = c.rgb;
            o.Alpha = c.a;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
