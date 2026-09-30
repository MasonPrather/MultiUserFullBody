/*
 * Script Name: M_QuestUserMenu.cs
 * Author: Mason Prather
 * Description: Provides a summonable Quest world-space utility menu for passthrough, phone upload pairing instructions, player recentering, and dismissal.
 * Project Role: Runtime replacement for scattered camera-mounted and static scene controls in the PhonePhotoUpload workflow.
 * Key Inputs: OVR controller button, XR camera pose, M_PassthroughModeController, M_ServerBootstrap, optional M_PhoneImportHeadsetMode, and optional CharacterResetter.
 * Key Outputs: Stable world-space menu placement, passthrough commands, current phone pairing text, player/menu recenter command, and close behavior.
 */

using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
public class M_QuestUserMenu : MonoBehaviour
{
    [Header("Startup")]
    public bool openOnStart = false;
    public bool createMenuIfMissing = true;

    [Header("Summon Input")]
    public OVRInput.Button menuToggleButton = OVRInput.Button.Start;
    public bool enableSecondaryToggleButton = false;
    public OVRInput.Button secondaryMenuToggleButton = OVRInput.Button.Two;
    public KeyCode editorToggleKey = KeyCode.M;

    [Header("References")]
    public Camera xrCamera;
    public M_PassthroughModeController passthroughController;
    public M_ServerBootstrap serverBootstrap;
    public M_PhoneImportHeadsetMode phoneImportMode;
    public XRMultiplayer.CharacterResetter characterResetter;
    public XRMultiplayer.PlayerOptions pauseMenuOptions;

    [Header("Pause Menu Docking")]
    [Tooltip("After the launch prompt is dismissed, show this phone pairing UI beside the XR Multiplayer Template pause menu instead of as a separate summoned menu.")]
    public bool attachToPauseMenuAfterDismissal = true;

    [Tooltip("Template pause/menu transform used as the side-by-side anchor. If empty, the PlayerOptions menu is found at runtime, including inactive menu objects.")]
    public Transform pauseMenuRoot;

    [Tooltip("Fallback scene object name used when a PlayerOptions component cannot be found.")]
    public string pauseMenuRootName = "Player_Menu_UI";

    [Tooltip("Suppress this component's own menu-button handling once the phone UI has been dismissed into the template pause menu.")]
    public bool pauseMenuOwnsToggleAfterDismissal = true;

    [Tooltip("Meters to the right of the pause menu where the phone pairing panel appears.")]
    public float dockedHorizontalOffsetMeters = 0.86f;

    [Tooltip("Meters above the pause menu where the phone pairing panel appears.")]
    public float dockedVerticalOffsetMeters = 0f;

    [Tooltip("Meters forward from the pause menu where the phone pairing panel appears.")]
    public float dockedDepthOffsetMeters = 0f;

    [Header("Generated Menu")]
    public GameObject menuRoot;
    public TMP_Text titleText;
    public TMP_Text instructionsText;
    public TMP_Text statusText;
    public TMP_Text passthroughButtonText;
    public Button passthroughButton;
    public Button recenterButton;
    public Button closeButton;

    [Header("Placement")]
    public float menuDistanceMeters = 1.35f;
    public float menuVerticalOffsetMeters = -0.03f;
    public float menuHorizontalOffsetMeters = 0f;
    public bool flattenYawOnly = true;

    [Header("Menu Layout")]
    public Vector2 canvasSize = new Vector2(820f, 560f);
    public float canvasScale = 0.00145f;
    public int sortingOrder = 150;
    public Color panelColor = new Color(0.025f, 0.03f, 0.036f, 0.96f);
    public Color buttonColor = new Color(0.12f, 0.42f, 0.9f, 0.96f);
    public Color recenterButtonColor = new Color(0.16f, 0.48f, 0.34f, 0.96f);
    public Color closeButtonColor = new Color(0.36f, 0.36f, 0.4f, 0.96f);

    [Header("Refresh")]
    public float visibleRefreshIntervalSeconds = 0.5f;
    public int maxDisplayedUrls = 2;

    [Header("Debug")]
    public bool verboseLogging = false;

    private bool _isMenuVisible;
    private bool _createdMenu;
    private bool _dismissedToPauseMenu;
    private bool _hiddenForCurrentPauseSession;
    private bool _pauseMenuWasVisible;
    private float _nextPauseMenuResolveTime;
    private float _nextRefreshTime;
    private M_PassthroughModeController _subscribedPassthroughController;
    private M_ServerBootstrap _subscribedServerBootstrap;

    private void Awake()
    {
        ResolveReferences();
        EnsureMenu();
        SetMenuVisible(openOnStart);
    }

    private void OnEnable()
    {
        ResolveReferences();
        EnsureMenu();
        BindButtons();
        SubscribeEvents();
    }

    private void Start()
    {
        ResolveReferences();
        EnsureMenu();
        BindButtons();
        SubscribeEvents();
        SetMenuVisible(openOnStart);
    }

    private void Update()
    {
        if (ShouldToggleMenu())
            ToggleMenu();

        SyncWithPauseMenuDock();

        if (!_isMenuVisible || Time.unscaledTime < _nextRefreshTime)
            return;

        RefreshMenuText();
        _nextRefreshTime = Time.unscaledTime + Mathf.Max(0.1f, visibleRefreshIntervalSeconds);
    }

    private void OnDisable()
    {
        UnbindButtons();
        UnsubscribeEvents();
    }

    private void OnDestroy()
    {
        UnbindButtons();
        UnsubscribeEvents();

        if (_createdMenu && menuRoot != null)
            Destroy(menuRoot);
    }

    public void ToggleMenu()
    {
        if (IsPauseMenuDockingActive())
            return;

        if (_isMenuVisible)
            DismissMenu(hideForCurrentPauseSession: false);
        else
            SetMenuVisible(true);
    }

    public void OpenMenu()
    {
        SetMenuVisible(true);
    }

    public void CloseMenu()
    {
        DismissMenu(hideForCurrentPauseSession: true);
    }

    public void DismissMenu()
    {
        DismissMenu(hideForCurrentPauseSession: true);
    }

    private void DismissMenu(bool hideForCurrentPauseSession)
    {
        if (attachToPauseMenuAfterDismissal)
        {
            ResolvePauseMenuReferences(force: true);
            bool pauseVisible = IsPauseMenuVisible();
            _dismissedToPauseMenu = true;
            _hiddenForCurrentPauseSession = hideForCurrentPauseSession && pauseVisible;
            _pauseMenuWasVisible = pauseVisible;
        }

        SetMenuVisible(false);
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
        RefreshMenuText();
    }

    public void Recenter()
    {
        ResolveReferences();

        if (characterResetter != null)
        {
            characterResetter.ResetPlayer();
            SetStatus("Player recentered.");
        }
        else
        {
            SetStatus("Menu recentered.");
        }

        if (IsPauseMenuDockingActive() && IsPauseMenuVisible())
        {
            PlaceMenuBesidePauseMenu();
            SetStatus("Menu tethered to pause menu.");
        }
        else
        {
            PlaceMenuInFrontOfUser();
        }
    }

    public void SetMenuVisible(bool visible)
    {
        EnsureMenu();

        _isMenuVisible = visible;

        if (menuRoot != null)
            menuRoot.SetActive(visible);

        if (visible)
        {
            if (IsPauseMenuDockingActive() && !IsPauseMenuVisible())
            {
                _isMenuVisible = false;
                if (menuRoot != null)
                    menuRoot.SetActive(false);

                return;
            }

            if (IsPauseMenuDockingActive() && PlaceMenuBesidePauseMenu())
                _hiddenForCurrentPauseSession = false;
            else
                PlaceMenuInFrontOfUser();

            RefreshMenuText();
            _nextRefreshTime = Time.unscaledTime + Mathf.Max(0.1f, visibleRefreshIntervalSeconds);
        }

        if (verboseLogging)
            Debug.Log($"[M_QuestUserMenu] Menu {(visible ? "opened" : "closed")}.");
    }

    private bool ShouldToggleMenu()
    {
        if (pauseMenuOwnsToggleAfterDismissal && IsPauseMenuDockingActive())
            return false;

        bool editorToggle = IsEditorTogglePressedThisFrame();
        bool primaryToggle = menuToggleButton != OVRInput.Button.None && OVRInput.GetDown(menuToggleButton);
        bool secondaryToggle = enableSecondaryToggleButton &&
                               secondaryMenuToggleButton != OVRInput.Button.None &&
                               OVRInput.GetDown(secondaryMenuToggleButton);

        return editorToggle || primaryToggle || secondaryToggle;
    }

    private bool IsEditorTogglePressedThisFrame()
    {
#if UNITY_EDITOR
        if (editorToggleKey == KeyCode.None)
            return false;

#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && TryGetInputSystemKey(editorToggleKey, out Key inputSystemKey))
            return keyboard[inputSystemKey].wasPressedThisFrame;
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(editorToggleKey);
#else
        return false;
#endif
#else
        return false;
#endif
    }

#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
    private static bool TryGetInputSystemKey(KeyCode keyCode, out Key inputSystemKey)
    {
        switch (keyCode)
        {
            case KeyCode.M:
                inputSystemKey = Key.M;
                return true;
            case KeyCode.Space:
                inputSystemKey = Key.Space;
                return true;
            case KeyCode.Return:
                inputSystemKey = Key.Enter;
                return true;
            case KeyCode.Escape:
                inputSystemKey = Key.Escape;
                return true;
            default:
                inputSystemKey = Key.None;
                return false;
        }
    }
#endif

    private void ResolveReferences()
    {
        if (xrCamera == null)
            xrCamera = Camera.main;

        if (xrCamera == null)
            xrCamera = OVRManager.FindMainCamera();

        if (passthroughController == null)
            passthroughController = UnityEngine.Object.FindObjectOfType<M_PassthroughModeController>();

        if (serverBootstrap == null)
            serverBootstrap = UnityEngine.Object.FindObjectOfType<M_ServerBootstrap>();

        if (phoneImportMode == null)
            phoneImportMode = UnityEngine.Object.FindObjectOfType<M_PhoneImportHeadsetMode>();

        if (characterResetter == null)
            characterResetter = UnityEngine.Object.FindObjectOfType<XRMultiplayer.CharacterResetter>();

        ResolvePauseMenuReferences();
    }

    private bool IsPauseMenuDockingActive()
    {
        return attachToPauseMenuAfterDismissal && _dismissedToPauseMenu;
    }

    private void SyncWithPauseMenuDock()
    {
        if (!IsPauseMenuDockingActive())
            return;

        ResolvePauseMenuReferences();

        bool pauseVisible = IsPauseMenuVisible();
        if (pauseVisible != _pauseMenuWasVisible)
        {
            _pauseMenuWasVisible = pauseVisible;

            if (pauseVisible)
                _hiddenForCurrentPauseSession = false;
        }

        if (!pauseVisible || _hiddenForCurrentPauseSession)
        {
            if (_isMenuVisible || (menuRoot != null && menuRoot.activeSelf))
                SetMenuVisible(false);

            return;
        }

        EnsureMenu();
        if (menuRoot == null)
            return;

        PlaceMenuBesidePauseMenu();

        if (!_isMenuVisible || !menuRoot.activeSelf)
        {
            _isMenuVisible = true;
            menuRoot.SetActive(true);
            RefreshMenuText();
            _nextRefreshTime = Time.unscaledTime + Mathf.Max(0.1f, visibleRefreshIntervalSeconds);
        }
    }

    private bool IsPauseMenuVisible()
    {
        ResolvePauseMenuReferences();
        return pauseMenuRoot != null && pauseMenuRoot.gameObject.activeInHierarchy;
    }

    private void ResolvePauseMenuReferences(bool force = false)
    {
        if (!force && Time.unscaledTime < _nextPauseMenuResolveTime)
            return;

        _nextPauseMenuResolveTime = Time.unscaledTime + 1f;

        if (pauseMenuRoot != null && IsSceneObject(pauseMenuRoot.gameObject))
        {
            if (pauseMenuOptions == null)
                pauseMenuOptions = pauseMenuRoot.GetComponent<XRMultiplayer.PlayerOptions>();

            return;
        }

        if (pauseMenuOptions == null || !IsSceneObject(pauseMenuOptions.gameObject))
            pauseMenuOptions = FindScenePauseMenuOptions();

        if (pauseMenuOptions != null)
        {
            pauseMenuRoot = pauseMenuOptions.transform;
            return;
        }

        pauseMenuRoot = FindSceneTransformByName(pauseMenuRootName);
    }

    private static XRMultiplayer.PlayerOptions FindScenePauseMenuOptions()
    {
        XRMultiplayer.PlayerOptions[] options = Resources.FindObjectsOfTypeAll<XRMultiplayer.PlayerOptions>();
        for (int i = 0; i < options.Length; i++)
        {
            XRMultiplayer.PlayerOptions option = options[i];
            if (option != null && IsSceneObject(option.gameObject))
                return option;
        }

        return null;
    }

    private static Transform FindSceneTransformByName(string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName))
            return null;

        Transform[] transforms = Resources.FindObjectsOfTypeAll<Transform>();
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform candidate = transforms[i];
            if (candidate != null &&
                candidate.name == objectName &&
                IsSceneObject(candidate.gameObject))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool IsSceneObject(GameObject gameObject)
    {
        return gameObject != null &&
               gameObject.scene.IsValid() &&
               !gameObject.hideFlags.HasFlag(HideFlags.HideAndDontSave);
    }

    private void EnsureMenu()
    {
        if (menuRoot != null)
        {
            ConfigureExistingMenu();
            return;
        }

        if (!createMenuIfMissing)
            return;

        CreateGeneratedMenu();
    }

    private void ConfigureExistingMenu()
    {
        SetLayerRecursive(menuRoot, GetUiLayer());

        Canvas[] canvases = menuRoot.GetComponentsInChildren<Canvas>(true);
        for (int i = 0; i < canvases.Length; i++)
            ConfigureCanvasForXRInput(canvases[i]);

        if (passthroughButton == null)
            passthroughButton = FindButtonByName("PassthroughButton");

        if (recenterButton == null)
            recenterButton = FindButtonByName("RecenterButton");

        if (closeButton == null)
            closeButton = FindButtonByName("CloseButton");
    }

    private Button FindButtonByName(string objectName)
    {
        Button[] buttons = menuRoot.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] != null && buttons[i].name == objectName)
                return buttons[i];
        }

        return null;
    }

    private void CreateGeneratedMenu()
    {
        GameObject canvasObject = new GameObject("QuestUserMenu");
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

        Image panel = canvasObject.AddComponent<Image>();
        panel.color = panelColor;
        panel.raycastTarget = true;

        titleText = CreateText(canvasRect, "Title", new Vector2(0f, 218f), new Vector2(740f, 58f), 42f, FontStyles.Bold, TextAlignmentOptions.Center);
        instructionsText = CreateText(canvasRect, "PhoneInstructions", new Vector2(0f, 40f), new Vector2(720f, 280f), 28f, FontStyles.Normal, TextAlignmentOptions.Center);
        statusText = CreateText(canvasRect, "Status", new Vector2(0f, -238f), new Vector2(720f, 42f), 22f, FontStyles.Italic, TextAlignmentOptions.Center);

        passthroughButton = CreateButton(canvasRect, "PassthroughButton", new Vector2(-250f, -166f), new Vector2(230f, 68f), "Passthrough Off", buttonColor);
        passthroughButtonText = passthroughButton.GetComponentInChildren<TMP_Text>(true);
        recenterButton = CreateButton(canvasRect, "RecenterButton", new Vector2(0f, -166f), new Vector2(210f, 68f), "Recenter", recenterButtonColor);
        closeButton = CreateButton(canvasRect, "CloseButton", new Vector2(240f, -166f), new Vector2(190f, 68f), "Close", closeButtonColor);

        menuRoot = canvasObject;
        _createdMenu = true;
        BindButtons();
    }

    private TMP_Text CreateText(RectTransform parent, string objectName, Vector2 position, Vector2 size, float fontSize, FontStyles style, TextAlignmentOptions alignment)
    {
        GameObject textObject = new GameObject(objectName);
        textObject.transform.SetParent(parent, false);
        textObject.layer = parent.gameObject.layer;

        RectTransform rect = textObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        TextMeshProUGUI label = textObject.AddComponent<TextMeshProUGUI>();
        label.text = string.Empty;
        label.alignment = alignment;
        label.enableWordWrapping = true;
        label.fontSize = fontSize;
        label.fontStyle = style;
        label.color = Color.white;
        label.margin = new Vector4(10f, 4f, 10f, 4f);
        label.raycastTarget = false;
        return label;
    }

    private Button CreateButton(RectTransform parent, string objectName, Vector2 position, Vector2 size, string label, Color color)
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
        image.color = color;
        image.raycastTarget = true;

        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;

        ColorBlock colors = button.colors;
        colors.normalColor = color;
        colors.highlightedColor = new Color(Mathf.Min(color.r + 0.08f, 1f), Mathf.Min(color.g + 0.08f, 1f), Mathf.Min(color.b + 0.08f, 1f), color.a);
        colors.pressedColor = new Color(color.r * 0.75f, color.g * 0.75f, color.b * 0.75f, color.a);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = new Color(color.r, color.g, color.b, 0.35f);
        button.colors = colors;

        TMP_Text text = CreateText(rect, "Label", Vector2.zero, size - new Vector2(28f, 12f), 24f, FontStyles.Bold, TextAlignmentOptions.Center);
        text.text = label;
        text.enableWordWrapping = false;
        return button;
    }

    private void BindButtons()
    {
        if (passthroughButton != null)
        {
            passthroughButton.onClick.RemoveListener(TogglePassthrough);
            passthroughButton.onClick.AddListener(TogglePassthrough);
        }

        if (recenterButton != null)
        {
            recenterButton.onClick.RemoveListener(Recenter);
            recenterButton.onClick.AddListener(Recenter);
        }

        if (closeButton != null)
        {
            closeButton.onClick.RemoveListener(CloseMenu);
            closeButton.onClick.AddListener(CloseMenu);
        }
    }

    private void UnbindButtons()
    {
        if (passthroughButton != null)
            passthroughButton.onClick.RemoveListener(TogglePassthrough);

        if (recenterButton != null)
            recenterButton.onClick.RemoveListener(Recenter);

        if (closeButton != null)
            closeButton.onClick.RemoveListener(CloseMenu);
    }

    private void SubscribeEvents()
    {
        if (_subscribedPassthroughController != passthroughController)
        {
            if (_subscribedPassthroughController != null)
                _subscribedPassthroughController.PassthroughChanged -= HandlePassthroughChanged;

            _subscribedPassthroughController = passthroughController;

            if (_subscribedPassthroughController != null)
                _subscribedPassthroughController.PassthroughChanged += HandlePassthroughChanged;
        }

        if (_subscribedServerBootstrap != serverBootstrap)
        {
            if (_subscribedServerBootstrap != null)
                _subscribedServerBootstrap.InstructionsPublished -= HandleInstructionsPublished;

            _subscribedServerBootstrap = serverBootstrap;

            if (_subscribedServerBootstrap != null)
                _subscribedServerBootstrap.InstructionsPublished += HandleInstructionsPublished;
        }
    }

    private void UnsubscribeEvents()
    {
        if (_subscribedPassthroughController != null)
        {
            _subscribedPassthroughController.PassthroughChanged -= HandlePassthroughChanged;
            _subscribedPassthroughController = null;
        }

        if (_subscribedServerBootstrap != null)
        {
            _subscribedServerBootstrap.InstructionsPublished -= HandleInstructionsPublished;
            _subscribedServerBootstrap = null;
        }
    }

    private void HandlePassthroughChanged(bool enabled)
    {
        RefreshPassthroughLabel();
        if (_isMenuVisible)
            SetStatus(enabled ? "Passthrough is on." : "Passthrough is off.");
    }

    private void HandleInstructionsPublished(M_ServerBootstrap bootstrap)
    {
        serverBootstrap = bootstrap;
        if (_isMenuVisible)
            RefreshMenuText();
    }

    private void RefreshMenuText()
    {
        ResolveReferences();
        SubscribeEvents();

        if (titleText != null)
            titleText.text = "Quest Menu";

        if (instructionsText != null)
            instructionsText.text = BuildPhonePairingInstructions();

        RefreshPassthroughLabel();

        if (statusText != null && string.IsNullOrWhiteSpace(statusText.text))
            SetStatus("Ready.");
    }

    private void RefreshPassthroughLabel()
    {
        if (passthroughButtonText == null && passthroughButton != null)
            passthroughButtonText = passthroughButton.GetComponentInChildren<TMP_Text>(true);

        if (passthroughButtonText == null)
            return;

        bool passthroughEnabled = passthroughController != null && passthroughController.IsPassthroughEnabled;
        passthroughButtonText.text = passthroughEnabled ? "Passthrough On" : "Passthrough Off";
    }

    private string BuildPhonePairingInstructions()
    {
        if (phoneImportMode != null)
            return phoneImportMode.GetPhonePairingInstructions();

        ResolveReferences();

        if (serverBootstrap == null)
        {
            return "Phone pairing\n\n" +
                   "Starting upload server.\n" +
                   "Keep the phone and headset on the same Wi-Fi network.";
        }

        string[] urls = serverBootstrap.PublishedPhoneUrls;
        string code = serverBootstrap.EffectivePairingCode;

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("Phone pairing");
        sb.AppendLine();
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
        sb.Append("Upload from the phone browser. The newest photo appears in the room display.");
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

    private void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;
    }

    private void PlaceMenuInFrontOfUser()
    {
        if (menuRoot == null)
            return;

        ResolveReferences();
        if (xrCamera == null)
            return;

        Vector3 forward = xrCamera.transform.forward;
        if (flattenYawOnly)
            forward.y = 0f;

        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;

        forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 position = xrCamera.transform.position +
                           forward * Mathf.Max(0.5f, menuDistanceMeters) +
                           right * menuHorizontalOffsetMeters +
                           Vector3.up * menuVerticalOffsetMeters;

        menuRoot.transform.position = position;
        menuRoot.transform.rotation = Quaternion.LookRotation(position - xrCamera.transform.position, Vector3.up);
    }

    private bool PlaceMenuBesidePauseMenu()
    {
        if (menuRoot == null)
            return false;

        ResolvePauseMenuReferences(force: pauseMenuRoot == null);
        if (pauseMenuRoot == null)
            return false;

        Vector3 position = pauseMenuRoot.position +
                           pauseMenuRoot.right * dockedHorizontalOffsetMeters +
                           pauseMenuRoot.up * dockedVerticalOffsetMeters +
                           pauseMenuRoot.forward * dockedDepthOffsetMeters;

        menuRoot.transform.position = position;
        menuRoot.transform.rotation = pauseMenuRoot.rotation;
        return true;
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
