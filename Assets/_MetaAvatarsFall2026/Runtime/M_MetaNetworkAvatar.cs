/*
 * Script Name: M_MetaNetworkAvatar.cs
 * Description: Relays Meta's full avatar animation stream through the existing NGO session.
 * Project Role: Replaces RPM URLs, three-point pose variables, and blendshape RPCs for the cloned scene.
 */
using System;
using Oculus.Avatar2;
using Unity.Netcode;
using UnityEngine;

namespace MUFB.Avatars
{
    public sealed class M_MetaNetworkAvatar : NetworkBehaviour
    {
        private const int MaxPacketBytes = 16384;
        [SerializeField] private M_MetaAvatarEntity remoteAvatarPrefab;
        [SerializeField, Range(5f, 30f)] private float sendRate = 15f;
        private M_MetaAvatarEntity _remote;
        private byte[] _recordBuffer = Array.Empty<byte>();
        private byte[] _pendingPacket;
        private float _nextSend;
        private float _nextHealthLog;
        private int _sent, _received, _applied;
        private int _lastPacketSize;
        private Vector3 _targetPosition;
        private Quaternion _targetRotation = Quaternion.identity;
        private bool _receivedRoot;
        private bool _warnedPacketSize;
#if UNITY_EDITOR
        private static bool _editorStreamEcho;
        private static byte[] _editorEchoPacket;
        private static Vector3 _editorEchoOrigin;
        private static Quaternion _editorEchoRotation;
        public static void SetEditorStreamEcho(bool enabled)
        {
            _editorStreamEcho = enabled;
            _editorEchoPacket = null;
        }
#endif

        public override void OnNetworkSpawn()
        {
            Debug.Log($"[MetaAvatarNet] Spawn owner={OwnerClientId} localOwner={IsOwner} object={NetworkObjectId}", this);
            if (!IsOwner && M_MetaAvatarRig.AvatarRuntimeAvailable)
            {
                _remote = Instantiate(remoteAvatarPrefab, transform, false);
                _remote.name = $"Remote Meta Avatar ({OwnerClientId})";
                _remote.Initialize(false);
            }
        }

        private void LateUpdate()
        {
            if (!IsSpawned) return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Time.unscaledTime >= _nextHealthLog)
            {
                _nextHealthLog = Time.unscaledTime + 10f;
                Debug.Log($"[MetaAvatarNet] owner={OwnerClientId} mine={IsOwner} sent={_sent} received={_received} applied={_applied} bytes={_lastPacketSize} remoteReady={(_remote && _remote.ReadyToStream)}", this);
            }
#endif
            if (IsOwner)
            {
#if UNITY_EDITOR
                // Opt-in round-trip test: receive a Quest stream and send it back as this
                // editor player's stream. This checks remote initialization on real hardware.
                if (_editorStreamEcho && !M_MetaAvatarRig.AvatarRuntimeAvailable)
                {
                    if (_editorEchoPacket == null || Time.unscaledTime < _nextSend) return;
                    _nextSend = Time.unscaledTime + 1f / Mathf.Clamp(sendRate, 5f, 30f);
                    _sent++;
                    _lastPacketSize = _editorEchoPacket.Length;
                    ReceivePoseRpc(_editorEchoPacket, _editorEchoOrigin, _editorEchoRotation);
                    return;
                }
#endif
                var rig = M_MetaAvatarRig.Instance;
                if (!rig || !rig.LocalAvatar || !rig.LocalAvatar.ReadyToStream) return;
                transform.SetPositionAndRotation(rig.Origin.position, rig.Origin.rotation);
                if (Time.unscaledTime < _nextSend) return;
                _nextSend = Time.unscaledTime + 1f / Mathf.Clamp(sendRate, 5f, 30f);
                uint count = rig.LocalAvatar.RecordStreamData_AutoBuffer(OvrAvatarEntity.StreamLOD.Medium, ref _recordBuffer);
                if (count == 0) return;
                if (count > MaxPacketBytes)
                {
                    if (!_warnedPacketSize) Debug.LogWarning("[MetaAvatar] Animation packet exceeds the 16 KiB safety limit.", this);
                    _warnedPacketSize = true;
                    return;
                }
                var packet = new byte[(int)count];
                Buffer.BlockCopy(_recordBuffer, 0, packet, 0, packet.Length);
                _sent++;
                _lastPacketSize = packet.Length;
                ReceivePoseRpc(packet, transform.position, transform.rotation);
            }
            else if (_remote)
            {
                if (_receivedRoot)
                {
                    float blend = 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime);
                    transform.SetPositionAndRotation(Vector3.Lerp(transform.position, _targetPosition, blend),
                        Quaternion.Slerp(transform.rotation, _targetRotation, blend));
                }
                if (_pendingPacket != null && _remote.ReadyToStream)
                {
                    if (_remote.ApplyStreamData(_pendingPacket))
                    {
                        if (_applied++ == 0) Debug.Log($"[MetaAvatarNet] First remote pose applied for player {OwnerClientId}.", this);
                        _pendingPacket = null;
                    }
                }
            }
        }

        // Universal owner-to-peers RPC works with PhonePhotoUpload's distributed authority
        // sessions as well as host/client sessions. ServerRpc/ClientRpc require a server.
        [Rpc(SendTo.NotMe, InvokePermission = RpcInvokePermission.Owner, Delivery = RpcDelivery.Reliable)]
        private void ReceivePoseRpc(byte[] packet, Vector3 origin, Quaternion rotation)
        {
            if (IsOwner || !ValidPacket(packet, origin, rotation)) return;
            _received++;
            _lastPacketSize = packet.Length;
            rotation = rotation.normalized;
#if UNITY_EDITOR
            if (_editorStreamEcho && !M_MetaAvatarRig.AvatarRuntimeAvailable)
            {
                _editorEchoPacket = packet;
                var head = transform.Find("Head");
                var forward = Vector3.ProjectOnPlane(head ? head.forward : rotation * Vector3.forward, Vector3.up);
                if (forward.sqrMagnitude < 0.01f) forward = rotation * Vector3.forward;
                _editorEchoOrigin = origin + forward.normalized * 1.5f;
                _editorEchoRotation = rotation;
            }
#endif
            _pendingPacket = packet; // Only keep the newest pose while the remote model loads.
            _targetPosition = origin;
            _targetRotation = rotation;
            if (!_receivedRoot) transform.SetPositionAndRotation(origin, rotation);
            _receivedRoot = true;
        }

        private static bool ValidPacket(byte[] packet, Vector3 p, Quaternion q)
        {
            float norm = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            return packet != null && packet.Length > 0 && packet.Length <= MaxPacketBytes &&
                Finite(p.x) && Finite(p.y) && Finite(p.z) &&
                Finite(q.x) && Finite(q.y) && Finite(q.z) && Finite(q.w) &&
                Finite(norm) && norm > 0.01f;
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        public override void OnNetworkDespawn()
        {
            if (_remote) Destroy(_remote.gameObject);
            _remote = null;
            _pendingPacket = null;
            _receivedRoot = false;
        }
    }
}
