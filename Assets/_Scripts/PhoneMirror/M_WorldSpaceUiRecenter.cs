/*
 * Script Name: M_WorldSpaceUiRecenter.cs
 * Author: Mason Prather
 * Description: Recenters a world-space UI transform in front of the active headset/camera from a public method or VR-friendly Button.
 * Project Role: Reusable reposition control for pairing, mirroring, and media UI panels.
 * Key Inputs: UI root transform, headset/camera transform, distance/offset settings, and optional Button/TMP label.
 * Key Outputs: Updated world position/rotation for the UI root.
 */

using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

[DisallowMultipleComponent]
public class M_WorldSpaceUiRecenter : MonoBehaviour
{
    [Header("Target")]
    public Transform uiRoot;
    public Transform headOrCameraTransform;

    [Header("Button")]
    public Button recenterButton;
    public TMP_Text recenterButtonLabel;
    public string recenterLabel = "Recenter";

    [Header("Placement")]
    public float distanceFromHead = 1.2f;
    public float verticalOffset = -0.1f;
    public float horizontalOffset = 0f;
    public bool faceUser = true;
    public bool flattenYawOnly = true;
    public bool recenterOnStart = false;

    [Header("Generated Button")]
    public bool createButtonIfMissing = false;
    public RectTransform generatedButtonParent;
    public bool useTargetCanvasForGeneratedButton = true;
    public Vector2 generatedButtonAnchoredPosition = new Vector2(-25f, -585f);
    public Vector2 generatedButtonSize = new Vector2(240f, 68f);
    public float generatedCanvasDistanceMeters = 1.25f;
    public Vector3 generatedCanvasOffsetMeters = new Vector3(0.12f, -0.28f, 0f);
    public float generatedCanvasScale = 0.00135f;
    public int sortingOrder = 131;
    public Color buttonColor = new Color(0.16f, 0.5f, 0.36f, 0.96f);
    public Color labelColor = Color.white;

    [Header("Debug")]
    public bool verboseLogging = false;

    private GameObject _generatedCanvasRoot;
    private bool _createdButton;

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
    }

    private void Start()
    {
        ResolveReferences();
        EnsureButton();
        BindButton();

        if (recenterOnStart)
            RecenterNow();
    }

    private void OnDisable()
    {
        if (recenterButton != null)
            recenterButton.onClick.RemoveListener(RecenterNow);
    }

    private void OnDestroy()
    {
        if (_createdButton && recenterButton != null)
            Destroy(recenterButton.gameObject);

        if (_generatedCanvasRoot != null)
            Destroy(_generatedCanvasRoot);
    }

    public void RefreshBinding()
    {
        ResolveReferences();
        EnsureButton();
        BindButton();
    }

    public void RecenterNow()
    {
        ResolveReferences();

        if (uiRoot == null)
        {
            Debug.LogWarning("[M_WorldSpaceUiRecenter] Cannot recenter because uiRoot is not assigned.");
            return;
        }

        if (headOrCameraTransform == null)
        {
            Debug.LogWarning("[M_WorldSpaceUiRecenter] Cannot recenter because no headset/camera transform was found.");
            return;
        }

        Vector3 forward = headOrCameraTransform.forward;
        if (flattenYawOnly)
            forward.y = 0f;

        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;

        forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 targetPosition = headOrCameraTransform.position +
                                 forward * Mathf.Max(0.2f, distanceFromHead) +
                                 right * horizontalOffset +
                                 Vector3.up * verticalOffset;

        uiRoot.position = targetPosition;

        if (faceUser)
        {
            Vector3 direction = uiRoot.position - headOrCameraTransform.position;
            if (flattenYawOnly)
                direction.y = 0f;

            if (direction.sqrMagnitude < 0.0001f)
                direction = forward;

            uiRoot.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }

        if (verboseLogging)
            Debug.Log($"[M_WorldSpaceUiRecenter] Recentered '{uiRoot.name}' in front of the user.");
    }

    private void ResolveReferences()
    {
        if (headOrCameraTransform != null)
            return;

        Camera camera = Camera.main;
        if (camera == null)
            camera = OVRManager.FindMainCamera();

        if (camera != null)
            headOrCameraTransform = camera.transform;
    }

    private void EnsureButton()
    {
        if (recenterButton != null || !createButtonIfMissing)
            return;

        RectTransform parent = ResolveButtonParent();
        if (parent == null)
            parent = CreateStandaloneButtonCanvas();

        if (parent == null)
        {
            Debug.LogWarning("[M_WorldSpaceUiRecenter] Could not create recenter button because no Canvas parent was available.");
            return;
        }

        recenterButton = CreateButton(parent, "PhonePairingRecenterButton", generatedButtonAnchoredPosition, generatedButtonSize);
        recenterButtonLabel = recenterButton.GetComponentInChildren<TMP_Text>(true);
        _createdButton = true;
    }

    private RectTransform ResolveButtonParent()
    {
        if (generatedButtonParent != null)
            return generatedButtonParent;

        Canvas canvas = null;
        if (useTargetCanvasForGeneratedButton && uiRoot != null)
            canvas = uiRoot.GetComponentInParent<Canvas>(true);

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
        GameObject canvasObject = new GameObject("PhonePairingRecenterCanvas");
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
        rect.sizeDelta = new Vector2(300f, 96f);
        rect.localScale = Vector3.one * generatedCanvasScale;
        PlaceStandaloneCanvas(rect);

        generatedButtonParent = rect;
        generatedButtonAnchoredPosition = Vector2.zero;
        return rect;
    }

    private void PlaceStandaloneCanvas(RectTransform rect)
    {
        ResolveReferences();

        if (rect == null || headOrCameraTransform == null)
            return;

        Vector3 forward = headOrCameraTransform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            forward = headOrCameraTransform.forward;

        forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 position = headOrCameraTransform.position +
                           forward * Mathf.Max(0.4f, generatedCanvasDistanceMeters) +
                           right * generatedCanvasOffsetMeters.x +
                           Vector3.up * generatedCanvasOffsetMeters.y;

        rect.position = position;
        rect.rotation = Quaternion.LookRotation(position - headOrCameraTransform.position, Vector3.up);
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
        labelRect.offsetMin = new Vector2(14f, 6f);
        labelRect.offsetMax = new Vector2(-14f, -6f);

        TextMeshProUGUI label = labelObject.AddComponent<TextMeshProUGUI>();
        label.text = recenterLabel;
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
        if (recenterButton == null)
            return;

        recenterButton.onClick.RemoveListener(RecenterNow);
        recenterButton.onClick.AddListener(RecenterNow);

        if (recenterButtonLabel == null)
            recenterButtonLabel = recenterButton.GetComponentInChildren<TMP_Text>(true);

        if (recenterButtonLabel != null)
            recenterButtonLabel.text = recenterLabel;
    }

    private void ConfigureCanvasForXRInput(Canvas canvas)
    {
        if (canvas == null)
            return;

        if (headOrCameraTransform != null && canvas.worldCamera == null)
            canvas.worldCamera = headOrCameraTransform.GetComponent<Camera>();

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
