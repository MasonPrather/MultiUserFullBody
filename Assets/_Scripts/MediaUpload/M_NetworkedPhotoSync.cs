/*
 * Script Name: M_NetworkedPhotoSync.cs
 * Author: Mason Prather
 * Description: Bridges local media selections and phone uploads into XRINetworkPlayer shared-media synchronization so connected clients display the same media.
 * Project Role: Shared media relay between the local gallery/display layer and the multiplayer avatar/network layer.
 * Key Inputs: Prepared image/video payloads, selected gallery items, loaded Texture2D instances, and XRINetworkPlayer shared-media callbacks.
 * Key Outputs: Local media display updates, JPEG/video network payloads, and remote shared-media display updates.
 */

using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using XRMultiplayer;

/// <summary>
/// Bridges local media imports and phone uploads with the active multiplayer player object.
/// Local uploads are displayed immediately, then forwarded through XRINetworkPlayer so
/// every connected client applies the same media to their own shared media panel.
/// </summary>
public class M_NetworkedPhotoSync : MonoBehaviour
{
    [SerializeField] private M_QuestPhotoDisplay photoDisplay;
    [SerializeField, Range(1, 100)] private int jpegQuality = 75;
    [SerializeField] private int maxSharedVideoBytes = 64 * 1024 * 1024;
    [SerializeField] private float localPlayerLookupTimeout = 3f;
    [SerializeField] private float duplicateBroadcastWindowSeconds = 2f;
    [SerializeField] private bool verboseLogging = true;
    [SerializeField] private bool clearDisplayWhenSceneReceiverInitializes = true;
    [SerializeField] private bool clearDisplayWhenLocalPlayerSpawns = true;

    private int _lastBroadcastSignature;
    private float _lastBroadcastTime = -999f;
    private bool _clearedLocalDisplayForJoin;
    private bool _receivedSharedMediaThisScene;
    private bool _hasLocalMediaInteraction;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void BootstrapSceneReceiver()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        EnsureSceneReceiver();
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureSceneReceiver();
    }

    private static void EnsureSceneReceiver()
    {
        M_NetworkedPhotoSync existingSync = UnityEngine.Object.FindFirstObjectByType<M_NetworkedPhotoSync>();
        M_QuestPhotoDisplay display = ResolveSceneDisplay();

        if (existingSync != null)
        {
            if (existingSync.photoDisplay == null && display != null)
                existingSync.Initialize(display);

            return;
        }

        if (display == null)
            return;

        M_NetworkedPhotoSync sync = display.GetComponent<M_NetworkedPhotoSync>();
        if (sync == null)
            sync = display.gameObject.AddComponent<M_NetworkedPhotoSync>();

        sync.Initialize(display);
    }

    private static M_QuestPhotoDisplay ResolveSceneDisplay()
    {
        M_QuestPhotoDisplay display = UnityEngine.Object.FindFirstObjectByType<M_QuestPhotoDisplay>();
        if (display != null)
            return display;

        RawImage rawImage = FindRawImageByName("LatestUploadImage");
        if (rawImage == null)
            return null;

        display = rawImage.GetComponent<M_QuestPhotoDisplay>();
        if (display == null)
            display = rawImage.gameObject.AddComponent<M_QuestPhotoDisplay>();

        if (display.targetRawImage == null)
            display.targetRawImage = rawImage;

        if (display.rawImageFitBounds == null)
            display.rawImageFitBounds = rawImage.rectTransform != null
                ? rawImage.rectTransform.parent as RectTransform
                : null;

        return display;
    }

    private static RawImage FindRawImageByName(string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName))
            return null;

        RawImage[] rawImages = UnityEngine.Object.FindObjectsByType<RawImage>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        RawImage inactiveMatch = null;

        for (int i = 0; i < rawImages.Length; i++)
        {
            RawImage rawImage = rawImages[i];
            if (rawImage == null || !string.Equals(rawImage.name, objectName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (rawImage.gameObject.activeInHierarchy)
                return rawImage;

            if (inactiveMatch == null)
                inactiveMatch = rawImage;
        }

        return inactiveMatch;
    }

    public void Initialize(M_QuestPhotoDisplay display)
    {
        if (photoDisplay == null)
            photoDisplay = display;

        if (clearDisplayWhenSceneReceiverInitializes)
            ClearLocalDisplayForJoin("scene receiver initialized");
    }

    private void Awake()
    {
        if (photoDisplay == null)
            photoDisplay = GetComponent<M_QuestPhotoDisplay>();
    }

    private void OnEnable()
    {
        XRINetworkPlayer.onSharedMediaPayloadReceived -= HandleSharedMediaReceived;
        XRINetworkPlayer.onSharedMediaPayloadReceived += HandleSharedMediaReceived;
        XRINetworkPlayer.onLocalPlayerSpawned -= HandleLocalPlayerSpawned;
        XRINetworkPlayer.onLocalPlayerSpawned += HandleLocalPlayerSpawned;

        if (clearDisplayWhenSceneReceiverInitializes)
            ClearLocalDisplayForJoin("scene receiver enabled");
    }

    private void OnDisable()
    {
        XRINetworkPlayer.onSharedMediaPayloadReceived -= HandleSharedMediaReceived;
        XRINetworkPlayer.onLocalPlayerSpawned -= HandleLocalPlayerSpawned;
    }

    public void UploadSelectedItem(M_QuestGalleryAndroidBridge bridge, M_QuestGalleryAndroidBridge.GalleryItem item)
    {
        StartCoroutine(UploadSelectedItemCoroutine(bridge, item));
    }

    public void BroadcastPreparedImage(ImagePicker.PreparedImage image)
    {
        if (image == null)
        {
            Debug.LogWarning("[M_NetworkedPhotoSync] BroadcastPreparedImage ignored null image.");
            return;
        }

        BroadcastImageBytes(image.fileName, image.bytes, image.mimeType);
    }

    public void BroadcastTexture(Texture2D texture, string fileName)
    {
        StartCoroutine(BroadcastTextureCoroutine(texture, fileName));
    }

    public void BroadcastImageBytes(string fileName, byte[] encodedBytes)
    {
        BroadcastImageBytes(fileName, encodedBytes, null);
    }

    public void BroadcastImageBytes(string fileName, byte[] encodedBytes, string mime)
    {
        StartCoroutine(BroadcastMediaBytesCoroutine(fileName, encodedBytes, M_MediaTypeUtility.KindImage, mime));
    }

    public void BroadcastVideoFile(string videoPath, string fileName = null)
    {
        BroadcastMediaFile(videoPath, fileName, M_MediaTypeUtility.KindVideo, null);
    }

    public void BroadcastMediaFile(string path, string fileName = null, string kind = null, string mime = null)
    {
        StartCoroutine(BroadcastMediaFileCoroutine(path, fileName, kind, mime));
    }

    private XRINetworkPlayer ResolveLocalPlayer()
    {
        XRINetworkPlayer localPlayer = XRINetworkPlayer.LocalPlayer;
        if (localPlayer != null)
            return localPlayer;

        XRINetworkPlayer[] players = UnityEngine.Object.FindObjectsByType<XRINetworkPlayer>(FindObjectsSortMode.None);
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] != null && players[i].IsLocalPlayer)
                return players[i];
        }

        return null;
    }

    private IEnumerator UploadSelectedItemCoroutine(M_QuestGalleryAndroidBridge bridge, M_QuestGalleryAndroidBridge.GalleryItem item)
    {
        if (bridge == null)
        {
            Debug.LogWarning("[M_NetworkedPhotoSync] UploadSelectedItem failed: bridge is null.");
            yield break;
        }

        if (item == null || (string.IsNullOrWhiteSpace(item.filePath) && string.IsNullOrWhiteSpace(item.contentUri)))
        {
            Debug.LogWarning("[M_NetworkedPhotoSync] UploadSelectedItem failed: item source is invalid.");
            yield break;
        }

        yield return null;

        int maxDimension = photoDisplay != null && photoDisplay.maxDisplayDimension > 0 ? photoDisplay.maxDisplayDimension : 2048;
        Texture2D localTexture = bridge.LoadFullTexture(item, maxDimension, markNonReadable: false);

        if (localTexture == null)
        {
            Debug.LogWarning($"[M_NetworkedPhotoSync] Failed to load '{item.fileName}' for upload.");
            yield break;
        }

        bool displayOwnsTexture = false;
        if (photoDisplay != null)
        {
            photoDisplay.DisplayTexture(localTexture, item.fileName);
            displayOwnsTexture = true;
        }

        byte[] encodedBytes = ImageConversion.EncodeToJPG(localTexture, Mathf.Clamp(jpegQuality, 1, 100));
        if (encodedBytes == null || encodedBytes.Length == 0)
        {
            Debug.LogWarning($"[M_NetworkedPhotoSync] Failed to encode '{item.fileName}' for synchronization.");

            if (!displayOwnsTexture)
                Destroy(localTexture);

            yield break;
        }

        yield return BroadcastMediaBytesCoroutine(item.fileName, encodedBytes, M_MediaTypeUtility.KindImage, "image/jpeg");

        if (!displayOwnsTexture)
            Destroy(localTexture);
    }

    private IEnumerator BroadcastTextureCoroutine(Texture2D texture, string fileName)
    {
        if (texture == null)
        {
            Debug.LogWarning("[M_NetworkedPhotoSync] BroadcastTexture ignored null texture.");
            yield break;
        }

        byte[] encodedBytes = null;

        try
        {
            encodedBytes = ImageConversion.EncodeToJPG(texture, Mathf.Clamp(jpegQuality, 1, 100));
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[M_NetworkedPhotoSync] Failed to encode texture '{fileName}' for synchronization: {ex.Message}");
        }

        yield return BroadcastMediaBytesCoroutine(fileName, encodedBytes, M_MediaTypeUtility.KindImage, "image/jpeg");
    }

    private IEnumerator BroadcastMediaFileCoroutine(string path, string fileName, string kind, string mime)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            Debug.LogWarning($"[M_NetworkedPhotoSync] BroadcastMediaFile ignored missing file '{path}'.");
            yield break;
        }

        string safeFileName = string.IsNullOrWhiteSpace(fileName) ? Path.GetFileName(path) : fileName;
        string normalizedMime = M_MediaTypeUtility.NormalizeMime(mime, safeFileName, null);
        string normalizedKind = M_MediaTypeUtility.IsVideoKind(kind) || M_MediaTypeUtility.IsVideoMime(normalizedMime)
            ? M_MediaTypeUtility.KindVideo
            : M_MediaTypeUtility.KindImage;

        FileInfo fileInfo = new FileInfo(path);
        if (M_MediaTypeUtility.IsVideoKind(normalizedKind) && fileInfo.Length > Mathf.Max(1, maxSharedVideoBytes))
        {
            Debug.LogWarning($"[M_NetworkedPhotoSync] Video '{safeFileName}' is too large to share ({fileInfo.Length} bytes > {maxSharedVideoBytes} bytes).");
            yield break;
        }

        byte[] bytes = null;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[M_NetworkedPhotoSync] Failed to read media file '{path}' for broadcast: {ex.Message}");
        }

        yield return BroadcastMediaBytesCoroutine(safeFileName, bytes, normalizedKind, normalizedMime);
    }

    private IEnumerator BroadcastMediaBytesCoroutine(string fileName, byte[] encodedBytes, string kind, string mime)
    {
        if (encodedBytes == null || encodedBytes.Length == 0)
        {
            Debug.LogWarning("[M_NetworkedPhotoSync] BroadcastMediaBytes ignored an empty payload.");
            yield break;
        }

        string normalizedMime = M_MediaTypeUtility.NormalizeMime(mime, fileName, encodedBytes);
        string normalizedKind = M_MediaTypeUtility.IsVideoKind(kind) || M_MediaTypeUtility.IsVideoMime(normalizedMime)
            ? M_MediaTypeUtility.KindVideo
            : M_MediaTypeUtility.KindImage;

        if (M_MediaTypeUtility.IsVideoKind(normalizedKind) && encodedBytes.Length > Mathf.Max(1, maxSharedVideoBytes))
        {
            Debug.LogWarning($"[M_NetworkedPhotoSync] Video '{fileName}' is too large to share ({encodedBytes.Length} bytes > {maxSharedVideoBytes} bytes).");
            yield break;
        }

        string safeFileName = string.IsNullOrWhiteSpace(fileName)
            ? (M_MediaTypeUtility.IsVideoKind(normalizedKind) ? "Uploaded video" : "Uploaded photo")
            : fileName;

        if (IsDuplicateRecentBroadcast(safeFileName, normalizedKind, normalizedMime, encodedBytes))
        {
            if (verboseLogging)
                Debug.Log($"[M_NetworkedPhotoSync] Skipped duplicate shared media broadcast: {safeFileName}");

            yield break;
        }

        _hasLocalMediaInteraction = true;

        XRINetworkPlayer localPlayer = null;
        float playerLookupTimer = 0f;
        float timeout = Mathf.Max(0.1f, localPlayerLookupTimeout);

        while (playerLookupTimer < timeout)
        {
            localPlayer = ResolveLocalPlayer();
            if (IsReadyLocalPlayer(localPlayer))
                break;

            playerLookupTimer += Time.unscaledDeltaTime;
            yield return null;
        }

        if (!IsReadyLocalPlayer(localPlayer))
        {
            Debug.LogWarning("[M_NetworkedPhotoSync] Shared media applied locally, but no spawned local network player was available for broadcast.");
            yield break;
        }

        MarkBroadcastPayload(safeFileName, normalizedKind, normalizedMime, encodedBytes);

        if (verboseLogging)
            Debug.Log($"[M_NetworkedPhotoSync] Broadcasting '{safeFileName}' as {normalizedKind}/{normalizedMime} ({encodedBytes.Length} bytes).");

        InvokeBroadcastSharedMedia(localPlayer, safeFileName, encodedBytes, normalizedKind, normalizedMime);
    }

    private void HandleSharedMediaReceived(string fileName, string kind, string mime, byte[] encodedBytes)
    {
        if (!isActiveAndEnabled || photoDisplay == null || encodedBytes == null || encodedBytes.Length == 0)
            return;

        _receivedSharedMediaThisScene = true;

        string normalizedMime = M_MediaTypeUtility.NormalizeMime(mime, fileName, encodedBytes);
        if (M_MediaTypeUtility.IsVideoKind(kind) || M_MediaTypeUtility.IsVideoMime(normalizedMime))
        {
            DisplayReceivedVideo(fileName, normalizedMime, encodedBytes);
            return;
        }

        DisplayReceivedImage(fileName, encodedBytes);
    }

    private void DisplayReceivedImage(string fileName, byte[] encodedBytes)
    {
        Texture2D syncedTexture = new Texture2D(2, 2, TextureFormat.RGBA32, true);

        if (!ImageConversion.LoadImage(syncedTexture, encodedBytes, markNonReadable: false))
        {
            Destroy(syncedTexture);
            Debug.LogWarning($"[M_NetworkedPhotoSync] Failed to decode synchronized image '{fileName}'.");
            return;
        }

        syncedTexture.wrapMode = TextureWrapMode.Clamp;
        syncedTexture.filterMode = FilterMode.Trilinear;
        syncedTexture.anisoLevel = 4;

        if (!photoDisplay.keepTextureReadable)
            syncedTexture.Apply(updateMipmaps: true, makeNoLongerReadable: true);

        if (verboseLogging)
            Debug.Log($"[M_NetworkedPhotoSync] Applied synchronized image '{fileName}' ({syncedTexture.width}x{syncedTexture.height}).");

        photoDisplay.DisplayTexture(syncedTexture, fileName);
    }

    private void DisplayReceivedVideo(string fileName, string mime, byte[] videoBytes)
    {
        string normalizedMime = M_MediaTypeUtility.NormalizeMime(mime, fileName, videoBytes);
        if (!M_MediaTypeUtility.IsSupportedVideoMime(normalizedMime))
        {
            Debug.LogWarning($"[M_NetworkedPhotoSync] Ignored synchronized video '{fileName}' with unsupported MIME '{normalizedMime}'.");
            return;
        }

        if (videoBytes.Length > Mathf.Max(1, maxSharedVideoBytes))
        {
            Debug.LogWarning($"[M_NetworkedPhotoSync] Ignored synchronized video '{fileName}' because it exceeded {maxSharedVideoBytes} bytes.");
            return;
        }

        string videoPath = CacheReceivedVideo(fileName, normalizedMime, videoBytes);
        if (string.IsNullOrWhiteSpace(videoPath))
            return;

        if (verboseLogging)
            Debug.Log($"[M_NetworkedPhotoSync] Applied synchronized video '{fileName}' from '{videoPath}'.");

        photoDisplay.DisplayVideo(videoPath, string.IsNullOrWhiteSpace(fileName) ? Path.GetFileName(videoPath) : Path.GetFileName(fileName));
    }

    private static string CacheReceivedVideo(string fileName, string mime, byte[] videoBytes)
    {
        try
        {
            string sha = M_MediaHashUtility.Sha256Hex(videoBytes);
            if (string.IsNullOrWhiteSpace(sha))
                return null;

            string extension = M_MediaTypeUtility.ExtensionForMime(mime, fileName);
            string directory = Path.Combine(Application.persistentDataPath, "MURPM", "NetworkSharedMedia", "videos");
            Directory.CreateDirectory(directory);

            string path = Path.Combine(directory, $"{sha}{extension}");
            if (!File.Exists(path) || new FileInfo(path).Length != videoBytes.Length)
                File.WriteAllBytes(path, videoBytes);

            return path;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[M_NetworkedPhotoSync] Failed to cache synchronized video '{fileName}': {ex.Message}");
            return null;
        }
    }

    private void HandleLocalPlayerSpawned()
    {
        if (clearDisplayWhenLocalPlayerSpawns)
            ClearLocalDisplayForJoin("local player spawned");
    }

    private void ClearLocalDisplayForJoin(string reason)
    {
        if (_clearedLocalDisplayForJoin || _receivedSharedMediaThisScene || _hasLocalMediaInteraction || photoDisplay == null)
            return;

        photoDisplay.ClearDisplay();
        _clearedLocalDisplayForJoin = true;

        if (verboseLogging)
            Debug.Log($"[M_NetworkedPhotoSync] Cleared local media display on join ({reason}).");
    }

    private bool IsDuplicateRecentBroadcast(string fileName, string kind, string mime, byte[] bytes)
    {
        if (duplicateBroadcastWindowSeconds <= 0f)
            return false;

        int signature = ComputePayloadSignature(fileName, kind, mime, bytes);
        return signature == _lastBroadcastSignature &&
               Time.unscaledTime - _lastBroadcastTime <= duplicateBroadcastWindowSeconds;
    }

    private void MarkBroadcastPayload(string fileName, string kind, string mime, byte[] bytes)
    {
        _lastBroadcastSignature = ComputePayloadSignature(fileName, kind, mime, bytes);
        _lastBroadcastTime = Time.unscaledTime;
    }

    private static int ComputePayloadSignature(string fileName, string kind, string mime, byte[] bytes)
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + (fileName != null ? fileName.GetHashCode() : 0);
            hash = hash * 31 + (kind != null ? kind.GetHashCode() : 0);
            hash = hash * 31 + (mime != null ? mime.GetHashCode() : 0);
            hash = hash * 31 + (bytes != null ? bytes.Length : 0);

            if (bytes == null || bytes.Length == 0)
                return hash;

            int sampleCount = Mathf.Min(32, bytes.Length);
            for (int i = 0; i < sampleCount; i++)
                hash = hash * 31 + bytes[i];

            int start = Mathf.Max(sampleCount, bytes.Length - sampleCount);
            for (int i = start; i < bytes.Length; i++)
                hash = hash * 31 + bytes[i];

            return hash;
        }
    }

    private bool IsReadyLocalPlayer(XRINetworkPlayer localPlayer)
    {
        return localPlayer != null &&
               localPlayer.IsSpawned &&
               localPlayer.IsOwner;
    }

    private void InvokeBroadcastSharedMedia(XRINetworkPlayer localPlayer, string fileName, byte[] encodedBytes, string kind, string mime)
    {
        if (localPlayer == null)
        {
            Debug.LogWarning("[M_NetworkedPhotoSync] Local network player is unavailable.");
            return;
        }

        localPlayer.BroadcastSharedMedia(fileName, encodedBytes, kind, mime);
    }
}
