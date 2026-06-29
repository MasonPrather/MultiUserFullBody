/*
 * Script Name: M_MediaLibrary.cs
 * Description: Persistent Quest-local media library rooted under Application.persistentDataPath.
 * Project Role: Owns privacy boundary between permanent user media and temporary session media.
 */

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class M_MediaLibrary : MonoBehaviour
{
    [Header("Storage")]
    [Tooltip("Relative directory below Application.persistentDataPath. Do not use Resources, StreamingAssets, or PlayerPrefs for user media.")]
    [SerializeField] private string libraryRelativeRoot = M_MediaPaths.LibraryRelativeRoot;

    [Tooltip("Load manifest and create folders during Awake.")]
    [SerializeField] private bool initializeOnAwake = true;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging = true;

    private readonly List<M_MediaRecord> _records = new List<M_MediaRecord>();
    private readonly Dictionary<string, M_MediaRecord> _byMediaId = new Dictionary<string, M_MediaRecord>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, M_MediaRecord> _bySha256 = new Dictionary<string, M_MediaRecord>(StringComparer.OrdinalIgnoreCase);

    private M_MediaManifestStore _store;
    private bool _initialized;

    public event Action<IReadOnlyList<M_MediaRecord>> LibraryChanged;

    public string LibraryRootPath => Path.Combine(Application.persistentDataPath, libraryRelativeRoot);
    public string ImagesRootPath => Path.Combine(LibraryRootPath, M_MediaPaths.ImagesDirectory);
    public string VideosRootPath => Path.Combine(LibraryRootPath, M_MediaPaths.VideosDirectory);
    public string ThumbsRootPath => Path.Combine(LibraryRootPath, M_MediaPaths.ThumbsDirectory);
    public string IncomingRootPath => Path.Combine(LibraryRootPath, M_MediaPaths.IncomingDirectory);
    public string SessionCacheRootPath => Path.Combine(LibraryRootPath, M_MediaPaths.SessionCacheDirectory);
    public IReadOnlyList<M_MediaRecord> Records => _records;

    private void Awake()
    {
        if (initializeOnAwake)
            Initialize();
    }

    public void Initialize()
    {
        if (_initialized)
            return;

        Directory.CreateDirectory(LibraryRootPath);
        Directory.CreateDirectory(ImagesRootPath);
        Directory.CreateDirectory(VideosRootPath);
        Directory.CreateDirectory(ThumbsRootPath);
        Directory.CreateDirectory(IncomingRootPath);
        Directory.CreateDirectory(SessionCacheRootPath);

        _store = new M_MediaManifestStore(LibraryRootPath);
        LoadManifest();
        _initialized = true;

        if (verboseLogging)
            Debug.Log($"[M_MediaLibrary] Ready at {LibraryRootPath} with {_records.Count} records.");
    }

    public void Reload()
    {
        Initialize();
        LoadManifest();
        LibraryChanged?.Invoke(_records);
    }

    public M_MediaRecord GetByMediaId(string mediaId)
    {
        Initialize();
        if (string.IsNullOrWhiteSpace(mediaId))
            return null;

        _byMediaId.TryGetValue(mediaId, out M_MediaRecord record);
        return record;
    }

    public M_MediaRecord GetBySha256(string sha256)
    {
        Initialize();
        if (string.IsNullOrWhiteSpace(sha256))
            return null;

        _bySha256.TryGetValue(sha256, out M_MediaRecord record);
        return record;
    }

    public string ResolveFullPath(M_MediaRecord record)
    {
        if (record == null)
            return null;

        return ResolveRelativePath(record.relativePath);
    }

    public string ResolveThumbnailPath(M_MediaRecord record)
    {
        if (record == null)
            return null;

        return ResolveRelativePath(record.thumbPath);
    }

    public string ResolveRelativePath(string relativePath)
    {
        Initialize();
        return M_MediaPaths.CombineUnderRoot(LibraryRootPath, relativePath);
    }

    public bool ContainsCompleteFiles(M_MediaRecord record)
    {
        string full = ResolveFullPath(record);
        string thumb = ResolveThumbnailPath(record);
        return !string.IsNullOrEmpty(full) && !string.IsNullOrEmpty(thumb) && File.Exists(full) && File.Exists(thumb);
    }

    public bool AddOrUpdateRecord(M_MediaRecord record)
    {
        Initialize();
        if (record == null || string.IsNullOrWhiteSpace(record.mediaId) || string.IsNullOrWhiteSpace(record.sha256))
            return false;

        M_MediaRecord existingByHash = GetBySha256(record.sha256);
        if (existingByHash != null)
        {
            CopyRecordFields(record, existingByHash, keepMediaId: true);
            SaveAndNotify();
            return true;
        }

        M_MediaRecord existingById = GetByMediaId(record.mediaId);
        if (existingById != null)
        {
            CopyRecordFields(record, existingById, keepMediaId: false);
        }
        else
        {
            _records.Add(record);
        }

        RebuildIndexes();
        SaveAndNotify();
        return true;
    }

    public bool DeleteLocalMedia(string mediaId)
    {
        Initialize();
        M_MediaRecord record = GetByMediaId(mediaId);
        if (record == null)
            return false;

        // Deleting from the permanent library is intentionally separate from removing a shared catalog entry. Shared media
        // shown in a lobby should never erase the owner's private Quest-local original.
        TryDeleteFile(ResolveFullPath(record));
        TryDeleteFile(ResolveThumbnailPath(record));

        _records.Remove(record);
        RebuildIndexes();
        SaveAndNotify();
        return true;
    }

    public string CreateIncomingTempPath(string extension)
    {
        Initialize();
        string safeExt = string.IsNullOrWhiteSpace(extension) ? ".bin" : extension;
        if (!safeExt.StartsWith(".", StringComparison.Ordinal))
            safeExt = "." + safeExt;

        return Path.Combine(IncomingRootPath, $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Guid.NewGuid():N}{safeExt}");
    }

    private void LoadManifest()
    {
        _records.Clear();
        M_MediaManifest manifest = _store.Load();

        if (manifest.records != null)
        {
            for (int i = 0; i < manifest.records.Count; i++)
            {
                M_MediaRecord record = manifest.records[i];
                if (IsRecordUsable(record))
                    _records.Add(record);
            }
        }

        RebuildIndexes();
    }

    private void SaveAndNotify()
    {
        M_MediaManifest manifest = new M_MediaManifest { version = 1, records = new List<M_MediaRecord>(_records) };
        _store.Save(manifest);
        LibraryChanged?.Invoke(_records);
    }

    private void RebuildIndexes()
    {
        _byMediaId.Clear();
        _bySha256.Clear();

        for (int i = 0; i < _records.Count; i++)
        {
            M_MediaRecord record = _records[i];
            if (record == null)
                continue;

            if (!string.IsNullOrWhiteSpace(record.mediaId))
                _byMediaId[record.mediaId] = record;

            if (!string.IsNullOrWhiteSpace(record.sha256) && !_bySha256.ContainsKey(record.sha256))
                _bySha256[record.sha256] = record;
        }
    }

    private static bool IsRecordUsable(M_MediaRecord record)
    {
        return record != null
               && !string.IsNullOrWhiteSpace(record.mediaId)
               && !string.IsNullOrWhiteSpace(record.sha256)
               && M_MediaPaths.IsSafeRelativePath(record.relativePath)
               && M_MediaPaths.IsSafeRelativePath(record.thumbPath);
    }

    private static void CopyRecordFields(M_MediaRecord source, M_MediaRecord target, bool keepMediaId)
    {
        if (!keepMediaId)
            target.mediaId = source.mediaId;

        target.kind = source.kind;
        target.mime = source.mime;
        target.sha256 = source.sha256;
        target.relativePath = source.relativePath;
        target.thumbPath = source.thumbPath;
        target.width = source.width;
        target.height = source.height;
        target.byteSize = source.byteSize;
        target.createdUnixMs = source.createdUnixMs;
        target.displayName = source.displayName;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                File.Delete(path);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[M_MediaLibrary] Failed to delete '{path}': {e.Message}");
        }
    }
}
