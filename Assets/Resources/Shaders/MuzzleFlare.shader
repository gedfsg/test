// 총구 섬광 메인 플레어 (2026-11-22) - 실사 레퍼런스(Assets/UI/References/muzzle_flash_ref_1·2, variations) 톤.
// 텍스처 없이 UV로 모양을 그림: uv.y = 총구(0) → 앞 끝(1), uv.x = 가로(0.5 = 중심선)
//  - 총구 쪽 흰 별 모양 코어 (방사 스파이크)
//  - 코어 바로 바깥 노랑 #FFE060
//  - 바깥 주황 #FF8030 플레어가 전방으로 길쭉하게 가늘어지며 분출
// 가산 합성(Blend One One), ZWrite Off, ZTest On(LEqual), 양면. 색 × _Intensity(HDR 1.5배, Bloom에 걸림) × _Alpha.
// _Tint: 일반 총 = 흰(색 그대로), 화염방사기·RPG·레이저 = 무기 색으로 물들임. _Seed: 발사마다 모양 조금씩 다르게.
// MuzzleLight가 축 빌보드(긴 축 = 총구 방향, 면 = 카메라 쪽)로 세움.
Shader "Custom/MuzzleFlare"
{
    Properties
    {
        [HDR] _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Intensity ("Emissive Intensity", Float) = 1.5
        _Alpha ("Alpha", Range(0, 1)) = 1
        _Seed ("Seed", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+20" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        ZTest LEqual
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            half4 _Tint;
            float _Intensity, _Alpha, _Seed;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert (appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }

            half4 frag (v2f i) : SV_Target
            {
                float along = i.uv.y;                 // 0 = 총구, 1 = 앞 끝
                float across = (i.uv.x - 0.5) * 2.0;  // -1 ~ 1

                // ── 전방 분출 플레어: 앞으로 갈수록 가늘어지는 불꽃 (가장자리 살짝 흔들림) ──
                float wobble = 1.0 + 0.12 * sin(along * 23.0 + _Seed * 6.28) + 0.06 * sin(along * 57.0 - _Seed * 3.1);
                float halfW = 0.95 * pow(saturate(1.0 - along), 0.75) * wobble * smoothstep(0.0, 0.12, along + 0.02);
                float flame = saturate(1.0 - abs(across) / max(halfW, 1e-3));
                flame = flame * flame * pow(saturate(1.0 - along), 0.6);

                // ── 총구 쪽 코어: 둥근 빛 + 별 모양 스파이크 ──
                // float2 c = float2(across * 0.5, (along - 0.16) * 1.6); // (이전) 총구와 섬광 사이 틈이 보였음
                float2 c = float2(across * 0.5, (along - 0.07) * 1.6); // 코어 중심 = 총구 바로 앞
                float r = length(c);
                float ang = atan2(c.y, c.x);
                float spikes = pow(abs(cos(ang * 4.0 + _Seed * 2.0)), 18.0) * 0.8 + pow(abs(cos(ang * 7.0 - _Seed)), 30.0) * 0.5;
                float core = exp(-r * r * 38.0) + spikes * exp(-r * 9.0) * 0.9;

                // float e = saturate(flame + core); // (이전) 색이 희게 바램
                float e = saturate(flame * 1.25 + core * 1.2); // 0~1 밝기 (조금 더 진하게)

                // ── 색: 주황 → 노랑 → 흰 ──
                const float3 orange = float3(1.0, 0.502, 0.188); // #FF8030
                const float3 yellow = float3(1.0, 0.878, 0.376); // #FFE060
                // float3 col = lerp(orange, yellow, smoothstep(0.25, 0.6, e)); // (이전)
                // col = lerp(col, float3(1, 1, 1), smoothstep(0.7, 0.95, e));
                // 흰색은 코어 한가운데만, 나머지는 노랑·주황 채도 살림
                float3 col = lerp(orange, yellow, smoothstep(0.35, 0.75, e));
                col = lerp(col, float3(1, 1, 1), smoothstep(0.88, 1.0, e) * saturate(core));
                col *= _Tint.rgb;

                float a = saturate(e * 1.3) * _Alpha;
                return half4(col * a * _Intensity, 1);
            }
            ENDCG
        }
    }
}
