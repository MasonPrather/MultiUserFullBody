/*
 * Script Name: M_LobbyMediaCatalog.cs
 * Description: Host-authoritative metadata catalog for currently shared lobby media.
 * Project Role: Late-join-safe shared state. This list never stores raw image bytes or local paths.
 */

using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public enum MediaShareState : byte
{
    Announced = 0,
    Transferring = 1,
    ThumbnailReady = 2,
    Ready = 3,
    Failed = 4,
    Cancelled = 5
}

public struct SharedMediaEntry : INetworkSerializable, IEquatable<SharedMediaEntry>
{
    public ulong Sequence;
    public ulong OwnerClientId;
    public FixedString64Bytes MediaId;
    public FixedString128Bytes Sha256;
    public FixedString32Bytes Kind;
    public FixedString32Bytes Mime;
    public FixedString64Bytes DisplayName;
    public int Width;
    public int Height;
    public int ByteSize;
    public int ThumbnailByteSize;
    public MediaShareState State;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Sequence);
        serializer.SerializeValue(ref OwnerClientId);
        serializer.SerializeValue(ref MediaId);
        serializer.SerializeValue(ref Sha256);
        serializer.SerializeValue(ref Kind);
        serializer.SerializeValue(ref Mime);
        serializer.SerializeValue(ref DisplayName);
        serializer.SerializeValue(ref Width);
        serializer.SerializeValue(ref Height);
        serializer.SerializeValue(ref ByteSize);
        serializer.SerializeValue(ref ThumbnailByteSize);
        serializer.SerializeValue(ref State);
    }

    public bool Equals(SharedMediaEntry other)
    {
        return Sequence == other.Sequence
               && OwnerClientId == other.OwnerClientId
               && MediaId.Equals(other.MediaId)
               && Sha256.Equals(other.Sha256)
               && Kind.Equals(other.Kind)
               && Mime.Equals(other.Mime)
               && DisplayName.Equals(other.DisplayName)
               && Width == other.Width
               && Height == other.Height
               && ByteSize == other.ByteSize
               && ThumbnailByteSize == other.ThumbnailByteSize
               && State == other.State;
    }

    public override bool Equals(object obj)
    {
        return obj is SharedMediaEntry other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Sequence.GetHashCode();
    }
}

public class M_LobbyMediaCatalog : NetworkBehaviour
{
    [Header("Catalog Limits")]
    [SerializeField] private int maxSessionHistory = 50;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging = true;

    private readonly NetworkList<SharedMediaEntry> _entries = new NetworkList<SharedMediaEntry>(
        null,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private ulong _nextSequence = 1;

    public event Action<SharedMediaEntry> EntryAddedOrUpdated;
    public event Action CatalogChanged;

    public int Count => _entries.Count;

    public override void OnNetworkSpawn()
    {
        _entries.OnListChanged += HandleListChanged;

        for (int i = 0; i < _entries.Count; i++)
            EntryAddedOrUpdated?.Invoke(_entries[i]);

        CatalogChanged?.Invoke();
    }

    public override void OnNetworkDespawn()
    {
        _entries.OnListChanged -= HandleListChanged;
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        _entries.Dispose();
    }

    public SharedMediaEntry GetEntryAt(int index)
    {
        return _entries[index];
    }

    public bool TryGetBySequence(ulong sequence, out SharedMediaEntry entry)
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Sequence == sequence)
            {
                entry = _entries[i];
                return true;
            }
        }

        entry = default;
        return false;
    }

    public bool TryGetByMediaId(string mediaId, out SharedMediaEntry entry)
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            if (string.Equals(_entries[i].MediaId.ToString(), mediaId, StringComparison.OrdinalIgnoreCase))
            {
                entry = _entries[i];
                return true;
            }
        }

        entry = default;
        return false;
    }

    public bool TryGetBySha256(string sha256, out SharedMediaEntry entry)
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            if (string.Equals(_entries[i].Sha256.ToString(), sha256, StringComparison.OrdinalIgnoreCase))
            {
                entry = _entries[i];
                return true;
            }
        }

        entry = default;
        return false;
    }

    public ulong ReserveSequenceServer()
    {
        if (!IsServer)
        {
            Debug.LogWarning("[M_LobbyMediaCatalog] Only the server can reserve media sequence numbers.");
            return 0;
        }

        return _nextSequence++;
    }

    public bool AddOrUpdateServer(SharedMediaEntry entry)
    {
        if (!IsServer)
        {
            Debug.LogWarning("[M_LobbyMediaCatalog] AddOrUpdateServer ignored on non-server.");
            return false;
        }

        if (entry.Sequence == 0)
            entry.Sequence = ReserveSequenceServer();

        int existingIndex = FindIndexByShaOrSequence(entry.Sequence, entry.Sha256.ToString());
        if (existingIndex >= 0)
        {
            SharedMediaEntry existing = _entries[existingIndex];
            entry.Sequence = existing.Sequence;
            _entries[existingIndex] = entry;
        }
        else
        {
            while (_entries.Count >= Mathf.Max(1, maxSessionHistory))
                _entries.RemoveAt(0);

            _entries.Add(entry);
            if (entry.Sequence >= _nextSequence)
                _nextSequence = entry.Sequence + 1;
        }

        if (verboseLogging)
            Debug.Log($"[M_LobbyMediaCatalog] {entry.State} seq={entry.Sequence} sha={entry.Sha256}");

        EntryAddedOrUpdated?.Invoke(entry);
        CatalogChanged?.Invoke();
        return true;
    }

    public bool SetStateServer(ulong sequence, MediaShareState state)
    {
        if (!IsServer)
            return false;

        for (int i = 0; i < _entries.Count; i++)
        {
            SharedMediaEntry entry = _entries[i];
            if (entry.Sequence != sequence)
                continue;

            entry.State = state;
            _entries[i] = entry;
            EntryAddedOrUpdated?.Invoke(entry);
            CatalogChanged?.Invoke();
            return true;
        }

        return false;
    }

    public static SharedMediaEntry CreateEntryFromRecord(M_MediaRecord record, ulong ownerClientId, ulong sequence, int thumbnailByteSize, MediaShareState state)
    {
        SharedMediaEntry entry = new SharedMediaEntry
        {
            Sequence = sequence,
            OwnerClientId = ownerClientId,
            MediaId = ToFixed64(record?.mediaId),
            Sha256 = ToFixed128(record?.sha256),
            Kind = ToFixed32(record?.kind ?? "image"),
            Mime = ToFixed32(record?.mime ?? "image/jpeg"),
            DisplayName = ToFixed64(record?.displayName),
            Width = record?.width ?? 0,
            Height = record?.height ?? 0,
            ByteSize = record?.byteSize ?? 0,
            ThumbnailByteSize = thumbnailByteSize,
            State = state
        };

        return entry;
    }

    private void HandleListChanged(NetworkListEvent<SharedMediaEntry> changeEvent)
    {
        if (changeEvent.Type == NetworkListEvent<SharedMediaEntry>.EventType.Clear)
        {
            CatalogChanged?.Invoke();
            return;
        }

        EntryAddedOrUpdated?.Invoke(changeEvent.Value);
        CatalogChanged?.Invoke();
    }

    private int FindIndexByShaOrSequence(ulong sequence, string sha256)
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            SharedMediaEntry entry = _entries[i];
            if (entry.Sequence == sequence)
                return i;

            if (!string.IsNullOrWhiteSpace(sha256)
                && string.Equals(entry.Sha256.ToString(), sha256, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static FixedString32Bytes ToFixed32(string value)
    {
        return new FixedString32Bytes(TruncateUtf16(value, 29));
    }

    private static FixedString64Bytes ToFixed64(string value)
    {
        return new FixedString64Bytes(TruncateUtf16(value, 61));
    }

    private static FixedString128Bytes ToFixed128(string value)
    {
        return new FixedString128Bytes(TruncateUtf16(value, 125));
    }

    private static string TruncateUtf16(string value, int maxChars)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Length <= maxChars ? value : value.Substring(0, maxChars);
    }
}
