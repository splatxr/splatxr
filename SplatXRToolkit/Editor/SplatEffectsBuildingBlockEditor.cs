using UnityEngine;
using UnityEditor;

namespace SplatXR
{
    [CustomEditor(typeof(SplatEffectsBuildingBlock))]
    [CanEditMultipleObjects]
    public class SplatEffectsBuildingBlockEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            SerializedProperty property = serializedObject.GetIterator();
            bool enterChildren = true;
            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;

                using (new EditorGUI.DisabledScope(property.propertyPath == "m_Script"))
                {
                    EditorGUILayout.PropertyField(property, true);
                }

                if (property.propertyPath == "isEffectActive" && (property.boolValue || property.hasMultipleDifferentValues))
                {
                    EditorGUILayout.HelpBox(
                        "Some effects fade the splat in over the course of the effect, so the splat may be invisible at time = 0.",
                        MessageType.Warning);
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
