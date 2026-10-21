Shader "SplatXR/SplatEffectGenie"
{
    Properties
    {
        _ScaleFactor ("Scale Factor", Float) = 1.0

        [Header(Effect Properties)]
        _EffectActive ("Effect Active", Float) = 0.0
        _EffectTime ("Effect Time", Range(0,1)) = 0.0

        [Header(Genie Effect)]
        _StartPoint ("Start Point", Vector) = (0,0,0,0)
        _EndPoint ("End Point", Vector) = (0,5,0,0)
        _SwirlIntensity ("Swirl Intensity", Range(0,10)) = 3.0
        _SwirlSpeed ("Swirl Speed", Range(0,5)) = 2.0
        _TeleportRadius ("Teleport Radius", Range(0.1,10)) = 2.0
        _StreamWidth ("Stream Width", Range(0.1,5)) = 1.0

        [Header(Debug)]
        _DebugMode ("Debug Mode", Float) = 0.0

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
                float2 vpositionScaled : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float4 genieEffect(float3 pos, float t, float3 startPoint, float3 endPoint, float swirlIntensity, float swirlSpeed, float teleportRadius, float streamWidth)
            {
                float3 direction = endPoint - startPoint;
                float totalDistance = length(direction);

                if (totalDistance < 0.01) {
                    return float4(pos, 1.0);
                }

                direction /= totalDistance;

                float3 toPos = pos - startPoint;
                float projectionLength = dot(toPos, direction);
                float3 closestPointOnPath = startPoint + direction * projectionLength;
                float distanceFromPath = length(pos - closestPointOnPath);

                float radiusInfluence = 1.0 - saturate(distanceFromPath / teleportRadius);
                if (radiusInfluence <= 0.01) {
                    return float4(pos, 1.0);
                }

                float pathProgress = saturate(projectionLength / totalDistance);

                float waveCenter = t;
                float waveWidth = 0.5;

                float distanceFromWave = abs(pathProgress - waveCenter);
                float waveInfluence = 1.0 - saturate(distanceFromWave / waveWidth);
                waveInfluence = smoothstep(0.0, 1.0, waveInfluence);

                waveInfluence = max(waveInfluence, 0.3);

                float3 up = abs(direction.y) < 0.9 ? float3(0, 1, 0) : float3(1, 0, 0);
                float3 perpendicular1 = normalize(cross(direction, up));
                float3 perpendicular2 = cross(direction, perpendicular1);

                float swirlAngle = (pathProgress * swirlIntensity * 6.28 + t * swirlSpeed * 6.28);

                float spiralRadius = distanceFromPath * (1.0 - pathProgress * 0.8);
                float3 swirlOffset = (cos(swirlAngle) * perpendicular1 + sin(swirlAngle) * perpendicular2) * spiralRadius * 0.8;

                float3 streamPosition = startPoint + direction * (pathProgress * totalDistance);
                streamPosition += swirlOffset * radiusInfluence;

                float effectStrength = radiusInfluence * waveInfluence;

                float3 simpleTarget = lerp(startPoint, endPoint, t);
                streamPosition = lerp(streamPosition, simpleTarget, 0.5);

                float3 finalPosition = lerp(pos, streamPosition, effectStrength);

                float scaleMultiplier = 1.0 + effectStrength * 0.5;

                return float4(finalPosition, scaleMultiplier);
            }

            float _ScaleFactor;
            float _EffectActive;
            float _EffectTime;

            float4 _StartPoint;
            float4 _EndPoint;
            float _SwirlIntensity;
            float _SwirlSpeed;
            float _TeleportRadius;
            float _StreamWidth;
            float _DebugMode;

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
                o.vpositionScaled = sqrt(8.0) * v.vertex.xy;
                o.colorOpacity = float4(0.0, 0.0, 0.0, 0.0);

                float3 scaledCenterPos = v.centerPos * _ScaleFactor;
                float4 worldCenter = mul(UNITY_MATRIX_M, float4(scaledCenterPos, 1.0));

                float growthOpacity = 1.0;
                float growthScale = 1.0;

                if (_EffectActive > 0.5) {
                    if (_DebugMode > 0.5) {
                        worldCenter.y += 5.0 * _EffectTime;
                        growthScale = 1.0 + _EffectTime;
                    } else {
                        float4 genieResult = genieEffect(worldCenter.xyz, _EffectTime, _StartPoint.xyz, _EndPoint.xyz,
                                                       _SwirlIntensity, _SwirlSpeed, _TeleportRadius, _StreamWidth);
                        worldCenter.xyz = genieResult.xyz;
                        growthScale = genieResult.w;
                    }
                    growthOpacity = 0.8 + 0.4 * sin(_EffectTime * 10.0 + length(worldCenter.xyz) * 5.0);
                }

                float3 color = GammaToLinearSpace(applyASC_CDL(v.color)) * _ColorTint.rgb;
                float opacity = v.opacity * growthOpacity * _Transparency;

                if (_DebugMode > 0.5) {
                    if (_EffectActive > 0.5) {
                        color = lerp(color, float3(1.0, 0.0, 0.0), 0.8);
                        opacity *= 2.0;
                    } else {
                        color = lerp(color, float3(0.0, 1.0, 0.0), 0.5);
                    }
                }

                if (_EffectActive > 0.5) {
                    float energyPulse = sin(_EffectTime * 15.0 + dot(worldCenter.xyz, float3(2.0, 3.0, 1.5))) * 0.5 + 0.5;

                    float3 energyColor = lerp(float3(0.2, 0.4, 1.0), float3(1.0, 0.2, 0.8), energyPulse);

                    color = lerp(color, color * energyColor, 0.6);

                    color *= (1.5 + _EffectTime);

                    opacity *= (1.2 + energyPulse * 0.3);
                }

                o.colorOpacity.a = opacity;
                if (opacity < 1.0 / 255.0)
                {
                    return o;
                }

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

                float scale2 = _ScaleFactor * _ScaleFactor * growthScale * growthScale;
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

                o.colorOpacity.rgb = adjustColor(color);

                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float A = dot(i.vpositionScaled, i.vpositionScaled);
                if (A > 8.0) discard;

                float opacity = exp(-0.5 * A) * i.colorOpacity.a;
                return fixed4(i.colorOpacity.rgb * opacity, opacity);
            }
            ENDCG
        }
    }
}
