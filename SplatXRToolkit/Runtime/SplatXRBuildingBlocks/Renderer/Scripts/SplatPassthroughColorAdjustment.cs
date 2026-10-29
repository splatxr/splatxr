using UnityEngine;
using UnityEngine.Serialization;
using System;

namespace SplatXR
{
    [System.Serializable]
    [AddComponentMenu("SplatXR/Splat Passthrough Color Adjustment")]
    public class SplatPassthroughColorAdjustment : MonoBehaviour
    {

        [Serializable]
        public struct ColorCorrectionParams
        {
            public Vector3 slope;
            public Vector3 offset;
            public Vector3 power;

            public ColorCorrectionParams(Vector3 slope, Vector3 offset, Vector3 power)
            {
                this.slope = slope;
                this.offset = offset;
                this.power = power;
            }
        }

        [Header("Input Images")]
        [SerializeField, FormerlySerializedAs("sourceImage"), Tooltip("Capture of the splat render whose colors will be corrected")]
        private Texture2D splatRenderImage;

        [SerializeField, FormerlySerializedAs("targetImage"), Tooltip("Passthrough camera image the splat colors should match")]
        private Texture2D passthroughImage;

        [Header("Material")]
        [SerializeField, Tooltip("Material to apply color correction to")]
        private Material targetMaterial;

        public Texture2D SplatRenderImage { get => splatRenderImage; set => splatRenderImage = value; }
        public Texture2D PassthroughImage { get => passthroughImage; set => passthroughImage = value; }
        public Material TargetMaterial { get => targetMaterial; set => targetMaterial = value; }

        public static bool ApplyColorCorrectionToMaterial(Texture2D sourceImage, Texture2D targetImage, Material material)
        {
            if (sourceImage == null || targetImage == null || material == null)
            {
                Debug.LogError("SplatPassthroughColorAdjustment: One or more input parameters are null");
                return false;
            }

            try
            {
                ColorCorrectionParams correctionParams = CalculateASCCDL(sourceImage, targetImage);
            
                material.SetVector("_ASC_CDL_Slope", new Vector4(correctionParams.slope.x, correctionParams.slope.y, correctionParams.slope.z, 0));
                material.SetVector("_ASC_CDL_Offset", new Vector4(correctionParams.offset.x, correctionParams.offset.y, correctionParams.offset.z, 0));
                material.SetVector("_ASC_CDL_Power", new Vector4(correctionParams.power.x, correctionParams.power.y, correctionParams.power.z, 0));
                material.SetFloat("_ASC_CDL_Enable", 1.0f);

                Debug.Log($"SplatPassthroughColorAdjustment: Successfully applied ASC-CDL correction to material '{material.name}'");

                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"SplatPassthroughColorAdjustment: Error applying color correction to material: {e.Message}");
                return false;
            }
        }

        public static void DisableColorCorrectionOnMaterial(Material material)
        {
            if (material == null)
            {
                Debug.LogError("SplatPassthroughColorAdjustment: Material is null");
                return;
            }

            material.SetFloat("_ASC_CDL_Enable", 0.0f);
        }

        public static void ResetColorCorrectionOnMaterial(Material material)
        {
            if (material == null)
            {
                Debug.LogError("SplatPassthroughColorAdjustment: Material is null");
                return;
            }

            material.SetVector("_ASC_CDL_Slope", new Vector4(1, 1, 1, 0));
            material.SetVector("_ASC_CDL_Offset", new Vector4(0, 0, 0, 0));
            material.SetVector("_ASC_CDL_Power", new Vector4(1, 1, 1, 0));
            material.SetFloat("_ASC_CDL_Enable", 0.0f);

            Debug.Log($"SplatPassthroughColorAdjustment: Reset ASC-CDL correction on material '{material.name}' to neutral values");
        }

        public static ColorCorrectionParams CalculateASCCDL(Texture2D sourceImage, Texture2D targetImage)
        {
            Color[] sourceColors = null;
            Color[] targetColors = null;

            try
            {
                if (!EnsureTextureReadable(sourceImage) || !EnsureTextureReadable(targetImage))
                {
                    Debug.LogWarning("SplatPassthroughColorAdjustment: Direct pixel access failed, trying alternative method...");
                
                    sourceColors = GetPixelsAlternative(sourceImage);
                    targetColors = GetPixelsAlternative(targetImage);
                
                    if (sourceColors == null || targetColors == null)
                    {
                        Debug.LogError("SplatPassthroughColorAdjustment: All methods to read texture pixels failed");
                        return new ColorCorrectionParams(Vector3.one, Vector3.zero, Vector3.one);
                    }
                }
                else
                {
                    sourceColors = sourceImage.GetPixels();
                    targetColors = targetImage.GetPixels();
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"SplatPassthroughColorAdjustment: Exception during pixel access: {e.Message}");
            
                Debug.LogWarning("Attempting fallback pixel reading method...");
                sourceColors = GetPixelsAlternative(sourceImage);
                targetColors = GetPixelsAlternative(targetImage);
            
                if (sourceColors == null || targetColors == null)
                {
                    Debug.LogError("SplatPassthroughColorAdjustment: All pixel reading methods failed");
                    return new ColorCorrectionParams(Vector3.one, Vector3.zero, Vector3.one);
                }
            }

            Vector3 sourceMean = CalculateMean(sourceColors);
            Vector3 targetMean = CalculateMean(targetColors);
            Vector3 sourceStd = CalculateStandardDeviation(sourceColors, sourceMean);
            Vector3 targetStd = CalculateStandardDeviation(targetColors, targetMean);

            Vector3 slope = new Vector3(
                sourceStd.x != 0 ? targetStd.x / sourceStd.x : 1.0f,
                sourceStd.y != 0 ? targetStd.y / sourceStd.y : 1.0f,
                sourceStd.z != 0 ? targetStd.z / sourceStd.z : 1.0f
            );

            Vector3 offset = targetMean - Vector3.Scale(sourceMean, slope);
            Vector3 power = Vector3.one;

            slope = ValidateASCCDLParameters(slope, offset, power, out offset, out power);

            return new ColorCorrectionParams(slope, offset, power);
        }

        private static Vector3 ValidateASCCDLParameters(Vector3 slope, Vector3 offset, Vector3 power, out Vector3 validatedOffset, out Vector3 validatedPower)
        {
            Vector3 validatedSlope = new Vector3(
                Mathf.Clamp(slope.x, 0.1f, 5.0f),
                Mathf.Clamp(slope.y, 0.1f, 5.0f),
                Mathf.Clamp(slope.z, 0.1f, 5.0f)
            );

            validatedOffset = new Vector3(
                Mathf.Clamp(offset.x, -0.5f, 0.5f),
                Mathf.Clamp(offset.y, -0.5f, 0.5f),
                Mathf.Clamp(offset.z, -0.5f, 0.5f)
            );

            validatedPower = new Vector3(
                Mathf.Clamp(power.x, 0.5f, 2.0f),
                Mathf.Clamp(power.y, 0.5f, 2.0f),
                Mathf.Clamp(power.z, 0.5f, 2.0f)
            );

            if (!Mathf.Approximately(slope.x, validatedSlope.x) || !Mathf.Approximately(slope.y, validatedSlope.y) || !Mathf.Approximately(slope.z, validatedSlope.z))
            {
                Debug.LogWarning($"ASC-CDL Slope clamped from {slope} to {validatedSlope}");
            }
            if (!Mathf.Approximately(offset.x, validatedOffset.x) || !Mathf.Approximately(offset.y, validatedOffset.y) || !Mathf.Approximately(offset.z, validatedOffset.z))
            {
                Debug.LogWarning($"ASC-CDL Offset clamped from {offset} to {validatedOffset}");
            }
            if (!Mathf.Approximately(power.x, validatedPower.x) || !Mathf.Approximately(power.y, validatedPower.y) || !Mathf.Approximately(power.z, validatedPower.z))
            {
                Debug.LogWarning($"ASC-CDL Power clamped from {power} to {validatedPower}");
            }

            return validatedSlope;
        }

        private static Vector3 CalculateMean(Color[] colors)
        {
            Vector3 sum = Vector3.zero;

            for (int i = 0; i < colors.Length; i++)
            {
                sum.x += colors[i].r;
                sum.y += colors[i].g;
                sum.z += colors[i].b;
            }

            return sum / colors.Length;
        }

        private static Vector3 CalculateStandardDeviation(Color[] colors, Vector3 mean)
        {
            Vector3 sumSquaredDiffs = Vector3.zero;

            for (int i = 0; i < colors.Length; i++)
            {
                Vector3 diff = new Vector3(
                    colors[i].r - mean.x,
                    colors[i].g - mean.y,
                    colors[i].b - mean.z
                );

                sumSquaredDiffs.x += diff.x * diff.x;
                sumSquaredDiffs.y += diff.y * diff.y;
                sumSquaredDiffs.z += diff.z * diff.z;
            }

            return new Vector3(
                Mathf.Sqrt(sumSquaredDiffs.x / colors.Length),
                Mathf.Sqrt(sumSquaredDiffs.y / colors.Length),
                Mathf.Sqrt(sumSquaredDiffs.z / colors.Length)
            );
        }

        private static bool EnsureTextureReadable(Texture2D texture)
        {
            if (texture == null)
            {
                Debug.LogError("SplatPassthroughColorAdjustment: Texture is null");
                return false;
            }

            if (!texture.isReadable)
            {
                Debug.LogError($"SplatPassthroughColorAdjustment: Texture '{texture.name}' reports as not readable. " +
                              $"Please ensure 'Read/Write Enabled' is checked in the texture import settings.");
                return false;
            }

            try
            {
                Color testPixel = texture.GetPixel(0, 0);
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"SplatPassthroughColorAdjustment: Failed to read pixels from texture '{texture.name}': {e.Message}");
                Debug.LogError($"Texture info - Width: {texture.width}, Height: {texture.height}, " +
                              $"Format: {texture.format}, Readable: {texture.isReadable}");
            
                Debug.LogError($"Troubleshooting steps:\n" +
                              $"1. Select texture '{texture.name}' in Project window\n" +
                              $"2. In Inspector, check 'Read/Write Enabled'\n" +
                              $"3. Click 'Apply' and wait for reimport\n" +
                              $"4. Try a different texture format (e.g., RGBA32)\n" +
                              $"5. Ensure texture is not compressed with a format that doesn't support read/write");
            
                return false;
            }
        }

        private static Color[] GetPixelsAlternative(Texture2D texture)
        {
            if (texture == null) return null;

            try
            {
                RenderTexture renderTexture = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32);
                RenderTexture.active = renderTexture;

                Graphics.Blit(texture, renderTexture);

                Texture2D readableTexture = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
                readableTexture.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                readableTexture.Apply();

                Color[] pixels = readableTexture.GetPixels();

                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(renderTexture);
                UnityEngine.Object.DestroyImmediate(readableTexture);

                return pixels;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Alternative pixel reading failed for texture '{texture.name}': {e.Message}");
                return null;
            }
        }

        [ContextMenu("Apply Color Correction to Material")]
        public void ApplyColorCorrection()
        {
            if (!ValidateInputs())
                return;

            ApplyColorCorrectionToMaterial(splatRenderImage, passthroughImage, targetMaterial);
        }

        [ContextMenu("Disable Color Correction")]
        public void DisableColorCorrection()
        {
            if (targetMaterial == null)
            {
                Debug.LogError("SplatPassthroughColorAdjustment: No target material assigned");
                return;
            }

            DisableColorCorrectionOnMaterial(targetMaterial);
        }

        [ContextMenu("Reset Color Correction")]
        public void ResetColorCorrection()
        {
            if (targetMaterial == null)
            {
                Debug.LogError("SplatPassthroughColorAdjustment: No target material assigned");
                return;
            }

            ResetColorCorrectionOnMaterial(targetMaterial);
        }

        private void Reset()
        {
        }

        private void OnEnable()
        {
        }

        private void OnDisable()
        {
        }

        private bool ValidateInputs()
        {
            if (splatRenderImage == null)
            {
                Debug.LogError("SplatPassthroughColorAdjustment: Splat render image is not assigned");
                return false;
            }

            if (passthroughImage == null)
            {
                Debug.LogError("SplatPassthroughColorAdjustment: Passthrough image is not assigned");
                return false;
            }

            if (targetMaterial == null)
            {
                Debug.LogError("SplatPassthroughColorAdjustment: Target material is not assigned");
                return false;
            }

            if (!EnsureTextureReadable(splatRenderImage) || !EnsureTextureReadable(passthroughImage))
            {
                return false;
            }

            return true;
        }

    }
}