using System.Collections;
using MUFB.Avatars;
using Oculus.Avatar2;
using UnityEngine;
using XRMultiplayer;

// Opt-in development-build checks. A normal launch never creates a test avatar or joins a room.
public sealed class M_MetaAvatarDiagnostics : MonoBehaviour
{
    [SerializeField] private M_MetaAvatarEntity remotePrefab;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    IEnumerator Start()
    {
        string mode = "";
#if UNITY_EDITOR
        const string request = "/tmp/mufb-editor-test-mode";
        if (System.IO.File.Exists(request))
        {
            mode = System.IO.File.ReadAllText(request).Trim();
            System.IO.File.Delete(request);
        }
#elif UNITY_ANDROID
        using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
        using (var intent = activity.Call<AndroidJavaObject>("getIntent"))
            mode = intent.Call<string>("getStringExtra", "mufb_test") ?? "";
#endif
        if (string.IsNullOrEmpty(mode)) yield break;
        Debug.Log($"[MetaAvatarTest] Starting {mode}.");
        if (mode == "host" || mode == "join" || mode == "join-echo" || mode == "host-echo")
        {
#if UNITY_EDITOR
            M_MetaNetworkAvatar.SetEditorStreamEcho(mode.EndsWith("-echo"));
#endif
            float authDeadline = Time.realtimeSinceStartup + 30f;
            while (XRINetworkGameManager.CurrentConnectionState.Value != XRINetworkGameManager.ConnectionState.Authenticated &&
                   Time.realtimeSinceStartup < authDeadline) yield return null;
            if (mode == "host" || mode == "host-echo") XRINetworkGameManager.Instance.CreateNewLobby("MUFB Avatar Test", false, 8);
            else XRINetworkGameManager.Instance.QuickJoinLobby();
            yield break;
        }
        if (mode != "loopback" || !remotePrefab) yield break;
        float deadline = Time.realtimeSinceStartup + 60f;
        while ((!M_MetaAvatarRig.Instance || !M_MetaAvatarRig.Instance.LocalAvatar ||
                !M_MetaAvatarRig.Instance.LocalAvatar.ReadyToStream) && Time.realtimeSinceStartup < deadline)
            yield return null;
        var rig = M_MetaAvatarRig.Instance;
        if (!rig || !rig.LocalAvatar || !rig.LocalAvatar.ReadyToStream)
        {
            Debug.LogError("[MetaAvatarTest] Local avatar did not initialize.");
            yield break;
        }
        var remote = Instantiate(remotePrefab);
        remote.name = "Development Stream Loopback";
        remote.Initialize(false);
        byte[] buffer = System.Array.Empty<byte>();
        int applied = 0;
        var interval = new WaitForSecondsRealtime(1f / 15f);
        while (remote && rig)
        {
            // A translated copy of the real headset pose, outside the user's body.
            remote.transform.SetPositionAndRotation(rig.Origin.position + rig.Origin.forward * 2f, rig.Origin.rotation);
            uint bytes = rig.LocalAvatar.RecordStreamData_AutoBuffer(OvrAvatarEntity.StreamLOD.Medium, ref buffer);
            if (bytes > 0 && remote.ReadyToStream)
            {
                var packet = new byte[bytes];
                System.Buffer.BlockCopy(buffer, 0, packet, 0, packet.Length);
                if (remote.ApplyStreamData(packet))
                {
                    applied++;
                    if (applied == 1 || applied % 150 == 0)
                        Debug.Log($"[MetaAvatarTest] Loopback applied={applied} bytes={bytes} joints={remote.SkeletonJointCount} renderers={remote.GetComponentsInChildren<Renderer>().Length}");
                }
            }
            yield return interval;
        }
    }
#if UNITY_EDITOR
    void OnDestroy() => M_MetaNetworkAvatar.SetEditorStreamEcho(false);
#endif
#endif
}
