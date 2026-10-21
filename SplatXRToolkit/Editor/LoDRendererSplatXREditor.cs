using UnityEngine;
using UnityEditor;

namespace SplatXR
{
    [CustomEditor(typeof(LoDRendererSplatXR))]
    [CanEditMultipleObjects]
    public class LoDRendererSplatXREditor : Editor
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

            SerializedProperty property = serializedObject.GetIterator();
            bool enterChildren = true;
            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (System.Array.IndexOf(cropPropertyNames, property.name) >= 0) continue;

                using (new EditorGUI.DisabledScope(property.propertyPath == "m_Script"))
                {
                    EditorGUILayout.PropertyField(property, true);
                }

                if (property.propertyPath == "enableLODDebugColoring")
                {
                    EditorGUILayout.HelpBox("Use the debug coloring to help adjust the LOD cutoff distances for the medium and low splats: normal color is high, blue is medium, red is low (far).", MessageType.Info);
                }
            }

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
