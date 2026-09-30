/*
 * Script Name: M_SessionMediaCache.cs
 * Description: Per-lobby cache for shared media that should not silently become permanent gallery media.
 * Project Role: Enforces the privacy rule that received media stays temporary until the user explicitly saves it.
 */

using System;
using System.IO;
using Unity.Netcode;
using UnityEngine;

public class M_SessionMediaCache : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private M_MediaLibrary mediaLibrary;

    [Header("Session")]
    [Tooltip("Optional stable id supplied by the lobby/session layer. If empty, a runtime local id is generated.")]
    [SerializeField] private string lobbyIdOverride;

    [SerializeField] private bool clearReceivedCacheOnStart = false;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging = true;

    private string _activeLobbyId;

    public string ActiveLobbyId
    {
        get
        {
            EnsureInitialized();
            return _activeLobbyId;
        }
    }

    public string SessionRootPath => Path.Combine(mediaLibrary.SessionCacheRootPath, ActiveLobbyId);
    public string ReceivedRootPath => Path.Combine(SessionRootPath, "shared_from_others");
    public string HostCacheRootPath => Path.Combine(SessionRootPath, "host_cache");

    private void Awake()
    {
        EnsureInitialized();
        if (clearReceivedCacheOnStart)
            ClearReceivedCache();
    }

    public void SetLobbyId(string lobbyId)
    {
        _activeLobbyId = SanitizeLobbyId(lobbyId);
        CreateDirectories();
    }

    public bool HasVerifiedFile(string identitySha256, M_MediaTransferFileRole role, bool hostCache, string expectedSha256 = null, string mime = null)
    {
        string path = GetCachePath(identitySha256, role, hostCache, mime);
        if (!File.Exists(path))
            return false;

        if (string.IsNullOrWhiteSpace(expectedSha256))
            return true;

        string actual = M_MediaHashUtility.Sha256HexForFile(path);
        return string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    public bool TryGetCachedPath(string identitySha256, M_MediaTransferFileRole role, bool preferHostCache, out string path, string mime = null)
    {
        EnsureInitialized();
        path = GetCachePath(identitySha256, role, preferHostCache, mime);
        if (File.Exists(path))
            return true;

        path = GetCachePath(identitySha256, role, !preferHostCache, mime);
        return File.Exists(path);
    }

    public string GetCachePath(string identitySha256, M_MediaTransferFileRole role, bool hostCache, string mime = null)
    {
        EnsureInitialized();
        string safeSha = M_MediaPaths.SafeIdFragment(identitySha256);
        string root = hostCache ? HostCacheRootPath : ReceivedRootPath;
        string directory = GetRoleDirectory(role, mime);
        string suffix = GetRoleSuffix(role, mime);
        return Path.Combine(root, directory, safeSha + suffix);
    }

    public string CreateTempPath(string transferId, M_MediaTransferFileRole role, bool hostCache)
    {
        EnsureInitialized();
        string root = hostCache ? HostCacheRootPath : ReceivedRootPath;
        string directory = Path.Combine(root, "temp");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{M_MediaPaths.SafeIdFragment(transferId)}_{role}_{Guid.NewGuid():N}.tmp");
    }

    public bool StoreVerifiedTemp(string tempPath, string identitySha256, string expectedContentSha256, M_MediaTransferFileRole role, bool hostCache, out string finalPath, out string failureReason, string mime = null)
    {
        finalPath = null;
        failureReason = null;

        try
        {
            if (string.IsNullOrWhiteSpace(tempPath) || !File.Exists(tempPath))
            {
                failureReason = "Transfer temp file was missing.";
                return false;
            }

            string actual = M_MediaHashUtility.Sha256HexForFile(tempPath);
            if (!string.Equals(actual, expectedContentSha256, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(tempPath);
                failureReason = "Image failed verification.";
                return false;
            }

            finalPath = GetCachePath(identitySha256, role, hostCache, mime);
            Directory.CreateDirectory(Path.GetDirectoryName(finalPath));
            if (File.Exists(finalPath))
                File.Delete(finalPath);

            File.Move(tempPath, finalPath);
            return true;
        }
        catch (Exception e)
        {
            failureReason = e.Message;
            Debug.LogWarning($"[M_SessionMediaCache] Failed to store verified temp file: {e.Message}");
            return false;
        }
    }

    public bool CopyIntoCache(string sourcePath, string identitySha256, M_MediaTransferFileRole role, bool hostCache, out string finalPath, out string contentSha256, string mime = null)
    {
        finalPath = null;
        contentSha256 = null;

        try
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                return false;

            contentSha256 = M_MediaHashUtility.Sha256HexForFile(sourcePath);
            finalPath = GetCachePath(identitySha256, role, hostCache, mime);
            Directory.CreateDirectory(Path.GetDirectoryName(finalPath));
            File.Copy(sourcePath, finalPath, true);

            if (verboseLogging)
                Debug.Log($"[M_SessionMediaCache] Cached {role} for {identitySha256} at {finalPath}");

            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[M_SessionMediaCache] CopyIntoCache failed: {e.Message}");
            return false;
        }
    }

    public void ClearReceivedCache()
    {
        EnsureInitialized();
        DeleteDirectoryContents(ReceivedRootPath);
        CreateDirectories();
    }

    public void ClearEntireSessionCache()
    {
        EnsureInitialized();
        DeleteDirectoryContents(SessionRootPath);
        CreateDirectories();
    }

    private void EnsureInitialized()
    {
        if (mediaLibrary == null)
            mediaLibrary = GetComponent<M_MediaLibrary>();

        if (mediaLibrary == null)
            mediaLibrary = FindFirstObjectByType<M_MediaLibrary>();

        if (mediaLibrary == null)
        {
            Debug.LogError("[M_SessionMediaCache] Missing M_MediaLibrary reference.");
            return;
        }

        mediaLibrary.Initialize();

        if (string.IsNullOrWhiteSpace(_activeLobbyId))
            _activeLobbyId = SanitizeLobbyId(lobbyIdOverride);

        if (string.IsNullOrWhiteSpace(_activeLobbyId))
            _activeLobbyId = BuildRuntimeSessionId();

        CreateDirectories();
    }

    private void CreateDirectories()
    {
        Directory.CreateDirectory(Path.Combine(ReceivedRootPath, "images"));
        Directory.CreateDirectory(Path.Combine(ReceivedRootPath, "videos"));
        Directory.CreateDirectory(Path.Combine(ReceivedRootPath, "thumbs"));
        Directory.CreateDirectory(Path.Combine(ReceivedRootPath, "temp"));
        Directory.CreateDirectory(Path.Combine(HostCacheRootPath, "images"));
        Directory.CreateDirectory(Path.Combine(HostCacheRootPath, "videos"));
        Directory.CreateDirectory(Path.Combine(HostCacheRootPath, "thumbs"));
        Directory.CreateDirectory(Path.Combine(HostCacheRootPath, "temp"));
    }

    private static string GetRoleDirectory(M_MediaTransferFileRole role, string mime)
    {
        if (role == M_MediaTransferFileRole.Thumbnail)
            return "thumbs";

        return M_MediaTypeUtility.IsVideoMime(mime) ? "videos" : "images";
    }

    private static string GetRoleSuffix(M_MediaTransferFileRole role, string mime)
    {
        if (role == M_MediaTransferFileRole.Thumbnail)
            return "_thumb.jpg";

        return M_MediaTypeUtility.IsVideoMime(mime)
            ? M_MediaTypeUtility.ExtensionForMime(mime)
            : ".jpg";
    }

    private static string BuildRuntimeSessionId()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            return $"net_{NetworkManager.Singleton.LocalClientId}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

        return $"local_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
    }

    private static string SanitizeLobbyId(string lobbyId)
    {
        string safe = M_MediaPaths.SafeIdFragment(lobbyId);
        return string.IsNullOrWhiteSpace(safe) ? string.Empty : safe;
    }

    private static void DeleteDirectoryContents(string root)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                return;

            foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                TryDelete(file);

            foreach (string directory in Directory.GetDirectories(root))
                Directory.Delete(directory, true);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[M_SessionMediaCache] Failed to clear cache '{root}': {e.Message}");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Cache cleanup is best effort; a failed delete should not interrupt a support session.
        }
    }
}
