Shader "SplatXR/BlendingPortalShaderVignette"
{
    Properties
    {
        _LeftEyeTex("Left Eye Texture", 2D) = "white" {}
        _RightEyeTex("Right Eye Texture", 2D) = "white" {}
        _EdgeSoftness("Edge Softness", Range(0.0, 0.5)) = 0.1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off 

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            sampler2D _LeftEyeTex;
            sampler2D _RightEyeTex;
            float _EdgeSoftness;

            struct appdata {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID 
            };

            struct v2f {
                float4 vertex   : SV_POSITION;
                float4 xy       : TEXCOORD0;
                float2 uv       : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID 
                UNITY_VERTEX_OUTPUT_STEREO     
            };
            
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.vertex = UnityObjectToClipPos(v.vertex);
                o.xy     = ComputeScreenPos(o.vertex);
                o.uv     = v.uv; 
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float2 screenUV = i.xy.xy / i.xy.w;
                fixed4 col = (unity_StereoEyeIndex == 0) ? tex2D(_LeftEyeTex, screenUV) : tex2D(_RightEyeTex, screenUV);

                float horizMask = smoothstep(0.0, _EdgeSoftness, i.uv.x) * smoothstep(1.0, 1.0 - _EdgeSoftness, i.uv.x);
                float vertMask = smoothstep(0.0, _EdgeSoftness, i.uv.y) * smoothstep(1.0, 1.0 - _EdgeSoftness, i.uv.y);
                
                float finalMask = horizMask * vertMask;

                col.a *= finalMask;
                return col;
            }
            ENDCG
        }
    }
}