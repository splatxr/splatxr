using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace SplatXR
{
    public interface ISplatXRBuildingBlock
    {
        string Name { get; }
    
        string Description { get; }

        string DocumentationUrl { get; }
        Dictionary<string, object> InitializeParameters();
        string[] GetDropdownOptions(string parameterName);
    
        bool IsParameterVisible(string parameterName, Dictionary<string, object> parameters);
    
        void AddToScene(GameObject targetObject, Dictionary<string, object> parameters);
        Texture2D LoadIcon();
    }

    public abstract class SplatXRBuildingBlockBase : ISplatXRBuildingBlock
    {
        public abstract string Name { get; }
        public abstract string Description { get; }
        public virtual string DocumentationUrl => null;
    
        public abstract Dictionary<string, object> InitializeParameters();
        public abstract void AddToScene(GameObject targetObject, Dictionary<string, object> parameters);
 
        public virtual string[] GetDropdownOptions(string parameterName)
        {
            return null;
        }
        public virtual bool IsParameterVisible(string parameterName, Dictionary<string, object> parameters)
        {
            return true;
        }

        public virtual Texture2D LoadIcon()
        {
            string iconName = Name.Replace(" ", "").Replace("-", "");
        
            Texture2D loadedIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(
                SplatXRPackagePaths.GetAssetPath($"Editor/BuildingBlocks/PanelInterface/Icons/{iconName}_Icon.png"));

            if (loadedIcon != null)
            {
                return loadedIcon;
            }
            else
            {
                return CreateDefaultIcon();
            }
        }
    
        protected virtual Texture2D CreateDefaultIcon()
        {
            Texture2D icon = new Texture2D(48, 48);
            Color[] pixels = new Color[48 * 48];
        
            Color greyColor = new Color(0.6f, 0.6f, 0.6f, 1f);
        
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = greyColor;
            }
        
            icon.SetPixels(pixels);
            icon.Apply();
            icon.filterMode = FilterMode.Point;
        
            return icon;
        }
    
        protected bool ValidateStringParameter(Dictionary<string, object> parameters, string parameterName, out string value)
        {
            value = "";
            if (parameters.ContainsKey(parameterName) && parameters[parameterName] is string stringValue)
            {
                value = stringValue;
                return !string.IsNullOrEmpty(stringValue);
            }
            return false;
        }

        protected T GetParameterValue<T>(Dictionary<string, object> parameters, string parameterName, T defaultValue)
        {
            if (parameters.ContainsKey(parameterName) && parameters[parameterName] is T value)
            {
                return value;
            }
            return defaultValue;
        }

        protected string GetUniqueObjectName(string baseName, System.Func<string, string> materialPathForName)
        {
            var existingNames = new HashSet<string>();
            foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go.scene.IsValid())
                {
                    existingNames.Add(go.name);
                }
            }

            string candidate = baseName;
            int suffix = 2;
            while (existingNames.Contains(candidate) ||
                   AssetDatabase.LoadAssetAtPath<Material>(materialPathForName(candidate)) != null)
            {
                candidate = $"{baseName}{suffix}";
                suffix++;
            }
            return candidate;
        }
    }
}