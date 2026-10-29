using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.IO;

namespace SplatXR
{
    public class SplatRendererBlock : SplatXRBuildingBlockBase
    {
        public override string Name => "Splat Renderer";

        private static readonly Vector3 DefaultPlyUpDirection = new Vector3(0f, -1f, 0f);

        public override string Description => "Core rendering component for Gaussian splats with optimized GPU rendering pipeline. For optimal performance, maximum splats in a scene should not exceed 200k.\nChoose between Base Renderer (single PLY file) or LoD Renderer (multiple detail levels for distance-based optimization).\nThis building block will create a new splat file and a new splat material.";
        public override string DocumentationUrl => "https://splatxr.github.io/docs/building-blocks/renderer.html";
    
        public override Dictionary<string, object> InitializeParameters()
        {
            return new Dictionary<string, object>
            {
                ["Object Name"] = "SplatObject",
                ["Renderer"] = 0,
                ["Input Splat File (PLY)"] = "",
                ["High Detail Splat"] = "",
                ["Medium Detail Splat"] = "",
                ["Low Detail Splat"] = "",
                ["PLY Up Direction"] = DefaultPlyUpDirection
    };
        }
    
        public override bool IsParameterVisible(string parameterName, Dictionary<string, object> parameters)
        {
            int rendererType = GetParameterValue(parameters, "Renderer", 0);
        
            switch (parameterName)
            {
                case "Input Splat File (PLY)":
                    return rendererType == 0;
                case "High Detail Splat":
                case "Medium Detail Splat":
                case "Low Detail Splat":
                    return rendererType == 1;
                default:
                    return true;
            }
        }
    
        public override string[] GetDropdownOptions(string parameterName)
        {
            switch (parameterName)
            {
                case "Renderer":
                    return new string[] { "Base Renderer", "LoD Renderer" };
                default:
                    return base.GetDropdownOptions(parameterName);
            }
        }

        public override void AddToScene(GameObject targetObject, Dictionary<string, object> parameters)
        {
            string objectName = GetParameterValue(parameters, "Object Name", "SplatObject");
            int rendererType = GetParameterValue(parameters, "Renderer", 0);

            bool useExistingObject = targetObject != null &&
                (targetObject.GetComponent<SplatRendererBuildingBlock>() != null ||
                 targetObject.name == objectName);

            if (!useExistingObject)
            {
                objectName = GetUniqueObjectName(objectName, name => $"Assets/Materials/SplatMaterials/SplatMaterial_{GetSafeMaterialName(name)}.mat");
            }
            string safeObjectName = GetSafeMaterialName(objectName);

            Vector3 plyUpDirection = GetParameterValue(parameters, "PLY Up Direction", DefaultPlyUpDirection);
            if (plyUpDirection.sqrMagnitude < 1e-8f)
            {
                Debug.LogError("PLY Up Direction can't be (0, 0, 0). Use e.g. (0, -1, 0) for Y-down, (0, 1, 0) for Y-up or (0, 0, 1) for Z-up.");
                return;
            }
            Vector4 uprightRotationWXYZ = GetUprightRotationWXYZ(plyUpDirection);

            TextAsset baseSplat = null, highSplat = null, mediumSplat = null, lowSplat = null;
            if (rendererType == 0)
            {
                if (!TryConvertSplat(parameters, "Input Splat File (PLY)", $"{safeObjectName}_usplat_splat", uprightRotationWXYZ, out baseSplat))
                    return;
            }
            else if (rendererType == 1)
            {
                if (!TryConvertSplat(parameters, "High Detail Splat", $"{safeObjectName}_high_usplat_splat", uprightRotationWXYZ, out highSplat) ||
                    !TryConvertSplat(parameters, "Medium Detail Splat", $"{safeObjectName}_medium_usplat_splat", uprightRotationWXYZ, out mediumSplat) ||
                    !TryConvertSplat(parameters, "Low Detail Splat", $"{safeObjectName}_low_usplat_splat", uprightRotationWXYZ, out lowSplat))
                    return;
            }

            GameObject mainObject;
            if (useExistingObject)
            {
                mainObject = targetObject;
                Debug.Log($"Using existing GameObject '{mainObject.name}' for Splat Renderer");
            }
            else
            {
                if (targetObject != null)
                {
                    Object.DestroyImmediate(targetObject);
                }
                mainObject = new GameObject(objectName);
            }

            var splatRendererComponent = mainObject.GetComponent<SplatRendererBuildingBlock>();
            if (splatRendererComponent == null)
            {
                splatRendererComponent = mainObject.AddComponent<SplatRendererBuildingBlock>();
            }

            GameObject splatRendererChild = null;
            Transform existingChild = mainObject.transform.Find("splat_renderer");

            if (existingChild != null)
            {
                splatRendererChild = existingChild.gameObject;
            }
            else
            {
                splatRendererChild = new GameObject("splat_renderer");
                splatRendererChild.transform.SetParent(mainObject.transform);
            }

            var meshRenderer = splatRendererChild.GetComponent<MeshRenderer>();
            if (meshRenderer == null)
            {
                meshRenderer = splatRendererChild.AddComponent<MeshRenderer>();
            }

            var meshFilter = splatRendererChild.GetComponent<MeshFilter>();
            if (meshFilter == null)
            {
                meshFilter = splatRendererChild.AddComponent<MeshFilter>();
            }


            Shader splatShader = Shader.Find("SplatXR/SplatShader");
            if (splatShader == null)
            {
                splatShader = Shader.Find("Unlit/Texture");
                if (splatShader == null)
                {
                    splatShader = Shader.Find("Standard");
                }
                Debug.LogWarning("SplatXR/SplatShader shader not found. Using fallback shader: " + (splatShader != null ? splatShader.name : "None"));
            }

            if (splatShader == null)
            {
                Debug.LogError("Could not find any suitable shader for splat material!");
                return;
            }

            Material splatMaterial = new Material(splatShader);
            splatMaterial.name = $"SplatMaterial_{safeObjectName}";

            splatMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 1;

            string materialsDir = "Assets/Materials";
            string splatMaterialsDir = "Assets/Materials/SplatMaterials";
            if (!AssetDatabase.IsValidFolder(materialsDir))
            {
                AssetDatabase.CreateFolder("Assets", "Materials");
            }
            if (!AssetDatabase.IsValidFolder(splatMaterialsDir))
            {
                AssetDatabase.CreateFolder("Assets/Materials", "SplatMaterials");
            }

            string materialPath = $"Assets/Materials/SplatMaterials/SplatMaterial_{safeObjectName}.mat";
            AssetDatabase.CreateAsset(splatMaterial, materialPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Material savedMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (savedMaterial != null && meshRenderer != null)
            {
                meshRenderer.sharedMaterial = savedMaterial;
                EditorUtility.SetDirty(meshRenderer);
            }

            EditorUtility.SetDirty(splatRendererChild);

            splatRendererComponent.splatRenderer = splatRendererChild;
            splatRendererComponent.rendererType = (SplatRendererBuildingBlock.RendererType)rendererType;

            if (rendererType == 0)
            {
                AddBaseRenderer(splatRendererChild, baseSplat);
            }
            else if (rendererType == 1)
            {
                AddLoDRenderer(splatRendererChild, highSplat, mediumSplat, lowSplat);
            }

            Selection.activeGameObject = mainObject;

            EditorUtility.SetDirty(mainObject);

            AssetDatabase.Refresh();

            if (useExistingObject)
            {
                Debug.Log($"Updated existing Splat Renderer configuration for '{mainObject.name}'");
            }
            else
            {
            }
        }

        private bool TryConvertSplat(Dictionary<string, object> parameters, string parameterName, string outputName, Vector4 uprightRotationWXYZ, out TextAsset splatAsset)
        {
            splatAsset = null;
            string plyPath = GetParameterValue(parameters, parameterName, "");
            if (string.IsNullOrEmpty(plyPath))
            {
                return true;
            }

            if (!File.Exists(plyPath))
            {
                Debug.LogError($"{parameterName} not found: {plyPath}");
                return false;
            }

            string usplatFolderPath = "Assets/Resources/Usplats";
            if (!AssetDatabase.IsValidFolder(usplatFolderPath))
            {
                if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                {
                    AssetDatabase.CreateFolder("Assets", "Resources");
                }
                AssetDatabase.CreateFolder("Assets/Resources", "Usplats");
            }

            string outputPath = $"{usplatFolderPath}/{outputName}.csv";
            try
            {
                PlyToUsplatConverter.Convert(plyPath, outputPath, sceneWXYZ: uprightRotationWXYZ, flipY: false);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Failed to convert {parameterName} ({plyPath}): {ex.Message}");
                return false;
            }

            AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceUpdate);
            splatAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(outputPath);
            if (splatAsset == null)
            {
                Debug.LogError($"Failed to load converted CSV file as TextAsset: {outputPath}");
                return false;
            }

            return true;
        }

        private void AddBaseRenderer(GameObject splatRendererChild, TextAsset splatFile)
        {
            if (splatFile == null)
            {
                Debug.LogWarning("No Input Splat File (PLY) specified. Skipping PLY conversion and renderer setup.");
                return;
            }

            var existingLoDRenderer = splatRendererChild.GetComponent<LoDRendererSplatXR>();
            if (existingLoDRenderer != null)
            {
                Object.DestroyImmediate(existingLoDRenderer);
            }

            var baseRenderer = splatRendererChild.GetComponent<BaseRendererSplatXR>();
            if (baseRenderer == null)
            {
                baseRenderer = splatRendererChild.AddComponent<BaseRendererSplatXR>();
            }

            Material splatMaterial = splatRendererChild.GetComponent<MeshRenderer>()?.sharedMaterial;
            if (splatMaterial != null)
            {
                baseRenderer.material = splatMaterial;
            }

            baseRenderer.splatFilePath = splatFile;
        }

        private void AddLoDRenderer(GameObject splatRendererChild, TextAsset highDetailFile, TextAsset mediumDetailFile, TextAsset lowDetailFile)
        {
            var existingBaseRenderer = splatRendererChild.GetComponent<BaseRendererSplatXR>();
            if (existingBaseRenderer != null)
            {
                Object.DestroyImmediate(existingBaseRenderer);
            }

            var lodRenderer = splatRendererChild.GetComponent<LoDRendererSplatXR>();
            if (lodRenderer == null)
            {
                lodRenderer = splatRendererChild.AddComponent<LoDRendererSplatXR>();
            }

            Material splatMaterial = splatRendererChild.GetComponent<MeshRenderer>()?.sharedMaterial;
            if (splatMaterial != null)
            {
                lodRenderer.material = splatMaterial;
            }

            if (highDetailFile != null) lodRenderer.splatFilePathHigh = highDetailFile;
            if (mediumDetailFile != null) lodRenderer.splatFilePathMedium = mediumDetailFile;
            if (lowDetailFile != null) lodRenderer.splatFilePathLow = lowDetailFile;

            if (highDetailFile == null && mediumDetailFile == null && lowDetailFile == null)
            {
                Debug.LogWarning("No LOD splat files specified. LoDRendererSplatXR component added but no data assigned.");
            }
        }

        private static Vector4 GetUprightRotationWXYZ(Vector3 plyUpDirection)
        {
            Vector3 up = plyUpDirection.normalized;

            Quaternion rotation = Vector3.Dot(up, Vector3.down) > 0.9999f
                ? Quaternion.AngleAxis(180f, Vector3.forward)
                : Quaternion.FromToRotation(up, Vector3.up);

            return new Vector4(rotation.w, rotation.x, rotation.y, rotation.z);
        }

        private static string GetSafeMaterialName(string objectName)
        {
            return objectName.Replace(" ", "_").Replace("/", "_").Replace("\\", "_").Replace(":", "_").Replace("*", "_").Replace("?", "_").Replace("\"", "_").Replace("<", "_").Replace(">", "_").Replace("|", "_");
        }

        private string GetRendererTypeName(int index)
        {
            string[] rendererTypes = GetDropdownOptions("Renderer");
            return index >= 0 && index < rendererTypes.Length ? rendererTypes[index] : "Unknown";
        }
    }
}