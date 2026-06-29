/*
 * Script Name: M_PairingUiPresenter.cs
 * Author: Mason Prather
 * Description: Displays phone-mirroring pairing information, including host address, signaling port, and pairing code.
 * Project Role: Headset UI presenter for the local phone mirror connection path.
 * Key Inputs: M_QuestSignalingHostTcp state, M_PairingCodeProvider code, and configured TMP labels.
 * Key Outputs: Updated pairing text and connection status labels.
 */

using TMPro;
using UnityEngine;

public class M_PairingUiPresenter : MonoBehaviour
{
    public M_PairingCodeProvider codeProvider;
    public M_QuestSignalingHostTcp signalingHost;

    public TMP_Text pairCodeText;
    public TMP_Text statusText;
    public TMP_Text networkHintText;

    [Header("Runtime UI Controls")]
    [Tooltip("Visual root that should be shown/hidden by the generated pairing toggle. If empty, the root panel containing the pairing labels is used.")]
    public GameObject pairingUiRoot;

    [Tooltip("Canvas that receives generated toggle/recenter buttons. If empty, the Canvas containing the pairing labels is used.")]
    public Canvas pairingUiCanvas;

    public Camera xrCamera;
    public bool createRuntimeToggleButton = false;
    public bool createRuntimeRecenterButton = false;
    public M_PhonePairingUiToggle pairingUiToggle;
    public M_WorldSpaceUiRecenter pairingUiRecenter;

    private void Awake()
    {
        ResolveRuntimeUiReferences();
        EnsureRuntimeControls();
    }

    private void OnEnable()
    {
        ResolveRuntimeUiReferences();
        EnsureRuntimeControls();
        Refresh();

        if (codeProvider != null)
            codeProvider.OnCodeChanged += OnCodeChanged;

        if (signalingHost != null)
        {
            signalingHost.OnClientConnected += OnClientConnected;
            signalingHost.OnClientDisconnected += OnClientDisconnected;
            signalingHost.OnClientRejected += OnClientRejected;
        }
    }

    private void OnDisable()
    {
        if (codeProvider != null)
            codeProvider.OnCodeChanged -= OnCodeChanged;

        if (signalingHost != null)
        {
            signalingHost.OnClientConnected -= OnClientConnected;
            signalingHost.OnClientDisconnected -= OnClientDisconnected;
            signalingHost.OnClientRejected -= OnClientRejected;
        }
    }

    private void Refresh()
    {
        ResolveRuntimeUiReferences();
        EnsureRuntimeControls();

        if (pairCodeText != null && codeProvider != null)
            pairCodeText.text = codeProvider.PairingCode;

        if (networkHintText != null && signalingHost != null)
            networkHintText.text = $"Signaling: {signalingHost.HostIp}:{signalingHost.port}";

        SetStatus("Waiting for iOS client...");
    }

    private void OnCodeChanged(string code)
    {
        if (pairCodeText != null) pairCodeText.text = code;
    }

    private void OnClientConnected() => SetStatus("Paired (HELLO/ACK). Waiting for WebRTC...");
    private void OnClientDisconnected() => SetStatus("Disconnected. Waiting...");
    private void OnClientRejected(string reason) => SetStatus($"Rejected: {reason}");

    private void SetStatus(string s)
    {
        if (statusText != null) statusText.text = s;
    }

    private void ResolveRuntimeUiReferences()
    {
        TMP_Text anchorText = pairCodeText != null ? pairCodeText : statusText != null ? statusText : networkHintText;
        if (anchorText == null)
            return;

        if (pairingUiCanvas == null)
            pairingUiCanvas = anchorText.GetComponentInParent<Canvas>(true);

        if (pairingUiRoot == null && pairingUiCanvas != null)
        {
            Transform root = anchorText.transform;
            while (root.parent != null && root.parent != pairingUiCanvas.transform)
                root = root.parent;

            pairingUiRoot = root.gameObject;
        }

        if (xrCamera == null && pairingUiCanvas != null && pairingUiCanvas.worldCamera != null)
            xrCamera = pairingUiCanvas.worldCamera;

        if (xrCamera == null)
            xrCamera = Camera.main;

        if (xrCamera == null)
            xrCamera = OVRManager.FindMainCamera();
    }

    private void EnsureRuntimeControls()
    {
        if (pairingUiRoot == null && pairingUiCanvas == null)
            return;

        RectTransform buttonParent = pairingUiCanvas != null ? pairingUiCanvas.transform as RectTransform : null;

        if (createRuntimeToggleButton)
        {
            if (pairingUiToggle == null)
                pairingUiToggle = GetComponent<M_PhonePairingUiToggle>();

            if (pairingUiToggle == null)
                pairingUiToggle = gameObject.AddComponent<M_PhonePairingUiToggle>();

            pairingUiToggle.pairingUiRoot = pairingUiRoot;
            pairingUiToggle.phoneImportMode = null;
            pairingUiToggle.xrCamera = xrCamera;
            pairingUiToggle.generatedButtonParent = buttonParent;
            pairingUiToggle.applyStartVisibleOnStart = false;
            pairingUiToggle.startVisible = pairingUiRoot == null || pairingUiRoot.activeSelf;
            pairingUiToggle.createButtonIfMissing = true;
            pairingUiToggle.RefreshBinding();
        }

        if (createRuntimeRecenterButton && pairingUiCanvas != null)
        {
            if (pairingUiRecenter == null)
                pairingUiRecenter = GetComponent<M_WorldSpaceUiRecenter>();

            if (pairingUiRecenter == null)
                pairingUiRecenter = gameObject.AddComponent<M_WorldSpaceUiRecenter>();

            pairingUiRecenter.uiRoot = pairingUiCanvas.transform;
            pairingUiRecenter.headOrCameraTransform = xrCamera != null ? xrCamera.transform : null;
            pairingUiRecenter.generatedButtonParent = buttonParent;
            pairingUiRecenter.createButtonIfMissing = true;
            pairingUiRecenter.recenterOnStart = false;
            pairingUiRecenter.RefreshBinding();
        }
    }
}
