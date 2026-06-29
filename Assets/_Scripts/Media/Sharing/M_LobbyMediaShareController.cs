/*
 * Script Name: M_LobbyMediaShareController.cs
 * Description: Entry point for sharing a selected local gallery item with the current Netcode lobby.
 * Project Role: Enforces host authority: clients upload to host first; host verifies and redistributes.
 */

using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class M_LobbyMediaShareController : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private M_MediaLibrary mediaLibrary;
    [SerializeField] private M_LobbyMediaCatalog catalog;
    [SerializeField] private M_MediaTransferManager transferManager;
    [SerializeField] private M_SessionMediaCache sessionCache;

    [Header("Limits")]
    [SerializeField] private int maxShareBytes = 5 * 1024 * 1024;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging = true;

    private readonly Dictionary<string, PendingLocalShare> _pendingLocalSharesBySha = new Dictionary<string, PendingLocalShare>(StringComparer.OrdinalIgnoreCase);

    public event Action<string> StatusChanged;

    private void Awake()
    {
        ResolveReferences();
    }

    public void ShareSelectedMedia(string mediaId)
    {
        ResolveReferences();

        if (NetworkManager == null || !NetworkManager.IsListening)
        {
            SetStatus("Join or host a lobby before sharing a photo.");
            return;
        }

        if (!TryBuildPendingShare(mediaId, out PendingLocalShare pending, out string failure))
        {
            SetStatus(failure);
            return;
        }

        _pendingLocalSharesBySha[pending.Record.sha256] = pending;

        if (IsServer)
        {
            ShareFromHost(pending);
        }
        else
        {
            SharedMediaEntry request = M_LobbyMediaCatalog.CreateEntryFromRecord(
                pending.Record,
                NetworkManager.LocalClientId,
                sequence: 0,
                thumbnailByteSize: pending.ThumbnailByteSize,
                state: MediaShareState.Announced);

            ShareMediaRequestServerRpc(request);
            SetStatus("Asking host to share photo...");
        }
    }

    private void ShareFromHost(PendingLocalShare pending)
    {
        if (catalog == null || transferManager == null || sessionCache == null)
        {
            SetStatus("Media sharing objects are not fully configured.");
            return;
        }

        ulong sequence = catalog.ReserveSequenceServer();
        SharedMediaEntry entry = M_LobbyMediaCatalog.CreateEntryFromRecord(
            pending.Record,
            NetworkManager.ServerClientId,
            sequence,
            pending.ThumbnailByteSize,
            MediaShareState.Announced);

        sessionCache.CopyIntoCache(pending.FullPath, pending.Record.sha256, M_MediaTransferFileRole.FullImage, hostCache: true, out _, out _);
        sessionCache.CopyIntoCache(pending.ThumbnailPath, pending.Record.sha256, M_MediaTransferFileRole.Thumbnail, hostCache: true, out _, out _);

        catalog.AddOrUpdateServer(entry);
        catalog.SetStateServer(entry.Sequence, MediaShareState.Transferring);
        transferManager.SendHostMediaToClients(entry, pending.ThumbnailPath, pending.FullPath);
        SetStatus("Sharing photo with everyone in the lobby.");
    }

    [ServerRpc(RequireOwnership = false)]
    private void ShareMediaRequestServerRpc(SharedMediaEntry requestedEntry, ServerRpcParams rpcParams = default)
    {
        ResolveReferences();
        ulong senderClientId = rpcParams.Receive.SenderClientId;

        if (!ValidateClientShareRequest(requestedEntry, out string reason))
        {
            ShareRejectedClientRpc(new FixedString128Bytes(reason), Target(senderClientId));
            return;
        }

        string sha = requestedEntry.Sha256.ToString();
        if (catalog.TryGetBySha256(sha, out SharedMediaEntry existing)
            && existing.State == MediaShareState.Ready
            && sessionCache != null
            && sessionCache.HasVerifiedFile(sha, M_MediaTransferFileRole.FullImage, hostCache: true, expectedSha256: sha))
        {
            ShareRejectedClientRpc(new FixedString128Bytes("That photo is already shared in this lobby."), Target(senderClientId));
            return;
        }

        requestedEntry.OwnerClientId = senderClientId;
        requestedEntry.Sequence = catalog.ReserveSequenceServer();
        requestedEntry.State = MediaShareState.Transferring;

        catalog.AddOrUpdateServer(requestedEntry);
        transferManager.ExpectClientShare(requestedEntry);
        ShareAcceptedClientRpc(requestedEntry, Target(senderClientId));
    }

    [ClientRpc]
    private void ShareAcceptedClientRpc(SharedMediaEntry acceptedEntry, ClientRpcParams clientRpcParams = default)
    {
        if (IsServer)
            return;

        string sha = acceptedEntry.Sha256.ToString();
        if (!_pendingLocalSharesBySha.TryGetValue(sha, out PendingLocalShare pending))
        {
            SetStatus("Host accepted the share, but the local photo was no longer available.");
            return;
        }

        transferManager.SendLocalMediaToHost(acceptedEntry, pending.ThumbnailPath, pending.FullPath);
        SetStatus("Sending photo to host for verification...");
        _pendingLocalSharesBySha.Remove(sha);
    }

    [ClientRpc]
    private void ShareRejectedClientRpc(FixedString128Bytes reason, ClientRpcParams clientRpcParams = default)
    {
        if (IsServer)
            return;

        SetStatus(reason.ToString());
    }

    private bool TryBuildPendingShare(string mediaId, out PendingLocalShare pending, out string failure)
    {
        pending = default;
        failure = null;

        if (mediaLibrary == null)
        {
            failure = "Media library is not configured.";
            return false;
        }

        M_MediaRecord record = mediaLibrary.GetByMediaId(mediaId);
        if (record == null)
        {
            failure = "Choose a gallery photo before sharing.";
            return false;
        }

        if (!string.Equals(record.kind, "image", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(record.mime, "image/jpeg", StringComparison.OrdinalIgnoreCase))
        {
            failure = "Only imported JPG photos can be shared with the lobby right now.";
            return false;
        }

        string fullPath = mediaLibrary.ResolveFullPath(record);
        string thumbPath = mediaLibrary.ResolveThumbnailPath(record);
        if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
        {
            failure = "The selected photo file is missing from local storage.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(thumbPath) || !File.Exists(thumbPath))
        {
            failure = "The selected photo thumbnail is missing. Re-import the photo and try again.";
            return false;
        }

        long byteSize = new FileInfo(fullPath).Length;
        if (byteSize <= 0 || byteSize > maxShareBytes)
        {
            failure = "This photo is too large to share in the lobby. Please import a smaller image.";
            return false;
        }

        string actualSha = M_MediaHashUtility.Sha256HexForFile(fullPath);
        if (!string.Equals(actualSha, record.sha256, StringComparison.OrdinalIgnoreCase))
        {
            failure = "The selected photo failed local verification and will not be shared.";
            return false;
        }

        pending = new PendingLocalShare
        {
            Record = record,
            FullPath = fullPath,
            ThumbnailPath = thumbPath,
            ThumbnailByteSize = (int)Math.Min(int.MaxValue, new FileInfo(thumbPath).Length)
        };
        return true;
    }

    private static bool ValidateClientShareRequest(SharedMediaEntry entry, out string reason)
    {
        string sha = entry.Sha256.ToString();
        if (sha.Length != 64)
        {
            reason = "Host rejected the photo: invalid hash metadata.";
            return false;
        }

        if (!string.Equals(entry.Kind.ToString(), "image", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(entry.Mime.ToString(), "image/jpeg", StringComparison.OrdinalIgnoreCase))
        {
            reason = "Host rejected the photo: only JPG images are supported.";
            return false;
        }

        if (entry.ByteSize <= 0 || entry.ByteSize > 5 * 1024 * 1024)
        {
            reason = "Host rejected the photo: optimized file is too large.";
            return false;
        }

        if (entry.ThumbnailByteSize <= 0 || entry.ThumbnailByteSize > 64 * 1024)
        {
            reason = "Host rejected the photo: thumbnail is too large.";
            return false;
        }

        reason = null;
        return true;
    }

    private static ClientRpcParams Target(ulong clientId)
    {
        return new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { clientId }
            }
        };
    }

    private void ResolveReferences()
    {
        if (mediaLibrary == null)
            mediaLibrary = GetComponent<M_MediaLibrary>();

        if (mediaLibrary == null)
            mediaLibrary = FindFirstObjectByType<M_MediaLibrary>();

        if (catalog == null)
            catalog = GetComponent<M_LobbyMediaCatalog>();

        if (catalog == null)
            catalog = FindFirstObjectByType<M_LobbyMediaCatalog>();

        if (transferManager == null)
            transferManager = GetComponent<M_MediaTransferManager>();

        if (transferManager == null)
            transferManager = FindFirstObjectByType<M_MediaTransferManager>();

        if (sessionCache == null)
            sessionCache = GetComponent<M_SessionMediaCache>();

        if (sessionCache == null)
            sessionCache = FindFirstObjectByType<M_SessionMediaCache>();
    }

    private void SetStatus(string status)
    {
        if (verboseLogging)
            Debug.Log($"[M_LobbyMediaShare] {status}");

        StatusChanged?.Invoke(status);
    }

    private struct PendingLocalShare
    {
        public M_MediaRecord Record;
        public string FullPath;
        public string ThumbnailPath;
        public int ThumbnailByteSize;
    }
}
