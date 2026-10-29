using UnityEngine;
using UnityEditor;
using System.Reflection;

namespace SplatXR
{
    [InitializeOnLoad]
    public static class BaseRendererSplatXROutlineDisabler
    {
        private static bool? savedOutlineState = null;
        private static bool wasShowingOutlineForSplatXR = false;

        static BaseRendererSplatXROutlineDisabler()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            Selection.selectionChanged += OnSelectionChanged;
        }

        private static void OnSelectionChanged()
        {
            bool isSplatXRSelected = false;
        
            foreach (var obj in Selection.gameObjects)
            {
                if (obj.GetComponent<BaseRendererSplatXR>() != null)
                {
                    isSplatXRSelected = true;
                    break;
                }
            }

            if (isSplatXRSelected && !wasShowingOutlineForSplatXR)
            {
                savedOutlineState = GetSelectionOutlineState();
                SetSelectionOutlineState(false);
                wasShowingOutlineForSplatXR = true;
            }
            else if (!isSplatXRSelected && wasShowingOutlineForSplatXR)
            {
                if (savedOutlineState.HasValue)
                {
                    SetSelectionOutlineState(savedOutlineState.Value);
                    savedOutlineState = null;
                }
                wasShowingOutlineForSplatXR = false;
            }
        }

        private static void OnSceneGUI(SceneView sceneView)
        {
            foreach (var obj in Selection.gameObjects)
            {
                BaseRendererSplatXR renderer = obj.GetComponent<BaseRendererSplatXR>();
                if (renderer != null)
                {
                    MeshRenderer meshRenderer = renderer.GetComponent<MeshRenderer>();
                    if (meshRenderer != null)
                    {
                        EditorUtility.SetSelectedRenderState(meshRenderer, EditorSelectedRenderState.Hidden);
                    }
                }
            }
        }

        private static bool GetSelectionOutlineState()
        {
            try
            {
                var annotationUtility = System.Type.GetType("UnityEditor.AnnotationUtility,UnityEditor");
                if (annotationUtility != null)
                {
                    var showOutlineProperty = annotationUtility.GetProperty("showSelectionOutline", 
                        BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                    if (showOutlineProperty != null)
                    {
                        return (bool)showOutlineProperty.GetValue(null);
                    }
                }
            }
            catch { }
            return true;
        }

        private static void SetSelectionOutlineState(bool enabled)
        {
            try
            {
                var annotationUtility = System.Type.GetType("UnityEditor.AnnotationUtility,UnityEditor");
                if (annotationUtility != null)
                {
                    var showOutlineProperty = annotationUtility.GetProperty("showSelectionOutline", 
                        BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                    if (showOutlineProperty != null)
                    {
                        showOutlineProperty.SetValue(null, enabled);
                    }
                }
            }
            catch { }
        }
    }

    [CustomEditor(typeof(BaseRendererSplatXR))]
    [CanEditMultipleObjects]
    public class BaseRendererSplatXREditor : Editor
    {
        private static readonly string[] cropPropertyNames =
        {
            "enableCropping",
            "cropBoundsMin",
            "cropBoundsMax",
            "cropBoundsRotation",
            "showCropBounds"
        };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            if (!serializedObject.isEditingMultipleObjects)
            {
                int splatCount = ((BaseRendererSplatXR)target).LoadedSplatCount;
                if (splatCount > SplatCsvLoader.RecommendedMaxSplats)
                {
                    EditorGUILayout.HelpBox(
                        $"This splat has {splatCount:N0} splats, more than the recommended maximum of {SplatCsvLoader.RecommendedMaxSplats:N0}. " +
                        "Rendering may be slow, especially on XR headsets. Consider cropping, decimating the splat, or using the LoD Renderer.",
                        MessageType.Warning);
                }
            }

            DrawPropertiesExcluding(serializedObject, cropPropertyNames);

            SerializedProperty enableCropping = serializedObject.FindProperty("enableCropping");
            EditorGUILayout.Space(5);
            enableCropping.isExpanded = EditorGUILayout.BeginFoldoutHeaderGroup(enableCropping.isExpanded, "Cropping Controls");
            if (enableCropping.isExpanded)
            {
                EditorGUI.indentLevel++;
                foreach (string propertyName in cropPropertyNames)
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty(propertyName));
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            serializedObject.ApplyModifiedProperties();
        }
    }
}
