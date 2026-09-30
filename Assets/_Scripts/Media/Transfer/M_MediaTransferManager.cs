/*
 * Script Name: M_MediaTransferManager.cs
 * Description: NGO named-message router for thumbnail-first media byte transfer.
 * Project Role: Host-authoritative fallback transport for bounded shared media bytes.
 */

using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class M_MediaTransferManager : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private M_LobbyMediaCatalog catalog;
    [SerializeField] private M_MediaTransferScheduler scheduler;
    [SerializeField] private M_SessionMediaCache sessionCache;
    [SerializeField] private M_MediaLibrary mediaLibrary;

    [Header("Transfer Limits")]
    [Tooltip("Hard cap for optimized JPG transfers through NGO.")]
    [SerializeField] private int maxFullImageBytes = 5 * 1024 * 1024;

    [Tooltip("Hard cap for small video transfers through NGO. Keep conservative, especially when Unity Relay is active.")]
    [SerializeField] private int maxFullVideoBytes = 64 * 1024 * 1024;

    [SerializeField] private int maxThumbnailBytes = 64 * 1024;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging = true;

    private readonly Dictionary<string, SharedMediaEntry> _pendingClientSharesBySha = new Dictionary<string, SharedMediaEntry>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _requestedMissing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private M_MediaTransferSender _sender;
    private M_MediaTransferReceiver _receiver;
    private bool _registered;

    public event Action<string> StatusChanged;
    public event Action<ulong, M_MediaTransferFileRole, float> ProgressChanged;
    public event Action<M_MediaTransferCompletedFile> FileReceived;

    private void Awake()
    {
        ResolveReferences();
        EnsureRuntimeObjects();
    }

    public override void OnNetworkSpawn()
    {
        ResolveReferences();
        EnsureRuntimeObjects();
        RegisterMessages();

        if (catalog != null)
            catalog.EntryAddedOrUpdated += HandleCatalogEntry;

        if (NetworkManager != null)
            NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;

        CheckCatalogForMissingFiles();
    }

    public override void OnNetworkDespawn()
    {
        if (catalog != null)
            catalog.EntryAddedOrUpdated -= HandleCatalogEntry;

        if (NetworkManager != null)
            NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;

        UnregisterMessages();
    }

    public void ExpectClientShare(SharedMediaEntry entry)
    {
        if (!IsServer)
            return;

        string sha = entry.Sha256.ToString();
        if (!string.IsNullOrWhiteSpace(sha))
            _pendingClientSharesBySha[sha] = entry;
    }

    public void SendLocalMediaToHost(SharedMediaEntry entry, string thumbnailPath, string fullMediaPath)
    {
        if (!IsClient || IsServer)
            return;

        List<ulong> target = new List<ulong> { NetworkManager.ServerClientId };
        EnqueueFile(entry, thumbnailPath, M_MediaTransferFileRole.Thumbnail, target);
        EnqueueFile(entry, fullMediaPath, M_MediaTransferFileRole.FullMedia, target);
        ReportTransferStatus($"Sharing {M_MediaTypeUtility.DisplayNoun(entry.Kind.ToString())} with host...");
    }

    public void SendHostMediaToClients(SharedMediaEntry entry, string thumbnailPath, string fullMediaPath, ulong excludedClientId = ulong.MaxValue)
    {
        if (!IsServer)
            return;

        List<ulong> targets = BuildClientTargets(excludedClientId);
        if (targets.Count == 0)
        {
            catalog?.SetStateServer(entry.Sequence, MediaShareState.Ready);
            return;
        }

        if (EnqueueFile(entry, thumbnailPath, M_MediaTransferFileRole.Thumbnail, targets))
            catalog?.SetStateServer(entry.Sequence, MediaShareState.ThumbnailReady);

        if (EnqueueFile(entry, fullMediaPath, M_MediaTransferFileRole.FullMedia, targets))
            catalog?.SetStateServer(entry.Sequence, MediaShareState.Ready);
    }

    public void RequestMediaFromHost(SharedMediaEntry entry, M_MediaTransferFileRole role)
    {
        if (!IsClient || IsServer || NetworkManager == null || !NetworkManager.IsListening)
            return;

        string identitySha = entry.Sha256.ToString();
        string requestKey = $"{identitySha}_{role}";
        if (!_requestedMissing.Add(requestKey))
            return;

        M_MediaTransferHeader header = new M_MediaTransferHeader
        {
            Version = M_MediaTransferHeader.ProtocolVersion,
            MsgType = MediaMsgType.Request,
            Role = role,
            TransferId = M_MediaTransferProtocolUtility.Fixed64($"request_{entry.Sequence}_{role}"),
            MediaId = entry.MediaId,
            Sha256 = entry.Sha256,
            IdentitySha256 = entry.Sha256,
            Sequence = entry.Sequence,
            ChunkIndex = -1,
            ChunkCount = 0,
            PayloadLength = 0,
            TotalBytes = 0,
            Width = entry.Width,
            Height = entry.Height
        };

        SendProtocolMessage(header, null, new List<ulong> { NetworkManager.ServerClientId });
        string noun = M_MediaTypeUtility.DisplayNoun(entry.Kind.ToString());
        ReportTransferStatus(role == M_MediaTransferFileRole.Thumbnail ? $"Requesting shared {noun} thumbnail..." : $"Requesting shared {noun}...");
    }

    public void SendProtocolMessage(M_MediaTransferHeader header, byte[] payload, IReadOnlyList<ulong> targetClientIds)
    {
        if (NetworkManager == null || !NetworkManager.IsListening || targetClientIds == null || targetClientIds.Count == 0)
            return;

        int payloadLength = payload?.Length ?? 0;
        int capacity = 512 + payloadLength;
        using FastBufferWriter writer = new FastBufferWriter(capacity, Allocator.Temp);
        header.PayloadLength = (ushort)payloadLength;
        header.Write(writer);
        if (payloadLength > 0)
            writer.WriteBytesSafe(payload, payloadLength);

        // ReliableFragmentedSequenced is available in NGO 2.8. We still keep chunks small because Relay and NGO are not a
        // production bulk media pipeline; this path is a conservative fallback for bounded media files.
        NetworkManager.CustomMessagingManager.SendNamedMessage(
            M_MediaTransferProtocolUtility.NamedMessage,
            targetClientIds,
            writer,
            NetworkDelivery.ReliableFragmentedSequenced);
    }

    public void ReportTransferStatus(string status)
    {
        if (verboseLogging)
            Debug.Log($"[M_MediaTransfer] {status}");

        StatusChanged?.Invoke(status);
    }

    public void ReportTransferProgress(ulong sequence, M_MediaTransferFileRole role, float progress)
    {
        ProgressChanged?.Invoke(sequence, role, Mathf.Clamp01(progress));
    }

    private bool EnqueueFile(SharedMediaEntry entry, string path, M_MediaTransferFileRole role, IReadOnlyList<ulong> targets)
    {
        EnsureRuntimeObjects();
        if (_sender == null || scheduler == null)
            return false;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            ReportTransferStatus($"Cannot send {role}: file is missing.");
            return false;
        }

        long length = new FileInfo(path).Length;
        int limit = GetLimitFor(entry, role);
        if (length > limit)
        {
            ReportTransferStatus($"{role} is too large to send through the NGO fallback.");
            return false;
        }

        return scheduler.EnqueueSend(_sender.SendFileCoroutine(entry, path, role, targets));
    }

    private void HandleNamedMessage(ulong senderClientId, FastBufferReader reader)
    {
        EnsureRuntimeObjects();

        try
        {
            M_MediaTransferHeader header = M_MediaTransferHeader.Read(reader);
            if (!header.IsSupported)
            {
                ReportTransferStatus("Ignored media message with unsupported protocol version.");
                return;
            }

            byte[] payload = null;
            if (header.PayloadLength > 0)
            {
                if (header.PayloadLength > scheduler.ChunkPayloadBytes || !reader.TryBeginRead(header.PayloadLength))
                {
                    ReportTransferStatus("Rejected invalid media chunk payload.");
                    return;
                }

                payload = new byte[header.PayloadLength];
                reader.ReadBytesSafe(ref payload, header.PayloadLength);
            }

            switch (header.MsgType)
            {
                case MediaMsgType.Request:
                    HandleRequest(senderClientId, header);
                    break;
                case MediaMsgType.Begin:
                    _receiver.HandleBegin(senderClientId, header, ShouldStoreInHostCache(senderClientId));
                    break;
                case MediaMsgType.Chunk:
                    _receiver.HandleChunk(header, payload);
                    break;
                case MediaMsgType.Complete:
                    _receiver.HandleComplete(header);
                    break;
                case MediaMsgType.Cancel:
                    ReportTransferStatus("Media transfer was cancelled.");
                    break;
                case MediaMsgType.Error:
                    ReportTransferStatus("Media transfer error received.");
                    break;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[M_MediaTransfer] Failed to parse media message: {e.Message}");
        }
    }

    private void HandleRequest(ulong requesterClientId, M_MediaTransferHeader request)
    {
        if (!IsServer || catalog == null || requesterClientId == NetworkManager.ServerClientId)
            return;

        string identitySha = request.IdentitySha256.ToString();
        if (!catalog.TryGetBySha256(identitySha, out SharedMediaEntry entry))
        {
            ReportTransferStatus("Late-join media request could not be matched to catalog.");
            return;
        }

        string path = ResolveHostPathForRequest(entry, request.Role);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            ReportTransferStatus("Host cache did not contain the requested shared media.");
            return;
        }

        EnqueueFile(entry, path, request.Role, new List<ulong> { requesterClientId });
    }

    private string ResolveHostPathForRequest(SharedMediaEntry entry, M_MediaTransferFileRole role)
    {
        string identitySha = entry.Sha256.ToString();
        if (sessionCache != null && sessionCache.TryGetCachedPath(identitySha, role, true, out string cachePath, entry.Mime.ToString()))
            return cachePath;

        // Fallback for the host's own local gallery if setup code forgot to pre-copy into host_cache.
        if (mediaLibrary != null && entry.OwnerClientId == NetworkManager.ServerClientId)
        {
            M_MediaRecord record = mediaLibrary.GetByMediaId(entry.MediaId.ToString());
            return role == M_MediaTransferFileRole.Thumbnail
                ? mediaLibrary.ResolveThumbnailPath(record)
                : mediaLibrary.ResolveFullPath(record);
        }

        return null;
    }

    private void HandleReceiverCompleted(M_MediaTransferCompletedFile file)
    {
        _requestedMissing.Remove($"{file.IdentitySha256}_{file.Role}");

        if (IsServer && file.StoredInHostCache && file.SenderClientId != NetworkManager.ServerClientId)
        {
            if (!_pendingClientSharesBySha.TryGetValue(file.IdentitySha256, out SharedMediaEntry entry))
            {
                ReportTransferStatus("Received media bytes from a client, but no pending catalog entry was found.");
                return;
            }

            if (file.Role == M_MediaTransferFileRole.Thumbnail)
            {
                catalog?.SetStateServer(entry.Sequence, MediaShareState.ThumbnailReady);
                EnqueueFile(entry, file.LocalPath, M_MediaTransferFileRole.Thumbnail, BuildClientTargets(file.SenderClientId));
                ReportTransferStatus("Thumbnail received and verified by host.");
            }
            else
            {
                catalog?.SetStateServer(entry.Sequence, MediaShareState.Ready);
                EnqueueFile(entry, file.LocalPath, M_MediaTransferFileRole.FullMedia, BuildClientTargets(file.SenderClientId));
                _pendingClientSharesBySha.Remove(file.IdentitySha256);
                ReportTransferStatus("Media received and verified by host.");
            }
        }
        else
        {
            FileReceived?.Invoke(file);
            ReportTransferStatus(file.Role == M_MediaTransferFileRole.Thumbnail ? "Thumbnail ready." : "Shared media ready.");
        }
    }

    private void HandleReceiverProgress(ulong sequence, M_MediaTransferFileRole role, float progress)
    {
        ProgressChanged?.Invoke(sequence, role, progress);
    }

    private void HandleReceiverFailed(ulong sequence, string reason)
    {
        ReportTransferStatus(reason);
        if (IsServer)
            catalog?.SetStateServer(sequence, MediaShareState.Failed);
    }

    private void HandleCatalogEntry(SharedMediaEntry entry)
    {
        if (IsServer)
            return;

        string identitySha = entry.Sha256.ToString();
        if (entry.State >= MediaShareState.ThumbnailReady
            && sessionCache != null
            && !sessionCache.HasVerifiedFile(identitySha, M_MediaTransferFileRole.Thumbnail, false, null, entry.Mime.ToString()))
        {
            RequestMediaFromHost(entry, M_MediaTransferFileRole.Thumbnail);
        }

        if (entry.State == MediaShareState.Ready
            && sessionCache != null
            && !sessionCache.HasVerifiedFile(identitySha, M_MediaTransferFileRole.FullMedia, false, identitySha, entry.Mime.ToString()))
        {
            RequestMediaFromHost(entry, M_MediaTransferFileRole.FullMedia);
        }
    }

    private void CheckCatalogForMissingFiles()
    {
        if (catalog == null || IsServer)
            return;

        for (int i = 0; i < catalog.Count; i++)
            HandleCatalogEntry(catalog.GetEntryAt(i));
    }

    private List<ulong> BuildClientTargets(ulong excludedClientId)
    {
        List<ulong> targets = new List<ulong>();
        if (NetworkManager == null)
            return targets;

        foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
        {
            if (clientId == NetworkManager.ServerClientId || clientId == excludedClientId)
                continue;

            targets.Add(clientId);
        }

        return targets;
    }

    private bool ShouldStoreInHostCache(ulong senderClientId)
    {
        return IsServer && senderClientId != NetworkManager.ServerClientId;
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        _receiver?.CancelFromSender(clientId);
    }

    private void RegisterMessages()
    {
        if (_registered || NetworkManager == null || NetworkManager.CustomMessagingManager == null)
            return;

        NetworkManager.CustomMessagingManager.RegisterNamedMessageHandler(M_MediaTransferProtocolUtility.NamedMessage, HandleNamedMessage);
        _registered = true;
    }

    private void UnregisterMessages()
    {
        if (!_registered || NetworkManager == null || NetworkManager.CustomMessagingManager == null)
            return;

        NetworkManager.CustomMessagingManager.UnregisterNamedMessageHandler(M_MediaTransferProtocolUtility.NamedMessage);
        _registered = false;
    }

    private void EnsureRuntimeObjects()
    {
        ResolveReferences();

        if (_sender == null && scheduler != null)
            _sender = new M_MediaTransferSender(this, scheduler);

        if (_receiver == null && sessionCache != null && scheduler != null)
        {
            _receiver = new M_MediaTransferReceiver(sessionCache, scheduler.ChunkPayloadBytes, maxFullImageBytes, maxFullVideoBytes, maxThumbnailBytes);
            _receiver.FileCompleted += HandleReceiverCompleted;
            _receiver.ProgressChanged += HandleReceiverProgress;
            _receiver.TransferFailed += HandleReceiverFailed;
        }
    }

    private int GetLimitFor(SharedMediaEntry entry, M_MediaTransferFileRole role)
    {
        if (role == M_MediaTransferFileRole.Thumbnail)
            return maxThumbnailBytes;

        return M_MediaTypeUtility.IsVideoMime(entry.Mime.ToString()) ? maxFullVideoBytes : maxFullImageBytes;
    }

    private void ResolveReferences()
    {
        if (catalog == null)
            catalog = GetComponent<M_LobbyMediaCatalog>();

        if (catalog == null)
            catalog = FindFirstObjectByType<M_LobbyMediaCatalog>();

        if (scheduler == null)
            scheduler = GetComponent<M_MediaTransferScheduler>();

        if (scheduler == null)
            scheduler = FindFirstObjectByType<M_MediaTransferScheduler>();

        if (sessionCache == null)
            sessionCache = GetComponent<M_SessionMediaCache>();

        if (sessionCache == null)
            sessionCache = FindFirstObjectByType<M_SessionMediaCache>();

        if (mediaLibrary == null)
            mediaLibrary = GetComponent<M_MediaLibrary>();

        if (mediaLibrary == null)
            mediaLibrary = FindFirstObjectByType<M_MediaLibrary>();
    }
}
