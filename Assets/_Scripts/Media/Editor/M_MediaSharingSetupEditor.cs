/*
 * Script Name: M_MediaSharingSetupEditor.cs
 * Description: Editor helper for adding the scene-level media sharing services safely.
 * Project Role: Avoids direct YAML scene edits in a project with many existing scene modifications.
 */

#if UNITY_EDITOR
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class M_MediaSharingSetupEditor
{
    [MenuItem("MURPM/Media Sharing/Create Media Sharing Services In Scene")]
    public static void CreateMediaSharingServices()
    {
        GameObject root = new GameObject("MURPM Media Sharing Services");
        Undo.RegisterCreatedObjectUndo(root, "Create Media Sharing Services");

        root.AddComponent<NetworkObject>();
        root.AddComponent<M_PairingCodeProvider>();
        root.AddComponent<M_MediaLibrary>();
        root.AddComponent<M_MediaImportController>();
        root.AddComponent<M_QuestMediaHttpServer>();
        root.AddComponent<M_SessionMediaCache>();
        root.AddComponent<M_MediaTransferScheduler>();
        root.AddComponent<M_LobbyMediaCatalog>();
        root.AddComponent<M_MediaTransferManager>();
        root.AddComponent<M_LobbyMediaShareController>();
        root.AddComponent<NgoHostChunkMediaTransport>();

        Selection.activeGameObject = root;
        EditorSceneManager.MarkSceneDirty(root.scene);
        Debug.Log("[M_MediaSharingSetupEditor] Created MURPM Media Sharing Services. Add this NetworkObject to the NetworkManager prefab/list if your project requires explicit network prefab registration.");
    }
}
#endif
