using UnityEngine;
using UnityEditor;

namespace SplatXR
{

    public class Vector3Drawer : MaterialPropertyDrawer
    {
        public override void OnGUI(Rect position, MaterialProperty prop, GUIContent label, MaterialEditor editor)
        {
            // MaterialProperty.propertyType only exists from Unity 6.3; older versions (down to 2022.3) only have type.
    #if UNITY_6000_3_OR_NEWER
            if (prop.propertyType != UnityEngine.Rendering.ShaderPropertyType.Vector)
    #else
            if (prop.type != MaterialProperty.PropType.Vector)
    #endif
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
