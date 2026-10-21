Shader "SplatXR/SplatEffectGrow"
{
    Properties
    {
        _ScaleFactor ("Scale Factor", Float) = 1.0

        [Header(Effect Properties)]
        _EffectActive ("Effect Active", Float) = 0.0
        _EffectTime ("Grow Time", Float) = 0.0
        _GrowHeight ("Grow Height Range", Float) = 5.0
        _GlowIntensity ("Glow Intensity", Range(0,5)) = 2.0
        _GlowColor ("Glow Color", Color) = (0.5, 1.0, 1.0, 1.0)

        [Header(Color Adjustments)]
        _ColorTint ("Color Tint", Color) = (1,1,1,1)
        _Brightness ("Brightness", Range(0,3)) = 1
        _Contrast ("Contrast", Range(0,3)) = 1
        _Saturation ("Saturation", Range(0,3)) = 1
        _Temperature ("Temperature", Range(-1,1)) = 0
        _Transparency ("Transparency", Range(0,1)) = 1

        [Header(ASC CDL Color Correction)]
        _ASC_CDL_Slope ("ASC-CDL Slope (RGB)", Vector) = (1,1,1,0)
        _ASC_CDL_Offset ("ASC-CDL Offset (RGB)", Vector) = (0,0,0,0)
        _ASC_CDL_Power ("ASC-CDL Power (RGB)", Vector) = (1,1,1,0)
        _ASC_CDL_Enable ("Enable ASC-CDL", Float) = 0
    }
    SubShader
    {
        Tags {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }
        LOD 100

        ZWrite Off
        ZTest Less
        Cull Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 centerPos : TEXCOORD1;
                float3 color : TEXCOORD2;
                float opacity : TEXCOORD3;
                float3 cov0_3 : TEXCOORD5;
                float3 cov3_6 : TEXCOORD6;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 colorOpacity : TEXCOORD0;
                float3 vpositionGlow : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            struct GrowResult
            {
                float scaleFactor;
                float opacity;
                float3 glowColor;
            };

            GrowResult growEffect(float3 pos, float t, float3 minBounds, float3 maxBounds, float glowIntensity, float3 glowColor)
            {
                float normalizedTime = saturate(t);

                float relativeHeight = (pos.y - minBounds.y) / (maxBounds.y - minBounds.y);
                relativeHeight = saturate(relativeHeight);

                float wavePosition = normalizedTime * 1.2 - 0.2;
                float distanceFromWave = relativeHeight - wavePosition;

                float visibility = 1.0 - smoothstep(-0.1, 0.1, distanceFromWave);

                float scaleProgress = smoothstep(-0.05, 0.15, -distanceFromWave);
                float scaleFactor = lerp(0.1, 1.0, scaleProgress);

                float glowFactor = 1.0 - smoothstep(0.0, 0.2, abs(distanceFromWave));
                glowFactor *= visibility;

                GrowResult result;
                result.scaleFactor = scaleFactor;
                result.opacity = visibility;
                result.glowColor = glowColor * glowIntensity * glowFactor;
                return result;
            }

            float _ScaleFactor;
            float _EffectActive;
            float _EffectTime;
            float _GrowHeight;
            float _GlowIntensity;
            fixed4 _GlowColor;

            fixed4 _ColorTint;
            float _Brightness;
            float _Contrast;
            float _Saturation;
            float _Temperature;
            float _Transparency;

            float4 _ASC_CDL_Slope;
            float4 _ASC_CDL_Offset;
            float4 _ASC_CDL_Power;
            float _ASC_CDL_Enable;

            float3 applyASC_CDL(float3 color)
            {
                if (_ASC_CDL_Enable < 0.5) return color;
                float3 result = max(saturate(color) * _ASC_CDL_Slope.rgb + _ASC_CDL_Offset.rgb, 0.0001);
                return saturate(pow(result, _ASC_CDL_Power.rgb));
            }

            float3 adjustColor(float3 color)
            {
                float3 temperatureShift = lerp(float3(1.0, 1.0, 1.0),
                                               _Temperature > 0 ? float3(1.0, 0.9, 0.8) : float3(0.8, 0.9, 1.0),
                                               abs(_Temperature));
                color *= temperatureShift;

                float luminance = dot(color, float3(0.299, 0.587, 0.114));
                color = lerp(luminance.xxx, color, _Saturation);

                return ((color - 0.5) * _Contrast + 0.5) * _Brightness;
            }

            v2f vert (appdata v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                const float4 culledVertex = float4(0.0, 0.0, 2.0, 1.0);

                v2f o;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = culledVertex;
                o.colorOpacity = float4(0.0, 0.0, 0.0, 0.0);

                float3 scaledCenterPos = v.centerPos * _ScaleFactor;
                float growOpacity = 1.0;
                float growScale = 1.0;
                float3 glowColor = float3(0, 0, 0);

                if (_EffectActive > 0.5) {
                    float3 minBounds = float3(-_GrowHeight, -_GrowHeight, -_GrowHeight);
                    float3 maxBounds = float3(_GrowHeight, _GrowHeight, _GrowHeight);

                    GrowResult growResult = growEffect(scaledCenterPos, _EffectTime, minBounds, maxBounds, _GlowIntensity, _GlowColor.rgb);
                    growScale = growResult.scaleFactor;
                    growOpacity = growResult.opacity;
                    glowColor = growResult.glowColor;
                }

                float glowBoost = length(glowColor) * 0.5;
                o.vpositionGlow = float3(sqrt(8.0) * v.vertex.xy, glowBoost);
                o.colorOpacity.a = v.opacity * growOpacity * _Transparency;
                if (o.colorOpacity.a < 1.0 / 255.0 && glowBoost < 1.0 / 255.0)
                {
                    return o;
                }

                float4 worldCenter = mul(UNITY_MATRIX_M, float4(scaledCenterPos, 1.0));
                float4 viewCenter = mul(UNITY_MATRIX_V, worldCenter);
                float4 clipCenter = mul(UNITY_MATRIX_P, viewCenter);

                float clip = 1.2 * clipCenter.w;
                if (clipCenter.z < -clip ||
                    clipCenter.x < -clip ||
                    clipCenter.x > clip ||
                    clipCenter.y < -clip ||
                    clipCenter.y > clip)
                {
                    return o;
                }

                float focal = 0.5 * _ScreenParams.y * unity_CameraProjection._m11;

                float3x3 MV = mul((float3x3)UNITY_MATRIX_V, (float3x3)UNITY_MATRIX_M);
                float invZ = 1.0 / viewCenter.z;
                float s = invZ * invZ;
                float3 J0 = float3(focal * invZ, 0, -(focal * viewCenter.x) * s);
                float3 J1 = float3(0, focal * invZ, -(focal * viewCenter.y) * s);
                float3 T0 = mul(J0, MV);
                float3 T1 = mul(J1, MV);

                float3x3 Vrk = float3x3(
                    v.cov0_3.x, v.cov0_3.y, v.cov0_3.z,
                    v.cov0_3.y, v.cov3_6.x, v.cov3_6.y,
                    v.cov0_3.z, v.cov3_6.y, v.cov3_6.z
                );
                float3 VrkT0 = mul(Vrk, T0);
                float3 VrkT1 = mul(Vrk, T1);

                float scale2 = _ScaleFactor * _ScaleFactor * growScale * growScale;
                float a = dot(T0, VrkT0) * scale2 + 0.3;
                float b = dot(T0, VrkT1) * scale2;
                float d = dot(T1, VrkT1) * scale2 + 0.3;

                float D = a * d - b * b;
                float traceOver2 = 0.5 * (a + d);
                float term2 = sqrt(max(0.1, traceOver2 * traceOver2 - D));
                float eigenValue1 = traceOver2 + term2;
                float eigenValue2 = traceOver2 - term2;

                if (eigenValue2 <= 0.0)
                {
                    return o;
                }

                float2 eigenVector1 = normalize(float2(b, eigenValue1 - a));
                float2 eigenVector2 = float2(eigenVector1.y, -eigenVector1.x);

                float sqrt8 = sqrt(8.0);
                float2 basisVector1 = eigenVector1 * min(sqrt8 * sqrt(eigenValue1), 1024.0);
                float2 basisVector2 = eigenVector2 * min(sqrt8 * sqrt(eigenValue2), 1024.0);

                float pixelToView = viewCenter.z / focal;
                float2 viewOffset = (v.vertex.x * basisVector1 + v.vertex.y * basisVector2) * pixelToView;
                o.vertex = clipCenter
                         + UNITY_MATRIX_P._m00_m10_m20_m30 * viewOffset.x
                         + UNITY_MATRIX_P._m01_m11_m21_m31 * viewOffset.y;

                o.colorOpacity.rgb = adjustColor(GammaToLinearSpace(applyASC_CDL(v.color)) * _ColorTint.rgb + glowColor);

                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float A = dot(i.vpositionGlow.xy, i.vpositionGlow.xy);
                if (A > 8.0) discard;

                float opacity = saturate(exp(-0.5 * A) * i.colorOpacity.a + i.vpositionGlow.z);
                return fixed4(i.colorOpacity.rgb * opacity, opacity);
            }
            ENDCG
        }
    }
}
