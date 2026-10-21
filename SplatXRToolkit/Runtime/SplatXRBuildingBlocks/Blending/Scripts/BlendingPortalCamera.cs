using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using System.IO;

namespace SplatXR
{
    public class BlendingPortalCamera : MonoBehaviour
    {
        public Camera.StereoscopicEye targetStereoscopicEye;

        private RenderTexture camouflageTexture;
        private MaterialLibrary materialLibrary;
        private Camera centerEyeAnchorCamera;

        private float halfIPD = 0.03f;

        void Start()
        {
            materialLibrary = FindAnyObjectByType<MaterialLibrary>();
            if (materialLibrary == null || materialLibrary.portalMaterial == null)
            {
                Debug.LogError($"[BlendingPortalCamera] {name}: no MaterialLibrary with a portalMaterial found in the scene. Add the SplatBlendingPortal prefab.", this);
                enabled = false;
                return;
            }

            centerEyeAnchorCamera = transform.parent != null ? transform.parent.GetComponentInParent<Camera>() : null;
            if (centerEyeAnchorCamera == null) centerEyeAnchorCamera = Camera.main;
            if (centerEyeAnchorCamera == null)
            {
                Debug.LogError($"[BlendingPortalCamera] {name}: no XR camera found. Parent this object under the CenterEyeAnchor camera.", this);
                enabled = false;
                return;
            }

            setEyePosition();

            camouflageTexture = makeRenderTexture();

            camouflageTexture.antiAliasing = Mathf.Max(1, QualitySettings.antiAliasing);
            if (targetStereoscopicEye == Camera.StereoscopicEye.Left)
            {
                materialLibrary.portalMaterial.SetTexture("_LeftEyeTex", camouflageTexture);
            }
            else if (targetStereoscopicEye == Camera.StereoscopicEye.Right)
            {
                materialLibrary.portalMaterial.SetTexture("_RightEyeTex", camouflageTexture);
            }
            else
            {
                throw new InvalidOperationException();
            }

            if (!XRSettings.enabled)
            {
                GetComponent<Camera>().projectionMatrix = centerEyeAnchorCamera.projectionMatrix;
            }
            else if (TryGetEyeProjection(out Matrix4x4 projection))
            {
                GetComponent<Camera>().projectionMatrix = projection;
            }
            GetComponent<Camera>().targetTexture = camouflageTexture;
        }

        private RenderTexture makeRenderTexture()
        {
            RenderTexture renderTexture;
            if (XRSettings.enabled)
            {
                var d = XRSettings.eyeTextureDesc;
          
                renderTexture = new RenderTexture(1680, 1760, 24); 
            }
            else
            {
                renderTexture = new RenderTexture(488, 512, 24);
            }

            return renderTexture;
        }

        void LateUpdate()
    {
        string eyeTexProperty = targetStereoscopicEye == Camera.StereoscopicEye.Left ? "_LeftEyeTex" : "_RightEyeTex";
        if (materialLibrary.portalMaterial.GetTexture(eyeTexProperty) != camouflageTexture)
        {
            materialLibrary.portalMaterial.SetTexture(eyeTexProperty, camouflageTexture);
        }

        if (XRSettings.enabled && TryGetEyeProjection(out Matrix4x4 projection))
        {
            GetComponent<Camera>().projectionMatrix = projection;
        }

        GetComponent<Camera>().aspect = (float)camouflageTexture.width / (float)camouflageTexture.height;

    }

        private static readonly List<XRDisplaySubsystem> displaySubsystems = new List<XRDisplaySubsystem>();

        private bool TryGetEyeProjection(out Matrix4x4 projection)
        {
            int eye = targetStereoscopicEye == Camera.StereoscopicEye.Left ? 0 : 1;

            SubsystemManager.GetSubsystems(displaySubsystems);
            foreach (var display in displaySubsystems)
            {
                if (!display.running) continue;

                int passCount = display.GetRenderPassCount();
                if (passCount == 0) continue;

                display.GetRenderPass(passCount == 1 ? 0 : Mathf.Min(eye, passCount - 1), out XRDisplaySubsystem.XRRenderPass pass);
                int paramIndex = pass.GetRenderParameterCount() > 1 ? eye : 0;
                pass.GetRenderParameter(centerEyeAnchorCamera, paramIndex, out XRDisplaySubsystem.XRRenderParameter parameter);

                projection = parameter.projection;
                if (IsValidProjection(projection)) return true;
            }

            if (centerEyeAnchorCamera.stereoEnabled)
            {
                projection = centerEyeAnchorCamera.GetStereoProjectionMatrix(targetStereoscopicEye);
                if (IsValidProjection(projection)) return true;
            }

            projection = default;
            return false;
        }

        private static bool IsValidProjection(Matrix4x4 m)
        {
            return m.m00 != 0f && m.m11 != 0f && !float.IsNaN(m.m00) && !float.IsNaN(m.m11);
        }

        void OnDisable()
        {
            if (materialLibrary == null || materialLibrary.portalMaterial == null) return;

            string eyeTexProperty = targetStereoscopicEye == Camera.StereoscopicEye.Left ? "_LeftEyeTex" : "_RightEyeTex";
            materialLibrary.portalMaterial.SetTexture(eyeTexProperty, null);
        }

        void OnDestroy()
        {
            if (camouflageTexture == null) return;

            Camera portalCamera = GetComponent<Camera>();
            if (portalCamera != null && portalCamera.targetTexture == camouflageTexture)
            {
                portalCamera.targetTexture = null;
            }

            camouflageTexture.Release();
            Destroy(camouflageTexture);
            camouflageTexture = null;
        }

        private void setEyePosition()
        {
            if (targetStereoscopicEye == Camera.StereoscopicEye.Left)
            {
                transform.localPosition = new Vector3(-1 * halfIPD, 0, 0);
            }
            else if (targetStereoscopicEye == Camera.StereoscopicEye.Right)
            {
                transform.localPosition = new Vector3(halfIPD, 0, 0);
            }
        }


        public void SaveRenderTextureToFile(RenderTexture renderTextureToSave, string filePath)
        {
            RenderTexture currentRT = RenderTexture.active;
            RenderTexture.active = renderTextureToSave;

            Texture2D texture = new Texture2D(renderTextureToSave.width, renderTextureToSave.height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, renderTextureToSave.width, renderTextureToSave.height), 0, 0);
            texture.Apply();

            RenderTexture.active = currentRT;

            byte[] textureBytes = texture.EncodeToJPG();
            string fullPath = Path.Combine(Application.persistentDataPath, filePath);
            File.WriteAllBytes(fullPath, textureBytes);
        }

        public void SaveTextureToFile(Texture2D textureToSave, string filePath)
        {
            byte[] textureBytes = textureToSave.EncodeToJPG();
            string fullPath = Path.Combine(Application.persistentDataPath, filePath);
            File.WriteAllBytes(fullPath, textureBytes);
        }


    }
}