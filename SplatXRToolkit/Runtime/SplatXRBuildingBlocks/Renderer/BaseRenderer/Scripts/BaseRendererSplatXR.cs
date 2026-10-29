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


        private System.Threading.Tasks.Task<(int index, float dist)[]> sortingTask;
        private System.Threading.Tasks.Task<(Vector3[] vertices, Vector3[] centerPositions, Vector3[] vertexColors, 
            Vector4[] vertexOpacities, Vector3[] vertexCov0_3, Vector3[] vertexCov3_6, int[] indices)> meshArraysTask;
        [System.NonSerialized] private bool isSorting = false;
        [System.NonSerialized] private bool isCreatingMeshArrays = false;
        private Vector3[] sortedPositions;

        private System.Threading.Tasks.Task meshSetTask;
        [System.NonSerialized] private bool isSettingMesh = false;
        private Mesh tempMesh;

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
            mesh = new Mesh();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            mesh.MarkDynamic();
            GetComponent<MeshFilter>().mesh = mesh;


            var meshRenderer = GetComponent<MeshRenderer>();

            meshRenderer.forceRenderingOff = false;
            meshRenderer.allowOcclusionWhenDynamic = false;
            meshRenderer.bounds = new Bounds(Vector3.zero, Vector3.one * 10000f);


            tempMesh = new Mesh();
            tempMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            tempMesh.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            tempMesh.MarkDynamic();

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
                if (!isSorting && !isCreatingMeshArrays && !isSettingMesh)
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
            
                if (!isSorting && !isCreatingMeshArrays && !isSettingMesh)
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

            if (Time.time - lastSortTime >= sortingRate && !isSorting && !isCreatingMeshArrays && !isSettingMesh)
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
            else if ((isSorting && sortingTask?.IsCompleted == true) ||
                     (isCreatingMeshArrays && meshArraysTask?.IsCompleted == true) ||
                     (isSettingMesh && meshSetTask?.IsCompleted == true))
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
            if (positions == null) return;

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

            var cameraPos = GetCurrentCameraPosition();
            var worldMatrix = transform.localToWorldMatrix;
        
            var currentPositions = positions;
            var currentColors = colors;
            var currentOpacities = opacities;
            var currentCovariances0_3 = covariances0_3;
            var currentCovariances3_6 = covariances3_6;
        
            var currentEnableCropping = enableCropping;
            var currentCropBoundsMin = cropBoundsMin;
            var currentCropBoundsMax = cropBoundsMax;
            var currentCropBoundsRotation = cropBoundsRotation;
        
            if (!isSorting && !isCreatingMeshArrays && !isSettingMesh)
            {
                isSorting = true;
                sortedPositions = currentPositions;
                sortingTask = System.Threading.Tasks.Task.Run(() =>
                {
                    var sortingArray = new (int index, float dist)[currentPositions.Length];

                    for (int i = 0; i < currentPositions.Length; i++)
                    {
                        Vector3 flippedPos = currentPositions[i];
                        flippedPos.x = -flippedPos.x;
                        Vector3 worldPos = worldMatrix.MultiplyPoint3x4(flippedPos);
                        float distSqr = (worldPos - cameraPos).sqrMagnitude;
                        sortingArray[i] = (i, distSqr);
                    }

                    System.Array.Sort(sortingArray, (a, b) => b.dist.CompareTo(a.dist));
                    return sortingArray;
                });
                return;
            }

            if (isSorting && sortingTask?.IsCompleted == true && !isCreatingMeshArrays)
            {
                isSorting = false;
                if (LogIfFailed(sortingTask, "sorting")) return;

                if (sortedPositions != currentPositions)
                {
                    lastSortTime = float.NegativeInfinity;
                    return;
                }

                var sortingArray = sortingTask.Result;
                isCreatingMeshArrays = true;

                meshArraysTask = System.Threading.Tasks.Task.Run(() => CreateMeshArrays(sortingArray, currentPositions, currentColors, currentOpacities, currentCovariances0_3, currentCovariances3_6, currentEnableCropping, currentCropBoundsMin, currentCropBoundsMax, currentCropBoundsRotation));
                return;
            }

            if (isCreatingMeshArrays && meshArraysTask?.IsCompleted == true && !isSettingMesh)
            {
                isCreatingMeshArrays = false;
                if (LogIfFailed(meshArraysTask, "mesh building")) return;

                var meshArrays = meshArraysTask.Result;

                bool isQuestPlatform = Application.platform == RuntimePlatform.Android && !Application.isEditor;

                if (isQuestPlatform)
                {
                    isSettingMesh = true;
                    meshSetTask = System.Threading.Tasks.Task.Run(() =>
                    {
                        tempMesh.Clear();
                        tempMesh.vertices = meshArrays.vertices;
                        tempMesh.SetUVs(1, meshArrays.centerPositions);
                        tempMesh.SetUVs(2, meshArrays.vertexColors);
                        tempMesh.SetUVs(3, meshArrays.vertexOpacities);
                        tempMesh.SetUVs(5, meshArrays.vertexCov0_3);
                        tempMesh.SetUVs(6, meshArrays.vertexCov3_6);
                        tempMesh.SetIndices(meshArrays.indices, MeshTopology.Triangles, 0);
                        tempMesh.RecalculateBounds();
                    });
                }
                else
                {
                    mesh.Clear();
                    mesh.vertices = meshArrays.vertices;
                    mesh.SetUVs(1, meshArrays.centerPositions);
                    mesh.SetUVs(2, meshArrays.vertexColors);
                    mesh.SetUVs(3, meshArrays.vertexOpacities);
                    mesh.SetUVs(5, meshArrays.vertexCov0_3);
                    mesh.SetUVs(6, meshArrays.vertexCov3_6);
                    mesh.SetIndices(meshArrays.indices, MeshTopology.Triangles, 0);
                    mesh.RecalculateBounds();
                }
                return;
            }

            bool isQuestDevice = Application.platform == RuntimePlatform.Android && !Application.isEditor;
            if (isQuestDevice && isSettingMesh && meshSetTask?.IsCompleted == true)
            {
                isSettingMesh = false;
                if (LogIfFailed(meshSetTask, "mesh upload")) return;

                var temp = mesh;
                mesh = tempMesh;
                tempMesh = temp;
                GetComponent<MeshFilter>().mesh = mesh;
            }
        }

        private bool LogIfFailed(System.Threading.Tasks.Task task, string stage)
        {
            if (!task.IsFaulted && !task.IsCanceled) return false;
            Debug.LogError($"[SplatXR] Splat {stage} failed on {name}: {task.Exception?.GetBaseException()}", this);
            return true;
        }

        private bool IsPositionWithinCropBounds(Vector3 position)
        {
            return IsPositionWithinCropBounds(position, enableCropping, cropBoundsMin, cropBoundsMax, cropBoundsRotation);
        }
    
        private bool IsPositionWithinCropBounds(Vector3 position, bool enableCroppingParam, Vector3 cropBoundsMinParam, Vector3 cropBoundsMaxParam, Vector3 cropBoundsRotationParam)
        {
            if (!enableCroppingParam) return true;
        
            Vector3 cropCenter = (cropBoundsMinParam + cropBoundsMaxParam) * 0.5f;
            Vector3 cropSize = cropBoundsMaxParam - cropBoundsMinParam;
        
            Vector3 localPosition = position - cropCenter;
        
            if (cropBoundsRotationParam != Vector3.zero)
            {
                Quaternion inverseRotation = Quaternion.Inverse(Quaternion.Euler(cropBoundsRotationParam));
                localPosition = inverseRotation * localPosition;
            }
        
            return Mathf.Abs(localPosition.x) <= cropSize.x * 0.5f &&
                   Mathf.Abs(localPosition.y) <= cropSize.y * 0.5f &&
                   Mathf.Abs(localPosition.z) <= cropSize.z * 0.5f;
        }

        private (Vector3[] vertices, Vector3[] centerPositions, Vector3[] vertexColors, 
                 Vector4[] vertexOpacities, Vector3[] vertexCov0_3, Vector3[] vertexCov3_6, 
                 int[] indices) 
            CreateMeshArrays((int index, float dist)[] sortingArray, Vector3[] positionsArray, Vector3[] colorsArray, 
                            float[] opacitiesArray, Vector3[] covariances0_3Array, Vector3[] covariances3_6Array,
                            bool enableCroppingParam, Vector3 cropBoundsMinParam, Vector3 cropBoundsMaxParam, Vector3 cropBoundsRotationParam)
        {
            int validSplatCount = 0;
            for (int i = 0; i < sortingArray.Length; i++)
            {
                Vector3 splatPosition = positionsArray[sortingArray[i].index];
                splatPosition.x = -splatPosition.x;
                if (IsPositionWithinCropBounds(splatPosition, enableCroppingParam, cropBoundsMinParam, cropBoundsMaxParam, cropBoundsRotationParam))
                {
                    validSplatCount++;
                }
            }

            int vertexCount = validSplatCount * 4;
            Vector3[] vertices = new Vector3[vertexCount];
            Vector3[] centerPositions = new Vector3[vertexCount];
            Vector3[] vertexColors = new Vector3[vertexCount];
            Vector4[] vertexOpacities = new Vector4[vertexCount];
            Vector3[] vertexCov0_3 = new Vector3[vertexCount];
            Vector3[] vertexCov3_6 = new Vector3[vertexCount];

            int validIndex = 0;
            for (int i = 0; i < sortingArray.Length; i++)
            {
                Vector3 splatPosition = positionsArray[sortingArray[i].index];
                splatPosition.x = -splatPosition.x;
                if (!IsPositionWithinCropBounds(splatPosition, enableCroppingParam, cropBoundsMinParam, cropBoundsMaxParam, cropBoundsRotationParam))
                    continue;

                int baseIndex = validIndex * 4;
            
                vertices[baseIndex + 0] = new Vector3(-1, -1, 0);
                vertices[baseIndex + 1] = new Vector3( 1, -1, 0);
                vertices[baseIndex + 2] = new Vector3(-1,  1, 0);
                vertices[baseIndex + 3] = new Vector3( 1,  1, 0);

                Vector3 flippedPosition = positionsArray[sortingArray[i].index];
                flippedPosition.x = -flippedPosition.x;
            
                Vector3 flippedCov0_3 = covariances0_3Array[sortingArray[i].index];
                Vector3 flippedCov3_6 = covariances3_6Array[sortingArray[i].index];
                flippedCov0_3.y = -flippedCov0_3.y;
                flippedCov0_3.z = -flippedCov0_3.z;
            
                for (int j = 0; j < 4; j++)
                {
                    centerPositions[baseIndex + j] = flippedPosition;
                    vertexColors[baseIndex + j] = colorsArray[sortingArray[i].index];
                    vertexOpacities[baseIndex + j] = new Vector4(opacitiesArray[sortingArray[i].index], 0, 0, 0);
                    vertexCov0_3[baseIndex + j] = flippedCov0_3;
                    vertexCov3_6[baseIndex + j] = flippedCov3_6;
                }
                validIndex++;
            }

            int[] indices = new int[validSplatCount * 6];
            for (int i = 0; i < validSplatCount; i++)
            {
                int baseIndex = i * 6;
                int baseVertex = i * 4;
            
                indices[baseIndex + 0] = baseVertex + 0;
                indices[baseIndex + 1] = baseVertex + 2;
                indices[baseIndex + 2] = baseVertex + 1;
            
                indices[baseIndex + 3] = baseVertex + 2;
                indices[baseIndex + 4] = baseVertex + 3;
                indices[baseIndex + 5] = baseVertex + 1;
            }

            return (vertices, centerPositions, vertexColors, vertexOpacities, vertexCov0_3, vertexCov3_6, indices);
        }

        private void OnDestroy()
        {
            if ((isSorting && sortingTask != null) || 
                (isCreatingMeshArrays && meshArraysTask != null) || 
                (isSettingMesh && meshSetTask != null))
            {
                try
                {
                    sortingTask?.Wait();
                    meshArraysTask?.Wait();
                    meshSetTask?.Wait();
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
                if (tempMesh != null) DestroyImmediate(tempMesh);
            }
            else
            {
                if (mesh != null) Destroy(mesh);
                if (tempMesh != null) Destroy(tempMesh);
            }
    #else
            if (mesh != null) Destroy(mesh);
            if (tempMesh != null) Destroy(tempMesh);
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
        
            int count = 0;
            for (int i = 0; i < positions.Length; i++)
            {
                Vector3 pos = positions[i];
                pos.x = -pos.x;
                if (IsPositionWithinCropBounds(pos))
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
