/*
 * Script Name: M_SharedMediaSurface.cs
 * Description: Applies shared-media images or videos to a RawImage or Renderer and releases old runtime assets.
 * Project Role: Prevents repeated sharing from leaking Texture2D/RenderTexture instances in long support sessions.
 */

using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.Video;

public class M_SharedMediaSurface : MonoBehaviour
{
    [SerializeField] private RawImage targetRawImage;
    [SerializeField] private Renderer targetRenderer;
    [SerializeField] private string rendererTextureProperty = "_BaseMap";

    [Header("Video")]
    [SerializeField] private bool autoPlayVideo = true;
    [SerializeField] private bool loopVideo = true;
    [SerializeField] private int fallbackVideoWidth = 1280;
    [SerializeField] private int fallbackVideoHeight = 720;

    private Texture _ownedTexture;
    private RenderTexture _ownedVideoTexture;
    private VideoPlayer _videoPlayer;
    private M_SharedMediaVideoAudio _videoAudio;
    private Coroutine _displayCoroutine;

    private void Awake()
    {
        ResolveTargets();
    }

    private void OnDestroy()
    {
        Clear();
    }

    public void Clear()
    {
        StopActiveDisplayCoroutine();
        StopVideo();

        if (_ownedTexture != null)
            Destroy(_ownedTexture);

        _ownedTexture = null;
        ReleaseVideoTexture();

        if (targetRawImage != null)
            targetRawImage.texture = null;

        if (targetRenderer != null)
            targetRenderer.material.SetTexture(rendererTextureProperty, null);
    }

    public void DisplayTexture(Texture texture, bool ownsTexture)
    {
        StopActiveDisplayCoroutine();
        StopVideo();
        ReleaseVideoTexture();
        DisplayTextureInternal(texture, ownsTexture);
    }

    public Coroutine DisplayFromFile(MonoBehaviour runner, string path)
    {
        if (runner == null || string.IsNullOrWhiteSpace(path))
            return null;

        StopActiveDisplayCoroutine();
        _displayCoroutine = StartCoroutine(DisplayFromFileCoroutine(path));
        return _displayCoroutine;
    }

    public Coroutine DisplayMediaFromFile(MonoBehaviour runner, string path, string kind, string mime)
    {
        if (runner == null || string.IsNullOrWhiteSpace(path))
            return null;

        StopActiveDisplayCoroutine();
        _displayCoroutine = M_MediaTypeUtility.IsVideoKind(kind) || M_MediaTypeUtility.IsVideoMime(mime)
            ? StartCoroutine(DisplayVideoFromFileCoroutine(path))
            : StartCoroutine(DisplayFromFileCoroutine(path));
        return _displayCoroutine;
    }

    public Coroutine DisplayVideoFromFile(MonoBehaviour runner, string path)
    {
        if (runner == null || string.IsNullOrWhiteSpace(path))
            return null;

        StopActiveDisplayCoroutine();
        _displayCoroutine = StartCoroutine(DisplayVideoFromFileCoroutine(path));
        return _displayCoroutine;
    }

    public void ConfigureTargets(RawImage rawImage, Renderer rendererTarget, string textureProperty = null)
    {
        targetRawImage = rawImage;
        targetRenderer = rendererTarget;

        if (!string.IsNullOrWhiteSpace(textureProperty))
            rendererTextureProperty = textureProperty;
    }

    private void DisplayTextureInternal(Texture texture, bool ownsTexture)
    {
        if (_ownedTexture != null && _ownedTexture != texture)
            Destroy(_ownedTexture);

        _ownedTexture = ownsTexture ? texture : null;

        if (targetRawImage != null)
            targetRawImage.texture = texture;

        if (targetRenderer != null)
            targetRenderer.material.SetTexture(rendererTextureProperty, texture);
    }

    private IEnumerator DisplayFromFileCoroutine(string path)
    {
        StopVideo();
        ReleaseVideoTexture();

        using UnityWebRequest request = UnityWebRequestTexture.GetTexture(new Uri(path).AbsoluteUri);
        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
            DisplayTextureInternal(DownloadHandlerTexture.GetContent(request), ownsTexture: true);

        _displayCoroutine = null;
    }

    private IEnumerator DisplayVideoFromFileCoroutine(string path)
    {
        StopVideo();

        if (_ownedTexture != null)
            Destroy(_ownedTexture);

        _ownedTexture = null;
        ReleaseVideoTexture();
        EnsureVideoPlayer();

        string url = new Uri(path).AbsoluteUri;
        _videoPlayer.source = VideoSource.Url;
        _videoPlayer.url = url;
        _videoPlayer.isLooping = loopVideo;
        _videoPlayer.renderMode = VideoRenderMode.RenderTexture;
        _videoPlayer.playOnAwake = false;
        _videoPlayer.waitForFirstFrame = true;
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

        _ownedVideoTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
        {
            name = $"SharedMediaVideo_{width}x{height}"
        };
        _ownedVideoTexture.Create();
        _videoPlayer.targetTexture = _ownedVideoTexture;
        DisplayTextureInternal(_ownedVideoTexture, ownsTexture: false);

        if (autoPlayVideo && _videoPlayer.isPrepared)
            _videoPlayer.Play();

        _displayCoroutine = null;
    }

    private void ResolveTargets()
    {
        if (targetRawImage == null)
            targetRawImage = GetComponent<RawImage>();

        if (targetRenderer == null)
            targetRenderer = GetComponent<Renderer>();
    }

    private void EnsureVideoPlayer()
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

    private void StopVideo()
    {
        if (_videoPlayer != null)
        {
            _videoPlayer.Stop();
            _videoPlayer.targetTexture = null;
        }

        _videoAudio?.Stop();
    }

    private void ReleaseVideoTexture()
    {
        if (_ownedVideoTexture == null)
            return;

        _ownedVideoTexture.Release();
        Destroy(_ownedVideoTexture);
        _ownedVideoTexture = null;
    }

    private void StopActiveDisplayCoroutine()
    {
        if (_displayCoroutine == null)
            return;

        StopCoroutine(_displayCoroutine);
        _displayCoroutine = null;
    }
}
