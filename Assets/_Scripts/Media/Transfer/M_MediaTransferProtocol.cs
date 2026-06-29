/*
 * Script Name: M_MediaTransferProtocol.cs
 * Description: Binary protocol definitions for NGO named custom media messages.
 * Project Role: Keeps byte transfer explicit, small, versioned, and separate from catalog metadata.
 */

using System;
using Unity.Collections;
using Unity.Netcode;

public enum MediaMsgType : byte
{
    Announce = 1,
    Request = 2,
    Begin = 3,
    Chunk = 4,
    Ack = 5,
    Missing = 6,
    Complete = 7,
    Cancel = 8,
    Error = 9
}

public enum M_MediaTransferFileRole : byte
{
    Thumbnail = 1,
    FullImage = 2
}

public struct M_MediaTransferHeader
{
    public const byte ProtocolVersion = 1;

    public byte Version;
    public MediaMsgType MsgType;
    public M_MediaTransferFileRole Role;
    public FixedString64Bytes TransferId;
    public FixedString64Bytes MediaId;
    public FixedString128Bytes Sha256;
    public FixedString128Bytes IdentitySha256;
    public ulong Sequence;
    public int ChunkIndex;
    public int ChunkCount;
    public ushort PayloadLength;
    public int TotalBytes;
    public int Width;
    public int Height;

    public bool IsSupported => Version == ProtocolVersion;

    public void Write(FastBufferWriter writer)
    {
        writer.WriteValueSafe(Version);
        writer.WriteValueSafe(MsgType);
        writer.WriteValueSafe(Role);
        writer.WriteValueSafe(TransferId);
        writer.WriteValueSafe(MediaId);
        writer.WriteValueSafe(Sha256);
        writer.WriteValueSafe(IdentitySha256);
        writer.WriteValueSafe(Sequence);
        writer.WriteValueSafe(ChunkIndex);
        writer.WriteValueSafe(ChunkCount);
        writer.WriteValueSafe(PayloadLength);
        writer.WriteValueSafe(TotalBytes);
        writer.WriteValueSafe(Width);
        writer.WriteValueSafe(Height);
    }

    public static M_MediaTransferHeader Read(FastBufferReader reader)
    {
        M_MediaTransferHeader header = default;
        reader.ReadValueSafe(out header.Version);
        reader.ReadValueSafe(out header.MsgType);
        reader.ReadValueSafe(out header.Role);
        reader.ReadValueSafe(out header.TransferId);
        reader.ReadValueSafe(out header.MediaId);
        reader.ReadValueSafe(out header.Sha256);
        reader.ReadValueSafe(out header.IdentitySha256);
        reader.ReadValueSafe(out header.Sequence);
        reader.ReadValueSafe(out header.ChunkIndex);
        reader.ReadValueSafe(out header.ChunkCount);
        reader.ReadValueSafe(out header.PayloadLength);
        reader.ReadValueSafe(out header.TotalBytes);
        reader.ReadValueSafe(out header.Width);
        reader.ReadValueSafe(out header.Height);
        return header;
    }
}

public sealed class M_MediaTransferCompletedFile
{
    public ulong SenderClientId;
    public ulong Sequence;
    public string TransferId;
    public string MediaId;
    public string IdentitySha256;
    public string ContentSha256;
    public M_MediaTransferFileRole Role;
    public string LocalPath;
    public bool StoredInHostCache;
}

public static class M_MediaTransferProtocolUtility
{
    public const string NamedMessage = "MURPM_MEDIA_TRANSFER";

    public static FixedString64Bytes Fixed64(string value)
    {
        return new FixedString64Bytes(Truncate(value, 61));
    }

    public static FixedString128Bytes Fixed128(string value)
    {
        return new FixedString128Bytes(Truncate(value, 125));
    }

    public static string BuildTransferId(ulong sequence, M_MediaTransferFileRole role)
    {
        string roleText = role == M_MediaTransferFileRole.Thumbnail ? "thumb" : "full";
        string id = $"{sequence}_{roleText}_{Guid.NewGuid():N}";
        return id.Length <= 61 ? id : id.Substring(0, 61);
    }

    private static string Truncate(string value, int maxChars)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Length <= maxChars ? value : value.Substring(0, maxChars);
    }
}
