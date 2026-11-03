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

        private float lastSortTime = 0f;
    
        private TextAsset lastCsvFileHigh;
        private TextAsset lastCsvFileMedium;
        private TextAsset lastCsvFileLow;
        private TextAsset lastCsvFile;

        private System.Threading.Tasks.Task<MeshUpdate> sortingTask;
        [System.NonSerialized] private bool isSorting = false;

        // The splats of every level currently in the mesh's vertex buffer ("slots") and the reusable sort buffers.
        [System.NonSerialized] private Vector3[] meshSourcePositionsHigh;
        [System.NonSerialized] private Vector3[] meshSourcePositionsMedium;
        [System.NonSerialized] private Vector3[] meshSourcePositionsLow;
        [System.NonSerialized] private SplatCropBox meshCrop;
        [System.NonSerialized] private bool meshDebugColoring;
        [System.NonSerialized] private Vector3[] slotCenters;
        [System.NonSerialized] private byte[] slotLevels;
        [System.NonSerialized] private int slotCount;
        [System.NonSerialized] private Bounds slotBounds;
        [System.NonSerialized] private float[] sortKeys;
        [System.NonSerialized] private int[] sortSlots;
        [System.NonSerialized] private int[] sortIndices;

        private static readonly int ScaleFactorId = Shader.PropertyToID("_ScaleFactor");

        // Result of a background mesh update. vertices is null when the vertex buffer didn't need rebuilding.
        private sealed class MeshUpdate
        {
            public Vector3[] sourcePositionsHigh;
            public Vector3[] sourcePositionsMedium;
            public Vector3[] sourcePositionsLow;
            public SplatCropBox crop;
            public bool debugColoring;
            public SplatQuads.Vertex[] vertices;
            public Vector3[] slotCenters;
            public byte[] slotLevels;
            public int slotCount;
            public Bounds slotBounds;
            public float[] sortKeys;
            public int[] sortSlots;
            public int[] sortIndices;
            public int drawCount;
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
            mesh = SplatQuads.CreateMesh();
            GetComponent<MeshFilter>().mesh = mesh;
            meshSourcePositionsHigh = meshSourcePositionsMedium = meshSourcePositionsLow = null;


            var meshRenderer = GetComponent<MeshRenderer>();
            if (material != null)
            {
                meshRenderer.sharedMaterial = material;
            }

            meshRenderer.forceRenderingOff = false;
            meshRenderer.allowOcclusionWhenDynamic = false;
            meshRenderer.bounds = new Bounds(Vector3.zero, Vector3.one * 10000f);

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

            if (Time.time - lastSortTime >= sortInterval && !isSorting)
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

            if (!hasLODData || mesh == null) return;

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
            if (update.sourcePositionsHigh != positionsHigh ||
                update.sourcePositionsMedium != positionsMedium ||
                update.sourcePositionsLow != positionsLow)
            {
                lastSortTime = float.NegativeInfinity;
                return;
            }

            ApplyMeshUpdate(update);
        }

        // Snapshots the inputs on the main thread, then on a background thread picks the level of detail for each
        // splat and sorts the picked splats (first rebuilding the vertex data, which holds every level, if the
        // splats, crop bounds or debug coloring changed). Unity's Mesh API is only used in ApplyMeshUpdate.
        private void StartMeshUpdate()
        {
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

            var crop = new SplatCropBox(enableCropping, cropBoundsMin, cropBoundsMax, cropBoundsRotation);
            bool debugColoring = enableLODDebugColoring;
            bool rebuildVertices = meshSourcePositionsHigh != currentPositionsHigh ||
                                   meshSourcePositionsMedium != currentPositionsMedium ||
                                   meshSourcePositionsLow != currentPositionsLow ||
                                   !crop.SelectsSameAs(meshCrop) ||
                                   debugColoring != meshDebugColoring ||
                                   mesh.vertexCount != slotCount * 4;

            int[] levelForBand = GetLevelForEachBand((currentPositionsHigh?.Length ?? 0) > 0,
                                                     (currentPositionsMedium?.Length ?? 0) > 0,
                                                     (currentPositionsLow?.Length ?? 0) > 0);
            float lodDistanceHighSqr = lodDistanceHigh * lodDistanceHigh;
            float lodDistanceMediumSqr = lodDistanceMedium * lodDistanceMedium;

            var currentSlotCenters = slotCenters;
            var currentSlotLevels = slotLevels;
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
                var update = new MeshUpdate
                {
                    sourcePositionsHigh = currentPositionsHigh,
                    sourcePositionsMedium = currentPositionsMedium,
                    sourcePositionsLow = currentPositionsLow,
                    crop = crop,
                    debugColoring = debugColoring,
                };

                if (rebuildVertices)
                {
                    BuildSlots(update,
                        currentPositionsHigh, currentColorsHigh, currentOpacitiesHigh, currentCovariances0_3High, currentCovariances3_6High,
                        currentPositionsMedium, currentColorsMedium, currentOpacitiesMedium, currentCovariances0_3Medium, currentCovariances3_6Medium,
                        currentPositionsLow, currentColorsLow, currentOpacitiesLow, currentCovariances0_3Low, currentCovariances3_6Low);
                }
                else
                {
                    update.slotCenters = currentSlotCenters;
                    update.slotLevels = currentSlotLevels;
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
                var levels = update.slotLevels;
                int drawCount = 0;
                for (int slot = 0; slot < count; slot++)
                {
                    float distanceSqr = (worldMatrix.MultiplyPoint3x4(centers[slot]) - cameraPos).sqrMagnitude;
                    int band = distanceSqr <= lodDistanceHighSqr ? 0 : distanceSqr <= lodDistanceMediumSqr ? 1 : 2;
                    if (levelForBand[band] != levels[slot]) continue;

                    keys[drawCount] = -distanceSqr;
                    slots[drawCount] = slot;
                    drawCount++;
                }
                SplatQuads.SortAndWriteIndices(keys, slots, drawCount, indices);

                update.sortKeys = keys;
                update.sortSlots = slots;
                update.sortIndices = indices;
                update.drawCount = drawCount;
                return update;
            });
        }

        private static void BuildSlots(MeshUpdate update,
            Vector3[] posHighArray, Vector3[] colHighArray, float[] opHighArray, Vector3[] cov0_3HighArray, Vector3[] cov3_6HighArray,
            Vector3[] posMedArray, Vector3[] colMedArray, float[] opMedArray, Vector3[] cov0_3MedArray, Vector3[] cov3_6MedArray,
            Vector3[] posLowArray, Vector3[] colLowArray, float[] opLowArray, Vector3[] cov0_3LowArray, Vector3[] cov3_6LowArray)
        {
            int capacity = (posHighArray?.Length ?? 0) + (posMedArray?.Length ?? 0) + (posLowArray?.Length ?? 0);
            var vertices = new SplatQuads.Vertex[capacity * 4];
            var centers = new Vector3[capacity];
            var levels = new byte[capacity];
            int count = 0;

            var crop = update.crop;
            Vector3 mediumTint = update.debugColoring ? new Vector3(0.7f, 0.7f, 1.5f) : Vector3.one;
            Vector3 lowTint = update.debugColoring ? new Vector3(1.5f, 0.7f, 0.7f) : Vector3.one;

            AddLevel(0, posHighArray, colHighArray, opHighArray, cov0_3HighArray, cov3_6HighArray, Vector3.one);
            AddLevel(1, posMedArray, colMedArray, opMedArray, cov0_3MedArray, cov3_6MedArray, mediumTint);
            AddLevel(2, posLowArray, colLowArray, opLowArray, cov0_3LowArray, cov3_6LowArray, lowTint);

            update.vertices = vertices;
            update.slotCenters = centers;
            update.slotLevels = levels;
            update.slotCount = count;
            update.slotBounds = SplatQuads.ComputeBounds(centers, count);

            void AddLevel(byte level, Vector3[] posArray, Vector3[] colArray, float[] opArray, Vector3[] cov0_3Array, Vector3[] cov3_6Array, Vector3 tint)
            {
                if (posArray == null) return;

                for (int i = 0; i < posArray.Length; i++)
                {
                    Vector3 splatPosition = posArray[i];
                    splatPosition.x = -splatPosition.x;
                    if (!crop.Contains(splatPosition)) continue;

                    centers[count] = SplatQuads.WriteSlot(vertices, count, posArray[i], Vector3.Scale(colArray[i], tint), opArray[i],
                                                          cov0_3Array[i], cov3_6Array[i]);
                    levels[count] = level;
                    count++;
                }
            }
        }

        private void ApplyMeshUpdate(MeshUpdate update)
        {
            if (update.vertices != null)
            {
                SplatQuads.UploadVertices(mesh, update.vertices, update.slotCount);
                meshSourcePositionsHigh = update.sourcePositionsHigh;
                meshSourcePositionsMedium = update.sourcePositionsMedium;
                meshSourcePositionsLow = update.sourcePositionsLow;
                meshCrop = update.crop;
                meshDebugColoring = update.debugColoring;
                slotCenters = update.slotCenters;
                slotLevels = update.slotLevels;
                slotCount = update.slotCount;
                slotBounds = update.slotBounds;
            }

            sortKeys = update.sortKeys;
            sortSlots = update.sortSlots;
            sortIndices = update.sortIndices;
            SplatQuads.UploadIndices(mesh, sortIndices, update.drawCount, slotBounds);
        }

        // The splat shaders scale centers by _ScaleFactor in object space, so sort and pick detail levels in that space too.
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
    
        public int GetVisibleSplatCount()
        {
            var crop = new SplatCropBox(enableCropping, cropBoundsMin, cropBoundsMax, cropBoundsRotation);
            return CountInside(positionsHigh, crop) + CountInside(positionsMedium, crop) + CountInside(positionsLow, crop);
        }

        private static int CountInside(Vector3[] levelPositions, SplatCropBox crop)
        {
            if (levelPositions == null) return 0;
            if (!crop.enabled) return levelPositions.Length;

            int count = 0;
            for (int i = 0; i < levelPositions.Length; i++)
            {
                Vector3 pos = levelPositions[i];
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
