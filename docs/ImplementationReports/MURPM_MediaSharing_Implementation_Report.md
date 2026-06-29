# MURPM Media Sharing Implementation Report

## 1. Summary

Implemented a new local-first media architecture separate from the legacy phone mirroring/WebRTC path:

1. Phone browser upload -> Quest `TcpListener` HTTP server -> persistent Quest-local media library.
2. Persistent media records + `manifest.json` + thumbnails -> prefab-ready in-game gallery UI.
3. Local gallery media -> host-authoritative Netcode catalog + thumbnail-first custom-message transfer -> receiver session cache + shared display UI.

The new system does not mirror the phone screen, does not synchronize `Texture2D`, does not store bytes in Lobby data, does not base64 media, and does not use local filesystem paths as network identity. The content identity is sha256 of the optimized stored JPG.

## 2. New Scripts / Files

- `Assets/_Scripts/Media/Import/M_QuestMediaHttpServer.cs`
- `Assets/_Scripts/Media/Import/M_MediaImportController.cs`
- `Assets/_Scripts/Media/Library/M_MediaModels.cs`
- `Assets/_Scripts/Media/Library/M_MediaLibrary.cs`
- `Assets/_Scripts/Media/Library/M_MediaManifestStore.cs`
- `Assets/_Scripts/Media/Library/M_MediaThumbnailGenerator.cs`
- `Assets/_Scripts/Media/Gallery/M_MediaGalleryUI.cs`
- `Assets/_Scripts/Media/Gallery/M_MediaTile.cs`
- `Assets/_Scripts/Media/Sharing/M_LobbyMediaCatalog.cs`
- `Assets/_Scripts/Media/Sharing/M_LobbyMediaShareController.cs`
- `Assets/_Scripts/Media/Transfer/M_MediaTransferProtocol.cs`
- `Assets/_Scripts/Media/Transfer/M_MediaTransferManager.cs`
- `Assets/_Scripts/Media/Transfer/M_MediaTransferSender.cs`
- `Assets/_Scripts/Media/Transfer/M_MediaTransferReceiver.cs`
- `Assets/_Scripts/Media/Transfer/M_MediaTransferScheduler.cs`
- `Assets/_Scripts/Media/Transfer/IMediaShareTransport.cs`
- `Assets/_Scripts/Media/Transfer/LanHttpPeerMediaTransport.cs`
- `Assets/_Scripts/Media/Transfer/NgoHostChunkMediaTransport.cs`
- `Assets/_Scripts/Media/Transfer/KsuRelayMediaTransport.cs`
- `Assets/_Scripts/Media/Session/M_SessionMediaCache.cs`
- `Assets/_Scripts/Media/UI/M_MediaUploadStatusUI.cs`
- `Assets/_Scripts/Media/UI/M_SharedMediaDisplayUI.cs`
- `Assets/_Scripts/Media/UI/M_SharedMediaSurface.cs`
- `Assets/_Scripts/Media/Editor/M_MediaSharingSetupEditor.cs`
- `Docs/ImplementationReports/MURPM_MediaSharing_Implementation_Report.md`

## 3. Modified Files

- No existing runtime scripts, scenes, prefabs, or Android manifest files were modified.
- Existing `M_PairingCodeProvider` and `M_AndroidIpUtil` are reused by the new HTTP upload server.

## 4. Deprecated / Removed Scripts

No scripts were moved or removed because existing scenes reference the older media upload and phone mirror components. The new subsystem is isolated under `Assets/_Scripts/Media/` and does not use these legacy scripts as its backbone:

- `M_QuestSignalingHostTcp`
- `M_PhoneMirrorQuestWebRTC`
- `M_PhoneMirrorIosWebRTCClient`
- `M_RemoteUIInputInjector`
- `M_PhonePanelRayInputSender`
- old `M_NetworkedPhotoSync` byte broadcast path

## 5. Scene / Prefab Changes

No scenes were edited directly because the worktree already contains many unrelated scene and prefab modifications. Added an editor helper:

`MURPM/Media Sharing/Create Media Sharing Services In Scene`

It creates a scene object with:

- `NetworkObject`
- `M_PairingCodeProvider`
- `M_MediaLibrary`
- `M_MediaImportController`
- `M_QuestMediaHttpServer`
- `M_SessionMediaCache`
- `M_MediaTransferScheduler`
- `M_LobbyMediaCatalog`
- `M_MediaTransferManager`
- `M_LobbyMediaShareController`
- `NgoHostChunkMediaTransport`

## 6. Phone Upload

`M_QuestMediaHttpServer` listens on port `29100` using raw `TcpListener`. It serves:

- `GET /`: self-contained HTML upload page
- `GET /status`: JSON status
- `POST /upload?code=123456`: multipart upload
- `GET /thumb/{mediaId}?code=123456`: optional thumbnail route

The upload page uses no external assets or CDNs, includes a pairing-code field, and optionally resizes images in-browser before upload. Server request threads marshal import work back to Unity main thread before using `ImageConversion.LoadImage` or texture APIs.

## 7. Persistent Gallery Storage

Root:

`Application.persistentDataPath/MURPM/MediaLibrary/`

Layout:

- `manifest.json`
- `images/`
- `videos/`
- `thumbs/`
- `incoming/temp_uploads/`
- `session_cache/{lobbyId}/shared_from_others/`
- `session_cache/{lobbyId}/host_cache/`

`M_MediaManifestStore` writes `manifest.json` atomically via temp file and replace/move. `M_MediaLibrary` validates relative paths and prevents traversal.

## 8. Lobby Media Sharing

`M_LobbyMediaCatalog` is a scene-level `NetworkBehaviour` with a `NetworkList<SharedMediaEntry>`. It stores metadata only. Host/server is authoritative.

Host share:

- Host validates local file and sha256.
- Host adds catalog metadata.
- Host copies files into `host_cache`.
- Host sends thumbnail first, then full JPG to clients.

Client share:

- Client sends metadata request via `ShareMediaRequestServerRpc`.
- Host reserves sequence and records catalog state.
- Client transfers thumbnail and full JPG to host.
- Host verifies sha256 into `host_cache`.
- Host redistributes verified bytes to other clients.

## 9. Thumbnail-First Display

`M_MediaTransferSender` always queues thumbnail before full image. `M_SharedMediaDisplayUI` displays a cached thumbnail as soon as available and replaces it with the full image after verification.

Visible states include:

- `Receiving photo...`
- `Thumbnail ready`
- `Full image loading: X%`
- `Transfer interrupted`
- `Image failed verification`

## 10. Late Joiner Catch-Up

Late joiners receive `M_LobbyMediaCatalog` through `NetworkList` state. `M_MediaTransferManager` checks missing thumbnail/full files in the session cache and sends `MediaMsgType.Request` to the host. The host serves missing bytes from `host_cache` or, for host-owned media, from the host's local gallery fallback.

## 11. Privacy / Storage Behavior

- Imported phone media is permanent only on the importing user's Quest.
- Received lobby media goes to `session_cache/.../shared_from_others`.
- Received media is not added to the permanent gallery unless the receiver uses `Save to my gallery`.
- Clearing received session media does not delete the owner's original gallery file.
- Removing/changing shared display state does not delete permanent owner media.

## 12. Configurable Limits

Import defaults:

- input max: `20 MB`
- stored long edge: `1600`
- stored JPG quality: `82`
- optimized JPG hard cap: `5 MB`
- thumbnail long edge: `512`
- thumbnail max: `64 KB`

Transfer defaults:

- chunk payload: `4 KB`
- chunk window/frame: `16`
- active sends: `1`
- queued shares: `10`
- catalog/session history: `50`
- full image cap: `5 MB`
- thumbnail cap: `64 KB`

## 13. Known Limitations

- Video lobby sharing is not implemented. Video upload is rejected by import with a friendly message unless future local-only video support is added.
- LAN HTTP peer transfer is scaffolded but disabled by default.
- KSU relay transport is a placeholder and makes no external service calls.
- NGO transfer has no production-grade congestion control or ACK window yet. It uses reliable fragmented sequenced delivery with small chunks and verification.
- UI is prefab-ready but not wired into existing scenes automatically.

## 14. Unity Relay Warning

Unity Relay should be treated as game-state and small-metadata transport. Do not build production image/video transfer around Relay. For LAN/lab/shared Wi-Fi, prefer direct Quest-to-Quest HTTP with short-lived descriptors/tokens. For remote users, use a KSU-owned or otherwise approved relay/connectivity layer designed for session-scoped media bytes and privacy review.

## 15. Manual Test Checklist

Phone import:

- Start upload server in Quest.
- Open shown `http://192.168.x.x:29100` URL from a phone on same Wi-Fi.
- Upload JPG with correct pairing code.
- Verify optimized JPG and thumbnail are created.
- Restart app and verify gallery persists.
- Upload duplicate and verify dedupe message.
- Try bad code and verify friendly rejection.
- Try oversized/unsupported file and verify visible failure.

Host share:

- User A hosts.
- User B joins.
- A shares a gallery image.
- B sees thumbnail quickly, then full image.
- B session cache contains verified file.

Client share:

- User A hosts.
- User B joins.
- B shares gallery image.
- Host receives and verifies bytes.
- A sees thumbnail/full image.
- Host cache contains file for late joiners.

Late join:

- A hosts.
- B shares image.
- C joins after sharing.
- C receives catalog metadata.
- C requests missing thumbnail/full image from host.
- C sees image after verification.

Failure states:

- Disconnect sender mid-transfer and verify interrupted status.
- Corrupt temp file/hash mismatch and verify rejection.
- Share repeated images and verify duplicate cache behavior.

## 16. Inspector Setup Required

Use the setup menu to add scene-level services. Then wire UI prefabs/components:

- Upload panel: `M_MediaUploadStatusUI`
- Gallery panel: `M_MediaGalleryUI` + `M_MediaTile` prefab
- Shared display: `M_SharedMediaDisplayUI` + `M_SharedMediaSurface`

The media services object must be present in networked scenes as a spawned scene `NetworkObject` so `NetworkList` catalog state reaches late joiners.

## 17. Android Manifest / Player Settings

`Assets/Plugins/Android/AndroidManifest.xml` already contains:

- `android.permission.INTERNET`
- `android.permission.ACCESS_NETWORK_STATE`
- `android.permission.ACCESS_WIFI_STATE`
- `android:usesCleartextTraffic="true"`

No manifest change was required. Cleartext traffic is needed for local `http://192.168.x.x:29100` phone upload.

## 18. Verification Performed

- Unity batchmode compile could not run because another Unity instance had the project open.
- Project file currently reports `m_EditorVersion: 6000.3.8f1`, while the prompt requested Unity `2022.3.57f1`.
- The open Unity editor produced fresh `Library/ScriptAssemblies/Assembly-CSharp.dll` and `Assembly-CSharp-Editor.dll` containing the new runtime/editor types.
- `~/Library/Logs/Unity/Editor.log` recent tail showed no C# errors for the new media scripts.
- External Roslyn checks were attempted; they were blocked by Unity/Mono `System` vs `netstandard` facade conflicts, not by concrete script errors.

Full PlayMode multiplayer, Quest device, phone-browser, and Relay/LAN tests still need to be run in Unity with the scene services and UI wired.
