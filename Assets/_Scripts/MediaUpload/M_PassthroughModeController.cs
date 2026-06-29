/*
 * Script Name: M_PassthroughModeController.cs
 * Author: Mason Prather
 * Description: Controls Quest passthrough state for pairing and media-import workflows, including OVR passthrough layer state, camera clear state, hidden scene objects, and UI toggle synchronization.
 * Project Role: Runtime passthrough coordinator for headset-on phone import and review workflows.
 * Key Inputs: OVRManager, OVRPassthroughLayer, XR camera, optional hidden scene objects, and UI toggle state.
 * Key Outputs: Updated passthrough visibility, camera clear settings, scene object visibility, and PassthroughChanged events.
 */

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Small runtime wrapper for Quest passthrough so UI flows can toggle it without
/// depending on scene-specific OVR wiring.
/// </summary>
[DisallowMultipleComponent]
public class M_PassthroughModeController : MonoBehaviour
{
    public event Action<bool> PassthroughChanged;

    [Header("Behavior")]
    [Tooltip("If true, passthrough is enabled when this component starts.")]
    public bool enableOnStart = false;

    [Tooltip("If true, an OVRPassthroughLayer is created when the scene does not already have one.")]
    public bool createLayerIfMissing = true;

    [Tooltip("If true, add a hand-disabling companion when this scene forgot to include one. Passthrough should never show tracked XR hands.")]
    public bool createHandVisualControllerIfMissing = true;

    [Tooltip("If true, the main camera is made transparent while passthrough is active.")]
    public bool makeCameraTransparentWhileEnabled = true;

    [Tooltip("Objects hidden while passthrough is active, such as opaque room geometry.")]
    public GameObject[] objectsHiddenWhilePassthrough;

    [Tooltip("If true, opaque scene MeshRenderer/SkinnedMeshRenderer components are hidden while passthrough is active so the underlay is actually visible.")]
    public bool hideSceneRenderersWhileEnabled = true;

    [Tooltip("If true, only MeshRenderer and SkinnedMeshRenderer components are hidden. Controller rays, line renderers, particles, and UI remain available.")]
    public bool hideMeshAndSkinnedRenderersOnly = true;

    [Tooltip("If true, renderers below world-space Canvas roots remain visible during passthrough.")]
    public bool keepCanvasRenderersVisible = true;

    [Tooltip("Additional roots that should remain visible while passthrough hides scene geometry.")]
    public GameObject[] objectsKeptVisibleDuringPassthrough;

    [Header("References")]
    public OVRManager ovrManager;
    public OVRPassthroughLayer passthroughLayer;
    public Camera xrCamera;
    public Toggle passthroughToggle;
    public M_PassthroughHandVisualController handVisualController;

    [Header("Debug")]
    public bool verboseLogging = false;

    public bool IsPassthroughEnabled { get; private set; }

    private bool _cameraStateCaptured;
    private CameraClearFlags _previousClearFlags;
    private Color _previousBackgroundColor;
    private bool _updatingToggle;
    private readonly Dictionary<GameObject, bool> _hiddenObjectActiveStates = new Dictionary<GameObject, bool>();
    private readonly Dictionary<Renderer, bool> _hiddenRendererStates = new Dictionary<Renderer, bool>();
    private bool _hiddenObjectStatesCaptured;
    private bool _hiddenRendererStatesCaptured;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        BindToggle(passthroughToggle);
    }

    private void Start()
    {
        SetPassthroughEnabled(enableOnStart);
    }

    private void OnDisable()
    {
        if (passthroughToggle != null)
            passthroughToggle.onValueChanged.RemoveListener(SetPassthroughEnabled);
    }

    public void BindToggle(Toggle toggle)
    {
        if (passthroughToggle != null)
            passthroughToggle.onValueChanged.RemoveListener(SetPassthroughEnabled);

        passthroughToggle = toggle;

        if (passthroughToggle != null)
        {
            passthroughToggle.onValueChanged.RemoveListener(SetPassthroughEnabled);
            passthroughToggle.onValueChanged.AddListener(SetPassthroughEnabled);
        }

        SyncToggleState();
    }

    public void TogglePassthrough()
    {
        SetPassthroughEnabled(!IsPassthroughEnabled);
    }

    public void SetPassthroughEnabled(bool enabled)
    {
        ResolveReferences();

        if (enabled && ovrManager == null)
        {
            Debug.LogWarning("[M_PassthroughModeController] Cannot enable passthrough because no OVRManager was found.");
            SyncToggleState();
            return;
        }

        if (enabled && !IsPassthroughSupportedOnThisRuntime())
        {
            Debug.LogWarning("[M_PassthroughModeController] Cannot enable passthrough because this runtime does not report Insight Passthrough support.");
            SyncToggleState();
            return;
        }

        if (enabled && passthroughLayer == null)
        {
            Debug.LogWarning("[M_PassthroughModeController] Cannot enable passthrough because no OVRPassthroughLayer was found or created.");
            SyncToggleState();
            return;
        }

        IsPassthroughEnabled = enabled;

        if (ovrManager != null)
            ovrManager.isInsightPassthroughEnabled = enabled;

        if (passthroughLayer != null)
        {
            passthroughLayer.overlayType = OVROverlay.OverlayType.Underlay;
            passthroughLayer.textureOpacity = enabled ? 1f : 0f;
            passthroughLayer.hidden = !enabled;
            passthroughLayer.enabled = true;
        }

        SetCameraTransparent(enabled);
        SetObjectsHiddenForPassthrough(enabled);
        SetSceneRenderersHiddenForPassthrough(enabled);
        SetHandVisualsForPassthrough(enabled);
        SyncToggleState();

        if (verboseLogging)
            Debug.Log($"[M_PassthroughModeController] Passthrough {(enabled ? "enabled" : "disabled")}.");

        PassthroughChanged?.Invoke(enabled);
    }

    private void ResolveReferences()
    {
        if (ovrManager == null)
            ovrManager = UnityEngine.Object.FindObjectOfType<OVRManager>();

        if (passthroughLayer == null)
            passthroughLayer = UnityEngine.Object.FindObjectOfType<OVRPassthroughLayer>();

        if (passthroughLayer == null && createLayerIfMissing)
        {
            GameObject layerObject = new GameObject("Runtime Passthrough Layer");
            passthroughLayer = layerObject.AddComponent<OVRPassthroughLayer>();
            passthroughLayer.overlayType = OVROverlay.OverlayType.Underlay;
            passthroughLayer.textureOpacity = 0f;
            passthroughLayer.hidden = true;
        }

        if (handVisualController == null)
            handVisualController = UnityEngine.Object.FindObjectOfType<M_PassthroughHandVisualController>();

        if (handVisualController == null && createHandVisualControllerIfMissing)
            handVisualController = gameObject.AddComponent<M_PassthroughHandVisualController>();

        if (xrCamera == null)
        {
            xrCamera = Camera.main;

            if (xrCamera == null)
                xrCamera = OVRManager.FindMainCamera();
        }
    }

    private static bool IsPassthroughSupportedOnThisRuntime()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return OVRManager.IsInsightPassthroughSupported();
#else
        return true;
#endif
    }

    private void SetCameraTransparent(bool transparent)
    {
        if (!makeCameraTransparentWhileEnabled || xrCamera == null)
            return;

        if (transparent)
        {
            if (!_cameraStateCaptured)
            {
                _previousClearFlags = xrCamera.clearFlags;
                _previousBackgroundColor = xrCamera.backgroundColor;
                _cameraStateCaptured = true;
            }

            xrCamera.clearFlags = CameraClearFlags.SolidColor;
            xrCamera.backgroundColor = Color.clear;
            return;
        }

        if (!_cameraStateCaptured)
            return;

        xrCamera.clearFlags = _previousClearFlags;
        xrCamera.backgroundColor = _previousBackgroundColor;
        _cameraStateCaptured = false;
    }

    private void SetObjectsHiddenForPassthrough(bool passthroughEnabled)
    {
        if (objectsHiddenWhilePassthrough == null)
            return;

        if (passthroughEnabled)
        {
            if (!_hiddenObjectStatesCaptured)
            {
                _hiddenObjectActiveStates.Clear();
                for (int i = 0; i < objectsHiddenWhilePassthrough.Length; i++)
                {
                    GameObject target = objectsHiddenWhilePassthrough[i];
                    if (target != null && !_hiddenObjectActiveStates.ContainsKey(target))
                        _hiddenObjectActiveStates.Add(target, target.activeSelf);
                }

                _hiddenObjectStatesCaptured = true;
            }

            for (int i = 0; i < objectsHiddenWhilePassthrough.Length; i++)
            {
                if (objectsHiddenWhilePassthrough[i] != null)
                    objectsHiddenWhilePassthrough[i].SetActive(false);
            }

            return;
        }

        if (!_hiddenObjectStatesCaptured)
            return;

        foreach (KeyValuePair<GameObject, bool> entry in _hiddenObjectActiveStates)
        {
            if (entry.Key != null)
                entry.Key.SetActive(entry.Value);
        }

        _hiddenObjectActiveStates.Clear();
        _hiddenObjectStatesCaptured = false;
    }

    private void SetSceneRenderersHiddenForPassthrough(bool passthroughEnabled)
    {
        if (!hideSceneRenderersWhileEnabled)
            return;

        if (passthroughEnabled)
        {
            if (!_hiddenRendererStatesCaptured)
            {
                _hiddenRendererStates.Clear();
                Renderer[] renderers = UnityEngine.Object.FindObjectsOfType<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer renderer = renderers[i];
                    if (renderer == null || !ShouldHideRendererForPassthrough(renderer))
                        continue;

                    if (!_hiddenRendererStates.ContainsKey(renderer))
                        _hiddenRendererStates.Add(renderer, renderer.enabled);
                }

                _hiddenRendererStatesCaptured = true;
            }

            foreach (KeyValuePair<Renderer, bool> entry in _hiddenRendererStates)
            {
                if (entry.Key != null)
                    entry.Key.enabled = false;
            }

            return;
        }

        if (!_hiddenRendererStatesCaptured)
            return;

        foreach (KeyValuePair<Renderer, bool> entry in _hiddenRendererStates)
        {
            if (entry.Key != null)
                entry.Key.enabled = entry.Value;
        }

        _hiddenRendererStates.Clear();
        _hiddenRendererStatesCaptured = false;
    }

    private bool ShouldHideRendererForPassthrough(Renderer renderer)
    {
        if (renderer == null)
            return false;

        Transform rendererTransform = renderer.transform;

        if (keepCanvasRenderersVisible && renderer.GetComponentInParent<Canvas>(true) != null)
            return false;

        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer >= 0 && renderer.gameObject.layer == uiLayer)
            return false;

        if (passthroughLayer != null && rendererTransform.IsChildOf(passthroughLayer.transform))
            return false;

        if (ovrManager != null && rendererTransform.IsChildOf(ovrManager.transform))
            return false;

        if (objectsKeptVisibleDuringPassthrough != null)
        {
            for (int i = 0; i < objectsKeptVisibleDuringPassthrough.Length; i++)
            {
                GameObject keepRoot = objectsKeptVisibleDuringPassthrough[i];
                if (keepRoot != null && rendererTransform.IsChildOf(keepRoot.transform))
                    return false;
            }
        }

        if (hideMeshAndSkinnedRenderersOnly)
            return renderer is MeshRenderer || renderer is SkinnedMeshRenderer;

        return !(renderer is LineRenderer) && !(renderer is TrailRenderer);
    }

    private void SetHandVisualsForPassthrough(bool passthroughEnabled)
    {
        if (handVisualController == null)
            return;

        handVisualController.SetPassthroughActive(passthroughEnabled);
    }

    private void SyncToggleState()
    {
        if (passthroughToggle == null || _updatingToggle)
            return;

        _updatingToggle = true;
        passthroughToggle.SetIsOnWithoutNotify(IsPassthroughEnabled);
        _updatingToggle = false;
    }
}
