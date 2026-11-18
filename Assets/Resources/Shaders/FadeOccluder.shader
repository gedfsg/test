// 가림 오브젝트 페이드 (2026-11-18) - OccluderFader가 카메라와 플레이어 사이 오브젝트에 잠깐 씌우는 반투명 재질.
// Built-in RP Standard 표면 셰이더 기반: 텍스처·색·노멀·금속/거칠기·발광은 원본에서 복사(OccluderFader.MakeFadeMaterial),
// 알파만 Lerp(1, _FadeAlpha, _FadeAmount). _FadeAmount는 렌더러 MaterialPropertyBlock으로만 바꿈 (재질 공유).
// Transparent 큐 + ZWrite Off + 알파 블렌드. 그림자는 Fallback(Diffuse)의 ShadowCaster로 원래대로 드리움
// (지붕이 반투명해져도 실내가 갑자기 햇빛에 밝아지지 않게).
// Resources 폴더에 둔 이유: 런타임 Shader.Find로 찾으므로 빌드에 반드시 포함되게.
Shader "Custom/FadeOccluder"
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
        _FadeAmount ("Fade Amount (0 불투명 ~ 1 최대 투명)", Range(0,1)) = 0
        _FadeAlpha ("Faded Alpha", Range(0,1)) = 0.3
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 200
        ZWrite Off
        Cull Back

        CGPROGRAM
        #pragma surface surf Standard alpha:fade
        #pragma target 3.0

        sampler2D _MainTex;
        sampler2D _BumpMap;
        sampler2D _EmissionMap;
        half _BumpScale;
        fixed4 _Color;
        half _Glossiness;
        half _Metallic;
        half4 _EmissionColor;
        half _UseVertexColor;
        half _FadeAmount;
        half _FadeAlpha;

        struct Input
        {
            float2 uv_MainTex;
            float4 color : COLOR;
        };

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            if (_UseVertexColor > 0.5)
            {
                // glTFast 셰이더와 같은 방식 (감마 공간이면 정점 색을 감마로 변환)
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
            // 알파만 바뀜: 0 → 원래(불투명), 1 → _FadeAlpha(기본 0.3)
            o.Alpha = lerp(1.0, _FadeAlpha, saturate(_FadeAmount));
        }
        ENDCG
    }
    FallBack "Diffuse"
}
