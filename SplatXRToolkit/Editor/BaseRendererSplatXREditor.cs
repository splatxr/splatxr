using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.Reflection;

namespace SplatXR
{
    [InitializeOnLoad]
    public static class BaseRendererSplatXROutlineDisabler
    {
        // Kept in EditorPrefs rather than a static field so the user's outline setting is still restored after a
        // domain reload or editor restart that happens while a splat is selected.
        private const string SavedOutlineStateKey = "SplatXR.SavedSelectionOutline";

        private static MeshRenderer[] selectedSplatRenderers = new MeshRenderer[0];

        static BaseRendererSplatXROutlineDisabler()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            Selection.selectionChanged += OnSelectionChanged;
            EditorApplication.delayCall += OnSelectionChanged;
        }

        private static void OnSelectionChanged()
        {
            // Selecting the Splat Renderer parent outlines its splat_renderer child too, so look at children.
            var renderers = new List<MeshRenderer>();
            foreach (var obj in Selection.gameObjects)
            {
                foreach (var splat in obj.GetComponentsInChildren<BaseRendererSplatXR>(true))
                {
                    renderers.Add(splat.GetComponent<MeshRenderer>());
                }
                foreach (var splat in obj.GetComponentsInChildren<LoDRendererSplatXR>(true))
                {
                    renderers.Add(splat.GetComponent<MeshRenderer>());
                }
            }
            selectedSplatRenderers = renderers.ToArray();

            bool isSplatXRSelected = selectedSplatRenderers.Length > 0;
            bool outlineDisabledForSplatXR = EditorPrefs.HasKey(SavedOutlineStateKey);

            if (isSplatXRSelected && !outlineDisabledForSplatXR)
            {
                EditorPrefs.SetBool(SavedOutlineStateKey, GetSelectionOutlineState());
                SetSelectionOutlineState(false);
            }
            else if (!isSplatXRSelected && outlineDisabledForSplatXR)
            {
                SetSelectionOutlineState(EditorPrefs.GetBool(SavedOutlineStateKey, true));
                EditorPrefs.DeleteKey(SavedOutlineStateKey);
            }
        }

        private static void OnSceneGUI(SceneView sceneView)
        {
            foreach (MeshRenderer meshRenderer in selectedSplatRenderers)
            {
                if (meshRenderer != null)
                {
                    EditorUtility.SetSelectedRenderState(meshRenderer, EditorSelectedRenderState.Hidden);
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
