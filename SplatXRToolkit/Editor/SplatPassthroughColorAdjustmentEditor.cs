using UnityEngine;
using UnityEditor;

namespace SplatXR
{
    [CustomEditor(typeof(SplatPassthroughColorAdjustment))]
    [CanEditMultipleObjects]
    public class SplatPassthroughColorAdjustmentEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(5);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply Color Correction"))
            {
                foreach (SplatPassthroughColorAdjustment colorAdjustment in targets)
                {
                    RecordMaterialUndo(colorAdjustment, "Apply Color Correction");
                    colorAdjustment.ApplyColorCorrection();
                    SaveMaterial(colorAdjustment);
                }
            }
            if (GUILayout.Button("Reset Color Correction"))
            {
                foreach (SplatPassthroughColorAdjustment colorAdjustment in targets)
                {
                    RecordMaterialUndo(colorAdjustment, "Reset Color Correction");
                    colorAdjustment.ResetColorCorrection();
                    SaveMaterial(colorAdjustment);
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private static void RecordMaterialUndo(SplatPassthroughColorAdjustment colorAdjustment, string actionName)
        {
            if (colorAdjustment.TargetMaterial != null)
            {
                Undo.RecordObject(colorAdjustment.TargetMaterial, actionName);
            }
        }

        private static void SaveMaterial(SplatPassthroughColorAdjustment colorAdjustment)
        {
            if (colorAdjustment.TargetMaterial != null)
            {
                EditorUtility.SetDirty(colorAdjustment.TargetMaterial);
                AssetDatabase.SaveAssetIfDirty(colorAdjustment.TargetMaterial);
            }
        }
    }
}
