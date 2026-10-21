using UnityEngine;
using UnityEditor;

namespace SplatXR
{
    [CustomEditor(typeof(SplatRendererBuildingBlock))]
    [CanEditMultipleObjects]
    public class SplatRendererBuildingBlockEditor : Editor
    {
        private void OnEnable()
        {
            foreach (Object t in targets)
            {
                var block = (SplatRendererBuildingBlock)t;
                Material splatMaterial = block.GetSplatMaterial();
                if (splatMaterial != null && block.renderQueue != splatMaterial.renderQueue)
                {
                    var blockObject = new SerializedObject(block);
                    blockObject.FindProperty("renderQueue").intValue = splatMaterial.renderQueue;
                    blockObject.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            SerializedProperty property = serializedObject.GetIterator();
            bool enterChildren = true;
            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;

                if (property.propertyPath == "renderQueue")
                {
                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.PropertyField(property);
                    if (EditorGUI.EndChangeCheck())
                    {
                        property.intValue = Mathf.Clamp(property.intValue, 0, 5000);
                        serializedObject.ApplyModifiedProperties();
                        ApplyRenderQueue();
                    }

                    EditorGUILayout.HelpBox(
                        "Sets the draw order of this splat's material. Splats within one object are sorted automatically, but separate splat objects (and other transparent objects) are drawn in Render Queue order: higher values are drawn later, on top of lower ones. Default is 3001. If one splat incorrectly shows through or covers another, give them different values.",
                        MessageType.Info);
                    continue;
                }

                using (new EditorGUI.DisabledScope(property.propertyPath == "m_Script"))
                {
                    EditorGUILayout.PropertyField(property, true);
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void ApplyRenderQueue()
        {
            foreach (Object t in targets)
            {
                var block = (SplatRendererBuildingBlock)t;
                Material splatMaterial = block.GetSplatMaterial();
                if (splatMaterial == null)
                {
                    continue;
                }

                Undo.RecordObject(splatMaterial, "Change Splat Render Queue");
                block.ApplyRenderQueue();
                EditorUtility.SetDirty(splatMaterial);
            }
        }
    }
}
