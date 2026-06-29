/*
 * Script Name: M_NetFaceMirror.cs
 * Author: Mason Prather
 * Description: Samples owner-side local face blendshape weights, scales and thresholds them, sends compact snapshots through Netcode RPCs, and applies smoothed weights to each networked avatar face mesh.
 * Project Role: Network facial-expression replication for Ready Player Me avatars.
 * Key Inputs: Owner local face mesh weights, avatar face mesh, send interval, deadzone/smoothing settings, and Netcode ownership state.
 * Key Outputs: ServerRpc/ClientRpc face snapshots, remote blendshape updates, neutral relaxation, and diagnostic logs.
 */

using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// M_NetFaceMirror
/// 
/// Owner clients sample the local OVR-driven Ready Player Me face mesh, scale
/// the blendshape weights, and send snapshots through Netcode. All clients
/// receive those snapshots and apply them to the corresponding network avatar
/// face mesh. This component assumes local and networked avatars use the same
/// RPM avatar URL so blendshape indices remain aligned.
/// </summary>
public class M_NetFaceMirror : NetworkBehaviour
{
    [Header("Local Source (Owner Only)")]
    [Tooltip("Face SkinnedMeshRenderer driven by OVRFaceExpressions on the local rig.")]
    [SerializeField] private SkinnedMeshRenderer localSourceMesh;

    [Header("Networked Avatar Face (All Clients)")]
    [Tooltip("RPM avatar head mesh on THIS player's networked avatar.")]
    [SerializeField] private SkinnedMeshRenderer avatarFaceMesh;

    [Header("Network Settings")]
    [Tooltip("How often to send face snapshots in seconds. 0.03 is approximately 30 FPS.")]
    [Range(0.02f, 0.1f)]
    [SerializeField] private float sendInterval = 0.04f;

    [Tooltip("Only send when a blendshape changes more than this amount (except when going to 0, which always sends).")]
    [Range(0f, 5f)]
    [SerializeField] private float changeThreshold = 0.5f;

    [Header("Application")]
    [Tooltip("0 = instant; higher = smoother. Values around 0.15-0.35 are used for active expressions.")]
    [Range(0f, 1f)]
    [SerializeField] private float applySmoothing = 0.25f;

    [Tooltip("Scale factor applied to local weights before networking (0.05 = 5%).")]
    [Range(0.01f, 0.5f)]
    [SerializeField] private float weightScale = 0.1f;

    [Tooltip("Hard maximum for network-applied blendshape weight.")]
    [Range(0f, 100f)]
    [SerializeField] private float maxWeight = 40f;

    [Header("Neutral & Deadzones (Owner to Network)")]
    [Tooltip("Raw deadzone on local 0–100 weights; below this, treated as exact 0 before scaling.")]
    [Range(0f, 10f)]
    [SerializeField] private float rawDeadzone = 1.5f;

    [Tooltip("Additional deadzone after scaling; below this, sent as 0.")]
    [Range(0f, 5f)]
    [SerializeField] private float scaledDeadzone = 0.05f;

    [Header("Neutral Relax (Client Apply)")]
    [Tooltip("Extra smoothing factor when relaxing back to neutral (target = 0). 0 = instant, 1 = very slow.")]
    [Range(0f, 1f)]
    [SerializeField] private float zeroReturnSmoothing = 0.45f;

    [Tooltip("If |current| is below this and target is 0, snap to exact 0.")]
    [Range(0f, 10f)]
    [SerializeField] private float zeroSnapThreshold = 0.25f;

    [Header("Debug Logging")]
    [Tooltip("Enable verbose logging of blendshape weights for diagnostics.")]
    [SerializeField] private bool enableDebug = true;

    [Tooltip("Seconds between debug logs (per owner / per client).")]
    [Range(0.05f, 2f)]
    [SerializeField] private float debugLogInterval = 0.25f;

    [Tooltip("Max number of blendshapes to log each debug print.")]
    [Range(1, 64)]
    [SerializeField] private int maxLoggedBlendshapes = 16;

    [Tooltip("If true, only log blendshapes whose weights are non-zero.")]
    [SerializeField] private bool logNonZeroOnly = false;

    // Internal buffers / timers
    private float[] _lastSentWeights;
    private float _sendTimer;
    private float _ownerDebugTimer;
    private float _clientDebugTimer;

    #region Lifecycle

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // The target mesh starts in a neutral state before any network snapshot arrives.
        InitBuffersForAvatarMesh();

        // Owner instances can bind the local face mesh after the avatar loader finishes.
        if (IsOwner)
        {
            if (localSourceMesh == null && M_LocalAvatarManager.LastLocalFaceSMR != null)
            {
                BindLocalSource(M_LocalAvatarManager.LastLocalFaceSMR);
                Debug.Log($"[M_NetFaceMirror][OnNetworkSpawn][Owner={OwnerClientId}] Late-bound local source from M_LocalAvatarManager.LastLocalFaceSMR: {localSourceMesh.name}");
            }
            else
            {
                Debug.Log($"[M_NetFaceMirror][OnNetworkSpawn][Owner={OwnerClientId}] localSourceMesh already set? {localSourceMesh != null}");
            }
        }
        else
        {
            Debug.Log($"[M_NetFaceMirror][OnNetworkSpawn][Client={OwnerClientId}] Spawned mirror for remote player.");
        }

        if (avatarFaceMesh != null && avatarFaceMesh.sharedMesh != null)
        {
            Debug.Log($"[M_NetFaceMirror][OnNetworkSpawn] Avatar face bound: {avatarFaceMesh.name} " +
                      $"(blendShapeCount={avatarFaceMesh.sharedMesh.blendShapeCount})");
        }
        else
        {
            Debug.LogWarning("[M_NetFaceMirror][OnNetworkSpawn] avatarFaceMesh not yet bound or has no mesh.");
        }
    }

    private void Update()
    {
        if (!IsSpawned)
            return;

        // Only the owning client samples local tracking data and sends face snapshots.
        if (IsOwner && IsClient && localSourceMesh != null)
        {
            OwnerTick_SendFaceSnapshots();
        }
    }

    #endregion

    #region Public Binds

    /// <summary>
    /// Binds the owner-side OVR-driven face mesh after the local RPM avatar is loaded.
    /// </summary>
    public void BindLocalSource(SkinnedMeshRenderer source)
    {
        if (source == null)
        {
            Debug.LogWarning("[M_NetFaceMirror] BindLocalSource received null source.");
            return;
        }

        localSourceMesh = source;
        _lastSentWeights = null; // force re-init on next send

        int count = localSourceMesh.sharedMesh != null ? localSourceMesh.sharedMesh.blendShapeCount : -1;
        Debug.Log($"[M_NetFaceMirror] Bound local source face mesh: {source.name} (blendShapeCount={count})");
    }

    /// <summary>
    /// Binds the network avatar face mesh that receives replicated weights on each client.
    /// </summary>
    public void BindAvatarFace(SkinnedMeshRenderer target)
    {
        if (target == null)
        {
            Debug.LogWarning("[M_NetFaceMirror] BindAvatarFace received null target.");
            return;
        }

        avatarFaceMesh = target;
        InitBuffersForAvatarMesh();

        int count = avatarFaceMesh.sharedMesh != null ? avatarFaceMesh.sharedMesh.blendShapeCount : -1;
        Debug.Log($"[M_NetFaceMirror] Bound avatar face mesh: {target.name} (blendShapeCount={count})");
    }

    /// <summary>
    /// Convenience helper for debug logging (used by M_LocalAvatarManager).
    /// </summary>
    public string IsFaceBound()
    {
        return avatarFaceMesh != null ? "Yes" : "No";
    }

    #endregion

    #region Owner → Network

    private void OwnerTick_SendFaceSnapshots()
    {
        if (localSourceMesh == null || localSourceMesh.sharedMesh == null)
            return;

        _sendTimer += Time.deltaTime;
        _ownerDebugTimer += Time.deltaTime;

        if (_sendTimer < sendInterval)
            return;

        _sendTimer = 0f;

        int count = localSourceMesh.sharedMesh.blendShapeCount;
        if (count == 0)
            return;

        // Initialize or resize our last-sent buffer.
        if (_lastSentWeights == null || _lastSentWeights.Length != count)
        {
            _lastSentWeights = new float[count];
            for (int i = 0; i < count; i++)
                _lastSentWeights[i] = -999f; // force first send
        }

        bool changedEnough = false;
        FaceSnapshot snapshot = new FaceSnapshot
        {
            Count = (ushort)count,
            Weights = new float[count]
        };

        int nonZeroCount = 0;
        float minW = float.MaxValue;
        float maxWLocal = float.MinValue;

        for (int i = 0; i < count; i++)
        {
            // Raw OVR-driven RPM weight 0–100
            float wRaw = localSourceMesh.GetBlendShapeWeight(i);

            // Clamp raw tracking values into Unity's blendshape range.
            wRaw = Mathf.Clamp(wRaw, 0f, 100f);

            // 1) Raw deadzone: treat tiny noise as perfect neutral.
            if (Mathf.Abs(wRaw) < rawDeadzone)
            {
                snapshot.Weights[i] = 0f;

                // If we previously had any non-zero here, forcing back to 0 is a meaningful change.
                if (!Mathf.Approximately(_lastSentWeights[i], 0f))
                {
                    changedEnough = true;
                }

                _lastSentWeights[i] = 0f;
                continue;
            }

            // 2) Scale down to a fraction (e.g. 5–10%).
            float wScaled = wRaw * weightScale;

            // 3) Scaled deadzone: still treat tiny values as 0.
            if (Mathf.Abs(wScaled) < scaledDeadzone)
            {
                snapshot.Weights[i] = 0f;

                if (!Mathf.Approximately(_lastSentWeights[i], 0f))
                {
                    changedEnough = true;
                }

                _lastSentWeights[i] = 0f;
                continue;
            }

            // 4) Clamp to safe bounds.
            float w = Mathf.Clamp(wScaled, 0f, maxWeight);
            snapshot.Weights[i] = w;

            // Track stats for logging.
            if (w > 0.0001f)
            {
                nonZeroCount++;
                if (w < minW) minW = w;
                if (w > maxWLocal) maxWLocal = w;
            }

            // 5) Threshold-based change detection for non-zero weights.
            if (!changedEnough && Mathf.Abs(w - _lastSentWeights[i]) > changeThreshold)
            {
                changedEnough = true;
            }

            _lastSentWeights[i] = w;
        }

        // If nothing meaningfully changed (including "all zero" case), skip sending.
        if (!changedEnough)
            return;

        // Debug logging for owner-side snapshot
        if (enableDebug && _ownerDebugTimer >= debugLogInterval)
        {
            _ownerDebugTimer = 0f;

            string meshName = localSourceMesh.name;
            int logCount = Mathf.Min(count, maxLoggedBlendshapes);
            var sharedMesh = localSourceMesh.sharedMesh;

            Debug.Log($"[M_NetFaceMirror][Owner={OwnerClientId}] SEND SNAPSHOT " +
                      $"sourceMesh={meshName}, blendShapeCount={count}, nonZero={nonZeroCount}, " +
                      $"minNonZero={minW}, maxNonZero={maxWLocal}, weightScale={weightScale}, maxWeight={maxWeight}, " +
                      $"rawDeadzone={rawDeadzone}, scaledDeadzone={scaledDeadzone}");

            int logged = 0;
            for (int i = 0; i < count && logged < logCount; i++)
            {
                float w = snapshot.Weights[i];
                if (logNonZeroOnly && Mathf.Abs(w) < 0.0001f)
                    continue;

                string bsName = sharedMesh != null ? sharedMesh.GetBlendShapeName(i) : $"BS_{i}";

                Debug.Log($"[M_NetFaceMirror][Owner={OwnerClientId}]   IDX={i} " +
                          $"name={bsName}, weightSent={w}");

                logged++;
            }
        }

        SubmitFaceServerRpc(snapshot);
    }

    [ServerRpc]
    private void SubmitFaceServerRpc(FaceSnapshot snapshot)
    {
        // Relay snapshot to everyone.
        ApplyFaceClientRpc(snapshot);
    }

    #endregion

    #region Apply on Clients

    [ClientRpc]
    private void ApplyFaceClientRpc(FaceSnapshot snapshot)
    {
        if (avatarFaceMesh == null || avatarFaceMesh.sharedMesh == null)
            return;

        _clientDebugTimer += Time.deltaTime;

        int meshCount = avatarFaceMesh.sharedMesh.blendShapeCount;
        if (meshCount == 0)
            return;

        int count = Mathf.Min(meshCount, snapshot.Count);

        int nonZeroCount = 0;
        float minW = float.MaxValue;
        float maxW = float.MinValue;

        for (int i = 0; i < count; i++)
        {
            // Clamp replicated values before applying them to the local mesh.
            float target = Mathf.Clamp(snapshot.Weights[i], 0f, maxWeight);
            float current = avatarFaceMesh.GetBlendShapeWeight(i);

            float lerpFactor;

            if (target <= 0f)
            {
                // We are relaxing back toward neutral.
                // If we're already very close, snap to pure 0 to avoid lingering micro-expressions.
                if (Mathf.Abs(current) <= zeroSnapThreshold)
                {
                    lerpFactor = 1f; // snap to zero this frame
                }
                else
                {
                    // Same exponential smoothing style as applySmoothing,
                    // but using zeroReturnSmoothing for faster relaxation.
                    lerpFactor = zeroReturnSmoothing <= 0f
                        ? 1f
                        : 1f - Mathf.Pow(1f - zeroReturnSmoothing, Time.deltaTime * 60f);
                }

                float smoothedToZero = Mathf.Lerp(current, 0f, lerpFactor);

                // If we got very close to 0, hard clamp to exactly 0.
                if (Mathf.Abs(smoothedToZero) <= zeroSnapThreshold)
                {
                    smoothedToZero = 0f;
                }

                avatarFaceMesh.SetBlendShapeWeight(i, smoothedToZero);

                if (smoothedToZero > 0.0001f)
                {
                    nonZeroCount++;
                    if (smoothedToZero < minW) minW = smoothedToZero;
                    if (smoothedToZero > maxW) maxW = smoothedToZero;
                }
            }
            else
            {
                // Active expression → use normal smoothing.
                lerpFactor = applySmoothing <= 0f
                    ? 1f
                    : 1f - Mathf.Pow(1f - applySmoothing, Time.deltaTime * 60f);

                float smoothed = Mathf.Lerp(current, target, lerpFactor);
                avatarFaceMesh.SetBlendShapeWeight(i, smoothed);

                if (smoothed > 0.0001f)
                {
                    nonZeroCount++;
                    if (smoothed < minW) minW = smoothed;
                    if (smoothed > maxW) maxW = smoothed;
                }
            }
        }

        // Debug logging on client-side apply
        if (enableDebug && _clientDebugTimer >= debugLogInterval)
        {
            _clientDebugTimer = 0f;

            string meshName = avatarFaceMesh.name;
            var sharedMesh = avatarFaceMesh.sharedMesh;
            int logCount = Mathf.Min(count, maxLoggedBlendshapes);

            Debug.Log($"[M_NetFaceMirror][Client={NetworkManager.Singleton.LocalClientId}] APPLY SNAPSHOT " +
                      $"avatarMesh={meshName}, recvCount={snapshot.Count}, applyCount={count}, " +
                      $"nonZero={nonZeroCount}, minNonZero={minW}, maxNonZero={maxW}, " +
                      $"applySmoothing={applySmoothing}, zeroReturnSmoothing={zeroReturnSmoothing}, zeroSnapThreshold={zeroSnapThreshold}");

            int logged = 0;
            for (int i = 0; i < count && logged < logCount; i++)
            {
                float w = avatarFaceMesh.GetBlendShapeWeight(i);
                if (logNonZeroOnly && Mathf.Abs(w) < 0.0001f)
                    continue;

                string bsName = sharedMesh != null ? sharedMesh.GetBlendShapeName(i) : $"BS_{i}";

                Debug.Log($"[M_NetFaceMirror][Client={NetworkManager.Singleton.LocalClientId}]   IDX={i} " +
                          $"name={bsName}, weightApplied={w}");

                logged++;
            }
        }
    }

    private void InitBuffersForAvatarMesh()
    {
        if (avatarFaceMesh == null || avatarFaceMesh.sharedMesh == null)
            return;

        int count = avatarFaceMesh.sharedMesh.blendShapeCount;
        if (count <= 0)
            return;

        // Neutral on spawn/bind.
        for (int i = 0; i < count; i++)
        {
            avatarFaceMesh.SetBlendShapeWeight(i, 0f);
        }

        Debug.Log($"[M_NetFaceMirror] InitBuffersForAvatarMesh: cleared {count} blendshapes on avatarFaceMesh={avatarFaceMesh.name}");
    }

    #endregion

    #region Snapshot Struct

    /// <summary>
    /// Network payload for a frame of face weights.
    /// </summary>
    public struct FaceSnapshot : INetworkSerializable
    {
        public ushort Count;
        public float[] Weights;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Count);

            if (serializer.IsWriter)
            {
                // Weights is guaranteed allocated by the owner when sending.
                for (int i = 0; i < Count; i++)
                {
                    serializer.SerializeValue(ref Weights[i]);
                }
            }
            else
            {
                if (Weights == null || Weights.Length != Count)
                {
                    Weights = new float[Count];
                }

                for (int i = 0; i < Count; i++)
                {
                    serializer.SerializeValue(ref Weights[i]);
                }
            }
        }
    }

    #endregion
}
