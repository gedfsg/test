// 외부 가림 구멍 재질 (2026-11-21) - OccluderFader가 카메라와 플레이어 사이 Prop(나무·간판·담장·차 등)에 잠깐 씌움.
// FadeOccluder(반투명 페이드)와 같은 Standard 표면 셰이더·같은 속성(원본에서 복사)이지만 불투명 그대로 두고
// 플레이어 근처(반경 3.5m, 페더 1m)만 디더링으로 잘라냄 → 몸이 반투명해지거나 오브젝트 전체가 흐려지지 않음.
// 건물(BuildingCutaway)은 재질을 안 바꾸고 BuildingCutaway 셰이더가 같은 구멍 함수(OccluderHole.cginc)를 씀.
// Resources 폴더: 런타임 Shader.Find로 찾으므로 빌드에 반드시 포함되게.
Shader "Custom/OccluderHole"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Float) = 1
        _Glossiness ("Smoothness", Range(0,1)) = 0.3
        _Metallic ("Metallic", Range(0,1)) = 0
        _EmissionMap ("Emission", 2D) = "white" {}
        [HDR] _EmissionColor ("Emission Color", Color) = (0,0,0,1)
        _UseVertexColor ("Vertex Color (glTF)", Float) = 0
        _HoleAmount ("Hole Amount (0~1, MPB)", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 200
        Cull Back // 원본(Standard)과 같게

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0
        #include "OccluderHole.cginc"

        sampler2D _MainTex;
        sampler2D _BumpMap;
        sampler2D _EmissionMap;
        half _BumpScale;
        fixed4 _Color;
        half _Glossiness;
        half _Metallic;
        half4 _EmissionColor;
        half _UseVertexColor;
        float _HoleAmount;
        float _HoleInteriorFill; // (2026-11-21 3차) 건물 안 가구 등: 구멍 안 바닥 높이를 어둡게 (MPB)
        float _HoleDither;       // (2026-11-22) 1 = 링 없이 페더 구간 디더링 (정적 배칭 렌더러)

        struct Input
        {
            float2 uv_MainTex;
            float4 color : COLOR;
            float3 worldPos;
            float4 screenPos;
        };

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            float2 pixel = IN.screenPos.xy / max(IN.screenPos.w, 1e-5) * _ScreenParams.xy;
            // OccluderHoleClip(IN.worldPos, pixel, _HoleAmount); // (이전 2차)
            // float fillK = OccluderHoleClipFill(IN.worldPos, _HoleAmount, _HoleInteriorFill); // (3차)
            float fillK = OccluderHoleClipFillDither(IN.worldPos, pixel, _HoleAmount, _HoleInteriorFill, _HoleDither); // (2026-11-22) 1 = 원래 색, 0 = 어두운 단색

            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            if (_UseVertexColor > 0.5)
            {
                #ifdef UNITY_COLORSPACE_GAMMA
                c.rgb *= LinearToGammaSpace(IN.color.rgb);
                #else
                c.rgb *= IN.color.rgb;
                #endif
            }
            o.Albedo = c.rgb;
            o.Normal = UnpackScaleNormal(tex2D(_BumpMap, IN.uv_MainTex), _BumpScale);
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Emission = tex2D(_EmissionMap, IN.uv_MainTex).rgb * _EmissionColor.rgb;
            // (2026-11-21 3차) 내부 가림
            o.Emission = lerp(OccHoleFillColor(), o.Emission, fillK);
            o.Albedo *= fillK;
            o.Metallic *= fillK;
            o.Smoothness *= fillK;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse" // 그림자는 원래대로 (구멍 없이)
}
