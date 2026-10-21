using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.IO;

namespace SplatXR
{
    public class SplatPhysicsBlock : SplatXRBuildingBlockBase
    {
        public override string Name => "Splat Physics";

        public override string Description => "Generate a physics mesh for an existing splat renderer object, enabling collisions, rigid body dynamics, and shadows. Re-applying it regenerates the mesh.\nUse Object for detailed objects and Space for larger scenes.";
        public override string DocumentationUrl => "https://splatxr.github.io/docs/building-blocks/physics.html";

        public const float DefaultFlattenPercentage = 5f;
        public const string FlattenPercentageRangeError = "[SplatXR] Flatten Percentage must be greater than 0 and less than 100.";

        public static bool IsValidFlattenPercentage(float percentage)
        {
            return percentage > 0f && percentage < 100f;
        }

        public static float FlattenPercentageField(float value, params GUILayoutOption[] options)
        {
            return ValidateFlattenPercentageEdit(value, EditorGUILayout.DelayedFloatField(value, options));
        }

        public static float FlattenPercentageField(string label, float value)
        {
            return ValidateFlattenPercentageEdit(value, EditorGUILayout.DelayedFloatField(label, value));
        }

        private static float ValidateFlattenPercentageEdit(float oldValue, float newValue)
        {
            if (newValue != oldValue && !IsValidFlattenPercentage(newValue))
            {
                Debug.LogError(FlattenPercentageRangeError);
            }
            return newValue;
        }

        public override Dictionary<string, object> InitializeParameters()
        {
            return new Dictionary<string, object>
            {
                ["Target Splat"] = null,
                ["Splat Type"] = 0,
                ["Mesh Threshold"] = 0.5f,
                ["Gaussian Mesh Scale"] = 1.5f,
                ["Mesh Base Flattening"] = false,
                ["Flatten Percentage"] = DefaultFlattenPercentage,
                ["Flatten Axis"] = 1
            };
        }

        public override string[] GetDropdownOptions(string parameterName)
        {
            switch (parameterName)
            {
                case "Splat Type":
                    return new string[] { "Object", "Space" };
                case "Flatten Axis":
                    return new string[] { "X", "Y", "Z", "-X", "-Y", "-Z" };
                default:
                    return base.GetDropdownOptions(parameterName);
            }
        }

        public override bool IsParameterVisible(string parameterName, Dictionary<string, object> parameters)
        {
            bool isObjectType = GetParameterValue(parameters, "Splat Type", 0) == 0;
            bool meshBaseFlattening = GetParameterValue(parameters, "Mesh Base Flattening", false);

            switch (parameterName)
            {
                case "Mesh Threshold":
                case "Gaussian Mesh Scale":
                case "Mesh Base Flattening":
                    return isObjectType;
                case "Flatten Percentage":
                case "Flatten Axis":
                    return isObjectType && meshBaseFlattening;
                default:
                    return true;
            }
        }

        public override void AddToScene(GameObject targetObject, Dictionary<string, object> parameters)
        {
            GameObject targetSplat = GetParameterValue(parameters, "Target Splat", (GameObject)null);

            if (targetSplat == null)
            {
                Debug.LogError("No target splat provided. Please assign a target splat GameObject to apply physics to.");
                return;
            }

            if (GetParameterValue(parameters, "Splat Type", 0) == 0 &&
                GetParameterValue(parameters, "Mesh Base Flattening", false) &&
                !IsValidFlattenPercentage(GetParameterValue(parameters, "Flatten Percentage", DefaultFlattenPercentage)))
            {
                Debug.LogError(FlattenPercentageRangeError);
                return;
            }

            if (SplatXRMeshingTool.IsRunning)
            {
                Debug.LogError("[SplatXR] A physics mesh is already being generated. Wait for it to finish, or cancel it from the progress bar in the bottom-right of the editor.");
                return;
            }

            if (HasSplatRenderer(targetSplat))
            {
                if (targetSplat.transform.parent == null)
                {
                    Debug.LogError($"{targetSplat.name} draws the splat itself, so physics can't be added to it without hiding the splat. Select the parent object created by the Splat Renderer building block instead.");
                    return;
                }

                GameObject parent = targetSplat.transform.parent.gameObject;
                targetSplat = parent;
            }

            string usplatCsvPath = GetUsplatCsvPathFromSplat(targetSplat);
            if (string.IsNullOrEmpty(usplatCsvPath))
            {
                Debug.LogError("Could not find USplat CSV file path from the target splat GameObject. Make sure the target splat has a SplatXR Renderer component with a valid splatFilePath.");
                return;
            }

            string usplatDirectory = Path.GetDirectoryName(usplatCsvPath);
            string usplatFileName = Path.GetFileNameWithoutExtension(usplatCsvPath);
            string plyPath = Path.Combine(usplatDirectory, usplatFileName + "_converted.ply");

            try
            {
                UsplatToPlyConverter.Convert(usplatCsvPath, plyPath, flipY: false);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Failed to convert USplat to PLY: {ex.Message}");
                return;
            }

            ApplyPhysicsToSplat(targetSplat, usplatCsvPath, plyPath, parameters);
        }

        private void ApplyPhysicsToSplat(GameObject targetSplat, string sourceSplatPath, string plyPath, Dictionary<string, object> parameters)
        {
            int splatType = GetParameterValue(parameters, "Splat Type", 0);

            string meshFileName = Path.GetFileNameWithoutExtension(plyPath) + "_mesh.obj";
            string meshAssetPath = $"Assets/ImportedSplats/{meshFileName}";
            string meshOutputPath = Path.Combine(Application.dataPath, "ImportedSplats");
            string fullPath = Path.Combine(meshOutputPath, meshFileName);

            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }

            var meshingParameters = new Dictionary<string, object>(parameters);
            string targetName = targetSplat.name;

            bool started = CreatePhysicsSplatMesh(plyPath, splatType, meshingParameters, meshOutputPath, targetName, success =>
            {
                if (!success)
                {
                    Debug.LogError($"Mesh generation failed, physics was not added to {targetName}.");
                    return;
                }
                if (targetSplat == null)
                {
                    Debug.LogWarning($"{targetName} was deleted while its physics mesh was being generated, so physics was not added.");
                    return;
                }
                AddPhysicsComponents(targetSplat, sourceSplatPath, meshAssetPath, fullPath, splatType, meshingParameters);
            });

            if (!started)
            {
                Debug.LogError($"Mesh generation failed, physics was not added to {targetName}.");
            }
        }

        private void AddPhysicsComponents(GameObject targetSplat, string sourceSplatPath, string meshAssetPath, string fullPath, int splatType, Dictionary<string, object> parameters)
        {
            AssetDatabase.Refresh();

            if (!File.Exists(fullPath))
            {
                Debug.LogError($"Mesh file does not exist at: {fullPath}");
                return;
            }

            AssetDatabase.ImportAsset(meshAssetPath, ImportAssetOptions.ForceUpdate);

            Mesh generatedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshAssetPath);
            if (generatedMesh == null)
            {
                foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(meshAssetPath))
                {
                    if (asset is Mesh mesh)
                    {
                        generatedMesh = mesh;
                        break;
                    }
                }
            }

            if (generatedMesh == null)
            {
                Debug.LogError($"Could not load mesh from: {meshAssetPath}");
                return;
            }

            MeshFilter meshFilter = targetSplat.GetComponent<MeshFilter>();
            if (meshFilter == null)
            {
                meshFilter = targetSplat.AddComponent<MeshFilter>();
            }
            meshFilter.sharedMesh = generatedMesh;

            MeshRenderer meshRenderer = targetSplat.GetComponent<MeshRenderer>();
            if (meshRenderer == null)
            {
                meshRenderer = targetSplat.AddComponent<MeshRenderer>();
            }

            string materialPath = SplatXRPackagePaths.GetAssetPath("Runtime/SplatXRBuildingBlocks/Renderer/Materials/InvisibleSplatMesh.mat");
            Material invisibleMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);

            if (invisibleMaterial == null || invisibleMaterial.shader == null)
            {
                Debug.LogWarning($"InvisibleSplatMesh material not found or has no shader. Creating temporary invisible material.");

                Shader invisibleShader = Shader.Find("Transparent/Diffuse");
                if (invisibleShader == null)
                {
                    invisibleShader = Shader.Find("Standard");
                }

                if (invisibleShader != null)
                {
                    invisibleMaterial = new Material(invisibleShader);
                    invisibleMaterial.name = "InvisibleSplatMesh (Runtime)";

                    if (invisibleMaterial.HasProperty("_Color"))
                    {
                        invisibleMaterial.SetColor("_Color", new Color(1, 1, 1, 0));
                    }
                    if (invisibleMaterial.HasProperty("_Mode"))
                    {
                        invisibleMaterial.SetFloat("_Mode", 3);
                    }
                    if (invisibleMaterial.HasProperty("_SrcBlend"))
                    {
                        invisibleMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    }
                    if (invisibleMaterial.HasProperty("_DstBlend"))
                    {
                        invisibleMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    }
                    if (invisibleMaterial.HasProperty("_ZWrite"))
                    {
                        invisibleMaterial.SetFloat("_ZWrite", 0);
                    }

                    invisibleMaterial.renderQueue = 3000;
                }
                else
                {
                    Debug.LogError("Could not find any suitable shader for invisible material.");
                }
            }

            if (invisibleMaterial != null)
            {
                meshRenderer.sharedMaterial = invisibleMaterial;
            }
            else
            {
                Debug.LogError($"Could not create or load InvisibleSplatMesh material.");
            }

            Rigidbody rb = targetSplat.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = targetSplat.AddComponent<Rigidbody>();
            }

            rb.isKinematic = true;
            rb.useGravity = false;
            rb.mass = 1.0f;
    #if UNITY_6000_0_OR_NEWER
            rb.linearDamping = 0.0f;
            rb.angularDamping = 0.05f;
    #else
            rb.drag = 0.0f;
            rb.angularDrag = 0.05f;
    #endif

            MeshCollider meshCollider = targetSplat.GetComponent<MeshCollider>();
            if (meshCollider == null)
            {
                meshCollider = targetSplat.AddComponent<MeshCollider>();
            }

            meshCollider.convex = splatType == 0;
            meshCollider.sharedMesh = generatedMesh;

            SplatPhysicsBuildingBlock splatPhysicsComponent = targetSplat.GetComponent<SplatPhysicsBuildingBlock>();
            if (splatPhysicsComponent == null)
            {
                splatPhysicsComponent = targetSplat.AddComponent<SplatPhysicsBuildingBlock>();
            }
            var settings = new SplatPhysicsBuildingBlock.MeshGenerationSettings
            {
                splatType = splatType,
                meshThreshold = GetParameterValue(parameters, "Mesh Threshold", 0.5f),
                gaussianMeshScale = GetParameterValue(parameters, "Gaussian Mesh Scale", 1.5f),
                meshBaseFlattening = GetParameterValue(parameters, "Mesh Base Flattening", false),
                flattenPercentage = GetParameterValue(parameters, "Flatten Percentage", DefaultFlattenPercentage),
                flattenAxis = GetParameterValue(parameters, "Flatten Axis", 4)
            };
            splatPhysicsComponent.SetMeshGenerationInfo(BuildMeshGenerationInfo(sourceSplatPath, meshAssetPath, generatedMesh, splatType, parameters), settings);
            EditorUtility.SetDirty(splatPhysicsComponent);

            Debug.Log($"Added Splat Physics to {targetSplat.name}");
        }

        private string BuildMeshGenerationInfo(string sourceSplatPath, string meshAssetPath, Mesh mesh, int splatType, Dictionary<string, object> parameters)
        {
            var info = new System.Text.StringBuilder();
            info.AppendLine($"Generated: {System.DateTime.Now:yyyy-MM-dd HH:mm}");
            info.AppendLine($"Source splat: {sourceSplatPath}");
            info.AppendLine($"Mesh: {meshAssetPath} ({mesh.vertexCount} vertices, {mesh.triangles.Length / 3} triangles)");

            string[] splatTypes = GetDropdownOptions("Splat Type");
            info.AppendLine($"Splat Type: {(splatType >= 0 && splatType < splatTypes.Length ? splatTypes[splatType] : splatType.ToString())}");

            if (splatType == 0)
            {
                info.AppendLine($"Mesh Threshold: {GetParameterValue(parameters, "Mesh Threshold", 0.5f):0.###}");
                info.AppendLine($"Gaussian Mesh Scale: {GetParameterValue(parameters, "Gaussian Mesh Scale", 1.5f):0.###}");

                if (GetParameterValue(parameters, "Mesh Base Flattening", false))
                {
                    string[] axes = GetDropdownOptions("Flatten Axis");
                    int axis = GetParameterValue(parameters, "Flatten Axis", 4);
                    info.AppendLine($"Mesh Base Flattening: On ({GetParameterValue(parameters, "Flatten Percentage", DefaultFlattenPercentage):0.###}%, axis {(axis >= 0 && axis < axes.Length ? axes[axis] : axis.ToString())})");
                }
                else
                {
                    info.AppendLine("Mesh Base Flattening: Off");
                }
            }

            info.Append($"Collider: {(splatType == 0 ? "Convex" : "Concave")} MeshCollider, kinematic Rigidbody");
            return info.ToString();
        }

        private static bool HasSplatRenderer(GameObject gameObject)
        {
            return gameObject.GetComponent<BaseRendererSplatXR>() != null ||
                   gameObject.GetComponent<LoDRendererSplatXR>() != null;
        }

        string GetUsplatCsvPathFromSplat(GameObject targetSplat)
        {
            BaseRendererSplatXR baseRenderer = targetSplat.GetComponent<BaseRendererSplatXR>();

            if (baseRenderer == null)
            {
                baseRenderer = targetSplat.GetComponentInChildren<BaseRendererSplatXR>();
            }

            if (baseRenderer != null && baseRenderer.splatFilePath != null)
            {
                return AssetDatabase.GetAssetPath(baseRenderer.splatFilePath);
            }

            LoDRendererSplatXR lodRenderer = targetSplat.GetComponent<LoDRendererSplatXR>();

            if (lodRenderer == null)
            {
                lodRenderer = targetSplat.GetComponentInChildren<LoDRendererSplatXR>();
            }

            if (lodRenderer != null)
            {
                TextAsset lodSplatFile = lodRenderer.splatFilePathLow != null ? lodRenderer.splatFilePathLow
                                       : lodRenderer.splatFilePathMedium != null ? lodRenderer.splatFilePathMedium
                                       : lodRenderer.splatFilePathHigh;
                if (lodSplatFile != null)
                {
                    return AssetDatabase.GetAssetPath(lodSplatFile);
                }
            }

            Debug.LogWarning($"Could not find BaseRendererSplatXR or LoDRendererSplatXR component with valid splatFilePath in {targetSplat.name} or its children.");
            return null;
        }

        private bool CreatePhysicsSplatMesh(string splatFilePath, int splatType, Dictionary<string, object> parameters, string meshOutputPath,
                                            string targetName, System.Action<bool> onFinished)
        {
            int meshTargetType = splatType;

            float meshThreshold = 0.5f;
            float meshSplatScale = 1.0f;
            bool meshBaseFlattening = false;
            float meshFlattenPercentage = GetParameterValue(parameters, "Flatten Percentage", DefaultFlattenPercentage);
            int meshFlattenAxis = GetParameterValue(parameters, "Flatten Axis", 4);

            if (splatType == 0)
            {
                meshThreshold = GetParameterValue(parameters, "Mesh Threshold", 0.5f);
                meshSplatScale = GetParameterValue(parameters, "Gaussian Mesh Scale", 1.5f);
                meshBaseFlattening = GetParameterValue(parameters, "Mesh Base Flattening", false);
            }

            try
            {
                return MeshSplat(splatFilePath, meshTargetType,
                    meshThreshold, meshSplatScale,
                    meshOutputPath, meshBaseFlattening,
                    meshFlattenPercentage, meshFlattenAxis,
                    targetName, onFinished);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error calling ImportSplat: {ex.Message}");
                return false;
            }
        }

        private bool MeshSplat(string splatFilePath, int meshTargetType,
                            float meshThreshold, float meshSplatScale,
                            string meshOutputPath, bool meshFlattenBase,
                            float meshFlattenPercentage, int meshFlattenAxis,
                            string targetName, System.Action<bool> onFinished)
        {
            string exeFileName = SplatXRMeshingTool.FileName;

            if (string.IsNullOrEmpty(splatFilePath))
            {
                Debug.LogWarning("No splat file selected.");
                return false;
            }

            string exePath = SplatXRMeshingTool.FindExecutable();
            if (exePath == null)
            {
                Debug.LogError(SplatXRMeshingTool.MissingMessage);
                return false;
            }

            #if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX || UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
            try
            {
                var chmodProcess = new System.Diagnostics.Process();
                chmodProcess.StartInfo.FileName = "chmod";
                chmodProcess.StartInfo.Arguments = $"+x \"{exePath}\"";
                chmodProcess.StartInfo.UseShellExecute = false;
                chmodProcess.StartInfo.RedirectStandardOutput = true;
                chmodProcess.StartInfo.RedirectStandardError = true;
                chmodProcess.StartInfo.CreateNoWindow = true;

                chmodProcess.Start();
                chmodProcess.WaitForExit();

                if (chmodProcess.ExitCode != 0)
                {
                    string error = chmodProcess.StandardError.ReadToEnd();
                    Debug.LogWarning($"Could not automatically set execute permissions. Error: {error}");
                    Debug.LogWarning($"Please manually run in Terminal: chmod +x \"{exePath}\"");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"Could not set execute permissions automatically: {ex.Message}");
                Debug.LogWarning($"If you get an 'Access denied' error, please run this command in Terminal:\nchmod +x \"{exePath}\"");
            }
            #endif

            if (!string.IsNullOrEmpty(meshOutputPath) && !Directory.Exists(meshOutputPath))
            {
                Directory.CreateDirectory(meshOutputPath);
            }

            try
            {
                System.Collections.Generic.List<string> arguments = new System.Collections.Generic.List<string>();

                arguments.Add($"\"{splatFilePath}\"");

                if (!string.IsNullOrEmpty(meshOutputPath))
                {
                    arguments.Add($"--mesh_output_path \"{meshOutputPath}\"");
                }

                arguments.Add($"--mesh_is_object {(meshTargetType == 0 ? "true" : "false")}");

                if (meshTargetType == 0)
                {
                    arguments.Add(System.FormattableString.Invariant($"--mesh_threshold {meshThreshold}"));
                    arguments.Add(System.FormattableString.Invariant($"--mesh_splatscale {meshSplatScale}"));
                }

                if (meshFlattenBase)
                {
                    arguments.Add($"--mesh_flatten_base true");
                    arguments.Add(System.FormattableString.Invariant($"--mesh_flatten_percentage {meshFlattenPercentage}"));
                    arguments.Add($"--mesh_flatten_axis {meshFlattenAxis}");
                }

                return SplatXRMeshingTool.Run(exePath, string.Join(" ", arguments), $"Generating physics mesh for {targetName}", onFinished);
            }
            catch (System.Exception e)
            {
                string errorMsg = $"Error running {exeFileName}: {e.Message}";

                #if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX || UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
                if (e.Message.Contains("Access denied") || e.Message.Contains("Permission denied"))
                {
                    errorMsg += $"\n\nThe executable does not have execute permissions. Please run this command in Terminal:\nchmod +x \"{exePath}\"";
                }
                #endif

                Debug.LogError(errorMsg);
                return false;
            }
        }
    }
}