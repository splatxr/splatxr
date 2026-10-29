using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace SplatXR
{
    public class SplatBlendingBlock : SplatXRBuildingBlockBase
    {
        public override string Name => "Splat Blending";
    
        public override string Description => "Building block for adding visual experiences such as a portal effect for splat spaces. This building block will add left and right camera GameObjects under the CenterEyeAnchor in the Meta Camera Rig. To add objects visible through the portal shader/material, add those GameObjects to a layer only visible by the left and right blending cameras, such as the BlendingWorld layer. You can apply the Blending Portal material on a mesh to create the blending portal window.";
        public override string DocumentationUrl => "https://splatxr.github.io/docs/building-blocks/blending.html";
        public override Dictionary<string, object> InitializeParameters()
        {
            return new Dictionary<string, object>
            {
                ["XR Platform"] = 0,                      
                ["Center Eye Camera"] = (GameObject)null
            };
        }
    
        public override string[] GetDropdownOptions(string parameterName)
        {
            switch (parameterName)
            {
                case "XR Platform":
                    return new string[] { "Meta SDK", "Other" };
                default:
                    return base.GetDropdownOptions(parameterName);
            }
        }

        public override void AddToScene(GameObject targetObject, Dictionary<string, object> parameters)
        {
            int xrPlatform = GetParameterValue(parameters, "XR Platform", 0);
            string platformName = GetXRPlatformName(xrPlatform);

            if (platformName == "Other")
            {
                Debug.LogWarning("Splat Blending on platforms other than Meta SDK is untested. On Apple Vision Pro, only fully immersive (Metal) apps are expected to work.");
            }

            GameObject centerEyeCameraObject = GetParameterValue(parameters, "Center Eye Camera", (GameObject)null);

            if (centerEyeCameraObject == null)
            {
                Debug.LogError("Center Eye Camera is not specified. Please assign the center eye camera GameObject (CenterEyeAnchor for Meta SDK).");
                return;
            }

            Transform centerEyeCamera = ResolveCenterEyeCamera(centerEyeCameraObject);
            if (centerEyeCamera == null)
            {
                Debug.LogError($"Could not find a camera on or under {centerEyeCameraObject.name}. Please assign the center eye camera GameObject (CenterEyeAnchor for Meta SDK).");
                return;
            }

            bool cameraAlreadySetUp =
                FindPrefabInstanceInScene("LeftEyeBlending.prefab", centerEyeCamera) != null ||
                FindPrefabInstanceInScene("RightEyeBlending.prefab", centerEyeCamera) != null;

            GameObject portalInstance = cameraAlreadySetUp ? FindPrefabInstanceInScene("SplatBlendingPortal.prefab", null) : null;
            if (portalInstance != null)
            {
                Debug.Log($"Splat Blending is already set up on {centerEyeCamera.name}, reusing {portalInstance.name} instead of adding another");
            }
            else
            {
                portalInstance = SpawnPrefab("SplatBlendingPortal.prefab", null);
                if (portalInstance == null)
                {
                    Debug.LogError("Failed to spawn SplatBlendingPortal prefab.");
                    return;
                }
            }

            GameObject leftEyeBlending = FindOrSpawnUnder("LeftEyeBlending.prefab", centerEyeCamera);
            GameObject rightEyeBlending = FindOrSpawnUnder("RightEyeBlending.prefab", centerEyeCamera);

            int blendingWorldLayer = CreateOrGetLayer("BlendingWorld");
            if (blendingWorldLayer >= 0)
            {
                ExcludeLayerFromCamera(centerEyeCamera, blendingWorldLayer);
            
                ConfigureCameraCullingMask(leftEyeBlending, blendingWorldLayer);
                ConfigureCameraCullingMask(rightEyeBlending, blendingWorldLayer);
            }
        
        
            Selection.activeGameObject = portalInstance;
        
            EditorUtility.SetDirty(portalInstance);
            if (leftEyeBlending != null) EditorUtility.SetDirty(leftEyeBlending);
            if (rightEyeBlending != null) EditorUtility.SetDirty(rightEyeBlending);
        }
    
        #region Helper Methods
        private string GetXRPlatformName(int index)
        {
            var options = GetDropdownOptions("XR Platform");
            return index >= 0 && index < options.Length ? options[index] : "Unknown";
        }
    
        private GameObject SpawnPrefab(string prefabName, Transform parent)
        {
            string prefabPath = GetPrefabPath(prefabName);
        
            if (string.IsNullOrEmpty(prefabPath))
            {
                Debug.LogWarning($"Could not find prefab path for: {prefabName}");
                return null;
            }
        
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogWarning($"Could not load prefab at path: {prefabPath}");
                return null;
            }
        
            GameObject instance = PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject;
            
        
            return instance;
        }
    
        private Transform ResolveCenterEyeCamera(GameObject centerEyeCameraObject)
        {
            if (centerEyeCameraObject.GetComponent<Camera>() != null)
                return centerEyeCameraObject.transform;

            Transform centerEyeAnchor = centerEyeCameraObject.transform.Find("TrackingSpace/CenterEyeAnchor");
            if (centerEyeAnchor != null && centerEyeAnchor.GetComponent<Camera>() != null)
            {
                return centerEyeAnchor;
            }

            foreach (Camera childCamera in centerEyeCameraObject.GetComponentsInChildren<Camera>(true))
            {
                if (childCamera.CompareTag("MainCamera"))
                {
                    return childCamera.transform;
                }
            }

            return null;
        }
        private GameObject FindOrSpawnUnder(string prefabName, Transform parent)
        {
            GameObject existing = FindPrefabInstanceInScene(prefabName, parent);
            if (existing != null)
            {
                return existing;
            }

            GameObject instance = SpawnPrefab(prefabName, parent);

            return instance;
        }

        private GameObject FindPrefabInstanceInScene(string prefabName, Transform parent)
        {
            string objectName = System.IO.Path.GetFileNameWithoutExtension(prefabName);
            string prefabPath = GetPrefabPath(prefabName);
            GameObject prefabAsset = string.IsNullOrEmpty(prefabPath) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            IEnumerable<GameObject> candidates;
            if (parent != null)
            {
                var children = new List<GameObject>();
                foreach (Transform child in parent)
                {
                    children.Add(child.gameObject);
                }
                candidates = children;
            }
            else
            {
                candidates = Resources.FindObjectsOfTypeAll<GameObject>();
            }

            GameObject nameMatch = null;
            foreach (GameObject go in candidates)
            {
                if (!go.scene.IsValid()) continue;

                if (prefabAsset != null && PrefabUtility.IsOutermostPrefabInstanceRoot(go) &&
                    PrefabUtility.GetCorrespondingObjectFromOriginalSource(go) == prefabAsset)
                {
                    return go;
                }

                if (nameMatch == null && go.name == objectName)
                {
                    nameMatch = go;
                }
            }
            return nameMatch;
        }

        private string GetPrefabPath(string prefabName)
        {
            string prefabPath = SplatXRPackagePaths.GetAssetPath($"Runtime/SplatXRBuildingBlocks/Blending/Prefabs/{prefabName}");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
                return prefabPath;

            return null;
        }
    
        private int CreateOrGetLayer(string layerName)
        {
            int existingLayer = LayerMask.NameToLayer(layerName);
            if (existingLayer >= 0)
            {
                return existingLayer;
            }
        
            SerializedObject tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");
        
            for (int i = 8; i < layers.arraySize; i++)
            {
                SerializedProperty layer = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(layer.stringValue))
                {
                    layer.stringValue = layerName;
                    tagManager.ApplyModifiedProperties();
                    return i;
                }
            }
        
            Debug.LogError($"Could not create layer '{layerName}' - all layer slots are full");
            return -1;
        }
        private void ConfigureCameraCullingMask(GameObject eyeBlendingObject, int layerIndex)
        {
            if (eyeBlendingObject == null) return;
        
            Camera camera = eyeBlendingObject.GetComponentInChildren<Camera>();
            if (camera == null)
            {
                Debug.LogWarning($"No camera found in {eyeBlendingObject.name}");
                return;
            }
        
            camera.cullingMask = 1 << layerIndex;
        }
    
        private void ExcludeLayerFromCamera(Transform cameraTransform, int layerIndex)
        {
            if (cameraTransform == null) return;
        
            Camera camera = cameraTransform.GetComponent<Camera>();
            if (camera == null)
            {
                Debug.LogWarning($"No camera found on {cameraTransform.name}");
                return;
            }
        
            camera.cullingMask &= ~(1 << layerIndex);
        }
    
        #endregion
    }
}