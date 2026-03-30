Shader "Custom/FOVMask"
{
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+1" }
        
        Pass
        {
            // 스텐실 버퍼에 1을 씀 (부채꼴 영역 표시)
            Stencil
            {
                Ref 1
                Comp Always
                Pass Replace
            }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            ColorMask 0      // 색상은 안 그림 (투명)
            ZWrite Off
        }
    }
}