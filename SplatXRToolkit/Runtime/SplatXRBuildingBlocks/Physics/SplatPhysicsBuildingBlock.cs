using UnityEngine;

namespace SplatXR
{
    public class SplatPhysicsBuildingBlock : MonoBehaviour
    {
        [System.Serializable]
        public struct MeshGenerationSettings
        {
            public int splatType;            // 0 = Object, 1 = Space
            public float meshThreshold;
            public float gaussianMeshScale;
            public bool meshBaseFlattening;
            public float flattenPercentage;
            public int flattenAxis;          // 0=X, 1=Y, 2=Z, 3=-X, 4=-Y, 5=-Z
        }

        [SerializeField, HideInInspector]
        private string meshGenerationInfo = "";

        [SerializeField, HideInInspector]
        private bool hasMeshGenerationSettings = false;

        [SerializeField, HideInInspector]
        private MeshGenerationSettings meshGenerationSettings;

        public string MeshGenerationInfo => meshGenerationInfo;

        public bool HasMeshGenerationSettings => hasMeshGenerationSettings;

        public MeshGenerationSettings LastMeshGenerationSettings => meshGenerationSettings;

        public void SetMeshGenerationInfo(string info, MeshGenerationSettings settings)
        {
            meshGenerationInfo = info;
            meshGenerationSettings = settings;
            hasMeshGenerationSettings = true;
        }
    }
}
