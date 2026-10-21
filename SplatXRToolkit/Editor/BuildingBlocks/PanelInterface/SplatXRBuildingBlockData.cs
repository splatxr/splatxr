using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace SplatXR
{
    public static class SplatXRBuildingBlockData
    {
        private static readonly ISplatXRBuildingBlock[] buildingBlockTypes = new ISplatXRBuildingBlock[]
        {
            new SplatRendererBlock(),
            new SplatPhysicsBlock(),
            new SplatEffectsBlock(),
            new SplatBlendingBlock(),
        };

        [System.Serializable]
        public class BuildingBlock
        {
            public string name;
            public string description;
            public string documentationUrl;
            public Texture2D icon;
            public GameObject prefab;
            [System.NonSerialized]
            public Dictionary<string, object> parameters;
            public ISplatXRBuildingBlock blockImplementation;
        
            public BuildingBlock(ISplatXRBuildingBlock implementation)
            {
                this.blockImplementation = implementation;
                this.name = implementation.Name;
                this.description = implementation.Description;
                this.documentationUrl = implementation.DocumentationUrl;
                this.parameters = implementation.InitializeParameters();
                this.icon = implementation.LoadIcon();
            }
        }

        public static List<BuildingBlock> InitializeBuildingBlocks()
        {
            var buildingBlocks = new List<BuildingBlock>();
        
            foreach (var implementation in buildingBlockTypes)
            {
                buildingBlocks.Add(new BuildingBlock(implementation));
            }

            return buildingBlocks;
        }

        public static void AddBlockToScene(BuildingBlock block)
        {
            block.blockImplementation.AddToScene(null, block.parameters);
        }
    }
}