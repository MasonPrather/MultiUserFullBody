/*
 * Script Name: M_MetaAvatarInput.cs
 * Description: Feeds the existing OVR rig into Meta Avatars in avatar-local floor coordinates.
 * Project Role: Head, hand, controller and tracked-finger input for the Fall 2026 avatar.
 */
using Oculus.Avatar2;
using UnityEngine;
using UnityEngine.XR;

namespace MUFB.Avatars
{
    public sealed class M_MetaAvatarInput : OvrAvatarInputManager
    {
        private Transform _head;
        private Transform _left;
        private Transform _right;
        private Transform _origin;
        private float _nextTrackingAttempt;

        public void Bind(OVRCameraRig rig, Transform origin)
        {
            Bind(rig.centerEyeAnchor, rig.leftHandAnchor, rig.rightHandAnchor, origin);
        }

        public void Bind(Transform head, Transform left, Transform right, Transform origin)
        {
            _head = head;
            _left = left;
            _right = right;
            _origin = origin;
        }

        public bool HasTrackedPose
        {
            get
            {
                var device = InputDevices.GetDeviceAtXRNode(XRNode.Head);
                return _head && _origin && device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) &&
                    tracked && _origin.InverseTransformPoint(_head.position).y > 0.2f;
            }
        }

        protected override void OnTrackingInitialized()
        {
            // OVRManager requests permissions; unavailable tracking simply leaves these providers idle.
            TryStartFaceAndEyeTracking();
            _inputTrackingProvider = new OvrAvatarInputTrackingDelegatedProvider(new RigTracking(this));
            _inputControlProvider = new OvrAvatarInputControlDelegatedProvider(new SampleInputControlDelegate());
        }

        private void Update()
        {
            // Permissions can be granted after the avatar initializes, or tracking can stop on suspend.
            if (!_trackingInitialized || Time.unscaledTime < _nextTrackingAttempt) return;
            _nextTrackingAttempt = Time.unscaledTime + 2f;
            TryStartFaceAndEyeTracking();
        }

        private static void TryStartFaceAndEyeTracking()
        {
            if (OVRPlugin.faceTrackingSupported && !OVRPlugin.faceTrackingEnabled) OVRPlugin.StartFaceTracking();
            if (OVRPlugin.eyeTrackingSupported && !OVRPlugin.eyeTrackingEnabled) OVRPlugin.StartEyeTracking();
        }

        private sealed class RigTracking : OvrAvatarInputTrackingDelegate
        {
            // Resolve bindings when sampled: SDK provider initialization may precede rig binding.
            private readonly M_MetaAvatarInput _input;
            public RigTracking(M_MetaAvatarInput input) { _input = input; }

            public override bool GetRawInputTrackingState(out OvrAvatarInputTrackingState state)
            {
                state = default;
                if (!_input.HasTrackedPose || !_input._left || !_input._right) return false;
                // Without a tracked headset, don't calibrate the crouch solver against a zero-height pose.
                if (!OVRManager.isHmdPresent) return false;
                bool usingHands = OVRInput.GetActiveController() == OVRInput.Controller.Hands;
                state.headsetActive = true;
                state.leftControllerActive = !usingHands && OVRInput.GetControllerOrientationTracked(OVRInput.Controller.LTouch);
                state.rightControllerActive = !usingHands && OVRInput.GetControllerOrientationTracked(OVRInput.Controller.RTouch);
                state.leftControllerVisible = false;
                state.rightControllerVisible = false;
                state.headset = LocalPose(_input._head);
                state.leftController = LocalPose(_input._left);
                state.rightController = LocalPose(_input._right);
                return true;
            }

            private CAPI.ovrAvatar2Transform LocalPose(Transform anchor)
            {
                // The SDK delegate converts Unity handedness itself. Only remove the player origin here.
                return new CAPI.ovrAvatar2Transform
                {
                    position = _input._origin.InverseTransformPoint(anchor.position),
                    orientation = Quaternion.Inverse(_input._origin.rotation) * anchor.rotation,
                    scale = Vector3.one
                };
            }
        }
    }
}
