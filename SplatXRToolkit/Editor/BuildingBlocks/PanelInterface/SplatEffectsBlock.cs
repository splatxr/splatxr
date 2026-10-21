using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEditor;

namespace SplatXR
{
    public class SplatEffectsBlock : SplatXRBuildingBlockBase
    {
        public override string Name => "Splat Effects";
    
        private const int CustomShaderIndex = 3;
    
        public override string Description => "Apply an animated shader effect (Disintegrate, Radial Reveal, Digital Rain, or your own custom effect shader) to an existing object with a splat renderer building block. This changes the shader on that splat's material and adds a SplatEffectsBuildingBlock component to start, stop, time and loop the effect.";
        public override string DocumentationUrl => "https://splatxr.github.io/docs/building-blocks/effects.html";
    
        public override Dictionary<string, object> InitializeParameters()
        {
            return new Dictionary<string, object>
            {
                ["Target Splat"] = null,
                ["Effect Selection"] = 0,
                ["Custom Shader"] = null
            };
        }
    
        public override string[] GetDropdownOptions(string parameterName)
        {
            switch (parameterName)
            {
                case "Effect Selection":
                    return new string[] { "Disintegrate", "Radial Reveal", "Digital Rain", "Custom Shader" };
                default:
                    return base.GetDropdownOptions(parameterName);
            }
        }
    
        public override bool IsParameterVisible(string parameterName, Dictionary<string, object> parameters)
        {
            if (parameterName == "Custom Shader")
            {
                return GetParameterValue(parameters, "Effect Selection", 0) == CustomShaderIndex;
            }
            return true;
        }
    
        public override void AddToScene(GameObject targetObject, Dictionary<string, object> parameters)
        {
            GameObject targetSplat = GetParameterValue(parameters, "Target Splat", (GameObject)null);
            int effectSelection = GetParameterValue(parameters, "Effect Selection", 0);
        
            if (effectSelection == CustomShaderIndex && GetParameterValue(parameters, "Custom Shader", (Shader)null) == null)
            {
                Debug.LogError("No custom shader provided. Please assign a shader to the Custom Shader field.");
                return;
            }
        
            if (targetSplat != null)
            {
                ApplyEffectsToSplat(targetSplat, parameters);
            }
            else
            {
                Debug.LogError("No target splat provided. Please assign a target splat GameObject to apply effects to.");
            }
        }
    
        private void ApplyEffectsToSplat(GameObject targetSplat, Dictionary<string, object> parameters)
        {
            if (targetSplat == null)
            {
                Debug.LogError("Target splat GameObject is null.");
                return;
            }
        
            int effectSelection = GetParameterValue(parameters, "Effect Selection", 0);
        
            var effectsComponent = targetSplat.GetComponent<SplatEffectsBuildingBlock>();
            if (effectsComponent == null)
            {
                effectsComponent = targetSplat.AddComponent<SplatEffectsBuildingBlock>();
            }
        
            Shader effectShader = GetEffectShader(effectSelection, parameters);
            ConfigureEffectsComponent(effectsComponent, effectShader);
        
            string effectName = effectSelection == CustomShaderIndex && effectShader != null ? effectShader.name : GetEffectTypeName(effectSelection);
            Debug.Log($"Added {effectName} effect to {targetSplat.name}");
        
            EditorUtility.SetDirty(targetSplat);
        }
    
        private void ConfigureEffectsComponent(SplatEffectsBuildingBlock effectsComponent, Shader effectShader)
        {
            Transform splatRendererChild = effectsComponent.transform.Find("splat_renderer");
            if (splatRendererChild != null)
            {
                MeshRenderer meshRenderer = splatRendererChild.GetComponent<MeshRenderer>();
                if (meshRenderer != null && meshRenderer.sharedMaterial != null)
                {
                    Material currentMaterial = meshRenderer.sharedMaterial;
                
                    if (effectShader != null)
                    {
                        currentMaterial.shader = effectShader;
                    }
                
                    effectsComponent.effectMaterial = currentMaterial;
                    effectsComponent.isEffectActive = false;
                    currentMaterial.SetFloat("_EffectActive", 0f);
                }
                else
                {
                    Debug.LogWarning($"splat_renderer child found but no MeshRenderer or material found on {effectsComponent.gameObject.name}");
                }
            }
            else
            {
                Debug.LogWarning($"No splat_renderer child object found on {effectsComponent.gameObject.name}");
            }
        }
    
        private Shader GetEffectShader(int effectSelection, Dictionary<string, object> parameters)
        {
            if (effectSelection == CustomShaderIndex)
            {
                return GetParameterValue(parameters, "Custom Shader", (Shader)null);
            }
        
            string shaderName = GetShaderNameForEffect(effectSelection);
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogWarning($"Could not find shader: {shaderName}");
            }
            return shader;
        }
    
        private string GetShaderNameForEffect(int effectSelection)
        {
            switch (effectSelection)
            {
                case 0:
                    return "SplatXR/SplatEffectDisintegrate";
                case 1:
                    return "SplatXR/SplatEffectRadialReveal";
                case 2:
                    return "SplatXR/SplatEffectDigitalRain";
                default:
                    return "Unlit/SplatShaderColor";
            }
        }
    
        private string GetEffectTypeName(int index)
        {
            string[] types = GetDropdownOptions("Effect Selection");
            return index >= 0 && index < types.Length ? types[index] : "Unknown";
        }

        public const string CustomShaderFolder = "Assets/Materials/SplatShaders";

        public static string CreateCustomShaderTemplate(string shaderName, out string error)
        {
            error = null;

            string safeName = Regex.Replace(shaderName ?? "", @"[^A-Za-z0-9_]", "");
            if (string.IsNullOrEmpty(safeName))
            {
                error = "Please enter a shader name (letters, numbers and underscores only).";
                return null;
            }

            if (!AssetDatabase.IsValidFolder("Assets/Materials"))
            {
                AssetDatabase.CreateFolder("Assets", "Materials");
            }
            if (!AssetDatabase.IsValidFolder(CustomShaderFolder))
            {
                AssetDatabase.CreateFolder("Assets/Materials", "SplatShaders");
            }

            string assetPath = $"{CustomShaderFolder}/SplatEffect_{safeName}.shader";
            if (File.Exists(assetPath))
            {
                error = $"A shader already exists at {assetPath}. Please choose a different name.";
                return null;
            }

            File.WriteAllText(assetPath, CustomShaderTemplate.Replace("#SHADER_NAME#", $"SplatXR/SplatEffect{safeName}"));
            AssetDatabase.ImportAsset(assetPath);

            Debug.Log($"Created custom splat effect shader at {assetPath}");
            return assetPath;
        }

        private const string CustomShaderTemplate = @"// Custom Splat Effect shader created by the SplatXR Toolkit.
//
// This shader renders gaussian splats exactly like the built-in SplatXR effect shaders, but has no effect yet.
// Look for the ""YOUR EFFECT GOES HERE"" sections below to add your own effect:
//   1. applySplatEffect() - per-splat effect in the vertex shader (move, scale, fade or recolor each splat)
//   2. frag()             - per-pixel effect in the fragment shader (color/opacity of the final pixels)
//
// The SplatEffectsBuildingBlock component drives these material properties at runtime:
//   _EffectActive : 1 while the effect is playing, 0 otherwise
//   _EffectTime   : effect progress, goes from 0 to 1 (wraps back to 0 when looping is enabled)
Shader ""#SHADER_NAME#""
{
    Properties
    {
        _ScaleFactor (""Scale Factor"", Float) = 1.0

        [Header(Effect Properties)]
        _EffectActive (""Effect Active"", Float) = 0.0
        _EffectTime (""Effect Time"", Float) = 0.0
        // Add your own effect properties here (and declare matching variables in the CGPROGRAM below), e.g.
        // _EffectColor (""Effect Color"", Color) = (1,1,1,1)
        // _EffectStrength (""Effect Strength"", Range(0,10)) = 1

        [Header(Color Adjustments)]
        _ColorTint (""Color Tint"", Color) = (1,1,1,1)
        _Brightness (""Brightness"", Range(0,3)) = 1
        _Contrast (""Contrast"", Range(0,3)) = 1
        _Saturation (""Saturation"", Range(0,3)) = 1
        _Temperature (""Temperature"", Range(-1,1)) = 0
        _Transparency (""Transparency"", Range(0,1)) = 1

        [Header(ASC CDL Color Correction)]
        _ASC_CDL_Slope (""ASC-CDL Slope (RGB)"", Vector) = (1,1,1,0)
        _ASC_CDL_Offset (""ASC-CDL Offset (RGB)"", Vector) = (0,0,0,0)
        _ASC_CDL_Power (""ASC-CDL Power (RGB)"", Vector) = (1,1,1,0)
        _ASC_CDL_Enable (""Enable ASC-CDL"", Float) = 0
    }
    SubShader
    {
        Tags {
            ""RenderType"" = ""Transparent""
            ""Queue"" = ""Transparent""
            ""IgnoreProjector"" = ""True""
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

            #include ""UnityCG.cginc""

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

            float3 hash(float3 p)
            {
                return frac(sin(p * 123.456) * 123.456);
            }

            float _ScaleFactor;
            float _EffectActive;
            float _EffectTime;
            // Declare your own effect property variables here, e.g.

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

            struct SplatEffectResult
            {
                float3 positionOffset;
                float scale;
                float opacity;
                float3 color;
            };

            SplatEffectResult applySplatEffect(float3 worldPos, float3 splatColor, float t)
            {
                SplatEffectResult r;
                r.positionOffset = float3(0.0, 0.0, 0.0);
                r.scale = 1.0;
                r.opacity = 1.0;
                r.color = float3(1.0, 1.0, 1.0);

                // ================================================================
                // YOUR EFFECT GOES HERE (per splat, vertex shader)
                //   worldPos   : world-space center of this splat
                //   splatColor : original color of this splat (linear space)
                //   t          : _EffectTime, from 0 to 1 while the effect plays
                //
                // Examples:
                //   r.positionOffset.y = t * 2.0;                        // float splats upward
                //   r.opacity = 1.0 - t;                                 // fade everything out
                //   r.scale = lerp(1.0, 0.0, t);                         // shrink splats away
                //   float n = hash(worldPos).x;                          // random value per splat
                //   r.opacity = step(n, 1.0 - t);                        // splats randomly disappear
                //   r.color = lerp(float3(1,1,1), float3(1,0.3,0.1), t); // tint toward orange
                // ================================================================

                return r;
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
                float3 linearColor = GammaToLinearSpace(applyASC_CDL(v.color));

                float effectScale = 1.0;
                float effectOpacity = 1.0;
                float3 effectColor = float3(1.0, 1.0, 1.0);
                if (_EffectActive > 0.5) {
                    SplatEffectResult effect = applySplatEffect(worldCenter.xyz, linearColor, _EffectTime);
                    worldCenter.xyz += effect.positionOffset;
                    effectScale = effect.scale;
                    effectOpacity = effect.opacity;
                    effectColor = effect.color;
                }

                o.colorOpacity.a = v.opacity * effectOpacity * _Transparency;
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

                float scale2 = _ScaleFactor * _ScaleFactor * effectScale * effectScale;
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

                o.colorOpacity.rgb = adjustColor(linearColor * effectColor * _ColorTint.rgb);

                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float A = dot(i.vpositionScaled, i.vpositionScaled);
                if (A > 8.0) discard;

                float3 finalColor = i.colorOpacity.rgb;
                float opacity = exp(-0.5 * A) * i.colorOpacity.a;

                // ================================================================
                // YOUR EFFECT GOES HERE (per pixel, fragment shader) - optional
                //   finalColor : this splat's color after the color adjustments
                //   opacity    : gaussian opacity of this pixel
                //   A          : squared distance from the splat center (0 at the center, 8 at the edge)
                //
                // Example:
                //   finalColor = lerp(finalColor, float3(1,1,1), step(4.0, A)); // white rim on each splat
                // ================================================================

                return fixed4(finalColor * opacity, opacity);
            }
            ENDCG
        }
    }
}
";
    }
}