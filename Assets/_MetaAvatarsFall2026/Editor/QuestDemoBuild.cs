using System;
using System.IO;
using System.Linq;
using System.Reflection;
using XRMultiplayer;
using Unity.XR.CoreUtils;
using Meta.XR.MultiplayerBlocks.Shared;
using MUFB.Avatars;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class QuestDemoBuild
{
    public const string ScenePath = "Assets/_Scenes/MUFBAvatars_Fall2026.unity";
    public const string ProfilePath = "Assets/_MetaAvatarsFall2026/Quest Demo.asset";
    public const string ApkPath = "Builds/MUFBAvatars_Fall2026.apk";

    [MenuItem("Tools/MUFB/Validate Quest Demo")]
    public static void Validate()
    {
        var scene = EditorSceneManager.OpenPreviewScene(ScenePath);
        try
        {
            var roots = scene.GetRootGameObjects();
            var objects = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
            var missing = objects.Where(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) > 0).ToArray();
            Require(missing.Length == 0, "Missing scripts: " + string.Join(", ", missing.Select(t => t.name)));
            var manager = roots.SelectMany(r => r.GetComponentsInChildren<NetworkManager>(true)).Single();
            Require(manager.transform.parent == null, "NetworkManager must be a scene root.");
            Require(manager is NetworkManagerVRMultiplayer, "Use the PhonePhotoUpload network manager.");
            // The template applies its private configuration during Awake; preview scenes do not run Awake.
            var config = (NetworkConfig)typeof(NetworkManagerVRMultiplayer)
                .GetField("m_NetworkConfig", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager);
            var player = config.PlayerPrefab;
            Require(player && player.GetComponent<NetworkObject>() && player.GetComponent<M_MetaNetworkAvatar>(),
                "NetworkManager must spawn MetaNetworkedPlayer with a NetworkObject and Meta stream component.");
            Require(config.Prefabs.NetworkPrefabsLists.Any(l => l && l.PrefabList.Any(p => p.Prefab == player)),
                "MetaNetworkedPlayer must be registered in the network prefab list.");
            Require(config.ProtocolVersion == 2027, "Unexpected multiplayer protocol.");
            Require(typeof(M_MetaNetworkAvatar).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                .Count(m => m.Name.StartsWith("__rpc_handler_")) >= 1, "NGO RPC code generation is missing.");
            var rig = roots.SelectMany(r => r.GetComponentsInChildren<M_MetaAvatarRig>(true)).Single();
            var rigData = new SerializedObject(rig);
            foreach (string field in new[] { "head", "leftHand", "rightHand", "avatarRoot", "servicesPrefab", "localAvatarPrefab" })
                Require(rigData.FindProperty(field).objectReferenceValue, "Missing rig reference: " + field);
            Require(player.GetComponent<XRINetworkPlayer>() && player.GetComponent<M_MetaXriPlayerBridge>(),
                "Meta player must retain the template's player list, voice and media integration.");
            Require(!player.GetComponentsInChildren<MonoBehaviour>(true).Any(c => c is XRAvatarIK || c is XRAvatarVisuals || c is XRHandPoseReplicator || c is JointBasedHand || c.GetType().Name == "NetworkedPlatformSpecificVisuals"), "Template avatar animation/visual scripts must be removed.");
            Require(!player.GetComponentsInChildren<Renderer>(true).Any(), "Template avatar meshes must be removed.");
            var sessions = roots.SelectMany(r => r.GetComponentsInChildren<SessionManager>(true)).Single();
            Require(!sessions.quickJoinCreatesSession, "Join must not silently create another room.");
            Require(roots.SelectMany(r => r.GetComponentsInChildren<XROrigin>(true)).Count() == 1, "Exactly one XR origin is required.");
            var metaTracking = roots.SelectMany(r => r.GetComponentsInChildren<OVRManager>(true)).Single();
            Require(metaTracking.trackingOriginType == OVRManager.TrackingOrigin.Stage, "Meta tracking must use OpenXR's stable stage origin.");
            Require(roots.SelectMany(r => r.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Inputs.InputActionManager>(true)).Count() == 1, "Only the XR origin should own shared input action enable/disable.");
            Require(roots.SelectMany(r => r.GetComponentsInChildren<LobbyUI>(true)).Any(), "PhonePhotoUpload lobby menus are missing.");
            Require(!roots.SelectMany(r => r.GetComponentsInChildren<M_MenuManager>(true)).Any(), "Legacy menu must be removed.");
            ValidatePhoneMap(scene);
            var profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(ProfilePath);
            Require(profile && profile.overrideGlobalScenes, "Quest profile must override the phone scene list.");
            Require(profile.scenes.Count(s => s.enabled) == 1 && profile.scenes.Any(s => s.enabled && s.path == ScenePath),
                "Quest profile must launch the Meta Avatars scene.");
            foreach (string prefabPath in AssetDatabase.GetDependencies(ScenePath, true).Where(p => p.StartsWith("Assets/_MetaAvatarsFall2026/") && p.EndsWith(".prefab")))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Require(prefab.GetComponentsInChildren<Transform>(true).All(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) == 0),
                    "Missing script in " + prefabPath);
            }
            Debug.Log("[QuestDemo] PASS: imported scene, Meta network player registration, generated NGO RPC handlers, rig references, PhonePhotoUpload menus/session manager, and Quest profile.");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    static void ValidatePhoneMap(UnityEngine.SceneManagement.Scene scene)
    {
        var source = EditorSceneManager.OpenPreviewScene("Assets/_Scenes/PhonePhotoUpload.unity");
        try
        {
            var sourceRoots = source.GetRootGameObjects();
            var targetRoots = scene.GetRootGameObjects();
            foreach (string rootName in new[] { "Environment", "Gameplay" })
            {
                var expected = sourceRoots.Single(r => r.name == rootName).GetComponentsInChildren<Transform>(true);
                var actual = targetRoots.Single(r => r.name == rootName).GetComponentsInChildren<Transform>(true);
                Require(expected.Length == actual.Length, rootName + " hierarchy differs from PhonePhotoUpload.");
                for (int i = 0; i < expected.Length; i++)
                    Require(expected[i].name == actual[i].name &&
                        Vector3.Distance(expected[i].position, actual[i].position) < 0.001f &&
                        Quaternion.Angle(expected[i].rotation, actual[i].rotation) < 0.01f &&
                        Vector3.Distance(expected[i].lossyScale, actual[i].lossyScale) < 0.001f &&
                        expected[i].gameObject.activeSelf == actual[i].gameObject.activeSelf,
                        "Phone map transform/state differs: " + expected[i].name);
            }
            var sourceOrigin = sourceRoots.SelectMany(r => r.GetComponentsInChildren<XROrigin>(true)).Single();
            var targetOrigin = targetRoots.SelectMany(r => r.GetComponentsInChildren<XROrigin>(true)).Single();
            Require(Vector3.Distance(sourceOrigin.transform.position, targetOrigin.transform.position) < 0.001f &&
                Quaternion.Angle(sourceOrigin.transform.rotation, targetOrigin.transform.rotation) < 0.01f,
                "XR origin spawn must match PhonePhotoUpload.");
            var expectedReset = new SerializedObject(sourceOrigin.GetComponent<CharacterResetter>());
            var actualReset = new SerializedObject(targetOrigin.GetComponent<CharacterResetter>());
            foreach (string field in new[] { "offlinePosition", "onlinePosition" })
                Require(expectedReset.FindProperty(field).vector3Value == actualReset.FindProperty(field).vector3Value,
                    "Player reset/spawn differs from PhonePhotoUpload: " + field);
            Require(!targetRoots.Any(r => r.name == "Environment Setup" || r.name == "[BuildingBlock] Cube"),
                "Old conference environment roots remain.");
            Debug.Log($"[QuestDemo] Phone map/spawns match: origin={targetOrigin.transform.position}, offline={actualReset.FindProperty("offlinePosition").vector3Value}, online={actualReset.FindProperty("onlinePosition").vector3Value}.");
        }
        finally { EditorSceneManager.ClosePreviewScene(source); }
    }

    [MenuItem("Tools/MUFB/Use Quest Demo Profile")]
    public static void SelectProfile()
    {
        var profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(ProfilePath);
        profile.overrideGlobalScenes = true;
        profile.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssetIfDirty(profile);
        BuildProfile.SetActiveBuildProfile(profile);
        Debug.Log("[QuestDemo] Quest Demo profile selected. Only MUFBAvatars_Fall2026 is included.");
    }

    [MenuItem("Tools/MUFB/Build Quest Demo APK")]
    public static void Build()
    {
        Validate();
        var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (active.path == ScenePath && active.isDirty && !EditorSceneManager.SaveScene(active))
            throw new BuildFailedException("Could not save the Quest demo scene before building.");
        Directory.CreateDirectory("Builds");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerWithProfileOptions
        {
            buildProfile = AssetDatabase.LoadAssetAtPath<BuildProfile>(ProfilePath),
            locationPathName = ApkPath,
            options = BuildOptions.Development
        });
        string summary = $"{DateTime.UtcNow:O}\nResult: {report.summary.result}\nErrors: {report.summary.totalErrors}\nWarnings: {report.summary.totalWarnings}\nSize: {report.summary.totalSize}\nDuration: {report.summary.totalTime}\nAPK: {Path.GetFullPath(ApkPath)}\n";
        File.WriteAllText("Builds/QuestDemoBuildReport.txt", summary);
        if (report.summary.result != BuildResult.Succeeded) throw new BuildFailedException(summary);
        Debug.Log("[QuestDemo] APK BUILD SUCCEEDED\n" + summary);
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new BuildFailedException("[QuestDemo] " + message);
    }
}
