/*
 * Script Name: M_MetaAvatarRig.cs
 * Description: Creates one local Meta avatar on the existing floor-level player rig.
 * Project Role: Avatar service startup and local avatar lifetime, including Vivox-driven lip sync.
 */
using Oculus.Avatar2;
using Unity.Services.Vivox.AudioTaps;
using Unity.Services.Vivox;
using UnityEngine;

namespace MUFB.Avatars
{
    public sealed class M_MetaAvatarRig : MonoBehaviour
    {
        public static M_MetaAvatarRig Instance { get; private set; }
        [SerializeField] private OVRCameraRig cameraRig;
        [SerializeField] private Transform avatarRoot;
        [SerializeField] private Transform head;
        [SerializeField] private Transform leftHand;
        [SerializeField] private Transform rightHand;
        private GameObject _speech;
        private float _nextHealthLog;
        private M_MetaAvatarInput _input;
        public Transform Head => head ? head : cameraRig ? cameraRig.centerEyeAnchor : null;
        public Transform LeftHand => leftHand ? leftHand : cameraRig ? cameraRig.leftHandAnchor : null;
        public Transform RightHand => rightHand ? rightHand : cameraRig ? cameraRig.rightHandAnchor : null;
        [SerializeField] private GameObject servicesPrefab;
        [SerializeField] private M_MetaAvatarEntity localAvatarPrefab;
        [SerializeField] private Renderer[] interactionHandRenderers = System.Array.Empty<Renderer>();
        public M_MetaAvatarEntity LocalAvatar { get; private set; }
        public Transform Origin => avatarRoot;

        public static bool AvatarRuntimeAvailable
        {
            get
            {
#if UNITY_EDITOR_OSX
                // Match the SDK's native-library path. The distributed package does not
                // install this framework here, but the editor can still test networking.
                return System.IO.File.Exists(OvrAvatarPlugin.FullPluginFolderPath + "libovravatar2.framework/libovravatar2");
#else
                return true;
#endif
            }
        }

        private void Awake()
        {
            Instance = this;
            if (!AvatarRuntimeAvailable)
            {
                Debug.LogWarning("[MetaAvatar] Native avatar library is absent on this Mac. Networking remains available; avatar rendering and pose recording require Quest or a supported editor runtime.", this);
                enabled = false;
                return;
            }
            if (!OvrAvatarManager.hasInstance) Instantiate(servicesPrefab);
        }

        private void Start()
        {
            if (!Head || !avatarRoot || !localAvatarPrefab)
            {
                Debug.LogError("[MetaAvatar] Local avatar rig references are incomplete.", this);
                return;
            }
            LocalAvatar = Instantiate(localAvatarPrefab, avatarRoot, false);
            LocalAvatar.name = "Local Meta Avatar (Full Body)";
            // The camera is a sibling on our existing OVR rig, so the SDK cannot discover it
            // underneath the entity. Explicitly keep our first-person avatar updating every frame.
            if (AvatarLODManager.hasInstance)
                AvatarLODManager.Instance.firstPersonAvatarLod = LocalAvatar.AvatarLOD;
            var input = LocalAvatar.GetComponent<M_MetaAvatarInput>();
            _input = input;
            if (cameraRig) input.Bind(cameraRig, avatarRoot);
            else input.Bind(head, leftHand, rightHand, avatarRoot);

            // Use Vivox's existing microphone capture, avoiding a second competing Microphone.Start.
            // Vivox fills the AudioSource clip; the SDK filter consumes it without local playback.
            var speech = new GameObject("Meta Avatar Speech (Vivox)");
            speech.SetActive(false);
            speech.transform.SetParent(LocalAvatar.transform, false);
            var audio = speech.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 0f;
            speech.AddComponent<VivoxCaptureSourceTap>();
            var lipSync = speech.AddComponent<OvrAvatarLipSyncContext>();
            lipSync.AudioSampleRate = AudioSettings.outputSampleRate;
            lipSync.CaptureAudio = true;
            _speech = speech;
            LocalAvatar.Initialize(true, input, lipSync);
        }

        private void LateUpdate()
        {
            // Audio taps must bind to an existing voice channel, including after leave/rejoin.
            bool hasVoiceChannel = VivoxService.Instance != null && VivoxService.Instance.IsLoggedIn &&
                VivoxService.Instance.ActiveChannels.Count > 0;
            if (_speech && _speech.activeSelf != hasVoiceChannel) _speech.SetActive(hasVoiceChannel);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (LocalAvatar && Time.unscaledTime >= _nextHealthLog)
            {
                _nextHealthLog = Time.unscaledTime + 2f;
                var headJoint = LocalAvatar.HasJoints ? LocalAvatar.GetSkeletonTransform(CAPI.ovrAvatar2JointType.Head) : null;
                var foot = LocalAvatar.HasJoints && LocalAvatar.HasCriticalJoint(CAPI.ovrAvatar2JointType.LeftFootBall)
                    ? LocalAvatar.GetSkeletonTransform(CAPI.ovrAvatar2JointType.LeftFootBall) : null;
                var rightFoot = LocalAvatar.HasJoints && LocalAvatar.HasCriticalJoint(CAPI.ovrAvatar2JointType.RightFootBall)
                    ? LocalAvatar.GetSkeletonTransform(CAPI.ovrAvatar2JointType.RightFootBall) : null;
                var legs = LocalAvatar.GetComponentInChildren<MecanimLegsAnimationController>();
                Debug.Log($"[MetaAvatarLegs] tracked={_input && _input.HasTrackedPose} headY={avatarRoot.InverseTransformPoint(Head.position).y:F3} leftY={(foot ? avatarRoot.InverseTransformPoint(foot.position).y.ToString("F3") : "none")} rightY={(rightFoot ? avatarRoot.InverseTransformPoint(rightFoot.position).y.ToString("F3") : "none")} forced={(legs && legs.forceEnableCrouch)} strength={(legs ? legs.forceCrouchStrength : 0):F2}", this);
                Debug.Log($"[MetaAvatarHealth] tracked={_input && _input.HasTrackedPose} head={avatarRoot.InverseTransformPoint(Head.position):F2} joints={LocalAvatar.SkeletonJointCount} behavior={LocalAvatar.BehaviorSystemEnabled} poseValid={LocalAvatar.TrackingPoseValid} ready={LocalAvatar.ReadyToStream} headJoint={(headJoint ? avatarRoot.InverseTransformPoint(headJoint.position).ToString("F2") : "none")}", this);
            }
#endif
            // Keep hand data, colliders, grab poses and ray visuals active, but avoid a second hand mesh.
            bool showAvatarHands = LocalAvatar && LocalAvatar.ReadyToStream && _input && _input.HasTrackedPose;
            foreach (var hand in interactionHandRenderers)
                if (hand) hand.forceRenderingOff = showAvatarHands;
        }

        private void OnDestroy()
        {
            foreach (var hand in interactionHandRenderers)
                if (hand) hand.forceRenderingOff = false;
            if (Instance == this) Instance = null;
        }
    }
}
