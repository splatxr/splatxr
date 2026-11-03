using UnityEngine;
using Unity.Collections;
using System.Globalization;
using System.IO;
using System.Linq;

namespace SplatXR
{
    #if UNITY_EDITOR
    using UnityEditor;
    #endif

    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class BaseRendererSplatXR : MonoBehaviour
    {

        [Header("Splat Settings")]
        [SerializeField]
        public TextAsset splatFilePath;

        [SerializeField]
        [Tooltip("Rate in seconds for sorting the splats. Keep a higher number for performance on larger scenes and lower number for smaller objects")]
        public float sortingRate = 1.0f;

        [HideInInspector]
        public Material material;
        private Mesh mesh;

        private Vector3[] positions;
        private Vector3[] colors;
        private float[] opacities;
        private Vector3[] covariances0_3;  
        private Vector3[] covariances3_6;  

        private float lastSortTime = 0f;



        private TextAsset lastCsvFile;

        private bool enableEditorRendering = true;


        private System.Threading.Tasks.Task<MeshUpdate> sortingTask;
        [System.NonSerialized] private bool isSorting = false;

        // The splats currently in the mesh's vertex buffer ("slots") and the reusable sort buffers.
        [System.NonSerialized] private Vector3[] meshSourcePositions;
        [System.NonSerialized] private SplatCropBox meshCrop;
        [System.NonSerialized] private Vector3[] slotCenters;
        [System.NonSerialized] private int slotCount;
        [System.NonSerialized] private Bounds slotBounds;
        [System.NonSerialized] private float[] sortKeys;
        [System.NonSerialized] private int[] sortSlots;
        [System.NonSerialized] private int[] sortIndices;

        private static readonly int ScaleFactorId = Shader.PropertyToID("_ScaleFactor");

        // Result of a background mesh update. vertices is null when the vertex buffer didn't need rebuilding.
        private sealed class MeshUpdate
        {
            public Vector3[] sourcePositions;
            public SplatCropBox crop;
            public SplatQuads.Vertex[] vertices;
            public Vector3[] slotCenters;
            public int slotCount;
            public Bounds slotBounds;
            public float[] sortKeys;
            public int[] sortSlots;
            public int[] sortIndices;
        }

        [SerializeField] private bool enableCropping = false;
        [SerializeField] private Vector3 cropBoundsMin = new Vector3(-5f, -5f, -5f);
        [SerializeField] private Vector3 cropBoundsMax = new Vector3(5f, 5f, 5f);
        [SerializeField] private Vector3 cropBoundsRotation = Vector3.zero;
        [SerializeField] private bool showCropBounds = true;
        private Color cropBoundsGizmoColor = Color.yellow;
        private float cropBoundsGizmoThickness = 1.0f;
    
        public bool EnableCropping => enableCropping;

        private bool lastEnableCropping;
        private Vector3 lastCropBoundsMin;
        private Vector3 lastCropBoundsMax;
        private Vector3 lastCropBoundsRotation;

        private const double updateInterval = 1.0;
            private Vector3 lastCamPosition;
        private Quaternion lastCamRotation;

    #if UNITY_EDITOR
        void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        void OnSceneGUI(SceneView sceneView)
        {
            Camera cam = sceneView.camera;

            if (cam.transform.position != lastCamPosition || cam.transform.rotation != lastCamRotation)
            {
                lastCamPosition = cam.transform.position;
                lastCamRotation = cam.transform.rotation;

                            if (!EditorApplication.isPlaying && SceneView.lastActiveSceneView != null && SceneView.lastActiveSceneView.camera != null)
                {
                    Camera editorCamera = SceneView.lastActiveSceneView.camera;
                    editorCameraPosition = editorCamera.transform.position;
                    editorCameraRotation = editorCamera.transform.rotation;
                    editorCameraProjectionMatrix = editorCamera.projectionMatrix;
                    UpdateMesh();
                }
                else
                {
                    if (EditorApplication.isPlaying)
                    {
                        if (Camera.main != null)
                        {
                            editorCameraPosition = Camera.main.transform.position;
                            editorCameraRotation = Camera.main.transform.rotation;
                            editorCameraProjectionMatrix = Camera.main.projectionMatrix;
                        }
                    }
                    else if (SceneView.lastActiveSceneView?.camera != null)
                    {
                        Camera editorCamera = SceneView.lastActiveSceneView.camera;
                        editorCameraPosition = editorCamera.transform.position;
                        editorCameraRotation = editorCamera.transform.rotation;
                        editorCameraProjectionMatrix = editorCamera.projectionMatrix;
                    }
                    UpdateMesh();
                }
            }
        }
    #endif
        public void Start()
        {
            mesh = SplatQuads.CreateMesh();
            GetComponent<MeshFilter>().mesh = mesh;
            meshSourcePositions = null;


            var meshRenderer = GetComponent<MeshRenderer>();

            meshRenderer.forceRenderingOff = false;
            meshRenderer.allowOcclusionWhenDynamic = false;
            meshRenderer.bounds = new Bounds(Vector3.zero, Vector3.one * 10000f);

            LoadFromCSV();
            lastCsvFile = splatFilePath;
        }
    
        public void LoadFromCSV()
        {
            if (splatFilePath == null)
            {
                Debug.LogError("No CSV file assigned. Please assign a CSV file in the inspector.");
                return;
            }

            SplatCsvLoader.Load(splatFilePath, out Vector3[] positions, out Vector3[] colors, out float[] opacities,
                out Vector3[] covariances0_3, out Vector3[] covariances3_6);

            this.positions = positions;
            this.colors = colors;
            this.opacities = opacities;
            this.covariances0_3 = covariances0_3;
            this.covariances3_6 = covariances3_6;

            this.lastSortTime = -sortingRate;
        }

        private Vector3 editorCameraPosition;
        private Quaternion editorCameraRotation;
        private Matrix4x4 editorCameraProjectionMatrix;
        double  lastPrintTime;
        float lastPrintTime2;


        private void Update()
        {
           #if UNITY_EDITOR
            double time = UnityEditor.EditorApplication.timeSinceStartup;
            if (Mathf.FloorToInt((float)time) != Mathf.FloorToInt((float)lastPrintTime))
            {
                lastPrintTime = time;
            }
    #endif
            transform.position += Vector3.zero;

    #if !UNITY_EDITOR
            if (Camera.main != null)
            {
                editorCameraPosition = Camera.main.transform.position;
                editorCameraRotation = Camera.main.transform.rotation;
                editorCameraProjectionMatrix = Camera.main.projectionMatrix;
            }
    #else
            if (EditorApplication.isPlaying)
            {
                if (Camera.main != null)
                {
                    editorCameraPosition = Camera.main.transform.position;
                    editorCameraRotation = Camera.main.transform.rotation;
                    editorCameraProjectionMatrix = Camera.main.projectionMatrix;
                }
            }
    #endif

            bool cropBoundsChanged = enableCropping != lastEnableCropping ||
                                    cropBoundsMin != lastCropBoundsMin ||
                                    cropBoundsMax != lastCropBoundsMax ||
                                    cropBoundsRotation != lastCropBoundsRotation;
        
            bool csvFileChanged = splatFilePath != lastCsvFile;
            if (csvFileChanged)
            {
                LoadFromCSV();
                lastCsvFile = splatFilePath;
                if (!isSorting)
                {
    #if UNITY_EDITOR
                    if (!EditorApplication.isPlaying && !enableEditorRendering)
                    {
                        return;
                    }
    #endif
                    UpdateMesh();
                    lastSortTime = Time.time;
                }
            }
        
            if (cropBoundsChanged)
            {
                lastEnableCropping = enableCropping;
                lastCropBoundsMin = cropBoundsMin;
                lastCropBoundsMax = cropBoundsMax;
                lastCropBoundsRotation = cropBoundsRotation;
            
                if (!isSorting)
                {
    #if UNITY_EDITOR
                    if (!EditorApplication.isPlaying && !enableEditorRendering)
                    {
                        return;
                    }
    #endif
                    UpdateMesh();
                    lastSortTime = Time.time;
                }
                else
                {
                    // Rebuild with the new bounds as soon as the running update finishes.
                    lastSortTime = float.NegativeInfinity;
                }
            }

            if (Time.time - lastSortTime >= sortingRate && !isSorting)
            {
    #if UNITY_EDITOR
                if (!EditorApplication.isPlaying && !enableEditorRendering)
                {
                    return;
                }
    #endif
                UpdateMesh();
                lastSortTime = Time.time;
            }
            else if (isSorting && sortingTask?.IsCompleted == true)
            {
    #if UNITY_EDITOR
                if (!EditorApplication.isPlaying && !enableEditorRendering)
                {
                    return;
                }
    #endif
                UpdateMesh();
            }
    #if UNITY_EDITOR
    #endif

    #if !UNITY_EDITOR
            if (Camera.main != null)
            {
                editorCameraPosition = Camera.main.transform.position;
                editorCameraRotation = Quaternion.identity;
                editorCameraProjectionMatrix = Camera.main.projectionMatrix;
            }
    #endif
        }
    
    
    #if UNITY_EDITOR
         private static void UpdateSceneCamera()
        {
            if (SceneView.lastActiveSceneView != null)
            {
                Camera sceneCam = SceneView.lastActiveSceneView.camera;
            }
        }
    #endif
    
        private void UpdateMesh()
        {
            if (positions == null || mesh == null) return;

    #if UNITY_EDITOR
            if (!EditorApplication.isPlaying && !enableEditorRendering)
            {
                if (mesh != null)
                {
                    mesh.Clear();
                }
                return;
            }
    #endif

            if (!isSorting)
            {
                StartMeshUpdate();
                return;
            }

            if (sortingTask?.IsCompleted != true) return;

            isSorting = false;
            if (LogIfFailed(sortingTask, "sorting")) return;

            var update = sortingTask.Result;
            if (update.sourcePositions != positions)
            {
                lastSortTime = float.NegativeInfinity;
                return;
            }

            ApplyMeshUpdate(update);
        }

        // Snapshots the inputs on the main thread, then sorts on a background thread (first rebuilding the vertex
        // data if the splats or the crop bounds changed). Unity's Mesh API is only used in ApplyMeshUpdate.
        private void StartMeshUpdate()
        {
            var currentPositions = positions;
            var currentColors = colors;
            var currentOpacities = opacities;
            var currentCovariances0_3 = covariances0_3;
            var currentCovariances3_6 = covariances3_6;

            var crop = new SplatCropBox(enableCropping, cropBoundsMin, cropBoundsMax, cropBoundsRotation);
            bool rebuildVertices = meshSourcePositions != currentPositions ||
                                   !crop.SelectsSameAs(meshCrop) ||
                                   mesh.vertexCount != slotCount * 4;

            var currentSlotCenters = slotCenters;
            var currentSlotCount = slotCount;
            var currentSlotBounds = slotBounds;
            var keys = sortKeys;
            var slots = sortSlots;
            var indices = sortIndices;

            var cameraPos = GetCurrentCameraPosition();
            var worldMatrix = GetSortMatrix();

            isSorting = true;
            sortingTask = System.Threading.Tasks.Task.Run(() =>
            {
                var update = new MeshUpdate { sourcePositions = currentPositions, crop = crop };

                if (rebuildVertices)
                {
                    BuildSlots(update, currentPositions, currentColors, currentOpacities, currentCovariances0_3, currentCovariances3_6, crop);
                }
                else
                {
                    update.slotCenters = currentSlotCenters;
                    update.slotCount = currentSlotCount;
                    update.slotBounds = currentSlotBounds;
                }

                int count = update.slotCount;
                if (keys == null || keys.Length < count)
                {
                    keys = new float[count];
                    slots = new int[count];
                    indices = new int[count * 6];
                }

                var centers = update.slotCenters;
                for (int i = 0; i < count; i++)
                {
                    keys[i] = -(worldMatrix.MultiplyPoint3x4(centers[i]) - cameraPos).sqrMagnitude;
                    slots[i] = i;
                }
                SplatQuads.SortAndWriteIndices(keys, slots, count, indices);

                update.sortKeys = keys;
                update.sortSlots = slots;
                update.sortIndices = indices;
                return update;
            });
        }

        private static void BuildSlots(MeshUpdate update, Vector3[] positionsArray, Vector3[] colorsArray, float[] opacitiesArray,
                                       Vector3[] covariances0_3Array, Vector3[] covariances3_6Array, SplatCropBox crop)
        {
            var inside = new int[positionsArray.Length];
            int count = 0;
            for (int i = 0; i < positionsArray.Length; i++)
            {
                Vector3 splatPosition = positionsArray[i];
                splatPosition.x = -splatPosition.x;
                if (crop.Contains(splatPosition))
                {
                    inside[count++] = i;
                }
            }

            var vertices = new SplatQuads.Vertex[count * 4];
            var centers = new Vector3[count];
            for (int slot = 0; slot < count; slot++)
            {
                int i = inside[slot];
                centers[slot] = SplatQuads.WriteSlot(vertices, slot, positionsArray[i], colorsArray[i], opacitiesArray[i],
                                                     covariances0_3Array[i], covariances3_6Array[i]);
            }

            update.vertices = vertices;
            update.slotCenters = centers;
            update.slotCount = count;
            update.slotBounds = SplatQuads.ComputeBounds(centers, count);
        }

        private void ApplyMeshUpdate(MeshUpdate update)
        {
            if (update.vertices != null)
            {
                SplatQuads.UploadVertices(mesh, update.vertices, update.slotCount);
                meshSourcePositions = update.sourcePositions;
                meshCrop = update.crop;
                slotCenters = update.slotCenters;
                slotCount = update.slotCount;
                slotBounds = update.slotBounds;
            }

            sortKeys = update.sortKeys;
            sortSlots = update.sortSlots;
            sortIndices = update.sortIndices;
            SplatQuads.UploadIndices(mesh, sortIndices, slotCount, slotBounds);
        }

        // The splat shaders scale centers by _ScaleFactor in object space, so sort in that space too.
        private Matrix4x4 GetSortMatrix()
        {
            Material splatMaterial = GetComponent<MeshRenderer>().sharedMaterial;
            float scaleFactor = splatMaterial != null && splatMaterial.HasProperty(ScaleFactorId) ? splatMaterial.GetFloat(ScaleFactorId) : 1f;
            return transform.localToWorldMatrix * Matrix4x4.Scale(new Vector3(scaleFactor, scaleFactor, scaleFactor));
        }

        private bool LogIfFailed(System.Threading.Tasks.Task task, string stage)
        {
            if (!task.IsFaulted && !task.IsCanceled) return false;
            Debug.LogError($"[SplatXR] Splat {stage} failed on {name}: {task.Exception?.GetBaseException()}", this);
            return true;
        }

        private void OnDestroy()
        {
            if (isSorting && sortingTask != null)
            {
                try
                {
                    sortingTask.Wait();
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Error waiting for background tasks to complete: {e}");
                }
            }

    #if UNITY_EDITOR
            if (!UnityEditor.EditorApplication.isPlaying)
            {
                if (mesh != null) DestroyImmediate(mesh);
            }
            else
            {
                if (mesh != null) Destroy(mesh);
            }
    #else
            if (mesh != null) Destroy(mesh);
    #endif
        }

        private void OnDrawGizmos()
        {
            if (enableCropping && showCropBounds)
            {
                Gizmos.color = cropBoundsGizmoColor;
            
                Vector3 thicknessOffset = Vector3.one * (cropBoundsGizmoThickness - 1.0f) * 0.001f;
            
                Vector3 center = transform.TransformPoint((cropBoundsMin + cropBoundsMax) * 0.5f);
                Vector3 size = Vector3.Scale(cropBoundsMax - cropBoundsMin, transform.lossyScale);
            
                Matrix4x4 oldMatrix = Gizmos.matrix;
            
                Vector3 localCenter = (cropBoundsMin + cropBoundsMax) * 0.5f;
                Matrix4x4 trs = Matrix4x4.TRS(
                    transform.TransformPoint(localCenter), 
                    transform.rotation * Quaternion.Euler(cropBoundsRotation), 
                    transform.lossyScale
                );
                Gizmos.matrix = trs;
            
                Vector3 localSize = cropBoundsMax - cropBoundsMin;
            
                int thicknessSteps = Mathf.Max(1, Mathf.RoundToInt(cropBoundsGizmoThickness));
                for (int i = 0; i < thicknessSteps; i++)
                {
                    float offset = (i - (thicknessSteps - 1) * 0.5f) * 0.001f;
                    Vector3 sizeWithOffset = localSize + Vector3.one * offset;
                    Gizmos.DrawWireCube(Vector3.zero, sizeWithOffset);
                }
            
                Gizmos.matrix = oldMatrix;
            }
        }

        public void SetCropBounds(Vector3 min, Vector3 max)
        {
            cropBoundsMin = min;
            cropBoundsMax = max;
        }
    
        public void SetCropBounds(Vector3 min, Vector3 max, Vector3 rotation)
        {
            cropBoundsMin = min;
            cropBoundsMax = max;
            cropBoundsRotation = rotation;
        }
    
        public void GetCropBounds(out Vector3 min, out Vector3 max)
        {
            min = cropBoundsMin;
            max = cropBoundsMax;
        }
    
        public void GetCropBounds(out Vector3 min, out Vector3 max, out Vector3 rotation)
        {
            min = cropBoundsMin;
            max = cropBoundsMax;
            rotation = cropBoundsRotation;
        }
    
        public void SetCropBoundsRotation(Vector3 rotation)
        {
            cropBoundsRotation = rotation;
        }
    
        public void SetCroppingEnabled(bool enabled)
        {
            enableCropping = enabled;
        }
    
        public int LoadedSplatCount => positions?.Length ?? 0;

        public int GetVisibleSplatCount()
        {
            if (positions == null) return 0;
        
            if (!enableCropping) return positions.Length;

            var crop = new SplatCropBox(enableCropping, cropBoundsMin, cropBoundsMax, cropBoundsRotation);
            int count = 0;
            for (int i = 0; i < positions.Length; i++)
            {
                Vector3 pos = positions[i];
                pos.x = -pos.x;
                if (crop.Contains(pos))
                    count++;
            }
            return count;
        }

        private Vector3 GetCurrentCameraPosition()
        {
    #if UNITY_EDITOR
            if (!EditorApplication.isPlaying && SceneView.lastActiveSceneView?.camera != null)
            {
                return SceneView.lastActiveSceneView.camera.transform.position;
            }
            else if (Camera.main != null)
            {
                return Camera.main.transform.position;
            }
            else
            {
                return editorCameraPosition;
            }
    #else
            return Camera.main != null ? Camera.main.transform.position : Vector3.zero;
    #endif
        }
    }
}
