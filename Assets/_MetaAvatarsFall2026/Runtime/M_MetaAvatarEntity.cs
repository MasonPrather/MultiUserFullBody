/*
 * Script Name: M_MetaAvatarEntity.cs
 * Description: Loads the bundled Meta preset as a full-body local or streamed remote avatar.
 * Project Role: Replaces RPM mesh loading and FinalIK for MUFBAvatars_Fall2026.
 */
using System.Collections;
using Oculus.Avatar2;
using UnityEngine;

namespace MUFB.Avatars
{
    public sealed class M_MetaAvatarEntity : OvrAvatarEntity
    {
        private bool _configured;
        private OvrAvatarInputManagerBehavior _tracking;
        private OvrAvatarLipSyncBehavior _speech;
        protected override bool CreateEntityOnAwake => false;

        // Remote rendering starts only after a stream is applied; waiting for visible/model state
        // here can prevent that first pose from ever reaching the SDK.
        public bool ReadyToStream => IsCreated && HasJoints && (!IsLocal || (HasNonDefaultAvatar && BehaviorSystemEnabled));

        public void Initialize(bool local, OvrAvatarInputManagerBehavior tracking = null, OvrAvatarLipSyncBehavior speech = null)
        {
            if (_configured) return;
            _configured = true;
            _tracking = tracking;
            _speech = speech;
            SetIsLocal(local);
            OnUserAvatarLoadedEvent.AddListener(entity => Debug.Log($"[MetaAvatar] Model loaded: local={IsLocal}, joints={SkeletonJointCount}, behavior={BehaviorSystemEnabled}", this));
            StartCoroutine(CreateAndLoad());
        }

        private IEnumerator CreateAndLoad()
        {
            float deadline = Time.realtimeSinceStartup + 30f;
            while (!OvrAvatarManager.initialized || OvrAvatarManager.Instance.ShaderManager == null ||
                   !OvrAvatarManager.Instance.ShaderManager.Initialized)
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    Debug.LogError("[MetaAvatar] Avatar services failed to initialize within 30 seconds.", this);
                    yield break;
                }
                yield return null;
            }
            SetInitialManifestatation(CAPI.ovrAvatar2EntityManifestationFlags.Full);
            CreateEntity();
            if (!IsCreated) yield break;
            string platform = OvrAvatarManager.IsAndroidStandalone ? "quest" : "rift";
            if (!LoadAssetsFromStreamingAssets(new[] { $"MUFBAvatars_Fall2026/7_{platform}_light.glb.zst" }))
                Debug.LogError("[MetaAvatar] Bundled Meta preset could not be loaded.", this);
        }

        protected override CAPI.ovrAvatar2EntityCreateInfo? ConfigureCreationInfo()
        {
            var info = new CAPI.ovrAvatar2EntityCreateInfo
            {
                features = IsLocal ? CAPI.ovrAvatar2EntityFeatures.Preset_Default : CAPI.ovrAvatar2EntityFeatures.Preset_Remote,
                renderFilters = CAPI.OvrAvatar2_DefaultLoadFilters
            };
            info.renderFilters.manifestationFlags = CAPI.ovrAvatar2EntityManifestationFlags.Full;
            info.renderFilters.viewFlags = CAPI.ovrAvatar2EntityViewFlags.All;
            info.renderFilters.quality = CAPI.ovrAvatar2EntityQuality.Light;
            return info;
        }

        protected override void ConfigureEntity()
        {
            SetActiveManifestation(CAPI.ovrAvatar2EntityManifestationFlags.Full);
            SetActiveView(IsLocal ? CAPI.ovrAvatar2EntityViewFlags.FirstPerson : CAPI.ovrAvatar2EntityViewFlags.ThirdPerson);
            if (IsLocal)
            {
                SetInputManager(_tracking);
                SetFacePoseProvider(GetComponent<OvrAvatarFaceTrackingBehaviorOvrPlugin>());
                SetEyePoseProvider(GetComponent<OvrAvatarEyeTrackingBehaviorOvrPlugin>());
                SetLipSync(_speech);
            }
            else SetPlaybackTimeDelay(0.12f);
        }
    }
}
