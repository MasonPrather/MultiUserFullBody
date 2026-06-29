/*
 * Script Name: M_SharedMediaDisplayUI.cs
 * Description: Displays the current shared lobby photo with transfer states, retry, and explicit save.
 * Project Role: Ensures shared media failures are visible instead of becoming blank or silently ignored panels.
 */

using System;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class M_SharedMediaDisplayUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private M_LobbyMediaCatalog catalog;
    [SerializeField] private M_MediaTransferManager transferManager;
    [SerializeField] private M_SessionMediaCache sessionCache;
    [SerializeField] private M_MediaLibrary mediaLibrary;
    [SerializeField] private M_MediaImportController importController;
    [SerializeField] private M_SharedMediaSurface surface;

    [Header("UI")]
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private Slider progressSlider;
    [SerializeField] private Button retryButton;
    [SerializeField] private Button saveToGalleryButton;
    [SerializeField] private Button clearReceivedButton;

    private SharedMediaEntry _currentEntry;
    private bool _hasCurrentEntry;

    private void Awake()
    {
        ResolveReferences();

        if (retryButton != null)
            retryButton.onClick.AddListener(RetryCurrent);

        if (saveToGalleryButton != null)
            saveToGalleryButton.onClick.AddListener(SaveCurrentToGallery);

        if (clearReceivedButton != null)
            clearReceivedButton.onClick.AddListener(ClearReceivedCache);
    }

    private void OnEnable()
    {
        ResolveReferences();
        Subscribe();
        SelectLatestCatalogEntry();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    public void RetryCurrent()
    {
        if (!_hasCurrentEntry || transferManager == null)
            return;

        transferManager.RequestMediaFromHost(_currentEntry, M_MediaTransferFileRole.Thumbnail);
        transferManager.RequestMediaFromHost(_currentEntry, M_MediaTransferFileRole.FullImage);
        SetStatus("Retrying shared photo transfer...");
    }

    public void SaveCurrentToGallery()
    {
        if (!_hasCurrentEntry || sessionCache == null || importController == null)
            return;

        string sha = _currentEntry.Sha256.ToString();
        if (!sessionCache.TryGetCachedPath(sha, M_MediaTransferFileRole.FullImage, preferHostCache: false, out string path))
        {
            SetStatus("Full image is not available yet.");
            return;
        }

        if (importController.TryImportReceivedImageToLibrary(path, _currentEntry.DisplayName.ToString(), out M_MediaImportResult result))
            SetStatus(result.Duplicate ? "This photo was already in your gallery." : "Saved to your Quest gallery.");
        else
            SetStatus(result?.Message ?? "Could not save this photo.");
    }

    public void ClearReceivedCache()
    {
        sessionCache?.ClearReceivedCache();
        surface?.Clear();
        SetStatus("Cleared received session media from this Quest.");
    }

    private void HandleCatalogEntry(SharedMediaEntry entry)
    {
        if (!_hasCurrentEntry || entry.Sequence >= _currentEntry.Sequence)
        {
            _currentEntry = entry;
            _hasCurrentEntry = true;
            UpdateDisplayForCurrent();
        }
    }

    private void UpdateDisplayForCurrent()
    {
        if (!_hasCurrentEntry)
        {
            SetStatus("No shared photo yet.");
            return;
        }

        if (titleText != null)
            titleText.text = _currentEntry.DisplayName.ToString();

        string sha = _currentEntry.Sha256.ToString();
        string fullPath = null;
        string thumbPath = null;

        if (sessionCache != null)
        {
            sessionCache.TryGetCachedPath(sha, M_MediaTransferFileRole.Thumbnail, preferHostCache: NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer, out thumbPath);
            sessionCache.TryGetCachedPath(sha, M_MediaTransferFileRole.FullImage, preferHostCache: NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer, out fullPath);
        }

        if (string.IsNullOrWhiteSpace(fullPath) && mediaLibrary != null && NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClientId == _currentEntry.OwnerClientId)
        {
            M_MediaRecord localRecord = mediaLibrary.GetByMediaId(_currentEntry.MediaId.ToString());
            fullPath = mediaLibrary.ResolveFullPath(localRecord);
            thumbPath = mediaLibrary.ResolveThumbnailPath(localRecord);
        }

        switch (_currentEntry.State)
        {
            case MediaShareState.Announced:
            case MediaShareState.Transferring:
                SetStatus("Receiving photo...");
                break;
            case MediaShareState.ThumbnailReady:
                SetStatus("Thumbnail ready");
                break;
            case MediaShareState.Ready:
                SetStatus("Photo ready");
                break;
            case MediaShareState.Failed:
                SetStatus("Image failed verification");
                break;
            case MediaShareState.Cancelled:
                SetStatus("Transfer interrupted");
                break;
        }

        if (!string.IsNullOrWhiteSpace(thumbPath) && string.IsNullOrWhiteSpace(fullPath))
            surface?.DisplayFromFile(this, thumbPath);

        if (!string.IsNullOrWhiteSpace(fullPath))
            surface?.DisplayFromFile(this, fullPath);
    }

    private void HandleTransferProgress(ulong sequence, M_MediaTransferFileRole role, float progress)
    {
        if (!_hasCurrentEntry || sequence != _currentEntry.Sequence)
            return;

        if (progressSlider != null)
        {
            progressSlider.gameObject.SetActive(progress > 0f && progress < 1f);
            progressSlider.value = Mathf.Clamp01(progress);
        }

        if (role == M_MediaTransferFileRole.FullImage && progress < 1f)
            SetStatus($"Full image loading: {Mathf.RoundToInt(progress * 100f)}%");
    }

    private void HandleFileReceived(M_MediaTransferCompletedFile file)
    {
        if (!_hasCurrentEntry || file.Sequence != _currentEntry.Sequence)
            return;

        UpdateDisplayForCurrent();
    }

    private void SelectLatestCatalogEntry()
    {
        if (catalog == null || catalog.Count == 0)
        {
            SetStatus("No shared photo yet.");
            return;
        }

        SharedMediaEntry latest = catalog.GetEntryAt(0);
        for (int i = 1; i < catalog.Count; i++)
        {
            SharedMediaEntry candidate = catalog.GetEntryAt(i);
            if (candidate.Sequence > latest.Sequence)
                latest = candidate;
        }

        _currentEntry = latest;
        _hasCurrentEntry = true;
        UpdateDisplayForCurrent();
    }

    private void Subscribe()
    {
        if (catalog != null)
            catalog.EntryAddedOrUpdated += HandleCatalogEntry;

        if (transferManager != null)
        {
            transferManager.ProgressChanged += HandleTransferProgress;
            transferManager.FileReceived += HandleFileReceived;
            transferManager.StatusChanged += SetStatus;
        }
    }

    private void Unsubscribe()
    {
        if (catalog != null)
            catalog.EntryAddedOrUpdated -= HandleCatalogEntry;

        if (transferManager != null)
        {
            transferManager.ProgressChanged -= HandleTransferProgress;
            transferManager.FileReceived -= HandleFileReceived;
            transferManager.StatusChanged -= SetStatus;
        }
    }

    private void ResolveReferences()
    {
        if (catalog == null)
            catalog = FindFirstObjectByType<M_LobbyMediaCatalog>();

        if (transferManager == null)
            transferManager = FindFirstObjectByType<M_MediaTransferManager>();

        if (sessionCache == null)
            sessionCache = FindFirstObjectByType<M_SessionMediaCache>();

        if (mediaLibrary == null)
            mediaLibrary = FindFirstObjectByType<M_MediaLibrary>();

        if (importController == null)
            importController = FindFirstObjectByType<M_MediaImportController>();

        if (surface == null)
            surface = GetComponent<M_SharedMediaSurface>();
    }

    private void SetStatus(string status)
    {
        if (statusText != null)
            statusText.text = status ?? string.Empty;
    }
}
