/*
 * Script Name: M_NetworkedPhotoSync.cs
 * Author: Mason Prather
 * Description: Bridges local image selections and phone uploads into XRINetworkPlayer shared-media synchronization so connected clients display the same photo.
 * Project Role: Shared media relay between the local gallery/display layer and the multiplayer avatar/network layer.
 * Key Inputs: Prepared image payloads, selected gallery items, loaded Texture2D instances, and XRINetworkPlayer shared-media callbacks.
 * Key Outputs: Local photo display updates, JPEG-encoded network payloads, and remote shared-media display updates.
 */

using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using XRMultiplayer;

/// <summary>
/// Bridges local media imports and phone uploads with the active multiplayer player object.
/// Local uploads are displayed immediately, then forwarded through XRINetworkPlayer so
/// every connected client applies the same image to their own shared photo panel.
/// </summary>
public class M_NetworkedPhotoSync : MonoBehaviour
{
    [SerializeField] private M_QuestPhotoDisplay photoDisplay;
    [SerializeField, Range(1, 100)] private int jpegQuality = 75;
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
        XRINetworkPlayer.onSharedMediaReceived -= HandleSharedMediaReceived;
        XRINetworkPlayer.onSharedMediaReceived += HandleSharedMediaReceived;
        XRINetworkPlayer.onLocalPlayerSpawned -= HandleLocalPlayerSpawned;
        XRINetworkPlayer.onLocalPlayerSpawned += HandleLocalPlayerSpawned;

        if (clearDisplayWhenSceneReceiverInitializes)
            ClearLocalDisplayForJoin("scene receiver enabled");
    }

    private void OnDisable()
    {
        XRINetworkPlayer.onSharedMediaReceived -= HandleSharedMediaReceived;
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

        BroadcastImageBytes(image.fileName, image.bytes);
    }

    public void BroadcastTexture(Texture2D texture, string fileName)
    {
        StartCoroutine(BroadcastTextureCoroutine(texture, fileName));
    }

    public void BroadcastImageBytes(string fileName, byte[] encodedBytes)
    {
        StartCoroutine(BroadcastImageBytesCoroutine(fileName, encodedBytes));
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

        yield return BroadcastImageBytesCoroutine(item.fileName, encodedBytes);

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

        yield return BroadcastImageBytesCoroutine(fileName, encodedBytes);
    }

    private IEnumerator BroadcastImageBytesCoroutine(string fileName, byte[] encodedBytes)
    {
        if (encodedBytes == null || encodedBytes.Length == 0)
        {
            Debug.LogWarning("[M_NetworkedPhotoSync] BroadcastImageBytes ignored an empty payload.");
            yield break;
        }

        string safeFileName = string.IsNullOrWhiteSpace(fileName) ? "Uploaded photo" : fileName;
        if (IsDuplicateRecentBroadcast(safeFileName, encodedBytes))
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
            Debug.LogWarning("[M_NetworkedPhotoSync] Shared image applied locally, but no spawned local network player was available for broadcast.");
            yield break;
        }

        MarkBroadcastPayload(safeFileName, encodedBytes);

        if (verboseLogging)
            Debug.Log($"[M_NetworkedPhotoSync] Broadcasting '{safeFileName}' ({encodedBytes.Length} bytes).");

        InvokeBroadcastSharedMedia(localPlayer, safeFileName, encodedBytes);
    }

    private void HandleSharedMediaReceived(string fileName, byte[] encodedBytes)
    {
        if (!isActiveAndEnabled || photoDisplay == null || encodedBytes == null || encodedBytes.Length == 0)
            return;

        _receivedSharedMediaThisScene = true;

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
            Debug.Log($"[M_NetworkedPhotoSync] Cleared local image display on join ({reason}).");
    }

    private bool IsDuplicateRecentBroadcast(string fileName, byte[] bytes)
    {
        if (duplicateBroadcastWindowSeconds <= 0f)
            return false;

        int signature = ComputePayloadSignature(fileName, bytes);
        return signature == _lastBroadcastSignature &&
               Time.unscaledTime - _lastBroadcastTime <= duplicateBroadcastWindowSeconds;
    }

    private void MarkBroadcastPayload(string fileName, byte[] bytes)
    {
        _lastBroadcastSignature = ComputePayloadSignature(fileName, bytes);
        _lastBroadcastTime = Time.unscaledTime;
    }

    private static int ComputePayloadSignature(string fileName, byte[] bytes)
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + (fileName != null ? fileName.GetHashCode() : 0);
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

    private void InvokeBroadcastSharedMedia(XRINetworkPlayer localPlayer, string fileName, byte[] encodedBytes)
    {
        if (localPlayer == null)
        {
            Debug.LogWarning("[M_NetworkedPhotoSync] Local network player is unavailable.");
            return;
        }

        localPlayer.BroadcastSharedMedia(fileName, encodedBytes);
    }
}
