using UnityEngine;

namespace SplatXR
{
    public class SplatRendererBuildingBlock : MonoBehaviour
    {
        [Header("Splat Renderer Configuration")]
        [Tooltip("The GameObject that contains the splat renderer component")]
        public GameObject splatRenderer;
    
        [Header("Renderer Settings")]
        [Tooltip("Type of renderer being used")]
        [HideInInspector]
        public RendererType rendererType = RendererType.BaseRenderer;

        [Header("Rendering Order")]
        [Tooltip("Render Queue of the splat material. Higher values are drawn later, on top of lower ones.")]
        public int renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 1;
    
        public enum RendererType
        {
            BaseRenderer,
            LoDRenderer
        }
    
        void Start()
        {
            InitializeSplatRenderer();
        }
    
        private void InitializeSplatRenderer()
        {
            if (splatRenderer == null)
            {
                Debug.LogWarning($"[SplatRendererBuildingBlock] No splat renderer assigned to {gameObject.name}");
                return;
            }
        
        }
    
        public Material GetSplatMaterial()
        {
            if (splatRenderer == null)
            {
                return null;
            }

            var meshRenderer = splatRenderer.GetComponent<MeshRenderer>();
            return meshRenderer != null ? meshRenderer.sharedMaterial : null;
        }

        public void ApplyRenderQueue()
        {
            renderQueue = Mathf.Clamp(renderQueue, 0, 5000);

            Material splatMaterial = GetSplatMaterial();
            if (splatMaterial != null)
            {
                splatMaterial.renderQueue = renderQueue;
            }
        }

        public GameObject GetSplatRenderer()
        {
            return splatRenderer;
        }
    
        public void SetSplatRenderer(GameObject renderer)
        {
            splatRenderer = renderer;
            InitializeSplatRenderer();
        }
    }
}