// 외부 가림 구멍 가장자리 링 - 건물용 (2026-11-21 2차).
// 건물 원본(Custom/BuildingCutaway)은 구멍+페더 구간을 잘라내고, 이 셰이더가 같은 메시로 페더 구간만 반투명 그라데이션으로 그림.
// BuildingCutaway와 같은 지붕 컷 규칙(_CutHeight/_CutEnabled, 컷 켜졌을 때만 뒷면, 단면 캡 색, 안쪽 면 어둡게)을 그대로 따름.
// 컷 값은 OccluderFader가 원본 인스턴스 재질에서 매 프레임 복사 (실내 들어가고 나올 때 같이 바뀜).
//  - 1패스: 페더 구간 깊이만 기록 → 2패스가 가장 앞면만 칠함 (벽 바깥·안쪽 면이 겹쳐 진해지지 않게)
//  - 2패스: Lambert 조명(원본과 같음) + 알파 = 남은 정도(0~1)
Shader "Custom/BuildingCutawayHoleRing"
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
        _HoleAmount ("Hole Amount (0~1, MPB)", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent-5" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 200

        Pass
        {
            ZWrite On
            ColorMask 0
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "OccluderHole.cginc"
            float _HoleAmount, _CutHeight, _CutEnabled, _HoleInteriorFill; // (3차) 내부 가림
            struct v2f { float4 pos : SV_POSITION; float3 wpos : TEXCOORD0; };
            v2f vert (float4 v : POSITION) { v2f o; o.pos = UnityObjectToClipPos(v); o.wpos = mul(unity_ObjectToWorld, v).xyz; return o; }
            fixed4 frag (v2f i, float facing : VFACE) : SV_Target
            {
                bool cutOn = _CutEnabled > 0.5;
                clip(cutOn ? _CutHeight - i.wpos.y : 1.0);
                clip(cutOn || facing > 0 ? 1.0 : -1.0);
                OccluderHoleRingFill(i.wpos, _HoleAmount, _HoleInteriorFill); // (3차) 바닥 높이는 원본이 어둡게 칠함
                return 0;
            }
            ENDCG
        }

        ZWrite Off
        Cull Off
        CGPROGRAM
        #pragma surface surf Lambert alpha:fade
        #pragma target 3.0
        #include "OccluderHole.cginc"

        sampler2D _MainTex;
        fixed4 _Color;
        float _CutHeight;
        float _CutEnabled;
        float _WallThickness;
        fixed4 _EdgeColor;
        half _InnerShade;
        float _HoleAmount;
        float _HoleInteriorFill; // (2026-11-21 3차)

        struct Input
        {
            float2 uv_MainTex;
            float3 worldPos;
            float facing : VFACE;
        };

        void surf (Input IN, inout SurfaceOutput o)
        {
            bool cutOn = _CutEnabled > 0.5;
            bool backFace = IN.facing < 0;
            float distFromCut = _CutHeight - IN.worldPos.y;
            clip(cutOn ? distFromCut : 1.0);
            clip(cutOn || !backFace ? 1.0 : -1.0);
            float a = OccluderHoleRingFill(IN.worldPos, _HoleAmount, _HoleInteriorFill); // (3차)

            o.Normal = float3(0, 0, backFace ? -1 : 1);
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            if (cutOn && distFromCut < _WallThickness)
            {
                o.Albedo = 0;
                o.Emission = _EdgeColor.rgb;
            }
            else
            {
                o.Albedo = backFace ? c.rgb * _InnerShade : c.rgb;
            }
            o.Alpha = a;
        }
        ENDCG
    }
}
