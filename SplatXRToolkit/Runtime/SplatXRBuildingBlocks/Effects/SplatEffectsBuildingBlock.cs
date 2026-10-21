using UnityEngine;

namespace SplatXR
{
    public class SplatEffectsBuildingBlock : MonoBehaviour
    {
        [Header("Effect Material")]
        [Tooltip("Material that contains the effect shader")]
        public Material effectMaterial;

        [Header("Effect Control")]
        [Tooltip("Whether the effect is currently active")]
        public bool isEffectActive = false;

        [Header("Time Properties")]
        [Tooltip("Current effect time (automatically managed when active)")]
        [SerializeField] private float effectTime = 0f;

        [Tooltip("Speed multiplier for effect progression")]
        public float timeMultiplier = 1.0f;

        [Tooltip("Whether to loop the effect")]
        public bool loopEffect = false;

        public float EffectTime => effectTime;

        public bool IsActive => isEffectActive;

        void Start()
        {
            InitializeEffect();
        }

        void Update()
        {
            UpdateEffect();
        }

        private void InitializeEffect()
        {
            if (effectMaterial == null)
            {
                Debug.LogWarning($"[SplatEffectsBuildingBlock] No effect material assigned to {gameObject.name}");
                return;
            }

            ApplyEffectToMaterial();

        }

        private void UpdateEffect()
        {
            if (effectMaterial == null) return;

            if (isEffectActive)
            {
                UpdateEffectTime();
            }

            ApplyEffectToMaterial();
        }

        private void UpdateEffectTime()
        {
            effectTime += Time.deltaTime * timeMultiplier;

            if (loopEffect)
            {
                effectTime = effectTime % 1.0f;
            }
            else
            {
                effectTime = Mathf.Clamp01(effectTime);
            }
        }

        private void ApplyEffectToMaterial()
        {
            if (effectMaterial == null) return;

            effectMaterial.SetFloat("_EffectActive", isEffectActive ? 1f : 0f);
            effectMaterial.SetFloat("_EffectTime", effectTime);
        }


        #region Public API Methods

        public void StartEffect()
        {
            isEffectActive = true;
        }

        public void StopEffect()
        {
            isEffectActive = false;
        }

        public void SetTime(float time)
        {
            effectTime = Mathf.Clamp01(time);
        }

        public void SetLoop(bool loop)
        {
            loopEffect = loop;
        }

        #endregion

        #region Unity Editor Support

    #if UNITY_EDITOR
        void OnValidate()
        {
            timeMultiplier = Mathf.Max(0.1f, timeMultiplier);
            effectTime = Mathf.Clamp01(effectTime);
            ApplyEffectToMaterial();
        }
    #endif

        #endregion
    }
}
