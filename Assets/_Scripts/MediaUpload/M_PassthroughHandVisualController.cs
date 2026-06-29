/*
 * Script Name: M_PassthroughHandVisualController.cs
 * Author: Mason Prather
 * Description: Disables XR hand roots and hides hand/controller meshes while Quest passthrough is active, then restores previous states afterward.
 * Project Role: Passthrough companion that guarantees tracked hands are not present in the passthrough view.
 * Key Inputs: XRI modality hand roots, XR hand skeleton drivers, assigned visual roots/renderers/behaviours, and optional automatic discovery.
 * Key Outputs: XR hand GameObject, Behaviour, and Renderer state changes synchronized with passthrough state.
 */

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Inputs;

[DisallowMultipleComponent]
public class M_PassthroughHandVisualController : MonoBehaviour
{
    [Header("Assigned Visuals")]
    public GameObject[] handVisualRoots;
    public Renderer[] handRenderers;
    public SkinnedMeshRenderer[] handSkinnedMeshRenderers;
    public Behaviour[] visualOnlyBehaviours;

    [Header("Full XR Hand Disable")]
    [Tooltip("Critical passthrough behavior: deactivate the full left/right XR hand roots while passthrough is active so hands are not present at all.")]
    public bool disableFullXrHandsWhenPassthroughEnabled = true;

    [Tooltip("Explicit full hand roots to deactivate while passthrough is active. Use this for custom hand rigs not referenced by XRInputModalityManager.")]
    public GameObject[] xrHandRoots;

    [Tooltip("Optional modality managers. Their leftHand/rightHand roots are auto-added to xrHandRoots.")]
    public XRInputModalityManager[] xrInputModalityManagers;

    [Tooltip("Optional XR hand skeleton drivers to disable while passthrough is active.")]
    public XRHandSkeletonDriver[] xrHandSkeletonDrivers;

    [Tooltip("Additional hand-tracking-only behaviours to disable while passthrough is active.")]
    public Behaviour[] handTrackingBehaviours;

    [Header("Behavior")]
    public bool hideWhenPassthroughEnabled = true;
    public bool autoFindHandRenderers = true;
    public bool autoFindFullXrHands = true;
    public bool refreshAutoFindOnEachToggle = true;
    public bool keepHandsDisabledWhilePassthroughEnabled = true;
    public Transform autoSearchRoot;

    [Tooltip("Renderer or parent names containing any of these tokens are treated as hand visuals.")]
    public string[] handVisualNameContains =
    {
        "OVRLeftHandVisual",
        "OVRRightHandVisual",
        "OVRHandPrefab",
        "HandVisual",
        "handMeshNode"
    };

    [Tooltip("Also hide controller model visuals when passthrough is active.")]
    public bool includeControllerVisuals = false;

    public string[] controllerVisualNameContains =
    {
        "OVRLeftControllerVisual",
        "OVRRightControllerVisual",
        "OVRControllerPrefab",
        "ControllerVisual"
    };

    [Header("Debug")]
    public bool verboseLogging = false;

    private readonly Dictionary<GameObject, bool> _rootStates = new Dictionary<GameObject, bool>();
    private readonly Dictionary<GameObject, bool> _xrHandRootStates = new Dictionary<GameObject, bool>();
    private readonly Dictionary<Renderer, bool> _rendererStates = new Dictionary<Renderer, bool>();
    private readonly Dictionary<Behaviour, bool> _behaviourStates = new Dictionary<Behaviour, bool>();
    private readonly Dictionary<Behaviour, bool> _handTrackingBehaviourStates = new Dictionary<Behaviour, bool>();
    private bool _passthroughActive;

    private void Awake()
    {
        RefreshAutoFoundTargets();
        CacheOriginalStates();
    }

    private void OnDisable()
    {
        if (_passthroughActive)
            SetPassthroughActive(false);
    }

    private void LateUpdate()
    {
        if (_passthroughActive && keepHandsDisabledWhilePassthroughEnabled)
            ForceXrHandsInactive();
    }

    public void SetPassthroughActive(bool active)
    {
        if (refreshAutoFindOnEachToggle)
            RefreshAutoFoundTargets();

        _passthroughActive = active;
        CacheOriginalStates();

        bool shouldHide = hideWhenPassthroughEnabled ? active : !active;
        bool shouldDisableXrHands = disableFullXrHandsWhenPassthroughEnabled && active;
        ApplyXrHandDisable(shouldDisableXrHands);
        ApplyVisibility(shouldHide);

        if (verboseLogging)
        {
            Debug.Log($"[M_PassthroughHandVisualController] Hand visuals {(shouldHide ? "hidden" : "restored")}; XR hands {(shouldDisableXrHands ? "disabled" : "restored")} for passthrough={active}.");
        }
    }

    public void RecacheOriginalStates()
    {
        _rootStates.Clear();
        _xrHandRootStates.Clear();
        _rendererStates.Clear();
        _behaviourStates.Clear();
        _handTrackingBehaviourStates.Clear();
        CacheOriginalStates();
    }

    private void RefreshAutoFoundTargets()
    {
        RefreshAutoFoundXrHands();
        RefreshAutoFoundVisuals();
    }

    private void RefreshAutoFoundXrHands()
    {
        if (!autoFindFullXrHands)
            return;

        List<GameObject> roots = new List<GameObject>();
        AddGameObjectRange(roots, xrHandRoots);

        List<XRInputModalityManager> modalityManagers = new List<XRInputModalityManager>();
        if (xrInputModalityManagers != null)
            modalityManagers.AddRange(xrInputModalityManagers);

        XRInputModalityManager[] discoveredManagers = UnityEngine.Object.FindObjectsOfType<XRInputModalityManager>(true);
        for (int i = 0; i < discoveredManagers.Length; i++)
        {
            if (discoveredManagers[i] != null && !modalityManagers.Contains(discoveredManagers[i]))
                modalityManagers.Add(discoveredManagers[i]);
        }

        for (int i = 0; i < modalityManagers.Count; i++)
        {
            XRInputModalityManager manager = modalityManagers[i];
            if (manager == null)
                continue;

            AddGameObject(roots, manager.leftHand);
            AddGameObject(roots, manager.rightHand);
        }

        xrInputModalityManagers = modalityManagers.ToArray();

        List<XRHandSkeletonDriver> skeletonDrivers = new List<XRHandSkeletonDriver>();
        if (xrHandSkeletonDrivers != null)
            skeletonDrivers.AddRange(xrHandSkeletonDrivers);

        XRHandSkeletonDriver[] discoveredSkeletons = UnityEngine.Object.FindObjectsOfType<XRHandSkeletonDriver>(true);
        for (int i = 0; i < discoveredSkeletons.Length; i++)
        {
            XRHandSkeletonDriver driver = discoveredSkeletons[i];
            if (driver == null)
                continue;

            if (autoSearchRoot != null && !driver.transform.IsChildOf(autoSearchRoot))
                continue;

            if (!skeletonDrivers.Contains(driver))
                skeletonDrivers.Add(driver);

            AddGameObject(roots, FindHandRootForSkeleton(driver));
        }

        xrHandSkeletonDrivers = skeletonDrivers.ToArray();

        List<Behaviour> trackingBehaviours = new List<Behaviour>();
        if (handTrackingBehaviours != null)
            trackingBehaviours.AddRange(handTrackingBehaviours);

        for (int i = 0; i < skeletonDrivers.Count; i++)
        {
            if (skeletonDrivers[i] != null && !trackingBehaviours.Contains(skeletonDrivers[i]))
                trackingBehaviours.Add(skeletonDrivers[i]);
        }

        handTrackingBehaviours = trackingBehaviours.ToArray();
        xrHandRoots = roots.ToArray();
    }

    private void RefreshAutoFoundVisuals()
    {
        if (!autoFindHandRenderers)
            return;

        List<Renderer> renderers = new List<Renderer>();
        if (handRenderers != null)
            renderers.AddRange(handRenderers);

        if (handSkinnedMeshRenderers != null)
        {
            for (int i = 0; i < handSkinnedMeshRenderers.Length; i++)
            {
                if (handSkinnedMeshRenderers[i] != null && !renderers.Contains(handSkinnedMeshRenderers[i]))
                    renderers.Add(handSkinnedMeshRenderers[i]);
            }
        }

        Renderer[] sceneRenderers = UnityEngine.Object.FindObjectsOfType<Renderer>(true);
        for (int i = 0; i < sceneRenderers.Length; i++)
        {
            Renderer renderer = sceneRenderers[i];
            if (renderer == null || renderers.Contains(renderer))
                continue;

            if (autoSearchRoot != null && !renderer.transform.IsChildOf(autoSearchRoot))
                continue;

            if (MatchesAnyNameToken(renderer.transform, handVisualNameContains) ||
                (includeControllerVisuals && MatchesAnyNameToken(renderer.transform, controllerVisualNameContains)))
            {
                renderers.Add(renderer);
            }
        }

        handRenderers = renderers.ToArray();
    }

    private void CacheOriginalStates()
    {
        CacheRootStates();
        CacheXrHandRootStates();
        CacheRendererStates(handRenderers);
        CacheRendererStates(handSkinnedMeshRenderers);
        CacheBehaviourStates();
        CacheHandTrackingBehaviourStates();
    }

    private void CacheRootStates()
    {
        if (handVisualRoots == null)
            return;

        for (int i = 0; i < handVisualRoots.Length; i++)
        {
            GameObject root = handVisualRoots[i];
            if (root != null && !_rootStates.ContainsKey(root))
                _rootStates.Add(root, root.activeSelf);
        }
    }

    private void CacheRendererStates(Renderer[] renderers)
    {
        if (renderers == null)
            return;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer != null && !_rendererStates.ContainsKey(renderer))
                _rendererStates.Add(renderer, renderer.enabled);
        }
    }

    private void CacheBehaviourStates()
    {
        if (visualOnlyBehaviours == null)
            return;

        for (int i = 0; i < visualOnlyBehaviours.Length; i++)
        {
            Behaviour behaviour = visualOnlyBehaviours[i];
            if (behaviour != null && !_behaviourStates.ContainsKey(behaviour))
                _behaviourStates.Add(behaviour, behaviour.enabled);
        }
    }

    private void CacheXrHandRootStates()
    {
        if (xrHandRoots == null)
            return;

        for (int i = 0; i < xrHandRoots.Length; i++)
        {
            GameObject root = xrHandRoots[i];
            if (root != null && !_xrHandRootStates.ContainsKey(root))
                _xrHandRootStates.Add(root, root.activeSelf);
        }
    }

    private void CacheHandTrackingBehaviourStates()
    {
        if (handTrackingBehaviours == null)
            return;

        for (int i = 0; i < handTrackingBehaviours.Length; i++)
        {
            Behaviour behaviour = handTrackingBehaviours[i];
            if (behaviour != null && !_handTrackingBehaviourStates.ContainsKey(behaviour))
                _handTrackingBehaviourStates.Add(behaviour, behaviour.enabled);
        }
    }

    private void ApplyXrHandDisable(bool disabled)
    {
        if (!disableFullXrHandsWhenPassthroughEnabled)
            return;

        if (disabled)
        {
            ForceXrHandsInactive();
            return;
        }

        foreach (KeyValuePair<Behaviour, bool> entry in _handTrackingBehaviourStates)
        {
            if (entry.Key != null)
                entry.Key.enabled = entry.Value;
        }

        foreach (KeyValuePair<GameObject, bool> entry in _xrHandRootStates)
        {
            if (entry.Key != null)
                entry.Key.SetActive(entry.Value);
        }
    }

    private void ForceXrHandsInactive()
    {
        // Disable behaviours first so skeleton drivers/interactors cannot immediately repaint a hand pose in the same frame.
        foreach (KeyValuePair<Behaviour, bool> entry in _handTrackingBehaviourStates)
        {
            if (entry.Key != null)
                entry.Key.enabled = false;
        }

        foreach (KeyValuePair<GameObject, bool> entry in _xrHandRootStates)
        {
            if (entry.Key != null)
                entry.Key.SetActive(false);
        }
    }

    private void ApplyVisibility(bool hidden)
    {
        foreach (KeyValuePair<GameObject, bool> entry in _rootStates)
        {
            if (entry.Key != null)
                entry.Key.SetActive(hidden ? false : entry.Value);
        }

        foreach (KeyValuePair<Renderer, bool> entry in _rendererStates)
        {
            if (entry.Key != null)
                entry.Key.enabled = hidden ? false : entry.Value;
        }

        foreach (KeyValuePair<Behaviour, bool> entry in _behaviourStates)
        {
            if (entry.Key != null)
                entry.Key.enabled = hidden ? false : entry.Value;
        }
    }

    private static bool MatchesAnyNameToken(Transform transform, string[] tokens)
    {
        if (transform == null || tokens == null || tokens.Length == 0)
            return false;

        Transform current = transform;
        while (current != null)
        {
            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i];
                if (!string.IsNullOrWhiteSpace(token) &&
                    current.name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            current = current.parent;
        }

        return false;
    }

    private static GameObject FindHandRootForSkeleton(XRHandSkeletonDriver driver)
    {
        if (driver == null)
            return null;

        Transform root = driver.rootTransform != null ? driver.rootTransform : driver.transform;

        // If the skeleton is inside an XRI modality hand group, deactivate that whole group. This removes hand rays,
        // direct/poke interactors, wrist menus, and the visual skeleton together so no XR hand remains in passthrough.
        XRInputModalityManager manager = driver.GetComponentInParent<XRInputModalityManager>(true);
        if (manager != null)
        {
            if (manager.leftHand != null && root.IsChildOf(manager.leftHand.transform))
                return manager.leftHand;

            if (manager.rightHand != null && root.IsChildOf(manager.rightHand.transform))
                return manager.rightHand;
        }

        return root.gameObject;
    }

    private static void AddGameObjectRange(List<GameObject> list, GameObject[] objects)
    {
        if (objects == null)
            return;

        for (int i = 0; i < objects.Length; i++)
            AddGameObject(list, objects[i]);
    }

    private static void AddGameObject(List<GameObject> list, GameObject target)
    {
        if (list == null || target == null || list.Contains(target))
            return;

        list.Add(target);
    }
}
