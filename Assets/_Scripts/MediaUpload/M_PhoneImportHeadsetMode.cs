/*
 * Script Name: M_PhoneImportHeadsetMode.cs
 * Author: Mason Prather
 * Description: Presents the headset-side phone import prompt, displays upload URLs and pairing code, manages optional passthrough pairing mode, and restores scene visibility when pairing closes.
 * Project Role: Headset-on pairing workflow for the phone media upload path in PhonePhotoUpload scenes.
 * Key Inputs: M_ServerBootstrap published instructions, phone upload bridge state, XR camera pose, passthrough controller state, and serialized prompt/environment references.
 * Key Outputs: Pairing prompt text, optional generated world-space UI, passthrough toggles, temporary scene isolation, and restored renderer/camera state.
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

/// <summary>
/// Keeps the phone photo import flow usable without removing the headset.
/// It presents the phone URL/code as a headset overlay and can enable Quest
/// passthrough so the old dark pairing room is not needed.
/// </summary>
public class M_PhoneImportHeadsetMode : MonoBehaviour
{
    [Header("Flow")]
    [Tooltip("If true, enter phone pairing mode as soon as the scene starts.")]
    public bool activateOnStart = false;

    [Tooltip("If true, hide the pairing prompt/environment after the first phone upload is detected.")]
    public bool closePromptAfterFirstUpload = true;

    [Tooltip("Seconds to keep the prompt visible after the first upload arrives.")]
    public float closeDelayAfterUploadSeconds = 2f;

    [Header("References")]
    public M_ServerBootstrap serverBootstrap;
    public M_PhoneUploadToDisplay phoneUploadBridge;
    public M_PassthroughModeController passthroughController;
    public Camera xrCamera;

    [Header("Passthrough")]
    [Tooltip("If true, pairing mode uses Quest passthrough instead of the generated dark room.")]
    public bool usePassthroughForPairing = true;

    [Tooltip("If true, entering pairing mode turns passthrough on automatically.")]
    public bool enablePassthroughOnEnter = true;

    [Tooltip("If true, leaving pairing mode turns passthrough off. Keep false when passthrough should replace the room.")]
    public bool disablePassthroughOnExit = false;

    [Tooltip("If true, the automatic prompt close caused by the first phone upload also turns passthrough off.")]
    public bool disablePassthroughAfterFirstUpload = true;

    [Tooltip("If true, add a passthrough controller at runtime when no scene controller exists.")]
    public bool createPassthroughControllerIfMissing = true;

    [Tooltip("If true, add a passthrough toggle button to the generated prompt.")]
    public bool createPassthroughToggleIfMissing = false;

    public Button passthroughToggleButton;
    public TMP_Text passthroughToggleButtonText;

    [Header("Pairing UI Controls")]
    [Tooltip("If true, create an always-visible runtime button that shows/hides the phone pairing prompt.")]
    public bool createPairingToggleIfMissing = false;

    [Tooltip("If true, create an always-visible runtime button that recenters the phone pairing prompt.")]
    public bool createPairingRecenterIfMissing = false;

    public M_PhonePairingUiToggle pairingUiToggle;
    public M_WorldSpaceUiRecenter pairingUiRecenter;

    [Header("UI")]
    [Tooltip("Root object for an existing prompt. If empty, one is generated at runtime.")]
    public GameObject promptRoot;

    public TMP_Text titleText;
    public TMP_Text instructionsText;
    public TMP_Text statusText;

    [Tooltip("If no prompt text is assigned, create a simple world-space prompt at runtime.")]
    public bool createPromptIfMissing = false;

    [Tooltip("Maximum URLs shown in the headset prompt.")]
    public int maxDisplayedUrls = 2;

    [Header("Placement")]
    [Tooltip("Distance in front of the headset where the generated prompt appears.")]
    public float promptDistanceMeters = 1.35f;

    [Tooltip("Prompt offset in headset-local meters.")]
    public Vector3 promptOffsetMeters = new Vector3(0f, 0.14f, 0f);

    [Tooltip("If true, place the prompt in front of the headset when pairing mode starts.")]
    public bool recenterPromptOnEnter = true;

    [Tooltip("If true, keep the prompt rotated toward the headset while it is visible.")]
    public bool billboardPromptToHeadset = true;

    [Header("Pairing Environment")]
    [Tooltip("Root object for an existing neutral environment. If empty, a dark sphere is generated at runtime.")]
    public GameObject environmentRoot;

    [Tooltip("If true, create a dark inside-out sphere when no environment root is assigned.")]
    public bool createEnvironmentIfMissing = true;

    [Tooltip("If true, center the pairing sphere/environment on the headset when pairing mode starts.")]
    public bool centerEnvironmentOnEnter = true;

    [Tooltip("Radius of the generated pairing sphere.")]
    public float environmentRadiusMeters = 8f;

    [Tooltip("Color used by the generated sphere and camera background.")]
    public Color environmentColor = new Color(0.025f, 0.027f, 0.03f, 1f);

    [Tooltip("If true, set the camera clear color to the environment color while pairing mode is active.")]
    public bool setCameraBackgroundDuringPairing = true;

    [Tooltip("If true, temporarily hide normal scene renderers so the user starts in the pairing environment.")]
    public bool isolateSceneDuringPairing = true;

    [Tooltip("Objects that should keep their renderers while pairing mode hides the rest of the scene.")]
    public GameObject[] rendererRootsToKeepVisible;

    [Header("Debug")]
    public bool verboseLogging = false;

    private const int SphereSegments = 48;
    private const int SphereRings = 24;

    private readonly List<RendererState> _hiddenRenderers = new List<RendererState>();
    private bool _isPairingModeActive;
    private bool _createdPrompt;
    private bool _createdEnvironment;
    private bool _createdPassthroughToggle;
    private bool _changedCameraBackground;
    private CameraClearFlags _previousCameraClearFlags;
    private Color _previousCameraBackgroundColor;
    private string _lastObservedUploadPath;
    private Coroutine _closePromptCoroutine;
    private bool _exitingAfterFirstUpload;

    public bool IsPairingModeActive => _isPairingModeActive;

    private struct RendererState
    {
        public Renderer renderer;
        public bool enabled;
    }

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        SubscribeServerEvents();
        SubscribePassthroughEvents();
    }

    private void Start()
    {
        StartCoroutine(StartCoroutineDeferred());
    }

    private void OnDisable()
    {
        UnsubscribeServerEvents();
        UnsubscribePassthroughEvents();

        if (_isPairingModeActive)
            ExitPhoneImportMode();
    }

    private void OnDestroy()
    {
        UnsubscribeServerEvents();
        UnsubscribePassthroughEvents();
    }

    private IEnumerator StartCoroutineDeferred()
    {
        yield return null;

        ResolveReferences();
        SubscribeServerEvents();
        SubscribePassthroughEvents();
        EnsureEnvironment();
        EnsurePrompt();
        EnsurePairingUiControls();
        RefreshPromptText();

        _lastObservedUploadPath = M_SimpleHttpServer.LastSavedPhotoPath;

        if (activateOnStart)
            EnterPhoneImportMode();
    }

    private void Update()
    {
        if (!_isPairingModeActive || !closePromptAfterFirstUpload)
            return;

        string uploadPath = M_SimpleHttpServer.LastSavedPhotoPath;
        if (string.IsNullOrEmpty(uploadPath) ||
            string.Equals(uploadPath, _lastObservedUploadPath, StringComparison.Ordinal))
        {
            return;
        }

        _lastObservedUploadPath = uploadPath;
        SetStatus("Photo received. Opening gallery...");

        if (_closePromptCoroutine != null)
            StopCoroutine(_closePromptCoroutine);

        _closePromptCoroutine = StartCoroutine(ClosePromptAfterDelay());
    }

    private void LateUpdate()
    {
        if (!_isPairingModeActive || !billboardPromptToHeadset || promptRoot == null)
            return;

        ResolveCamera();
        if (xrCamera == null)
            return;

        Vector3 direction = promptRoot.transform.position - xrCamera.transform.position;
        if (direction.sqrMagnitude > 0.0001f)
            promptRoot.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
    }

    public void EnterPhoneImportMode()
    {
        if (_isPairingModeActive)
        {
            RefreshPromptText();
            return;
        }

        ResolveReferences();
        EnsureEnvironment();
        EnsurePrompt();
        EnsurePairingUiControls();

        _isPairingModeActive = true;

        if (usePassthroughForPairing && enablePassthroughOnEnter && passthroughController != null)
            passthroughController.SetPassthroughEnabled(true);

        bool passthroughActive = IsPassthroughActiveForPairing();

        if (environmentRoot != null)
            environmentRoot.SetActive(!passthroughActive && !usePassthroughForPairing);

        if (promptRoot != null)
            promptRoot.SetActive(true);

        if (!passthroughActive && !usePassthroughForPairing && centerEnvironmentOnEnter)
            PlaceEnvironmentAroundHeadset();

        if (recenterPromptOnEnter)
            PlacePromptInFrontOfHeadset();

        RefreshPromptText();
        UpdatePassthroughToggleButton();

        if (!passthroughActive && !usePassthroughForPairing)
            ApplyCameraBackground();

        if (passthroughActive || !usePassthroughForPairing)
            HideSceneRenderers();
        else
            RestoreSceneRenderers();

        SetStatus(passthroughActive
            ? "Waiting for phone upload. Passthrough is on."
            : "Waiting for phone upload.");

        if (verboseLogging)
            Debug.Log("[M_PhoneImportHeadsetMode] Phone pairing mode entered.");
    }

    public void ExitPhoneImportMode()
    {
        if (!_isPairingModeActive)
            return;

        _isPairingModeActive = false;

        if (_closePromptCoroutine != null)
        {
            StopCoroutine(_closePromptCoroutine);
            _closePromptCoroutine = null;
        }

        bool shouldDisablePassthrough = passthroughController != null &&
            (disablePassthroughOnExit || (_exitingAfterFirstUpload && disablePassthroughAfterFirstUpload));

        if (shouldDisablePassthrough)
            passthroughController.SetPassthroughEnabled(false);

        RestoreSceneRenderers();
        RestoreCameraBackground();

        if (promptRoot != null)
            promptRoot.SetActive(false);

        if (environmentRoot != null)
            environmentRoot.SetActive(false);

        if (verboseLogging)
            Debug.Log("[M_PhoneImportHeadsetMode] Phone pairing mode exited.");
    }

    public void RefreshPromptText()
    {
        ResolveReferences();
        EnsureEnvironment();
        EnsurePrompt();

        if (titleText != null)
            titleText.text = "Pair Your Phone";

        if (instructionsText != null)
            instructionsText.text = BuildHeadsetInstructions();

        UpdatePassthroughToggleButton();
    }

    public string GetPhonePairingInstructions()
    {
        ResolveReferences();
        return BuildHeadsetInstructions();
    }

    public void TogglePassthrough()
    {
        ResolveReferences();

        if (passthroughController == null)
        {
            SetStatus("Passthrough controller unavailable.");
            return;
        }

        passthroughController.TogglePassthrough();
    }

    public void TogglePhoneImportMode()
    {
        SetPhoneImportModeVisible(!_isPairingModeActive);
    }

    public void SetPhoneImportModeVisible(bool visible)
    {
        if (visible)
            EnterPhoneImportMode();
        else
            ExitPhoneImportMode();
    }

    public void RecenterPrompt()
    {
        ResolveReferences();
        EnsurePrompt();
        PlacePromptInFrontOfHeadset();
    }

    public void SetPassthroughEnabled(bool enabled)
    {
        ResolveReferences();

        if (passthroughController == null)
        {
            SetStatus("Passthrough controller unavailable.");
            return;
        }

        passthroughController.SetPassthroughEnabled(enabled);
    }

    private IEnumerator ClosePromptAfterDelay()
    {
        yield return new WaitForSeconds(Mathf.Max(0f, closeDelayAfterUploadSeconds));
        _closePromptCoroutine = null;

        _exitingAfterFirstUpload = true;
        try
        {
            ExitPhoneImportMode();
        }
        finally
        {
            _exitingAfterFirstUpload = false;
        }
    }

    private void ResolveReferences()
    {
        if (serverBootstrap == null)
            serverBootstrap = UnityEngine.Object.FindObjectOfType<M_ServerBootstrap>();

        if (phoneUploadBridge == null)
            phoneUploadBridge = UnityEngine.Object.FindObjectOfType<M_PhoneUploadToDisplay>();

        if (passthroughController == null)
            passthroughController = UnityEngine.Object.FindObjectOfType<M_PassthroughModeController>();

        if (passthroughController == null && createPassthroughControllerIfMissing)
            passthroughController = gameObject.AddComponent<M_PassthroughModeController>();

        ResolveCamera();
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

    private void SubscribeServerEvents()
    {
        if (serverBootstrap == null)
            return;

        serverBootstrap.InstructionsPublished -= HandleInstructionsPublished;
        serverBootstrap.InstructionsPublished += HandleInstructionsPublished;
    }

    private void SubscribePassthroughEvents()
    {
        if (passthroughController == null)
            return;

        passthroughController.PassthroughChanged -= HandlePassthroughChanged;
        passthroughController.PassthroughChanged += HandlePassthroughChanged;
    }

    private void UnsubscribeServerEvents()
    {
        if (serverBootstrap != null)
            serverBootstrap.InstructionsPublished -= HandleInstructionsPublished;
    }

    private void UnsubscribePassthroughEvents()
    {
        if (passthroughController != null)
            passthroughController.PassthroughChanged -= HandlePassthroughChanged;
    }

    private void HandleInstructionsPublished(M_ServerBootstrap bootstrap)
    {
        RefreshPromptText();
    }

    private void HandlePassthroughChanged(bool enabled)
    {
        UpdatePassthroughToggleButton();

        if (!_isPairingModeActive)
            return;

        if (environmentRoot != null && usePassthroughForPairing)
            environmentRoot.SetActive(false);

        if (enabled)
        {
            HideSceneRenderers();
            SetStatus("Passthrough is on.");
        }
        else
        {
            RestoreSceneRenderers();
            SetStatus("Passthrough is off.");
        }
    }

    private string BuildHeadsetInstructions()
    {
        if (serverBootstrap == null)
        {
            return "Starting phone import...\n\n" +
                   "Keep the phone and headset on the same Wi-Fi network.";
        }

        string[] urls = serverBootstrap.PublishedPhoneUrls;
        string code = serverBootstrap.EffectivePairingCode;

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("Open on the phone:");

        if (urls == null || urls.Length == 0)
        {
            sb.AppendLine($"<quest-ip>:{serverBootstrap.httpPort}");
        }
        else
        {
            int count = Mathf.Min(Mathf.Max(1, maxDisplayedUrls), urls.Length);
            for (int i = 0; i < count; i++)
                sb.AppendLine(urls[i]);
        }

        if (!string.IsNullOrWhiteSpace(code))
        {
            sb.AppendLine();
            sb.AppendLine("Code:");
            sb.AppendLine(FormatCodeForReading(code));
        }

        sb.AppendLine();
        sb.Append("Choose photos in the browser. They will appear here.");
        return sb.ToString();
    }

    private static string FormatCodeForReading(string code)
    {
        string trimmed = (code ?? string.Empty).Trim();
        if (trimmed.Length <= 3)
            return trimmed;

        StringBuilder sb = new StringBuilder(trimmed.Length + trimmed.Length / 3);
        for (int i = 0; i < trimmed.Length; i++)
        {
            if (i > 0 && i % 3 == 0)
                sb.Append(' ');

            sb.Append(trimmed[i]);
        }

        return sb.ToString();
    }

    private void SetStatus(string value)
    {
        if (statusText != null)
            statusText.text = value;

        if (verboseLogging)
            Debug.Log("[M_PhoneImportHeadsetMode] " + value);
    }

    private void EnsurePrompt()
    {
        if (instructionsText != null && promptRoot != null)
        {
            EnsurePromptCanvasSupportsXRInput();
            EnsurePassthroughToggle();
            EnsurePairingUiControls();
            return;
        }

        if (!createPromptIfMissing || _createdPrompt)
        {
            EnsurePromptCanvasSupportsXRInput();
            EnsurePassthroughToggle();
            EnsurePairingUiControls();
            return;
        }

        CreateGeneratedPrompt();
        EnsurePromptCanvasSupportsXRInput();
        EnsurePassthroughToggle();
        EnsurePairingUiControls();
    }

    private void CreateGeneratedPrompt()
    {
        GameObject canvasObject = new GameObject("PhoneImportHeadsetPrompt");
        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer >= 0)
            canvasObject.layer = uiLayer;

        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 12f;

        canvasObject.AddComponent<GraphicRaycaster>();
        ConfigureCanvasForXRInput(canvas);

        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(1040f, 760f);
        canvasRect.localScale = Vector3.one * 0.0018f;

        Image background = canvasObject.AddComponent<Image>();
        background.color = new Color(0.02f, 0.024f, 0.028f, 0.94f);

        titleText = CreateText(canvasRect, "Title", new Vector2(0f, 282f), new Vector2(920f, 92f), 58f, FontStyles.Bold);
        instructionsText = CreateText(canvasRect, "Instructions", new Vector2(0f, 38f), new Vector2(920f, 390f), 34f, FontStyles.Normal);
        statusText = CreateText(canvasRect, "Status", new Vector2(0f, -326f), new Vector2(920f, 62f), 24f, FontStyles.Italic);

        promptRoot = canvasObject;
        _createdPrompt = true;

        PlacePromptInFrontOfHeadset();

        if (!_isPairingModeActive && !activateOnStart)
            canvasObject.SetActive(false);
    }

    private void EnsurePromptCanvasSupportsXRInput()
    {
        if (promptRoot == null)
            return;

        Canvas[] canvases = promptRoot.GetComponentsInChildren<Canvas>(true);
        if (canvases == null || canvases.Length == 0)
            return;

        for (int i = 0; i < canvases.Length; i++)
            ConfigureCanvasForXRInput(canvases[i]);
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

    private void EnsurePassthroughToggle()
    {
        if (passthroughToggleButton != null)
        {
            BindPassthroughToggleButton();
            return;
        }

        if (!createPassthroughToggleIfMissing || promptRoot == null || _createdPassthroughToggle)
            return;

        RectTransform parent = promptRoot.GetComponent<RectTransform>();
        if (parent == null)
            return;

        passthroughToggleButton = CreateButton(
            parent,
            "PassthroughToggleButton",
            new Vector2(0f, -246f),
            new Vector2(430f, 64f),
            "Passthrough");

        passthroughToggleButtonText = passthroughToggleButton.GetComponentInChildren<TMP_Text>(true);
        BindPassthroughToggleButton();
        _createdPassthroughToggle = true;
    }

    private Button CreateButton(RectTransform parent, string name, Vector2 position, Vector2 size, string label)
    {
        GameObject buttonObject = new GameObject(name);
        buttonObject.transform.SetParent(parent, false);
        buttonObject.layer = parent.gameObject.layer;

        RectTransform rect = buttonObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        Image image = buttonObject.AddComponent<Image>();
        image.color = new Color(0.12f, 0.42f, 0.9f, 0.95f);

        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;

        GameObject labelObject = new GameObject("Label");
        labelObject.transform.SetParent(buttonObject.transform, false);
        labelObject.layer = buttonObject.layer;

        RectTransform labelRect = labelObject.AddComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(18f, 6f);
        labelRect.offsetMax = new Vector2(-18f, -6f);

        TextMeshProUGUI labelText = labelObject.AddComponent<TextMeshProUGUI>();
        labelText.text = label;
        labelText.alignment = TextAlignmentOptions.Center;
        labelText.fontSize = 28f;
        labelText.fontStyle = FontStyles.Bold;
        labelText.color = Color.white;
        labelText.raycastTarget = false;

        return button;
    }

    private void BindPassthroughToggleButton()
    {
        if (passthroughToggleButton == null)
            return;

        if (passthroughToggleButtonText == null)
            passthroughToggleButtonText = passthroughToggleButton.GetComponentInChildren<TMP_Text>(true);

        passthroughToggleButton.onClick.RemoveListener(TogglePassthrough);
        passthroughToggleButton.onClick.AddListener(TogglePassthrough);
        UpdatePassthroughToggleButton();
    }

    private void EnsurePairingUiControls()
    {
        if (promptRoot == null)
            return;

        if (createPairingToggleIfMissing)
        {
            if (pairingUiToggle == null)
                pairingUiToggle = GetComponent<M_PhonePairingUiToggle>();

            if (pairingUiToggle == null)
                pairingUiToggle = gameObject.AddComponent<M_PhonePairingUiToggle>();

            pairingUiToggle.phoneImportMode = this;
            pairingUiToggle.pairingUiRoot = promptRoot;
            pairingUiToggle.xrCamera = xrCamera;
            pairingUiToggle.createButtonIfMissing = true;
            pairingUiToggle.usePairingCanvasForGeneratedButton = false;
            pairingUiToggle.applyStartVisibleOnStart = false;
            pairingUiToggle.startVisible = _isPairingModeActive || (promptRoot != null && promptRoot.activeSelf);
            pairingUiToggle.RefreshBinding();
        }

        if (createPairingRecenterIfMissing)
        {
            if (pairingUiRecenter == null)
                pairingUiRecenter = GetComponent<M_WorldSpaceUiRecenter>();

            if (pairingUiRecenter == null)
                pairingUiRecenter = gameObject.AddComponent<M_WorldSpaceUiRecenter>();

            pairingUiRecenter.uiRoot = promptRoot.transform;
            pairingUiRecenter.headOrCameraTransform = xrCamera != null ? xrCamera.transform : null;
            pairingUiRecenter.createButtonIfMissing = true;
            pairingUiRecenter.useTargetCanvasForGeneratedButton = false;
            pairingUiRecenter.recenterOnStart = false;
            pairingUiRecenter.RefreshBinding();
        }
    }

    private void UpdatePassthroughToggleButton()
    {
        if (passthroughToggleButtonText == null)
            return;

        bool active = IsPassthroughActiveForPairing();
        passthroughToggleButtonText.text = active ? "Passthrough On" : "Passthrough Off";
    }

    private bool IsPassthroughActiveForPairing()
    {
        return usePassthroughForPairing &&
               passthroughController != null &&
               passthroughController.IsPassthroughEnabled;
    }

    private static TMP_Text CreateText(RectTransform parent, string name, Vector2 position, Vector2 size, float fontSize, FontStyles style)
    {
        GameObject textObject = new GameObject(name);
        textObject.transform.SetParent(parent, false);
        textObject.layer = parent.gameObject.layer;

        RectTransform rect = textObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = true;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.color = Color.white;
        text.margin = new Vector4(10f, 4f, 10f, 4f);

        return text;
    }

    private void EnsureEnvironment()
    {
        if (usePassthroughForPairing)
            return;

        if (environmentRoot != null || !createEnvironmentIfMissing || _createdEnvironment)
            return;

        GameObject sphereObject = new GameObject("PhoneImportPairingEnvironment");
        MeshFilter meshFilter = sphereObject.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = sphereObject.AddComponent<MeshRenderer>();

        meshFilter.sharedMesh = CreateDoubleSidedSphereMesh(Mathf.Max(1f, environmentRadiusMeters), SphereSegments, SphereRings);
        meshRenderer.sharedMaterial = CreateEnvironmentMaterial(environmentColor);

        environmentRoot = sphereObject;
        _createdEnvironment = true;

        PlaceEnvironmentAroundHeadset();

        if (!_isPairingModeActive && !activateOnStart)
            sphereObject.SetActive(false);
    }

    private static Mesh CreateDoubleSidedSphereMesh(float radius, int segments, int rings)
    {
        int safeSegments = Mathf.Max(8, segments);
        int safeRings = Mathf.Max(4, rings);

        List<Vector3> vertices = new List<Vector3>((safeSegments + 1) * (safeRings + 1));
        List<int> triangles = new List<int>(safeSegments * safeRings * 12);

        for (int ring = 0; ring <= safeRings; ring++)
        {
            float v = ring / (float)safeRings;
            float theta = v * Mathf.PI;
            float sinTheta = Mathf.Sin(theta);
            float cosTheta = Mathf.Cos(theta);

            for (int segment = 0; segment <= safeSegments; segment++)
            {
                float u = segment / (float)safeSegments;
                float phi = u * Mathf.PI * 2f;
                float sinPhi = Mathf.Sin(phi);
                float cosPhi = Mathf.Cos(phi);

                vertices.Add(new Vector3(
                    sinTheta * cosPhi * radius,
                    cosTheta * radius,
                    sinTheta * sinPhi * radius));
            }
        }

        for (int ring = 0; ring < safeRings; ring++)
        {
            for (int segment = 0; segment < safeSegments; segment++)
            {
                int a = ring * (safeSegments + 1) + segment;
                int b = a + 1;
                int c = a + safeSegments + 1;
                int d = c + 1;

                AddDoubleSidedTriangle(triangles, a, c, b);
                AddDoubleSidedTriangle(triangles, b, c, d);
            }
        }

        Mesh mesh = new Mesh();
        mesh.name = "PhoneImportPairingSphere";
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void AddDoubleSidedTriangle(List<int> triangles, int a, int b, int c)
    {
        triangles.Add(a);
        triangles.Add(b);
        triangles.Add(c);
        triangles.Add(c);
        triangles.Add(b);
        triangles.Add(a);
    }

    private static Material CreateEnvironmentMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");

        if (shader == null)
            shader = Shader.Find("Standard");

        Material material = new Material(shader);
        material.name = "PhoneImportPairingEnvironment";

        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);

        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);

        return material;
    }

    private void PlaceEnvironmentAroundHeadset()
    {
        if (environmentRoot == null)
            return;

        ResolveCamera();
        if (xrCamera == null)
            return;

        environmentRoot.transform.position = xrCamera.transform.position;
        environmentRoot.transform.rotation = Quaternion.identity;
    }

    private void PlacePromptInFrontOfHeadset()
    {
        if (promptRoot == null)
            return;

        ResolveCamera();
        if (xrCamera == null)
            return;

        Vector3 localOffset = promptOffsetMeters;
        localOffset.z += Mathf.Max(0.4f, promptDistanceMeters);
        promptRoot.transform.position = xrCamera.transform.TransformPoint(localOffset);
        promptRoot.transform.rotation = Quaternion.LookRotation(promptRoot.transform.position - xrCamera.transform.position, Vector3.up);
    }

    private void ApplyCameraBackground()
    {
        if (usePassthroughForPairing)
            return;

        if (!setCameraBackgroundDuringPairing)
            return;

        ResolveCamera();
        if (xrCamera == null || _changedCameraBackground)
            return;

        _previousCameraClearFlags = xrCamera.clearFlags;
        _previousCameraBackgroundColor = xrCamera.backgroundColor;
        _changedCameraBackground = true;

        xrCamera.clearFlags = CameraClearFlags.SolidColor;
        xrCamera.backgroundColor = environmentColor;
    }

    private void RestoreCameraBackground()
    {
        if (!_changedCameraBackground || xrCamera == null)
            return;

        xrCamera.clearFlags = _previousCameraClearFlags;
        xrCamera.backgroundColor = _previousCameraBackgroundColor;
        _changedCameraBackground = false;
    }

    private void HideSceneRenderers()
    {
        if (!isolateSceneDuringPairing || _hiddenRenderers.Count > 0)
            return;

        Renderer[] renderers = UnityEngine.Object.FindObjectsOfType<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || ShouldKeepRendererVisible(renderer))
                continue;

            _hiddenRenderers.Add(new RendererState
            {
                renderer = renderer,
                enabled = renderer.enabled
            });

            renderer.enabled = false;
        }
    }

    private void RestoreSceneRenderers()
    {
        for (int i = 0; i < _hiddenRenderers.Count; i++)
        {
            RendererState state = _hiddenRenderers[i];
            if (state.renderer != null)
                state.renderer.enabled = state.enabled;
        }

        _hiddenRenderers.Clear();
    }

    private bool ShouldKeepRendererVisible(Renderer renderer)
    {
        if (renderer == null)
            return true;

        Transform rendererTransform = renderer.transform;

        if (promptRoot != null && rendererTransform.IsChildOf(promptRoot.transform))
            return true;

        if (environmentRoot != null && rendererTransform.IsChildOf(environmentRoot.transform))
            return true;

        if (rendererRootsToKeepVisible != null)
        {
            for (int i = 0; i < rendererRootsToKeepVisible.Length; i++)
            {
                GameObject root = rendererRootsToKeepVisible[i];
                if (root != null && rendererTransform.IsChildOf(root.transform))
                    return true;
            }
        }

        return false;
    }
}
