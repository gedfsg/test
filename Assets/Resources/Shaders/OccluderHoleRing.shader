// 외부 가림 구멍 가장자리 링 - Prop용 (2026-11-21 2차).
// 불투명 원본(Custom/OccluderHole)은 구멍+페더 구간을 잘라내고, 이 셰이더가 같은 메시로 페더 구간만 반투명 그라데이션으로 그림.
// OccluderFader가 원본 렌더러 자식(_HoleRing)으로 붙이고, 속성은 원본 재질에서 복사(CopyProps), _HoleAmount는 MPB.
//  - 1패스: 페더 구간 깊이만 기록 → 2패스가 가장 앞면만 칠함 (자기 자신 앞뒤 면이 겹쳐 진해지지 않게)
//  - 2패스: Standard 조명 + 알파 = 남은 정도(0~1)
// Transparent 큐: 뒤에 있는 플레이어·땅이 먼저 그려진 뒤 그 위에 섞임.
Shader "Custom/OccluderHoleRing"
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
        Tags { "Queue"="Transparent-5" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 200

        Pass
        {
            ZWrite On
            ColorMask 0
            Cull Back
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "OccluderHole.cginc"
            float _HoleAmount, _HoleInteriorFill; // (3차) 내부 가림
            struct v2f { float4 pos : SV_POSITION; float3 wpos : TEXCOORD0; };
            v2f vert (float4 v : POSITION) { v2f o; o.pos = UnityObjectToClipPos(v); o.wpos = mul(unity_ObjectToWorld, v).xyz; return o; }
            fixed4 frag (v2f i) : SV_Target { OccluderHoleRingFill(i.wpos, _HoleAmount, _HoleInteriorFill); return 0; }
            ENDCG
        }

        ZWrite Off
        Cull Back
        CGPROGRAM
        #pragma surface surf Standard alpha:fade
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
        float _HoleInteriorFill; // (2026-11-21 3차)

        struct Input
        {
            float2 uv_MainTex;
            float4 color : COLOR;
            float3 worldPos;
        };

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            float a = OccluderHoleRingFill(IN.worldPos, _HoleAmount, _HoleInteriorFill); // (3차)
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
            o.Alpha = a;
        }
        ENDCG
    }
}
