using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.XR.CoreUtils;
using MUFB.Avatars;
using XRMultiplayer;
using UnityEngine.UI;
using UnityEditor.Events;
using TMPro;

public static class QuestDemoSceneMigration
{
    [InitializeOnLoadMethod]
    static void InspectRequestedScene()
    {
        EditorApplication.update -= RunRequestedCommand;
        EditorApplication.update += RunRequestedCommand;
    }

    static void RunRequestedCommand()
    {
        const string request = "/tmp/mufb-unity-command.txt";
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(request)) return;
        string command = File.ReadAllText(request).Trim();
        File.Delete(request);
        try
        {
            switch (command)
            {
                case "inspect": Inspect(); break;
                case "refresh": AssetDatabase.Refresh(); break;
                case "import": Import(); break;
                case "replace-map": ReplaceMap(); break;
                case "network-report": NetworkReport(); break;
                case "save-network-ids": SaveNetworkIds(); break;
                case "finalize": FinalizeImport(); break;
                case "validate": QuestDemoBuild.Validate(); break;
                case "build": QuestDemoBuild.Build(); break;
                case "play-join":
                case "play-join-echo":
                case "play-host-echo":
                    File.WriteAllText("/tmp/mufb-editor-test-mode", command.Substring("play-".Length));
                    EditorApplication.isPlaying = true;
                    break;
                case "stop": EditorApplication.isPlaying = false; break;
                default: throw new System.InvalidOperationException("Unknown MUFB editor command.");
            }
            File.WriteAllText("/tmp/mufb-unity-result.txt", command + ": succeeded");
        }
        catch (System.Exception e)
        {
            File.WriteAllText("/tmp/mufb-unity-result.txt", command + ": " + e);
            Debug.LogException(e);
        }
    }
    [MenuItem("Tools/MUFB/Inspect Phone Rig")]
    public static void Inspect()
    {
        var scene = EditorSceneManager.OpenPreviewScene("Assets/_Scenes/PhonePhotoUpload.unity");
        try
        {
            var report = new StringBuilder();
            foreach (var root in scene.GetRootGameObjects()) Describe(root.transform, "", report);
            File.WriteAllText("/tmp/mufb-phone-rig.txt", report.ToString());
            Debug.Log("[QuestDemo] Phone rig inventory written to /tmp/mufb-phone-rig.txt");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    [MenuItem("Tools/MUFB/Import Phone Rig and Menus")]
    public static void Import() => Import(false);

    [MenuItem("Tools/MUFB/Replace Map with PhonePhotoUpload")]
    public static void ReplaceMap() => Import(true);

    static void Import(bool replaceMap)
    {
        if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Stop Play mode first.");
        var target = EditorSceneManager.OpenScene(QuestDemoBuild.ScenePath, OpenSceneMode.Single);
        File.Copy(QuestDemoBuild.ScenePath, replaceMap ? "/tmp/MUFBAvatars_Fall2026.before-phone-map.unity" : "/tmp/MUFBAvatars_Fall2026.before-phone-rig.unity", true);
        var oldRoots = target.GetRootGameObjects();
        var oldRig = oldRoots.SelectMany(r => r.GetComponentsInChildren<M_MetaAvatarRig>(true)).Single();
        var oldOvr = oldRoots.SelectMany(r => r.GetComponentsInChildren<OVRManager>(true)).Single();
        Vector3 spawnPosition = oldRig.transform.position;
        Quaternion spawnRotation = oldRig.transform.rotation;
        var originalTarget = target;
        // Saving the complete source scene as a copy preserves lighting, occlusion and all
        // cross-root gameplay references as well as the source's exact transforms.
        var source = replaceMap
            ? EditorSceneManager.OpenScene("Assets/_Scenes/PhonePhotoUpload.unity", OpenSceneMode.Additive)
            : EditorSceneManager.OpenPreviewScene("Assets/_Scenes/PhonePhotoUpload.unity");
        GameObject imported;
        try
        {
            var sourceRoots = source.GetRootGameObjects();
            var group = new GameObject("PhonePhotoUpload XR and Menus");
            SceneManager.MoveGameObjectToScene(group, source);
            foreach (var root in sourceRoots.Where(r => replaceMap || (r.name != "Environment" && r.name != "Gameplay")))
            {
                if (PrefabUtility.IsPartOfPrefabInstance(root))
                    PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                root.transform.SetParent(group.transform, true);
            }
            if (replaceMap)
            {
                imported = group;
                target = source;
                var sourceOrigin = group.GetComponentInChildren<XROrigin>(true);
                spawnPosition = sourceOrigin.transform.position;
                spawnRotation = sourceOrigin.transform.rotation;
                SceneManager.SetActiveScene(source);
            }
            else
            {
                // Clone once so cross-root menu, input, voice and phone-pairing references remap together.
                imported = Object.Instantiate(group);
                imported.name = group.name;
                SceneManager.MoveGameObjectToScene(imported, target);
            }
        }
        finally { if (!replaceMap) EditorSceneManager.ClosePreviewScene(source); }

        foreach (var t in imported.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
        foreach (var menu in imported.GetComponentsInChildren<OfflineMenu>(true)) menu.skipAppearanceOnStart = true;
        foreach (var appearance in imported.GetComponentsInChildren<PlayerAppearanceMenu>(true))
        {
            appearance.gameObject.SetActive(false);
            foreach (var camera in appearance.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
        }

        var origin = imported.GetComponentsInChildren<XROrigin>(true).Single();
        origin.transform.SetPositionAndRotation(spawnPosition, spawnRotation);
        origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
        origin.Camera.nearClipPlane = 0.02f;
        foreach (var input in imported.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Inputs.InputActionManager>(true))
            if (input.gameObject != origin.gameObject) Object.DestroyImmediate(input);
        var ovr = imported.GetComponentsInChildren<OVRManager>(true).Single();
        EditorUtility.CopySerialized(oldOvr, ovr);
        ovr.trackingOriginType = OVRManager.TrackingOrigin.Stage;

        // The template's offline preview owns a separate mesh and microphone. Meta replaces both.
        foreach (var offline in imported.GetComponentsInChildren<OfflinePlayerAvatar>(true))
            Object.DestroyImmediate(offline.gameObject);

        var rig = origin.gameObject.AddComponent<M_MetaAvatarRig>();
        EditorUtility.CopySerialized(oldRig, rig);
        var data = new SerializedObject(rig);
        data.FindProperty("cameraRig").objectReferenceValue = null;
        data.FindProperty("avatarRoot").objectReferenceValue = origin.transform;
        data.FindProperty("head").objectReferenceValue = origin.Camera.transform;
        var transforms = origin.GetComponentsInChildren<Transform>(true);
        var left = transforms.First(t => t.name == "Left Controller");
        var right = transforms.First(t => t.name == "Right Controller");
        data.FindProperty("leftHand").objectReferenceValue = left;
        data.FindProperty("rightHand").objectReferenceValue = right;
        var hands = transforms.Where(t => t.name == "Left Hand" || t.name == "Right Hand")
            .SelectMany(t => t.GetComponentsInChildren<Renderer>(true)).Where(r => !(r is LineRenderer)).Distinct().ToArray();
        var handRefs = data.FindProperty("interactionHandRenderers");
        handRefs.arraySize = hands.Length;
        for (int i = 0; i < hands.Length; i++) handRefs.GetArrayElementAtIndex(i).objectReferenceValue = hands[i];
        data.ApplyModifiedPropertiesWithoutUndo();
        var diagnostics = origin.gameObject.AddComponent<M_MetaAvatarDiagnostics>();
        var diagnosticsData = new SerializedObject(diagnostics);
        diagnosticsData.FindProperty("remotePrefab").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_MetaAvatarsFall2026/Prefabs/RemoteMetaAvatar.prefab").GetComponent<M_MetaAvatarEntity>();
        diagnosticsData.ApplyModifiedPropertiesWithoutUndo();

        var manager = imported.GetComponentInChildren<NetworkManagerVRMultiplayer>(true);
        manager.transform.SetParent(null, true); // NGO requires the manager to be a scene root.
        var configData = new SerializedObject(manager);
        var config = configData.FindProperty("m_NetworkConfig");
        var sourcePlayer = (GameObject)config.FindPropertyRelative("PlayerPrefab").objectReferenceValue;
        var player = BuildNetworkPlayer(sourcePlayer);
        var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>("Assets/_MetaAvatarsFall2026/MetaNetworkPrefabs.asset");
        var listData = new SerializedObject(list);
        var entries = listData.FindProperty("List");
        entries.arraySize = 1;
        entries.GetArrayElementAtIndex(0).FindPropertyRelative("Prefab").objectReferenceValue = player;
        listData.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssetIfDirty(list);
        config.FindPropertyRelative("PlayerPrefab").objectReferenceValue = player;
        config.FindPropertyRelative("ProtocolVersion").intValue = 2027;
        config.FindPropertyRelative("NetworkTopology").enumValueIndex = 1;
        config.FindPropertyRelative("ForceSamePrefabs").boolValue = true;
        var lists = config.FindPropertyRelative("Prefabs.NetworkPrefabsLists");
        lists.InsertArrayElementAtIndex(lists.arraySize);
        lists.GetArrayElementAtIndex(lists.arraySize - 1).objectReferenceValue = list;
        configData.ApplyModifiedPropertiesWithoutUndo();
        var transportData = new SerializedObject(manager.GetComponent<UnityTransport>());
        transportData.FindProperty("m_MaxPayloadSize").intValue = 17408;
        transportData.ApplyModifiedPropertiesWithoutUndo();
        imported.GetComponentInChildren<SessionManager>(true).quickJoinCreatesSession = false;
        foreach (var button in imported.GetComponentsInChildren<Button>(true))
        {
            var buttonData = new SerializedObject(button);
            var calls = buttonData.FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
            {
                if (!(button.onClick.GetPersistentTarget(i) is LobbyUI lobby)) continue;
                string method = button.onClick.GetPersistentMethodName(i);
                if (method == "ToggleConnectionSubPanel" &&
                    calls.GetArrayElementAtIndex(i).FindPropertyRelative("m_Arguments.m_IntArgument").intValue == 1)
                {
                    UnityEventTools.RemovePersistentListener(button.onClick, i);
                    UnityEventTools.AddPersistentListener(button.onClick, lobby.HostPublicRoom);
                    var label = button.GetComponentInChildren<TMP_Text>(true);
                    if (label) label.text = "Host";
                    break;
                }
                if (method == "QuickJoinLobby")
                {
                    var label = button.GetComponentInChildren<TMP_Text>(true);
                    if (label) label.text = "Join";
                }
            }
        }
        foreach (var passthrough in imported.GetComponentsInChildren<M_PassthroughModeController>(true))
        {
            passthrough.ovrManager = ovr;
            passthrough.xrCamera = origin.Camera;
            passthrough.enableOnStart = false;
        }

        // Remove the old rig, managers and menus after all replacement references have been assigned.
        if (!replaceMap)
            foreach (var root in oldRoots.Where(r => r.name != "Environment Setup" && r.name != "UnityMainThreadRunner" && r.name != "[BuildingBlock] Cube"))
                Object.DestroyImmediate(root);
        foreach (var t in target.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray())
            if (t && t.name == "MenuCanvas") Object.DestroyImmediate(t.gameObject);

        if (replaceMap)
        {
            // Restore the original root hierarchy and source world positions.
            foreach (var child in imported.transform.Cast<Transform>().ToArray()) child.SetParent(null, true);
            Object.DestroyImmediate(imported);
            // Unity cannot save over a scene path while that scene is open elsewhere.
            EditorSceneManager.CloseScene(originalTarget, true);
            if (!EditorSceneManager.SaveScene(target, QuestDemoBuild.ScenePath, true))
                throw new System.IO.IOException("Could not save the PhonePhotoUpload scene copy.");
            // Opening Single closes the modified source without writing it back.
            target = EditorSceneManager.OpenScene(QuestDemoBuild.ScenePath, OpenSceneMode.Single);
            SaveNetworkIds();
        }
        else
        {
            EditorSceneManager.MarkSceneDirty(target);
            EditorSceneManager.SaveScene(target);
        }
        var report = new StringBuilder();
        foreach (var root in target.GetRootGameObjects()) Describe(root.transform, "", report);
        File.WriteAllText("/tmp/mufb-migrated-rig.txt", report.ToString());
        Debug.Log("[QuestDemo] Imported PhonePhotoUpload " + (replaceMap ? "complete map, source spawn positions, " : "") + "rig, networking and menus with Meta avatars.");
    }

    static void NetworkReport()
    {
        var report = new StringBuilder();
        foreach (var obj in Object.FindObjectsByType<NetworkObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!obj.gameObject.scene.IsValid()) continue;
            var data = new SerializedObject(obj);
            report.AppendLine($"{obj.name}\tactive={obj.gameObject.activeInHierarchy}\tspawned={obj.IsSpawned}\thash={data.FindProperty("GlobalObjectIdHash").longValue}\t{GlobalObjectId.GetGlobalObjectIdSlow(obj)}");
        }
        File.WriteAllText("/tmp/mufb-network-objects.txt", report.ToString());
    }

    static void SaveNetworkIds()
    {
        if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Stop Play mode first.");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != QuestDemoBuild.ScenePath) throw new System.InvalidOperationException("Open the Meta clone first.");
        var validate = typeof(NetworkObject).GetMethod("OnValidate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        foreach (var obj in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<NetworkObject>(true)))
        {
            validate.Invoke(obj, null);
            // OnValidate may already have fixed the in-memory hash when opening the copy.
            // Persist it even when that invocation itself reports no change.
            if (PrefabUtility.IsPartOfPrefabInstance(obj)) PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
            EditorUtility.SetDirty(obj);
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        NetworkReport();
    }

    static void FinalizeImport()
    {
        var scene = SceneManager.GetActiveScene();
        var components = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
        var origin = components.Select(t => t.GetComponent<XROrigin>()).Single(c => c);
        var ovr = components.Select(t => t.GetComponent<OVRManager>()).Single(c => c);
        ovr.trackingOriginType = OVRManager.TrackingOrigin.Stage;
        foreach (var t in components)
        {
            var input = t.GetComponent<UnityEngine.XR.Interaction.Toolkit.Inputs.InputActionManager>();
            if (input && input.gameObject != origin.gameObject) Object.DestroyImmediate(input);
        }
        var manager = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<NetworkManager>(true)).Single();
        manager.transform.SetParent(null, true);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        const string path = "Assets/_MetaAvatarsFall2026/Prefabs/MetaXriNetworkPlayer.prefab";
        var prefab = PrefabUtility.LoadPrefabContents(path);
        try
        {
            foreach (var component in prefab.GetComponentsInChildren<MonoBehaviour>(true))
                if (component && (component is JointBasedHand || component.GetType().Name == "NetworkedPlatformSpecificVisuals"))
                    Object.DestroyImmediate(component);
            PrefabUtility.SaveAsPrefabAsset(prefab, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        QuestDemoBuild.Validate();
    }

    static GameObject BuildNetworkPlayer(GameObject source)
    {
        const string path = "Assets/_MetaAvatarsFall2026/Prefabs/MetaXriNetworkPlayer.prefab";
        var player = (GameObject)PrefabUtility.InstantiatePrefab(source);
        try
        {
            PrefabUtility.UnpackPrefabInstance(player, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            player.name = "MetaXriNetworkPlayer";
            foreach (var component in player.GetComponentsInChildren<MonoBehaviour>(true))
                if (component && (component is XRAvatarVisuals || component is XRAvatarIK || component is XRHandPoseReplicator || component is JointBasedHand || component.GetType().Name == "NetworkedPlatformSpecificVisuals"))
                    Object.DestroyImmediate(component);
            foreach (var renderer in player.GetComponentsInChildren<Renderer>(true)) Object.DestroyImmediate(renderer);
            foreach (var mesh in player.GetComponentsInChildren<MeshFilter>(true)) Object.DestroyImmediate(mesh);
            foreach (var animator in player.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(animator);
            var stream = player.AddComponent<M_MetaNetworkAvatar>();
            EditorUtility.CopySerialized(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_MetaAvatarsFall2026/Prefabs/MetaNetworkedPlayer.prefab").GetComponent<M_MetaNetworkAvatar>(), stream);
            player.AddComponent<M_MetaXriPlayerBridge>();
            return PrefabUtility.SaveAsPrefabAsset(player, path);
        }
        finally { Object.DestroyImmediate(player); }
    }

    static void Describe(Transform node, string indent, StringBuilder report)
    {
        report.AppendLine(indent + node.name + " [" + string.Join(", ", node.GetComponents<Component>().Select(c => c ? c.GetType().Name : "MISSING")) + "]");
        foreach (Transform child in node) Describe(child, indent + "  ", report);
    }
}
