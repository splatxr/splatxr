using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace SplatXR
{
    [CustomEditor(typeof(SplatPhysicsBuildingBlock))]
    public class SplatPhysicsBuildingBlockEditor : Editor
    {
        private readonly SplatPhysicsBlock physicsBlock = new SplatPhysicsBlock();

        private Dictionary<string, object> regenerateParameters;
        private bool showRegenerate = false;

        private void OnEnable()
        {
            var component = (SplatPhysicsBuildingBlock)target;

            regenerateParameters = physicsBlock.InitializeParameters();
            if (component.HasMeshGenerationSettings)
            {
                var settings = component.LastMeshGenerationSettings;
                regenerateParameters["Splat Type"] = settings.splatType;
                regenerateParameters["Mesh Threshold"] = settings.meshThreshold;
                regenerateParameters["Gaussian Mesh Scale"] = settings.gaussianMeshScale;
                regenerateParameters["Mesh Base Flattening"] = settings.meshBaseFlattening;
                regenerateParameters["Flatten Percentage"] = settings.flattenPercentage;
                regenerateParameters["Flatten Axis"] = settings.flattenAxis;
            }
        }

        public override void OnInspectorGUI()
        {
            var component = (SplatPhysicsBuildingBlock)target;

            EditorGUILayout.LabelField("Physics Mesh Generation", EditorStyles.boldLabel);

            string info = string.IsNullOrEmpty(component.MeshGenerationInfo)
                ? "No mesh generation info recorded. Run the Splat Physics building block on this splat to generate its mesh."
                : component.MeshGenerationInfo;
            EditorGUILayout.HelpBox(info, MessageType.None);

            EditorGUILayout.Space();
            showRegenerate = EditorGUILayout.Foldout(showRegenerate, "Regenerate Mesh", true);
            if (showRegenerate)
            {
                EditorGUI.indentLevel++;
                DrawRegenerateControls(component);
                EditorGUI.indentLevel--;
            }
        }

        private void DrawRegenerateControls(SplatPhysicsBuildingBlock component)
        {
            EditorGUILayout.HelpBox("Generates a new physics mesh with these settings and replaces the current mesh and collider.", MessageType.Info);

            if (SplatXRMeshingTool.FindExecutable() == null)
            {
                SplatXRMeshingTool.DrawNotice();
            }

            foreach (string key in new List<string>(regenerateParameters.Keys))
            {
                if (key == "Target Splat" || !physicsBlock.IsParameterVisible(key, regenerateParameters))
                {
                    continue;
                }

                object value = regenerateParameters[key];
                if (value is int intValue)
                {
                    string[] options = physicsBlock.GetDropdownOptions(key);
                    regenerateParameters[key] = options != null
                        ? EditorGUILayout.Popup(key, intValue, options)
                        : EditorGUILayout.IntField(key, intValue);
                }
                else if (value is float flattenPercentage && key == "Flatten Percentage")
                {
                    regenerateParameters[key] = SplatPhysicsBlock.FlattenPercentageField(key, flattenPercentage);
                }
                else if (value is float floatValue)
                {
                    float max = key == "Gaussian Mesh Scale" ? 2f : 1f;
                    regenerateParameters[key] = EditorGUILayout.Slider(key, floatValue, 0f, max);
                }
                else if (value is bool boolValue)
                {
                    regenerateParameters[key] = EditorGUILayout.Toggle(key, boolValue);
                }
            }

            EditorGUILayout.Space();
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Regenerate Mesh", GUILayout.Width(150), GUILayout.Height(25)))
            {
                var parameters = new Dictionary<string, object>(regenerateParameters);
                GameObject targetSplat = component.gameObject;
                parameters["Target Splat"] = targetSplat;

                EditorApplication.delayCall += () =>
                {
                    if (targetSplat == null) return;

                    if (EditorUtility.DisplayDialog("Regenerate Physics Mesh",
                            $"Generate a new physics mesh for {targetSplat.name}? This replaces the current mesh and may take a while.",
                            "Regenerate", "Cancel"))
                    {
                        physicsBlock.AddToScene(null, parameters);
                    }
                };
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }
    }
}
