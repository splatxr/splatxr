Shader "SplatXR/InvisibleSplatMeshShader"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }

        Pass
        {
            Name "FORWARD"
            ColorMask 0
            ZWrite Off
        }

        UsePass "Legacy Shaders/VertexLit/SHADOWCASTER"
    }

    Fallback Off
}