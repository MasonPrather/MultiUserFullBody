/*
 * Script Name: M_MediaGalleryUI.cs
 * Description: Persistent Quest-local gallery UI for imported phone/device media.
 * Project Role: Shows thumbnails from Application.persistentDataPath and hands selected mediaId to lobby sharing.
 */

using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class M_MediaGalleryUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private M_MediaLibrary mediaLibrary;
    [SerializeField] private M_LobbyMediaShareController shareController;
    [SerializeField] private Transform tileParent;
    [SerializeField] private M_MediaTile tilePrefab;
    [SerializeField] private RawImage previewImage;

    [Header("UI")]
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text selectedTitleText;
    [SerializeField] private Button shareButton;
    [SerializeField] private Button refreshButton;
    [SerializeField] private Button deleteButton;

    [Header("Sharing")]
    [SerializeField] private bool requireShareConfirmation = true;

    private readonly List<M_MediaTile> _tiles = new List<M_MediaTile>();
    private M_MediaRecord _selectedRecord;
    private Texture _previewTexture;
    private bool _awaitingShareConfirmation;

    private void Awake()
    {
        ResolveReferences();

        if (shareButton != null)
            shareButton.onClick.AddListener(ShareSelectedFromUi);

        if (refreshButton != null)
            refreshButton.onClick.AddListener(Refresh);

        if (deleteButton != null)
            deleteButton.onClick.AddListener(DeleteSelected);
    }

    private void OnEnable()
    {
        ResolveReferences();
        if (mediaLibrary != null)
            mediaLibrary.LibraryChanged += HandleLibraryChanged;

        Refresh();
    }

    private void OnDisable()
    {
        if (mediaLibrary != null)
            mediaLibrary.LibraryChanged -= HandleLibraryChanged;
    }

    private void OnDestroy()
    {
        if (_previewTexture != null)
            Destroy(_previewTexture);
    }

    public void Refresh()
    {
        ResolveReferences();
        if (mediaLibrary == null || tileParent == null || tilePrefab == null)
        {
            SetStatus("Gallery UI is missing required references.");
            return;
        }

        mediaLibrary.Initialize();
        ClearTiles();
        IReadOnlyList<M_MediaRecord> records = mediaLibrary.Records;
        for (int i = 0; i < records.Count; i++)
            CreateTile(records[i]);

        if (records.Count == 0)
            SetStatus("No imported photos yet.");
        else
            SetStatus($"{records.Count} photo{(records.Count == 1 ? string.Empty : "s")} in your Quest gallery.");
    }

    public void ShareSelectedFromUi()
    {
        if (_selectedRecord == null)
        {
            SetStatus("Choose a photo before sharing.");
            return;
        }

        if (requireShareConfirmation && !_awaitingShareConfirmation)
        {
            _awaitingShareConfirmation = true;
            SetStatus("Share this photo with everyone in the current lobby?");
            return;
        }

        _awaitingShareConfirmation = false;
        shareController?.ShareSelectedMedia(_selectedRecord.mediaId);
    }

    public void DeleteSelected()
    {
        if (_selectedRecord == null || mediaLibrary == null)
            return;

        string deletedName = _selectedRecord.displayName;
        mediaLibrary.DeleteLocalMedia(_selectedRecord.mediaId);
        _selectedRecord = null;
        ClearPreview();
        Refresh();
        SetStatus($"Deleted {deletedName} from this Quest gallery.");
    }

    private void CreateTile(M_MediaRecord record)
    {
        M_MediaTile tile = Instantiate(tilePrefab, tileParent);
        tile.Bind(record);
        tile.Selected += HandleTileSelected;
        tile.ShareRequested += HandleTileShareRequested;
        _tiles.Add(tile);

        string thumbPath = mediaLibrary.ResolveThumbnailPath(record);
        if (!string.IsNullOrWhiteSpace(thumbPath))
            StartCoroutine(LoadTextureIntoTile(tile, thumbPath));
    }

    private IEnumerator LoadTextureIntoTile(M_MediaTile tile, string path)
    {
        using UnityWebRequest request = UnityWebRequestTexture.GetTexture(new Uri(path).AbsoluteUri);
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            tile.SetStatus("Thumbnail unavailable");
            yield break;
        }

        tile.SetThumbnail(DownloadHandlerTexture.GetContent(request), ownsTexture: true);
    }

    private void HandleTileSelected(M_MediaTile tile)
    {
        _selectedRecord = tile.Record;
        _awaitingShareConfirmation = false;

        if (selectedTitleText != null)
            selectedTitleText.text = _selectedRecord != null ? _selectedRecord.displayName : string.Empty;

        ClearPreview();
        string fullPath = mediaLibrary.ResolveFullPath(_selectedRecord);
        if (!string.IsNullOrWhiteSpace(fullPath))
            StartCoroutine(LoadPreview(fullPath));
    }

    private void HandleTileShareRequested(M_MediaTile tile)
    {
        HandleTileSelected(tile);
        ShareSelectedFromUi();
    }

    private IEnumerator LoadPreview(string path)
    {
        using UnityWebRequest request = UnityWebRequestTexture.GetTexture(new Uri(path).AbsoluteUri);
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            SetStatus("Preview unavailable.");
            yield break;
        }

        _previewTexture = DownloadHandlerTexture.GetContent(request);
        if (previewImage != null)
            previewImage.texture = _previewTexture;
    }

    private void HandleLibraryChanged(IReadOnlyList<M_MediaRecord> records)
    {
        Refresh();
    }

    private void ClearTiles()
    {
        for (int i = 0; i < _tiles.Count; i++)
        {
            if (_tiles[i] != null)
                Destroy(_tiles[i].gameObject);
        }

        _tiles.Clear();
    }

    private void ClearPreview()
    {
        if (_previewTexture != null)
            Destroy(_previewTexture);

        _previewTexture = null;
        if (previewImage != null)
            previewImage.texture = null;
    }

    private void ResolveReferences()
    {
        if (mediaLibrary == null)
            mediaLibrary = FindFirstObjectByType<M_MediaLibrary>();

        if (shareController == null)
            shareController = FindFirstObjectByType<M_LobbyMediaShareController>();
    }

    private void SetStatus(string status)
    {
        if (statusText != null)
            statusText.text = status ?? string.Empty;
    }
}
