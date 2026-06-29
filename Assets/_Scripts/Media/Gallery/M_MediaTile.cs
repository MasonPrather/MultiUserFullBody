/*
 * Script Name: M_MediaTile.cs
 * Description: Small prefab-friendly tile for a persistent gallery record.
 * Project Role: Displays local thumbnails and exposes select/share callbacks without knowing networking details.
 */

using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class M_MediaTile : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private RawImage thumbnailImage;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Slider progressSlider;
    [SerializeField] private Button selectButton;
    [SerializeField] private Button shareButton;

    private M_MediaRecord _record;
    private Texture _thumbnail;

    public M_MediaRecord Record => _record;
    public event Action<M_MediaTile> Selected;
    public event Action<M_MediaTile> ShareRequested;

    private void Awake()
    {
        if (selectButton != null)
            selectButton.onClick.AddListener(() => Selected?.Invoke(this));

        if (shareButton != null)
            shareButton.onClick.AddListener(() => ShareRequested?.Invoke(this));
    }

    private void OnDestroy()
    {
        if (_thumbnail != null)
            Destroy(_thumbnail);
    }

    public void Bind(M_MediaRecord record)
    {
        _record = record;
        if (titleText != null)
            titleText.text = record != null ? record.displayName : string.Empty;

        SetStatus(string.Empty);
        SetProgress(-1f);
    }

    public void SetThumbnail(Texture texture, bool ownsTexture = true)
    {
        if (_thumbnail != null && _thumbnail != texture)
            Destroy(_thumbnail);

        _thumbnail = ownsTexture ? texture : null;

        if (thumbnailImage != null)
            thumbnailImage.texture = texture;
    }

    public void SetStatus(string status)
    {
        if (statusText != null)
        {
            statusText.text = status ?? string.Empty;
            statusText.gameObject.SetActive(!string.IsNullOrWhiteSpace(status));
        }
    }

    public void SetProgress(float normalized)
    {
        if (progressSlider == null)
            return;

        bool visible = normalized >= 0f && normalized < 1f;
        progressSlider.gameObject.SetActive(visible);
        progressSlider.value = Mathf.Clamp01(normalized);
    }
}
