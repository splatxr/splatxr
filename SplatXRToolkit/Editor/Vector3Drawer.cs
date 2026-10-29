using UnityEngine;
using UnityEditor;

namespace SplatXR
{

    public class Vector3Drawer : MaterialPropertyDrawer
    {
        public override void OnGUI(Rect position, MaterialProperty prop, GUIContent label, MaterialEditor editor)
        {
            if (prop.propertyType != UnityEngine.Rendering.ShaderPropertyType.Vector)
            {
                editor.DefaultShaderProperty(position, prop, label.text);
                return;
            }

            EditorGUI.showMixedValue = prop.hasMixedValue;
            float oldLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 0f;

            EditorGUI.BeginChangeCheck();
            Vector3 value = EditorGUI.Vector3Field(position, label, prop.vectorValue);
            if (EditorGUI.EndChangeCheck())
            {
                prop.vectorValue = new Vector4(value.x, value.y, value.z, 0f);
            }

            EditorGUIUtility.labelWidth = oldLabelWidth;
            EditorGUI.showMixedValue = false;
        }

        public override float GetPropertyHeight(MaterialProperty prop, string label, MaterialEditor editor)
        {
            return EditorGUIUtility.singleLineHeight;
        }
    }
}
