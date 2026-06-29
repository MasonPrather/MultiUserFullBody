/*
 * Script Name: M_MediaTransferSender.cs
 * Description: Reads a local file and emits Begin/Chunk/Complete messages with throttling.
 * Project Role: Sends thumbnails before full JPGs without serializing Texture2D or base64 payloads.
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public sealed class M_MediaTransferSender
{
    private readonly M_MediaTransferManager _manager;
    private readonly M_MediaTransferScheduler _scheduler;

    public M_MediaTransferSender(M_MediaTransferManager manager, M_MediaTransferScheduler scheduler)
    {
        _manager = manager;
        _scheduler = scheduler;
    }

    public IEnumerator SendFileCoroutine(SharedMediaEntry entry, string localPath, M_MediaTransferFileRole role, IReadOnlyList<ulong> targetClientIds)
    {
        if (targetClientIds == null || targetClientIds.Count == 0)
            yield break;

        if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath))
        {
            _manager.ReportTransferStatus($"Cannot share {role}: source file is missing.");
            yield break;
        }

        int chunkSize = _scheduler.ChunkPayloadBytes;
        FileInfo fileInfo = new FileInfo(localPath);
        if (fileInfo.Length <= 0 || fileInfo.Length > int.MaxValue)
        {
            _manager.ReportTransferStatus($"Cannot share {role}: file size is invalid.");
            yield break;
        }

        int totalBytes = (int)fileInfo.Length;
        int chunkCount = Mathf.CeilToInt(totalBytes / (float)chunkSize);
        string contentSha = M_MediaHashUtility.Sha256HexForFile(localPath);
        string identitySha = entry.Sha256.ToString();
        string transferId = M_MediaTransferProtocolUtility.BuildTransferId(entry.Sequence, role);

        M_MediaTransferHeader header = BuildHeader(entry, role, transferId, contentSha, identitySha, -1, chunkCount, 0, totalBytes);
        header.MsgType = MediaMsgType.Begin;
        _manager.SendProtocolMessage(header, null, targetClientIds);

        byte[] buffer = new byte[chunkSize];
        int sentThisFrame = 0;
        int chunkIndex = 0;

        using FileStream stream = File.OpenRead(localPath);
        while (chunkIndex < chunkCount)
        {
            int read = stream.Read(buffer, 0, buffer.Length);
            if (read <= 0)
                break;

            byte[] payload = new byte[read];
            Buffer.BlockCopy(buffer, 0, payload, 0, read);

            header = BuildHeader(entry, role, transferId, contentSha, identitySha, chunkIndex, chunkCount, (ushort)read, totalBytes);
            header.MsgType = MediaMsgType.Chunk;
            _manager.SendProtocolMessage(header, payload, targetClientIds);

            chunkIndex++;
            sentThisFrame++;
            _manager.ReportTransferProgress(entry.Sequence, role, chunkIndex / (float)chunkCount);

            if (sentThisFrame >= _scheduler.ChunkWindowPerFrame)
            {
                sentThisFrame = 0;
                yield return null;
            }
        }

        header = BuildHeader(entry, role, transferId, contentSha, identitySha, chunkCount, chunkCount, 0, totalBytes);
        header.MsgType = MediaMsgType.Complete;
        _manager.SendProtocolMessage(header, null, targetClientIds);
        _manager.ReportTransferProgress(entry.Sequence, role, 1f);
    }

    private static M_MediaTransferHeader BuildHeader(
        SharedMediaEntry entry,
        M_MediaTransferFileRole role,
        string transferId,
        string contentSha,
        string identitySha,
        int chunkIndex,
        int chunkCount,
        ushort payloadLength,
        int totalBytes)
    {
        return new M_MediaTransferHeader
        {
            Version = M_MediaTransferHeader.ProtocolVersion,
            MsgType = MediaMsgType.Chunk,
            Role = role,
            TransferId = M_MediaTransferProtocolUtility.Fixed64(transferId),
            MediaId = entry.MediaId,
            Sha256 = M_MediaTransferProtocolUtility.Fixed128(contentSha),
            IdentitySha256 = M_MediaTransferProtocolUtility.Fixed128(identitySha),
            Sequence = entry.Sequence,
            ChunkIndex = chunkIndex,
            ChunkCount = chunkCount,
            PayloadLength = payloadLength,
            TotalBytes = totalBytes,
            Width = entry.Width,
            Height = entry.Height
        };
    }
}
