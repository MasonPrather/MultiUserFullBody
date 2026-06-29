/*
 * Script Name: M_QuestGalleryTile.cs
 * Author: Mason Prather
 * Description: Renders one media gallery tile with thumbnail, selection, and delete controls for the Quest import browser.
 * Project Role: Reusable UI element managed by M_QuestGalleryController for browseable imported media.
 * Key Inputs: GalleryItem metadata, generated thumbnail texture, controller callback reference, and UI Button/EventSystem input.
 * Key Outputs: Selection callbacks, delete callbacks, selected-state visuals, and runtime thumbnail cleanup.
 */

using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

/// <summary>
/// Single gallery tile entry.
/// Displays a thumbnail + filename and notifies the controller when pressed.
/// </summary>
public class M_QuestGalleryTile : MonoBehaviour, IPointerClickHandler, IPointerDownHandler, ISubmitHandler
{
    [Header("UI References")]
    [Tooltip("Button used to select this tile.")]
    public Button button;

    [Tooltip("RawImage used to display the thumbnail.")]
    public RawImage thumbnailImage;

    [Tooltip("Optional text label for the file name.")]
    public TMP_Text fileNameText;

    [Tooltip("Optional selected-state object.")]
    public GameObject selectedVisual;

    [Tooltip("Optional button used to delete this tile's stored image.")]
    public Button deleteButton;

    [Header("Fallbacks")]
    [Tooltip("Fallback texture shown before thumbnail load.")]
    public Texture placeholderTexture;

    [Header("Deletion")]
    [Tooltip("If true, a small delete button is created when the prefab does not provide one.")]
    public bool createDeleteButtonIfMissing = true;

    [Header("Debug")]
    [Tooltip("If true, log tile events.")]
    public bool verboseLogging = false;

    private M_QuestGalleryAndroidBridge.GalleryItem _item;
    private M_QuestGalleryController _controller;
    private Texture2D _runtimeThumbnail;
    private int _lastPressFrame = -1;

    /// <summary>
    /// Initializes this tile with data and controller callback.
    /// </summary>
    public void Setup(M_QuestGalleryController controller, M_QuestGalleryAndroidBridge.GalleryItem item)
    {
        _controller = controller;
        _item = item;

        if (fileNameText != null)
            fileNameText.text = item != null ? item.fileName : "(null)";

        if (thumbnailImage != null)
        {
            thumbnailImage.texture = placeholderTexture;
            thumbnailImage.color = placeholderTexture != null ? Color.white : new Color(1f, 1f, 1f, 0.15f);
        }

        if (button != null)
        {
            button.onClick.RemoveListener(OnPressed);
            button.onClick.AddListener(OnPressed);

            if (button.targetGraphic != null)
                button.targetGraphic.raycastTarget = true;
        }

        EnsureDeleteButton();
        BindDeleteButton();
        DisableDecorativeRaycasts();

        SetSelected(false);

        if (verboseLogging && item != null)
            Debug.Log($"[M_QuestGalleryTile] Setup -> {item.fileName}");
    }

    /// <summary>
    /// Applies the generated thumbnail to this tile.
    /// </summary>
    public void SetThumbnail(Texture2D thumbnail)
    {
        if (_runtimeThumbnail != null && _runtimeThumbnail != thumbnail)
            Destroy(_runtimeThumbnail);

        _runtimeThumbnail = thumbnail;

        if (thumbnailImage != null)
        {
            thumbnailImage.texture = _runtimeThumbnail != null ? _runtimeThumbnail : placeholderTexture;
            thumbnailImage.color = Color.white;
        }
    }

    /// <summary>
    /// Updates selected-state visuals.
    /// </summary>
    public void SetSelected(bool isSelected)
    {
        if (selectedVisual != null)
            selectedVisual.SetActive(isSelected);
    }

    public void SetDeleteVisible(bool visible)
    {
        EnsureDeleteButton();

        if (deleteButton != null)
            deleteButton.gameObject.SetActive(visible);
    }

    private void OnPressed()
    {
        if (_lastPressFrame == Time.frameCount)
            return;

        _lastPressFrame = Time.frameCount;

        if (verboseLogging && _item != null)
            Debug.Log($"[M_QuestGalleryTile] Pressed -> {_item.fileName}");

        _controller?.OnTileSelected(this, _item);
    }

    private void OnDeletePressed()
    {
        if (verboseLogging && _item != null)
            Debug.Log($"[M_QuestGalleryTile] Delete pressed -> {_item.fileName}");

        _controller?.DeleteTileImage(this, _item);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        OnPressed();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        OnPressed();
    }

    public void OnSubmit(BaseEventData eventData)
    {
        OnPressed();
    }

    private void DisableDecorativeRaycasts()
    {
        Graphic buttonGraphic = button != null ? button.targetGraphic : null;
        Graphic deleteGraphic = deleteButton != null ? deleteButton.targetGraphic : null;
        Graphic[] graphics = GetComponentsInChildren<Graphic>(true);

        for (int i = 0; i < graphics.Length; i++)
        {
            if (graphics[i] == null || graphics[i] == buttonGraphic || graphics[i] == deleteGraphic)
                continue;

            graphics[i].raycastTarget = false;
        }
    }

    private void EnsureDeleteButton()
    {
        if (deleteButton != null || !createDeleteButtonIfMissing)
            return;

        GameObject buttonObject = new GameObject("DeleteButton");
        buttonObject.transform.SetParent(transform, false);
        buttonObject.layer = gameObject.layer;

        RectTransform rect = buttonObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-8f, -8f);
        rect.sizeDelta = new Vector2(46f, 46f);

        Image image = buttonObject.AddComponent<Image>();
        image.color = new Color(0.78f, 0.12f, 0.12f, 0.92f);

        deleteButton = buttonObject.AddComponent<Button>();
        deleteButton.targetGraphic = image;

        GameObject labelObject = new GameObject("Label");
        labelObject.transform.SetParent(buttonObject.transform, false);
        labelObject.layer = buttonObject.layer;

        RectTransform labelRect = labelObject.AddComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        TextMeshProUGUI label = labelObject.AddComponent<TextMeshProUGUI>();
        label.text = "X";
        label.alignment = TextAlignmentOptions.Center;
        label.fontStyle = FontStyles.Bold;
        label.fontSize = 28f;
        label.color = Color.white;
        label.raycastTarget = false;
    }

    private void BindDeleteButton()
    {
        if (deleteButton == null)
            return;

        deleteButton.onClick.RemoveListener(OnDeletePressed);
        deleteButton.onClick.AddListener(OnDeletePressed);
    }

    private void OnDestroy()
    {
        if (_runtimeThumbnail != null)
            Destroy(_runtimeThumbnail);
    }
}
