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
    public class LoDRendererSplatXR : MonoBehaviour
    {
        [HideInInspector]
        public Material material;
    
        [Space(5)]
        [Header("LOD Splat Data")]
        [SerializeField]
        public TextAsset splatFilePathHigh;
        [SerializeField]
        public TextAsset splatFilePathMedium;
        [SerializeField]
        public TextAsset splatFilePathLow;

        [Space(5)]
        [Header("LOD Settings")]
        [SerializeField] private float lodDistanceHigh = 2.0f;
        [SerializeField] private float lodDistanceMedium = 5.0f;
        [SerializeField] private bool enableLODDebugColoring = false;

        [Space(5)]
        [Header("Rendering Settings")]
        [SerializeField]
        [Tooltip("Rate in seconds for sorting the splats. Keep a higher number for performance on larger scenes and lower number for smaller objects")]
        public float sortInterval = 1.0f;

        private bool enableEditorRendering = true;

        private Mesh mesh;

        private Vector3[] positionsHigh;
        private Vector3[] colorsHigh;
        private float[] opacitiesHigh;
        private Vector3[] covariances0_3High;  
        private Vector3[] covariances3_6High;  

        private Vector3[] positionsMedium;
        private Vector3[] colorsMedium;
        private float[] opacitiesMedium;
        private Vector3[] covariances0_3Medium;  
        private Vector3[] covariances3_6Medium;  

        private Vector3[] positionsLow;
        private Vector3[] colorsLow;
        private float[] opacitiesLow;
        private Vector3[] covariances0_3Low;  
        private Vector3[] covariances3_6Low;  

        private Vector3[] positions;
        private Vector3[] colors;
        private float[] opacities;
        private Vector3[] covariances0_3;  
        private Vector3[] covariances3_6;  

        private float lastSortTime = 0f;
    
        private TextAsset lastCsvFileHigh;
        private TextAsset lastCsvFileMedium;
        private TextAsset lastCsvFileLow;
        private TextAsset lastCsvFile;

        private System.Threading.Tasks.Task<(int index, float dist)[]> sortingTask;
        private System.Threading.Tasks.Task<(Vector3[] vertices, Vector3[] centerPositions, Vector3[] vertexColors, 
            Vector4[] vertexOpacities, Vector3[] vertexCov0_3, Vector3[] vertexCov3_6, int[] indices)> meshArraysTask;
        [System.NonSerialized] private bool isSorting = false;
        [System.NonSerialized] private bool isCreatingMeshArrays = false;
        private Vector3[] sortedPositionsHigh;
        private Vector3[] sortedPositionsMedium;
        private Vector3[] sortedPositionsLow;

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

        private Vector3 editorCameraPosition;
        private Quaternion editorCameraRotation;
        private Matrix4x4 editorCameraProjectionMatrix;

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
            if (material != null)
            {
                meshRenderer.sharedMaterial = material;
            }

            meshRenderer.forceRenderingOff = false;
            meshRenderer.allowOcclusionWhenDynamic = false;
            meshRenderer.bounds = new Bounds(Vector3.zero, Vector3.one * 10000f);


            tempMesh = new Mesh();
            tempMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            tempMesh.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            tempMesh.MarkDynamic();

            LoadLODSplats();
            lastCsvFileHigh = splatFilePathHigh;
            lastCsvFileMedium = splatFilePathMedium;
            lastCsvFileLow = splatFilePathLow;
        }
    
        public void LoadLODSplats()
        {
            if (SplatCsvLoader.Load(splatFilePathHigh, out positionsHigh, out colorsHigh, out opacitiesHigh, out covariances0_3High, out covariances3_6High))
            {
                //Debug.Log($"Loaded {positionsHigh.Length} high detail splats");
            }

            if (SplatCsvLoader.Load(splatFilePathMedium, out positionsMedium, out colorsMedium, out opacitiesMedium, out covariances0_3Medium, out covariances3_6Medium))
            {
                //Debug.Log($"Loaded {positionsMedium.Length} medium detail splats");
            }

            if (SplatCsvLoader.Load(splatFilePathLow, out positionsLow, out colorsLow, out opacitiesLow, out covariances0_3Low, out covariances3_6Low))
            {
                //Debug.Log($"Loaded {positionsLow.Length} low detail splats");
            }
        }

        private void Update()
        {
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
        
            bool lodCsvFilesChanged = splatFilePathHigh != lastCsvFileHigh ||
                                     splatFilePathMedium != lastCsvFileMedium ||
                                     splatFilePathLow != lastCsvFileLow;

            if (lodCsvFilesChanged)
            {
                LoadLODSplats();
                lastCsvFileHigh = splatFilePathHigh;
                lastCsvFileMedium = splatFilePathMedium;
                lastCsvFileLow = splatFilePathLow;
            }
        
            if (lodCsvFilesChanged)
            {
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

            if (Time.time - lastSortTime >= sortInterval &&!isSorting && !isCreatingMeshArrays && !isSettingMesh)
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

    #if !UNITY_EDITOR
            if (Camera.main != null)
            {
                editorCameraPosition = Camera.main.transform.position;
                editorCameraRotation = Quaternion.identity;
                editorCameraProjectionMatrix = Camera.main.projectionMatrix;
            }
    #endif
        }
    
        private void UpdateMesh()
        {
            bool hasLODData = (positionsHigh != null && positionsHigh.Length > 0) ||
                             (positionsMedium != null && positionsMedium.Length > 0) ||
                             (positionsLow != null && positionsLow.Length > 0);
        
            if (!hasLODData && positions == null) return;

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

            var startTime = Time.realtimeSinceStartup;
            var cameraPos = GetCurrentCameraPosition();
            var worldMatrix = transform.localToWorldMatrix;
        
            var currentPositionsHigh = positionsHigh;
            var currentColorsHigh = colorsHigh;
            var currentOpacitiesHigh = opacitiesHigh;
            var currentCovariances0_3High = covariances0_3High;
            var currentCovariances3_6High = covariances3_6High;
        
            var currentPositionsMedium = positionsMedium;
            var currentColorsMedium = colorsMedium;
            var currentOpacitiesMedium = opacitiesMedium;
            var currentCovariances0_3Medium = covariances0_3Medium;
            var currentCovariances3_6Medium = covariances3_6Medium;
        
            var currentPositionsLow = positionsLow;
            var currentColorsLow = colorsLow;
            var currentOpacitiesLow = opacitiesLow;
            var currentCovariances0_3Low = covariances0_3Low;
            var currentCovariances3_6Low = covariances3_6Low;
        
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
                sortedPositionsHigh = currentPositionsHigh;
                sortedPositionsMedium = currentPositionsMedium;
                sortedPositionsLow = currentPositionsLow;
                sortingTask = System.Threading.Tasks.Task.Run(() =>
                {
                    return CreateLODSortingArray(cameraPos, worldMatrix, 
                        currentPositionsHigh, currentPositionsMedium, currentPositionsLow, currentPositions);
                });
                return;
            }

            if (isSorting && sortingTask?.IsCompleted == true && !isCreatingMeshArrays)
            {
                isSorting = false;
                if (LogIfFailed(sortingTask, "sorting")) return;

                if (sortedPositionsHigh != currentPositionsHigh ||
                    sortedPositionsMedium != currentPositionsMedium ||
                    sortedPositionsLow != currentPositionsLow)
                {
                    lastSortTime = float.NegativeInfinity;
                    return;
                }

                var sortingArray = sortingTask.Result;
                isCreatingMeshArrays = true;

                startTime = Time.realtimeSinceStartup;
                meshArraysTask = System.Threading.Tasks.Task.Run(() => CreateLODMeshArrays(sortingArray, 
                    currentPositionsHigh, currentColorsHigh, currentOpacitiesHigh, currentCovariances0_3High, currentCovariances3_6High,
                    currentPositionsMedium, currentColorsMedium, currentOpacitiesMedium, currentCovariances0_3Medium, currentCovariances3_6Medium,
                    currentPositionsLow, currentColorsLow, currentOpacitiesLow, currentCovariances0_3Low, currentCovariances3_6Low,
                    currentPositions, currentColors, currentOpacities, currentCovariances0_3, currentCovariances3_6,
                    currentEnableCropping, currentCropBoundsMin, currentCropBoundsMax, currentCropBoundsRotation, cameraPos, worldMatrix));
                return;
            }

            if (isCreatingMeshArrays && meshArraysTask?.IsCompleted == true && !isSettingMesh)
            {
                isCreatingMeshArrays = false;
                if (LogIfFailed(meshArraysTask, "mesh building")) return;

                var meshArrays = meshArraysTask.Result;

                startTime = Time.realtimeSinceStartup;

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

        private static int[] GetLevelForEachBand(bool hasHigh, bool hasMedium, bool hasLow)
        {
            bool[] hasLevel = { hasHigh, hasMedium, hasLow };
            int[][] preferredLevels =
            {
                new[] { 0, 1, 2 },
                new[] { 1, 2, 0 },
                new[] { 2, 1, 0 }
            };

            int[] levelForBand = { -1, -1, -1 };
            for (int band = 0; band < 3; band++)
            {
                foreach (int level in preferredLevels[band])
                {
                    if (hasLevel[level])
                    {
                        levelForBand[band] = level;
                        break;
                    }
                }
            }
            return levelForBand;
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

        private (int index, float dist)[] CreateLODSortingArray(Vector3 cameraPos, Matrix4x4 worldMatrix,
            Vector3[] posHighArray, Vector3[] posMedArray, Vector3[] posLowArray, Vector3[] posLegacyArray)
        {
            int totalCount = 0;
            if (posHighArray != null) totalCount += posHighArray.Length;
            if (posMedArray != null) totalCount += posMedArray.Length;
            if (posLowArray != null) totalCount += posLowArray.Length;
            if (posLegacyArray != null) totalCount += posLegacyArray.Length;

            if (totalCount == 0) return new (int index, float dist)[0];

            var sortingArray = new (int index, float dist)[totalCount];
            int arrayIndex = 0;

            if (posHighArray != null)
            {
                for (int i = 0; i < posHighArray.Length; i++)
                {
                    Vector3 flippedPos = posHighArray[i];
                    flippedPos.x = -flippedPos.x;
                    Vector3 worldPos = worldMatrix.MultiplyPoint3x4(flippedPos);
                    float distSqr = (worldPos - cameraPos).sqrMagnitude;
                    sortingArray[arrayIndex] = (i, distSqr);
                    arrayIndex++;
                }
            }

            if (posMedArray != null)
            {
                int mediumStartIndex = (posHighArray?.Length ?? 0);
                for (int i = 0; i < posMedArray.Length; i++)
                {
                    Vector3 flippedPos = posMedArray[i];
                    flippedPos.x = -flippedPos.x;
                    Vector3 worldPos = worldMatrix.MultiplyPoint3x4(flippedPos);
                    float distSqr = (worldPos - cameraPos).sqrMagnitude;
                    sortingArray[arrayIndex] = (mediumStartIndex + i, distSqr);
                    arrayIndex++;
                }
            }

            if (posLowArray != null)
            {
                int lowStartIndex = (posHighArray?.Length ?? 0) + (posMedArray?.Length ?? 0);
                for (int i = 0; i < posLowArray.Length; i++)
                {
                    Vector3 flippedPos = posLowArray[i];
                    flippedPos.x = -flippedPos.x;
                    Vector3 worldPos = worldMatrix.MultiplyPoint3x4(flippedPos);
                    float distSqr = (worldPos - cameraPos).sqrMagnitude;
                    sortingArray[arrayIndex] = (lowStartIndex + i, distSqr);
                    arrayIndex++;
                }
            }

            if (posLegacyArray != null)
            {
                int legacyStartIndex = (posHighArray?.Length ?? 0) + (posMedArray?.Length ?? 0) + (posLowArray?.Length ?? 0);
                for (int i = 0; i < posLegacyArray.Length; i++)
                {
                    Vector3 flippedPos = posLegacyArray[i];
                    flippedPos.x = -flippedPos.x;
                    Vector3 worldPos = worldMatrix.MultiplyPoint3x4(flippedPos);
                    float distSqr = (worldPos - cameraPos).sqrMagnitude;
                    sortingArray[arrayIndex] = (legacyStartIndex + i, distSqr);
                    arrayIndex++;
                }
            }

            System.Array.Sort(sortingArray, (a, b) => b.dist.CompareTo(a.dist));
            return sortingArray;
        }

        private (Vector3[] vertices, Vector3[] centerPositions, Vector3[] vertexColors, 
                 Vector4[] vertexOpacities, Vector3[] vertexCov0_3, Vector3[] vertexCov3_6, 
                 int[] indices) 
            CreateLODMeshArrays((int index, float dist)[] sortingArray,
                Vector3[] posHighArray, Vector3[] colHighArray, float[] opHighArray, Vector3[] cov0_3HighArray, Vector3[] cov3_6HighArray,
                Vector3[] posMedArray, Vector3[] colMedArray, float[] opMedArray, Vector3[] cov0_3MedArray, Vector3[] cov3_6MedArray,
                Vector3[] posLowArray, Vector3[] colLowArray, float[] opLowArray, Vector3[] cov0_3LowArray, Vector3[] cov3_6LowArray,
                Vector3[] posLegacyArray, Vector3[] colLegacyArray, float[] opLegacyArray, Vector3[] cov0_3LegacyArray, Vector3[] cov3_6LegacyArray,
                bool enableCroppingParam, Vector3 cropBoundsMinParam, Vector3 cropBoundsMaxParam, Vector3 cropBoundsRotationParam,
                Vector3 cameraPos, Matrix4x4 worldMatrix)
        {
            int highCount = posHighArray?.Length ?? 0;
            int mediumCount = posMedArray?.Length ?? 0;
            int lowCount = posLowArray?.Length ?? 0;
            int legacyCount = posLegacyArray?.Length ?? 0;

            int mediumStartIndex = highCount;
            int lowStartIndex = highCount + mediumCount;
            int legacyStartIndex = highCount + mediumCount + lowCount;

            float lodDistanceHighSqr = lodDistanceHigh * lodDistanceHigh;
            float lodDistanceMediumSqr = lodDistanceMedium * lodDistanceMedium;

            int[] levelForBand = GetLevelForEachBand(highCount > 0, mediumCount > 0, lowCount > 0);

            var validSplats = new System.Collections.Generic.List<(Vector3 position, Vector3 color, float opacity, Vector3 cov0_3, Vector3 cov3_6)>();

            for (int i = 0; i < sortingArray.Length; i++)
            {
                int splatIndex = sortingArray[i].index;
                Vector3 splatPosition;
                Vector3 splatColor;
                float splatOpacity;
                Vector3 splatCov0_3;
                Vector3 splatCov3_6;

                if (splatIndex < highCount && posHighArray != null)
                {
                    splatPosition = posHighArray[splatIndex];
                    splatColor = colHighArray[splatIndex];
                    splatOpacity = opHighArray[splatIndex];
                    splatCov0_3 = cov0_3HighArray[splatIndex];
                    splatCov3_6 = cov3_6HighArray[splatIndex];
                }
                else if (splatIndex < mediumStartIndex + mediumCount && posMedArray != null)
                {
                    int mediumIndex = splatIndex - mediumStartIndex;
                    splatPosition = posMedArray[mediumIndex];
                    splatColor = colMedArray[mediumIndex];
                    if (enableLODDebugColoring)
                    {
                        splatColor = new Vector3(splatColor.x * 0.7f, splatColor.y * 0.7f, splatColor.z * 1.5f);
                    }
                    splatOpacity = opMedArray[mediumIndex];
                    splatCov0_3 = cov0_3MedArray[mediumIndex];
                    splatCov3_6 = cov3_6MedArray[mediumIndex];
                }
                else if (splatIndex < lowStartIndex + lowCount && posLowArray != null)
                {
                    int lowIndex = splatIndex - lowStartIndex;
                    splatPosition = posLowArray[lowIndex];
                    splatColor = colLowArray[lowIndex];
                    if (enableLODDebugColoring)
                    {
                        splatColor = new Vector3(splatColor.x * 1.5f, splatColor.y * 0.7f, splatColor.z * 0.7f);
                    }
                    splatOpacity = opLowArray[lowIndex];
                    splatCov0_3 = cov0_3LowArray[lowIndex];
                    splatCov3_6 = cov3_6LowArray[lowIndex];
                }
                else if (splatIndex < legacyStartIndex + legacyCount && posLegacyArray != null)
                {
                    int legacyIndex = splatIndex - legacyStartIndex;
                    splatPosition = posLegacyArray[legacyIndex];
                    splatColor = colLegacyArray[legacyIndex];
                    splatOpacity = opLegacyArray[legacyIndex];
                    splatCov0_3 = cov0_3LegacyArray[legacyIndex];
                    splatCov3_6 = cov3_6LegacyArray[legacyIndex];
                }
                else
                {
                    continue;
                }

                Vector3 flippedPosition = splatPosition;
                flippedPosition.x = -flippedPosition.x;
            
                if (!IsPositionWithinCropBounds(flippedPosition, enableCroppingParam, cropBoundsMinParam, cropBoundsMaxParam, cropBoundsRotationParam))
                    continue;

                Vector3 worldPos = worldMatrix.MultiplyPoint3x4(flippedPosition);
                float distanceSqr = (worldPos - cameraPos).sqrMagnitude;

                bool shouldRender = false;

                if (splatIndex < lowStartIndex + lowCount)
                {
                    int splatLevel = splatIndex < highCount ? 0 : splatIndex < mediumStartIndex + mediumCount ? 1 : 2;
                    int band = distanceSqr <= lodDistanceHighSqr ? 0 : distanceSqr <= lodDistanceMediumSqr ? 1 : 2;
                    shouldRender = levelForBand[band] == splatLevel;
                }
                else
                {
                    shouldRender = true;
                }

                if (shouldRender)
                {
                    Vector3 flippedCov0_3 = splatCov0_3;
                    Vector3 flippedCov3_6 = splatCov3_6;
                    flippedCov0_3.y = -flippedCov0_3.y;
                    flippedCov0_3.z = -flippedCov0_3.z;
                
                    validSplats.Add((flippedPosition, splatColor, splatOpacity, flippedCov0_3, flippedCov3_6));
                }
            }

            int validSplatCount = validSplats.Count;
            int vertexCount = validSplatCount * 4;
        
            Vector3[] vertices = new Vector3[vertexCount];
            Vector3[] centerPositions = new Vector3[vertexCount];
            Vector3[] vertexColors = new Vector3[vertexCount];
            Vector4[] vertexOpacities = new Vector4[vertexCount];
            Vector3[] vertexCov0_3 = new Vector3[vertexCount];
            Vector3[] vertexCov3_6 = new Vector3[vertexCount];

            for (int i = 0; i < validSplatCount; i++)
            {
                var splat = validSplats[i];
                int baseIndex = i * 4;
            
                vertices[baseIndex + 0] = new Vector3(-1, -1, 0);
                vertices[baseIndex + 1] = new Vector3( 1, -1, 0);
                vertices[baseIndex + 2] = new Vector3(-1,  1, 0);
                vertices[baseIndex + 3] = new Vector3( 1,  1, 0);

                for (int j = 0; j < 4; j++)
                {
                    centerPositions[baseIndex + j] = splat.position;
                    vertexColors[baseIndex + j] = splat.color;
                    vertexOpacities[baseIndex + j] = new Vector4(splat.opacity, 0, 0, 0);
                    vertexCov0_3[baseIndex + j] = splat.cov0_3;
                    vertexCov3_6[baseIndex + j] = splat.cov3_6;
                }
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
    
        public int GetVisibleSplatCount()
        {
            int totalCount = 0;
        
            if (positionsHigh != null)
            {
                totalCount += positionsHigh.Length;
                if (enableCropping)
                {
                    int visibleCount = 0;
                    for (int i = 0; i < positionsHigh.Length; i++)
                    {
                        Vector3 pos = positionsHigh[i];
                        pos.x = -pos.x;
                        if (IsPositionWithinCropBounds(pos))
                            visibleCount++;
                    }
                    totalCount = visibleCount;
                }
            }
        
            if (positionsMedium != null)
            {
                if (enableCropping)
                {
                    for (int i = 0; i < positionsMedium.Length; i++)
                    {
                        Vector3 pos = positionsMedium[i];
                        pos.x = -pos.x;
                        if (IsPositionWithinCropBounds(pos))
                            totalCount++;
                    }
                }
                else
                {
                    totalCount += positionsMedium.Length;
                }
            }
        
            if (positionsLow != null)
            {
                if (enableCropping)
                {
                    for (int i = 0; i < positionsLow.Length; i++)
                    {
                        Vector3 pos = positionsLow[i];
                        pos.x = -pos.x;
                        if (IsPositionWithinCropBounds(pos))
                            totalCount++;
                    }
                }
                else
                {
                    totalCount += positionsLow.Length;
                }
            }
        
            if (positions != null && positionsHigh == null && positionsMedium == null && positionsLow == null)
            {
                if (!enableCropping) return positions.Length;
            
                int count = 0;
                for (int i = 0; i < positions.Length; i++)
                {
                    Vector3 pos = positions[i];
                    pos.x = -pos.x;
                    if (IsPositionWithinCropBounds(pos))
                        count++;
                }
                totalCount = count;
            }
        
            return totalCount;
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
