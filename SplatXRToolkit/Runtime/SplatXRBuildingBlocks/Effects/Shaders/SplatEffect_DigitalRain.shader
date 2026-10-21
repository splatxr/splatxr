Shader "SplatXR/SplatEffectDigitalRain"
{
    Properties
    {
        _ScaleFactor ("Scale Factor", Float) = 1.0

        [Header(Splat Rain Properties)]
        _EffectActive ("Effect Active", Float) = 0.0
        _EffectTime ("Rain Time", Range(0, 1)) = 0.0
        _RainHeight ("Rain Height", Float) = 5.0
        [Vector3] _RainDirection ("Rain Direction (World XYZ)", Vector) = (0, -1, 0, 0)
        _RainStagger ("Rain Stagger", Range(0.0, 0.9)) = 0.5
        _ColumnWidth ("Rain Column Width", Float) = 0.05
        _ColumnJitter ("Column Jitter", Range(0.0, 1.0)) = 0.15
        _ConstructionDuration ("Construction Duration", Range(0.05, 0.9)) = 0.3
        _RainScale ("Raindrop Splat Scale", Range(0.0, 2.0)) = 0.2
        _StreakLength ("Raindrop Streak Length", Range(0.0, 0.5)) = 0.04

        [Header(Matrix Colors)]
        _GreenColor ("Rain Trail Color", Color) = (0, 1, 0.25, 1)
        _HeadColor ("Rain Head Color", Color) = (0.75, 1, 0.8, 1)
        _HeadGlow ("Head Glow", Range(0, 4)) = 1.5
        _FlickerSpeed ("Glyph Flicker Speed", Range(0, 30)) = 12

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

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float hash31(float3 p)
            {
                p = frac(p * float3(443.897, 441.423, 437.195));
                p += dot(p, p.yzx + 19.19);
                return frac((p.x + p.y) * p.z);
            }

            float _ScaleFactor;
            float _EffectActive;
            float _EffectTime;
            float _RainHeight;
            float4 _RainDirection;
            float _RainStagger;
            float _ColumnWidth;
            float _ColumnJitter;
            float _ConstructionDuration;
            float _RainScale;
            float _StreakLength;
            fixed4 _GreenColor;
            fixed4 _HeadColor;
            float _HeadGlow;
            float _FlickerSpeed;

            struct RainResult
            {
                float3 position;     
                float scale;         
                float opacity;       
                float fall;          
                float construct;     
                float seed;          
            };

            float3 rainDirection()
            {
                float3 dir = _RainDirection.xyz;
                return dot(dir, dir) > 1e-8 ? normalize(dir) : float3(0.0, -1.0, 0.0);
            }

            
            RainResult splatRain(float3 pos, float t, float3 dir)
            {
                
                float3 helper = abs(dir.y) < 0.99 ? float3(0.0, 1.0, 0.0) : float3(1.0, 0.0, 0.0);
                float3 axisU = normalize(cross(dir, helper));
                float3 axisV = cross(dir, axisU);
                float columnWidth = max(_ColumnWidth, 0.0001);
                float columnRand = hash21(floor(float2(dot(pos, axisU), dot(pos, axisV)) / columnWidth));
                float splatRand = hash31(pos);

                float stagger = clamp(_RainStagger, 0.0, 0.9);
                float delay = lerp(columnRand, splatRand, saturate(_ColumnJitter)) * stagger;

                
                float local = saturate((t - delay) / (1.0 - stagger));

                float constructFrac = clamp(_ConstructionDuration, 0.05, 0.9);
                float fallFrac = 1.0 - constructFrac;

                RainResult r;
                r.seed = splatRand;
                r.fall = saturate(local / fallFrac);
                r.construct = saturate((local - fallFrac) / constructFrac);

                r.position = pos - dir * (_RainHeight * (1.0 - r.fall));

                r.opacity = (t > delay) ? smoothstep(0.0, 0.1, r.fall) : 0.0;
                r.scale = lerp(_RainScale, 1.0, smoothstep(0.0, 1.0, r.construct));
                return r;
            }

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

                float rainOpacity = 1.0;
                float rainScale = 1.0;
                float fallProgress = 1.0;
                float constructProgress = 1.0;
                float rainSeed = 0.0;
                float3 rainDir = rainDirection();

                if (_EffectActive > 0.5) {
                    RainResult rain = splatRain(worldCenter.xyz, _EffectTime, rainDir);
                    worldCenter.xyz = rain.position;
                    rainScale = rain.scale;
                    rainOpacity = rain.opacity;
                    fallProgress = rain.fall;
                    constructProgress = rain.construct;
                    rainSeed = rain.seed;
                }

                o.colorOpacity.a = v.opacity * rainOpacity * _Transparency;
                if (o.colorOpacity.a < 1.0 / 255.0)
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

                float scale2 = _ScaleFactor * _ScaleFactor * rainScale * rainScale;
                float a = dot(T0, VrkT0) * scale2 + 0.3;
                float b = dot(T0, VrkT1) * scale2;
                float d = dot(T1, VrkT1) * scale2 + 0.3;

                if (_EffectActive > 0.5 && constructProgress <= 0.0) {
                    float streak = _StreakLength * (1.0 - smoothstep(0.85, 1.0, fallProgress));
                    float3x3 V3 = (float3x3)UNITY_MATRIX_V;
                    float up0 = dot(mul(J0, V3), rainDir) * streak;
                    float up1 = dot(mul(J1, V3), rainDir) * streak;
                    a += up0 * up0;
                    b += up0 * up1;
                    d += up1 * up1;
                }

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

                float3 color = GammaToLinearSpace(applyASC_CDL(v.color));
                if (_EffectActive > 0.5) {
                    float3 trail = _GreenColor.rgb * lerp(0.3, 1.0, fallProgress);
                    float headAmount = smoothstep(0.75, 1.0, fallProgress) * (1.0 - constructProgress);
                    float3 rainColor = lerp(trail, _HeadColor.rgb * _HeadGlow, headAmount);

                    if (constructProgress <= 0.0) {
                        float flickerStep = floor(_Time.y * _FlickerSpeed + rainSeed * 17.0);
                        rainColor *= lerp(0.55, 1.25, hash21(float2(flickerStep, rainSeed * 101.0)));
                    }

                    color = lerp(rainColor, color * _ColorTint.rgb, smoothstep(0.0, 1.0, constructProgress));
                } else {
                    color *= _ColorTint.rgb;
                }
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
