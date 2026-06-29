/*
 * Script Name: M_SharedMediaSurface.cs
 * Description: Applies a loaded shared-media texture to a RawImage or Renderer and releases old textures.
 * Project Role: Prevents repeated sharing from leaking Texture2D instances in long support sessions.
 */

using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class M_SharedMediaSurface : MonoBehaviour
{
    [SerializeField] private RawImage targetRawImage;
    [SerializeField] private Renderer targetRenderer;
    [SerializeField] private string rendererTextureProperty = "_BaseMap";

    private Texture _ownedTexture;

    private void Awake()
    {
        if (targetRawImage == null)
            targetRawImage = GetComponent<RawImage>();

        if (targetRenderer == null)
            targetRenderer = GetComponent<Renderer>();
    }

    private void OnDestroy()
    {
        Clear();
    }

    public void Clear()
    {
        if (_ownedTexture != null)
            Destroy(_ownedTexture);

        _ownedTexture = null;

        if (targetRawImage != null)
            targetRawImage.texture = null;

        if (targetRenderer != null)
            targetRenderer.material.SetTexture(rendererTextureProperty, null);
    }

    public void DisplayTexture(Texture texture, bool ownsTexture)
    {
        if (_ownedTexture != null && _ownedTexture != texture)
            Destroy(_ownedTexture);

        _ownedTexture = ownsTexture ? texture : null;

        if (targetRawImage != null)
            targetRawImage.texture = texture;

        if (targetRenderer != null)
            targetRenderer.material.SetTexture(rendererTextureProperty, texture);
    }

    public Coroutine DisplayFromFile(MonoBehaviour runner, string path)
    {
        if (runner == null || string.IsNullOrWhiteSpace(path))
            return null;

        return runner.StartCoroutine(DisplayFromFileCoroutine(path));
    }

    private IEnumerator DisplayFromFileCoroutine(string path)
    {
        using UnityWebRequest request = UnityWebRequestTexture.GetTexture(new Uri(path).AbsoluteUri);
        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
            DisplayTexture(DownloadHandlerTexture.GetContent(request), ownsTexture: true);
    }
}
