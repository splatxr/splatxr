using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace SplatXR
{
    public class SplatXRBlockPanel : EditorWindow
    {
        private int selectedBlockIndex = 0;
        private Vector2 leftScrollPosition;
        private Vector2 rightScrollPosition;
        private List<SplatXRBuildingBlockData.BuildingBlock> buildingBlocks;

        private bool customShaderExpanded = false;
        private string customShaderName = "";
        private string customShaderMessage;
        private MessageType customShaderMessageType;
        private string effectAddedNotice;

        private Texture2D buttonNormalTexture;
        private Texture2D buttonHoverTexture;

        private bool wasMeshing;

        private const float LEFT_PANEL_WIDTH = 280f;
        private const float PANEL_PADDING = 15f;
        private const float BUTTON_HEIGHT = 35f;
        private const float SECTION_SPACING = 20f;
        private const float PARAMETER_LABEL_WIDTH = 140f;
        private const float LEFT_BUTTON_HEIGHT = 65f;
        private const float ICON_SIZE = 48f;

        [MenuItem("SplatXR/Building Blocks Panel")]
        public static void ShowWindow()
        {
            SplatXRBlockPanel window = GetWindow<SplatXRBlockPanel>("SplatXR Toolkit");
            window.minSize = new Vector2(600, 400);
            window.Show();
        }

        private void OnEnable()
        {
            buildingBlocks = SplatXRBuildingBlockData.InitializeBuildingBlocks();
        
            buttonNormalTexture = MakeRoundedTexture(32, 32, new Color(0.3f, 0.3f, 0.3f, 1f), 5);
            buttonHoverTexture = MakeRoundedTexture(32, 32, new Color(0.35f, 0.35f, 0.35f, 1f), 5);
        }

        private void OnInspectorUpdate()
        {
            if (SplatXRMeshingTool.IsRunning != wasMeshing)
            {
                wasMeshing = SplatXRMeshingTool.IsRunning;
                Repaint();
            }
        }

        private void OnGUI()
        {
            if (Event.current.type == EventType.MouseMove)
            {
                Repaint();
            }
        
            try
            {
                DrawLeftPanel();
                DrawRightPanel();
            }
            catch (System.Exception ex)
            {
                GUILayout.Label($"Error: {ex.Message}", EditorStyles.helpBox);
            }
        }

        private void DrawLeftPanel()
        {
            Rect leftRect = new Rect(0, 0, LEFT_PANEL_WIDTH, position.height);
            EditorGUI.DrawRect(leftRect, new Color(0.22f, 0.22f, 0.22f, 1f));
        
            EditorGUI.DrawRect(new Rect(LEFT_PANEL_WIDTH - 1, 0, 1, position.height), new Color(0.1f, 0.1f, 0.1f, 1f));

            GUILayout.BeginArea(new Rect(PANEL_PADDING, PANEL_PADDING, LEFT_PANEL_WIDTH - PANEL_PADDING * 2, position.height - PANEL_PADDING * 2));
        
            GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel);
            titleStyle.fontSize = 14;
            titleStyle.normal.textColor = Color.white;
            GUILayout.Label("Building Blocks", titleStyle);
        
            GUILayout.Space(15);

            if (buildingBlocks != null && buildingBlocks.Count > 0)
            {
                leftScrollPosition = GUILayout.BeginScrollView(leftScrollPosition, GUIStyle.none);

                for (int i = 0; i < buildingBlocks.Count; i++)
                {
                    bool isSelected = i == selectedBlockIndex;
                    var block = buildingBlocks[i];

                    GUIStyle buttonStyle = new GUIStyle();
                    buttonStyle.alignment = TextAnchor.MiddleLeft;
                    buttonStyle.padding = new RectOffset(70, 15, 12, 12);
                    buttonStyle.wordWrap = true;
                    buttonStyle.fontSize = 12;
                    buttonStyle.font = GUI.skin.button.font;
                    buttonStyle.border = new RectOffset(8, 8, 8, 8);
                
                    buttonStyle.normal.background = buttonNormalTexture;
                    buttonStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f, 1f);
                    buttonStyle.hover.background = buttonHoverTexture;
                    buttonStyle.hover.textColor = Color.white;
                    buttonStyle.active.background = buttonHoverTexture;
                    buttonStyle.active.textColor = Color.white;
                    buttonStyle.focused.background = buttonNormalTexture;
                    buttonStyle.focused.textColor = new Color(0.85f, 0.85f, 0.85f, 1f);
                    buttonStyle.onNormal.background = buttonNormalTexture;
                    buttonStyle.onNormal.textColor = new Color(0.85f, 0.85f, 0.85f, 1f);
                    buttonStyle.onHover.background = buttonHoverTexture;
                    buttonStyle.onHover.textColor = Color.white;
                    buttonStyle.onActive.background = buttonHoverTexture;
                    buttonStyle.onActive.textColor = Color.white;
                    buttonStyle.onFocused.background = buttonNormalTexture;
                    buttonStyle.onFocused.textColor = new Color(0.85f, 0.85f, 0.85f, 1f);
                
                    Rect buttonRect = GUILayoutUtility.GetRect(new GUIContent(block.name), buttonStyle, GUILayout.Height(LEFT_BUTTON_HEIGHT));
                
                    if (GUI.Button(buttonRect, block.name, buttonStyle))
                    {
                        selectedBlockIndex = i;
                        effectAddedNotice = null;
                    }
                
                    Rect outlineRect = new Rect(buttonRect.x - 0.5f, buttonRect.y - 0.5f, buttonRect.width + 1f, buttonRect.height + 1f);
                    DrawRoundedOutline(outlineRect, new Color(0.15f, 0.15f, 0.15f, 1f), 5.5f, 1.5f);
                
                    if (isSelected)
                    {
                        Rect accentRect = new Rect(buttonRect.x + 2, buttonRect.y + 4, 2, buttonRect.height - 8);
                        EditorGUI.DrawRect(accentRect, new Color(0.6f, 0.8f, 1f, 0.9f));
                    }
                
                    if (block.icon != null)
                    {
                        Rect iconRect = new Rect(buttonRect.x + 10, buttonRect.y + (buttonRect.height - ICON_SIZE) / 2, ICON_SIZE, ICON_SIZE);
                        GUI.DrawTexture(iconRect, block.icon);
                    }
                    else
                    {
                        Rect iconRect = new Rect(buttonRect.x + 10, buttonRect.y + (buttonRect.height - ICON_SIZE) / 2, ICON_SIZE, ICON_SIZE);
                        EditorGUI.DrawRect(iconRect, new Color(0.5f, 0.5f, 0.5f, 0.8f));
                    
                        Color shapeColor = isSelected ? Color.white : new Color(0.8f, 0.8f, 0.8f, 1f);
                        Rect shapeRect = new Rect(iconRect.x + 12, iconRect.y + 12, 24, 24);
                        EditorGUI.DrawRect(shapeRect, shapeColor);
                    }
                
                    GUILayout.Space(10);
                }

                GUILayout.EndScrollView();
            }
            else
            {
                GUILayout.Label("No building blocks available", EditorStyles.centeredGreyMiniLabel);
            }
        
            GUILayout.EndArea();
        }

        private void DrawRightPanel()
        {
            GUILayout.BeginArea(new Rect(LEFT_PANEL_WIDTH + PANEL_PADDING, PANEL_PADDING, 
                                       position.width - LEFT_PANEL_WIDTH - PANEL_PADDING * 2, position.height - PANEL_PADDING * 2));

            if (selectedBlockIndex >= 0 && selectedBlockIndex < buildingBlocks.Count)
            {
                var selectedBlock = buildingBlocks[selectedBlockIndex];
            
                rightScrollPosition = GUILayout.BeginScrollView(rightScrollPosition, false, true);
            
                GUIStyle titleStyle = new GUIStyle(EditorStyles.largeLabel);
                titleStyle.fontSize = 18;
                titleStyle.fontStyle = FontStyle.Bold;
                GUILayout.Label(selectedBlock.name, titleStyle);
                GUILayout.Space(SECTION_SPACING);

                GUILayout.BeginVertical("box");
                GUILayout.Label("Description", EditorStyles.boldLabel);
                GUILayout.Space(8);
            
                GUIStyle descriptionStyle = new GUIStyle(EditorStyles.wordWrappedLabel);
                descriptionStyle.richText = true;
                descriptionStyle.padding = new RectOffset(8, 8, 8, 8);
            
                GUILayout.Label(selectedBlock.description, descriptionStyle);

                if (!string.IsNullOrEmpty(selectedBlock.documentationUrl))
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(8);
                    if (EditorGUILayout.LinkButton("Documentation"))
                    {
                        Application.OpenURL(selectedBlock.documentationUrl);
                    }
                    GUILayout.EndHorizontal();
                    GUILayout.Space(4);
                }
                GUILayout.EndVertical();
            
                GUILayout.Space(SECTION_SPACING);

                if (selectedBlock.name == "Splat Physics")
                {
                    SplatXRMeshingTool.DrawNotice();
                    GUILayout.Space(SECTION_SPACING);
                }

                GUILayout.BeginVertical("box");
                GUILayout.Label("Parameters", EditorStyles.boldLabel);
                GUILayout.Space(8);
                DrawBlockParameters(selectedBlock);
                GUILayout.EndVertical();

                GUILayout.Space(SECTION_SPACING);

                if (selectedBlock.name == "Splat Effects")
                {
                    DrawCustomShaderSection(selectedBlock);
                    GUILayout.Space(SECTION_SPACING);

                    if (!string.IsNullOrEmpty(effectAddedNotice))
                    {
                        EditorGUILayout.HelpBox(effectAddedNotice, MessageType.Info);
                        GUILayout.Space(8);
                    }
                }

                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
            
                GUIStyle buttonStyle = new GUIStyle(GUI.skin.button);
                buttonStyle.fontSize = 12;
                buttonStyle.fontStyle = FontStyle.Bold;
                buttonStyle.fixedHeight = BUTTON_HEIGHT;
                buttonStyle.fixedWidth = 150;
            
                Color originalColor = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.2f, 0.8f, 0.2f, 1f);
            
                string buttonText = (selectedBlock.name == "Splat Physics" || selectedBlock.name == "Splat Effects") ? "Add to Splat" : "Add to Scene";
            
                if (GUILayout.Button(buttonText, buttonStyle))
                {
                    var blockToAdd = selectedBlock;
                    EditorApplication.delayCall += () => AddBlockToScene(blockToAdd);
                }
            
                GUI.backgroundColor = originalColor;
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                if (selectedBlock.name == "Splat Physics" && SplatXRMeshingTool.IsRunning)
                {
                    GUILayout.Space(8);
                    EditorGUILayout.HelpBox(SplatXRMeshingTool.RunningNotice, MessageType.Info);
                }

                GUILayout.Space(SECTION_SPACING);

                GUILayout.EndScrollView();
            }
            else
            {
                GUILayout.BeginVertical();
                GUILayout.FlexibleSpace();
            
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
            
                GUIStyle noSelectionStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel);
                noSelectionStyle.fontSize = 14;
                GUILayout.Label("Select a building block from the left panel", noSelectionStyle);
            
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            
                GUILayout.FlexibleSpace();
                GUILayout.EndVertical();
            }

            GUILayout.EndArea();
        }

        private void DrawBlockParameters(SplatXRBuildingBlockData.BuildingBlock block)
        {
            var keys = new List<string>(block.parameters.Keys);

            if (block.name == "Splat Blending")
            {
                EditorGUILayout.HelpBox("All objects that should be visible in the blending portal window must be assigned to the \"BlendingWorld\" layer.", MessageType.Info);
                GUILayout.Space(8);
            }

        
            foreach (var key in keys)
            {
                object value = block.parameters[key];

                if (block.blockImplementation != null && !block.blockImplementation.IsParameterVisible(key, block.parameters))
                {
                    continue;
                }

                GUILayout.BeginHorizontal();
            
                GUIStyle labelStyle = new GUIStyle(EditorStyles.label);
                labelStyle.fontStyle = FontStyle.Normal;
                GUIContent labelContent = new GUIContent(key);
                if (key == "Target Splat")
                {
                    labelContent = new GUIContent("Splat (Parent Object)", "The parent GameObject of the splat_renderer object");
                    labelStyle.wordWrap = true;
                }
                GUILayout.Label(labelContent, labelStyle, GUILayout.Width(PARAMETER_LABEL_WIDTH));

                if (value is float floatValue)
                {
                    if (key == "Flatten Percentage")
                    {
                        block.parameters[key] = SplatPhysicsBlock.FlattenPercentageField(floatValue, GUILayout.MinWidth(100));
                    }
                    else if (key.Contains("Quality") || key.Contains("Intensity") || key.Contains("Opacity") || key.Contains("Accuracy") ||
                        key == "Mesh Threshold")
                    {
                        float newValue = EditorGUILayout.Slider(floatValue, 0f, 1f);
                        block.parameters[key] = newValue;
                    }
                    else if (key == "Gaussian Mesh Scale")
                    {
                        float newValue = EditorGUILayout.Slider(floatValue, 0f, 2f);
                        block.parameters[key] = newValue;
                    }
                    else
                    {
                        block.parameters[key] = EditorGUILayout.FloatField(floatValue, GUILayout.MinWidth(100));
                    }
                }
                else if (value is int intValue)
                {
                    string[] options = block.blockImplementation?.GetDropdownOptions(key);
                    if (options != null && options.Length > 0)
                    {
                        int newValue = EditorGUILayout.Popup(intValue, options, GUILayout.MinWidth(120));
                        block.parameters[key] = newValue;
                    
                        if (key == "Splat Type" && newValue != intValue)
                        {
                            Repaint();
                        }

                        if (key == "XR Platform" && newValue >= 0 && newValue < options.Length && options[newValue] == "Other")
                        {
                            GUILayout.EndHorizontal();
                            GUILayout.Space(8);
                            GUILayout.BeginHorizontal();
                            GUILayout.Space(PARAMETER_LABEL_WIDTH + 5);
                            EditorGUILayout.HelpBox("Platforms other than Meta SDK are untested. Assign your XR rig's main camera (e.g. XR Origin > Main Camera) as the Center Eye Camera. On Apple Vision Pro, only fully immersive (Metal) apps are expected to work.", MessageType.Warning);
                            GUILayout.EndHorizontal();
                            GUILayout.Space(8);
                            continue;
                        }
                    }
                    else
                    {
                        block.parameters[key] = EditorGUILayout.IntField(intValue, GUILayout.MinWidth(100));
                    }
                }
                else if (value is bool boolValue)
                {
                    bool newValue = EditorGUILayout.Toggle(boolValue, GUILayout.Width(20));
                    block.parameters[key] = newValue;
                
                    GUILayout.EndHorizontal();

                    GUILayout.Space(8);
                    continue;
                }
                else if (value is Color colorValue)
                {
                    block.parameters[key] = EditorGUILayout.ColorField(colorValue, GUILayout.Width(60));
                }
                else if (value is GameObject || value == null && (key == "Target Splat" || key == "Center Eye Camera"))
                {
                    GameObject gameObjectValue = value as GameObject;
                    GameObject newGameObject = (GameObject)EditorGUILayout.ObjectField(gameObjectValue, typeof(GameObject), true, GUILayout.MinWidth(150));
                    block.parameters[key] = newGameObject;

                    if (key == "Center Eye Camera")
                    {
                        GUILayout.EndHorizontal();
                        GUILayout.Space(8);
                        GUILayout.BeginHorizontal();
                        GUILayout.Space(PARAMETER_LABEL_WIDTH + 5);
                        EditorGUILayout.HelpBox("For Meta SDK, this is the CenterEyeAnchor (Camera Rig > TrackingSpace > CenterEyeAnchor)", MessageType.Info);
                        GUILayout.EndHorizontal();
                        GUILayout.Space(8);
                        continue;
                    }
                }
                else if (value is Shader || value == null && key == "Custom Shader")
                {
                    Shader shaderValue = value as Shader;
                    block.parameters[key] = (Shader)EditorGUILayout.ObjectField(shaderValue, typeof(Shader), false, GUILayout.MinWidth(150));
                }
                else if (value is Vector3 vectorValue)
                {
                    block.parameters[key] = EditorGUILayout.Vector3Field(GUIContent.none, vectorValue, GUILayout.MinWidth(150));

                    if (key == "PLY Up Direction")
                    {
                        GUILayout.EndHorizontal();
                        GUILayout.Space(8);
                        GUILayout.BeginHorizontal();
                        GUILayout.Space(PARAMETER_LABEL_WIDTH + 5);
                        EditorGUILayout.HelpBox("The axis that points up in the PLY file. Most 3DGS/COLMAP splats are Y-down (0, -1, 0). Use (0, 1, 0) for Y-up, (0, 0, 1) for Z-up or (1, 0, 0) for X-up.", MessageType.Info);
                        GUILayout.EndHorizontal();
                        GUILayout.Space(8);
                        continue;
                    }
                }
                else if (value is string stringValue)
                {
                    if ((key.Contains("File") && key.Contains("PLY")) || 
                        key.Contains("Detail Splat"))
                    {
                        string newPath = EditorGUILayout.TextField(stringValue, GUILayout.MinWidth(150));
                        if (GUILayout.Button("Browse", GUILayout.Width(60)))
                        {
                            string selectedPath = EditorUtility.OpenFilePanel("Select PLY File", "", "ply");
                            if (!string.IsNullOrEmpty(selectedPath))
                            {
                                if (selectedPath.StartsWith(Application.dataPath))
                                {
                                    newPath = "Assets" + selectedPath.Substring(Application.dataPath.Length);
                                }
                                else
                                {
                                    newPath = selectedPath;
                                }
                            }
                        }
                        block.parameters[key] = newPath;
                    }
                    else
                    {
                        block.parameters[key] = EditorGUILayout.TextField(stringValue, GUILayout.MinWidth(120));
                    }
                }

                GUILayout.EndHorizontal();
                GUILayout.Space(8);
            }
        }

        private void DrawCustomShaderSection(SplatXRBuildingBlockData.BuildingBlock block)
        {
            GUILayout.BeginVertical("box");

            GUIStyle foldoutStyle = new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold };
            customShaderExpanded = EditorGUILayout.Foldout(customShaderExpanded, "Create Custom Shader Effect", true, foldoutStyle);
            if (!customShaderExpanded)
            {
                GUILayout.EndVertical();
                return;
            }
            GUILayout.Space(8);

            GUILayout.Label("Creates a template effect shader that you can edit to add your own effect.", EditorStyles.wordWrappedLabel);
            GUILayout.Space(8);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Shader Name", GUILayout.Width(PARAMETER_LABEL_WIDTH));
            customShaderName = EditorGUILayout.TextField(customShaderName, GUILayout.MinWidth(120));
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Create Custom Shader Effect", GUILayout.Width(200), GUILayout.Height(28)))
            {
                string nameToCreate = customShaderName;
                EditorApplication.delayCall += () => CreateCustomShader(block, nameToCreate);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(customShaderMessage))
            {
                GUILayout.Space(8);
                EditorGUILayout.HelpBox(customShaderMessage, customShaderMessageType);
            }

            GUILayout.EndVertical();
        }

        private void CreateCustomShader(SplatXRBuildingBlockData.BuildingBlock block, string shaderName)
        {
            string assetPath = SplatEffectsBlock.CreateCustomShaderTemplate(shaderName, out string error);
            if (assetPath == null)
            {
                customShaderMessage = error;
                customShaderMessageType = MessageType.Error;
            }
            else
            {
                customShaderMessage = $"Created custom shader effect at:\n{assetPath}\nIt has been selected as the Custom Shader above.";
                customShaderMessageType = MessageType.Info;

                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(assetPath);
                if (shader != null)
                {
                    block.parameters["Effect Selection"] = 3;
                    block.parameters["Custom Shader"] = shader;
                    EditorGUIUtility.PingObject(shader);
                }
            }
            Repaint();
        }

        private void AddBlockToScene(SplatXRBuildingBlockData.BuildingBlock block)
        {
            SplatXRBuildingBlockData.AddBlockToScene(block);

            if (block.name == "Splat Effects")
            {
                var targetSplat = block.parameters["Target Splat"] as GameObject;
                effectAddedNotice = targetSplat != null && targetSplat.GetComponent<SplatEffectsBuildingBlock>() != null
                    ? $"Effect added to {targetSplat.name}. The effect is disabled by default: enable the \"Is Effect Active\" checkbox in the Splat Effects Building Block component on {targetSplat.name} to play it."
                    : null;
                Repaint();
            }
        }

        private Texture2D MakeTexture(int width, int height, Color color)
        {
            Color[] pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = color;

            Texture2D texture = new Texture2D(width, height);
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private Texture2D MakeRoundedTexture(int width, int height, Color color, int cornerRadius)
        {
            Texture2D texture = new Texture2D(width, height);
            Color[] pixels = new Color[width * height];
        
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool inCorner = false;
                    int dx = 0, dy = 0;
                
                    if (x < cornerRadius && y < cornerRadius)
                    {
                        dx = cornerRadius - x;
                        dy = cornerRadius - y;
                        inCorner = true;
                    }
                    else if (x >= width - cornerRadius && y < cornerRadius)
                    {
                        dx = x - (width - cornerRadius - 1);
                        dy = cornerRadius - y;
                        inCorner = true;
                    }
                    else if (x < cornerRadius && y >= height - cornerRadius)
                    {
                        dx = cornerRadius - x;
                        dy = y - (height - cornerRadius - 1);
                        inCorner = true;
                    }
                    else if (x >= width - cornerRadius && y >= height - cornerRadius)
                    {
                        dx = x - (width - cornerRadius - 1);
                        dy = y - (height - cornerRadius - 1);
                        inCorner = true;
                    }
                
                    if (inCorner)
                    {
                        float distance = Mathf.Sqrt(dx * dx + dy * dy);
                        if (distance > cornerRadius)
                        {
                            pixels[y * width + x] = new Color(color.r, color.g, color.b, 0);
                        }
                        else
                        {
                            pixels[y * width + x] = color;
                        }
                    }
                    else
                    {
                        pixels[y * width + x] = color;
                    }
                }
            }
        
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private void DrawRoundedOutline(Rect rect, Color color, float cornerRadius, float thickness)
        {
            EditorGUI.DrawRect(new Rect(rect.x + cornerRadius, rect.y, rect.width - cornerRadius * 2, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x + cornerRadius, rect.y + rect.height - thickness, rect.width - cornerRadius * 2, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y + cornerRadius, thickness, rect.height - cornerRadius * 2), color);
            EditorGUI.DrawRect(new Rect(rect.x + rect.width - thickness, rect.y + cornerRadius, thickness, rect.height - cornerRadius * 2), color);
        
            int segments = 8;
            for (int i = 0; i <= segments; i++)
            {
                float angle = (i / (float)segments) * 90f * Mathf.Deg2Rad;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);
            
                float tlX = rect.x + cornerRadius - cos * cornerRadius;
                float tlY = rect.y + cornerRadius - sin * cornerRadius;
                EditorGUI.DrawRect(new Rect(tlX, tlY, thickness, thickness), color);
            
                float trX = rect.x + rect.width - cornerRadius + sin * cornerRadius;
                float trY = rect.y + cornerRadius - cos * cornerRadius;
                EditorGUI.DrawRect(new Rect(trX - thickness, trY, thickness, thickness), color);
            
                float blX = rect.x + cornerRadius - sin * cornerRadius;
                float blY = rect.y + rect.height - cornerRadius + cos * cornerRadius;
                EditorGUI.DrawRect(new Rect(blX, blY - thickness, thickness, thickness), color);
            
                float brX = rect.x + rect.width - cornerRadius + cos * cornerRadius;
                float brY = rect.y + rect.height - cornerRadius + sin * cornerRadius;
                EditorGUI.DrawRect(new Rect(brX - thickness, brY - thickness, thickness, thickness), color);
            }
        }
    }
}
