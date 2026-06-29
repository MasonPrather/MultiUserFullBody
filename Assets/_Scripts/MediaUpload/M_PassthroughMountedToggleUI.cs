/*
 * Script Name: M_PassthroughMountedToggleUI.cs
 * Author: Mason Prather
 * Description: Builds or binds a headset-mounted world-space button that toggles Quest passthrough during phone import.
 * Project Role: Operator-facing control surface for quickly changing passthrough state without leaving the Unity scene.
 * Key Inputs: XR camera pose, M_PassthroughModeController reference, generated UI sizing and placement settings.
 * Key Outputs: World-space Canvas/Button hierarchy and passthrough toggle commands.
 */

using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

/// <summary>
/// Creates a small headset-following world-space button for toggling Quest passthrough.
/// </summary>
[DisallowMultipleComponent]
public class M_PassthroughMountedToggleUI : MonoBehaviour
{
    private const string PassthroughOnLabel = "Passthrough On";
    private const string PassthroughOffLabel = "Passthrough Off";

    [Header("References")]
    public M_PassthroughModeController passthroughController;
    public Camera xrCamera;
    public GameObject uiRoot;
    public Button toggleButton;
    public TMP_Text toggleButtonText;

    [Header("Placement")]
    [Tooltip("Meters from the headset. X is player-right, Y is up, Z is forward.")]
    public Vector3 headsetOffsetMeters = new Vector3(0.58f, -0.22f, 1.15f);

    [Tooltip("If true, placement follows headset yaw but ignores head pitch/roll for comfort.")]
    public bool useYawOnlyPlacement = true;

    [Tooltip("Seconds used to smooth position changes. Set to 0 to hard mount.")]
    public float positionSmoothTime = 0.08f;

    [Tooltip("How quickly the mounted UI rotates to face the headset.")]
    public float rotationLerpSpeed = 18f;

    [Tooltip("If true, snap to the target placement when enabled before smoothing begins.")]
    public bool snapOnEnable = true;

    [Header("Generated UI")]
    public bool createUiIfMissing = false;
    public Vector2 canvasSize = new Vector2(360f, 96f);
    public Vector2 buttonSize = new Vector2(320f, 68f);
    public float canvasScale = 0.00135f;
    public Color buttonColor = new Color(0.12f, 0.42f, 0.9f, 0.96f);
    public Color buttonHighlightedColor = new Color(0.16f, 0.5f, 1f, 1f);
    public Color buttonPressedColor = new Color(0.08f, 0.28f, 0.65f, 1f);
    public Color labelColor = Color.white;
    public float labelFontSize = 28f;
    public int sortingOrder = 120;

    [Header("Debug")]
    public bool verboseLogging = false;

    private bool _createdUi;
    private bool _hasPlacedUi;
    private Vector3 _positionVelocity;
    private M_PassthroughModeController _subscribedController;

    private void Awake()
    {
        ResolveReferences();
        EnsureUi();
    }

    private void OnEnable()
    {
        ResolveReferences();
        EnsureUi();
        BindButton();
        SubscribePassthroughController();
        UpdateButtonLabel();

        if (snapOnEnable)
            SnapToTarget();
    }

    private void Start()
    {
        ResolveReferences();
        EnsureUi();
        BindButton();
        SubscribePassthroughController();
        UpdateButtonLabel();
        SnapToTarget();
    }

    private void LateUpdate()
    {
        if (uiRoot == null)
            return;

        ResolveCamera();
        if (xrCamera == null)
            return;

        Vector3 targetPosition = GetTargetPosition();
        Quaternion targetRotation = GetFacingRotation(targetPosition);
        Transform uiTransform = uiRoot.transform;

        if (!_hasPlacedUi || positionSmoothTime <= 0f)
        {
            uiTransform.position = targetPosition;
            _positionVelocity = Vector3.zero;
            _hasPlacedUi = true;
        }
        else
        {
            uiTransform.position = Vector3.SmoothDamp(
                uiTransform.position,
                targetPosition,
                ref _positionVelocity,
                positionSmoothTime);
        }

        if (rotationLerpSpeed <= 0f)
        {
            uiTransform.rotation = targetRotation;
        }
        else
        {
            float t = 1f - Mathf.Exp(-rotationLerpSpeed * Time.deltaTime);
            uiTransform.rotation = Quaternion.Slerp(uiTransform.rotation, targetRotation, t);
        }
    }

    private void OnDisable()
    {
        if (toggleButton != null)
            toggleButton.onClick.RemoveListener(TogglePassthrough);

        UnsubscribePassthroughController();
    }

    private void OnDestroy()
    {
        if (_createdUi && uiRoot != null)
            Destroy(uiRoot);

        UnsubscribePassthroughController();
    }

    public void TogglePassthrough()
    {
        ResolveReferences();

        if (passthroughController == null)
        {
            if (verboseLogging)
                Debug.LogWarning("[M_PassthroughMountedToggleUI] Passthrough controller unavailable.");

            return;
        }

        passthroughController.TogglePassthrough();
        UpdateButtonLabel();
    }

    private void ResolveReferences()
    {
        if (passthroughController == null)
            passthroughController = UnityEngine.Object.FindObjectOfType<M_PassthroughModeController>();

        ResolveCamera();
        SubscribePassthroughController();
    }

    private void ResolveCamera()
    {
        if (xrCamera != null)
            return;

        xrCamera = Camera.main;
        if (xrCamera != null)
            return;

        Camera[] cameras = UnityEngine.Object.FindObjectsOfType<Camera>();
        for (int i = 0; i < cameras.Length; i++)
        {
            if (cameras[i] == null || !cameras[i].isActiveAndEnabled)
                continue;

            xrCamera = cameras[i];
            return;
        }
    }

    private void EnsureUi()
    {
        if (uiRoot != null)
        {
            ConfigureExistingUi();
            return;
        }

        if (!createUiIfMissing)
            return;

        GameObject canvasObject = new GameObject("PassthroughMountedToggleUI");
        SetLayerRecursive(canvasObject, GetUiLayer());

        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = sortingOrder;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 12f;

        canvasObject.AddComponent<GraphicRaycaster>();
        ConfigureCanvasForXRInput(canvas);

        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
        canvasRect.sizeDelta = canvasSize;
        canvasRect.localScale = Vector3.one * canvasScale;

        toggleButton = CreateButton(canvasRect);
        toggleButtonText = toggleButton.GetComponentInChildren<TMP_Text>(true);
        uiRoot = canvasObject;
        _createdUi = true;

        BindButton();
    }

    private void ConfigureExistingUi()
    {
        SetLayerRecursive(uiRoot, GetUiLayer());

        Canvas[] canvases = uiRoot.GetComponentsInChildren<Canvas>(true);
        for (int i = 0; i < canvases.Length; i++)
        {
            canvases[i].sortingOrder = sortingOrder;
            ConfigureCanvasForXRInput(canvases[i]);
        }

        if (toggleButton == null)
            toggleButton = uiRoot.GetComponentInChildren<Button>(true);

        if (toggleButtonText == null && toggleButton != null)
            toggleButtonText = toggleButton.GetComponentInChildren<TMP_Text>(true);
    }

    private void ConfigureCanvasForXRInput(Canvas canvas)
    {
        if (canvas == null)
            return;

        ResolveCamera();
        if (xrCamera != null && canvas.worldCamera == null)
            canvas.worldCamera = xrCamera;

        GameObject canvasObject = canvas.gameObject;
        if (canvasObject.GetComponent<GraphicRaycaster>() == null)
            canvasObject.AddComponent<GraphicRaycaster>();

        TrackedDeviceGraphicRaycaster trackedRaycaster = canvasObject.GetComponent<TrackedDeviceGraphicRaycaster>();
        if (trackedRaycaster == null)
            trackedRaycaster = canvasObject.AddComponent<TrackedDeviceGraphicRaycaster>();

        trackedRaycaster.ignoreReversedGraphics = false;
        trackedRaycaster.checkFor2DOcclusion = false;
        trackedRaycaster.checkFor3DOcclusion = false;
    }

    private Button CreateButton(RectTransform parent)
    {
        GameObject buttonObject = new GameObject("TogglePassthroughButton");
        buttonObject.transform.SetParent(parent, false);
        buttonObject.layer = parent.gameObject.layer;

        RectTransform rect = buttonObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = buttonSize;

        Image image = buttonObject.AddComponent<Image>();
        image.color = buttonColor;
        image.raycastTarget = true;

        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;

        ColorBlock colors = button.colors;
        colors.normalColor = buttonColor;
        colors.highlightedColor = buttonHighlightedColor;
        colors.pressedColor = buttonPressedColor;
        colors.selectedColor = buttonHighlightedColor;
        colors.disabledColor = new Color(buttonColor.r, buttonColor.g, buttonColor.b, 0.35f);
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
        label.text = PassthroughOffLabel;
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = false;
        label.fontSize = labelFontSize;
        label.fontStyle = FontStyles.Bold;
        label.color = labelColor;
        label.raycastTarget = false;

        return button;
    }

    private void BindButton()
    {
        if (toggleButton == null)
            return;

        toggleButton.onClick.RemoveListener(TogglePassthrough);
        toggleButton.onClick.AddListener(TogglePassthrough);

        if (toggleButtonText == null)
            toggleButtonText = toggleButton.GetComponentInChildren<TMP_Text>(true);

        if (toggleButtonText != null)
            UpdateButtonLabel();
    }

    private void SubscribePassthroughController()
    {
        if (_subscribedController == passthroughController)
            return;

        UnsubscribePassthroughController();

        if (passthroughController == null)
            return;

        _subscribedController = passthroughController;
        _subscribedController.PassthroughChanged -= HandlePassthroughChanged;
        _subscribedController.PassthroughChanged += HandlePassthroughChanged;
    }

    private void UnsubscribePassthroughController()
    {
        if (_subscribedController == null)
            return;

        _subscribedController.PassthroughChanged -= HandlePassthroughChanged;
        _subscribedController = null;
    }

    private void HandlePassthroughChanged(bool enabled)
    {
        UpdateButtonLabel();
    }

    private void UpdateButtonLabel()
    {
        if (toggleButtonText == null)
            return;

        bool active = passthroughController != null && passthroughController.IsPassthroughEnabled;
        toggleButtonText.text = active ? PassthroughOnLabel : PassthroughOffLabel;
    }

    private void SnapToTarget()
    {
        if (uiRoot == null)
            return;

        ResolveCamera();
        if (xrCamera == null)
            return;

        Vector3 targetPosition = GetTargetPosition();
        Transform uiTransform = uiRoot.transform;
        uiTransform.position = targetPosition;
        uiTransform.rotation = GetFacingRotation(targetPosition);
        _positionVelocity = Vector3.zero;
        _hasPlacedUi = true;
    }

    private Vector3 GetTargetPosition()
    {
        Quaternion placementRotation = GetPlacementRotation();
        return xrCamera.transform.position + placementRotation * headsetOffsetMeters;
    }

    private Quaternion GetPlacementRotation()
    {
        if (!useYawOnlyPlacement)
            return xrCamera.transform.rotation;

        Vector3 forward = xrCamera.transform.forward;
        forward.y = 0f;

        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;

        return Quaternion.LookRotation(forward.normalized, Vector3.up);
    }

    private Quaternion GetFacingRotation(Vector3 uiPosition)
    {
        Vector3 direction = uiPosition - xrCamera.transform.position;

        if (direction.sqrMagnitude < 0.0001f)
            direction = xrCamera.transform.forward;

        return Quaternion.LookRotation(direction.normalized, Vector3.up);
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
        Transform rootTransform = root.transform;
        for (int i = 0; i < rootTransform.childCount; i++)
            SetLayerRecursive(rootTransform.GetChild(i).gameObject, layer);
    }
}
