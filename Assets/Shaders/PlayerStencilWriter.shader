// 플레이어가 "실제로 보이는" 픽셀에 스텐실 = 1을 기록.
// X-Ray 셰이더가 이 영역에는 그리지 않도록 마스크 역할.
Shader "Custom/PlayerStencilWriter"
{
    SubShader
    {
        Tags { "Queue"="Geometry+10" "RenderType"="Opaque" "IgnoreProjector"="True" }

        Pass
        {
            Name "StencilWrite"
            ZTest LEqual        // 보이는 픽셀만
            ZWrite Off          // 깊이는 안 건드림
            ColorMask 0         // 색상도 안 그림 (보이지 않음)
            Cull Back

            Stencil
            {
                Ref 1
                Comp Always
                Pass Replace
            }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; };
            struct v2f     { float4 vertex : SV_POSITION; };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return 0;
            }
            ENDCG
        }
    }
}
