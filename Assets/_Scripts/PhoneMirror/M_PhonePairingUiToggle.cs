/*
 * Script Name: M_PhonePairingUiToggle.cs
 * Author: Mason Prather
 * Description: Shows or hides a phone pairing UI root from a VR-friendly Button without disabling the underlying pairing/signaling components.
 * Project Role: Reusable runtime control for phone upload and phone mirroring pairing panels.
 * Key Inputs: Pairing UI root, optional M_PhoneImportHeadsetMode, optional Button/TMP label, and optional generated world-space button settings.
 * Key Outputs: Pairing UI visibility changes and synchronized toggle label state.
 */

using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

/// <summary>
/// Inspector-friendly show/hide control for phone pairing UI.
/// Hides only the assigned UI root unless a phone import mode is assigned.
/// </summary>
[DisallowMultipleComponent]
public class M_PhonePairingUiToggle : MonoBehaviour
{
    [Header("Targets")]
    [Tooltip("Visual root to show/hide. Keep signaling/WebRTC host objects outside this root.")]
    public GameObject pairingUiRoot;

    [Tooltip("Optional phone import mode. When assigned, Enter/Exit is used instead of directly toggling the root.")]
    public M_PhoneImportHeadsetMode phoneImportMode;

    [Header("Button")]
    public Button toggleButton;
    public TMP_Text toggleButtonLabel;
    public string showLabel = "Show Phone Pairing";
    public string hideLabel = "Hide Phone Pairing";

    [Header("Startup")]
    public bool startVisible = true;
    public bool applyStartVisibleOnStart = true;

    [Header("Generated Button")]
    public bool createButtonIfMissing = false;
    public RectTransform generatedButtonParent;
    public bool usePairingCanvasForGeneratedButton = true;
    public Vector2 generatedButtonAnchoredPosition = new Vector2(-365f, -585f);
    public Vector2 generatedButtonSize = new Vector2(320f, 68f);
    public float generatedCanvasDistanceMeters = 1.25f;
    public Vector3 generatedCanvasOffsetMeters = new Vector3(-0.34f, -0.28f, 0f);
    public float generatedCanvasScale = 0.00135f;
    public int sortingOrder = 130;
    public Color buttonColor = new Color(0.12f, 0.42f, 0.9f, 0.96f);
    public Color labelColor = Color.white;

    [Header("References")]
    public Camera xrCamera;

    [Header("Debug")]
    public bool verboseLogging = false;

    private GameObject _generatedCanvasRoot;
    private bool _createdButton;

    public bool IsVisible
    {
        get
        {
            if (phoneImportMode != null)
                return phoneImportMode.IsPairingModeActive;

            return pairingUiRoot != null && pairingUiRoot.activeSelf;
        }
    }

    private void Awake()
    {
        ResolveReferences();
        EnsureButton();
    }

    private void OnEnable()
    {
        ResolveReferences();
        EnsureButton();
        BindButton();
        UpdateLabel();
    }

    private void Start()
    {
        ResolveReferences();
        EnsureButton();
        BindButton();

        if (applyStartVisibleOnStart)
            SetVisible(startVisible);
        else
            UpdateLabel();
    }

    private void OnDisable()
    {
        if (toggleButton != null)
            toggleButton.onClick.RemoveListener(Toggle);
    }

    private void OnDestroy()
    {
        if (_createdButton && toggleButton != null)
            Destroy(toggleButton.gameObject);

        if (_generatedCanvasRoot != null)
            Destroy(_generatedCanvasRoot);
    }

    public void RefreshBinding()
    {
        ResolveReferences();
        EnsureButton();
        BindButton();
        UpdateLabel();
    }

    public void Toggle()
    {
        SetVisible(!IsVisible);
    }

    public void Show()
    {
        SetVisible(true);
    }

    public void Hide()
    {
        SetVisible(false);
    }

    public void SetVisible(bool visible)
    {
        ResolveReferences();

        if (phoneImportMode != null)
        {
            phoneImportMode.SetPhoneImportModeVisible(visible);
        }
        else if (pairingUiRoot != null)
        {
            pairingUiRoot.SetActive(visible);
        }
        else
        {
            Debug.LogWarning("[M_PhonePairingUiToggle] No pairing UI root or phone import mode is assigned.");
            return;
        }

        UpdateLabel();

        if (verboseLogging)
            Debug.Log($"[M_PhonePairingUiToggle] Phone pairing UI {(visible ? "shown" : "hidden")}.");
    }

    private void ResolveReferences()
    {
        if (phoneImportMode == null)
            phoneImportMode = GetComponent<M_PhoneImportHeadsetMode>();

        if (pairingUiRoot == null && phoneImportMode != null && phoneImportMode.promptRoot != null)
            pairingUiRoot = phoneImportMode.promptRoot;

        if (xrCamera == null)
            xrCamera = Camera.main;

        if (xrCamera == null)
            xrCamera = OVRManager.FindMainCamera();
    }

    private void EnsureButton()
    {
        if (toggleButton != null || !createButtonIfMissing)
            return;

        RectTransform parent = ResolveButtonParent();
        if (parent == null)
            parent = CreateStandaloneButtonCanvas();

        if (parent == null)
        {
            Debug.LogWarning("[M_PhonePairingUiToggle] Could not create toggle button because no Canvas parent was available.");
            return;
        }

        toggleButton = CreateButton(parent, "PhonePairingToggleButton", generatedButtonAnchoredPosition, generatedButtonSize);
        toggleButtonLabel = toggleButton.GetComponentInChildren<TMP_Text>(true);
        _createdButton = true;
    }

    private RectTransform ResolveButtonParent()
    {
        if (generatedButtonParent != null)
            return generatedButtonParent;

        Canvas canvas = null;
        if (usePairingCanvasForGeneratedButton && pairingUiRoot != null)
            canvas = pairingUiRoot.GetComponentInParent<Canvas>(true);

        if (canvas != null)
        {
            ConfigureCanvasForXRInput(canvas);
            generatedButtonParent = canvas.transform as RectTransform;
            return generatedButtonParent;
        }

        return null;
    }

    private RectTransform CreateStandaloneButtonCanvas()
    {
        GameObject canvasObject = new GameObject("PhonePairingToggleCanvas");
        _generatedCanvasRoot = canvasObject;
        SetLayerRecursive(canvasObject, GetUiLayer());

        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = sortingOrder;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 12f;

        canvasObject.AddComponent<GraphicRaycaster>();
        ConfigureCanvasForXRInput(canvas);

        RectTransform rect = canvasObject.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(420f, 96f);
        rect.localScale = Vector3.one * generatedCanvasScale;
        PlaceStandaloneCanvas(rect);

        generatedButtonParent = rect;
        generatedButtonAnchoredPosition = Vector2.zero;
        return rect;
    }

    private void PlaceStandaloneCanvas(RectTransform rect)
    {
        ResolveReferences();

        if (rect == null || xrCamera == null)
            return;

        Vector3 forward = xrCamera.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            forward = xrCamera.transform.forward;

        forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 position = xrCamera.transform.position +
                           forward * Mathf.Max(0.4f, generatedCanvasDistanceMeters) +
                           right * generatedCanvasOffsetMeters.x +
                           Vector3.up * generatedCanvasOffsetMeters.y;

        rect.position = position;
        rect.rotation = Quaternion.LookRotation(position - xrCamera.transform.position, Vector3.up);
    }

    private Button CreateButton(RectTransform parent, string objectName, Vector2 position, Vector2 size)
    {
        GameObject buttonObject = new GameObject(objectName);
        buttonObject.transform.SetParent(parent, false);
        buttonObject.layer = parent.gameObject.layer;

        RectTransform rect = buttonObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        Image image = buttonObject.AddComponent<Image>();
        image.color = buttonColor;
        image.raycastTarget = true;

        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;

        ColorBlock colors = button.colors;
        colors.normalColor = buttonColor;
        colors.highlightedColor = new Color(
            Mathf.Min(buttonColor.r + 0.08f, 1f),
            Mathf.Min(buttonColor.g + 0.08f, 1f),
            Mathf.Min(buttonColor.b + 0.08f, 1f),
            buttonColor.a);
        colors.pressedColor = new Color(buttonColor.r * 0.75f, buttonColor.g * 0.75f, buttonColor.b * 0.75f, buttonColor.a);
        colors.selectedColor = colors.highlightedColor;
        button.colors = colors;

        GameObject labelObject = new GameObject("Label");
        labelObject.transform.SetParent(buttonObject.transform, false);
        labelObject.layer = buttonObject.layer;

        RectTransform labelRect = labelObject.AddComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(16f, 6f);
        labelRect.offsetMax = new Vector2(-16f, -6f);

        TextMeshProUGUI label = labelObject.AddComponent<TextMeshProUGUI>();
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = false;
        label.fontSize = 24f;
        label.fontStyle = FontStyles.Bold;
        label.color = labelColor;
        label.raycastTarget = false;

        return button;
    }

    private void BindButton()
    {
        if (toggleButton == null)
            return;

        toggleButton.onClick.RemoveListener(Toggle);
        toggleButton.onClick.AddListener(Toggle);

        if (toggleButtonLabel == null)
            toggleButtonLabel = toggleButton.GetComponentInChildren<TMP_Text>(true);
    }

    private void UpdateLabel()
    {
        if (toggleButtonLabel != null)
            toggleButtonLabel.text = IsVisible ? hideLabel : showLabel;
    }

    private void ConfigureCanvasForXRInput(Canvas canvas)
    {
        if (canvas == null)
            return;

        ResolveReferences();
        if (xrCamera != null && canvas.worldCamera == null)
            canvas.worldCamera = xrCamera;

        if (canvas.GetComponent<GraphicRaycaster>() == null)
            canvas.gameObject.AddComponent<GraphicRaycaster>();

        TrackedDeviceGraphicRaycaster trackedRaycaster = canvas.GetComponent<TrackedDeviceGraphicRaycaster>();
        if (trackedRaycaster == null)
            trackedRaycaster = canvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();

        trackedRaycaster.ignoreReversedGraphics = false;
        trackedRaycaster.checkFor2DOcclusion = false;
        trackedRaycaster.checkFor3DOcclusion = false;
    }

    private static int GetUiLayer()
    {
        int uiLayer = LayerMask.NameToLayer("UI");
        return uiLayer >= 0 ? uiLayer : 0;
    }

    private static void SetLayerRecursive(GameObject root, int layer)
    {
        if (root == null)
            return;

        root.layer = layer;
        for (int i = 0; i < root.transform.childCount; i++)
            SetLayerRecursive(root.transform.GetChild(i).gameObject, layer);
    }
}
