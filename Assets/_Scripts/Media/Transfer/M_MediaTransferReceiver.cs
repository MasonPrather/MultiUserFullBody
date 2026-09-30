/*
 * Script Name: M_MediaTransferReceiver.cs
 * Description: Receives chunked media files into temp files and verifies sha256 before accepting.
 * Project Role: Makes corrupted, interrupted, spoofed, or oversized transfers visible failures instead of blank panels.
 */

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public sealed class M_MediaTransferReceiver
{
    private readonly M_SessionMediaCache _cache;
    private readonly int _maxChunkPayloadBytes;
    private readonly int _maxFullImageBytes;
    private readonly int _maxFullVideoBytes;
    private readonly int _maxThumbnailBytes;
    private readonly Dictionary<string, ReceiveSession> _sessions = new Dictionary<string, ReceiveSession>(StringComparer.OrdinalIgnoreCase);

    public event Action<M_MediaTransferCompletedFile> FileCompleted;
    public event Action<ulong, M_MediaTransferFileRole, float> ProgressChanged;
    public event Action<ulong, string> TransferFailed;

    public M_MediaTransferReceiver(M_SessionMediaCache cache, int maxChunkPayloadBytes, int maxFullImageBytes, int maxFullVideoBytes, int maxThumbnailBytes)
    {
        _cache = cache;
        _maxChunkPayloadBytes = Mathf.Max(512, maxChunkPayloadBytes);
        _maxFullImageBytes = Mathf.Max(1024, maxFullImageBytes);
        _maxFullVideoBytes = Mathf.Max(1024, maxFullVideoBytes);
        _maxThumbnailBytes = Mathf.Max(1024, maxThumbnailBytes);
    }

    public void HandleBegin(ulong senderClientId, M_MediaTransferHeader header, bool storeInHostCache)
    {
        string transferId = header.TransferId.ToString();
        if (!ValidateCommon(header, out string reason))
        {
            Fail(header.Sequence, transferId, reason);
            return;
        }

        int maxBytes = GetMaxBytes(header);
        if (header.TotalBytes <= 0 || header.TotalBytes > maxBytes)
        {
            Fail(header.Sequence, transferId, $"Received {header.Role} is too large.");
            return;
        }

        if (header.ChunkCount <= 0 || header.ChunkCount > 10000)
        {
            Fail(header.Sequence, transferId, "Transfer chunk count was invalid.");
            return;
        }

        string identitySha = header.IdentitySha256.ToString();
        string contentSha = header.Sha256.ToString();
        string mime = header.Mime.ToString();
        if (_cache.HasVerifiedFile(identitySha, header.Role, storeInHostCache, contentSha, mime))
        {
            FileCompleted?.Invoke(new M_MediaTransferCompletedFile
            {
                SenderClientId = senderClientId,
                Sequence = header.Sequence,
                TransferId = transferId,
                MediaId = header.MediaId.ToString(),
                IdentitySha256 = identitySha,
                ContentSha256 = contentSha,
                Role = header.Role,
                LocalPath = _cache.GetCachePath(identitySha, header.Role, storeInHostCache, mime),
                StoredInHostCache = storeInHostCache
            });
            return;
        }

        CancelSession(transferId, deleteTemp: true);

        string tempPath = _cache.CreateTempPath(transferId, header.Role, storeInHostCache);
        ReceiveSession session = new ReceiveSession
        {
            SenderClientId = senderClientId,
            Header = header,
            TempPath = tempPath,
            StoreInHostCache = storeInHostCache,
            ReceivedChunks = new bool[header.ChunkCount],
            Stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None)
        };

        _sessions[transferId] = session;
        ProgressChanged?.Invoke(header.Sequence, header.Role, 0f);
    }

    public void HandleChunk(M_MediaTransferHeader header, byte[] payload)
    {
        string transferId = header.TransferId.ToString();
        if (!_sessions.TryGetValue(transferId, out ReceiveSession session))
            return;

        if (payload == null || payload.Length != header.PayloadLength || payload.Length > _maxChunkPayloadBytes)
        {
            Fail(header.Sequence, transferId, "Received an invalid media chunk.");
            return;
        }

        if (header.ChunkIndex < 0 || header.ChunkIndex >= session.ReceivedChunks.Length)
        {
            Fail(header.Sequence, transferId, "Received a media chunk outside the expected range.");
            return;
        }

        if (header.ChunkIndex != session.ExpectedNextChunkIndex)
        {
            Fail(header.Sequence, transferId, "Transfer interrupted: chunks arrived out of order.");
            return;
        }

        session.Stream.Write(payload, 0, payload.Length);
        session.BytesReceived += payload.Length;
        session.ReceivedChunks[header.ChunkIndex] = true;
        session.ExpectedNextChunkIndex++;

        float progress = Mathf.Clamp01(session.BytesReceived / (float)Mathf.Max(1, session.Header.TotalBytes));
        ProgressChanged?.Invoke(session.Header.Sequence, session.Header.Role, progress);
    }

    public void HandleComplete(M_MediaTransferHeader header)
    {
        string transferId = header.TransferId.ToString();
        if (!_sessions.TryGetValue(transferId, out ReceiveSession session))
            return;

        for (int i = 0; i < session.ReceivedChunks.Length; i++)
        {
            if (!session.ReceivedChunks[i])
            {
                Fail(header.Sequence, transferId, "Transfer interrupted: one or more chunks were missing.");
                return;
            }
        }

        session.Stream.Flush();
        session.Stream.Dispose();
        session.Stream = null;

        string identitySha = session.Header.IdentitySha256.ToString();
        string contentSha = session.Header.Sha256.ToString();
        string mime = session.Header.Mime.ToString();
        if (!_cache.StoreVerifiedTemp(session.TempPath, identitySha, contentSha, session.Header.Role, session.StoreInHostCache, out string finalPath, out string failureReason, mime))
        {
            Fail(header.Sequence, transferId, failureReason);
            return;
        }

        _sessions.Remove(transferId);
        ProgressChanged?.Invoke(session.Header.Sequence, session.Header.Role, 1f);
        FileCompleted?.Invoke(new M_MediaTransferCompletedFile
        {
            SenderClientId = session.SenderClientId,
            Sequence = session.Header.Sequence,
            TransferId = transferId,
            MediaId = session.Header.MediaId.ToString(),
            IdentitySha256 = identitySha,
            ContentSha256 = contentSha,
            Role = session.Header.Role,
            LocalPath = finalPath,
            StoredInHostCache = session.StoreInHostCache
        });
    }

    public void CancelFromSender(ulong senderClientId)
    {
        List<string> toCancel = new List<string>();
        foreach (KeyValuePair<string, ReceiveSession> pair in _sessions)
        {
            if (pair.Value.SenderClientId == senderClientId)
                toCancel.Add(pair.Key);
        }

        for (int i = 0; i < toCancel.Count; i++)
            Fail(_sessions[toCancel[i]].Header.Sequence, toCancel[i], "Transfer interrupted: sender disconnected.");
    }

    private bool ValidateCommon(M_MediaTransferHeader header, out string reason)
    {
        if (!header.IsSupported)
        {
            reason = "Unsupported media transfer protocol version.";
            return false;
        }

        string contentSha = header.Sha256.ToString();
        string identitySha = header.IdentitySha256.ToString();
        if (contentSha.Length != 64 || identitySha.Length != 64)
        {
            reason = "Transfer hash metadata was invalid.";
            return false;
        }

        if (header.Role != M_MediaTransferFileRole.Thumbnail && header.Role != M_MediaTransferFileRole.FullMedia)
        {
            reason = "Transfer file role was invalid.";
            return false;
        }

        string mime = header.Mime.ToString();
        if (header.Role == M_MediaTransferFileRole.FullMedia
            && !M_MediaTypeUtility.IsSupportedImageMime(mime)
            && !M_MediaTypeUtility.IsSupportedVideoMime(mime))
        {
            reason = "Transfer media type was invalid.";
            return false;
        }

        reason = null;
        return true;
    }

    private int GetMaxBytes(M_MediaTransferHeader header)
    {
        if (header.Role == M_MediaTransferFileRole.Thumbnail)
            return _maxThumbnailBytes;

        return M_MediaTypeUtility.IsVideoMime(header.Mime.ToString()) ? _maxFullVideoBytes : _maxFullImageBytes;
    }

    private void Fail(ulong sequence, string transferId, string reason)
    {
        CancelSession(transferId, deleteTemp: true);
        TransferFailed?.Invoke(sequence, reason ?? "Media transfer failed.");
    }

    private void CancelSession(string transferId, bool deleteTemp)
    {
        if (!_sessions.TryGetValue(transferId, out ReceiveSession session))
            return;

        try
        {
            session.Stream?.Dispose();
        }
        catch
        {
        }

        if (deleteTemp && !string.IsNullOrWhiteSpace(session.TempPath) && File.Exists(session.TempPath))
            File.Delete(session.TempPath);

        _sessions.Remove(transferId);
    }

    private sealed class ReceiveSession
    {
        public ulong SenderClientId;
        public M_MediaTransferHeader Header;
        public string TempPath;
        public bool StoreInHostCache;
        public bool[] ReceivedChunks;
        public int ExpectedNextChunkIndex;
        public int BytesReceived;
        public FileStream Stream;
    }
}
