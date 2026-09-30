/*
 * Script Name: M_QuestPhotoDisplay.cs
 * Author: Mason Prather
 * Description: Applies selected, uploaded, or synchronized textures to the project photo display target and associated status UI.
 * Project Role: Shared display surface for headset imports, phone uploads, and remote media synchronization.
 * Key Inputs: Gallery bridge items, provided Texture2D instances, RawImage bounds, renderer material settings, and display sizing configuration.
 * Key Outputs: RawImage/renderer texture updates, fitted display layout, status/file labels, fallback display state, and released runtime textures.
 */

using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using UnityEngine.Video;

/// <summary>
/// Displays a selected Quest-local photo on either:
/// - a UI RawImage
/// - a Renderer material slot
///
/// Replaces the currently displayed texture each time a new item is selected.
/// </summary>
public class M_QuestPhotoDisplay : MonoBehaviour
{
    [Header("UI Target (Optional)")]
    [Tooltip("Optional RawImage for showing the selected photo in UI.")]
    public RawImage targetRawImage;

    [Tooltip("Optional bounds rect used to fit the RawImage within a panel. Defaults to the RawImage's parent.")]
    public RectTransform rawImageFitBounds;

    [Header("Renderer Target (Optional)")]
    [Tooltip("Optional Renderer for showing the selected photo on a material.")]
    public Renderer targetRenderer;

    [Tooltip("Material slot index on the target renderer.")]
    public int materialIndex = 0;

    [Tooltip("Optional albedo property name override.")]
    public string albedoProperty = "_BaseMap";

    [Header("Info UI (Optional)")]
    [Tooltip("Optional label for showing the selected file name.")]
    public TMP_Text fileNameText;

    [Tooltip("Optional label for showing status / debug info.")]
    public TMP_Text statusText;

    [Header("Display Behavior")]
    [Tooltip("If greater than 0, selected photos will be downscaled to this maximum dimension before display.")]
    public int maxDisplayDimension = 2048;

    [Tooltip("If true, keep the displayed texture readable.")]
    public bool keepTextureReadable = false;

    [Tooltip("If true, set RawImage to native size after applying texture.")]
    public bool useNativeSizeForRawImage = false;

    [Tooltip("If true, fit the RawImage inside the panel bounds while preserving aspect ratio.")]
    public bool fitRawImageToBounds = true;

    [Tooltip("If true, scale the RawImage to exactly match the usable bounds height after margins are applied. This keeps the viewer at a fixed display height under the header area.")]
    public bool matchRawImageToBoundsHeight = true;

    [Tooltip("If true, height-fit images are clamped to the bounds width so very wide uploads stay inside the panel.")]
    public bool clampHeightFitToBoundsWidth = true;

    [Tooltip("If true, recalculate the RawImage size on the next frame after Unity layout has settled.")]
    public bool refreshRawImageLayoutAfterFrame = true;

    [Tooltip("Margins inside the bounds rect in the order Left, Right, Top, Bottom.")]
    public Vector4 rawImageFitMargins = Vector4.zero;

    [Tooltip("Optional fallback texture shown when nothing is selected.")]
    public Texture fallbackTexture;

    [Header("Video Display")]
    [Tooltip("If true, videos start playing as soon as Unity finishes preparing them.")]
    public bool autoPlayVideos = true;

    [Tooltip("If true, displayed videos loop on the shared media display.")]
    public bool loopVideos = true;

    [Tooltip("Fallback render width used when a video does not report dimensions before playback.")]
    public int fallbackVideoWidth = 1280;

    [Tooltip("Fallback render height used when a video does not report dimensions before playback.")]
    public int fallbackVideoHeight = 720;

    [Header("Local Notifications")]
    [Tooltip("Local-only audio notifier for new image/video display events.")]
    [SerializeField] private M_LocalMediaNotificationAudio notificationAudio;

    [Tooltip("Sound played locally when a new image is applied to this display.")]
    [SerializeField] private AudioClip newImageNotificationClip;

    [Tooltip("Sound played locally when a new video is applied to this display.")]
    [SerializeField] private AudioClip newVideoNotificationClip;

    [Tooltip("If true, play local notification sounds for image and video display events.")]
    [SerializeField] private bool playLocalNotifications = true;

    [Header("Debug")]
    [Tooltip("If true, log detailed display information.")]
    public bool verboseLogging = true;

    private Texture2D _currentTexture;
    private RenderTexture _currentVideoTexture;
    private Material _runtimeMaterial;
    private string _currentFileName;
    private Coroutine _layoutRefreshCoroutine;
    private Coroutine _videoDisplayCoroutine;
    private VideoPlayer _videoPlayer;
    private M_SharedMediaVideoAudio _videoAudio;

    private void Start()
    {
        ApplyFallback();
    }

    /// <summary>
    /// Clears the current photo and restores fallback state.
    /// </summary>
    public void ClearDisplay()
    {
        StopVideoPlayback(releaseTexture: true);
        ReleaseCurrentTexture();
        _currentFileName = string.Empty;
        ApplyFallback();

        if (fileNameText != null)
            fileNameText.text = "No photo selected";

        if (statusText != null)
            statusText.text = "Idle";

        if (verboseLogging)
            Debug.Log("[M_QuestPhotoDisplay] Display cleared.");
    }

    /// <summary>
    /// Loads and displays a full-size image using the provided Android bridge.
    /// </summary>
    public void DisplayPhoto(M_QuestGalleryAndroidBridge bridge, M_QuestGalleryAndroidBridge.GalleryItem item)
    {
        if (bridge == null)
        {
            Debug.LogError("[M_QuestPhotoDisplay] DisplayPhoto failed: bridge is null.");
            return;
        }

        if (item == null || string.IsNullOrEmpty(item.filePath))
        {
            Debug.LogWarning("[M_QuestPhotoDisplay] DisplayPhoto failed: invalid item/path.");
            return;
        }

        StartCoroutine(DisplayPhotoCoroutine(bridge, item));
    }

    /// <summary>
    /// Applies a texture that was already loaded elsewhere, such as a synchronized network upload.
    /// </summary>
    public void DisplayTexture(Texture2D texture, string fileName = "")
    {
        if (texture == null)
        {
            Debug.LogWarning("[M_QuestPhotoDisplay] DisplayTexture failed: texture is null.");
            return;
        }

        StopVideoPlayback(releaseTexture: true);
        ReleaseCurrentTexture();
        _currentTexture = texture;
        _currentFileName = fileName ?? string.Empty;

        ApplyTexture(_currentTexture);

        if (fileNameText != null)
            fileNameText.text = string.IsNullOrWhiteSpace(_currentFileName) ? "Uploaded photo" : _currentFileName;

        if (statusText != null)
            statusText.text = $"{_currentTexture.width} x {_currentTexture.height}";

        if (verboseLogging)
            Debug.Log($"[M_QuestPhotoDisplay] Displayed provided texture: {_currentFileName} ({_currentTexture.width}x{_currentTexture.height})");

        PlayImageNotification(_currentFileName);
    }

    public void DisplayVideo(string videoPath, string fileName = "")
    {
        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
        {
            Debug.LogWarning($"[M_QuestPhotoDisplay] DisplayVideo failed: missing file '{videoPath}'.");
            if (statusText != null)
                statusText.text = "Video file missing";
            return;
        }

        if (_videoDisplayCoroutine != null)
            StopCoroutine(_videoDisplayCoroutine);

        PlayVideoNotification(string.IsNullOrWhiteSpace(fileName) ? Path.GetFileName(videoPath) : fileName);

        _videoDisplayCoroutine = null;
        StopVideoPlayback(releaseTexture: true);
        ReleaseCurrentTexture();
        _videoDisplayCoroutine = StartCoroutine(DisplayVideoCoroutine(videoPath, fileName));
    }

    private IEnumerator DisplayPhotoCoroutine(M_QuestGalleryAndroidBridge bridge, M_QuestGalleryAndroidBridge.GalleryItem item)
    {
        if (statusText != null)
            statusText.text = "Loading photo...";

        yield return null;

        Texture2D tex = bridge.LoadFullTexture(item, maxDisplayDimension, markNonReadable: !keepTextureReadable);

        if (tex == null)
        {
            if (statusText != null)
                statusText.text = "Failed to load photo";

            Debug.LogWarning($"[M_QuestPhotoDisplay] Failed to load selected photo: {item.fileName}");
            yield break;
        }

        DisplayTexture(tex, item.fileName);
    }

    private void ApplyFallback()
    {
        if (targetRawImage != null)
        {
            targetRawImage.texture = fallbackTexture;
            targetRawImage.color = fallbackTexture != null ? Color.white : new Color(1f, 1f, 1f, 0f);
        }

        if (targetRenderer != null)
        {
            Material mat = GetOrCreateRuntimeMaterial();
            if (mat != null)
            {
                if (mat.HasProperty("_MainTex"))
                    mat.SetTexture("_MainTex", fallbackTexture);

                if (!string.IsNullOrEmpty(albedoProperty) && mat.HasProperty(albedoProperty))
                    mat.SetTexture(albedoProperty, fallbackTexture);
            }
        }
    }

    private void ApplyTexture(Texture texture)
    {
        if (targetRawImage != null)
        {
            targetRawImage.texture = texture;
            targetRawImage.color = Color.white;

            if (fitRawImageToBounds)
            {
                UpdateRawImageLayout(texture);

                if (refreshRawImageLayoutAfterFrame && isActiveAndEnabled)
                {
                    if (_layoutRefreshCoroutine != null)
                        StopCoroutine(_layoutRefreshCoroutine);

                    _layoutRefreshCoroutine = StartCoroutine(RefreshRawImageLayoutNextFrame(texture));
                }
            }
            else if (useNativeSizeForRawImage)
            {
                targetRawImage.SetNativeSize();
            }
        }

        if (targetRenderer != null)
        {
            Material mat = GetOrCreateRuntimeMaterial();
            if (mat != null)
            {
                if (mat.HasProperty("_MainTex"))
                    mat.SetTexture("_MainTex", texture);

                if (!string.IsNullOrEmpty(albedoProperty) && mat.HasProperty(albedoProperty))
                    mat.SetTexture(albedoProperty, texture);

                if (mat.HasProperty("_Color"))
                    mat.SetColor("_Color", Color.white);
            }
        }
    }

    private Material GetOrCreateRuntimeMaterial()
    {
        if (targetRenderer == null)
            return null;

        Material[] mats = targetRenderer.materials;
        if (mats == null || mats.Length == 0)
        {
            Debug.LogWarning("[M_QuestPhotoDisplay] Target renderer has no materials.");
            return null;
        }

        int slot = Mathf.Clamp(materialIndex, 0, mats.Length - 1);

        if (_runtimeMaterial == null)
        {
            _runtimeMaterial = new Material(mats[slot]);
            _runtimeMaterial.name = mats[slot].name + "_Runtime";

            mats[slot] = _runtimeMaterial;
            targetRenderer.materials = mats;
        }

        return _runtimeMaterial;
    }

    private void ReleaseCurrentTexture()
    {
        if (_currentTexture != null)
        {
            Destroy(_currentTexture);
            _currentTexture = null;
        }
    }

    private IEnumerator DisplayVideoCoroutine(string videoPath, string fileName)
    {
        EnsureVideoComponents();

        _currentFileName = string.IsNullOrWhiteSpace(fileName) ? Path.GetFileName(videoPath) : fileName;

        if (fileNameText != null)
            fileNameText.text = _currentFileName;

        if (statusText != null)
            statusText.text = "Loading video...";

        _videoPlayer.source = VideoSource.Url;
        _videoPlayer.url = new Uri(videoPath).AbsoluteUri;
        _videoPlayer.renderMode = VideoRenderMode.RenderTexture;
        _videoPlayer.playOnAwake = false;
        _videoPlayer.waitForFirstFrame = true;
        _videoPlayer.isLooping = loopVideos;
        _videoAudio.ConfigureForVideoPlayer(_videoPlayer);

        _videoPlayer.Prepare();
        float timeout = 15f;
        while (!_videoPlayer.isPrepared && timeout > 0f)
        {
            timeout -= Time.unscaledDeltaTime;
            yield return null;
        }

        int width = Mathf.Max(1, (int)_videoPlayer.width);
        int height = Mathf.Max(1, (int)_videoPlayer.height);
        if (width <= 1 || height <= 1)
        {
            width = Mathf.Max(1, fallbackVideoWidth);
            height = Mathf.Max(1, fallbackVideoHeight);
        }

        _currentVideoTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
        {
            name = $"QuestPhotoDisplayVideo_{width}x{height}"
        };
        _currentVideoTexture.Create();
        _videoPlayer.targetTexture = _currentVideoTexture;
        ApplyTexture(_currentVideoTexture);

        if (statusText != null)
            statusText.text = $"Video ready: {width} x {height}";

        if (autoPlayVideos && _videoPlayer.isPrepared)
            _videoPlayer.Play();

        if (verboseLogging)
            Debug.Log($"[M_QuestPhotoDisplay] Displayed video: {_currentFileName} ({width}x{height})");

        _videoDisplayCoroutine = null;
    }

    private void EnsureVideoComponents()
    {
        if (_videoPlayer == null)
            _videoPlayer = GetComponent<VideoPlayer>();

        if (_videoPlayer == null)
            _videoPlayer = gameObject.AddComponent<VideoPlayer>();

        if (_videoAudio == null)
            _videoAudio = GetComponent<M_SharedMediaVideoAudio>();

        if (_videoAudio == null)
            _videoAudio = gameObject.AddComponent<M_SharedMediaVideoAudio>();
    }

    private void PlayImageNotification(string mediaName)
    {
        if (!playLocalNotifications)
            return;

        M_LocalMediaNotificationAudio notifier = EnsureNotificationAudio();
        notifier?.PlayNewImage(mediaName);
    }

    private void PlayVideoNotification(string mediaName)
    {
        if (!playLocalNotifications)
            return;

        M_LocalMediaNotificationAudio notifier = EnsureNotificationAudio();
        notifier?.PlayNewVideo(mediaName);
    }

    private M_LocalMediaNotificationAudio EnsureNotificationAudio()
    {
        if (notificationAudio == null)
            notificationAudio = GetComponent<M_LocalMediaNotificationAudio>();

        if (notificationAudio == null)
            notificationAudio = gameObject.AddComponent<M_LocalMediaNotificationAudio>();

        notificationAudio.ConfigureClips(newImageNotificationClip, newVideoNotificationClip);
        return notificationAudio;
    }

    private void StopVideoPlayback(bool releaseTexture)
    {
        if (_videoDisplayCoroutine != null)
        {
            StopCoroutine(_videoDisplayCoroutine);
            _videoDisplayCoroutine = null;
        }

        if (_videoPlayer != null)
        {
            _videoPlayer.Stop();
            _videoPlayer.targetTexture = null;
        }

        _videoAudio?.Stop();

        if (releaseTexture)
            ReleaseVideoTexture();
    }

    private void ReleaseVideoTexture()
    {
        if (_currentVideoTexture == null)
            return;

        _currentVideoTexture.Release();
        Destroy(_currentVideoTexture);
        _currentVideoTexture = null;
    }

    private void UpdateRawImageLayout(Texture texture)
    {
        if (targetRawImage == null || texture == null)
            return;

        RectTransform rawRect = targetRawImage.rectTransform;
        if (rawRect == null)
            return;

        RectTransform boundsRect = rawImageFitBounds != null ? rawImageFitBounds : rawRect.parent as RectTransform;
        if (boundsRect == null)
        {
            if (useNativeSizeForRawImage)
                targetRawImage.SetNativeSize();
            return;
        }

        float availableWidth = Mathf.Max(1f, boundsRect.rect.width - rawImageFitMargins.x - rawImageFitMargins.y);
        float availableHeight = Mathf.Max(1f, boundsRect.rect.height - rawImageFitMargins.z - rawImageFitMargins.w);
        float textureWidth = Mathf.Max(1f, texture.width);
        float textureHeight = Mathf.Max(1f, texture.height);
        float heightScale = availableHeight / textureHeight;
        float fitScale = Mathf.Min(availableWidth / textureWidth, heightScale);
        float scale = matchRawImageToBoundsHeight ? heightScale : fitScale;

        if (matchRawImageToBoundsHeight && clampHeightFitToBoundsWidth && textureWidth * scale > availableWidth)
            scale = fitScale;

        rawRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, textureWidth * scale);
        rawRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, textureHeight * scale);
        rawRect.anchoredPosition = new Vector2(
            (rawImageFitMargins.x - rawImageFitMargins.y) * 0.5f,
            (rawImageFitMargins.w - rawImageFitMargins.z) * 0.5f);

        if (verboseLogging)
        {
            Debug.Log($"[M_QuestPhotoDisplay] RawImage layout -> targetHeight={availableHeight:F1}, appliedSize={textureWidth * scale:F1}x{textureHeight * scale:F1}, fixedHeight={matchRawImageToBoundsHeight}");
        }
    }

    private IEnumerator RefreshRawImageLayoutNextFrame(Texture texture)
    {
        yield return null;

        Canvas.ForceUpdateCanvases();

        if (targetRawImage != null && targetRawImage.texture == texture)
            UpdateRawImageLayout(texture);

        _layoutRefreshCoroutine = null;
    }

    private void OnDestroy()
    {
        if (_layoutRefreshCoroutine != null)
        {
            StopCoroutine(_layoutRefreshCoroutine);
            _layoutRefreshCoroutine = null;
        }

        ReleaseCurrentTexture();
        StopVideoPlayback(releaseTexture: true);

        if (_runtimeMaterial != null)
            Destroy(_runtimeMaterial);
    }
}
