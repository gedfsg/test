Shader "Custom/DarkOverlay"
{
    Properties
    {
        _Color ("Color", Color) = (0,0,0,0.85)
    }
    
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+2" }
        
        Pass
        {
            // 스텐실 버퍼가 1이 아닌 곳만 그림 (부채꼴 밖만 어둡게)
            Stencil
            {
                Ref 1
                Comp NotEqual
            }
            
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            
            fixed4 _Color;
            
            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; };
            
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }
            
            fixed4 frag(v2f i) : SV_Target
            {
                return _Color;
            }
            ENDCG
        }
    }
}